using System.Runtime.InteropServices;

namespace Cueway.Interop;

internal static unsafe partial class GdiPlus
{
    public const int SmoothingModeAntiAlias = 4;
    public const int PixelOffsetModeHighQuality = 2;
    public const int InterpolationModeHighQualityBicubic = 7;
    public const int UnitPixel = 2;
    public const int LineCapRound = 2;
    public const int FillModeAlternate = 0;
    public const int PixelFormat32bppPARGB = 0x000E200B;

    [StructLayout(LayoutKind.Sequential)]
    public struct GdiplusStartupInput
    {
        public uint GdiplusVersion;
        public nint DebugEventCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    [LibraryImport("gdiplus.dll")]
    public static partial int GdiplusStartup(out nuint token, GdiplusStartupInput* input, nint output);

    [LibraryImport("gdiplus.dll")]
    public static partial void GdiplusShutdown(nuint token);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateFromHDC(nint hdc, out nint graphics);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteGraphics(nint graphics);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetSmoothingMode(nint graphics, int smoothingMode);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPixelOffsetMode(nint graphics, int pixelOffsetMode);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetInterpolationMode(nint graphics, int interpolationMode);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipGraphicsClear(nint graphics, uint color);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateSolidFill(uint color, out nint brush);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteBrush(nint brush);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreatePen1(uint color, float width, int unit, out nint pen);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPenStartCap(nint pen, int startCap);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPenEndCap(nint pen, int endCap);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeletePen(nint pen);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreatePath(int brushMode, out nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeletePath(nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipAddPathArc(nint path, float x, float y, float width, float height, float startAngle, float sweepAngle);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipAddPathLine(nint path, float x1, float y1, float x2, float y2);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipClosePathFigure(nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipFillPath(nint graphics, nint brush, nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawPath(nint graphics, nint pen, nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawArc(nint graphics, nint pen, float x, float y, float width, float height, float startAngle, float sweepAngle);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipFillEllipse(nint graphics, nint brush, float x, float y, float width, float height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawEllipse(nint graphics, nint pen, float x, float y, float width, float height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawLine(nint graphics, nint pen, float x1, float y1, float x2, float y2);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipFillRectangle(nint graphics, nint brush, float x, float y, float width, float height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateBitmapFromScan0(int width, int height, int stride, int format, nint scan0, out nint bitmap);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateHICONFromBitmap(nint bitmap, out nint hicon);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateBitmapFromHICON(nint hicon, out nint bitmap);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipGetImageGraphicsContext(nint image, out nint graphics);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDisposeImage(nint image);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawImageRectI(nint graphics, nint image, int x, int y, int width, int height);

    public static uint Argb(byte a, byte r, byte g, byte b) => ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

    public static uint Argb(uint rgb, byte a = 255) => ((uint)a << 24) | (rgb & 0x00FFFFFF);

    public static nint CreateRoundedRectPath(float x, float y, float w, float h, float radius)
    {
        if (GdipCreatePath(FillModeAlternate, out nint path) != 0 || path == 0)
            return 0;

        float d = MathF.Min(radius * 2f, MathF.Min(w, h));
        if (d <= 0.5f)
        {
            GdipDeletePath(path);
            return 0;
        }

        GdipAddPathArc(path, x, y, d, d, 180f, 90f);
        GdipAddPathArc(path, x + w - d, y, d, d, 270f, 90f);
        GdipAddPathArc(path, x + w - d, y + h - d, d, d, 0f, 90f);
        GdipAddPathArc(path, x, y + h - d, d, d, 90f, 90f);
        GdipClosePathFigure(path);
        return path;
    }

    public static void FillRoundedRect(nint graphics, float x, float y, float w, float h, float radius, uint argbColor)
    {
        if (GdipCreateSolidFill(argbColor, out nint brush) != 0 || brush == 0) return;
        try
        {
            if (radius <= 0.5f)
            {
                GdipFillRectangle(graphics, brush, x, y, w, h);
                return;
            }
            nint path = CreateRoundedRectPath(x, y, w, h, radius);
            if (path != 0)
            {
                try { GdipFillPath(graphics, brush, path); }
                finally { GdipDeletePath(path); }
            }
        }
        finally
        {
            GdipDeleteBrush(brush);
        }
    }

    public static void DrawRoundedRect(nint graphics, float x, float y, float w, float h, float radius, uint argbColor, float width = 1f)
    {
        if (GdipCreatePen1(argbColor, width, UnitPixel, out nint pen) != 0 || pen == 0) return;
        try
        {
            nint path = CreateRoundedRectPath(x, y, w, h, radius);
            if (path != 0)
            {
                try { GdipDrawPath(graphics, pen, path); }
                finally { GdipDeletePath(path); }
            }
        }
        finally
        {
            GdipDeletePen(pen);
        }
    }

    public static void FillAndStrokeRoundedRect(nint graphics, float x, float y, float w, float h, float radius, uint fillArgb, uint borderArgb, float borderWidth = 1f)
    {
        nint path = CreateRoundedRectPath(x, y, w, h, radius);
        if (path == 0) return;
        try
        {
            if ((fillArgb >> 24) > 0 && GdipCreateSolidFill(fillArgb, out nint brush) == 0 && brush != 0)
            {
                try { GdipFillPath(graphics, brush, path); }
                finally { GdipDeleteBrush(brush); }
            }
            if ((borderArgb >> 24) > 0 && GdipCreatePen1(borderArgb, borderWidth, UnitPixel, out nint pen) == 0 && pen != 0)
            {
                try { GdipDrawPath(graphics, pen, path); }
                finally { GdipDeletePen(pen); }
            }
        }
        finally
        {
            GdipDeletePath(path);
        }
    }
}
