using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cueway.Config;
using Cueway.Interop;
using Cueway.Localization;
using Cueway.Media;
using Cueway.Routing;

namespace Cueway.UI;

/// <summary>
/// Compact EarTrumpet-style tray flyout providing quick per-session playback, lock, mute, and volume controls.
/// </summary>
internal sealed unsafe class TrayFlyoutWindow : IDisposable
{
    private const string ClassName = "Cueway_TrayFlyout";
    private const uint WM_APP_FLYOUT_REFRESH = Win32.WM_APP + 216;
    private const int FlyoutWidthPx = 372;
    private const int HeaderHeightPx = 46;
    private const int CardHeightPx = 88;
    private const int CardGapPx = 8;
    private const int MaxVisibleCards = 4;

    private sealed record HitRegion(Win32.RECT Bounds, string Id, Action OnClick, bool IsButton = true);

    private sealed record SliderRegion(
        Win32.RECT Bounds,
        string Id,
        string SessionId,
        int TrackLeft,
        int TrackWidth);

    private static TrayFlyoutWindow? _instance;

    private readonly StateStore _stateStore;
    private readonly MediaRouterEngine _engine;
    private readonly DynamicIconRenderer _iconRenderer;
    private readonly Func<Win32.RECT?> _getTrayRect;
    private readonly List<HitRegion> _hitRegions = [];
    private readonly List<SliderRegion> _sliderRegions = [];
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
    private int _clientW = FlyoutWidthPx;
    private int _clientH = 180;
    private int _scrollY;
    private int _contentTotalH;

    private string? _hoverId;
    private bool _trackingMouse;
    private string? _draggingSliderId;
    private string? _draggingSessionId;
    private int _dragTrackLeft;
    private int _dragTrackWidth;
    private float _dragPreviewVolume;
    private long _lastSliderDispatchTick;
    private long _lastDeactivateTick;
    private RouterStateSnapshot _snapshot;
    private uint _lastDwmBorder = uint.MaxValue;
    private bool _lastDwmIsLight;
    private bool _disposed;

    public event Action? OpenDashboardRequested;
    public event Action? OpenSettingsRequested;

    public TrayFlyoutWindow(
        StateStore stateStore,
        MediaRouterEngine engine,
        DynamicIconRenderer iconRenderer,
        Func<Win32.RECT?> getTrayRect)
    {
        _stateStore = stateStore;
        _engine = engine;
        _iconRenderer = iconRenderer;
        _getTrayRect = getTrayRect;
        _snapshot = _engine.GetSnapshot();
        _instance = this;

        CreateFlyoutWindow();
        _engine.StateChanged += OnStateChanged;
        _stateStore.StateChanged += OnStoreChanged;
    }

    public bool IsVisible => _hwnd != 0 && Win32.IsWindowVisible(_hwnd);

    private void CreateFlyoutWindow()
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

        _hwnd = Win32.CreateWindowExW(
            Win32.WS_EX_TOPMOST | Win32.WS_EX_TOOLWINDOW,
            ClassName,
            "Cueway",
            Win32.WS_POPUP,
            0, 0, FlyoutWidthPx, 180,
            0, 0, hInst, 0);

        if (_hwnd != 0)
        {
            uint d = Win32.GetDpiForWindow(_hwnd);
            if (d >= 96) _dpi = d;
            ApplyDwmStyle();
        }
    }

    private void ApplyDwmStyle()
    {
        if (_hwnd == 0) return;
        int dark = Theme.IsLight ? 0 : 1;
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(int));
        int corner = Win32.DWMWCP_ROUND;
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(int));
        uint borderColor = Theme.ToColorRef(Theme.BorderStrong);
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_BORDER_COLOR, &borderColor, sizeof(uint));
        _lastDwmIsLight = Theme.IsLight;
        _lastDwmBorder = Theme.BorderStrong;
    }

    private void SyncDwmStyleIfNeeded()
    {
        if (_hwnd == 0) return;
        if (_lastDwmIsLight != Theme.IsLight || _lastDwmBorder != Theme.BorderStrong)
            ApplyDwmStyle();
    }

    public void Toggle()
    {
        if (_disposed || _hwnd == 0) return;
        if (IsVisible)
        {
            Hide();
            return;
        }

        // Ignore the tray click that just caused WM_ACTIVATE(WA_INACTIVE) to close the flyout
        if (Environment.TickCount64 - _lastDeactivateTick < 260)
            return;

        Show();
    }

    public void Show()
    {
        if (_disposed || _hwnd == 0) return;
        _snapshot = _engine.GetSnapshot();
        _scrollY = 0;
        _hoverId = null;

        ApplyDwmStyle();
        PositionAndResize();

        Win32.ShowWindow(_hwnd, Win32.SW_SHOW);
        Win32.SetForegroundWindow(_hwnd);
        Win32.InvalidateRect(_hwnd, null, false);
    }

    public void Hide()
    {
        if (_hwnd == 0 || !Win32.IsWindowVisible(_hwnd)) return;

        if (_draggingSliderId != null)
        {
            _draggingSliderId = null;
            _draggingSessionId = null;
            Win32.ReleaseCapture();
        }

        _hoverId = null;
        Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
        ReleaseBackBuffer();
        ClearFontCache();
        _hitRegions.Clear();
        _sliderRegions.Clear();
    }

    public void RefreshVisuals()
    {
        if (!_disposed && _hwnd != 0 && Win32.IsWindowVisible(_hwnd))
            Win32.PostMessageW(_hwnd, WM_APP_FLYOUT_REFRESH, 0, 0);
    }

    private void OnStateChanged(RouterStateSnapshot snap)
    {
        if (!_disposed && _hwnd != 0 && Win32.IsWindowVisible(_hwnd))
            Win32.PostMessageW(_hwnd, WM_APP_FLYOUT_REFRESH, 0, 0);
    }

    private void OnStoreChanged()
    {
        if (!_disposed && _hwnd != 0 && Win32.IsWindowVisible(_hwnd))
            Win32.PostMessageW(_hwnd, WM_APP_FLYOUT_REFRESH, 0, 0);
    }

    private void HandleAsyncRefresh()
    {
        if (_disposed || _hwnd == 0 || !Win32.IsWindowVisible(_hwnd))
            return;

        ClearFontCache();
        int prevCount = _snapshot.Sessions.Count;
        _snapshot = _engine.GetSnapshot();
        if (_snapshot.Sessions.Count != prevCount)
        {
            PositionAndResize();
        }
        else
        {
            SyncDwmStyleIfNeeded();
        }
        Win32.InvalidateRect(_hwnd, null, false);
    }

    private int ComputeDesiredHeightPx()
    {
        int count = _snapshot.Sessions.Count;
        int padV = 12;
        if (count == 0)
            return HeaderHeightPx + padV * 2 + 86;

        int visible = Math.Min(count, MaxVisibleCards);
        return HeaderHeightPx + padV * 2 + visible * CardHeightPx + Math.Max(0, visible - 1) * CardGapPx;
    }

    private void PositionAndResize()
    {
        if (_hwnd == 0) return;

        Win32.RECT? trayRect = _getTrayRect();
        Win32.POINT anchorPt;
        if (trayRect.HasValue && trayRect.Value.Width > 0 && trayRect.Value.Height > 0)
        {
            anchorPt = new Win32.POINT
            {
                X = (trayRect.Value.Left + trayRect.Value.Right) / 2,
                Y = (trayRect.Value.Top + trayRect.Value.Bottom) / 2
            };
        }
        else
        {
            Win32.GetCursorPos(out anchorPt);
        }

        nint hMon = Win32.MonitorFromPoint(anchorPt, Win32.MONITOR_DEFAULTTONEAREST);
        var mi = new Win32.MONITORINFO { cbSize = (uint)sizeof(Win32.MONITORINFO) };
        Win32.GetMonitorInfoW(hMon, &mi);

        int w = S(FlyoutWidthPx);
        int h = S(ComputeDesiredHeightPx());
        int margin = S(10);

        int maxW = Math.Max(S(280), mi.rcWork.Width - margin * 2);
        int maxH = Math.Max(S(160), mi.rcWork.Height - margin * 2);
        w = Math.Min(w, maxW);
        h = Math.Min(h, maxH);

        int x;
        int y;
        if (trayRect.HasValue && trayRect.Value.Width > 0 && trayRect.Value.Height > 0)
        {
            var tr = trayRect.Value;
            // Detect taskbar edge relative to work area
            if (tr.Top >= mi.rcWork.Bottom)
            {
                // Bottom taskbar
                x = tr.Left + tr.Width / 2 - w / 2;
                y = mi.rcWork.Bottom - h - margin;
            }
            else if (tr.Bottom <= mi.rcWork.Top)
            {
                // Top taskbar
                x = tr.Left + tr.Width / 2 - w / 2;
                y = mi.rcWork.Top + margin;
            }
            else if (tr.Left >= mi.rcWork.Right)
            {
                // Right taskbar
                x = mi.rcWork.Right - w - margin;
                y = tr.Top + tr.Height / 2 - h / 2;
            }
            else if (tr.Right <= mi.rcWork.Left)
            {
                // Left taskbar
                x = mi.rcWork.Left + margin;
                y = tr.Top + tr.Height / 2 - h / 2;
            }
            else
            {
                // Inside work area (e.g. overflow or auto-hide taskbar)
                x = mi.rcWork.Right - w - margin;
                y = mi.rcWork.Bottom - h - margin;
            }
        }
        else
        {
            x = mi.rcWork.Right - w - margin;
            y = mi.rcWork.Bottom - h - margin;
        }

        x = Math.Clamp(x, mi.rcWork.Left + margin, Math.Max(mi.rcWork.Left + margin, mi.rcWork.Right - w - margin));
        y = Math.Clamp(y, mi.rcWork.Top + margin, Math.Max(mi.rcWork.Top + margin, mi.rcWork.Bottom - h - margin));

        _clientW = w;
        _clientH = h;
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, y, w, h, Win32.SWP_NOACTIVATE);
        uint updatedDpi = Win32.GetDpiForWindow(_hwnd);
        if (updatedDpi >= 96 && updatedDpi != _dpi)
        {
            _dpi = updatedDpi;
            ClearFontCache();
        }
        ClampScroll();
    }

    private int S(int px) => Theme.Scale(px, _dpi);
    private float SF(float px) => Theme.ScaleF(px, _dpi);

    private void RequestRepaint()
    {
        if (_hwnd != 0 && Win32.IsWindowVisible(_hwnd))
            Win32.InvalidateRect(_hwnd, null, false);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        var inst = _instance;
        if (inst == null || hwnd != inst._hwnd)
            return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);

        switch (msg)
        {
            case WM_APP_FLYOUT_REFRESH:
                inst.HandleAsyncRefresh();
                return 0;

            case Win32.WM_DPICHANGED:
                inst._dpi = (uint)(wParam & 0xFFFF);
                inst.ClearFontCache();
                inst.PositionAndResize();
                Win32.InvalidateRect(hwnd, null, false);
                return 0;

            case Win32.WM_SIZE:
                inst._clientW = (int)(lParam & 0xFFFF);
                inst._clientH = (int)((lParam >> 16) & 0xFFFF);
                inst.ClampScroll();
                return 0;

            case Win32.WM_ERASEBKGND:
                return 1;

            case Win32.WM_PAINT:
                inst.HandlePaint(hwnd);
                return 0;

            case Win32.WM_MOUSEMOVE:
                inst.HandleMouseMove(lParam);
                return 0;

            case Win32.WM_MOUSELEAVE:
                inst._trackingMouse = false;
                if (inst._hoverId != null && inst._draggingSliderId == null)
                {
                    inst._hoverId = null;
                    inst.RequestRepaint();
                }
                return 0;

            case Win32.WM_LBUTTONDOWN:
                if (inst.HandleLeftButtonDown(lParam))
                    return 0;
                break;

            case Win32.WM_CAPTURECHANGED:
                if (inst._draggingSliderId != null)
                {
                    inst._draggingSliderId = null;
                    inst._draggingSessionId = null;
                    inst.RequestRepaint();
                }
                return 0;

            case Win32.WM_LBUTTONUP:
                inst.HandleLeftButtonUp(lParam);
                return 0;

            case Win32.WM_MOUSEWHEEL:
                short delta = unchecked((short)((long)wParam >> 16));
                inst.HandleMouseWheel(delta);
                return 0;

            case Win32.WM_KEYDOWN:
                if (wParam == Win32.VK_ESCAPE)
                {
                    inst.Hide();
                    return 0;
                }
                break;

            case Win32.WM_ACTIVATE:
                if ((wParam & 0xFFFF) == Win32.WA_INACTIVE)
                {
                    inst._lastDeactivateTick = Environment.TickCount64;
                    inst.Hide();
                }
                return 0;

            case Win32.WM_CLOSE:
                inst.Hide();
                return 0;
        }

        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void HandleMouseMove(nint lParam)
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

        int x = unchecked((short)(lParam & 0xFFFF));
        int y = unchecked((short)((lParam >> 16) & 0xFFFF));

        if (_draggingSliderId != null && _draggingSessionId != null)
        {
            float vol = Math.Clamp((float)(x - _dragTrackLeft) / Math.Max(1, _dragTrackWidth), 0f, 1f);
            _dragPreviewVolume = vol;
            Win32.SetCursor(_cursorHand != 0 ? _cursorHand : _cursorArrow);

            long nowTick = Environment.TickCount64;
            if (nowTick - _lastSliderDispatchTick >= 35)
            {
                _lastSliderDispatchTick = nowTick;
                _ = _engine.SetSessionVolumeAsync(_draggingSessionId, vol);
            }
            RequestRepaint();
            return;
        }

        var slider = FindSliderHit(x, y);
        if (slider != null)
        {
            Win32.SetCursor(_cursorHand != 0 ? _cursorHand : _cursorArrow);
            if (!string.Equals(_hoverId, slider.Id, StringComparison.Ordinal))
            {
                _hoverId = slider.Id;
                RequestRepaint();
            }
            return;
        }

        var hit = FindHit(x, y);
        string? nextHover = hit?.Id;
        Win32.SetCursor(hit != null && hit.IsButton && _cursorHand != 0 ? _cursorHand : _cursorArrow);

        if (!string.Equals(_hoverId, nextHover, StringComparison.Ordinal))
        {
            _hoverId = nextHover;
            RequestRepaint();
        }
    }

    private bool HandleLeftButtonDown(nint lParam)
    {
        int x = unchecked((short)(lParam & 0xFFFF));
        int y = unchecked((short)((lParam >> 16) & 0xFFFF));
        var slider = FindSliderHit(x, y);
        if (slider == null)
            return false;

        float vol = Math.Clamp((float)(x - slider.TrackLeft) / Math.Max(1, slider.TrackWidth), 0f, 1f);
        _draggingSliderId = slider.Id;
        _draggingSessionId = slider.SessionId;
        _dragTrackLeft = slider.TrackLeft;
        _dragTrackWidth = slider.TrackWidth;
        _dragPreviewVolume = vol;
        _lastSliderDispatchTick = Environment.TickCount64;

        Win32.SetCapture(_hwnd);
        _ = _engine.SetSessionVolumeAsync(slider.SessionId, vol);
        RequestRepaint();
        return true;
    }

    private void HandleLeftButtonUp(nint lParam)
    {
        int x = unchecked((short)(lParam & 0xFFFF));
        int y = unchecked((short)((lParam >> 16) & 0xFFFF));

        if (_draggingSliderId != null && _draggingSessionId != null)
        {
            float finalVol = Math.Clamp((float)(x - _dragTrackLeft) / Math.Max(1, _dragTrackWidth), 0f, 1f);
            string targetSession = _draggingSessionId;
            _draggingSliderId = null;
            _draggingSessionId = null;
            Win32.ReleaseCapture();
            _ = _engine.SetSessionVolumeAsync(targetSession, finalVol);
            RequestRepaint();
            return;
        }

        var hit = FindHit(x, y);
        if (hit != null)
        {
            hit.OnClick();
            RequestRepaint();
        }
    }

    private void HandleMouseWheel(short delta)
    {
        int step = S(36);
        _scrollY -= (delta / 120) * step;
        ClampScroll();
        RequestRepaint();
    }

    private void ClampScroll()
    {
        int headerH = S(HeaderHeightPx);
        int viewH = Math.Max(1, _clientH - headerH);
        int maxScroll = Math.Max(0, _contentTotalH - viewH);
        _scrollY = Math.Clamp(_scrollY, 0, maxScroll);
    }

    private HitRegion? FindHit(int x, int y)
    {
        for (int i = _hitRegions.Count - 1; i >= 0; i--)
        {
            var r = _hitRegions[i].Bounds;
            if (x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom)
                return _hitRegions[i];
        }
        return null;
    }

    private SliderRegion? FindSliderHit(int x, int y)
    {
        for (int i = _sliderRegions.Count - 1; i >= 0; i--)
        {
            var r = _sliderRegions[i].Bounds;
            if (x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom)
                return _sliderRegions[i];
        }
        return null;
    }

    private bool EnsureBackBuffer(nint referenceDc, int w, int h)
    {
        if (_backDc != 0 && _backBmp != 0 && _backW == w && _backH == h)
            return true;

        ReleaseBackBuffer();
        nint memDc = Win32.CreateCompatibleDC(referenceDc);
        if (memDc == 0) return false;

        var bmi = new Win32.BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)sizeof(Win32.BITMAPINFOHEADER);
        bmi.bmiHeader.biWidth = w;
        bmi.bmiHeader.biHeight = -h;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;

        nint hBmp = Win32.CreateDIBSection(memDc, &bmi, 0, out _, 0, 0);
        if (hBmp == 0)
        {
            Win32.DeleteDC(memDc);
            return false;
        }

        nint oldBmp = Win32.SelectObject(memDc, hBmp);
        Win32.SetBkMode(memDc, Win32.TRANSPARENT);
        _backDc = memDc;
        _backBmp = hBmp;
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

    private void HandlePaint(nint hwnd)
    {
        SyncDwmStyleIfNeeded();
        var ps = new Win32.PAINTSTRUCT();
        nint hdc = Win32.BeginPaint(hwnd, &ps);
        if (hdc == 0) return;

        int w = Math.Max(1, _clientW);
        int h = Math.Max(1, _clientH);

        try
        {
            if (!EnsureBackBuffer(hdc, w, h))
                return;

            _hitRegions.Clear();
            _sliderRegions.Clear();
            Win32.SetBkMode(_backDc, Win32.TRANSPARENT);

            if (GdiPlus.GdipCreateFromHDC(_backDc, out nint gfx) == 0 && gfx != 0)
            {
                try
                {
                    GdiPlus.GdipSetSmoothingMode(gfx, GdiPlus.SmoothingModeAntiAlias);
                    GdiPlus.GdipGraphicsClear(gfx, GdiPlus.Argb(Theme.BgWindow));

                    int headerH = S(HeaderHeightPx);
                    DrawBody(_backDc, gfx, w, h, headerH);
                    DrawHeader(_backDc, gfx, w, headerH);

                    // Crisp outer border matching the window edge
                    GdiPlus.DrawRoundedRect(gfx, 0.5f, 0.5f, w - 1f, h - 1f, SF(8),
                        GdiPlus.Argb(Theme.BorderStrong, 180), 1f);
                }
                finally
                {
                    GdiPlus.GdipDeleteGraphics(gfx);
                }
            }

            Win32.BitBlt(hdc, 0, 0, w, h, _backDc, 0, 0, Win32.SRCCOPY);
        }
        finally
        {
            Win32.EndPaint(hwnd, &ps);
        }
    }

    private void DrawHeader(nint dc, nint gfx, int w, int headerH)
    {
        GdiPlus.FillRoundedRect(gfx, 0, 0, w, headerH, 0, GdiPlus.Argb(Theme.BgHeader));
        GdiPlus.FillRoundedRect(gfx, 0, headerH - 1, w, 1, 0, GdiPlus.Argb(Theme.BorderSubtle));

        int padX = S(14);
        int iconSz = S(18);
        int iconY = (headerH - iconSz) / 2;
        nint brandIcon = _iconRenderer.GetBrandIcon(iconSz);
        if (brandIcon != 0)
            Win32.DrawIconEx(dc, padX, iconY, brandIcon, iconSz, iconSz, 0, 0, Win32.DI_NORMAL);
        else
            GdiPlus.FillRoundedRect(gfx, padX, iconY, iconSz, iconSz, SF(4), GdiPlus.Argb(Theme.Accent));

        int titleX = padX + iconSz + S(8);
        int btnH = S(26);
        int btnY = (headerH - btnH) / 2;
        int setW = Math.Clamp(MeasureTextWidth(dc, Strings.FlyoutBtnSettings, 10, Win32.FW_SEMIBOLD) + S(18), S(64), S(92));
        int dashW = Math.Clamp(MeasureTextWidth(dc, Strings.FlyoutBtnDashboard, 10, Win32.FW_SEMIBOLD) + S(18), S(74), S(104));
        int setX = w - padX - setW;
        int dashX = setX - S(6) - dashW;

        DrawHeaderButton(dc, gfx, "fly_dash", Strings.FlyoutBtnDashboard, dashX, btnY, dashW, btnH, () =>
        {
            Hide();
            OpenDashboardRequested?.Invoke();
        });
        DrawHeaderButton(dc, gfx, "fly_set", Strings.FlyoutBtnSettings, setX, btnY, setW, btnH, () =>
        {
            Hide();
            OpenSettingsRequested?.Invoke();
        });

        int titleRight = dashX - S(8);
        int titleW = MeasureTextWidth(dc, "Cueway", 13, Win32.FW_BOLD) + S(6);
        int nameEnd = Math.Min(titleRight, titleX + titleW);
        DrawText(dc, "Cueway", titleX, 0, nameEnd, headerH, 13, Win32.FW_BOLD, Theme.TextPrimary);

        if (_snapshot.SoloModeEnabled && nameEnd + S(48) <= titleRight)
        {
            int pillH = S(18);
            int pillW = Math.Max(S(40), MeasureTextWidth(dc, "SOLO", 9, Win32.FW_BOLD) + S(14));
            int pillX = nameEnd + S(4);
            if (pillX + pillW <= titleRight)
            {
                int pillY = (headerH - pillH) / 2;
                GdiPlus.FillRoundedRect(gfx, pillX, pillY, pillW, pillH, pillH / 2f, GdiPlus.Argb(Theme.AmberSoft));
                DrawText(dc, "SOLO", pillX, pillY, pillX + pillW, pillY + pillH, 9, Win32.FW_BOLD, Theme.Amber, centered: true);
            }
        }
    }

    private void DrawHeaderButton(nint dc, nint gfx, string id, string text, int x, int y, int w, int h, Action onClick)
    {
        bool hovered = string.Equals(_hoverId, id, StringComparison.Ordinal);
        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, w, h, SF(6),
            GdiPlus.Argb(hovered ? Theme.BgControlHover : Theme.BgCard),
            GdiPlus.Argb(Theme.BorderStrong));
        DrawText(dc, text, x + S(4), y, x + w - S(4), y + h, 10, Win32.FW_SEMIBOLD,
            hovered ? Theme.Accent : Theme.TextPrimary, centered: true);
        _hitRegions.Add(new HitRegion(new Win32.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h }, id, onClick));
    }

    private void DrawBody(nint dc, nint gfx, int w, int h, int headerH)
    {
        int padX = S(12);
        int padV = S(12);
        int contentW = w - padX * 2;
        int y = headerH + padV - _scrollY;
        int startY = y;

        var sessions = _snapshot.Sessions;
        if (sessions.Count == 0)
        {
            int emptyH = S(86);
            GdiPlus.FillAndStrokeRoundedRect(gfx, padX, y, contentW, emptyH, SF(10),
                GdiPlus.Argb(Theme.BgCard), GdiPlus.Argb(Theme.BorderSubtle));
            DrawText(dc, Strings.FlyoutEmptyTitle, padX + S(14), y + S(20), padX + contentW - S(14), y + S(42),
                12, Win32.FW_SEMIBOLD, Theme.TextPrimary);
            DrawText(dc, Strings.FlyoutEmptySub, padX + S(14), y + S(46), padX + contentW - S(14), y + S(66),
                11, Win32.FW_NORMAL, Theme.TextSecondary);
            _contentTotalH = emptyH + padV * 2;
            return;
        }

        int cardH = S(CardHeightPx);
        int gap = S(CardGapPx);
        for (int i = 0; i < sessions.Count; i++)
        {
            y = DrawSessionCard(dc, gfx, padX, y, contentW, cardH, sessions[i], headerH);
            if (i < sessions.Count - 1)
                y += gap;
        }

        _contentTotalH = (y - startY) + padV * 2;
        ClampScroll();

        int viewH = h - headerH;
        if (_contentTotalH > viewH && viewH > 20)
        {
            float ratio = (float)viewH / _contentTotalH;
            int thumbH = Math.Max(S(24), (int)(viewH * ratio));
            int maxScroll = _contentTotalH - viewH;
            int thumbY = headerH + (int)((viewH - thumbH) * ((float)_scrollY / maxScroll));
            GdiPlus.FillRoundedRect(gfx, w - S(5), thumbY, S(3), thumbH, SF(1.5f), GdiPlus.Argb(Theme.BorderStrong, 180));
        }
    }

    private int DrawSessionCard(
        nint dc, nint gfx, int x, int y, int w, int cardH, MediaSessionInfo s, int headerH)
    {
        bool isTarget = _snapshot.ActiveTarget != null &&
                        string.Equals(_snapshot.ActiveTarget.SessionId, s.SessionId, StringComparison.Ordinal);
        bool isLocked = _snapshot.IsManualLock && isTarget;

        string focusId = $"fly_focus_{s.SessionId}";
        bool focusHovered = string.Equals(_hoverId, focusId, StringComparison.Ordinal);

        uint tint = isLocked ? Theme.Amber : Theme.Accent;
        uint cardBg = isTarget
            ? Theme.BlendRgb(Theme.BgCardElevated, tint, Theme.IsLight ? 0.07f : 0.12f)
            : Theme.BgCard;
        uint border = isTarget ? tint : (focusHovered ? Theme.Accent : Theme.BorderSubtle);

        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, w, cardH, SF(10),
            GdiPlus.Argb(cardBg), GdiPlus.Argb(border, isTarget ? (byte)220 : (byte)255),
            isTarget ? 1.4f : 1f);

        int pad = S(10);
        int icoSz = S(26);
        int icoX = x + pad;
        int icoY = y + S(12);
        DrawAppIconOrMonogram(dc, gfx, s.AppKey, s.ExePath, s.DisplayName, icoX, icoY, icoSz, SF(6));

        int btnSz = S(24);
        int btnY = y + S(13);
        int lockX = x + w - pad - btnSz;
        int nextX = lockX - S(4) - btnSz;
        int playX = nextX - S(4) - btnSz;
        int prevX = playX - S(4) - btnSz;

        int textX = icoX + icoSz + S(10);
        int textRight = prevX - S(8);
        bool isWeb = s.IsBrowser || BrowserSiteScanner.ExtractBrowserKeyFromAppKey(s.AppKey) != null;
        int globeSz = isWeb ? S(11) : 0;
        int globeGap = isWeb ? S(4) : 0;
        int titleMeasured = MeasureTextWidth(dc, s.DisplayName, 12, Win32.FW_SEMIBOLD) + S(4);
        int nameMaxRight = Math.Min(textRight - globeSz - globeGap, textX + titleMeasured);

        DrawText(dc, s.DisplayName, textX, y + S(9), textRight - globeSz - globeGap, y + S(27),
            12, Win32.FW_SEMIBOLD, focusHovered ? Theme.Accent : Theme.TextPrimary);

        if (isWeb && nameMaxRight + globeSz + S(2) <= textRight)
        {
            DrawGlobeGlyph(gfx, nameMaxRight + S(2), y + S(13), globeSz, Theme.TextMuted);
        }

        DrawText(dc, s.FormattedMediaTitle, textX, y + S(28), textRight, y + S(45),
            11, Win32.FW_NORMAL, Theme.TextSecondary);

        GdiPlus.FillRoundedRect(gfx, x + pad, y + S(50), w - pad * 2, Math.Max(1, S(1)), 0f,
            GdiPlus.Argb(Theme.BorderSubtle, 140));

        if (y + cardH > headerH)
        {
            var captured = s;
            RegisterScrollableHit(
                new Win32.RECT { Left = x, Top = y, Right = prevX - S(4), Bottom = y + S(50) },
                focusId,
                () =>
                {
                    _ = _engine.FocusSessionAsync(captured.SessionId);
                    Hide();
                },
                headerH);

            DrawTransportBtn(gfx, $"fly_prev_{s.SessionId}", 0, prevX, btnY, btnSz, false, headerH,
                () => _ = _engine.DispatchMediaActionToSessionAsync(captured.SessionId, MediaActionKind.Previous));
            DrawTransportBtn(gfx, $"fly_play_{s.SessionId}", captured.IsPlaying ? 2 : 1, playX, btnY, btnSz, true, headerH,
                () => _ = _engine.DispatchMediaActionToSessionAsync(captured.SessionId, MediaActionKind.PlayPause));
            DrawTransportBtn(gfx, $"fly_next_{s.SessionId}", 3, nextX, btnY, btnSz, false, headerH,
                () => _ = _engine.DispatchMediaActionToSessionAsync(captured.SessionId, MediaActionKind.Next));
            DrawLockBtn(gfx, $"fly_lock_{s.SessionId}", lockX, btnY, btnSz, isLocked, headerH,
                () => _engine.ToggleManualLockOnActiveOrSelected(captured.SessionId));

            DrawVolumeRow(dc, gfx, $"fly_vol_{s.SessionId}", captured.SessionId, captured.Volume, captured.IsMuted,
                x + pad, y + S(55), w - pad * 2, S(26), headerH);
        }

        return y + cardH;
    }

    private void DrawTransportBtn(nint gfx, string id, int kind, int x, int y, int sz, bool accent, int headerH, Action onClick)
    {
        bool hovered = string.Equals(_hoverId, id, StringComparison.Ordinal);
        uint bg = accent
            ? (hovered ? Theme.AccentHover : Theme.Accent)
            : (hovered ? Theme.BgControlHover : Theme.BgControl);
        uint border = accent ? Theme.Accent : Theme.BorderStrong;
        uint fg = accent ? Theme.OnAccent : Theme.TextPrimary;

        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, sz, sz, SF(6), GdiPlus.Argb(bg), GdiPlus.Argb(border));

        if (GdiPlus.GdipCreateSolidFill(GdiPlus.Argb(fg), out nint brush) == 0 && brush != 0)
        {
            try
            {
                float cx = x + sz / 2f;
                float cy = y + sz / 2f;
                float r = sz * 0.18f;
                switch (kind)
                {
                    case 0: // Previous
                        float barW = MathF.Max(1.8f, sz * 0.075f);
                        GdiPlus.GdipFillRectangle(gfx, brush, cx - r * 0.85f, cy - r * 0.85f, barW, r * 1.7f);
                        FillTriangle(gfx, brush, cx + r * 0.9f, cy - r * 0.85f, cx + r * 0.9f, cy + r * 0.85f, cx - r * 0.45f, cy);
                        break;
                    case 1: // Play
                        FillTriangle(gfx, brush, cx - r * 0.55f, cy - r, cx - r * 0.55f, cy + r, cx + r * 1.1f, cy);
                        break;
                    case 2: // Pause
                        float bw = MathF.Max(2f, sz * 0.085f);
                        float bh = r * 1.85f;
                        GdiPlus.GdipFillRectangle(gfx, brush, cx - bw * 1.35f, cy - bh / 2f, bw, bh);
                        GdiPlus.GdipFillRectangle(gfx, brush, cx + bw * 0.35f, cy - bh / 2f, bw, bh);
                        break;
                    case 3: // Next
                        FillTriangle(gfx, brush, cx - r * 0.9f, cy - r * 0.85f, cx - r * 0.9f, cy + r * 0.85f, cx + r * 0.45f, cy);
                        GdiPlus.GdipFillRectangle(gfx, brush, cx + r * 0.55f, cy - r * 0.85f, MathF.Max(1.8f, sz * 0.075f), r * 1.7f);
                        break;
                }
            }
            finally
            {
                GdiPlus.GdipDeleteBrush(brush);
            }
        }

        RegisterScrollableHit(new Win32.RECT { Left = x, Top = y, Right = x + sz, Bottom = y + sz }, id, onClick, headerH);
    }

    private void DrawLockBtn(nint gfx, string id, int x, int y, int sz, bool isLocked, int headerH, Action onClick)
    {
        bool hovered = string.Equals(_hoverId, id, StringComparison.Ordinal);
        uint bg = isLocked ? Theme.AmberSoft : (hovered ? Theme.BgControlHover : Theme.BgControl);
        uint border = isLocked ? Theme.Amber : Theme.BorderStrong;
        uint fg = isLocked ? Theme.Amber : (hovered ? Theme.TextPrimary : Theme.TextSecondary);

        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, sz, sz, SF(6), GdiPlus.Argb(bg), GdiPlus.Argb(border));

        float cx = x + sz / 2f;
        float cy = y + sz / 2f;
        float bw = sz * 0.42f;
        float bh = sz * 0.34f;
        float bx = cx - bw / 2f;
        float by = cy - bh * 0.15f;

        if (GdiPlus.GdipCreatePen1(GdiPlus.Argb(fg), MathF.Max(1.3f, SF(1.35f)), GdiPlus.UnitPixel, out nint pen) == 0 && pen != 0)
        {
            try
            {
                float sw = bw * 0.62f;
                float sh = sz * 0.24f;
                float sx = cx - sw / 2f;
                float sy = by - sh * 0.65f;
                GdiPlus.GdipDrawArc(gfx, pen, sx, sy, sw, sh * 1.3f, 180f, 180f);
            }
            finally
            {
                GdiPlus.GdipDeletePen(pen);
            }
        }

        GdiPlus.FillRoundedRect(gfx, bx, by, bw, bh, SF(2f), GdiPlus.Argb(fg));
        RegisterScrollableHit(new Win32.RECT { Left = x, Top = y, Right = x + sz, Bottom = y + sz }, id, onClick, headerH);
    }

    private void DrawVolumeRow(
        nint dc, nint gfx, string idPrefix, string sessionId,
        float volume, bool isMuted, int x, int y, int w, int h, int headerH)
    {
        int muteSz = S(22);
        int muteX = x;
        int muteY = y + (h - muteSz) / 2;
        string muteId = $"{idPrefix}_mute";
        bool muteHovered = string.Equals(_hoverId, muteId, StringComparison.Ordinal);

        uint muteBg = isMuted ? Theme.RedSoft : (muteHovered ? Theme.BgControlHover : Theme.BgControl);
        uint muteBorder = isMuted ? Theme.Red : Theme.BorderSubtle;
        uint muteFg = isMuted ? Theme.Red : Theme.TextPrimary;
        GdiPlus.FillAndStrokeRoundedRect(gfx, muteX, muteY, muteSz, muteSz, SF(5),
            GdiPlus.Argb(muteBg), GdiPlus.Argb(muteBorder));
        DrawSpeakerGlyph(gfx, muteX, muteY, muteSz, isMuted, muteFg);

        RegisterScrollableHit(
            new Win32.RECT { Left = muteX, Top = muteY, Right = muteX + muteSz, Bottom = muteY + muteSz },
            muteId,
            () => _ = _engine.ToggleSessionMuteAsync(sessionId),
            headerH);

        int pctW = S(36);
        int pctX = x + w - pctW;
        int trackX = muteX + muteSz + S(8);
        int trackRight = pctX - S(6);
        int trackW = Math.Max(S(40), trackRight - trackX);
        int trackH = S(5);
        int trackY = y + (h - trackH) / 2;

        string sliderId = $"{idPrefix}_slider";
        bool isDraggingThis = string.Equals(_draggingSliderId, sliderId, StringComparison.Ordinal);
        bool sliderHovered = isDraggingThis || string.Equals(_hoverId, sliderId, StringComparison.Ordinal);

        float dispVol = isDraggingThis
            ? _dragPreviewVolume
            : (isMuted ? 0f : Math.Clamp(volume, 0f, 1f));

        GdiPlus.FillRoundedRect(gfx, trackX, trackY, trackW, trackH, trackH / 2f, GdiPlus.Argb(Theme.BgControl));
        float fillW = trackW * dispVol;
        if (fillW > 1f)
        {
            uint fillColor = (isMuted && !isDraggingThis) ? Theme.Red : Theme.Accent;
            GdiPlus.FillRoundedRect(gfx, trackX, trackY, MathF.Max(trackH, fillW), trackH, trackH / 2f, GdiPlus.Argb(fillColor));
        }

        float thumbD = sliderHovered ? S(12) : S(10);
        float cx = trackX + trackW * dispVol;
        float cy = y + h / 2f;
        if (GdiPlus.GdipCreateSolidFill(GdiPlus.Argb(Theme.SwitchKnob), out nint kBr) == 0 && kBr != 0)
        {
            try { GdiPlus.GdipFillEllipse(gfx, kBr, cx - thumbD / 2f, cy - thumbD / 2f, thumbD, thumbD); }
            finally { GdiPlus.GdipDeleteBrush(kBr); }
        }
        if (GdiPlus.GdipCreatePen1(GdiPlus.Argb(isDraggingThis ? Theme.Accent : Theme.BorderStrong),
            MathF.Max(1.1f, SF(1.1f)), GdiPlus.UnitPixel, out nint kPen) == 0 && kPen != 0)
        {
            try { GdiPlus.GdipDrawEllipse(gfx, kPen, cx - thumbD / 2f, cy - thumbD / 2f, thumbD, thumbD); }
            finally { GdiPlus.GdipDeletePen(kPen); }
        }

        int hitTop = Math.Max(headerH, y);
        if (y + h > hitTop)
        {
            _sliderRegions.Add(new SliderRegion(
                new Win32.RECT { Left = trackX - S(4), Top = hitTop, Right = trackX + trackW + S(4), Bottom = y + h },
                sliderId,
                sessionId,
                trackX,
                trackW));
        }

        int dispPct = (int)MathF.Round(dispVol * 100f);
        DrawText(dc, Strings.FormatPercent(dispPct), pctX, y, pctX + pctW, y + h, 10, Win32.FW_SEMIBOLD,
            isMuted && !isDraggingThis ? Theme.Red : Theme.TextSecondary, centered: true);
    }

    private void RegisterScrollableHit(Win32.RECT bounds, string id, Action onClick, int headerH)
    {
        if (bounds.Top < headerH)
            bounds.Top = headerH;
        if (bounds.Bottom > bounds.Top && bounds.Right > bounds.Left)
            _hitRegions.Add(new HitRegion(bounds, id, onClick));
    }

    private void DrawAppIconOrMonogram(
        nint dc, nint gfx, string? appKey, string? exePath, string? displayName,
        int x, int y, int size, float radius)
    {
        nint hIcon = _iconRenderer.GetIconForApp(appKey, exePath);
        if (hIcon != 0)
        {
            Win32.DrawIconEx(dc, x, y, hIcon, size, size, 0, 0, Win32.DI_NORMAL);
            return;
        }

        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, size, size, radius,
            GdiPlus.Argb(Theme.AccentSoft), GdiPlus.Argb(Theme.Accent, 95), 1f);

        string glyph = "♪";
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            foreach (char ch in displayName.Trim())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    glyph = char.ToUpperInvariant(ch).ToString();
                    break;
                }
            }
        }

        int ptSize = Math.Max(9, (int)MathF.Round(size / (_dpi / 96f) * 0.44f));
        DrawText(dc, glyph, x, y, x + size, y + size, ptSize, Win32.FW_BOLD, Theme.Accent, centered: true);
    }

    private void DrawSpeakerGlyph(nint gfx, int x, int y, int sz, bool isMuted, uint colorRgb)
    {
        float cx = x + sz / 2f;
        float cy = y + sz / 2f;
        float u = sz * 0.22f;

        if (GdiPlus.GdipCreateSolidFill(GdiPlus.Argb(colorRgb), out nint brush) == 0 && brush != 0)
        {
            try
            {
                if (GdiPlus.GdipCreatePath(GdiPlus.FillModeAlternate, out nint path) == 0 && path != 0)
                {
                    try
                    {
                        float x0 = cx - u * 1.15f;
                        float x1 = cx - u * 0.55f;
                        float x2 = cx + u * 0.12f;
                        GdiPlus.GdipAddPathLine(path, x0, cy - u * 0.42f, x1, cy - u * 0.42f);
                        GdiPlus.GdipAddPathLine(path, x1, cy - u * 0.42f, x2, cy - u * 0.95f);
                        GdiPlus.GdipAddPathLine(path, x2, cy - u * 0.95f, x2, cy + u * 0.95f);
                        GdiPlus.GdipAddPathLine(path, x2, cy + u * 0.95f, x1, cy + u * 0.42f);
                        GdiPlus.GdipAddPathLine(path, x1, cy + u * 0.42f, x0, cy + u * 0.42f);
                        GdiPlus.GdipClosePathFigure(path);
                        GdiPlus.GdipFillPath(gfx, brush, path);
                    }
                    finally
                    {
                        GdiPlus.GdipDeletePath(path);
                    }
                }
            }
            finally
            {
                GdiPlus.GdipDeleteBrush(brush);
            }
        }

        if (GdiPlus.GdipCreatePen1(GdiPlus.Argb(colorRgb), MathF.Max(1.2f, SF(1.25f)), GdiPlus.UnitPixel, out nint pen) == 0 && pen != 0)
        {
            try
            {
                GdiPlus.GdipSetPenStartCap(pen, GdiPlus.LineCapRound);
                GdiPlus.GdipSetPenEndCap(pen, GdiPlus.LineCapRound);

                if (isMuted)
                {
                    float mx1 = cx + u * 0.48f;
                    float mx2 = cx + u * 1.15f;
                    GdiPlus.GdipDrawLine(gfx, pen, mx1, cy - u * 0.42f, mx2, cy + u * 0.42f);
                    GdiPlus.GdipDrawLine(gfx, pen, mx2, cy - u * 0.42f, mx1, cy + u * 0.42f);
                }
                else
                {
                    GdiPlus.GdipDrawArc(gfx, pen, cx - u * 0.25f, cy - u * 0.55f, u * 0.95f, u * 1.1f, -50f, 100f);
                    GdiPlus.GdipDrawArc(gfx, pen, cx - u * 0.45f, cy - u * 0.9f, u * 1.6f, u * 1.8f, -50f, 100f);
                }
            }
            finally
            {
                GdiPlus.GdipDeletePen(pen);
            }
        }
    }

    private void DrawGlobeGlyph(nint gfx, int x, int y, int sz, uint colorRgb)
    {
        if (GdiPlus.GdipCreatePen1(GdiPlus.Argb(colorRgb), MathF.Max(1.05f, SF(1.1f)), GdiPlus.UnitPixel, out nint pen) != 0 || pen == 0)
            return;

        try
        {
            float pad = sz * 0.10f;
            float left = x + pad;
            float top = y + pad;
            float d = MathF.Max(4f, sz - pad * 2f);
            float midX = left + d * 0.5f;
            float midY = top + d * 0.5f;
            float mw = d * 0.46f;

            GdiPlus.GdipDrawEllipse(gfx, pen, left, top, d, d);
            GdiPlus.GdipDrawLine(gfx, pen, left, midY, left + d, midY);
            GdiPlus.GdipDrawEllipse(gfx, pen, midX - mw * 0.5f, top, mw, d);
        }
        finally
        {
            GdiPlus.GdipDeletePen(pen);
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

    private nint GetOrCreateFont(int ptSize, int weight)
    {
        var (face, actualWeight) = Strings.ResolveFont(weight);
        var key = (ptSize, actualWeight, face, _dpi);
        if (_fontCache.TryGetValue(key, out nint cached) && cached != 0)
            return cached;

        if (_fontCache.Count >= 32)
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
            var rc = new Win32.RECT { Left = 0, Top = 0, Right = 1200, Bottom = S(32) };
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
        _engine.StateChanged -= OnStateChanged;
        _stateStore.StateChanged -= OnStoreChanged;

        if (_hwnd != 0)
        {
            Win32.DestroyWindow(_hwnd);
            _hwnd = 0;
        }
        ClearFontCache();
        ReleaseBackBuffer();
        if (ReferenceEquals(_instance, this))
            _instance = null;
    }
}
