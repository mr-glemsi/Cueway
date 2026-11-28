using System.Runtime.InteropServices;
using Cueway.Interop;
using Cueway.Localization;

namespace Cueway.UI;

internal sealed unsafe class D2DRenderer : IDisposable
{
    internal enum TextAlign
    {
        Left = 0,
        Right = 1,
        Center = 2
    }

    internal enum VerticalAlign
    {
        Top = 0,
        Bottom = 1,
        Center = 2
    }

    private readonly record struct TextFormatKey(
        string Family,
        string Locale,
        int PtSize,
        int Weight,
        uint Dpi,
        TextAlign HAlign,
        VerticalAlign VAlign,
        bool Wrap,
        bool Ellipsis);

    private nint _d2dFactory;
    private nint _dwFactory;
    private nint _roundStrokeStyle;
    private nint _renderTarget;
    private nint _solidBrush;
    private nint _speakerBodyGeo;
    private nint _boundHwnd;
    private int _rtW;
    private int _rtH;
    private bool _disposed;

    private int _clipStackDepth;
    private ulong _clipPushedMask;
    private readonly Stack<bool> _clipOverflowStack = new();
    private Direct2D.D2D1_MATRIX_3X2_F _currentTransform = Direct2D.D2D1_MATRIX_3X2_F.Identity;
    private readonly Stack<Direct2D.D2D1_MATRIX_3X2_F> _transformStack = new();

    private readonly Dictionary<TextFormatKey, (nint Format, nint TrimSign)> _textFormatCache = new();
    private readonly Dictionary<(string Text, int PtSize, int Weight, uint Dpi, AppLanguage Lang), int> _textWidthCache = new();
    private readonly Dictionary<(nint HIcon, int Size), nint> _iconBitmapCache = new();
    private readonly Dictionary<(uint StartRgb, byte StartA, uint EndRgb, byte EndA), (nint StopCol, nint Brush)> _linearGradCache = new();
    private readonly Dictionary<(uint AccentRgb, uint SecondaryRgb, byte PeakA), (nint StopCol, nint Brush)> _specularRimCache = new();
    private readonly Dictionary<(uint Rgb, byte CenterA), (nint StopCol, nint Brush)> _radialAuraCache = new();
    private readonly Dictionary<(int StartDeg, int EndDeg, int HalfPxRadius), nint> _arcGeoCache = new();

    public D2DRenderer()
    {
        var guidD2d = Direct2D.IID_ID2D1Factory;
        Direct2D.D2D1CreateFactory(Direct2D.D2D1_FACTORY_TYPE_SINGLE_THREADED, in guidD2d, 0, out _d2dFactory);

        var guidDw = Direct2D.IID_IDWriteFactory;
        Direct2D.DWriteCreateFactory(Direct2D.DWRITE_FACTORY_TYPE_SHARED, in guidDw, out _dwFactory);

        if (_d2dFactory != 0)
        {
            var strokeProps = new Direct2D.D2D1_STROKE_STYLE_PROPERTIES
            {
                startCap = Direct2D.D2D1_CAP_STYLE_ROUND,
                endCap = Direct2D.D2D1_CAP_STYLE_ROUND,
                dashCap = Direct2D.D2D1_CAP_STYLE_ROUND,
                lineJoin = Direct2D.D2D1_LINE_JOIN_ROUND,
                miterLimit = 10f
            };
            Direct2D.Factory_CreateStrokeStyle(_d2dFactory, in strokeProps, null, 0, out _roundStrokeStyle);
        }
    }

    public bool IsAvailable => _d2dFactory != 0 && _dwFactory != 0;

    public bool BeginFrame(nint hwnd, int width, int height)
    {
        if (_disposed || hwnd == 0 || !IsAvailable)
            return false;

        int w = Math.Max(1, width);
        int h = Math.Max(1, height);

        if (!EnsureRenderTarget(hwnd, w, h))
            return false;

        _clipStackDepth = 0;
        _clipPushedMask = 0;
        _clipOverflowStack.Clear();
        _transformStack.Clear();
        _currentTransform = Direct2D.D2D1_MATRIX_3X2_F.Identity;
        Direct2D.RenderTarget_SetTransform(_renderTarget, _currentTransform);
        Direct2D.RenderTarget_BeginDraw(_renderTarget);
        return true;
    }

    public bool EndFrame(out bool deviceLost)
    {
        deviceLost = false;
        if (_renderTarget == 0)
            return false;

        int hr = Direct2D.RenderTarget_EndDraw(_renderTarget);
        if (hr == Direct2D.D2DERR_RECREATE_TARGET)
        {
            deviceLost = true;
            ReleaseDeviceResources();
            return false;
        }
        return hr >= 0;
    }

    public bool Resize(int width, int height)
    {
        if (_renderTarget == 0)
            return false;
        uint w = (uint)Math.Max(1, width);
        uint h = (uint)Math.Max(1, height);
        int hr = Direct2D.HwndRenderTarget_Resize(_renderTarget, w, h);
        if (hr < 0)
            return false;
        _rtW = (int)w;
        _rtH = (int)h;
        return true;
    }

    private bool EnsureRenderTarget(nint hwnd, int w, int h)
    {
        if (_renderTarget != 0 && _boundHwnd == hwnd)
        {
            if (_rtW == w && _rtH == h)
                return true;
            if (Resize(w, h))
                return true;
        }

        ReleaseDeviceResources();

        var rtProps = new Direct2D.D2D1_RENDER_TARGET_PROPERTIES
        {
            type = 0, // D2D1_RENDER_TARGET_TYPE_DEFAULT (Hardware GPU; VSync paced externally via DwmFlush)
            pixelFormat = new Direct2D.D2D1_PIXEL_FORMAT
            {
                format = Direct2D.DXGI_FORMAT_B8G8R8A8_UNORM,
                alphaMode = Direct2D.D2D1_ALPHA_MODE_IGNORE
            },
            dpiX = 96f,
            dpiY = 96f
        };

        var hwndProps = new Direct2D.D2D1_HWND_RENDER_TARGET_PROPERTIES
        {
            hwnd = hwnd,
            pixelSize = new Direct2D.D2D1_SIZE_U { width = (uint)w, height = (uint)h },
            presentOptions = Direct2D.D2D1_PRESENT_OPTIONS_IMMEDIATELY
        };

        int hr = Direct2D.Factory_CreateHwndRenderTarget(_d2dFactory, in rtProps, in hwndProps, out _renderTarget);
        if (hr < 0 || _renderTarget == 0)
        {
            _renderTarget = 0;
            return false;
        }

        _boundHwnd = hwnd;
        _rtW = w;
        _rtH = h;

        Direct2D.RenderTarget_SetDpi(_renderTarget, 96f, 96f);
        Direct2D.RenderTarget_SetAntialiasMode(_renderTarget, Direct2D.D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
        Direct2D.RenderTarget_SetTextAntialiasMode(_renderTarget, Direct2D.D2D1_TEXT_ANTIALIAS_MODE_CLEARTYPE);

        var initColor = Direct2D.D2D1_COLOR_F.FromRgb(0xFFFFFF);
        hr = Direct2D.RenderTarget_CreateSolidColorBrush(_renderTarget, in initColor, out _solidBrush);
        if (hr < 0 || _solidBrush == 0)
        {
            ReleaseDeviceResources();
            return false;
        }

        return true;
    }

    public void ClearIconCache()
    {
        foreach (nint bmp in _iconBitmapCache.Values)
        {
            if (bmp != 0) Direct2D.Release(bmp);
        }
        _iconBitmapCache.Clear();
    }

    private static void ClearBrushCache<TKey>(Dictionary<TKey, (nint StopCol, nint Brush)> cache) where TKey : notnull
    {
        foreach (var entry in cache.Values)
        {
            if (entry.Brush != 0) Direct2D.Release(entry.Brush);
            if (entry.StopCol != 0) Direct2D.Release(entry.StopCol);
        }
        cache.Clear();
    }

    public void ReleaseDeviceResources()
    {
        ClearIconCache();
        ClearBrushCache(_linearGradCache);
        ClearBrushCache(_specularRimCache);
        ClearBrushCache(_radialAuraCache);
        _clipStackDepth = 0;
        _clipPushedMask = 0;
        _clipOverflowStack.Clear();
        _transformStack.Clear();
        _currentTransform = Direct2D.D2D1_MATRIX_3X2_F.Identity;

        if (_solidBrush != 0)
        {
            Direct2D.Release(_solidBrush);
            _solidBrush = 0;
        }
        if (_renderTarget != 0)
        {
            Direct2D.Release(_renderTarget);
            _renderTarget = 0;
        }
        _boundHwnd = 0;
        _rtW = 0;
        _rtH = 0;
    }

    public void ClearTextFormatCache()
    {
        foreach (var entry in _textFormatCache.Values)
        {
            if (entry.TrimSign != 0) Direct2D.Release(entry.TrimSign);
            if (entry.Format != 0) Direct2D.Release(entry.Format);
        }
        _textFormatCache.Clear();
        _textWidthCache.Clear();
    }

    private nint SetSolidBrush(uint rgb, float alpha = 1f)
    {
        var col = Direct2D.D2D1_COLOR_F.FromRgb(rgb, alpha);
        Direct2D.SolidBrush_SetColor(_solidBrush, in col);
        return _solidBrush;
    }

    public void Clear(uint rgb)
    {
        if (_renderTarget == 0) return;
        var c = Direct2D.D2D1_COLOR_F.FromRgb(rgb);
        Direct2D.RenderTarget_Clear(_renderTarget, in c);
    }

    public void SetTransform(in Direct2D.D2D1_MATRIX_3X2_F matrix)
    {
        if (_renderTarget == 0) return;
        _currentTransform = matrix;
        Direct2D.RenderTarget_SetTransform(_renderTarget, in matrix);
    }

    public void SetTranslation(float dx, float dy)
    {
        if (_renderTarget == 0) return;
        _currentTransform = Direct2D.D2D1_MATRIX_3X2_F.Translation(dx, dy);
        Direct2D.RenderTarget_SetTransform(_renderTarget, in _currentTransform);
    }

    public void ResetTransform()
    {
        if (_renderTarget == 0) return;
        _currentTransform = Direct2D.D2D1_MATRIX_3X2_F.Identity;
        Direct2D.RenderTarget_SetTransform(_renderTarget, in _currentTransform);
    }

    public void PushTransform(in Direct2D.D2D1_MATRIX_3X2_F localMatrix)
    {
        _transformStack.Push(_currentTransform);
        _currentTransform = Direct2D.D2D1_MATRIX_3X2_F.Multiply(in localMatrix, in _currentTransform);
        if (_renderTarget != 0)
            Direct2D.RenderTarget_SetTransform(_renderTarget, in _currentTransform);
    }

    public void PopTransform()
    {
        _currentTransform = _transformStack.Count > 0
            ? _transformStack.Pop()
            : Direct2D.D2D1_MATRIX_3X2_F.Identity;
        if (_renderTarget != 0)
            Direct2D.RenderTarget_SetTransform(_renderTarget, in _currentTransform);
    }

    public void SetTextAntialiasGrayscale(bool grayscale)
    {
        if (_renderTarget == 0) return;
        Direct2D.RenderTarget_SetTextAntialiasMode(
            _renderTarget,
            grayscale ? Direct2D.D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE : Direct2D.D2D1_TEXT_ANTIALIAS_MODE_CLEARTYPE);
    }

    public void PushClip(float x, float y, float w, float h)
    {
        bool pushed = _renderTarget != 0 && w > 0f && h > 0f;
        if (_clipStackDepth < 64)
        {
            if (pushed)
                _clipPushedMask |= 1UL << _clipStackDepth;
            else
                _clipPushedMask &= ~(1UL << _clipStackDepth);
        }
        else
        {
            _clipOverflowStack.Push(pushed);
        }
        _clipStackDepth++;

        if (pushed)
        {
            var r = new Direct2D.D2D1_RECT_F { left = x, top = y, right = x + w, bottom = y + h };
            Direct2D.RenderTarget_PushAxisAlignedClip(_renderTarget, in r);
        }
    }

    public void PopClip()
    {
        if (_clipStackDepth <= 0)
            return;
        _clipStackDepth--;

        bool wasPushed = _clipStackDepth < 64
            ? (_clipPushedMask & (1UL << _clipStackDepth)) != 0
            : (_clipOverflowStack.Count > 0 && _clipOverflowStack.Pop());

        if (wasPushed && _renderTarget != 0)
            Direct2D.RenderTarget_PopAxisAlignedClip(_renderTarget);
    }

    public void FillRect(float x, float y, float w, float h, uint rgb, float alpha = 1f)
    {
        if (_renderTarget == 0 || w <= 0f || h <= 0f || alpha <= 0f) return;
        var r = new Direct2D.D2D1_RECT_F { left = x, top = y, right = x + w, bottom = y + h };
        Direct2D.RenderTarget_FillRectangle(_renderTarget, in r, SetSolidBrush(rgb, alpha));
    }

    public void FillRoundedRect(float x, float y, float w, float h, float radius, uint rgb, float alpha = 1f)
    {
        if (_renderTarget == 0 || w <= 0f || h <= 0f || alpha <= 0f) return;
        if (radius <= 0.4f)
        {
            FillRect(x, y, w, h, rgb, alpha);
            return;
        }
        float rad = MathF.Min(radius, MathF.Min(w, h) * 0.5f);
        var rr = new Direct2D.D2D1_ROUNDED_RECT
        {
            rect = new Direct2D.D2D1_RECT_F { left = x, top = y, right = x + w, bottom = y + h },
            radiusX = rad,
            radiusY = rad
        };
        Direct2D.RenderTarget_FillRoundedRectangle(_renderTarget, in rr, SetSolidBrush(rgb, alpha));
    }

    public void DrawRoundedRect(float x, float y, float w, float h, float radius, uint rgb, float strokeWidth = 1f, float alpha = 1f)
    {
        if (_renderTarget == 0 || w <= 0f || h <= 0f || alpha <= 0f) return;
        float rad = MathF.Min(radius, MathF.Min(w, h) * 0.5f);
        var rr = new Direct2D.D2D1_ROUNDED_RECT
        {
            rect = new Direct2D.D2D1_RECT_F { left = x + 0.5f, top = y + 0.5f, right = x + w - 0.5f, bottom = y + h - 0.5f },
            radiusX = rad,
            radiusY = rad
        };
        Direct2D.RenderTarget_DrawRoundedRectangle(_renderTarget, in rr, SetSolidBrush(rgb, alpha), strokeWidth);
    }

    public void FillAndStrokeRoundedRect(
        float x, float y, float w, float h, float radius,
        uint fillRgb, float fillAlpha,
        uint borderRgb, float borderAlpha,
        float borderWidth = 1f)
    {
        if (fillAlpha > 0f)
            FillRoundedRect(x, y, w, h, radius, fillRgb, fillAlpha);
        if (borderAlpha > 0f)
            DrawRoundedRect(x, y, w, h, radius, borderRgb, borderWidth, borderAlpha);
    }

    public void FillCircle(float x, float y, float diameter, uint rgb, float alpha = 1f)
    {
        if (_renderTarget == 0 || diameter <= 0f || alpha <= 0f) return;
        float r = diameter * 0.5f;
        var el = new Direct2D.D2D1_ELLIPSE
        {
            point = new Direct2D.D2D1_POINT_2F { x = x + r, y = y + r },
            radiusX = r,
            radiusY = r
        };
        Direct2D.RenderTarget_FillEllipse(_renderTarget, in el, SetSolidBrush(rgb, alpha));
    }

    public void StrokeCircle(float x, float y, float diameter, uint rgb, float strokeWidth = 1.2f, float alpha = 1f)
    {
        if (_renderTarget == 0 || diameter <= 0f || alpha <= 0f) return;
        float r = diameter * 0.5f;
        var el = new Direct2D.D2D1_ELLIPSE
        {
            point = new Direct2D.D2D1_POINT_2F { x = x + r, y = y + r },
            radiusX = r,
            radiusY = r
        };
        Direct2D.RenderTarget_DrawEllipse(_renderTarget, in el, SetSolidBrush(rgb, alpha), strokeWidth);
    }

    public void DrawLine(float x1, float y1, float x2, float y2, uint rgb, float strokeWidth = 1.5f, float alpha = 1f, bool roundCaps = true)
    {
        if (_renderTarget == 0 || alpha <= 0f) return;
        Direct2D.RenderTarget_DrawLine(
            _renderTarget,
            new Direct2D.D2D1_POINT_2F { x = x1, y = y1 },
            new Direct2D.D2D1_POINT_2F { x = x2, y = y2 },
            SetSolidBrush(rgb, alpha),
            strokeWidth,
            roundCaps ? _roundStrokeStyle : 0);
    }

    public void FillLinearGradientRoundedRect(
        float x, float y, float w, float h, float radius,
        uint startRgb, float startAlpha,
        uint endRgb, float endAlpha,
        bool vertical = false)
    {
        if (_renderTarget == 0 || w <= 0f || h <= 0f) return;

        byte sA = (byte)Math.Clamp((int)MathF.Round(startAlpha * 255f), 0, 255);
        byte eA = (byte)Math.Clamp((int)MathF.Round(endAlpha * 255f), 0, 255);
        var key = (startRgb, sA, endRgb, eA);
        if (!_linearGradCache.TryGetValue(key, out var entry) || entry.Brush == 0)
        {
            if (_linearGradCache.Count >= 48)
                ClearBrushCache(_linearGradCache);

            var stops = stackalloc Direct2D.D2D1_GRADIENT_STOP[2];
            stops[0] = new Direct2D.D2D1_GRADIENT_STOP { position = 0f, color = Direct2D.D2D1_COLOR_F.FromRgb(startRgb, sA / 255f) };
            stops[1] = new Direct2D.D2D1_GRADIENT_STOP { position = 1f, color = Direct2D.D2D1_COLOR_F.FromRgb(endRgb, eA / 255f) };

            if (Direct2D.RenderTarget_CreateGradientStopCollection(
                    _renderTarget, stops, 2, Direct2D.D2D1_GAMMA_2_2, Direct2D.D2D1_EXTEND_MODE_CLAMP, out nint stopCol) < 0 || stopCol == 0)
            {
                FillRoundedRect(x, y, w, h, radius, endRgb, endAlpha);
                return;
            }

            var props = new Direct2D.D2D1_LINEAR_GRADIENT_BRUSH_PROPERTIES
            {
                startPoint = new Direct2D.D2D1_POINT_2F { x = x, y = y },
                endPoint = vertical
                    ? new Direct2D.D2D1_POINT_2F { x = x, y = y + h }
                    : new Direct2D.D2D1_POINT_2F { x = x + w, y = y }
            };
            if (Direct2D.RenderTarget_CreateLinearGradientBrush(_renderTarget, in props, stopCol, out nint gradBrush) < 0 || gradBrush == 0)
            {
                Direct2D.Release(stopCol);
                FillRoundedRect(x, y, w, h, radius, endRgb, endAlpha);
                return;
            }
            entry = (stopCol, gradBrush);
            _linearGradCache[key] = entry;
        }
        else
        {
            Direct2D.LinearGradientBrush_SetStartPoint(entry.Brush, new Direct2D.D2D1_POINT_2F { x = x, y = y });
            Direct2D.LinearGradientBrush_SetEndPoint(entry.Brush, vertical
                ? new Direct2D.D2D1_POINT_2F { x = x, y = y + h }
                : new Direct2D.D2D1_POINT_2F { x = x + w, y = y });
        }

        float rad = MathF.Min(radius, MathF.Min(w, h) * 0.5f);
        var rr = new Direct2D.D2D1_ROUNDED_RECT
        {
            rect = new Direct2D.D2D1_RECT_F { left = x, top = y, right = x + w, bottom = y + h },
            radiusX = rad,
            radiusY = rad
        };
        Direct2D.RenderTarget_FillRoundedRectangle(_renderTarget, in rr, entry.Brush);
    }

    public void FillSpecularRim(float x, float y, float w, float h, uint accentRgb, uint secondaryRgb, float peakAlpha = 0.65f)
    {
        if (_renderTarget == 0 || w <= 0f || h <= 0f) return;

        byte pA = (byte)Math.Clamp((int)MathF.Round(peakAlpha * 255f), 0, 255);
        var key = (accentRgb, secondaryRgb, pA);
        if (!_specularRimCache.TryGetValue(key, out var entry) || entry.Brush == 0)
        {
            if (_specularRimCache.Count >= 24)
                ClearBrushCache(_specularRimCache);

            float aNorm = pA / 255f;
            var stops = stackalloc Direct2D.D2D1_GRADIENT_STOP[4];
            stops[0] = new Direct2D.D2D1_GRADIENT_STOP { position = 0.04f, color = Direct2D.D2D1_COLOR_F.FromRgb(accentRgb, 0f) };
            stops[1] = new Direct2D.D2D1_GRADIENT_STOP { position = 0.35f, color = Direct2D.D2D1_COLOR_F.FromRgb(accentRgb, aNorm) };
            stops[2] = new Direct2D.D2D1_GRADIENT_STOP { position = 0.65f, color = Direct2D.D2D1_COLOR_F.FromRgb(secondaryRgb, aNorm * 0.85f) };
            stops[3] = new Direct2D.D2D1_GRADIENT_STOP { position = 0.96f, color = Direct2D.D2D1_COLOR_F.FromRgb(secondaryRgb, 0f) };

            if (Direct2D.RenderTarget_CreateGradientStopCollection(
                    _renderTarget, stops, 4, Direct2D.D2D1_GAMMA_2_2, Direct2D.D2D1_EXTEND_MODE_CLAMP, out nint stopCol) < 0 || stopCol == 0)
                return;

            var props = new Direct2D.D2D1_LINEAR_GRADIENT_BRUSH_PROPERTIES
            {
                startPoint = new Direct2D.D2D1_POINT_2F { x = x, y = y },
                endPoint = new Direct2D.D2D1_POINT_2F { x = x + w, y = y }
            };
            if (Direct2D.RenderTarget_CreateLinearGradientBrush(_renderTarget, in props, stopCol, out nint gradBrush) < 0 || gradBrush == 0)
            {
                Direct2D.Release(stopCol);
                return;
            }
            entry = (stopCol, gradBrush);
            _specularRimCache[key] = entry;
        }
        else
        {
            Direct2D.LinearGradientBrush_SetStartPoint(entry.Brush, new Direct2D.D2D1_POINT_2F { x = x, y = y });
            Direct2D.LinearGradientBrush_SetEndPoint(entry.Brush, new Direct2D.D2D1_POINT_2F { x = x + w, y = y });
        }

        var r = new Direct2D.D2D1_RECT_F { left = x, top = y, right = x + w, bottom = y + h };
        Direct2D.RenderTarget_FillRectangle(_renderTarget, in r, entry.Brush);
    }

    public void FillRadialAura(float cx, float cy, float radiusX, float radiusY, uint rgb, float centerAlpha)
    {
        if (_renderTarget == 0 || radiusX <= 1f || radiusY <= 1f || centerAlpha <= 0f) return;

        byte cA = (byte)Math.Clamp((int)MathF.Round(centerAlpha * 255f), 0, 255);
        var key = (rgb, cA);
        if (!_radialAuraCache.TryGetValue(key, out var entry) || entry.Brush == 0)
        {
            if (_radialAuraCache.Count >= 24)
                ClearBrushCache(_radialAuraCache);

            float aNorm = cA / 255f;
            var stops = stackalloc Direct2D.D2D1_GRADIENT_STOP[3];
            stops[0] = new Direct2D.D2D1_GRADIENT_STOP { position = 0f, color = Direct2D.D2D1_COLOR_F.FromRgb(rgb, aNorm) };
            stops[1] = new Direct2D.D2D1_GRADIENT_STOP { position = 0.45f, color = Direct2D.D2D1_COLOR_F.FromRgb(rgb, aNorm * 0.45f) };
            stops[2] = new Direct2D.D2D1_GRADIENT_STOP { position = 1f, color = Direct2D.D2D1_COLOR_F.FromRgb(rgb, 0f) };

            if (Direct2D.RenderTarget_CreateGradientStopCollection(
                    _renderTarget, stops, 3, Direct2D.D2D1_GAMMA_2_2, Direct2D.D2D1_EXTEND_MODE_CLAMP, out nint stopCol) < 0 || stopCol == 0)
                return;

            var props = new Direct2D.D2D1_RADIAL_GRADIENT_BRUSH_PROPERTIES
            {
                center = new Direct2D.D2D1_POINT_2F { x = cx, y = cy },
                gradientOriginOffset = default,
                radiusX = radiusX,
                radiusY = radiusY
            };
            if (Direct2D.RenderTarget_CreateRadialGradientBrush(_renderTarget, in props, stopCol, out nint radBrush) < 0 || radBrush == 0)
            {
                Direct2D.Release(stopCol);
                return;
            }
            entry = (stopCol, radBrush);
            _radialAuraCache[key] = entry;
        }
        else
        {
            Direct2D.RadialGradientBrush_SetCenter(entry.Brush, new Direct2D.D2D1_POINT_2F { x = cx, y = cy });
            Direct2D.RadialGradientBrush_SetRadiusX(entry.Brush, radiusX);
            Direct2D.RadialGradientBrush_SetRadiusY(entry.Brush, radiusY);
        }

        var ellipse = new Direct2D.D2D1_ELLIPSE
        {
            point = new Direct2D.D2D1_POINT_2F { x = cx, y = cy },
            radiusX = radiusX,
            radiusY = radiusY
        };
        Direct2D.RenderTarget_FillEllipse(_renderTarget, in ellipse, entry.Brush);
    }

    public void DrawCardGlow(float x, float y, float w, float h, float radius, uint colorRgb, float intensity, bool isLight, uint dpi)
    {
        if (intensity <= 0.01f) return;

        float e2 = Theme.ScaleF(3.2f, dpi);
        DrawRoundedRect(x - e2, y - e2, w + e2 * 2f, h + e2 * 2f, radius + e2,
            colorRgb, MathF.Max(2.2f, Theme.ScaleF(2.8f, dpi)), Math.Clamp(intensity * (isLight ? 0.19f : 0.27f), 0f, 1f));

        float e1 = Theme.ScaleF(1.3f, dpi);
        DrawRoundedRect(x - e1, y - e1, w + e1 * 2f, h + e1 * 2f, radius + e1,
            colorRgb, MathF.Max(1.4f, Theme.ScaleF(1.8f, dpi)), Math.Clamp(intensity * (isLight ? 0.45f : 0.60f), 0f, 1f));

        FillRoundedRect(x, y, w, h, radius, colorRgb, Math.Clamp(intensity * (isLight ? 0.09f : 0.14f), 0f, 1f));
        DrawRoundedRect(x, y, w, h, radius, colorRgb, MathF.Max(1.5f, Theme.ScaleF(1.7f, dpi)), Math.Clamp(intensity * 0.96f, 0f, 1f));
    }

    private nint GetOrCreateTextFormat(int ptSize, int weight, uint dpi, TextAlign hAlign, VerticalAlign vAlign, bool wrap, bool ellipsis)
    {
        if (_dwFactory == 0) return 0;

        string family = Strings.ResolveDirectWriteFontFamily();
        string locale = Strings.ResolveDirectWriteLocale();
        var key = new TextFormatKey(family, locale, ptSize, weight, dpi, hAlign, vAlign, wrap, ellipsis);
        if (_textFormatCache.TryGetValue(key, out var cached) && cached.Format != 0)
            return cached.Format;

        if (_textFormatCache.Count >= 96)
            ClearTextFormatCache();

        float fontSizePx = MathF.Max(8f, ptSize * (96f / 72f) * (dpi / 96f) * 0.92f);
        if (Direct2D.DWrite_CreateTextFormat(
                _dwFactory,
                family,
                (uint)weight,
                Direct2D.DWRITE_FONT_STYLE_NORMAL,
                Direct2D.DWRITE_FONT_STRETCH_NORMAL,
                fontSizePx,
                locale,
                out nint fmt) < 0 || fmt == 0)
        {
            return 0;
        }

        Direct2D.TextFormat_SetTextAlignment(fmt, (uint)hAlign);
        Direct2D.TextFormat_SetParagraphAlignment(fmt, (uint)vAlign);
        Direct2D.TextFormat_SetWordWrapping(fmt, wrap ? Direct2D.DWRITE_WORD_WRAPPING_WRAP : Direct2D.DWRITE_WORD_WRAPPING_NO_WRAP);

        nint trimSign = 0;
        if (ellipsis && Direct2D.DWrite_CreateEllipsisTrimmingSign(_dwFactory, fmt, out trimSign) == 0 && trimSign != 0)
        {
            var trimming = new Direct2D.DWRITE_TRIMMING { granularity = Direct2D.DWRITE_TRIMMING_GRANULARITY_CHARACTER };
            Direct2D.TextFormat_SetTrimming(fmt, in trimming, trimSign);
        }

        _textFormatCache[key] = (fmt, trimSign);
        return fmt;
    }

    public int MeasureTextWidth(string text, int ptSize, int weight, uint dpi)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var cacheKey = (text, ptSize, weight, dpi, Strings.Current);
        if (_textWidthCache.TryGetValue(cacheKey, out int cachedWidth))
            return cachedWidth;

        if (_textWidthCache.Count >= 256)
            _textWidthCache.Clear();

        nint fmt = GetOrCreateTextFormat(ptSize, weight, dpi, TextAlign.Left, VerticalAlign.Top, wrap: false, ellipsis: false);
        if (fmt == 0 || _dwFactory == 0)
            return (int)MathF.Ceiling(text.Length * Theme.ScaleF(ptSize, dpi) * 0.6f);

        if (Direct2D.DWrite_CreateTextLayout(_dwFactory, text, fmt, 4000f, 400f, out nint layout) == 0 && layout != 0)
        {
            try
            {
                if (Direct2D.TextLayout_GetMetrics(layout, out var m) == 0)
                {
                    int width = (int)MathF.Ceiling(m.widthIncludingTrailingWhitespace);
                    _textWidthCache[cacheKey] = width;
                    return width;
                }
            }
            finally
            {
                Direct2D.Release(layout);
            }
        }
        return (int)MathF.Ceiling(text.Length * Theme.ScaleF(ptSize, dpi) * 0.6f);
    }

    public void DrawText(
        string text,
        float left,
        float top,
        float right,
        float bottom,
        int ptSize,
        int weight,
        uint rgb,
        uint dpi,
        bool centered = false,
        TextAlign? align = null,
        VerticalAlign vAlign = VerticalAlign.Center,
        float alpha = 1f,
        bool allowColorFont = false)
    {
        if (_renderTarget == 0 || string.IsNullOrEmpty(text) || right <= left || bottom <= top || alpha <= 0f)
            return;

        TextAlign hAlign = align ?? (centered ? TextAlign.Center : TextAlign.Left);
        nint fmt = GetOrCreateTextFormat(ptSize, weight, dpi, hAlign, vAlign, wrap: false, ellipsis: true);
        if (fmt == 0) return;

        var rc = new Direct2D.D2D1_RECT_F { left = left, top = top, right = right, bottom = bottom };
        uint opts = Direct2D.D2D1_DRAW_TEXT_OPTIONS_CLIP |
                    (allowColorFont ? Direct2D.D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT : 0u);
        Direct2D.RenderTarget_DrawText(
            _renderTarget,
            text,
            fmt,
            in rc,
            SetSolidBrush(rgb, alpha),
            opts);
    }

    public void DrawWrappedText(
        string text,
        float left,
        float top,
        float right,
        float bottom,
        int ptSize,
        int weight,
        uint rgb,
        uint dpi,
        float alpha = 1f,
        bool allowColorFont = false)
    {
        if (_renderTarget == 0 || string.IsNullOrEmpty(text) || right <= left || bottom <= top || alpha <= 0f)
            return;

        nint fmt = GetOrCreateTextFormat(ptSize, weight, dpi, TextAlign.Left, VerticalAlign.Top, wrap: true, ellipsis: true);
        if (fmt == 0) return;

        var rc = new Direct2D.D2D1_RECT_F { left = left, top = top, right = right, bottom = bottom };
        uint opts = Direct2D.D2D1_DRAW_TEXT_OPTIONS_CLIP |
                    (allowColorFont ? Direct2D.D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT : 0u);
        Direct2D.RenderTarget_DrawText(
            _renderTarget,
            text,
            fmt,
            in rc,
            SetSolidBrush(rgb, alpha),
            opts);
    }

    public bool DrawIcon(nint hIcon, float x, float y, float size)
    {
        if (_renderTarget == 0 || hIcon == 0)
            return false;

        int px = Math.Clamp((int)MathF.Round(size), 12, 96);
        nint bmp = GetOrCreateIconBitmap(hIcon, px);
        if (bmp == 0)
            return false;

        var dest = new Direct2D.D2D1_RECT_F { left = x, top = y, right = x + size, bottom = y + size };
        Direct2D.RenderTarget_DrawBitmap(_renderTarget, bmp, in dest, 1f, Direct2D.D2D1_BITMAP_INTERPOLATION_MODE_LINEAR);
        return true;
    }

    private nint GetOrCreateIconBitmap(nint hIcon, int size)
    {
        var key = (hIcon, size);
        if (_iconBitmapCache.TryGetValue(key, out nint cached) && cached != 0)
            return cached;

        if (_iconBitmapCache.Count >= 128)
        {
            foreach (nint old in _iconBitmapCache.Values)
            {
                if (old != 0) Direct2D.Release(old);
            }
            _iconBitmapCache.Clear();
        }

        nint srcBmp = 0;
        nint dstBmp = 0;
        nint gfx = 0;
        int stride = size * 4;
        byte* pixels = (byte*)NativeMemory.AllocZeroed((nuint)(stride * size));

        try
        {
            if (GdiPlus.GdipCreateBitmapFromHICON(hIcon, out srcBmp) != 0 || srcBmp == 0)
                return 0;
            if (GdiPlus.GdipCreateBitmapFromScan0(size, size, stride, GdiPlus.PixelFormat32bppPARGB, (nint)pixels, out dstBmp) != 0 || dstBmp == 0)
                return 0;
            if (GdiPlus.GdipGetImageGraphicsContext(dstBmp, out gfx) != 0 || gfx == 0)
                return 0;

            GdiPlus.GdipSetSmoothingMode(gfx, GdiPlus.SmoothingModeAntiAlias);
            GdiPlus.GdipSetInterpolationMode(gfx, GdiPlus.InterpolationModeHighQualityBicubic);
            GdiPlus.GdipGraphicsClear(gfx, 0x00000000);
            GdiPlus.GdipDrawImageRectI(gfx, srcBmp, 0, 0, size, size);

            var bmpProps = new Direct2D.D2D1_BITMAP_PROPERTIES
            {
                pixelFormat = new Direct2D.D2D1_PIXEL_FORMAT
                {
                    format = Direct2D.DXGI_FORMAT_B8G8R8A8_UNORM,
                    alphaMode = Direct2D.D2D1_ALPHA_MODE_PREMULTIPLIED
                },
                dpiX = 96f,
                dpiY = 96f
            };
            var sz = new Direct2D.D2D1_SIZE_U { width = (uint)size, height = (uint)size };
            if (Direct2D.RenderTarget_CreateBitmap(_renderTarget, sz, pixels, (uint)stride, in bmpProps, out nint d2dBmp) == 0 && d2dBmp != 0)
            {
                _iconBitmapCache[key] = d2dBmp;
                return d2dBmp;
            }
            return 0;
        }
        finally
        {
            if (gfx != 0) GdiPlus.GdipDeleteGraphics(gfx);
            if (dstBmp != 0) GdiPlus.GdipDisposeImage(dstBmp);
            if (srcBmp != 0) GdiPlus.GdipDisposeImage(srcBmp);
            NativeMemory.Free(pixels);
        }
    }

    public void FillTriangle(float x1, float y1, float x2, float y2, float x3, float y3, uint rgb, float alpha = 1f)
    {
        if (_renderTarget == 0 || _d2dFactory == 0) return;
        if (Direct2D.Factory_CreatePathGeometry(_d2dFactory, out nint geo) < 0 || geo == 0) return;

        nint sink = 0;
        try
        {
            if (Direct2D.PathGeometry_Open(geo, out sink) < 0 || sink == 0) return;
            Direct2D.Sink_BeginFigure(sink, new Direct2D.D2D1_POINT_2F { x = x1, y = y1 }, Direct2D.D2D1_FIGURE_BEGIN_FILLED);
            Direct2D.Sink_AddLine(sink, new Direct2D.D2D1_POINT_2F { x = x2, y = y2 });
            Direct2D.Sink_AddLine(sink, new Direct2D.D2D1_POINT_2F { x = x3, y = y3 });
            Direct2D.Sink_EndFigure(sink, Direct2D.D2D1_FIGURE_END_CLOSED);
            Direct2D.Sink_Close(sink);
            Direct2D.RenderTarget_FillGeometry(_renderTarget, geo, SetSolidBrush(rgb, alpha));
        }
        finally
        {
            if (sink != 0) Direct2D.Release(sink);
            Direct2D.Release(geo);
        }
    }

    private nint GetOrCreateSpeakerBodyGeo()
    {
        if (_speakerBodyGeo != 0 || _d2dFactory == 0)
            return _speakerBodyGeo;

        if (Direct2D.Factory_CreatePathGeometry(_d2dFactory, out nint geo) < 0 || geo == 0)
            return 0;

        nint sink = 0;
        bool ok = false;
        try
        {
            if (Direct2D.PathGeometry_Open(geo, out sink) == 0 && sink != 0)
            {
                Direct2D.Sink_BeginFigure(sink, new Direct2D.D2D1_POINT_2F { x = -1.15f, y = -0.42f }, Direct2D.D2D1_FIGURE_BEGIN_FILLED);
                Direct2D.Sink_AddLine(sink, new Direct2D.D2D1_POINT_2F { x = -0.55f, y = -0.42f });
                Direct2D.Sink_AddLine(sink, new Direct2D.D2D1_POINT_2F { x = 0.12f, y = -0.95f });
                Direct2D.Sink_AddLine(sink, new Direct2D.D2D1_POINT_2F { x = 0.12f, y = 0.95f });
                Direct2D.Sink_AddLine(sink, new Direct2D.D2D1_POINT_2F { x = -0.55f, y = 0.42f });
                Direct2D.Sink_AddLine(sink, new Direct2D.D2D1_POINT_2F { x = -1.15f, y = 0.42f });
                Direct2D.Sink_EndFigure(sink, Direct2D.D2D1_FIGURE_END_CLOSED);
                Direct2D.Sink_Close(sink);
                _speakerBodyGeo = geo;
                ok = true;
            }
        }
        finally
        {
            if (sink != 0) Direct2D.Release(sink);
            if (!ok) Direct2D.Release(geo);
        }
        return _speakerBodyGeo;
    }

    public void DrawSpeakerGlyph(float x, float y, float sz, bool isMuted, uint rgb, uint dpi)
    {
        if (_renderTarget == 0 || _d2dFactory == 0) return;

        float cx = x + sz * 0.5f;
        float cy = y + sz * 0.5f;
        float u = sz * 0.22f;

        nint bodyGeo = GetOrCreateSpeakerBodyGeo();
        if (bodyGeo != 0 && u > 0.01f)
        {
            PushTransform(Direct2D.D2D1_MATRIX_3X2_F.TranslateAndScaleAt(cx, cy, u, u, 0f, 0f));
            Direct2D.RenderTarget_FillGeometry(_renderTarget, bodyGeo, SetSolidBrush(rgb));
            PopTransform();
        }

        float strokeW = MathF.Max(1.3f, Theme.ScaleF(1.35f, dpi));
        if (isMuted)
        {
            float mx1 = cx + u * 0.48f;
            float mx2 = cx + u * 1.15f;
            DrawLine(mx1, cy - u * 0.42f, mx2, cy + u * 0.42f, rgb, strokeW);
            DrawLine(mx2, cy - u * 0.42f, mx1, cy + u * 0.42f, rgb, strokeW);
        }
        else
        {
            DrawArc(cx, cy, u * 0.55f, -50f, 50f, rgb, strokeW);
            DrawArc(cx, cy, u * 0.95f, -50f, 50f, rgb, strokeW);
        }
    }

    private void DrawArc(float cx, float cy, float radius, float startDeg, float endDeg, uint rgb, float strokeWidth)
    {
        if (_renderTarget == 0 || _d2dFactory == 0 || radius <= 0.5f) return;

        int sDeg = (int)MathF.Round(startDeg);
        int eDeg = (int)MathF.Round(endDeg);
        int halfPxR = Math.Clamp((int)MathF.Round(radius * 2f), 1, 2000);
        float actualR = halfPxR * 0.5f;
        var key = (sDeg, eDeg, halfPxR);

        if (!_arcGeoCache.TryGetValue(key, out nint geo) || geo == 0)
        {
            if (_arcGeoCache.Count >= 48)
            {
                foreach (nint g in _arcGeoCache.Values)
                {
                    if (g != 0) Direct2D.Release(g);
                }
                _arcGeoCache.Clear();
            }

            float r0 = sDeg * (MathF.PI / 180f);
            float r1 = eDeg * (MathF.PI / 180f);
            var p0 = new Direct2D.D2D1_POINT_2F { x = MathF.Cos(r0) * actualR, y = MathF.Sin(r0) * actualR };
            var p1 = new Direct2D.D2D1_POINT_2F { x = MathF.Cos(r1) * actualR, y = MathF.Sin(r1) * actualR };

            if (Direct2D.Factory_CreatePathGeometry(_d2dFactory, out nint createdGeo) < 0 || createdGeo == 0)
                return;

            nint sink = 0;
            bool ok = false;
            try
            {
                if (Direct2D.PathGeometry_Open(createdGeo, out sink) == 0 && sink != 0)
                {
                    Direct2D.Sink_BeginFigure(sink, p0, Direct2D.D2D1_FIGURE_BEGIN_HOLLOW);
                    var seg = new Direct2D.D2D1_ARC_SEGMENT
                    {
                        point = p1,
                        size = new Direct2D.D2D1_SIZE_F { width = actualR, height = actualR },
                        sweepDirection = Direct2D.D2D1_SWEEP_DIRECTION_CLOCKWISE,
                        arcSize = Math.Abs(eDeg - sDeg) > 180 ? Direct2D.D2D1_ARC_SIZE_LARGE : Direct2D.D2D1_ARC_SIZE_SMALL
                    };
                    Direct2D.Sink_AddArc(sink, in seg);
                    Direct2D.Sink_EndFigure(sink, Direct2D.D2D1_FIGURE_END_OPEN);
                    Direct2D.Sink_Close(sink);
                    geo = createdGeo;
                    _arcGeoCache[key] = geo;
                    ok = true;
                }
            }
            finally
            {
                if (sink != 0) Direct2D.Release(sink);
                if (!ok) Direct2D.Release(createdGeo);
            }
        }

        if (geo != 0)
        {
            PushTransform(Direct2D.D2D1_MATRIX_3X2_F.Translation(cx, cy));
            Direct2D.RenderTarget_DrawGeometry(_renderTarget, geo, SetSolidBrush(rgb), strokeWidth, _roundStrokeStyle);
            PopTransform();
        }
    }

    public void DrawGlobeGlyph(float x, float y, float sz, uint rgb, uint dpi)
    {
        float pad = sz * 0.10f;
        float left = x + pad;
        float top = y + pad;
        float d = MathF.Max(4f, sz - pad * 2f);
        float r = d * 0.5f;
        float midX = left + r;
        float midY = top + r;
        float strokeW = MathF.Max(1.1f, Theme.ScaleF(1.15f, dpi));

        StrokeCircle(left, top, d, rgb, strokeW);
        DrawLine(left, midY, left + d, midY, rgb, strokeW);
        if (_renderTarget != 0)
        {
            var inner = new Direct2D.D2D1_ELLIPSE
            {
                point = new Direct2D.D2D1_POINT_2F { x = midX, y = midY },
                radiusX = r * 0.46f,
                radiusY = r
            };
            Direct2D.RenderTarget_DrawEllipse(_renderTarget, in inner, SetSolidBrush(rgb), strokeW);
        }
    }

    public void DrawFocusArrowGlyph(float x, float y, float sz, uint rgb, uint dpi)
    {
        float pad = sz * 0.16f;
        float left = x + pad;
        float top = y + pad;
        float right = x + sz - pad;
        float bottom = y + sz - pad;
        float w = right - left;
        float h = bottom - top;
        float midX = left + w * 0.52f;
        float midY = top + h * 0.48f;
        float sw = MathF.Max(1.25f, Theme.ScaleF(1.25f, dpi));

        DrawLine(midX, top + h * 0.15f, left, top + h * 0.15f, rgb, sw);
        DrawLine(left, top + h * 0.15f, left, bottom, rgb, sw);
        DrawLine(left, bottom, right - w * 0.15f, bottom, rgb, sw);
        DrawLine(right - w * 0.15f, bottom, right - w * 0.15f, midY, rgb, sw);

        DrawLine(midX - w * 0.08f, midY + h * 0.08f, right, top, rgb, sw);
        DrawLine(right, top, right - w * 0.36f, top, rgb, sw);
        DrawLine(right, top, right, top + h * 0.36f, rgb, sw);
    }

    public void DrawDirectionArrowGlyph(float cx, float cy, float size, bool up, uint rgb, float alpha, uint dpi)
    {
        float sw = MathF.Max(1.4f, Theme.ScaleF(1.5f, dpi));
        float h = size * 0.42f;
        float w = size * 0.34f;
        DrawLine(cx, cy - h, cx, cy + h, rgb, sw, alpha);
        if (up)
        {
            DrawLine(cx - w, cy - h * 0.1f, cx, cy - h, rgb, sw, alpha);
            DrawLine(cx + w, cy - h * 0.1f, cx, cy - h, rgb, sw, alpha);
        }
        else
        {
            DrawLine(cx - w, cy + h * 0.1f, cx, cy + h, rgb, sw, alpha);
            DrawLine(cx + w, cy + h * 0.1f, cx, cy + h, rgb, sw, alpha);
        }
    }

    public void DrawDropdownChevron(float cx, float cy, bool pointingUp, uint rgb, uint dpi)
    {
        float sw = MathF.Max(1.4f, Theme.ScaleF(1.5f, dpi));
        float d = Theme.ScaleF(4.2f, dpi);
        float dy = pointingUp ? -d * 0.55f : d * 0.55f;
        float baseY = pointingUp ? d * 0.45f : -d * 0.45f;
        DrawLine(cx - d, cy + baseY, cx, cy + dy, rgb, sw);
        DrawLine(cx, cy + dy, cx + d, cy + baseY, rgb, sw);
    }

    public void DrawAudioSwitchGlyph(float cx, float cy, float sz, uint rgb, uint dpi)
    {
        float sw = MathF.Max(1.35f, Theme.ScaleF(1.45f, dpi));
        float hw = sz * 0.25f;
        float ry = sz * 0.15f;
        float ah = sz * 0.105f;
        DrawLine(cx - hw, cy - ry, cx + hw, cy - ry, rgb, sw);
        DrawLine(cx + hw - ah, cy - ry - ah, cx + hw, cy - ry, rgb, sw);
        DrawLine(cx + hw - ah, cy - ry + ah, cx + hw, cy - ry, rgb, sw);
        DrawLine(cx + hw, cy + ry, cx - hw, cy + ry, rgb, sw);
        DrawLine(cx - hw + ah, cy + ry - ah, cx - hw, cy + ry, rgb, sw);
        DrawLine(cx - hw + ah, cy + ry + ah, cx - hw, cy + ry, rgb, sw);
    }

    public void DrawClockGlyph(float x, float y, float sz, uint rgb, uint dpi)
    {
        float sw = MathF.Max(1.2f, Theme.ScaleF(1.25f, dpi));
        StrokeCircle(x, y, sz, rgb, sw);
        float cx = x + sz * 0.5f;
        float cy = y + sz * 0.5f;
        DrawLine(cx, cy, cx, y + sz * 0.24f, rgb, sw);
        DrawLine(cx, cy, x + sz * 0.74f, cy, rgb, sw);
    }

    public void DrawRefreshGlyph(float cx, float cy, float sz, uint rgb, uint dpi)
    {
        float sw = MathF.Max(1.3f, Theme.ScaleF(1.4f, dpi));
        float r = sz * 0.34f;
        DrawArc(cx, cy, r, -42f, 240f, rgb, sw);
        float tipRad = -42f * (MathF.PI / 180f);
        float tx = cx + MathF.Cos(tipRad) * r;
        float ty = cy + MathF.Sin(tipRad) * r;
        float ah = MathF.Max(2.5f, sz * 0.16f);
        DrawLine(tx, ty, tx - ah * 0.95f, ty - ah * 0.15f, rgb, sw);
        DrawLine(tx, ty, tx - ah * 0.05f, ty - ah * 0.95f, rgb, sw);
    }

    public void DrawLockGlyph(float cx, float cy, float sz, uint rgb, uint dpi)
    {
        float sw = MathF.Max(1.3f, Theme.ScaleF(1.35f, dpi));
        float bw = sz * 0.44f;
        float bh = sz * 0.34f;
        float bodyY = cy - bh * 0.10f;
        float shackleR = sz * 0.165f;
        float shackleCy = bodyY - sz * 0.04f;
        DrawArc(cx, shackleCy, shackleR, 180f, 360f, rgb, sw);
        DrawLine(cx - shackleR, shackleCy, cx - shackleR, bodyY + 0.5f, rgb, sw);
        DrawLine(cx + shackleR, shackleCy, cx + shackleR, bodyY + 0.5f, rgb, sw);
        FillRoundedRect(cx - bw * 0.5f, bodyY, bw, bh, MathF.Max(1.6f, Theme.ScaleF(2f, dpi)), rgb);
    }

    public void DrawEqBars(float x, float y, float h, uint rgb, long nowTick, bool animate, uint dpi)
    {
        float barW = MathF.Max(2f, Theme.ScaleF(2.5f, dpi));
        float gap = MathF.Max(2f, Theme.ScaleF(2.5f, dpi));
        float minH = MathF.Max(3f, h * 0.25f);
        float t = nowTick / 180f;

        for (int i = 0; i < 4; i++)
        {
            float ratio = animate
                ? 0.32f + 0.68f * (0.5f + 0.5f * MathF.Sin(t + i * 1.45f))
                : 0.35f + (i % 2 == 0 ? 0.45f : 0.2f);
            float bh = MathF.Max(minH, h * ratio);
            float bx = x + i * (barW + gap);
            float by = y + (h - bh);
            FillRoundedRect(bx, by, barW, bh, barW * 0.5f, rgb);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClearTextFormatCache();
        ReleaseDeviceResources();

        if (_speakerBodyGeo != 0)
        {
            Direct2D.Release(_speakerBodyGeo);
            _speakerBodyGeo = 0;
        }
        foreach (nint g in _arcGeoCache.Values)
        {
            if (g != 0) Direct2D.Release(g);
        }
        _arcGeoCache.Clear();
        if (_roundStrokeStyle != 0)
        {
            Direct2D.Release(_roundStrokeStyle);
            _roundStrokeStyle = 0;
        }
        if (_dwFactory != 0)
        {
            Direct2D.Release(_dwFactory);
            _dwFactory = 0;
        }
        if (_d2dFactory != 0)
        {
            Direct2D.Release(_d2dFactory);
            _d2dFactory = 0;
        }
    }
}
