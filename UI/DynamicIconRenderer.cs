using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Cueway.Interop;
using Cueway.Media;

namespace Cueway.UI;

internal sealed unsafe class DynamicIconRenderer : IDisposable
{
    private const string PngDataUrlPrefix = "data:image/png;base64,";
    private const int MaxIconBytes = 64 * 1024;

    private static readonly string SiteIconsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Cueway",
        "SiteIcons");

    private static readonly HashSet<string> BuiltInBrandVectorSites = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube", "youtubemusic", "x", "twitch", "kick",
        "spotifyweb", "soundcloud", "netflix", "instagram", "tiktok",
        "reddit", "facebook"
    };

    private readonly object _cacheLock = new();
    private readonly Dictionary<string, nint> _exeIconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, nint> _siteIconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _cachedSiteIconHashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, nint> _brandIconCache = new();
    private readonly Queue<nint> _retiredIcons = new();
    private readonly HashSet<string> _pendingWarmups = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public event Action? IconsUpdated;

    public nint GetIconForApp(string? appKey, string? exePath)
    {
        string? siteKey = BrowserSiteScanner.ExtractSiteKeyFromAppKey(appKey);
        if (!string.IsNullOrEmpty(siteKey))
        {
            nint siteIcon = GetOrCreateSiteIcon(siteKey);
            if (siteIcon != 0)
                return siteIcon;
        }
        return GetOrExtractAppIcon(exePath);
    }

    public nint GetIconForAppNonBlocking(string? appKey, string? exePath)
    {
        if (_disposed) return 0;

        string? siteKey = BrowserSiteScanner.ExtractSiteKeyFromAppKey(appKey);
        if (!string.IsNullOrEmpty(siteKey))
        {
            string normalizedKey = siteKey.ToLowerInvariant();
            string cacheKey = $"{normalizedKey}:48";
            lock (_cacheLock)
            {
                if (_siteIconCache.TryGetValue(cacheKey, out nint siteIcon) && siteIcon != 0)
                    return siteIcon;
            }
            QueueWarmupIcon(appKey, exePath);
            return 0;
        }

        if (string.IsNullOrWhiteSpace(exePath))
            return 0;

        lock (_cacheLock)
        {
            if (_exeIconCache.TryGetValue(exePath, out nint exeIcon))
                return exeIcon;
        }

        QueueWarmupIcon(appKey, exePath);
        return 0;
    }

    public void WarmupIconsAsync(IEnumerable<(string? AppKey, string? ExePath)> items)
    {
        if (_disposed) return;

        List<(string? AppKey, string? ExePath, string WarmupKey)>? toLoad = null;
        lock (_cacheLock)
        {
            foreach (var (appKey, exePath) in items)
            {
                string? siteKey = BrowserSiteScanner.ExtractSiteKeyFromAppKey(appKey);
                if (!string.IsNullOrEmpty(siteKey))
                {
                    string norm = siteKey.ToLowerInvariant();
                    string cacheKey = $"{norm}:48";
                    if (_siteIconCache.ContainsKey(cacheKey))
                        continue;
                    string wKey = $"site:{norm}";
                    if (_pendingWarmups.Add(wKey))
                        (toLoad ??= []).Add((appKey, exePath, wKey));
                }
                else if (!string.IsNullOrWhiteSpace(exePath))
                {
                    if (_exeIconCache.ContainsKey(exePath))
                        continue;
                    string wKey = $"exe:{exePath}";
                    if (_pendingWarmups.Add(wKey))
                        (toLoad ??= []).Add((appKey, exePath, wKey));
                }
            }
        }

        if (toLoad == null || toLoad.Count == 0)
            return;

        _ = Task.Run(() =>
        {
            bool anyLoaded = false;
            try
            {
                foreach (var (appKey, exePath, _) in toLoad)
                {
                    if (_disposed) break;
                    if (GetIconForApp(appKey, exePath) != 0)
                        anyLoaded = true;
                }
            }
            catch { }
            finally
            {
                lock (_cacheLock)
                {
                    foreach (var (_, _, wKey) in toLoad)
                        _pendingWarmups.Remove(wKey);
                }
            }

            if (anyLoaded && !_disposed)
                IconsUpdated?.Invoke();
        });
    }

    private void QueueWarmupIcon(string? appKey, string? exePath)
    {
        string? siteKey = BrowserSiteScanner.ExtractSiteKeyFromAppKey(appKey);
        string? warmupKey = !string.IsNullOrEmpty(siteKey)
            ? $"site:{siteKey.ToLowerInvariant()}"
            : (!string.IsNullOrWhiteSpace(exePath) ? $"exe:{exePath}" : null);

        if (warmupKey == null)
            return;

        lock (_cacheLock)
        {
            if (_disposed || !_pendingWarmups.Add(warmupKey))
                return;
        }

        _ = Task.Run(() =>
        {
            nint loaded = 0;
            try
            {
                if (!_disposed)
                    loaded = GetIconForApp(appKey, exePath);
            }
            catch { }
            finally
            {
                lock (_cacheLock)
                {
                    _pendingWarmups.Remove(warmupKey);
                }
            }

            if (loaded != 0 && !_disposed)
                IconsUpdated?.Invoke();
        });
    }

    public void FlushRetiredIcons()
    {
        List<nint>? toDestroy = null;
        lock (_cacheLock)
        {
            while (_retiredIcons.Count > 0)
                (toDestroy ??= []).Add(_retiredIcons.Dequeue());
        }

        if (toDestroy != null)
        {
            foreach (nint h in toDestroy)
            {
                if (h != 0) Win32.DestroyIcon(h);
            }
        }
    }

    public void InvalidateThemeDependentIcons()
    {
        lock (_cacheLock)
        {
            if (_siteIconCache.Count == 0)
                return;

            List<string>? toRemove = null;
            foreach (string key in _siteIconCache.Keys)
            {
                int colonIdx = key.IndexOf(':');
                string siteKey = colonIdx > 0 ? key[..colonIdx] : key;
                if (HasBuiltInBrandVector(siteKey))
                    continue;
                string safeKey = SanitizeSiteKey(siteKey);
                if (!string.IsNullOrEmpty(safeKey) && File.Exists(Path.Combine(SiteIconsDir, $"{safeKey}.png")))
                    continue;
                (toRemove ??= []).Add(key);
            }

            if (toRemove != null)
            {
                foreach (string k in toRemove)
                {
                    if (_siteIconCache.Remove(k, out nint oldIcon) && oldIcon != 0)
                        _retiredIcons.Enqueue(oldIcon);
                }
            }
        }
    }

    public nint GetOrExtractAppIcon(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return 0;

        lock (_cacheLock)
        {
            if (_exeIconCache.TryGetValue(exePath, out nint cached))
                return cached;
        }

        nint hIcon = 0;
        if (File.Exists(exePath))
        {
            nint large = 0;
            uint count = Win32.ExtractIconExW(exePath, 0, &large, null, 1);
            if (count > 0 && large != 0)
            {
                hIcon = large;
            }
            else
            {
                hIcon = TryExtractSiblingGuiAppIcon(exePath);
            }
        }

        lock (_cacheLock)
        {
            if (_exeIconCache.TryGetValue(exePath, out nint existing))
            {
                if (hIcon != 0) Win32.DestroyIcon(hIcon);
                return existing;
            }
            if (_exeIconCache.Count >= 256)
            {
                List<string>? zeroKeys = null;
                foreach (var kv in _exeIconCache)
                {
                    if (kv.Value == 0)
                        (zeroKeys ??= []).Add(kv.Key);
                }
                if (zeroKeys != null)
                {
                    foreach (string k in zeroKeys)
                        _exeIconCache.Remove(k);
                }
            }
            if (hIcon != 0 || _exeIconCache.Count < 256)
                _exeIconCache[exePath] = hIcon;
            return hIcon;
        }
    }

    private static nint TryExtractIconFromFile(string candidatePath, string originalExePath)
    {
        if (string.IsNullOrEmpty(candidatePath) ||
            string.Equals(candidatePath, originalExePath, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(candidatePath))
        {
            return 0;
        }

        nint large = 0;
        uint count = Win32.ExtractIconExW(candidatePath, 0, &large, null, 1);
        return (count > 0 && large != 0) ? large : 0;
    }

    private static nint TryExtractSiblingGuiAppIcon(string exePath)
    {
        string? companionExe = ProcessResolver.TryResolveCompanionGuiExePath(exePath);
        return companionExe != null ? TryExtractIconFromFile(companionExe, exePath) : 0;
    }

    public static bool HasBuiltInBrandVector(string? siteKey)
        => !string.IsNullOrEmpty(siteKey) && BuiltInBrandVectorSites.Contains(siteKey);

    private static string SanitizeSiteKey(string siteKey)
    {
        Span<char> buf = stackalloc char[Math.Min(siteKey.Length, 48)];
        int n = 0;
        foreach (char c in siteKey)
        {
            if (n >= buf.Length) break;
            if (char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-')
                buf[n++] = char.ToLowerInvariant(c);
        }
        return n > 0 ? new string(buf[..n]) : string.Empty;
    }

    public bool CacheSiteIconFromDataUrl(string? siteKey, string? iconDataUrl)
    {
        if (string.IsNullOrWhiteSpace(siteKey) ||
            string.IsNullOrWhiteSpace(iconDataUrl) ||
            HasBuiltInBrandVector(siteKey) ||
            !iconDataUrl.StartsWith(PngDataUrlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string safeKey = SanitizeSiteKey(siteKey);
        if (string.IsNullOrEmpty(safeKey))
            return false;

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(iconDataUrl[PngDataUrlPrefix.Length..]);
        }
        catch
        {
            return false;
        }

        if (bytes.Length < 16 || bytes.Length > MaxIconBytes)
            return false;

        // PNG 8-byte signature check: 89 50 4E 47 0D 0A 1A 0A
        if (bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47 ||
            bytes[4] != 0x0D || bytes[5] != 0x0A || bytes[6] != 0x1A || bytes[7] != 0x0A)
        {
            return false;
        }

        string digest = Convert.ToHexString(SHA256.HashData(bytes));
        string filePath = Path.Combine(SiteIconsDir, $"{safeKey}.png");

        lock (_cacheLock)
        {
            if (_cachedSiteIconHashes.TryGetValue(safeKey, out string? prevDigest) &&
                string.Equals(prevDigest, digest, StringComparison.Ordinal) &&
                File.Exists(filePath))
            {
                return false;
            }
        }

        try
        {
            Directory.CreateDirectory(SiteIconsDir);
            File.WriteAllBytes(filePath, bytes);
        }
        catch
        {
            return false;
        }

        lock (_cacheLock)
        {
            _cachedSiteIconHashes[safeKey] = digest;
            string prefix = $"{safeKey}:";
            List<string>? toRemove = null;
            foreach (string key in _siteIconCache.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    (toRemove ??= []).Add(key);
            }
            if (toRemove != null)
            {
                foreach (string k in toRemove)
                {
                    if (_siteIconCache.Remove(k, out nint oldIcon) && oldIcon != 0)
                        _retiredIcons.Enqueue(oldIcon);
                }
            }
        }

        return true;
    }

    private static nint TryLoadPngFileAsIcon(string filePath, int size)
    {
        if (!File.Exists(filePath))
            return 0;

        int stride = size * 4;
        byte* pixels = (byte*)NativeMemory.AllocZeroed((nuint)(stride * size));
        nint srcBmp = 0;
        nint dstBmp = 0;
        nint gfx = 0;

        try
        {
            if (GdiPlus.GdipCreateBitmapFromFile(filePath, out srcBmp) != 0 || srcBmp == 0)
                return 0;
            if (GdiPlus.GdipCreateBitmapFromScan0(size, size, stride, GdiPlus.PixelFormat32bppPARGB, (nint)pixels, out dstBmp) != 0 || dstBmp == 0)
                return 0;
            if (GdiPlus.GdipGetImageGraphicsContext(dstBmp, out gfx) != 0 || gfx == 0)
                return 0;

            GdiPlus.GdipSetSmoothingMode(gfx, GdiPlus.SmoothingModeAntiAlias);
            GdiPlus.GdipSetPixelOffsetMode(gfx, GdiPlus.PixelOffsetModeHighQuality);
            GdiPlus.GdipSetInterpolationMode(gfx, GdiPlus.InterpolationModeHighQualityBicubic);
            GdiPlus.GdipGraphicsClear(gfx, 0x00000000);
            GdiPlus.GdipDrawImageRectI(gfx, srcBmp, 0, 0, size, size);

            if (GdiPlus.GdipCreateHICONFromBitmap(dstBmp, out nint hIcon) == 0)
                return hIcon;
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

    public nint GetOrCreateSiteIcon(string siteKey, int size = 48)
    {
        if (string.IsNullOrWhiteSpace(siteKey))
            return 0;

        string normalizedKey = siteKey.ToLowerInvariant();
        string cacheKey = $"{normalizedKey}:{size}";
        lock (_cacheLock)
        {
            if (_siteIconCache.TryGetValue(cacheKey, out nint cached))
                return cached;
        }

        nint hIcon = 0;
        if (!HasBuiltInBrandVector(normalizedKey))
        {
            string safeKey = SanitizeSiteKey(normalizedKey);
            if (!string.IsNullOrEmpty(safeKey))
                hIcon = TryLoadPngFileAsIcon(Path.Combine(SiteIconsDir, $"{safeKey}.png"), size);
        }

        if (hIcon == 0)
            hIcon = RenderSiteBrandIcon(normalizedKey, size);
        if (hIcon == 0)
            return 0;

        lock (_cacheLock)
        {
            if (_siteIconCache.TryGetValue(cacheKey, out nint existing))
            {
                Win32.DestroyIcon(hIcon);
                return existing;
            }
            if (_siteIconCache.Count >= 128)
            {
                foreach (string k in _siteIconCache.Keys)
                {
                    if (_siteIconCache.Remove(k, out nint evicted) && evicted != 0)
                        _retiredIcons.Enqueue(evicted);
                    break;
                }
            }
            _siteIconCache[cacheKey] = hIcon;
            return hIcon;
        }
    }

    /// <summary>
    /// Creates an owned HICON containing the Cueway brand icon loaded from the embedded application icon resource
    /// (falling back to the vector emblem if unavailable).
    /// Caller owns the returned HICON and must release it with Win32.DestroyIcon.
    /// </summary>
    public nint CreateBrandIcon(int size = 48)
    {
        nint hInst = Win32.GetModuleHandleW(null);
        if (hInst != 0)
        {
            nint hLoaded = Win32.LoadImageW(hInst, Win32.IDI_APPLICATION, Win32.IMAGE_ICON, size, size, Win32.LR_DEFAULTCOLOR);
            if (hLoaded != 0)
                return hLoaded;
        }

        int stride = size * 4;
        byte* pixels = (byte*)NativeMemory.AllocZeroed((nuint)(stride * size));
        nint gdipBitmap = 0;
        nint graphics = 0;

        try
        {
            if (GdiPlus.GdipCreateBitmapFromScan0(size, size, stride, GdiPlus.PixelFormat32bppPARGB, (nint)pixels, out gdipBitmap) != 0 || gdipBitmap == 0)
                return 0;

            if (GdiPlus.GdipGetImageGraphicsContext(gdipBitmap, out graphics) != 0 || graphics == 0)
                return 0;

            GdiPlus.GdipSetSmoothingMode(graphics, GdiPlus.SmoothingModeAntiAlias);
            GdiPlus.GdipSetPixelOffsetMode(graphics, GdiPlus.PixelOffsetModeHighQuality);
            GdiPlus.GdipSetInterpolationMode(graphics, GdiPlus.InterpolationModeHighQualityBicubic);
            GdiPlus.GdipGraphicsClear(graphics, 0x00000000);

            DrawConcept4Emblem(graphics, size, 0xFF6B2C);

            if (GdiPlus.GdipCreateHICONFromBitmap(gdipBitmap, out nint hIconOut) == 0)
                return hIconOut;

            return 0;
        }
        finally
        {
            if (graphics != 0) GdiPlus.GdipDeleteGraphics(graphics);
            if (gdipBitmap != 0) GdiPlus.GdipDisposeImage(gdipBitmap);
            NativeMemory.Free(pixels);
        }
    }

    public nint GetBrandIcon(int size = 32)
    {
        lock (_cacheLock)
        {
            if (_brandIconCache.TryGetValue(size, out nint cached))
                return cached;
        }

        nint hIcon = CreateBrandIcon(size);
        if (hIcon == 0)
            return 0;

        lock (_cacheLock)
        {
            if (_brandIconCache.TryGetValue(size, out nint existing))
            {
                Win32.DestroyIcon(hIcon);
                return existing;
            }
            if (_brandIconCache.Count >= 16)
            {
                foreach (int k in _brandIconCache.Keys)
                {
                    if (_brandIconCache.Remove(k, out nint evicted) && evicted != 0)
                        _retiredIcons.Enqueue(evicted);
                    break;
                }
            }
            _brandIconCache[size] = hIcon;
            return hIcon;
        }
    }

    /// <summary>
    /// Creates a composite HICON containing the active target's icon (or framed initial badge if iconless) inside a
    /// status-colored tile with a status dot, or the Cueway brand icon when no active target exists.
    /// Caller owns the returned HICON and must release it with Win32.DestroyIcon.
    /// </summary>
    public nint CreateCompositeIcon(string? appKey, string? exePath, string? displayName, bool isPlaying, bool isMuted, bool isLocked, int size = 32)
    {
        int stride = size * 4;
        byte* pixels = (byte*)NativeMemory.AllocZeroed((nuint)(stride * size));
        nint gdipBitmap = 0;
        nint graphics = 0;
        nint appBitmap = 0;

        try
        {
            if (GdiPlus.GdipCreateBitmapFromScan0(size, size, stride, GdiPlus.PixelFormat32bppPARGB, (nint)pixels, out gdipBitmap) != 0 || gdipBitmap == 0)
                return 0;

            if (GdiPlus.GdipGetImageGraphicsContext(gdipBitmap, out graphics) != 0 || graphics == 0)
                return 0;

            GdiPlus.GdipSetSmoothingMode(graphics, GdiPlus.SmoothingModeAntiAlias);
            GdiPlus.GdipSetPixelOffsetMode(graphics, GdiPlus.PixelOffsetModeHighQuality);
            GdiPlus.GdipSetInterpolationMode(graphics, GdiPlus.InterpolationModeHighQualityBicubic);
            GdiPlus.GdipGraphicsClear(graphics, 0x00000000);

            float scale = size / 32f;
            if (!string.IsNullOrWhiteSpace(appKey))
            {
                uint badgeRgb;
                uint frameBorderRgb;
                if (isMuted)
                {
                    badgeRgb = Theme.Red;
                    frameBorderRgb = Theme.Red;
                }
                else if (isPlaying)
                {
                    badgeRgb = Theme.Green;
                    frameBorderRgb = Theme.Green;
                }
                else if (isLocked)
                {
                    badgeRgb = Theme.Amber;
                    frameBorderRgb = Theme.Amber;
                }
                else
                {
                    badgeRgb = 0xFF6B2C;
                    frameBorderRgb = 0xFF6B2C;
                }

                GdiPlus.FillAndStrokeRoundedRect(
                    graphics,
                    1.2f * scale,
                    1.2f * scale,
                    29.6f * scale,
                    29.6f * scale,
                    7.5f * scale,
                    GdiPlus.Argb(0x101116, 255),
                    GdiPlus.Argb(frameBorderRgb, 255),
                    2.8f * scale);

                nint appIcon = GetIconForApp(appKey, exePath);
                if (appIcon != 0 && GdiPlus.GdipCreateBitmapFromHICON(appIcon, out appBitmap) == 0 && appBitmap != 0)
                {
                    int iconSize = (int)MathF.Round(18f * scale);
                    int iconX = (size - iconSize) / 2;
                    int iconY = (size - iconSize) / 2;
                    GdiPlus.GdipDrawImageRectI(graphics, appBitmap, iconX, iconY, iconSize, iconSize);
                }
                else
                {
                    // Iconless app/daemon fallback: crisp inner accent badge so the framed target state stays visible
                    GdiPlus.FillAndStrokeRoundedRect(
                        graphics,
                        7.0f * scale,
                        7.0f * scale,
                        18.0f * scale,
                        18.0f * scale,
                        4.8f * scale,
                        GdiPlus.Argb(0x252B3B, 255),
                        GdiPlus.Argb(frameBorderRgb, 110),
                        1.1f * scale);
                    if (isPlaying)
                    {
                        FillTriangle(
                            graphics,
                            GdiPlus.Argb(0xF8FAFC, 255),
                            13.0f * scale, 11.2f * scale,
                            13.0f * scale, 20.8f * scale,
                            21.2f * scale, 16.0f * scale);
                    }
                    else
                    {
                        FillCircle(graphics, GdiPlus.Argb(0xF8FAFC, 240), 12.2f * scale, 12.2f * scale, 7.6f * scale);
                    }
                }

                float badgeD = 9.0f * scale;
                float ringPad = 1.6f * scale;
                float bx = size - badgeD - 0.8f * scale;
                float by = size - badgeD - 0.8f * scale;
                FillCircle(graphics, GdiPlus.Argb(0x101116, 255), bx - ringPad, by - ringPad, badgeD + ringPad * 2f);
                FillCircle(graphics, GdiPlus.Argb(badgeRgb, 255), bx, by, badgeD);
            }
            else
            {
                nint brandIcon = GetBrandIcon(size);
                if (brandIcon != 0 && GdiPlus.GdipCreateBitmapFromHICON(brandIcon, out appBitmap) == 0 && appBitmap != 0)
                {
                    GdiPlus.GdipDrawImageRectI(graphics, appBitmap, 0, 0, size, size);
                }
                else
                {
                    DrawConcept4Emblem(graphics, size, 0xFF6B2C);
                }
            }

            if (GdiPlus.GdipCreateHICONFromBitmap(gdipBitmap, out nint hIconOut) == 0)
                return hIconOut;

            return 0;
        }
        finally
        {
            if (appBitmap != 0) GdiPlus.GdipDisposeImage(appBitmap);
            if (graphics != 0) GdiPlus.GdipDeleteGraphics(graphics);
            if (gdipBitmap != 0) GdiPlus.GdipDisposeImage(gdipBitmap);
            NativeMemory.Free(pixels);
        }
    }

    private static void DrawConcept4Emblem(nint gfx, int size, uint stripRgb)
    {
        float u = size / 100f;

        // Outer matte carbon squircle (#16181D)
        GdiPlus.FillRoundedRect(gfx, 4f * u, 4f * u, 92f * u, 92f * u, 20f * u, GdiPlus.Argb(0x16181D));

        // Back inactive tab (#2E333D)
        ReadOnlySpan<float> backPts =
        [
            28f * u, 31f * u,
            45f * u, 31f * u,
            50f * u, 37f * u,
            78f * u, 37f * u,
            78f * u, 68f * u,
            28f * u, 68f * u
        ];
        FillAndStrokePolygon(gfx, GdiPlus.Argb(0x2E333D), GdiPlus.Argb(0x3F4554), MathF.Max(1f, 2f * u), backPts);

        // Foreground prioritized off-white tab (#F8FAFC)
        ReadOnlySpan<float> frontPts =
        [
            19f * u, 25f * u,
            41f * u, 25f * u,
            47f * u, 32f * u,
            75f * u, 32f * u,
            75f * u, 77f * u,
            19f * u, 77f * u
        ];
        FillAndStrokePolygon(gfx, GdiPlus.Argb(0xF8FAFC), 0, 0f, frontPts);

        // Sharp active tab accent strip
        FillRect(gfx, GdiPlus.Argb(stripRgb), 19f * u, 25f * u, 22f * u, 5.2f * u);

        // Centered flat cue-skip symbol (triangle + bar) in deep carbon (#16181D)
        FillTriangle(gfx, GdiPlus.Argb(0x16181D), 36f * u, 45f * u, 52f * u, 55.5f * u, 36f * u, 66f * u);
        GdiPlus.FillRoundedRect(gfx, 55f * u, 45f * u, 6f * u, 21f * u, 1.5f * u, GdiPlus.Argb(0x16181D));
    }

    private static void FillAndStrokePolygon(nint gfx, uint fillArgb, uint strokeArgb, float strokeWidth, ReadOnlySpan<float> xyPairs)
    {
        if (xyPairs.Length < 6) return;
        if (GdiPlus.GdipCreatePath(GdiPlus.FillModeAlternate, out nint path) != 0 || path == 0) return;
        try
        {
            for (int i = 0; i + 3 < xyPairs.Length; i += 2)
            {
                GdiPlus.GdipAddPathLine(path, xyPairs[i], xyPairs[i + 1], xyPairs[i + 2], xyPairs[i + 3]);
            }
            GdiPlus.GdipClosePathFigure(path);

            if ((fillArgb >> 24) > 0 && GdiPlus.GdipCreateSolidFill(fillArgb, out nint brush) == 0 && brush != 0)
            {
                try { GdiPlus.GdipFillPath(gfx, brush, path); }
                finally { GdiPlus.GdipDeleteBrush(brush); }
            }
            if ((strokeArgb >> 24) > 0 && strokeWidth > 0f && GdiPlus.GdipCreatePen1(strokeArgb, strokeWidth, GdiPlus.UnitPixel, out nint pen) == 0 && pen != 0)
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

    private static nint RenderSiteBrandIcon(string siteKey, int size)
    {
        int stride = size * 4;
        byte* pixels = (byte*)NativeMemory.AllocZeroed((nuint)(stride * size));
        nint bmp = 0;
        nint gfx = 0;

        try
        {
            if (GdiPlus.GdipCreateBitmapFromScan0(size, size, stride, GdiPlus.PixelFormat32bppPARGB, (nint)pixels, out bmp) != 0 || bmp == 0)
                return 0;
            if (GdiPlus.GdipGetImageGraphicsContext(bmp, out gfx) != 0 || gfx == 0)
                return 0;

            GdiPlus.GdipSetSmoothingMode(gfx, GdiPlus.SmoothingModeAntiAlias);
            GdiPlus.GdipSetPixelOffsetMode(gfx, GdiPlus.PixelOffsetModeHighQuality);
            GdiPlus.GdipGraphicsClear(gfx, 0x00000000);

            float s = size / 48f;

            switch (siteKey)
            {
                case "youtube":
                    GdiPlus.FillRoundedRect(gfx, 2f * s, 7f * s, 44f * s, 34f * s, 10f * s, GdiPlus.Argb(0xFF0033));
                    FillTriangle(gfx, GdiPlus.Argb(0xFFFFFF), 19.5f * s, 16f * s, 19.5f * s, 32f * s, 33f * s, 24f * s);
                    break;

                case "youtubemusic":
                    FillCircle(gfx, GdiPlus.Argb(0xFF0033), 4f * s, 4f * s, 40f * s);
                    StrokeCircle(gfx, GdiPlus.Argb(0xFFFFFF), 2.6f * s, 11f * s, 11f * s, 26f * s);
                    FillTriangle(gfx, GdiPlus.Argb(0xFFFFFF), 20f * s, 17f * s, 20f * s, 31f * s, 31.5f * s, 24f * s);
                    break;

                case "x":
                    GdiPlus.FillAndStrokeRoundedRect(gfx, 3f * s, 3f * s, 42f * s, 42f * s, 11f * s,
                        GdiPlus.Argb(0x0D1117), GdiPlus.Argb(0x3B4252), 1.6f * s);
                    DrawStrokeLine(gfx, GdiPlus.Argb(0xFFFFFF), 4.4f * s, 14f * s, 13.5f * s, 34f * s, 34.5f * s, round: false);
                    DrawStrokeLine(gfx, GdiPlus.Argb(0xFFFFFF), 3.2f * s, 33.5f * s, 13.5f * s, 14.5f * s, 34.5f * s, round: false);
                    break;

                case "twitch":
                    GdiPlus.FillRoundedRect(gfx, 4f * s, 4f * s, 40f * s, 40f * s, 10f * s, GdiPlus.Argb(0x9146FF));
                    GdiPlus.FillRoundedRect(gfx, 12f * s, 11f * s, 24f * s, 21f * s, 3.5f * s, GdiPlus.Argb(0xFFFFFF));
                    FillRect(gfx, GdiPlus.Argb(0x9146FF), 19.5f * s, 16.5f * s, 3.2f * s, 8.5f * s);
                    FillRect(gfx, GdiPlus.Argb(0x9146FF), 26.8f * s, 16.5f * s, 3.2f * s, 8.5f * s);
                    FillTriangle(gfx, GdiPlus.Argb(0xFFFFFF), 17f * s, 31f * s, 24f * s, 31f * s, 17f * s, 37.5f * s);
                    break;

                case "kick":
                    GdiPlus.FillAndStrokeRoundedRect(gfx, 4f * s, 4f * s, 40f * s, 40f * s, 10f * s,
                        GdiPlus.Argb(0x0B0E11), GdiPlus.Argb(0x53FC18), 2f * s);
                    DrawStrokeLine(gfx, GdiPlus.Argb(0x53FC18), 4.6f * s, 16.5f * s, 13f * s, 16.5f * s, 35f * s, round: false);
                    DrawStrokeLine(gfx, GdiPlus.Argb(0x53FC18), 4.6f * s, 31.5f * s, 13f * s, 18.5f * s, 24f * s, round: false);
                    DrawStrokeLine(gfx, GdiPlus.Argb(0x53FC18), 4.6f * s, 19.5f * s, 24f * s, 31.5f * s, 35f * s, round: false);
                    break;

                case "spotifyweb":
                    FillCircle(gfx, GdiPlus.Argb(0x1DB954), 4f * s, 4f * s, 40f * s);
                    DrawArcStroke(gfx, GdiPlus.Argb(0xFFFFFF), 3.4f * s, 10f * s, 18f * s, 28f * s, 22f * s, -125f, 70f);
                    DrawArcStroke(gfx, GdiPlus.Argb(0xFFFFFF), 3.0f * s, 13f * s, 24f * s, 22f * s, 18f * s, -125f, 70f);
                    DrawArcStroke(gfx, GdiPlus.Argb(0xFFFFFF), 2.6f * s, 16f * s, 30f * s, 16f * s, 14f * s, -125f, 70f);
                    break;

                case "soundcloud":
                    GdiPlus.FillRoundedRect(gfx, 4f * s, 4f * s, 40f * s, 40f * s, 10f * s, GdiPlus.Argb(0xFF5500));
                    DrawStrokeLine(gfx, GdiPlus.Argb(0xFFFFFF), 3.4f * s, 12f * s, 23f * s, 12f * s, 32f * s);
                    DrawStrokeLine(gfx, GdiPlus.Argb(0xFFFFFF), 3.4f * s, 18f * s, 18f * s, 18f * s, 32f * s);
                    DrawStrokeLine(gfx, GdiPlus.Argb(0xFFFFFF), 3.4f * s, 24f * s, 14f * s, 24f * s, 32f * s);
                    DrawStrokeLine(gfx, GdiPlus.Argb(0xFFFFFF), 3.4f * s, 30f * s, 17f * s, 30f * s, 32f * s);
                    DrawStrokeLine(gfx, GdiPlus.Argb(0xFFFFFF), 3.4f * s, 36f * s, 22f * s, 36f * s, 32f * s);
                    break;

                case "netflix":
                    GdiPlus.FillAndStrokeRoundedRect(gfx, 4f * s, 4f * s, 40f * s, 40f * s, 10f * s,
                        GdiPlus.Argb(0x141414), GdiPlus.Argb(0xE50914, 160), 1.5f * s);
                    DrawStrokeLine(gfx, GdiPlus.Argb(0xB20710), 4.6f * s, 16f * s, 12f * s, 16f * s, 36f * s, round: false);
                    DrawStrokeLine(gfx, GdiPlus.Argb(0xB20710), 4.6f * s, 32f * s, 12f * s, 32f * s, 36f * s, round: false);
                    DrawStrokeLine(gfx, GdiPlus.Argb(0xE50914), 5.0f * s, 16f * s, 12f * s, 32f * s, 36f * s, round: false);
                    break;

                case "instagram":
                    GdiPlus.FillRoundedRect(gfx, 4f * s, 4f * s, 40f * s, 40f * s, 11f * s, GdiPlus.Argb(0xE1306C));
                    GdiPlus.DrawRoundedRect(gfx, 12f * s, 12f * s, 24f * s, 24f * s, 6.5f * s, GdiPlus.Argb(0xFFFFFF), 2.8f * s);
                    StrokeCircle(gfx, GdiPlus.Argb(0xFFFFFF), 2.8f * s, 18.5f * s, 18.5f * s, 11f * s);
                    FillCircle(gfx, GdiPlus.Argb(0xFFFFFF), 30.5f * s, 15.5f * s, 3f * s);
                    break;

                case "tiktok":
                    GdiPlus.FillRoundedRect(gfx, 4f * s, 4f * s, 40f * s, 40f * s, 10f * s, GdiPlus.Argb(0x121212));
                    DrawStrokeLine(gfx, GdiPlus.Argb(0x25F4EE), 4f * s, 25f * s, 13f * s, 25f * s, 30f * s);
                    FillCircle(gfx, GdiPlus.Argb(0x25F4EE), 15f * s, 25f * s, 11f * s);
                    DrawStrokeLine(gfx, GdiPlus.Argb(0xFE2C55), 4f * s, 27f * s, 14f * s, 27f * s, 31f * s);
                    FillCircle(gfx, GdiPlus.Argb(0xFE2C55), 17f * s, 26f * s, 11f * s);
                    DrawStrokeLine(gfx, GdiPlus.Argb(0xFFFFFF), 3.6f * s, 26f * s, 13.5f * s, 26f * s, 30.5f * s);
                    DrawArcStroke(gfx, GdiPlus.Argb(0xFFFFFF), 3.6f * s, 26f * s, 8f * s, 14f * s, 14f * s, 90f, 90f);
                    FillCircle(gfx, GdiPlus.Argb(0xFFFFFF), 16f * s, 25.5f * s, 10.5f * s);
                    break;

                case "reddit":
                    FillCircle(gfx, GdiPlus.Argb(0xFF4500), 4f * s, 4f * s, 40f * s);
                    FillEllipse(gfx, GdiPlus.Argb(0xFFFFFF), 12f * s, 19f * s, 24f * s, 16f * s);
                    DrawStrokeLine(gfx, GdiPlus.Argb(0xFFFFFF), 2.5f * s, 24f * s, 19f * s, 27f * s, 13f * s);
                    FillCircle(gfx, GdiPlus.Argb(0xFFFFFF), 28f * s, 11f * s, 4.5f * s);
                    FillCircle(gfx, GdiPlus.Argb(0xFF4500), 17.5f * s, 24.5f * s, 3.8f * s);
                    FillCircle(gfx, GdiPlus.Argb(0xFF4500), 26.5f * s, 24.5f * s, 3.8f * s);
                    break;

                case "facebook":
                    FillCircle(gfx, GdiPlus.Argb(0x1877F2), 4f * s, 4f * s, 40f * s);
                    DrawStrokeLine(gfx, GdiPlus.Argb(0xFFFFFF), 4.6f * s, 25f * s, 16f * s, 25f * s, 38f * s);
                    DrawArcStroke(gfx, GdiPlus.Argb(0xFFFFFF), 4.6f * s, 25f * s, 12.5f * s, 10f * s, 10f * s, 180f, 90f);
                    DrawStrokeLine(gfx, GdiPlus.Argb(0xFFFFFF), 4.2f * s, 19f * s, 24f * s, 31f * s, 24f * s);
                    break;

                default:
                    uint fallbackRgb = siteKey switch
                    {
                        "disneyplus" => 0x113CCF,
                        "primevideo" => 0x00A8E1,
                        "vimeo" => 0x1AB7EA,
                        "dailymotion" => 0x0066DC,
                        "bilibili" => 0xFB7299,
                        _ => Theme.Accent
                    };
                    GdiPlus.FillRoundedRect(gfx, 4f * s, 4f * s, 40f * s, 40f * s, 10f * s, GdiPlus.Argb(fallbackRgb));
                    FillTriangle(gfx, GdiPlus.Argb(0xFFFFFF), 19f * s, 15.5f * s, 19f * s, 32.5f * s, 33f * s, 24f * s);
                    break;
            }

            if (GdiPlus.GdipCreateHICONFromBitmap(bmp, out nint hIcon) == 0)
                return hIcon;
            return 0;
        }
        finally
        {
            if (gfx != 0) GdiPlus.GdipDeleteGraphics(gfx);
            if (bmp != 0) GdiPlus.GdipDisposeImage(bmp);
            NativeMemory.Free(pixels);
        }
    }

    private static void FillTriangle(nint gfx, uint argb, float x1, float y1, float x2, float y2, float x3, float y3)
    {
        if (GdiPlus.GdipCreateSolidFill(argb, out nint brush) != 0 || brush == 0) return;
        try
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
        finally
        {
            GdiPlus.GdipDeleteBrush(brush);
        }
    }

    private static void FillCircle(nint gfx, uint argb, float x, float y, float d)
        => FillEllipse(gfx, argb, x, y, d, d);

    private static void FillEllipse(nint gfx, uint argb, float x, float y, float w, float h)
    {
        if (GdiPlus.GdipCreateSolidFill(argb, out nint brush) != 0 || brush == 0) return;
        try { GdiPlus.GdipFillEllipse(gfx, brush, x, y, w, h); }
        finally { GdiPlus.GdipDeleteBrush(brush); }
    }

    private static void FillRect(nint gfx, uint argb, float x, float y, float w, float h)
    {
        if (GdiPlus.GdipCreateSolidFill(argb, out nint brush) != 0 || brush == 0) return;
        try { GdiPlus.GdipFillRectangle(gfx, brush, x, y, w, h); }
        finally { GdiPlus.GdipDeleteBrush(brush); }
    }

    private static void StrokeCircle(nint gfx, uint argb, float width, float x, float y, float d)
    {
        if (GdiPlus.GdipCreatePen1(argb, width, GdiPlus.UnitPixel, out nint pen) != 0 || pen == 0) return;
        try { GdiPlus.GdipDrawEllipse(gfx, pen, x, y, d, d); }
        finally { GdiPlus.GdipDeletePen(pen); }
    }

    private static void DrawStrokeLine(nint gfx, uint argb, float width, float x1, float y1, float x2, float y2, bool round = true)
    {
        if (GdiPlus.GdipCreatePen1(argb, width, GdiPlus.UnitPixel, out nint pen) != 0 || pen == 0) return;
        try
        {
            if (round)
            {
                GdiPlus.GdipSetPenStartCap(pen, GdiPlus.LineCapRound);
                GdiPlus.GdipSetPenEndCap(pen, GdiPlus.LineCapRound);
            }
            GdiPlus.GdipDrawLine(gfx, pen, x1, y1, x2, y2);
        }
        finally
        {
            GdiPlus.GdipDeletePen(pen);
        }
    }

    private static void DrawArcStroke(nint gfx, uint argb, float width, float x, float y, float w, float h, float startAngle, float sweepAngle)
    {
        if (GdiPlus.GdipCreatePen1(argb, width, GdiPlus.UnitPixel, out nint pen) != 0 || pen == 0) return;
        try
        {
            GdiPlus.GdipSetPenStartCap(pen, GdiPlus.LineCapRound);
            GdiPlus.GdipSetPenEndCap(pen, GdiPlus.LineCapRound);
            GdiPlus.GdipDrawArc(gfx, pen, x, y, w, h, startAngle, sweepAngle);
        }
        finally
        {
            GdiPlus.GdipDeletePen(pen);
        }
    }

    public void Dispose()
    {
        lock (_cacheLock)
        {
            if (_disposed) return;
            _disposed = true;

            while (_retiredIcons.Count > 0)
            {
                nint retired = _retiredIcons.Dequeue();
                if (retired != 0) Win32.DestroyIcon(retired);
            }

            foreach (nint icon in _exeIconCache.Values)
            {
                if (icon != 0) Win32.DestroyIcon(icon);
            }
            _exeIconCache.Clear();

            foreach (nint icon in _siteIconCache.Values)
            {
                if (icon != 0) Win32.DestroyIcon(icon);
            }
            _siteIconCache.Clear();

            foreach (nint icon in _brandIconCache.Values)
            {
                if (icon != 0) Win32.DestroyIcon(icon);
            }
            _brandIconCache.Clear();
        }
    }
}
