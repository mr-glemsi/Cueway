using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cueway.Config;
using Cueway.Interop;
using Cueway.Localization;

namespace Cueway.UI;

internal sealed unsafe class ThemeEditorWindow : IDisposable
{
    private const string ClassName = "Cueway_ThemeStudioDialog";

    private sealed record HitRegion(Win32.RECT Bounds, string Id, Action OnClick, bool IsButton = true);

    private static readonly uint[] AccentQuickSwatches =
    [
        0x4F9CF9, 0x38BDF8, 0x10B981, 0xA78BFA,
        0xF472B6, 0xF43F5E, 0xF59E0B, 0xEF4444
    ];

    private static readonly uint[] BgQuickSwatches =
    [
        0x080A0F, 0x111318, 0x0B1420, 0x161311,
        0x18131A, 0xF3F5F9
    ];

    private static ThemeEditorWindow? _instance;

    private readonly nint _ownerHwnd;
    private readonly StateStore _stateStore;
    private readonly Action _onPreviewChanged;
    private readonly Action<string, bool>? _onStatusBanner;
    private readonly List<HitRegion> _hitRegions = [];
    private readonly Dictionary<(int PtSize, int Weight, string Face, uint Dpi), nint> _fontCache = new();

    private nint _hwnd;
    private nint _editHwnd;
    private nint _editFont;
    private nint _editBgBrush;
    private uint _editBgBrushRgb = uint.MaxValue;
    private nint _cursorArrow;
    private nint _cursorHand;
    private nint _backDc;
    private nint _backBmp;
    private nint _backOldBmp;
    private int _backW;
    private int _backH;
    private uint _dpi = 96;
    private int _clientW = 480;
    private int _clientH = 590;

    private string? _editingThemeId;
    private uint _bgRgb = 0x13161F;
    private uint _cardRgb = 0x1C212E;
    private uint _accentRgb = 0x6366F1;
    private bool _autoCard = true;

    private string? _hoverId;
    private bool _trackingMouse;
    private bool _disposed;

    public ThemeEditorWindow(
        nint ownerHwnd,
        StateStore stateStore,
        Action onPreviewChanged,
        Action<string, bool>? onStatusBanner = null)
    {
        _ownerHwnd = ownerHwnd;
        _stateStore = stateStore;
        _onPreviewChanged = onPreviewChanged;
        _onStatusBanner = onStatusBanner;
        _instance = this;

        CreateStudioWindow();
    }

    private void CreateStudioWindow()
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

        int initW = Theme.Scale(496, _dpi);
        int initH = Theme.Scale(628, _dpi);

        _hwnd = Win32.CreateWindowExW(
            Win32.WS_EX_TOOLWINDOW,
            ClassName,
            Strings.ThemeStudioTitle,
            Win32.WS_OVERLAPPED | Win32.WS_CAPTION | Win32.WS_SYSMENU | Win32.WS_CLIPCHILDREN,
            0, 0, initW, initH,
            _ownerHwnd, 0, hInst, 0);

        if (_hwnd != 0)
        {
            uint winDpi = Win32.GetDpiForWindow(_hwnd);
            if (winDpi >= 96) _dpi = winDpi;

            _editHwnd = Win32.CreateWindowExW(
                0,
                "EDIT",
                string.Empty,
                Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_TABSTOP | Win32.ES_AUTOHSCROLL,
                S(32), S(104), S(416), S(24),
                _hwnd, 101, hInst, 0);

            if (_editHwnd != 0)
            {
                Win32.SendMessageW(_editHwnd, Win32.EM_SETLIMITTEXT, 28, 0);
                UpdateEditControlStyle();
            }

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
        Win32.SetWindowPos(_hwnd, 0, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);
        Win32.RedrawWindow(_hwnd, null, 0, Win32.RDW_FRAME | Win32.RDW_INVALIDATE | Win32.RDW_UPDATENOW);
    }

    private void UpdateEditControlStyle()
    {
        if (_editHwnd == 0) return;

        if (_editFont != 0)
        {
            Win32.DeleteObject(_editFont);
            _editFont = 0;
        }
        var (editFace, editWeight) = Strings.ResolveFont(Win32.FW_SEMIBOLD);
        _editFont = Win32.CreateFontW(
            -S(13), 0, 0, 0, editWeight, 0, 0, 0,
            1, 0, 0, Win32.CLEARTYPE_QUALITY, 0, editFace);
        if (_editFont != 0)
        {
            Win32.SendMessageW(_editHwnd, Win32.WM_SETFONT, (nuint)_editFont, 1);
        }

        uint targetBg = Theme.BgControl;
        if (_editBgBrush == 0 || _editBgBrushRgb != targetBg)
        {
            if (_editBgBrush != 0)
            {
                Win32.DeleteObject(_editBgBrush);
                _editBgBrush = 0;
            }
            _editBgBrush = Win32.CreateSolidBrush(Theme.ToColorRef(targetBg));
            _editBgBrushRgb = targetBg;
        }
        Win32.InvalidateRect(_editHwnd, null, true);
    }

    public void RefreshLocalizationAndTheme()
    {
        if (_disposed || _hwnd == 0) return;
        ClearFontCache();
        UpdateEditControlStyle();
        ApplyDwmTheme();
        Win32.SetWindowTextW(_hwnd, Strings.ThemeStudioTitle);
        RequestRepaint();
    }

    public void ShowCreate()
    {
        if (_disposed || _hwnd == 0) return;

        Theme.SetPreviewPalette(null);
        var seed = Theme.Current;
        _editingThemeId = null;
        _bgRgb = seed.BgWindow;
        _cardRgb = seed.BgCard;
        _accentRgb = seed.Accent;
        _autoCard = false;

        if (_editHwnd != 0)
        {
            string baseName = Strings.ThemeDefaultCustomName;
            int n = _stateStore.GetSettingsSnapshot().CustomThemes.Count + 1;
            Win32.SetWindowTextW(_editHwnd, n > 1 ? $"{baseName} {n}" : baseName);
        }

        ApplyLivePreview();
        ShowCenteredOverOwner();
    }

    public void ShowEdit(CustomThemePreset preset)
    {
        if (_disposed || _hwnd == 0) return;

        _editingThemeId = preset.Id;
        _bgRgb = Theme.TryParseHex(preset.BgHex, out uint b) ? b : 0x13161Fu;
        _cardRgb = Theme.TryParseHex(preset.CardHex, out uint c) ? c : Theme.SuggestCardColor(_bgRgb);
        _accentRgb = Theme.TryParseHex(preset.AccentHex, out uint a) ? a : 0x6366F1u;
        _autoCard = _cardRgb == Theme.SuggestCardColor(_bgRgb);

        if (_editHwnd != 0)
            Win32.SetWindowTextW(_editHwnd, preset.Name);

        ApplyLivePreview();
        ShowCenteredOverOwner();
    }

    private void ShowCenteredOverOwner()
    {
        Win32.RECT targetRect;
        if (_ownerHwnd != 0 && Win32.GetWindowRect(_ownerHwnd, out var ownerRc))
            targetRect = ownerRc;
        else
        {
            Win32.GetCursorPos(out var pt);
            nint mon = Win32.MonitorFromPoint(pt, Win32.MONITOR_DEFAULTTONEAREST);
            var mi = new Win32.MONITORINFO { cbSize = (uint)sizeof(Win32.MONITORINFO) };
            Win32.GetMonitorInfoW(mon, &mi);
            targetRect = mi.rcWork;
        }

        Win32.GetWindowRect(_hwnd, out var dlgRc);
        int w = dlgRc.Width > 0 ? dlgRc.Width : Theme.Scale(496, _dpi);
        int h = dlgRc.Height > 0 ? dlgRc.Height : Theme.Scale(628, _dpi);
        int x = targetRect.Left + (targetRect.Width - w) / 2;
        int y = targetRect.Top + (targetRect.Height - h) / 2;

        Win32.SetWindowPos(_hwnd, 0, x, y, w, h, Win32.SWP_NOZORDER | Win32.SWP_SHOWWINDOW);
        Win32.TryFocusWindow(_hwnd);
        if (_editHwnd != 0)
        {
            PositionEditControl();
            Win32.SendMessageW(_editHwnd, 0x00B1, 0, unchecked((nint)(-1))); // EM_SETSEL all
        }
        RequestRepaint();
    }

    private ThemePalette BuildPreviewPalette()
    {
        string name = ReadNameInput();
        if (string.IsNullOrWhiteSpace(name))
            name = Strings.ThemeDefaultCustomName;
        return Theme.GenerateCustomPalette(_editingThemeId ?? "custom:preview", name, _bgRgb, _cardRgb, _accentRgb);
    }

    private void ApplyLivePreview()
    {
        var palette = BuildPreviewPalette();
        Theme.SetPreviewPalette(palette);
        UpdateEditControlStyle();
        ApplyDwmTheme();
        _onPreviewChanged();
        RequestRepaint();
    }

    private void CloseAndRevert()
    {
        Theme.SetPreviewPalette(null);
        _onPreviewChanged();
        if (_hwnd != 0)
            Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
    }

    private void SaveAndClose()
    {
        string name = ReadNameInput();
        Theme.SetPreviewPalette(null);
        var saved = _stateStore.SaveCustomTheme(_editingThemeId, name, _bgRgb, _cardRgb, _accentRgb, activate: true);
        _onPreviewChanged();
        _onStatusBanner?.Invoke(Strings.MsgCustomThemeSaved(saved.Name), false);
        if (_hwnd != 0)
            Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
    }

    private void DeleteAndClose()
    {
        if (string.IsNullOrWhiteSpace(_editingThemeId))
            return;
        string name = ReadNameInput();
        Theme.SetPreviewPalette(null);
        _stateStore.DeleteCustomTheme(_editingThemeId);
        _onPreviewChanged();
        if (!string.IsNullOrWhiteSpace(name))
            _onStatusBanner?.Invoke(Strings.MsgCustomThemeDeleted(name), false);
        if (_hwnd != 0)
            Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
    }

    private string ReadNameInput()
    {
        if (_editHwnd == 0)
            return Strings.ThemeDefaultCustomName;
        char* buf = stackalloc char[64];
        int len = Win32.GetWindowTextW(_editHwnd, buf, 64);
        if (len <= 0)
            return string.Empty;
        return new string(buf, 0, len).Trim();
    }

    private void PositionEditControl()
    {
        if (_editHwnd == 0) return;
        int w = Math.Max(120, _clientW - S(64));
        Win32.SetWindowPos(_editHwnd, 0, S(32), S(105), w, S(22), Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    private void RequestRepaint()
    {
        if (_hwnd != 0)
            Win32.InvalidateRect(_hwnd, null, false);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        var inst = _instance;
        if (inst == null)
            return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);

        switch (msg)
        {
            case Win32.WM_SIZE:
                inst._clientW = (int)(lParam & 0xFFFF);
                inst._clientH = (int)((lParam >> 16) & 0xFFFF);
                inst.PositionEditControl();
                inst.RequestRepaint();
                return 0;

            case Win32.WM_DPICHANGED:
                uint newDpi = (uint)(wParam & 0xFFFF);
                if (newDpi >= 96 && newDpi != inst._dpi)
                {
                    inst._dpi = newDpi;
                    inst.ClearFontCache();
                    inst.UpdateEditControlStyle();
                    inst.PositionEditControl();
                    inst.RequestRepaint();
                }
                return 0;

            case Win32.WM_CTLCOLOREDIT:
                if (lParam == inst._editHwnd && inst._editBgBrush != 0)
                {
                    Win32.SetTextColor((nint)wParam, Theme.ToColorRef(Theme.TextPrimary));
                    Win32.SetBkColor((nint)wParam, Theme.ToColorRef(Theme.BgControl));
                    return inst._editBgBrush;
                }
                break;

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
                if (inst._hoverId != null)
                {
                    inst._hoverId = null;
                    inst.RequestRepaint();
                }
                return 0;

            case Win32.WM_LBUTTONUP:
                inst.HandleLeftClick(lParam);
                return 0;

            case Win32.WM_KEYDOWN:
                if (wParam == Win32.VK_ESCAPE)
                {
                    inst.CloseAndRevert();
                    return 0;
                }
                break;

            case Win32.WM_CLOSE:
                inst.CloseAndRevert();
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
        HitRegion? hit = FindHit(x, y);
        string? nextHover = hit?.Id;
        Win32.SetCursor(hit != null && hit.IsButton && _cursorHand != 0 ? _cursorHand : _cursorArrow);

        if (!string.Equals(_hoverId, nextHover, StringComparison.Ordinal))
        {
            _hoverId = nextHover;
            RequestRepaint();
        }
    }

    private void HandleLeftClick(nint lParam)
    {
        int x = unchecked((short)(lParam & 0xFFFF));
        int y = unchecked((short)((lParam >> 16) & 0xFFFF));
        FindHit(x, y)?.OnClick();
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

    private void HandlePaint(nint hwnd)
    {
        Win32.GetClientRect(hwnd, out var rc);
        int w = Math.Max(1, rc.Width);
        int h = Math.Max(1, rc.Height);
        _clientW = w;
        _clientH = h;

        EnsureBackBuffer(w, h);
        if (_backDc == 0) return;

        DrawScene(_backDc, w, h);

        Win32.PAINTSTRUCT ps;
        nint screenDc = Win32.BeginPaint(hwnd, &ps);
        Win32.BitBlt(screenDc, 0, 0, w, h, _backDc, 0, 0, Win32.SRCCOPY);
        Win32.EndPaint(hwnd, &ps);
    }

    private void DrawScene(nint dc, int w, int h)
    {
        _hitRegions.Clear();
        if (GdiPlus.GdipCreateFromHDC(dc, out nint gfx) != 0 || gfx == 0)
            return;

        try
        {
            GdiPlus.GdipSetSmoothingMode(gfx, GdiPlus.SmoothingModeAntiAlias);
            GdiPlus.GdipSetPixelOffsetMode(gfx, GdiPlus.PixelOffsetModeHighQuality);
            GdiPlus.GdipGraphicsClear(gfx, GdiPlus.Argb(Theme.BgWindow));

            int pad = S(20);
            int contentW = w - pad * 2;
            int y = S(16);

            // Header
            DrawText(dc, Strings.ThemeStudioTitle, pad, y, w - pad, y + S(26), 17, Win32.FW_BOLD, Theme.TextPrimary);
            y += S(25);
            DrawText(dc, Strings.ThemeStudioSub, pad, y, w - pad, y + S(20), 11, Win32.FW_NORMAL, Theme.TextSecondary);
            y += S(26);

            // Name input card
            DrawText(dc, Strings.ThemeFieldName, pad, y, w - pad, y + S(18), 11, Win32.FW_SEMIBOLD, Theme.TextSecondary);
            y += S(22);
            int nameBoxH = S(36);
            GdiPlus.FillAndStrokeRoundedRect(gfx, pad, y, contentW, nameBoxH, SF(8),
                GdiPlus.Argb(Theme.BgControl), GdiPlus.Argb(Theme.BorderStrong));
            PositionEditControl();
            y += nameBoxH + S(14);

            // Accent Color row
            y = DrawColorPickerRow(
                dc, gfx, pad, y, contentW,
                id: "col_accent",
                label: Strings.ThemeFieldAccent,
                rgb: _accentRgb,
                swatches: AccentQuickSwatches,
                extraButton: null,
                onPick: () =>
                {
                    if (Win32.TryPickColor(_hwnd, _accentRgb, out uint picked))
                    {
                        _accentRgb = picked;
                        ApplyLivePreview();
                    }
                },
                onSwatch: sw =>
                {
                    _accentRgb = sw;
                    ApplyLivePreview();
                });
            y += S(10);

            // Background Color row
            y = DrawColorPickerRow(
                dc, gfx, pad, y, contentW,
                id: "col_bg",
                label: Strings.ThemeFieldBg,
                rgb: _bgRgb,
                swatches: BgQuickSwatches,
                extraButton: null,
                onPick: () =>
                {
                    if (Win32.TryPickColor(_hwnd, _bgRgb, out uint picked))
                    {
                        _bgRgb = picked;
                        if (_autoCard)
                            _cardRgb = Theme.SuggestCardColor(_bgRgb);
                        ApplyLivePreview();
                    }
                },
                onSwatch: sw =>
                {
                    _bgRgb = sw;
                    if (_autoCard)
                        _cardRgb = Theme.SuggestCardColor(_bgRgb);
                    ApplyLivePreview();
                });
            y += S(10);

            // Card Surface Color row
            y = DrawColorPickerRow(
                dc, gfx, pad, y, contentW,
                id: "col_card",
                label: Strings.ThemeFieldCard,
                rgb: _cardRgb,
                swatches: [],
                extraButton: (Strings.BtnAutoCard, _autoCard, () =>
                {
                    _autoCard = !_autoCard;
                    if (_autoCard)
                        _cardRgb = Theme.SuggestCardColor(_bgRgb);
                    ApplyLivePreview();
                }),
                onPick: () =>
                {
                    if (Win32.TryPickColor(_hwnd, _cardRgb, out uint picked))
                    {
                        _autoCard = false;
                        _cardRgb = picked;
                        ApplyLivePreview();
                    }
                },
                onSwatch: _ => { });
            y += S(14);

            // Live mini preview
            DrawText(dc, Strings.ThemePreviewHeading, pad, y, w - pad, y + S(18), 10, Win32.FW_BOLD, Theme.TextMuted);
            y += S(22);
            DrawMiniPreviewCard(dc, gfx, pad, y, contentW);

            // Footer buttons pinned at bottom
            int btnH = S(36);
            int footY = h - pad - btnH;
            int saveW = S(142);
            int cancelW = S(92);
            int saveX = w - pad - saveW;
            int cancelX = saveX - S(8) - cancelW;

            DrawActionButton(dc, gfx, "studio_save", Strings.BtnSaveTheme, saveX, footY, saveW, btnH,
                Theme.Accent, Theme.AccentHover, Theme.OnAccent, Theme.Accent, SaveAndClose);

            DrawActionButton(dc, gfx, "studio_cancel", Strings.BtnCancel, cancelX, footY, cancelW, btnH,
                Theme.BgControl, Theme.BgControlHover, Theme.TextPrimary, Theme.BorderStrong, CloseAndRevert);

            if (!string.IsNullOrWhiteSpace(_editingThemeId))
            {
                int delW = S(78);
                DrawActionButton(dc, gfx, "studio_delete", Strings.BtnDeleteTheme, pad, footY, delW, btnH,
                    Theme.RedSoft, Theme.RedSoft, Theme.Red, Theme.Red, DeleteAndClose);
            }
        }
        finally
        {
            GdiPlus.GdipDeleteGraphics(gfx);
        }
    }

    private int DrawColorPickerRow(
        nint dc, nint gfx, int x, int y, int w,
        string id, string label, uint rgb, uint[] swatches,
        (string Label, bool Active, Action OnClick)? extraButton,
        Action onPick, Action<uint> onSwatch)
    {
        bool hasSwatches = swatches.Length > 0;
        int rowH = hasSwatches ? S(82) : S(54);

        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, w, rowH, SF(10),
            GdiPlus.Argb(Theme.BgCard), GdiPlus.Argb(Theme.BorderSubtle));

        int pad = S(14);
        int swSz = S(26);
        int swY = y + S(14);
        GdiPlus.FillAndStrokeRoundedRect(gfx, x + pad, swY, swSz, swSz, SF(7),
            GdiPlus.Argb(rgb), GdiPlus.Argb(Theme.BorderStrong), 1.4f);

        int pickW = S(74);
        int pickH = S(28);
        int pickX = x + w - pad - pickW;
        int pickY = y + S(13);

        DrawActionButton(dc, gfx, $"{id}_pick", Strings.BtnPickColor, pickX, pickY, pickW, pickH,
            Theme.BgControl, Theme.BgControlHover, Theme.TextPrimary, Theme.BorderStrong, onPick);

        int rightBound = pickX - S(8);
        if (extraButton.HasValue)
        {
            int autoW = S(76);
            int autoX = rightBound - autoW;
            var extra = extraButton.Value;
            DrawActionButton(dc, gfx, $"{id}_auto", extra.Label, autoX, pickY, autoW, pickH,
                extra.Active ? Theme.AccentSoft : Theme.BgControl,
                extra.Active ? Theme.AccentSoft : Theme.BgControlHover,
                extra.Active ? Theme.Accent : Theme.TextSecondary,
                extra.Active ? Theme.Accent : Theme.BorderSubtle,
                extra.OnClick);
            rightBound = autoX - S(8);
        }

        int hexW = S(68);
        int hexX = rightBound - hexW;
        DrawText(dc, Theme.FormatHex(rgb), hexX, y + S(14), rightBound, y + S(38), 11, Win32.FW_SEMIBOLD, Theme.TextSecondary);

        int textX = x + pad + swSz + S(10);
        DrawText(dc, label, textX, y + S(14), hexX - S(6), y + S(38), 12, Win32.FW_SEMIBOLD, Theme.TextPrimary);

        _hitRegions.Add(new HitRegion(
            new Win32.RECT { Left = x + pad, Top = swY, Right = x + pad + swSz, Bottom = swY + swSz },
            $"{id}_swatch_box",
            onPick));

        if (hasSwatches)
        {
            int chipSz = S(20);
            int chipGap = S(8);
            int chipX = textX;
            int chipY = y + S(48);
            for (int i = 0; i < swatches.Length; i++)
            {
                uint sw = swatches[i];
                bool selected = (sw & 0xFFFFFF) == (rgb & 0xFFFFFF);
                string chipId = $"{id}_chip_{i}";
                bool hovered = string.Equals(_hoverId, chipId, StringComparison.Ordinal);

                GdiPlus.FillAndStrokeRoundedRect(gfx, chipX, chipY, chipSz, chipSz, SF(6),
                    GdiPlus.Argb(sw),
                    GdiPlus.Argb(selected || hovered ? Theme.TextPrimary : Theme.BorderStrong),
                    selected ? 2.0f : 1.0f);

                uint capturedSw = sw;
                _hitRegions.Add(new HitRegion(
                    new Win32.RECT { Left = chipX, Top = chipY, Right = chipX + chipSz, Bottom = chipY + chipSz },
                    chipId,
                    () => onSwatch(capturedSw)));

                chipX += chipSz + chipGap;
            }
        }

        return y + rowH;
    }

    private void DrawMiniPreviewCard(nint dc, nint gfx, int x, int y, int w)
    {
        var p = Theme.Current;
        int cardH = S(78);
        uint rowBg = Theme.BlendRgb(p.BgCardElevated, p.Accent, p.IsLight ? 0.08f : 0.13f);

        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, w, cardH, SF(10),
            GdiPlus.Argb(rowBg), GdiPlus.Argb(p.Accent, 235), 1.6f);
        GdiPlus.FillRoundedRect(gfx, x + S(4), y + S(10), S(3), cardH - S(20), SF(2), GdiPlus.Argb(p.Accent, 245));

        int iconX = x + S(14);
        int iconY = y + (cardH - S(34)) / 2;
        GdiPlus.FillRoundedRect(gfx, iconX, iconY, S(34), S(34), SF(8), GdiPlus.Argb(p.AccentSoft));
        GdiPlus.FillRoundedRect(gfx, iconX + S(10), iconY + S(10), S(14), S(14), SF(4), GdiPlus.Argb(p.Accent));

        int pillW = S(84);
        int pillH = S(22);
        int pillX = x + w - S(14) - pillW;
        GdiPlus.FillRoundedRect(gfx, pillX, y + S(14), pillW, pillH, pillH / 2f, GdiPlus.Argb(p.GreenSoft));
        DrawText(dc, Strings.StatusPlaying, pillX, y + S(14), pillX + pillW, y + S(14) + pillH, 10, Win32.FW_SEMIBOLD, p.Green, centered: true);

        int barW = S(84);
        int barY = y + S(48);
        GdiPlus.FillRoundedRect(gfx, pillX, barY, barW, S(6), SF(3), GdiPlus.Argb(p.BgControl));
        GdiPlus.FillRoundedRect(gfx, pillX, barY, (int)(barW * 0.68f), S(6), SF(3), GdiPlus.Argb(p.Accent));

        int textX = iconX + S(44);
        int prioW = S(74);
        DrawText(dc, "YouTube", textX, y + S(14), textX + S(74), y + S(36), 13, Win32.FW_BOLD, p.TextPrimary);
        GdiPlus.FillRoundedRect(gfx, textX + S(78), y + S(14), prioW, pillH, pillH / 2f, GdiPlus.Argb(p.AccentSoft));
        DrawText(dc, Strings.BadgePriorityTarget, textX + S(78), y + S(14), textX + S(78) + prioW, y + S(14) + pillH,
            10, Win32.FW_SEMIBOLD, p.Accent, centered: true);

        DrawText(dc, "Cueway Companion • 100%", textX, y + S(40), pillX - S(10), y + S(62), 11, Win32.FW_NORMAL, p.TextSecondary);
    }

    private void DrawActionButton(
        nint dc, nint gfx, string id, string text, int x, int y, int w, int h,
        uint bgNormal, uint bgHover, uint fg, uint border, Action onClick)
    {
        bool hovered = string.Equals(_hoverId, id, StringComparison.Ordinal);
        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, w, h, SF(7),
            GdiPlus.Argb(hovered ? bgHover : bgNormal),
            GdiPlus.Argb(border));
        DrawText(dc, text, x, y, x + w, y + h, 12, Win32.FW_SEMIBOLD, fg, centered: true);
        _hitRegions.Add(new HitRegion(new Win32.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h }, id, onClick));
    }

    private void DrawText(nint dc, string text, int left, int top, int right, int bottom, int ptSize, int weight, uint rgb, bool centered = false)
    {
        if (right <= left || bottom <= top || string.IsNullOrEmpty(text))
            return;
        nint font = GetOrCreateFont(ptSize, weight);
        nint old = Win32.SelectObject(dc, font);
        try
        {
            Win32.SetBkMode(dc, Win32.TRANSPARENT);
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

    private nint GetOrCreateFont(int ptSize, int weight)
    {
        var (face, actualWeight) = Strings.ResolveFont(weight);
        var key = (ptSize, actualWeight, face, _dpi);
        if (_fontCache.TryGetValue(key, out nint cached) && cached != 0)
            return cached;

        if (_fontCache.Count >= 32) ClearFontCache();
        nint created = Win32.CreateFontW(
            -S(ptSize), 0, 0, 0, actualWeight, 0, 0, 0,
            1, 0, 0, Win32.CLEARTYPE_QUALITY, 0, face);
        if (created != 0)
            _fontCache[key] = created;
        return created;
    }

    private void ClearFontCache()
    {
        foreach (nint f in _fontCache.Values)
            if (f != 0) Win32.DeleteObject(f);
        _fontCache.Clear();
    }

    private void EnsureBackBuffer(int w, int h)
    {
        if (_backDc != 0 && _backW == w && _backH == h)
            return;
        ReleaseBackBuffer();

        nint screenDc = Win32.GetDC(0);
        _backDc = Win32.CreateCompatibleDC(screenDc);
        var bmi = new Win32.BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)sizeof(Win32.BITMAPINFOHEADER);
        bmi.bmiHeader.biWidth = w;
        bmi.bmiHeader.biHeight = -h;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0;

        _backBmp = Win32.CreateDIBSection(_backDc, &bmi, 0, out _, 0, 0);
        if (_backBmp != 0)
        {
            _backOldBmp = Win32.SelectObject(_backDc, _backBmp);
            _backW = w;
            _backH = h;
        }
        Win32.ReleaseDC(0, screenDc);
    }

    private void ReleaseBackBuffer()
    {
        if (_backDc != 0)
        {
            if (_backOldBmp != 0) { Win32.SelectObject(_backDc, _backOldBmp); _backOldBmp = 0; }
            if (_backBmp != 0) { Win32.DeleteObject(_backBmp); _backBmp = 0; }
            Win32.DeleteDC(_backDc);
            _backDc = 0;
        }
        _backW = 0;
        _backH = 0;
    }

    private int S(int px) => Theme.Scale(px, _dpi);
    private float SF(float px) => Theme.ScaleF(px, _dpi);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Theme.SetPreviewPalette(null);

        if (_editFont != 0) { Win32.DeleteObject(_editFont); _editFont = 0; }
        if (_editBgBrush != 0) { Win32.DeleteObject(_editBgBrush); _editBgBrush = 0; }
        if (_hwnd != 0)
        {
            Win32.DestroyWindow(_hwnd);
            _hwnd = 0;
            _editHwnd = 0;
        }
        ClearFontCache();
        ReleaseBackBuffer();
        if (ReferenceEquals(_instance, this))
            _instance = null;
    }
}
