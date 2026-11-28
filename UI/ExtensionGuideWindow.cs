using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cueway.Interop;
using Cueway.Localization;
using Cueway.Media;
using Cueway.Startup;

namespace Cueway.UI;

internal enum ExtensionGuideMode
{
    Chromium = 0,
    Firefox = 1
}

internal sealed unsafe class ExtensionGuideWindow : IDisposable
{
    private const string ClassName = "Cueway_GuideDialog";
    private const nuint TIMER_ANIM = 9501;
    private const uint TIMER_INTERVAL_MS = 33;
    private const uint WM_APP_BRIDGE_REFRESH = Win32.WM_APP + 215;

    private const int SceneDurationMs = 3200;
    private const int TotalLoopMs = SceneDurationMs * 3;

    private sealed record HitRegion(Win32.RECT Bounds, string Id, Action OnClick);

    private static ExtensionGuideWindow? _instance;

    private readonly nint _ownerHwnd;
    private readonly BrowserBridgeServer _bridgeServer;
    private readonly List<HitRegion> _hitRegions = [];
    private readonly Dictionary<(int PtSize, int Weight, string Face, uint Dpi), nint> _fontCache = new();

    private nint _hwnd;
    private nint _cursorArrow;
    private nint _cursorHand;
    private nint _backDc;
    private nint _backBmp;
    private nint _backOldBmp;
    private int _backW;
    private int _backH;
    private uint _dpi = 96;
    private int _clientW = 640;
    private int _clientH = 540;
    private ExtensionGuideMode _mode = ExtensionGuideMode.Chromium;

    private bool _playing = true;
    private long _animMs;
    private long _lastTick;
    private bool _scrubbing;
    private Win32.RECT _timelineTrackRect;

    private string? _hoverId;
    private bool _trackingMouse;
    private bool _hoverIsHand;
    private string? _bannerText;
    private bool _bannerIsError;
    private long _bannerUntil;
    private bool _disposed;

    public ExtensionGuideWindow(nint ownerHwnd, BrowserBridgeServer bridgeServer)
    {
        _ownerHwnd = ownerHwnd;
        _bridgeServer = bridgeServer;
        _instance = this;
        _lastTick = Environment.TickCount64;

        CreateGuideWindow();
        _bridgeServer.SessionsChanged += OnBridgeChanged;
    }

    private void CreateGuideWindow()
    {
        _cursorArrow = Win32.LoadCursorW(0, Win32.IDC_ARROW);
        _cursorHand = Win32.LoadCursorW(0, Win32.IDC_HAND);
        nint hInst = Win32.GetModuleHandleW(null);
        fixed (char* cls = ClassName)
        {
            var wc = new Win32.WNDCLASSEXW
            {
                cbSize = (uint)sizeof(Win32.WNDCLASSEXW),
                style = Win32.CS_HREDRAW | Win32.CS_VREDRAW,
                lpfnWndProc = &WndProc,
                hInstance = hInst,
                hCursor = _cursorArrow,
                lpszClassName = cls
            };
            Win32.RegisterClassExW(&wc);
        }

        uint ownerDpi = _ownerHwnd != 0 ? Win32.GetDpiForWindow(_ownerHwnd) : 96;
        if (ownerDpi >= 96) _dpi = ownerDpi;

        int initW = Theme.Scale(656, _dpi);
        int initH = Theme.Scale(578, _dpi);

        _hwnd = Win32.CreateWindowExW(
            Win32.WS_EX_TOOLWINDOW,
            ClassName,
            Strings.GuideTitleChromium,
            Win32.WS_OVERLAPPED | Win32.WS_CAPTION | Win32.WS_SYSMENU,
            0, 0, initW, initH,
            _ownerHwnd, 0, hInst, 0);

        if (_hwnd != 0)
        {
            uint winDpi = Win32.GetDpiForWindow(_hwnd);
            if (winDpi >= 96) _dpi = winDpi;
            ApplyDwmTheme();
        }
    }

    private void ApplyDwmTheme()
    {
        if (_hwnd == 0) return;
        int dark = Theme.IsLight ? 0 : 1;
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(int));
        int corner = Win32.DWMWCP_ROUND;
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(int));
        uint cap = Theme.ToColorRef(Theme.BgHeader);
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_CAPTION_COLOR, &cap, sizeof(uint));
        uint text = Theme.ToColorRef(Theme.TextPrimary);
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_TEXT_COLOR, &text, sizeof(uint));
        uint bdr = Theme.ToColorRef(Theme.BorderStrong);
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_BORDER_COLOR, &bdr, sizeof(uint));
    }

    public void RefreshLocalizationAndTheme()
    {
        if (_disposed || _hwnd == 0) return;
        ClearFontCache();
        ApplyDwmTheme();
        Win32.SetWindowTextW(_hwnd, _mode == ExtensionGuideMode.Firefox
            ? Strings.GuideTitleFirefox
            : Strings.GuideTitleChromium);
        RequestRepaint();
    }

    public void ShowFor(ExtensionGuideMode mode)
    {
        if (_disposed || _hwnd == 0) return;

        _mode = mode;
        _animMs = 0;
        _playing = true;
        _lastTick = Environment.TickCount64;
        _bannerText = null;
        _hoverId = null;

        ApplyDwmTheme();
        Win32.SetWindowTextW(_hwnd, mode == ExtensionGuideMode.Firefox
            ? Strings.GuideTitleFirefox
            : Strings.GuideTitleChromium);

        CenterOverOwner();
        Win32.ShowWindow(_hwnd, Win32.SW_SHOW);
        Win32.SetForegroundWindow(_hwnd);
        Win32.SetTimer(_hwnd, TIMER_ANIM, TIMER_INTERVAL_MS, 0);
        Win32.InvalidateRect(_hwnd, null, false);
    }

    public void HideDialog()
    {
        if (_hwnd == 0) return;
        _scrubbing = false;
        _hoverId = null;
        Win32.KillTimer(_hwnd, TIMER_ANIM);
        Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
        ReleaseBackBuffer();
        ClearFontCache();
        _hitRegions.Clear();
    }

    private void CenterOverOwner()
    {
        if (_hwnd == 0) return;

        int winW = Theme.Scale(656, _dpi);
        int winH = Theme.Scale(578, _dpi);

        var mi = new Win32.MONITORINFO { cbSize = (uint)sizeof(Win32.MONITORINFO) };
        Win32.RECT anchorRc;
        if (_ownerHwnd != 0 && Win32.GetWindowRect(_ownerHwnd, out anchorRc))
        {
            nint hMon = Win32.MonitorFromWindow(_ownerHwnd, Win32.MONITOR_DEFAULTTONEAREST);
            Win32.GetMonitorInfoW(hMon, &mi);
        }
        else
        {
            Win32.GetCursorPos(out var pt);
            nint hMon = Win32.MonitorFromPoint(pt, Win32.MONITOR_DEFAULTTONEAREST);
            Win32.GetMonitorInfoW(hMon, &mi);
            anchorRc = mi.rcWork;
        }

        int x = anchorRc.Left + (anchorRc.Width - winW) / 2;
        int y = anchorRc.Top + (anchorRc.Height - winH) / 2;
        x = Math.Clamp(x, mi.rcWork.Left, Math.Max(mi.rcWork.Left, mi.rcWork.Right - winW));
        y = Math.Clamp(y, mi.rcWork.Top, Math.Max(mi.rcWork.Top, mi.rcWork.Bottom - winH));

        Win32.SetWindowPos(_hwnd, 0, x, y, winW, winH, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    private void OnBridgeChanged()
    {
        if (!_disposed && _hwnd != 0 && Win32.IsWindowVisible(_hwnd))
            Win32.PostMessageW(_hwnd, WM_APP_BRIDGE_REFRESH, 0, 0);
    }

    private int S(int px) => Theme.Scale(px, _dpi);
    private float SF(float px) => Theme.ScaleF(px, _dpi);

    private void ShowBanner(string text, bool isError = false, int ms = 5000)
    {
        _bannerText = text;
        _bannerIsError = isError;
        _bannerUntil = Environment.TickCount64 + ms;
        RequestRepaint();
    }

    private void RequestRepaint()
    {
        if (_hwnd != 0 && Win32.IsWindowVisible(_hwnd))
            Win32.InvalidateRect(_hwnd, null, false);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        var inst = _instance;
        try
        {
            if (inst != null && hwnd == inst._hwnd)
            {
                switch (msg)
                {
                    case Win32.WM_CLOSE:
                        inst.HideDialog();
                        return 0;

                    case Win32.WM_ERASEBKGND:
                        return 1;

                    case Win32.WM_SIZE:
                        inst._clientW = (int)(lParam & 0xFFFF);
                        inst._clientH = (int)((lParam >> 16) & 0xFFFF);
                        return 0;

                    case Win32.WM_TIMER:
                        if (wParam == TIMER_ANIM)
                        {
                            inst.OnAnimTimer();
                            return 0;
                        }
                        break;

                    case WM_APP_BRIDGE_REFRESH:
                        inst.RequestRepaint();
                        return 0;

                    case Win32.WM_DPICHANGED:
                        uint newDpi = (uint)(wParam & 0xFFFF);
                        if (newDpi >= 96) inst._dpi = newDpi;
                        inst.ClearFontCache();
                        var suggested = *(Win32.RECT*)lParam;
                        Win32.SetWindowPos(hwnd, 0, suggested.Left, suggested.Top, suggested.Width, suggested.Height,
                            Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
                        inst.RequestRepaint();
                        return 0;

                    case Win32.WM_SETCURSOR:
                        if ((lParam & 0xFFFF) == Win32.HTCLIENT)
                        {
                            Win32.SetCursor((inst._hoverIsHand || inst._scrubbing) && inst._cursorHand != 0 ? inst._cursorHand : inst._cursorArrow);
                            return 1;
                        }
                        break;

                    case Win32.WM_PAINT:
                        inst.PaintWindow();
                        return 0;

                    case Win32.WM_MOUSEMOVE:
                        inst.OnMouseMove(lParam);
                        return 0;

                    case Win32.WM_MOUSELEAVE:
                        inst._trackingMouse = false;
                        inst._hoverIsHand = false;
                        if (inst._hoverId != null)
                        {
                            inst._hoverId = null;
                            inst.RequestRepaint();
                        }
                        return 0;

                    case Win32.WM_LBUTTONDOWN:
                        inst.OnMouseDown(lParam);
                        return 0;

                    case Win32.WM_CAPTURECHANGED:
                        inst._scrubbing = false;
                        return 0;

                    case Win32.WM_LBUTTONUP:
                        if (inst._scrubbing)
                        {
                            inst._scrubbing = false;
                            Win32.ReleaseCapture();
                        }
                        return 0;

                    case Win32.WM_KEYDOWN:
                        if (wParam == Win32.VK_ESCAPE)
                        {
                            inst.HideDialog();
                            return 0;
                        }
                        break;
                }
            }
            return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
        catch
        {
            return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    private void OnAnimTimer()
    {
        if (!Win32.IsWindowVisible(_hwnd))
            return;

        long now = Environment.TickCount64;
        long dt = Math.Clamp(now - _lastTick, 0, 100);
        _lastTick = now;

        if (_playing && !_scrubbing)
        {
            _animMs = (_animMs + dt) % TotalLoopMs;
            RequestRepaint();
        }
        else if (!string.IsNullOrEmpty(_bannerText) && now > _bannerUntil)
        {
            _bannerText = null;
            RequestRepaint();
        }
    }

    private static (int X, int Y) UnpackPoint(nint lParam)
    {
        short x = (short)(lParam & 0xFFFF);
        short y = (short)((lParam >> 16) & 0xFFFF);
        return (x, y);
    }

    private void OnMouseMove(nint lParam)
    {
        if (!_trackingMouse)
        {
            var tme = new Win32.TRACKMOUSEEVENT
            {
                cbSize = (uint)sizeof(Win32.TRACKMOUSEEVENT),
                dwFlags = Win32.TME_LEAVE,
                hwndTrack = _hwnd
            };
            Win32.TrackMouseEvent(&tme);
            _trackingMouse = true;
        }

        var (mx, my) = UnpackPoint(lParam);
        if (_scrubbing)
        {
            UpdateScrubFromX(mx);
            return;
        }

        string? nextHover = FindHit(mx, my)?.Id;
        _hoverIsHand = nextHover != null || PointInRect(_timelineTrackRect, mx, my);
        Win32.SetCursor(_hoverIsHand && _cursorHand != 0 ? _cursorHand : _cursorArrow);

        if (!string.Equals(nextHover, _hoverId, StringComparison.Ordinal))
        {
            _hoverId = nextHover;
            if (!_playing)
                RequestRepaint();
        }
    }

    private void OnMouseDown(nint lParam)
    {
        var (mx, my) = UnpackPoint(lParam);
        if (PointInRect(_timelineTrackRect, mx, my))
        {
            _scrubbing = true;
            Win32.SetCapture(_hwnd);
            UpdateScrubFromX(mx);
            return;
        }

        try { FindHit(mx, my)?.OnClick(); } catch { }
    }

    private void UpdateScrubFromX(int mx)
    {
        int w = _timelineTrackRect.Width;
        if (w <= 0) return;
        float ratio = Math.Clamp((float)(mx - _timelineTrackRect.Left) / w, 0f, 0.999f);
        _animMs = (long)(ratio * TotalLoopMs);
        _lastTick = Environment.TickCount64;
        RequestRepaint();
    }

    private HitRegion? FindHit(int x, int y)
    {
        for (int i = _hitRegions.Count - 1; i >= 0; i--)
        {
            if (PointInRect(_hitRegions[i].Bounds, x, y))
                return _hitRegions[i];
        }
        return null;
    }

    private static bool PointInRect(Win32.RECT r, int x, int y)
        => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;

    private bool EnsureBackBuffer(nint referenceDc, int w, int h)
    {
        if (_backDc != 0 && _backBmp != 0 && _backW == w && _backH == h)
            return true;

        ReleaseBackBuffer();

        nint memDc = Win32.CreateCompatibleDC(referenceDc);
        if (memDc == 0)
            return false;

        var bmi = new Win32.BITMAPINFO
        {
            bmiHeader = new Win32.BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(Win32.BITMAPINFOHEADER),
                biWidth = w,
                biHeight = -h,
                biPlanes = 1,
                biBitCount = 32
            }
        };

        nint dib = Win32.CreateDIBSection(memDc, &bmi, 0, out _, 0, 0);
        if (dib == 0)
        {
            Win32.DeleteDC(memDc);
            return false;
        }

        nint oldBmp = Win32.SelectObject(memDc, dib);
        Win32.SetBkMode(memDc, Win32.TRANSPARENT);
        _backDc = memDc;
        _backBmp = dib;
        _backOldBmp = oldBmp;
        _backW = w;
        _backH = h;
        return true;
    }

    private void ReleaseBackBuffer()
    {
        if (_backDc != 0)
        {
            if (_backOldBmp != 0)
            {
                Win32.SelectObject(_backDc, _backOldBmp);
                _backOldBmp = 0;
            }
            Win32.DeleteDC(_backDc);
            _backDc = 0;
        }
        if (_backBmp != 0)
        {
            Win32.DeleteObject(_backBmp);
            _backBmp = 0;
        }
        _backW = 0;
        _backH = 0;
    }

    private void PaintWindow()
    {
        var ps = new Win32.PAINTSTRUCT();
        nint paintDc = Win32.BeginPaint(_hwnd, &ps);
        if (paintDc == 0) return;

        if (Win32.GetClientRect(_hwnd, out var rc))
        {
            _clientW = rc.Width;
            _clientH = rc.Height;
        }
        int w = _clientW;
        int h = _clientH;
        if (w <= 0 || h <= 0)
        {
            Win32.EndPaint(_hwnd, &ps);
            return;
        }

        nint gfx = 0;
        try
        {
            if (!EnsureBackBuffer(paintDc, w, h))
                return;

            _hitRegions.Clear();
            Win32.SetBkMode(_backDc, Win32.TRANSPARENT);

            if (GdiPlus.GdipCreateFromHDC(_backDc, out gfx) == 0 && gfx != 0)
            {
                GdiPlus.GdipSetSmoothingMode(gfx, GdiPlus.SmoothingModeAntiAlias);
                GdiPlus.GdipSetPixelOffsetMode(gfx, GdiPlus.PixelOffsetModeHighQuality);
                GdiPlus.GdipGraphicsClear(gfx, GdiPlus.Argb(Theme.BgWindow));

                RenderContent(_backDc, gfx, w, h);
            }

            Win32.BitBlt(paintDc, 0, 0, w, h, _backDc, 0, 0, Win32.SRCCOPY);
        }
        finally
        {
            if (gfx != 0) GdiPlus.GdipDeleteGraphics(gfx);
            Win32.EndPaint(_hwnd, &ps);
        }
    }

    private void RenderContent(nint dc, nint gfx, int w, int h)
    {
        bool isFirefox = _mode == ExtensionGuideMode.Firefox;
        int pad = S(20);
        int contentW = w - pad * 2;
        var (browsers, tabs) = _bridgeServer.GetStatusSnapshot();
        bool isConnected = browsers > 0;

        // 1. Header Row
        string title = isFirefox ? Strings.GuideTitleFirefox : Strings.GuideTitleChromium;
        string badgeText = isConnected
            ? Strings.BadgeBridgeConnected(browsers, tabs)
            : (isFirefox ? Strings.GuideBadgeFirefox : Strings.GuideBadgeChromium);
        uint badgeFg = isConnected ? Theme.Green : Theme.Accent;
        uint badgeBg = isConnected ? Theme.GreenSoft : Theme.AccentSoft;

        int badgeW = Math.Max(S(110), MeasureTextWidth(dc, badgeText, 10, Win32.FW_SEMIBOLD) + S(22));
        DrawText(dc, title, pad, S(14), w - pad - badgeW - S(10), S(44), 15, Win32.FW_BOLD, Theme.TextPrimary);

        int pillH = S(24);
        int pillY = S(17);
        GdiPlus.FillRoundedRect(gfx, w - pad - badgeW, pillY, badgeW, pillH, pillH / 2f, GdiPlus.Argb(badgeBg));
        DrawText(dc, badgeText, w - pad - badgeW, pillY, w - pad, pillY + pillH, 10, Win32.FW_SEMIBOLD, badgeFg, centered: true);

        // 2. Animated Tutorial Video Stage
        int stageY = S(54);
        int stageH = S(248);
        GdiPlus.FillAndStrokeRoundedRect(gfx, pad, stageY, contentW, stageH, SF(12),
            GdiPlus.Argb(Theme.BgCard), GdiPlus.Argb(Theme.BorderStrong), 1.2f);

        int barH = S(40);
        int canvasH = stageH - barH;
        if (isFirefox)
            DrawFirefoxAnimation(dc, gfx, pad, stageY, contentW, canvasH, isConnected);
        else
            DrawChromiumAnimation(dc, gfx, pad, stageY, contentW, canvasH, isConnected);

        DrawPlayerBar(dc, gfx, pad, stageY + canvasH, contentW, barH);

        // 3. Interactive 3-Step Checklist
        int activeScene = Math.Clamp((int)(_animMs / SceneDurationMs), 0, 2);
        string[] steps = isFirefox
            ? [Strings.GuideStepFirefox1, Strings.GuideStepFirefox2, Strings.GuideStepFirefox3]
            : [Strings.GuideStepChromium1, Strings.GuideStepChromium2, Strings.GuideStepChromium3];

        int stepsY = stageY + stageH + S(12);
        int rowH = S(32);
        int rowGap = S(6);
        for (int i = 0; i < 3; i++)
        {
            int ry = stepsY + i * (rowH + rowGap);
            bool active = i == activeScene;
            bool doneStep = (i < activeScene) || (isConnected && i == 2);
            string rowId = $"guide_step_{i}";
            bool hovered = string.Equals(_hoverId, rowId, StringComparison.Ordinal);

            uint rBg = active ? Theme.BgCardElevated : (hovered ? Theme.BgCardHover : Theme.BgCard);
            uint rBorder = active ? Theme.Accent : (doneStep ? Theme.Green : Theme.BorderSubtle);
            uint rFg = active ? Theme.TextPrimary : (doneStep ? Theme.Green : Theme.TextSecondary);

            GdiPlus.FillAndStrokeRoundedRect(gfx, pad, ry, contentW, rowH, SF(8),
                GdiPlus.Argb(rBg), GdiPlus.Argb(rBorder, active ? (byte)220 : (byte)150));

            int dotR = S(8);
            int dotX = pad + S(12);
            int dotY = ry + (rowH - dotR) / 2;
            uint dotColor = active ? Theme.Accent : (doneStep ? Theme.Green : Theme.TextMuted);
            FillCircle(gfx, GdiPlus.Argb(dotColor), dotX, dotY, dotR);

            DrawText(dc, steps[i], dotX + dotR + S(10), ry, pad + contentW - S(12), ry + rowH,
                11, active ? Win32.FW_SEMIBOLD : Win32.FW_NORMAL, rFg);

            int sceneIndex = i;
            _hitRegions.Add(new HitRegion(
                new Win32.RECT { Left = pad, Top = ry, Right = pad + contentW, Bottom = ry + rowH },
                rowId,
                () =>
                {
                    _animMs = sceneIndex * SceneDurationMs;
                    _lastTick = Environment.TickCount64;
                    RequestRepaint();
                }));
        }

        // 4. Live Status / Target Path Strip
        int stripY = stepsY + 3 * rowH + 2 * rowGap + S(10);
        int stripH = S(28);
        bool showBanner = !string.IsNullOrEmpty(_bannerText) && Environment.TickCount64 <= _bannerUntil;
        if (showBanner)
        {
            uint bFg = _bannerIsError ? Theme.Red : Theme.Green;
            uint bBg = _bannerIsError ? Theme.RedSoft : Theme.GreenSoft;
            GdiPlus.FillAndStrokeRoundedRect(gfx, pad, stripY, contentW, stripH, SF(7),
                GdiPlus.Argb(bBg), GdiPlus.Argb(bFg, 160));
            DrawText(dc, _bannerText!, pad + S(12), stripY, pad + contentW - S(12), stripY + stripH,
                11, Win32.FW_SEMIBOLD, bFg);
        }
        else if (isConnected)
        {
            GdiPlus.FillAndStrokeRoundedRect(gfx, pad, stripY, contentW, stripH, SF(7),
                GdiPlus.Argb(Theme.GreenSoft), GdiPlus.Argb(Theme.Green, 160));
            DrawText(dc, Strings.GuideConnectedNow, pad + S(12), stripY, pad + contentW - S(12), stripY + stripH,
                11, Win32.FW_SEMIBOLD, Theme.Green, centered: true);
        }
        else
        {
            string targetPath = isFirefox
                ? Path.Combine(ExtensionSetupService.GetFirefoxExtensionDir(), "manifest.json")
                : ExtensionSetupService.GetChromiumExtensionDir();
            GdiPlus.FillAndStrokeRoundedRect(gfx, pad, stripY, contentW, stripH, SF(7),
                GdiPlus.Argb(Theme.BgHeader), GdiPlus.Argb(Theme.BorderSubtle));
            DrawText(dc, targetPath, pad + S(12), stripY, pad + contentW - S(12), stripY + stripH,
                10, Win32.FW_NORMAL, Theme.TextMuted);
        }

        // 5. Bottom Action Bar
        int actY = h - S(50);
        int actH = S(36);
        int gap = S(8);
        int closeW = S(84);
        int folderW = S(116);
        int copyW = S(124);
        int mainW = Math.Max(S(180), contentW - closeW - folderW - copyW - gap * 3);

        DrawActionButton(dc, gfx, "guide_open_copy", Strings.GuideBtnOpenAndCopy,
            pad, actY, mainW, actH,
            Theme.Accent, Theme.AccentHover, Theme.OnAccent, Theme.Accent,
            () =>
            {
                var (ok, msg) = isFirefox
                    ? ExtensionSetupService.LaunchFirefoxGuidedInstall()
                    : ExtensionSetupService.LaunchChromiumGuidedInstall();
                ShowBanner(msg, !ok, 6500);
            });

        int x2 = pad + mainW + gap;
        DrawActionButton(dc, gfx, "guide_copy_path", Strings.GuideBtnCopyPath,
            x2, actY, copyW, actH,
            Theme.BgControl, Theme.BgControlHover, Theme.TextPrimary, Theme.BorderStrong,
            () =>
            {
                var (ok, msg) = ExtensionSetupService.CopyExtensionPath(isFirefox);
                ShowBanner(msg, !ok, 4500);
            });

        int x3 = x2 + copyW + gap;
        DrawActionButton(dc, gfx, "guide_open_folder", Strings.BtnOpenExtFolder,
            x3, actY, folderW, actH,
            Theme.BgControl, Theme.BgControlHover, Theme.TextPrimary, Theme.BorderStrong,
            () =>
            {
                if (!ExtensionSetupService.OpenSpecificExtensionFolder(isFirefox))
                    ShowBanner(Strings.MsgBridgeFolderMissing, isError: true);
            });

        int x4 = x3 + folderW + gap;
        DrawActionButton(dc, gfx, "guide_close", Strings.GuideBtnClose,
            x4, actY, closeW, actH,
            Theme.BgCard, Theme.BgControlHover, Theme.TextSecondary, Theme.BorderSubtle,
            HideDialog);
    }

    private void DrawPlayerBar(nint dc, nint gfx, int x, int y, int w, int h)
    {
        GdiPlus.FillRoundedRect(gfx, x + S(1), y, w - S(2), h - S(1), SF(8), GdiPlus.Argb(Theme.BgHeader));
        GdiPlus.FillRoundedRect(gfx, x + S(1), y, w - S(2), 1f, 0, GdiPlus.Argb(Theme.BorderSubtle));

        int btnSz = S(26);
        int btnX = x + S(12);
        int btnY = y + (h - btnSz) / 2;
        bool hovered = string.Equals(_hoverId, "guide_play", StringComparison.Ordinal);

        GdiPlus.FillAndStrokeRoundedRect(gfx, btnX, btnY, btnSz, btnSz, SF(6),
            GdiPlus.Argb(hovered ? Theme.AccentHover : Theme.Accent), GdiPlus.Argb(Theme.Accent));

        if (GdiPlus.GdipCreateSolidFill(GdiPlus.Argb(Theme.OnAccent), out nint br) == 0 && br != 0)
        {
            try
            {
                float cx = btnX + btnSz / 2f;
                float cy = btnY + btnSz / 2f;
                float r = S(5);
                if (_playing)
                {
                    float bw = S(2) + 0.5f;
                    GdiPlus.GdipFillRectangle(gfx, br, cx - bw * 1.4f, cy - r, bw, r * 2f);
                    GdiPlus.GdipFillRectangle(gfx, br, cx + bw * 0.4f, cy - r, bw, r * 2f);
                }
                else
                {
                    FillTriangle(gfx, br, cx - r * 0.55f, cy - r, cx - r * 0.55f, cy + r, cx + r, cy);
                }
            }
            finally
            {
                GdiPlus.GdipDeleteBrush(br);
            }
        }

        _hitRegions.Add(new HitRegion(
            new Win32.RECT { Left = btnX, Top = btnY, Right = btnX + btnSz, Bottom = btnY + btnSz },
            "guide_play",
            () =>
            {
                _playing = !_playing;
                _lastTick = Environment.TickCount64;
                RequestRepaint();
            }));

        int sceneIdx = Math.Clamp((int)(_animMs / SceneDurationMs), 0, 2);
        int stepLblW = S(42);
        int stepLblX = x + w - S(12) - stepLblW;
        DrawText(dc, $"0{sceneIdx + 1}/03", stepLblX, y, stepLblX + stepLblW, y + h,
            10, Win32.FW_SEMIBOLD, Theme.TextSecondary, centered: true);

        int langW = S(32);
        int langH = S(18);
        int langX = stepLblX - S(6) - langW;
        int langY = y + (h - langH) / 2;
        GdiPlus.FillRoundedRect(gfx, langX, langY, langW, langH, SF(4), GdiPlus.Argb(Theme.AccentSoft));
        DrawText(dc, Strings.GuideLangTag, langX, langY, langX + langW, langY + langH,
            9, Win32.FW_BOLD, Theme.Accent, centered: true);

        int trackX = btnX + btnSz + S(14);
        int trackRight = langX - S(12);
        int trackW = Math.Max(S(60), trackRight - trackX);
        int trackH = S(6);
        int trackY = y + (h - trackH) / 2;

        _timelineTrackRect = new Win32.RECT { Left = trackX, Top = y + S(4), Right = trackX + trackW, Bottom = y + h - S(4) };

        GdiPlus.FillRoundedRect(gfx, trackX, trackY, trackW, trackH, trackH / 2f, GdiPlus.Argb(Theme.BgControl));

        float progress = Math.Clamp((float)_animMs / TotalLoopMs, 0f, 1f);
        float fillW = Math.Max(trackH, trackW * progress);
        GdiPlus.FillRoundedRect(gfx, trackX, trackY, fillW, trackH, trackH / 2f, GdiPlus.Argb(Theme.Accent));

        // 3 scene boundary markers
        for (int m = 1; m <= 2; m++)
        {
            float mx = trackX + trackW * (m / 3f);
            GdiPlus.FillRoundedRect(gfx, mx - 1f, trackY - S(2), 2f, trackH + S(4), 1f, GdiPlus.Argb(Theme.BgWindow));
        }

        int knob = S(12);
        float knobX = trackX + trackW * progress - knob / 2f;
        float knobY = y + (h - knob) / 2f;
        FillCircle(gfx, GdiPlus.Argb(0xFFFFFF), knobX, knobY, knob);
        StrokeCircle(gfx, GdiPlus.Argb(Theme.Accent), MathF.Max(1.4f, SF(1.6f)), knobX, knobY, knob);
    }

    private void DrawChromiumAnimation(nint dc, nint gfx, int x, int y, int w, int h, bool realConnected)
    {
        int scene = Math.Clamp((int)(_animMs / SceneDurationMs), 0, 2);
        float t = (float)(_animMs % SceneDurationMs) / SceneDurationMs;

        int bx = x + S(16);
        int by = y + S(10);
        int bw = w - S(32);
        int bh = h - S(18);

        // Mini Browser Frame
        GdiPlus.FillAndStrokeRoundedRect(gfx, bx, by, bw, bh, SF(8),
            GdiPlus.Argb(Theme.BgWindow), GdiPlus.Argb(Theme.BorderSubtle));

        // Address bar
        int addrH = S(26);
        GdiPlus.FillRoundedRect(gfx, bx, by, bw, addrH, SF(8), GdiPlus.Argb(Theme.BgHeader));
        GdiPlus.FillAndStrokeRoundedRect(gfx, bx + S(12), by + S(4), bw - S(24), S(18), SF(4),
            GdiPlus.Argb(Theme.BgCard), GdiPlus.Argb(Theme.BorderSubtle));
        DrawText(dc, "chrome://extensions", bx + S(20), by + S(4), bx + bw - S(20), by + S(22),
            10, Win32.FW_MEDIUM, Theme.TextSecondary);

        // Developer mode toggle (top-right inside browser)
        bool devOn = scene > 0 || t >= 0.52f;
        float swW = S(34);
        float swH = S(17);
        float swX = bx + bw - S(18) - swW;
        float swY = by + addrH + S(10);
        DrawText(dc, Strings.GuideAnimDevMode, bx + bw - S(185), (int)swY, (int)swX - S(8), (int)(swY + swH),
            10, Win32.FW_MEDIUM, devOn ? Theme.TextPrimary : Theme.TextSecondary);

        GdiPlus.FillRoundedRect(gfx, swX, swY, swW, swH, swH / 2f,
            GdiPlus.Argb(devOn ? Theme.Accent : Theme.SwitchOffTrack));
        float knobD = swH - S(4);
        float knobX = devOn ? swX + swW - knobD - S(2) : swX + S(2);
        FillCircle(gfx, GdiPlus.Argb(Theme.SwitchKnob), knobX, swY + S(2), knobD);

        // Load unpacked button
        int loadX = bx + S(18);
        int loadY = by + addrH + S(8);
        int loadW = S(162);
        int loadH = S(24);
        bool loadHl = (scene == 1 && t >= 0.42f && t <= 0.72f);
        GdiPlus.FillAndStrokeRoundedRect(gfx, loadX, loadY, loadW, loadH, SF(5),
            GdiPlus.Argb(loadHl ? Theme.AccentSoft : Theme.BgControl),
            GdiPlus.Argb(loadHl ? Theme.Accent : Theme.BorderStrong));
        DrawText(dc, Strings.GuideAnimLoadUnpacked, loadX + S(4), loadY, loadX + loadW - S(4), loadY + loadH,
            10, Win32.FW_SEMIBOLD, devOn ? Theme.TextPrimary : Theme.TextMuted, centered: true);

        // Extension card appearing in Scene 2 (after confirmation) or when connected
        bool showExtCard = (scene == 2 && t >= 0.70f) || realConnected;
        if (showExtCard)
        {
            int cx = bx + S(18);
            int cy = loadY + loadH + S(12);
            int cw = S(268);
            int ch = S(58);
            GdiPlus.FillAndStrokeRoundedRect(gfx, cx, cy, cw, ch, SF(7),
                GdiPlus.Argb(Theme.BgCardElevated), GdiPlus.Argb(Theme.Green, 190));
            GdiPlus.FillRoundedRect(gfx, cx + S(10), cy + S(12), S(24), S(24), SF(5), GdiPlus.Argb(Theme.AccentSoft));
            DrawText(dc, "C", cx + S(10), cy + S(12), cx + S(34), cy + S(36), 12, Win32.FW_BOLD, Theme.Accent, centered: true);
            DrawText(dc, "Cueway Companion", cx + S(42), cy + S(9), cx + cw - S(10), cy + S(28),
                11, Win32.FW_SEMIBOLD, Theme.TextPrimary);
            DrawText(dc, realConnected ? Strings.GuideAnimConnectedLive : Strings.GuideAnimActiveVer,
                cx + S(42), cy + S(29), cx + cw - S(10), cy + S(47),
                10, Win32.FW_SEMIBOLD, Theme.Green);
        }

        // Folder Picker Popup Overlay in Scene 1 late & Scene 2 early
        if ((scene == 1 && t >= 0.58f) || (scene == 2 && t < 0.74f))
        {
            float slide = scene == 1 ? Ease(Math.Clamp((t - 0.58f) / 0.25f, 0f, 1f)) : 1f;
            int pw = S(344);
            int ph = S(112);
            int px = bx + (bw - pw) / 2;
            int py = by + addrH + S(14) + (int)((1f - slide) * S(10));

            GdiPlus.FillAndStrokeRoundedRect(gfx, px, py, pw, ph, SF(8),
                GdiPlus.Argb(Theme.BgCardElevated), GdiPlus.Argb(Theme.Accent, 210), 1.4f);

            DrawText(dc, Strings.GuideAnimDialogFolderTitle, px + S(12), py + S(5), px + pw - S(12), py + S(22),
                10, Win32.FW_SEMIBOLD, Theme.TextSecondary);

            bool pasted = scene == 2 && t >= 0.22f;
            int pathBoxY = py + S(25);
            GdiPlus.FillAndStrokeRoundedRect(gfx, px + S(12), pathBoxY, pw - S(24), S(25), SF(4),
                GdiPlus.Argb(Theme.BgWindow), GdiPlus.Argb(pasted ? Theme.Accent : Theme.BorderStrong));
            DrawText(dc, pasted ? "...\\BrowserExtension\\chromium" : "C:\\...",
                px + S(20), pathBoxY, px + pw - S(20), pathBoxY + S(25),
                10, Win32.FW_MEDIUM, pasted ? Theme.TextPrimary : Theme.TextMuted);

            // Ctrl+V pill
            if (scene == 2 && t >= 0.14f && t < 0.50f)
            {
                DrawKeycapBadge(dc, gfx, px + S(12), py + S(56), Strings.GuideAnimShortcutKey, Strings.GuideAnimPasteHint, pw - S(24));
            }

            // Select Folder button
            int okW = S(118);
            int okH = S(23);
            int okX = px + pw - S(12) - okW;
            int okY = py + ph - S(8) - okH;
            bool okClick = scene == 2 && t >= 0.56f;
            GdiPlus.FillRoundedRect(gfx, okX, okY, okW, okH, SF(5),
                GdiPlus.Argb(okClick ? Theme.AccentHover : Theme.Accent));
            DrawText(dc, Strings.GuideAnimSelectFolder, okX + S(4), okY, okX + okW - S(4), okY + okH,
                10, Win32.FW_SEMIBOLD, Theme.OnAccent, centered: true);
        }

        // Localized narration caption pill inside video frame
        DrawVideoCaption(dc, gfx, bx, by, bw, bh, Strings.GuideAnimCaptionChromium(scene));

        // Animated Mouse Cursor + Click Ripple
        float curX, curY;
        bool clickRipple = false;
        float rippleP = 0f;
        float p1X = swX + swW / 2f, p1Y = swY + swH / 2f;
        float p2X = loadX + loadW * 0.5f, p2Y = loadY + loadH * 0.5f;
        float p3X = bx + bw * 0.5f + S(98), p3Y = by + addrH + S(106);

        if (scene == 0)
        {
            float m = Ease(Math.Clamp(t / 0.45f, 0f, 1f));
            curX = Lerp(bx + S(80), p1X, m);
            curY = Lerp(by + S(110), p1Y, m);
            if (t >= 0.48f && t <= 0.78f)
            {
                clickRipple = true;
                rippleP = (t - 0.48f) / 0.30f;
            }
        }
        else if (scene == 1)
        {
            float m = Ease(Math.Clamp(t / 0.42f, 0f, 1f));
            curX = Lerp(p1X, p2X, m);
            curY = Lerp(p1Y, p2Y, m);
            if (t >= 0.44f && t <= 0.74f)
            {
                clickRipple = true;
                rippleP = (t - 0.44f) / 0.30f;
            }
        }
        else
        {
            float m = Ease(Math.Clamp((t - 0.30f) / 0.28f, 0f, 1f));
            curX = Lerp(bx + bw * 0.5f, p3X, m);
            curY = Lerp(by + addrH + S(52), p3Y, m);
            if (t >= 0.58f && t <= 0.86f)
            {
                clickRipple = true;
                rippleP = (t - 0.58f) / 0.28f;
            }
        }

        if (clickRipple)
            DrawClickRipple(gfx, curX, curY, Math.Clamp(rippleP, 0f, 1f));
        DrawCursorArrow(gfx, curX, curY);
    }

    private void DrawFirefoxAnimation(nint dc, nint gfx, int x, int y, int w, int h, bool realConnected)
    {
        int scene = Math.Clamp((int)(_animMs / SceneDurationMs), 0, 2);
        float t = (float)(_animMs % SceneDurationMs) / SceneDurationMs;

        int bx = x + S(16);
        int by = y + S(10);
        int bw = w - S(32);
        int bh = h - S(18);

        // Mini Browser Frame
        GdiPlus.FillAndStrokeRoundedRect(gfx, bx, by, bw, bh, SF(8),
            GdiPlus.Argb(Theme.BgWindow), GdiPlus.Argb(Theme.BorderSubtle));

        int addrH = S(26);
        GdiPlus.FillRoundedRect(gfx, bx, by, bw, addrH, SF(8), GdiPlus.Argb(Theme.BgHeader));
        GdiPlus.FillAndStrokeRoundedRect(gfx, bx + S(12), by + S(4), bw - S(24), S(18), SF(4),
            GdiPlus.Argb(Theme.BgCard), GdiPlus.Argb(Theme.BorderSubtle));
        DrawText(dc, "about:debugging#/runtime/this-firefox", bx + S(20), by + S(4), bx + bw - S(20), by + S(22),
            10, Win32.FW_MEDIUM, Theme.TextSecondary);

        // Load Temporary Add-on button
        int btnW = S(186);
        int btnH = S(24);
        int btnX = bx + bw - S(18) - btnW;
        int btnY = by + addrH + S(8);
        bool btnHl = (scene == 0 && t >= 0.46f && t <= 0.78f);
        GdiPlus.FillAndStrokeRoundedRect(gfx, btnX, btnY, btnW, btnH, SF(5),
            GdiPlus.Argb(btnHl ? Theme.AccentSoft : Theme.BgControl),
            GdiPlus.Argb(btnHl ? Theme.Accent : Theme.BorderStrong));
        DrawText(dc, Strings.GuideAnimLoadTemp, btnX + S(4), btnY, btnX + btnW - S(4), btnY + btnH,
            10, Win32.FW_SEMIBOLD, Theme.TextPrimary, centered: true);

        // Extension card in Scene 2
        if (scene == 2 || realConnected)
        {
            float fade = scene == 2 ? Ease(Math.Clamp(t / 0.35f, 0f, 1f)) : 1f;
            int cx = bx + S(18);
            int cy = btnY + btnH + S(12) + (int)((1f - fade) * S(6));
            int cw = bw - S(36);
            int ch = S(62);
            GdiPlus.FillAndStrokeRoundedRect(gfx, cx, cy, cw, ch, SF(7),
                GdiPlus.Argb(Theme.BgCardElevated), GdiPlus.Argb(Theme.Green, 190));

            GdiPlus.FillRoundedRect(gfx, cx + S(12), cy + S(14), S(26), S(26), SF(6), GdiPlus.Argb(Theme.AccentSoft));
            DrawText(dc, "C", cx + S(12), cy + S(14), cx + S(38), cy + S(40), 12, Win32.FW_BOLD, Theme.Accent, centered: true);

            int statusW = S(145);
            DrawText(dc, "Cueway Companion", cx + S(48), cy + S(10), cx + cw - statusW - S(12), cy + S(30),
                11, Win32.FW_SEMIBOLD, Theme.TextPrimary);
            DrawText(dc, "cueway@cueway.app • MV2", cx + S(48), cy + S(32), cx + cw - statusW - S(12), cy + S(50),
                10, Win32.FW_NORMAL, Theme.TextSecondary);
            DrawText(dc, realConnected ? Strings.GuideAnimRunningConnected : Strings.GuideAnimRunning,
                cx + cw - statusW - S(10), cy + S(10), cx + cw - S(14), cy + S(30),
                10, Win32.FW_SEMIBOLD, Theme.Green);
        }

        // File Picker Overlay in Scene 0 late & Scene 1
        if ((scene == 0 && t >= 0.62f) || scene == 1)
        {
            float slide = scene == 0 ? Ease(Math.Clamp((t - 0.62f) / 0.25f, 0f, 1f)) : 1f;
            int pw = S(356);
            int ph = S(112);
            int px = bx + (bw - pw) / 2;
            int py = by + addrH + S(14) + (int)((1f - slide) * S(10));

            GdiPlus.FillAndStrokeRoundedRect(gfx, px, py, pw, ph, SF(8),
                GdiPlus.Argb(Theme.BgCardElevated), GdiPlus.Argb(Theme.Accent, 210), 1.4f);

            DrawText(dc, Strings.GuideAnimDialogFileTitle, px + S(12), py + S(5), px + pw - S(12), py + S(22),
                10, Win32.FW_SEMIBOLD, Theme.TextSecondary);

            bool pasted = scene == 1 && t >= 0.24f;
            int pathBoxY = py + S(25);
            GdiPlus.FillAndStrokeRoundedRect(gfx, px + S(12), pathBoxY, pw - S(24), S(25), SF(4),
                GdiPlus.Argb(Theme.BgWindow), GdiPlus.Argb(pasted ? Theme.Accent : Theme.BorderStrong));
            DrawText(dc, pasted ? "...\\Firefox\\manifest.json" : Strings.GuideAnimFileNamePrompt,
                px + S(20), pathBoxY, px + pw - S(20), pathBoxY + S(25),
                10, Win32.FW_MEDIUM, pasted ? Theme.TextPrimary : Theme.TextMuted);

            if (scene == 1 && t >= 0.16f && t < 0.56f)
            {
                DrawKeycapBadge(dc, gfx, px + S(12), py + S(56), Strings.GuideAnimShortcutKey, Strings.GuideAnimPasteHint, pw - S(24));
            }

            int okW = S(94);
            int okH = S(23);
            int okX = px + pw - S(12) - okW;
            int okY = py + ph - S(8) - okH;
            bool okClick = scene == 1 && t >= 0.62f;
            GdiPlus.FillRoundedRect(gfx, okX, okY, okW, okH, SF(5),
                GdiPlus.Argb(okClick ? Theme.AccentHover : Theme.Accent));
            DrawText(dc, Strings.GuideAnimOpenBtn, okX + S(4), okY, okX + okW - S(4), okY + okH,
                10, Win32.FW_SEMIBOLD, Theme.OnAccent, centered: true);
        }

        // Localized narration caption pill inside video frame
        DrawVideoCaption(dc, gfx, bx, by, bw, bh, Strings.GuideAnimCaptionFirefox(scene));

        // Animated Mouse Cursor + Click Ripple
        float curX, curY;
        bool clickRipple = false;
        float rippleP = 0f;
        float bCenterX = btnX + btnW * 0.5f, bCenterY = btnY + btnH * 0.5f;
        float openX = bx + bw * 0.5f + S(118), openY = by + addrH + S(106);

        if (scene == 0)
        {
            float m = Ease(Math.Clamp(t / 0.44f, 0f, 1f));
            curX = Lerp(bx + S(90), bCenterX, m);
            curY = Lerp(by + S(115), bCenterY, m);
            if (t >= 0.46f && t <= 0.78f)
            {
                clickRipple = true;
                rippleP = (t - 0.46f) / 0.32f;
            }
        }
        else if (scene == 1)
        {
            float m = Ease(Math.Clamp((t - 0.25f) / 0.34f, 0f, 1f));
            curX = Lerp(bx + bw * 0.45f, openX, m);
            curY = Lerp(by + addrH + S(50), openY, m);
            if (t >= 0.62f && t <= 0.90f)
            {
                clickRipple = true;
                rippleP = (t - 0.62f) / 0.28f;
            }
        }
        else
        {
            float m = Ease(Math.Clamp(t / 0.45f, 0f, 1f));
            curX = Lerp(openX, bx + bw - S(65), m);
            curY = Lerp(openY, by + addrH + S(58), m);
        }

        if (clickRipple)
            DrawClickRipple(gfx, curX, curY, Math.Clamp(rippleP, 0f, 1f));
        DrawCursorArrow(gfx, curX, curY);
    }

    private void DrawVideoCaption(nint dc, nint gfx, int bx, int by, int bw, int bh, string caption)
    {
        int capH = S(22);
        int capW = Math.Clamp(MeasureTextWidth(dc, caption, 10, Win32.FW_SEMIBOLD) + S(24), S(160), bw - S(24));
        int capX = bx + (bw - capW) / 2;
        int capY = by + bh - capH - S(7);
        GdiPlus.FillAndStrokeRoundedRect(gfx, capX, capY, capW, capH, capH / 2f,
            GdiPlus.Argb(Theme.BgHeader, 235), GdiPlus.Argb(Theme.Accent, 130));
        DrawText(dc, caption, capX + S(10), capY, capX + capW - S(10), capY + capH,
            10, Win32.FW_SEMIBOLD, Theme.TextPrimary, centered: true);
    }

    private void DrawKeycapBadge(nint dc, nint gfx, int x, int y, string keys, string hint, int maxWidth)
    {
        int keyW = S(60);
        int h = S(22);
        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, keyW, h, SF(4),
            GdiPlus.Argb(Theme.AccentSoft), GdiPlus.Argb(Theme.Accent));
        DrawText(dc, keys, x, y, x + keyW, y + h, 10, Win32.FW_BOLD, Theme.Accent, centered: true);
        DrawText(dc, hint, x + keyW + S(8), y, x + maxWidth, y + h, 10, Win32.FW_MEDIUM, Theme.TextSecondary);
    }

    private void DrawClickRipple(nint gfx, float cx, float cy, float progress)
    {
        float r = SF(4f + 16f * progress);
        byte alpha = (byte)Math.Clamp((int)(220 * (1f - progress)), 0, 255);
        if (GdiPlus.GdipCreatePen1(GdiPlus.Argb(Theme.Accent, alpha), SF(2f), GdiPlus.UnitPixel, out nint pen) == 0 && pen != 0)
        {
            try { GdiPlus.GdipDrawEllipse(gfx, pen, cx - r, cy - r, r * 2f, r * 2f); }
            finally { GdiPlus.GdipDeletePen(pen); }
        }
    }

    private void DrawCursorArrow(nint gfx, float x, float y)
    {
        float s = _dpi / 96f;
        if (GdiPlus.GdipCreatePath(GdiPlus.FillModeAlternate, out nint path) != 0 || path == 0)
            return;
        try
        {
            GdiPlus.GdipAddPathLine(path, x, y, x, y + 14f * s);
            GdiPlus.GdipAddPathLine(path, x, y + 14f * s, x + 3.8f * s, y + 10.6f * s);
            GdiPlus.GdipAddPathLine(path, x + 3.8f * s, y + 10.6f * s, x + 6.8f * s, y + 16.5f * s);
            GdiPlus.GdipAddPathLine(path, x + 6.8f * s, y + 16.5f * s, x + 9.2f * s, y + 15.3f * s);
            GdiPlus.GdipAddPathLine(path, x + 9.2f * s, y + 15.3f * s, x + 6.2f * s, y + 9.5f * s);
            GdiPlus.GdipAddPathLine(path, x + 6.2f * s, y + 9.5f * s, x + 11f * s, y + 9.5f * s);
            GdiPlus.GdipClosePathFigure(path);

            if (GdiPlus.GdipCreateSolidFill(GdiPlus.Argb(0xFFFFFF), out nint br) == 0 && br != 0)
            {
                try { GdiPlus.GdipFillPath(gfx, br, path); }
                finally { GdiPlus.GdipDeleteBrush(br); }
            }
            if (GdiPlus.GdipCreatePen1(GdiPlus.Argb(0x0B0E11), MathF.Max(1.2f, 1.3f * s), GdiPlus.UnitPixel, out nint pen) == 0 && pen != 0)
            {
                try { GdiPlus.GdipDrawPath(gfx, pen, path); }
                finally { GdiPlus.GdipDeletePen(pen); }
            }
        }
        finally
        {
            GdiPlus.GdipDeletePath(path);
        }
    }

    private static float Ease(float t) => t * t * (3f - 2f * t);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static void FillCircle(nint gfx, uint argb, float x, float y, float d)
    {
        if (GdiPlus.GdipCreateSolidFill(argb, out nint br) == 0 && br != 0)
        {
            try { GdiPlus.GdipFillEllipse(gfx, br, x, y, d, d); }
            finally { GdiPlus.GdipDeleteBrush(br); }
        }
    }

    private static void StrokeCircle(nint gfx, uint argb, float width, float x, float y, float d)
    {
        if (GdiPlus.GdipCreatePen1(argb, width, GdiPlus.UnitPixel, out nint pen) == 0 && pen != 0)
        {
            try { GdiPlus.GdipDrawEllipse(gfx, pen, x, y, d, d); }
            finally { GdiPlus.GdipDeletePen(pen); }
        }
    }

    private static void FillTriangle(nint gfx, nint brush, float x1, float y1, float x2, float y2, float x3, float y3)
    {
        if (GdiPlus.GdipCreatePath(GdiPlus.FillModeAlternate, out nint path) != 0 || path == 0) return;
        try
        {
            GdiPlus.GdipAddPathLine(path, x1, y1, x2, y2);
            GdiPlus.GdipAddPathLine(path, x2, y2, x3, y3);
            GdiPlus.GdipClosePathFigure(path);
            GdiPlus.GdipFillPath(gfx, brush, path);
        }
        finally
        {
            GdiPlus.GdipDeletePath(path);
        }
    }

    private void DrawActionButton(
        nint dc, nint gfx, string id, string text,
        int x, int y, int w, int h,
        uint bgNormal, uint bgHover, uint fg, uint border, Action onClick)
    {
        bool hovered = string.Equals(_hoverId, id, StringComparison.Ordinal);
        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, w, h, SF(7),
            GdiPlus.Argb(hovered ? bgHover : bgNormal),
            GdiPlus.Argb(border));
        DrawText(dc, text, x + S(4), y, x + w - S(4), y + h, 11, Win32.FW_SEMIBOLD, fg, centered: true);
        _hitRegions.Add(new HitRegion(new Win32.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h }, id, onClick));
    }

    private nint GetOrCreateFont(int ptSize, int weight)
    {
        var (face, actualWeight) = Strings.ResolveFont(weight);
        var key = (ptSize, actualWeight, face, _dpi);
        if (_fontCache.TryGetValue(key, out nint cached) && cached != 0)
            return cached;

        if (_fontCache.Count >= 64)
            ClearFontCache();

        nint created = Win32.CreateFontW(
            -S(ptSize), 0, 0, 0, actualWeight, 0, 0, 0,
            1, 0, 0, Win32.CLEARTYPE_QUALITY, 0, face);
        if (created != 0)
            _fontCache[key] = created;
        return created;
    }

    private void ClearFontCache()
    {
        foreach (nint font in _fontCache.Values)
        {
            if (font != 0) Win32.DeleteObject(font);
        }
        _fontCache.Clear();
    }

    private int MeasureTextWidth(nint dc, string text, int ptSize, int weight)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        nint font = GetOrCreateFont(ptSize, weight);
        if (font == 0)
            return (int)MathF.Ceiling(text.Length * S(ptSize) * 0.6f);

        nint old = Win32.SelectObject(dc, font);
        try
        {
            var rc = new Win32.RECT { Left = 0, Top = 0, Right = 2000, Bottom = S(40) };
            Win32.DrawTextW(dc, text, -1, &rc, Win32.DT_CALCRECT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
            return Math.Max(0, rc.Right - rc.Left);
        }
        finally
        {
            Win32.SelectObject(dc, old);
        }
    }

    private void DrawText(nint dc, string text, int left, int top, int right, int bottom, int ptSize, int weight, uint rgb, bool centered = false)
    {
        if (right <= left || bottom <= top) return;
        nint font = GetOrCreateFont(ptSize, weight);
        if (font == 0) return;

        nint old = Win32.SelectObject(dc, font);
        try
        {
            Win32.SetTextColor(dc, Theme.ToColorRef(rgb));
            var rc = new Win32.RECT { Left = left, Top = top, Right = right, Bottom = bottom };
            uint fmt = (centered ? Win32.DT_CENTER : Win32.DT_LEFT)
                       | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_END_ELLIPSIS | Win32.DT_NOPREFIX;
            Win32.DrawTextW(dc, text, -1, &rc, fmt);
        }
        finally
        {
            Win32.SelectObject(dc, old);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bridgeServer.SessionsChanged -= OnBridgeChanged;

        if (_hwnd != 0)
        {
            Win32.KillTimer(_hwnd, TIMER_ANIM);
            Win32.DestroyWindow(_hwnd);
            _hwnd = 0;
        }
        ClearFontCache();
        ReleaseBackBuffer();
        if (ReferenceEquals(_instance, this))
            _instance = null;
    }
}
