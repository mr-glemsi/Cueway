using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cueway.Interop;
using Cueway.Localization;
using Cueway.Routing;

namespace Cueway.UI;

internal sealed unsafe class OsdOverlayWindow : IDisposable
{
    private const string ClassName = "Cueway_OsdWindow";
    private const nuint TIMER_HIDE = 7001;
    private static OsdOverlayWindow? _instance;

    private readonly DynamicIconRenderer _iconRenderer;
    private nint _hwnd;
    private nint _fontTitle;
    private nint _fontSub;
    private uint _fontDpi;
    private string? _fontFace;
    private bool _disposed;

    public OsdOverlayWindow(DynamicIconRenderer iconRenderer)
    {
        _iconRenderer = iconRenderer;
        _instance = this;
        CreateWindow();
    }

    private void CreateWindow()
    {
        nint hInst = Win32.GetModuleHandleW(null);
        fixed (char* cls = ClassName)
        {
            var wc = new Win32.WNDCLASSEXW
            {
                cbSize = (uint)sizeof(Win32.WNDCLASSEXW),
                style = 0,
                lpfnWndProc = &WndProc,
                hInstance = hInst,
                lpszClassName = cls
            };
            Win32.RegisterClassExW(&wc);
        }

        _hwnd = Win32.CreateWindowExW(
            Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_TOPMOST | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE,
            ClassName,
            "Cueway OSD",
            Win32.WS_POPUP,
            0, 0, 10, 10,
            0, 0, hInst, 0);
    }

    public void ShowMessage(OsdMessage msg)
    {
        if (_disposed || _hwnd == 0) return;

        Win32.GetCursorPos(out var pt);
        nint hMon = Win32.MonitorFromPoint(pt, Win32.MONITOR_DEFAULTTONEAREST);
        var mi = new Win32.MONITORINFO { cbSize = (uint)sizeof(Win32.MONITORINFO) };
        Win32.GetMonitorInfoW(hMon, &mi);

        uint dpi = Win32.GetMonitorDpi(hMon, 96);
        if (dpi == 0) dpi = 96;

        int width = Theme.Scale(340, dpi);
        int height = Theme.Scale(68, dpi);
        int margin = Theme.Scale(20, dpi);
        int x = mi.rcWork.Right - width - margin;
        int y = mi.rcWork.Bottom - height - margin;

        RenderLayered(msg, x, y, width, height, dpi);

        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, y, width, height,
            Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
        Win32.KillTimer(_hwnd, TIMER_HIDE);
        Win32.SetTimer(_hwnd, TIMER_HIDE, 2100, 0);
    }

    public void HideImmediate()
    {
        if (_hwnd != 0)
        {
            Win32.KillTimer(_hwnd, TIMER_HIDE);
            Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
        }
    }

    private void RenderLayered(OsdMessage msg, int x, int y, int width, int height, uint dpi)
    {
        nint screenDc = Win32.GetDC(0);
        nint memDc = Win32.CreateCompatibleDC(screenDc);

        var bmi = new Win32.BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)sizeof(Win32.BITMAPINFOHEADER);
        bmi.bmiHeader.biWidth = width;
        bmi.bmiHeader.biHeight = -height; // Top-down DIB
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;

        nint hBitmap = Win32.CreateDIBSection(memDc, &bmi, 0, out nint ppvBits, 0, 0);
        nint oldBmp = Win32.SelectObject(memDc, hBitmap);

        try
        {
            int iconSize = Theme.Scale(32, dpi);
            int iconX = Theme.Scale(16, dpi);
            int iconY = (height - iconSize) / 2;
            nint hIcon = _iconRenderer.GetIconForApp(msg.AppKey, msg.ExePath);
            bool iconDrawnByGdip = false;

            if (GdiPlus.GdipCreateFromHDC(memDc, out nint graphics) == 0 && graphics != 0)
            {
                try
                {
                    GdiPlus.GdipSetSmoothingMode(graphics, GdiPlus.SmoothingModeAntiAlias);
                    GdiPlus.GdipSetInterpolationMode(graphics, GdiPlus.InterpolationModeHighQualityBicubic);
                    GdiPlus.GdipGraphicsClear(graphics, 0x00000000);

                    float r = Theme.ScaleF(14f, dpi);
                    uint border = msg.IsWarning
                        ? GdiPlus.Argb(Theme.Amber, 220)
                        : GdiPlus.Argb(Theme.BorderStrong, 255);
                    GdiPlus.FillAndStrokeRoundedRect(
                        graphics,
                        1f, 1f, width - 2f, height - 2f, r,
                        GdiPlus.Argb(Theme.BgOsd, 255),
                        border,
                        1.2f);

                    if (hIcon != 0 && GdiPlus.GdipCreateBitmapFromHICON(hIcon, out nint iconBmp) == 0 && iconBmp != 0)
                    {
                        try
                        {
                            GdiPlus.GdipDrawImageRectI(graphics, iconBmp, iconX, iconY, iconSize, iconSize);
                            iconDrawnByGdip = true;
                        }
                        finally
                        {
                            GdiPlus.GdipDisposeImage(iconBmp);
                        }
                    }
                }
                finally
                {
                    GdiPlus.GdipDeleteGraphics(graphics);
                }
            }

            if (hIcon != 0 && !iconDrawnByGdip)
            {
                Win32.DrawIconEx(memDc, iconX, iconY, hIcon, iconSize, iconSize, 0, 0, Win32.DI_NORMAL);
            }

            // Text via ClearType GDI
            Win32.SetBkMode(memDc, Win32.TRANSPARENT);
            int textLeft = hIcon != 0 ? iconX + iconSize + Theme.Scale(12, dpi) : Theme.Scale(18, dpi);
            int textRight = width - Theme.Scale(16, dpi);
            int textTop = Theme.Scale(13, dpi);
            int textBottom = Theme.Scale(56, dpi);

            EnsureFonts(dpi);

            if (_fontTitle != 0 && _fontSub != 0)
            {
                nint oldFont = Win32.SelectObject(memDc, _fontTitle);
                try
                {
                    Win32.SetTextColor(memDc, Theme.ToColorRef(msg.IsWarning ? Theme.Amber : Theme.TextPrimary));
                    var r1 = new Win32.RECT
                    {
                        Left = textLeft,
                        Top = textTop,
                        Right = textRight,
                        Bottom = Theme.Scale(34, dpi)
                    };
                    Win32.DrawTextW(memDc, msg.AppName, -1, &r1,
                        Win32.DT_LEFT | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_END_ELLIPSIS | Win32.DT_NOPREFIX);

                    Win32.SelectObject(memDc, _fontSub);
                    Win32.SetTextColor(memDc, Theme.ToColorRef(Theme.TextSecondary));
                    var r2 = new Win32.RECT
                    {
                        Left = textLeft,
                        Top = Theme.Scale(34, dpi),
                        Right = textRight,
                        Bottom = textBottom
                    };
                    Win32.DrawTextW(memDc, msg.Subtitle, -1, &r2,
                        Win32.DT_LEFT | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_END_ELLIPSIS | Win32.DT_NOPREFIX);
                }
                finally
                {
                    Win32.SelectObject(memDc, oldFont);
                }
            }

            // GDI DrawTextW zeroes the alpha byte of pixels it writes (including black light-theme text!).
            // Restore full alpha across the text rect (and fallback icon rect if GDI+ failed).
            if (ppvBits != 0)
            {
                uint* p = (uint*)ppvBits;
                int x0 = Math.Clamp(textLeft, 0, width);
                int x1 = Math.Clamp(textRight, 0, width);
                int y0 = Math.Clamp(textTop, 0, height);
                int y1 = Math.Clamp(textBottom, 0, height);
                for (int py = y0; py < y1; py++)
                {
                    int row = py * width;
                    for (int px = x0; px < x1; px++)
                    {
                        p[row + px] |= 0xFF000000u;
                    }
                }
                if (hIcon != 0 && !iconDrawnByGdip)
                {
                    int ix0 = Math.Clamp(iconX, 0, width);
                    int ix1 = Math.Clamp(iconX + iconSize, 0, width);
                    int iy0 = Math.Clamp(iconY, 0, height);
                    int iy1 = Math.Clamp(iconY + iconSize, 0, height);
                    for (int py = iy0; py < iy1; py++)
                    {
                        int row = py * width;
                        for (int px = ix0; px < ix1; px++)
                        {
                            uint val = p[row + px];
                            if ((val & 0xFF000000u) == 0 && (val & 0x00FFFFFFu) != 0)
                                p[row + px] = val | 0xFF000000u;
                        }
                    }
                }
            }

            var ptDst = new Win32.POINT { X = x, Y = y };
            var ptSrc = new Win32.POINT { X = 0, Y = 0 };
            var sz = new Win32.SIZE { Width = width, Height = height };
            var blend = new Win32.BLENDFUNCTION
            {
                BlendOp = Win32.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 242,
                AlphaFormat = Win32.AC_SRC_ALPHA
            };
            Win32.UpdateLayeredWindow(_hwnd, screenDc, &ptDst, &sz, memDc, &ptSrc, 0, &blend, Win32.ULW_ALPHA);
        }
        finally
        {
            Win32.SelectObject(memDc, oldBmp);
            if (hBitmap != 0) Win32.DeleteObject(hBitmap);
            Win32.DeleteDC(memDc);
            Win32.ReleaseDC(0, screenDc);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        try
        {
            if (msg == Win32.WM_TIMER && wParam == TIMER_HIDE)
            {
                Win32.KillTimer(hwnd, TIMER_HIDE);
                Win32.ShowWindow(hwnd, Win32.SW_HIDE);
                return 0;
            }
            return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
        catch
        {
            return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    private void EnsureFonts(uint dpi)
    {
        var (titleFace, titleWeight) = Strings.ResolveFont(Win32.FW_SEMIBOLD);
        var (subFace, subWeight) = Strings.ResolveFont(Win32.FW_NORMAL);
        if (_fontTitle != 0 && _fontSub != 0 && _fontDpi == dpi &&
            string.Equals(_fontFace, titleFace, StringComparison.Ordinal))
        {
            return;
        }

        ReleaseFonts();
        _fontTitle = Win32.CreateFontW(
            -(int)MathF.Round(10.5f * dpi / 72f), 0, 0, 0, titleWeight, 0, 0, 0,
            1, 0, 0, Win32.CLEARTYPE_QUALITY, 0, titleFace);
        _fontSub = Win32.CreateFontW(
            -(int)MathF.Round(9f * dpi / 72f), 0, 0, 0, subWeight, 0, 0, 0,
            1, 0, 0, Win32.CLEARTYPE_QUALITY, 0, subFace);
        _fontDpi = dpi;
        _fontFace = titleFace;
    }

    private void ReleaseFonts()
    {
        if (_fontTitle != 0)
        {
            Win32.DeleteObject(_fontTitle);
            _fontTitle = 0;
        }
        if (_fontSub != 0)
        {
            Win32.DeleteObject(_fontSub);
            _fontSub = 0;
        }
        _fontDpi = 0;
        _fontFace = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hwnd != 0)
        {
            Win32.KillTimer(_hwnd, TIMER_HIDE);
            Win32.DestroyWindow(_hwnd);
            _hwnd = 0;
        }
        ReleaseFonts();
        if (ReferenceEquals(_instance, this))
            _instance = null;
    }
}
