using System.Runtime.InteropServices;

namespace Cueway.Interop;

internal static unsafe partial class Direct2D
{
    public static readonly Guid IID_ID2D1Factory = new("06152247-6f50-465a-9245-118bfd3b6007");
    public static readonly Guid IID_IDWriteFactory = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");

    public const int D2DERR_RECREATE_TARGET = unchecked((int)0x8899000C);

    public const uint D2D1_FACTORY_TYPE_SINGLE_THREADED = 0;
    public const uint DWRITE_FACTORY_TYPE_SHARED = 0;

    public const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    public const uint D2D1_ALPHA_MODE_PREMULTIPLIED = 1;
    public const uint D2D1_ALPHA_MODE_IGNORE = 3;

    public const uint D2D1_ANTIALIAS_MODE_PER_PRIMITIVE = 0;
    public const uint D2D1_ANTIALIAS_MODE_ALIASED = 1;

    public const uint D2D1_TEXT_ANTIALIAS_MODE_CLEARTYPE = 1;
    public const uint D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE = 2;

    public const uint D2D1_DRAW_TEXT_OPTIONS_NONE = 0x00000000;
    public const uint D2D1_DRAW_TEXT_OPTIONS_CLIP = 0x00000002;
    public const uint D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT = 0x00000004;

    public const uint D2D1_BITMAP_INTERPOLATION_MODE_LINEAR = 1;
    public const uint D2D1_CAP_STYLE_ROUND = 2;
    public const uint D2D1_LINE_JOIN_ROUND = 2;
    public const uint D2D1_GAMMA_2_2 = 0;
    public const uint D2D1_EXTEND_MODE_CLAMP = 0;

    public const uint D2D1_FIGURE_BEGIN_FILLED = 0;
    public const uint D2D1_FIGURE_BEGIN_HOLLOW = 1;
    public const uint D2D1_FIGURE_END_OPEN = 0;
    public const uint D2D1_FIGURE_END_CLOSED = 1;
    public const uint D2D1_SWEEP_DIRECTION_CLOCKWISE = 1;
    public const uint D2D1_ARC_SIZE_SMALL = 0;
    public const uint D2D1_ARC_SIZE_LARGE = 1;

    public const uint DWRITE_TEXT_ALIGNMENT_LEADING = 0;
    public const uint DWRITE_TEXT_ALIGNMENT_TRAILING = 1;
    public const uint DWRITE_TEXT_ALIGNMENT_CENTER = 2;

    public const uint DWRITE_PARAGRAPH_ALIGNMENT_NEAR = 0;
    public const uint DWRITE_PARAGRAPH_ALIGNMENT_FAR = 1;
    public const uint DWRITE_PARAGRAPH_ALIGNMENT_CENTER = 2;

    public const uint DWRITE_WORD_WRAPPING_WRAP = 0;
    public const uint DWRITE_WORD_WRAPPING_NO_WRAP = 1;
    public const uint DWRITE_WORD_WRAPPING_EMERGENCY_BREAK = 2;

    public const uint DWRITE_FONT_STYLE_NORMAL = 0;
    public const uint DWRITE_FONT_STRETCH_NORMAL = 5;
    public const uint DWRITE_TRIMMING_GRANULARITY_CHARACTER = 1;

    [LibraryImport("d2d1.dll")]
    public static partial int D2D1CreateFactory(
        uint factoryType,
        in Guid riid,
        nint pFactoryOptions,
        out nint ppIFactory);

    [LibraryImport("dwrite.dll")]
    public static partial int DWriteCreateFactory(
        uint factoryType,
        in Guid iid,
        out nint factory);

    [StructLayout(LayoutKind.Sequential)]
    public struct D2D1_COLOR_F
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public static D2D1_COLOR_F FromRgb(uint rgb, float alpha = 1f) => new()
        {
            r = ((rgb >> 16) & 0xFF) / 255f,
            g = ((rgb >> 8) & 0xFF) / 255f,
            b = (rgb & 0xFF) / 255f,
            a = alpha
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D2D1_POINT_2F
    {
        public float x;
        public float y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D2D1_SIZE_F
    {
        public float width;
        public float height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D2D1_SIZE_U
    {
        public uint width;
        public uint height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D2D1_RECT_F
    {
        public float left;
        public float top;
        public float right;
        public float bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D2D1_ROUNDED_RECT
    {
        public D2D1_RECT_F rect;
        public float radiusX;
        public float radiusY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D2D1_ELLIPSE
    {
        public D2D1_POINT_2F point;
        public float radiusX;
        public float radiusY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D2D1_PIXEL_FORMAT
    {
        public uint format;
        public uint alphaMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D2D1_RENDER_TARGET_PROPERTIES
    {
        public uint type;
        public D2D1_PIXEL_FORMAT pixelFormat;
        public float dpiX;
        public float dpiY;
        public uint usage;
        public uint minLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D2D1_HWND_RENDER_TARGET_PROPERTIES
    {
        public nint hwnd;
        public D2D1_SIZE_U pixelSize;
        public uint presentOptions;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D2D1_BITMAP_PROPERTIES
    {
        public D2D1_PIXEL_FORMAT pixelFormat;
        public float dpiX;
        public float dpiY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D2D1_GRADIENT_STOP
    {
        public float position;
        public D2D1_COLOR_F color;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D2D1_LINEAR_GRADIENT_BRUSH_PROPERTIES
    {
        public D2D1_POINT_2F startPoint;
        public D2D1_POINT_2F endPoint;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D2D1_RADIAL_GRADIENT_BRUSH_PROPERTIES
    {
        public D2D1_POINT_2F center;
        public D2D1_POINT_2F gradientOriginOffset;
        public float radiusX;
        public float radiusY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D2D1_STROKE_STYLE_PROPERTIES
    {
        public uint startCap;
        public uint endCap;
        public uint dashCap;
        public uint lineJoin;
        public float miterLimit;
        public uint dashStyle;
        public float dashOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D2D1_ARC_SEGMENT
    {
        public D2D1_POINT_2F point;
        public D2D1_SIZE_F size;
        public float rotationAngle;
        public uint sweepDirection;
        public uint arcSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D2D1_MATRIX_3X2_F
    {
        public float m11;
        public float m12;
        public float m21;
        public float m22;
        public float dx;
        public float dy;

        public static D2D1_MATRIX_3X2_F Identity => new() { m11 = 1f, m22 = 1f };

        public static D2D1_MATRIX_3X2_F Translation(float dx, float dy) => new()
        {
            m11 = 1f,
            m22 = 1f,
            dx = dx,
            dy = dy
        };

        public static D2D1_MATRIX_3X2_F ScaleAt(float sx, float sy, float cx, float cy) => new()
        {
            m11 = sx,
            m22 = sy,
            dx = cx - sx * cx,
            dy = cy - sy * cy
        };

        public static D2D1_MATRIX_3X2_F TranslateAndScaleAt(float dx, float dy, float sx, float sy, float cx, float cy) => new()
        {
            m11 = sx,
            m22 = sy,
            dx = cx - sx * cx + dx,
            dy = cy - sy * cy + dy
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DWRITE_TRIMMING
    {
        public uint granularity;
        public uint delimiter;
        public uint delimiterCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DWRITE_TEXT_METRICS
    {
        public float left;
        public float top;
        public float width;
        public float widthIncludingTrailingWhitespace;
        public float height;
        public float layoutWidth;
        public float layoutHeight;
        public uint maxBidiReorderingDepth;
        public uint lineCount;
    }

    public static void Release(nint comPtr)
    {
        if (comPtr == 0) return;
        nint* vtbl = *(nint**)comPtr;
        ((delegate* unmanaged[Stdcall]<nint, uint>)vtbl[2])(comPtr);
    }

    // ID2D1Factory
    // +7 (+10): CreatePathGeometry
    public static int Factory_CreatePathGeometry(nint factory, out nint pathGeometry)
    {
        pathGeometry = 0;
        nint* vtbl = *(nint**)factory;
        fixed (nint* p = &pathGeometry)
        {
            return ((delegate* unmanaged[Stdcall]<nint, nint*, int>)vtbl[10])(factory, p);
        }
    }

    // +8 (+11): CreateStrokeStyle
    public static int Factory_CreateStrokeStyle(nint factory, in D2D1_STROKE_STYLE_PROPERTIES props, float* dashes, uint dashesCount, out nint strokeStyle)
    {
        strokeStyle = 0;
        D2D1_STROKE_STYLE_PROPERTIES pProps = props;
        nint* vtbl = *(nint**)factory;
        fixed (nint* p = &strokeStyle)
        {
            return ((delegate* unmanaged[Stdcall]<nint, D2D1_STROKE_STYLE_PROPERTIES*, float*, uint, nint*, int>)vtbl[11])(factory, &pProps, dashes, dashesCount, p);
        }
    }

    // +11 (+14): CreateHwndRenderTarget
    public static int Factory_CreateHwndRenderTarget(
        nint factory,
        in D2D1_RENDER_TARGET_PROPERTIES rtProps,
        in D2D1_HWND_RENDER_TARGET_PROPERTIES hwndProps,
        out nint hwndRenderTarget)
    {
        hwndRenderTarget = 0;
        D2D1_RENDER_TARGET_PROPERTIES rtp = rtProps;
        D2D1_HWND_RENDER_TARGET_PROPERTIES hwp = hwndProps;
        nint* vtbl = *(nint**)factory;
        fixed (nint* p = &hwndRenderTarget)
        {
            return ((delegate* unmanaged[Stdcall]<nint, D2D1_RENDER_TARGET_PROPERTIES*, D2D1_HWND_RENDER_TARGET_PROPERTIES*, nint*, int>)vtbl[14])(factory, &rtp, &hwp, p);
        }
    }

    // ID2D1RenderTarget (IUnknown[3] + ID2D1Resource[1] => methods start at vtbl[4])
    // +0 ([4]): CreateBitmap
    public static int RenderTarget_CreateBitmap(
        nint rt,
        D2D1_SIZE_U size,
        void* srcData,
        uint pitch,
        in D2D1_BITMAP_PROPERTIES props,
        out nint bitmap)
    {
        bitmap = 0;
        D2D1_BITMAP_PROPERTIES pProps = props;
        nint* vtbl = *(nint**)rt;
        fixed (nint* p = &bitmap)
        {
            return ((delegate* unmanaged[Stdcall]<nint, D2D1_SIZE_U, void*, uint, D2D1_BITMAP_PROPERTIES*, nint*, int>)vtbl[4])(rt, size, srcData, pitch, &pProps, p);
        }
    }

    // +4 ([8]): CreateSolidColorBrush
    public static int RenderTarget_CreateSolidColorBrush(nint rt, in D2D1_COLOR_F color, out nint brush)
    {
        brush = 0;
        D2D1_COLOR_F c = color;
        nint* vtbl = *(nint**)rt;
        fixed (nint* p = &brush)
        {
            return ((delegate* unmanaged[Stdcall]<nint, D2D1_COLOR_F*, nint, nint*, int>)vtbl[8])(rt, &c, 0, p);
        }
    }

    // +5 ([9]): CreateGradientStopCollection
    public static int RenderTarget_CreateGradientStopCollection(
        nint rt,
        D2D1_GRADIENT_STOP* stops,
        uint count,
        uint gamma,
        uint extendMode,
        out nint collection)
    {
        collection = 0;
        nint* vtbl = *(nint**)rt;
        fixed (nint* p = &collection)
        {
            return ((delegate* unmanaged[Stdcall]<nint, D2D1_GRADIENT_STOP*, uint, uint, uint, nint*, int>)vtbl[9])(rt, stops, count, gamma, extendMode, p);
        }
    }

    // +6 ([10]): CreateLinearGradientBrush
    public static int RenderTarget_CreateLinearGradientBrush(
        nint rt,
        in D2D1_LINEAR_GRADIENT_BRUSH_PROPERTIES props,
        nint stopCollection,
        out nint brush)
    {
        brush = 0;
        D2D1_LINEAR_GRADIENT_BRUSH_PROPERTIES pProps = props;
        nint* vtbl = *(nint**)rt;
        fixed (nint* p = &brush)
        {
            return ((delegate* unmanaged[Stdcall]<nint, D2D1_LINEAR_GRADIENT_BRUSH_PROPERTIES*, nint, nint, nint*, int>)vtbl[10])(rt, &pProps, 0, stopCollection, p);
        }
    }

    // +7 ([11]): CreateRadialGradientBrush
    public static int RenderTarget_CreateRadialGradientBrush(
        nint rt,
        in D2D1_RADIAL_GRADIENT_BRUSH_PROPERTIES props,
        nint stopCollection,
        out nint brush)
    {
        brush = 0;
        D2D1_RADIAL_GRADIENT_BRUSH_PROPERTIES pProps = props;
        nint* vtbl = *(nint**)rt;
        fixed (nint* p = &brush)
        {
            return ((delegate* unmanaged[Stdcall]<nint, D2D1_RADIAL_GRADIENT_BRUSH_PROPERTIES*, nint, nint, nint*, int>)vtbl[11])(rt, &pProps, 0, stopCollection, p);
        }
    }

    // +11 ([15]): DrawLine
    public static void RenderTarget_DrawLine(nint rt, D2D1_POINT_2F p0, D2D1_POINT_2F p1, nint brush, float strokeWidth, nint strokeStyle = 0)
    {
        nint* vtbl = *(nint**)rt;
        ((delegate* unmanaged[Stdcall]<nint, D2D1_POINT_2F, D2D1_POINT_2F, nint, float, nint, void>)vtbl[15])(rt, p0, p1, brush, strokeWidth, strokeStyle);
    }

    // +12 ([16]): DrawRectangle
    public static void RenderTarget_DrawRectangle(nint rt, in D2D1_RECT_F rect, nint brush, float strokeWidth, nint strokeStyle = 0)
    {
        D2D1_RECT_F r = rect;
        nint* vtbl = *(nint**)rt;
        ((delegate* unmanaged[Stdcall]<nint, D2D1_RECT_F*, nint, float, nint, void>)vtbl[16])(rt, &r, brush, strokeWidth, strokeStyle);
    }

    // +13 ([17]): FillRectangle
    public static void RenderTarget_FillRectangle(nint rt, in D2D1_RECT_F rect, nint brush)
    {
        D2D1_RECT_F r = rect;
        nint* vtbl = *(nint**)rt;
        ((delegate* unmanaged[Stdcall]<nint, D2D1_RECT_F*, nint, void>)vtbl[17])(rt, &r, brush);
    }

    // +14 ([18]): DrawRoundedRectangle
    public static void RenderTarget_DrawRoundedRectangle(nint rt, in D2D1_ROUNDED_RECT rect, nint brush, float strokeWidth, nint strokeStyle = 0)
    {
        D2D1_ROUNDED_RECT r = rect;
        nint* vtbl = *(nint**)rt;
        ((delegate* unmanaged[Stdcall]<nint, D2D1_ROUNDED_RECT*, nint, float, nint, void>)vtbl[18])(rt, &r, brush, strokeWidth, strokeStyle);
    }

    // +15 ([19]): FillRoundedRectangle
    public static void RenderTarget_FillRoundedRectangle(nint rt, in D2D1_ROUNDED_RECT rect, nint brush)
    {
        D2D1_ROUNDED_RECT r = rect;
        nint* vtbl = *(nint**)rt;
        ((delegate* unmanaged[Stdcall]<nint, D2D1_ROUNDED_RECT*, nint, void>)vtbl[19])(rt, &r, brush);
    }

    // +16 ([20]): DrawEllipse
    public static void RenderTarget_DrawEllipse(nint rt, in D2D1_ELLIPSE ellipse, nint brush, float strokeWidth, nint strokeStyle = 0)
    {
        D2D1_ELLIPSE e = ellipse;
        nint* vtbl = *(nint**)rt;
        ((delegate* unmanaged[Stdcall]<nint, D2D1_ELLIPSE*, nint, float, nint, void>)vtbl[20])(rt, &e, brush, strokeWidth, strokeStyle);
    }

    // +17 ([21]): FillEllipse
    public static void RenderTarget_FillEllipse(nint rt, in D2D1_ELLIPSE ellipse, nint brush)
    {
        D2D1_ELLIPSE e = ellipse;
        nint* vtbl = *(nint**)rt;
        ((delegate* unmanaged[Stdcall]<nint, D2D1_ELLIPSE*, nint, void>)vtbl[21])(rt, &e, brush);
    }

    // +18 ([22]): DrawGeometry
    public static void RenderTarget_DrawGeometry(nint rt, nint geometry, nint brush, float strokeWidth, nint strokeStyle = 0)
    {
        nint* vtbl = *(nint**)rt;
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, float, nint, void>)vtbl[22])(rt, geometry, brush, strokeWidth, strokeStyle);
    }

    // +19 ([23]): FillGeometry
    public static void RenderTarget_FillGeometry(nint rt, nint geometry, nint brush, nint opacityBrush = 0)
    {
        nint* vtbl = *(nint**)rt;
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, nint, void>)vtbl[23])(rt, geometry, brush, opacityBrush);
    }

    // +22 ([26]): DrawBitmap
    public static void RenderTarget_DrawBitmap(
        nint rt,
        nint bitmap,
        in D2D1_RECT_F destRect,
        float opacity = 1f,
        uint interpolationMode = D2D1_BITMAP_INTERPOLATION_MODE_LINEAR)
    {
        D2D1_RECT_F dr = destRect;
        nint* vtbl = *(nint**)rt;
        ((delegate* unmanaged[Stdcall]<nint, nint, D2D1_RECT_F*, float, uint, D2D1_RECT_F*, void>)vtbl[26])(rt, bitmap, &dr, opacity, interpolationMode, null);
    }

    // +23 ([27]): DrawText
    public static void RenderTarget_DrawText(
        nint rt,
        string text,
        nint textFormat,
        in D2D1_RECT_F layoutRect,
        nint brush,
        uint options = D2D1_DRAW_TEXT_OPTIONS_CLIP,
        uint measuringMode = 0)
    {
        D2D1_RECT_F lr = layoutRect;
        nint* vtbl = *(nint**)rt;
        fixed (char* pText = text)
        {
            ((delegate* unmanaged[Stdcall]<nint, char*, uint, nint, D2D1_RECT_F*, nint, uint, uint, void>)vtbl[27])(
                rt, pText, (uint)text.Length, textFormat, &lr, brush, options, measuringMode);
        }
    }

    // +26 ([30]): SetTransform
    public static void RenderTarget_SetTransform(nint rt, in D2D1_MATRIX_3X2_F matrix)
    {
        D2D1_MATRIX_3X2_F m = matrix;
        nint* vtbl = *(nint**)rt;
        ((delegate* unmanaged[Stdcall]<nint, D2D1_MATRIX_3X2_F*, void>)vtbl[30])(rt, &m);
    }

    // +28 ([32]): SetAntialiasMode
    public static void RenderTarget_SetAntialiasMode(nint rt, uint mode)
    {
        nint* vtbl = *(nint**)rt;
        ((delegate* unmanaged[Stdcall]<nint, uint, void>)vtbl[32])(rt, mode);
    }

    // +30 ([34]): SetTextAntialiasMode
    public static void RenderTarget_SetTextAntialiasMode(nint rt, uint mode)
    {
        nint* vtbl = *(nint**)rt;
        ((delegate* unmanaged[Stdcall]<nint, uint, void>)vtbl[34])(rt, mode);
    }

    // +41 ([45]): PushAxisAlignedClip
    public static void RenderTarget_PushAxisAlignedClip(nint rt, in D2D1_RECT_F clipRect, uint antialiasMode = D2D1_ANTIALIAS_MODE_PER_PRIMITIVE)
    {
        D2D1_RECT_F cr = clipRect;
        nint* vtbl = *(nint**)rt;
        ((delegate* unmanaged[Stdcall]<nint, D2D1_RECT_F*, uint, void>)vtbl[45])(rt, &cr, antialiasMode);
    }

    // +42 ([46]): PopAxisAlignedClip
    public static void RenderTarget_PopAxisAlignedClip(nint rt)
    {
        nint* vtbl = *(nint**)rt;
        ((delegate* unmanaged[Stdcall]<nint, void>)vtbl[46])(rt);
    }

    // +43 ([47]): Clear
    public static void RenderTarget_Clear(nint rt, in D2D1_COLOR_F color)
    {
        D2D1_COLOR_F c = color;
        nint* vtbl = *(nint**)rt;
        ((delegate* unmanaged[Stdcall]<nint, D2D1_COLOR_F*, void>)vtbl[47])(rt, &c);
    }

    // +44 ([48]): BeginDraw
    public static void RenderTarget_BeginDraw(nint rt)
    {
        nint* vtbl = *(nint**)rt;
        ((delegate* unmanaged[Stdcall]<nint, void>)vtbl[48])(rt);
    }

    // +45 ([49]): EndDraw
    public static int RenderTarget_EndDraw(nint rt)
    {
        nint* vtbl = *(nint**)rt;
        return ((delegate* unmanaged[Stdcall]<nint, ulong*, ulong*, int>)vtbl[49])(rt, null, null);
    }

    // +47 ([51]): SetDpi
    public static void RenderTarget_SetDpi(nint rt, float dpiX, float dpiY)
    {
        nint* vtbl = *(nint**)rt;
        ((delegate* unmanaged[Stdcall]<nint, float, float, void>)vtbl[51])(rt, dpiX, dpiY);
    }

    // ID2D1HwndRenderTarget (+1 [58]): Resize
    public static int HwndRenderTarget_Resize(nint rt, uint width, uint height)
    {
        var size = new D2D1_SIZE_U { width = width, height = height };
        nint* vtbl = *(nint**)rt;
        return ((delegate* unmanaged[Stdcall]<nint, D2D1_SIZE_U*, int>)vtbl[58])(rt, &size);
    }

    // ID2D1SolidColorBrush (+0 [8]): SetColor
    public static void SolidBrush_SetColor(nint brush, in D2D1_COLOR_F color)
    {
        D2D1_COLOR_F c = color;
        nint* vtbl = *(nint**)brush;
        ((delegate* unmanaged[Stdcall]<nint, D2D1_COLOR_F*, void>)vtbl[8])(brush, &c);
    }

    // ID2D1PathGeometry (+0 [17]): Open
    public static int PathGeometry_Open(nint pathGeo, out nint sink)
    {
        sink = 0;
        nint* vtbl = *(nint**)pathGeo;
        fixed (nint* p = &sink)
        {
            return ((delegate* unmanaged[Stdcall]<nint, nint*, int>)vtbl[17])(pathGeo, p);
        }
    }

    // ID2D1SimplifiedGeometrySink / ID2D1GeometrySink
    // +2 ([5]): BeginFigure
    public static void Sink_BeginFigure(nint sink, D2D1_POINT_2F startPoint, uint figureBegin)
    {
        nint* vtbl = *(nint**)sink;
        ((delegate* unmanaged[Stdcall]<nint, D2D1_POINT_2F, uint, void>)vtbl[5])(sink, startPoint, figureBegin);
    }

    // +5 ([8]): EndFigure
    public static void Sink_EndFigure(nint sink, uint figureEnd)
    {
        nint* vtbl = *(nint**)sink;
        ((delegate* unmanaged[Stdcall]<nint, uint, void>)vtbl[8])(sink, figureEnd);
    }

    // +6 ([9]): Close
    public static int Sink_Close(nint sink)
    {
        nint* vtbl = *(nint**)sink;
        return ((delegate* unmanaged[Stdcall]<nint, int>)vtbl[9])(sink);
    }

    // +0 on ID2D1GeometrySink ([10]): AddLine
    public static void Sink_AddLine(nint sink, D2D1_POINT_2F point)
    {
        nint* vtbl = *(nint**)sink;
        ((delegate* unmanaged[Stdcall]<nint, D2D1_POINT_2F, void>)vtbl[10])(sink, point);
    }

    // +4 on ID2D1GeometrySink ([14]): AddArc
    public static void Sink_AddArc(nint sink, in D2D1_ARC_SEGMENT arc)
    {
        D2D1_ARC_SEGMENT a = arc;
        nint* vtbl = *(nint**)sink;
        ((delegate* unmanaged[Stdcall]<nint, D2D1_ARC_SEGMENT*, void>)vtbl[14])(sink, &a);
    }

    // IDWriteFactory
    // +12 ([15]): CreateTextFormat
    public static int DWrite_CreateTextFormat(
        nint dwFactory,
        string fontFamilyName,
        uint fontWeight,
        uint fontStyle,
        uint fontStretch,
        float fontSize,
        string localeName,
        out nint textFormat)
    {
        textFormat = 0;
        nint* vtbl = *(nint**)dwFactory;
        fixed (char* pFam = fontFamilyName)
        fixed (char* pLoc = localeName)
        fixed (nint* pFmt = &textFormat)
        {
            return ((delegate* unmanaged[Stdcall]<nint, char*, nint, uint, uint, uint, float, char*, nint*, int>)vtbl[15])(
                dwFactory, pFam, 0, fontWeight, fontStyle, fontStretch, fontSize, pLoc, pFmt);
        }
    }

    // +15 ([18]): CreateTextLayout
    public static int DWrite_CreateTextLayout(
        nint dwFactory,
        string text,
        nint textFormat,
        float maxWidth,
        float maxHeight,
        out nint textLayout)
    {
        textLayout = 0;
        nint* vtbl = *(nint**)dwFactory;
        fixed (char* pText = text)
        fixed (nint* pLay = &textLayout)
        {
            return ((delegate* unmanaged[Stdcall]<nint, char*, uint, nint, float, float, nint*, int>)vtbl[18])(
                dwFactory, pText, (uint)text.Length, textFormat, maxWidth, maxHeight, pLay);
        }
    }

    // +17 ([20]): CreateEllipsisTrimmingSign
    public static int DWrite_CreateEllipsisTrimmingSign(nint dwFactory, nint textFormat, out nint inlineObj)
    {
        inlineObj = 0;
        nint* vtbl = *(nint**)dwFactory;
        fixed (nint* p = &inlineObj)
        {
            return ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)vtbl[20])(dwFactory, textFormat, p);
        }
    }

    // IDWriteTextFormat
    // +0 ([3]): SetTextAlignment
    public static int TextFormat_SetTextAlignment(nint fmt, uint alignment)
    {
        nint* vtbl = *(nint**)fmt;
        return ((delegate* unmanaged[Stdcall]<nint, uint, int>)vtbl[3])(fmt, alignment);
    }

    // +1 ([4]): SetParagraphAlignment
    public static int TextFormat_SetParagraphAlignment(nint fmt, uint alignment)
    {
        nint* vtbl = *(nint**)fmt;
        return ((delegate* unmanaged[Stdcall]<nint, uint, int>)vtbl[4])(fmt, alignment);
    }

    // +2 ([5]): SetWordWrapping
    public static int TextFormat_SetWordWrapping(nint fmt, uint wrapping)
    {
        nint* vtbl = *(nint**)fmt;
        return ((delegate* unmanaged[Stdcall]<nint, uint, int>)vtbl[5])(fmt, wrapping);
    }

    // +6 ([9]): SetTrimming
    public static int TextFormat_SetTrimming(nint fmt, in DWRITE_TRIMMING trimming, nint trimmingSign)
    {
        DWRITE_TRIMMING tr = trimming;
        nint* vtbl = *(nint**)fmt;
        return ((delegate* unmanaged[Stdcall]<nint, DWRITE_TRIMMING*, nint, int>)vtbl[9])(fmt, &tr, trimmingSign);
    }

    // IDWriteTextLayout (+32 [60]): GetMetrics
    public static int TextLayout_GetMetrics(nint layout, out DWRITE_TEXT_METRICS metrics)
    {
        metrics = default;
        nint* vtbl = *(nint**)layout;
        fixed (DWRITE_TEXT_METRICS* p = &metrics)
        {
            return ((delegate* unmanaged[Stdcall]<nint, DWRITE_TEXT_METRICS*, int>)vtbl[60])(layout, p);
        }
    }
}
