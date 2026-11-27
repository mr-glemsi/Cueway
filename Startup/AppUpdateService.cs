using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cueway.Config;
using Cueway.Localization;

namespace Cueway.Startup;

internal sealed class GitHubReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAssetDto> Assets { get; set; } = [];
}

internal sealed class GitHubReleaseAssetDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

internal enum UpdatePhase
{
    Idle = 0,
    Checking = 1,
    UpToDate = 2,
    Available = 3,
    Downloading = 4,
    ReadyToInstall = 5,
    Failed = 6
}

internal sealed record UpdateSnapshot(
    UpdatePhase Phase,
    string CurrentVersion,
    string LatestVersion,
    long BytesDownloaded,
    long TotalBytes,
    string? DownloadedFilePath,
    string? ErrorMessage,
    bool Dismissed);

internal sealed class AppUpdateService : IDisposable
{
    private const string DefaultApiUrl = "https://api.github.com/repos/mr-glemsi/Cueway/releases/latest";
    private const string DefaultAllowedDownloadPrefix = "https://github.com/mr-glemsi/Cueway/releases/download/";
    private const int DefaultMinDownloadBytes = 100 * 1024;
    private static readonly TimeSpan StaleCheckInterval = TimeSpan.FromHours(6);

    private readonly string _currentVersion;
    private readonly string _apiUrl;
    private readonly string _allowedDownloadPrefix;
    private readonly int _minDownloadBytes;
    private readonly string _updatesDir;
    private readonly object _syncLock = new();
    private HttpClient? _http;

    private UpdateSnapshot _snapshot;
    private string? _downloadUrl;
    private long _expectedAssetSize;
    private DateTimeOffset _lastSuccessfulCheckUtc = DateTimeOffset.MinValue;
    private string? _notifiedVersion;
    private CancellationTokenSource? _downloadCts;
    private bool _checkInFlight;
    private bool _disposed;

    public event Action<UpdateSnapshot>? StateChanged;
    public event Action<string>? UpdateAvailableNotified;

    public string CurrentVersion => _currentVersion;

    public AppUpdateService()
        : this(
            ReadCurrentAssemblyVersion(),
            DefaultApiUrl,
            DefaultAllowedDownloadPrefix,
            DefaultMinDownloadBytes)
    {
    }

    internal AppUpdateService(
        string currentVersion,
        string apiUrl,
        string allowedDownloadPrefix,
        int minDownloadBytes,
        string? updatesDir = null)
    {
        _currentVersion = NormalizeVersionString(currentVersion) is { Length: > 0 } norm ? norm : "1.0.5";
        _apiUrl = apiUrl;
        _allowedDownloadPrefix = allowedDownloadPrefix;
        _minDownloadBytes = Math.Max(2, minDownloadBytes);
        _updatesDir = string.IsNullOrWhiteSpace(updatesDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cueway", "Updates")
            : updatesDir;

        _snapshot = new UpdateSnapshot(
            UpdatePhase.Idle,
            _currentVersion,
            string.Empty,
            0,
            0,
            null,
            null,
            false);
    }

    private HttpClient GetOrCreateHttpClient()
    {
        lock (_syncLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_http != null)
                return _http;

            var handler = new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(15),
                MaxConnectionsPerServer = 2
            };
            var client = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Cueway", _currentVersion));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            _http = client;
            return client;
        }
    }

    public UpdateSnapshot GetSnapshot()
    {
        lock (_syncLock)
            return _snapshot;
    }

    public Task CheckIfStaleAsync()
    {
        lock (_syncLock)
        {
            if (_disposed || _checkInFlight)
                return Task.CompletedTask;
            if (_snapshot.Phase is UpdatePhase.Downloading or UpdatePhase.ReadyToInstall or UpdatePhase.Available)
                return Task.CompletedTask;
            if (_lastSuccessfulCheckUtc != DateTimeOffset.MinValue &&
                DateTimeOffset.UtcNow - _lastSuccessfulCheckUtc < StaleCheckInterval)
            {
                return Task.CompletedTask;
            }
        }
        return CheckForUpdatesAsync(silent: true, initialDelayMs: 0);
    }

    public async Task CheckForUpdatesAsync(bool silent = false, int initialDelayMs = 0)
    {
        if (initialDelayMs > 0)
        {
            try
            {
                await Task.Delay(initialDelayMs).ConfigureAwait(false);
            }
            catch
            {
                return;
            }
        }

        if (_disposed)
            return;

        bool retryOnTransient = silent && initialDelayMs > 0;
        bool ok = await RunCheckCoreAsync(silent).ConfigureAwait(false);
        if (!ok && retryOnTransient && !_disposed)
        {
            try
            {
                await Task.Delay(25_000).ConfigureAwait(false);
            }
            catch
            {
                return;
            }
            if (!_disposed)
                await RunCheckCoreAsync(silent: true).ConfigureAwait(false);
        }
    }

    private async Task<bool> RunCheckCoreAsync(bool silent)
    {
        lock (_syncLock)
        {
            if (_disposed || _checkInFlight)
                return true;
            if (_snapshot.Phase is UpdatePhase.Downloading or UpdatePhase.ReadyToInstall)
                return true;

            _checkInFlight = true;
            if (!silent)
            {
                _snapshot = _snapshot with
                {
                    Phase = UpdatePhase.Checking,
                    ErrorMessage = null
                };
            }
        }

        if (!silent)
            NotifyStateChanged();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            using var req = new HttpRequestMessage(HttpMethod.Get, _apiUrl);
            using var resp = await GetOrCreateHttpClient().SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            string json = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var release = JsonSerializer.Deserialize(json, AppJsonContext.Default.GitHubReleaseDto);
            if (release == null || release.Draft || release.Prerelease)
            {
                MarkUpToDate();
                return true;
            }

            if (!IsNewerVersion(release.TagName, _currentVersion, out string latestNorm))
            {
                MarkUpToDate();
                return true;
            }

            GitHubReleaseAssetDto? asset = FindSetupAsset(release.Assets);
            if (asset == null ||
                string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl) ||
                !IsAllowedDownloadUrl(asset.BrowserDownloadUrl))
            {
                MarkUpToDate();
                return true;
            }

            string? existingInstaller = TryFindValidCachedInstaller(latestNorm, asset.Size);
            bool shouldNotifyOsd = false;
            lock (_syncLock)
            {
                if (_disposed || _snapshot.Phase == UpdatePhase.Downloading)
                    return true;

                _lastSuccessfulCheckUtc = DateTimeOffset.UtcNow;
                _downloadUrl = asset.BrowserDownloadUrl;
                _expectedAssetSize = asset.Size > 0 ? asset.Size : 0;

                if (existingInstaller != null)
                {
                    long len = new FileInfo(existingInstaller).Length;
                    _snapshot = new UpdateSnapshot(
                        UpdatePhase.ReadyToInstall,
                        _currentVersion,
                        latestNorm,
                        len,
                        len,
                        existingInstaller,
                        null,
                        false);
                }
                else
                {
                    _snapshot = new UpdateSnapshot(
                        UpdatePhase.Available,
                        _currentVersion,
                        latestNorm,
                        0,
                        _expectedAssetSize,
                        null,
                        null,
                        false);
                }

                if (!string.Equals(_notifiedVersion, latestNorm, StringComparison.OrdinalIgnoreCase))
                {
                    _notifiedVersion = latestNorm;
                    shouldNotifyOsd = true;
                }
            }

            NotifyStateChanged();
            if (shouldNotifyOsd)
                UpdateAvailableNotified?.Invoke(latestNorm);
            return true;
        }
        catch
        {
            lock (_syncLock)
            {
                if (_disposed)
                    return false;

                if (!silent)
                {
                    _snapshot = _snapshot with
                    {
                        Phase = UpdatePhase.Failed,
                        ErrorMessage = Strings.SetUpdateSubFailed,
                        Dismissed = false
                    };
                }
            }

            if (!silent)
                NotifyStateChanged();
            return false;
        }
        finally
        {
            lock (_syncLock)
                _checkInFlight = false;
        }
    }

    private void MarkUpToDate()
    {
        bool changed;
        lock (_syncLock)
        {
            _lastSuccessfulCheckUtc = DateTimeOffset.UtcNow;
            if (_snapshot.Phase is UpdatePhase.Downloading or UpdatePhase.ReadyToInstall)
                return;

            changed = _snapshot.Phase != UpdatePhase.UpToDate || _snapshot.Dismissed;
            _snapshot = new UpdateSnapshot(
                UpdatePhase.UpToDate,
                _currentVersion,
                _currentVersion,
                0,
                0,
                null,
                null,
                false);
        }

        if (changed)
            NotifyStateChanged();
    }

    public void DismissBanner()
    {
        lock (_syncLock)
        {
            if (_snapshot.Dismissed || _snapshot.Phase == UpdatePhase.Downloading)
                return;
            _snapshot = _snapshot with { Dismissed = true };
        }
        NotifyStateChanged();
    }

    public void StartDownload()
    {
        string url;
        string targetVer;
        long expectedSize;
        CancellationTokenSource cts;

        lock (_syncLock)
        {
            if (_disposed || _snapshot.Phase == UpdatePhase.Downloading)
                return;

            if (string.IsNullOrWhiteSpace(_downloadUrl) || !IsAllowedDownloadUrl(_downloadUrl) || string.IsNullOrWhiteSpace(_snapshot.LatestVersion))
                return;

            url = _downloadUrl;
            targetVer = _snapshot.LatestVersion;
            expectedSize = _expectedAssetSize;

            string? cached = TryFindValidCachedInstaller(targetVer, expectedSize);
            if (cached != null)
            {
                long cachedLen = new FileInfo(cached).Length;
                _snapshot = new UpdateSnapshot(
                    UpdatePhase.ReadyToInstall,
                    _currentVersion,
                    targetVer,
                    cachedLen,
                    cachedLen,
                    cached,
                    null,
                    false);
                NotifyStateChanged();
                return;
            }

            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            cts = new CancellationTokenSource();
            _downloadCts = cts;

            _snapshot = new UpdateSnapshot(
                UpdatePhase.Downloading,
                _currentVersion,
                targetVer,
                0,
                expectedSize,
                null,
                null,
                false);
        }

        NotifyStateChanged();
        _ = Task.Run(() => DownloadWorkerAsync(url, targetVer, expectedSize, cts));
    }

    public void CancelDownload()
    {
        CancellationTokenSource? cts;
        lock (_syncLock)
        {
            cts = _downloadCts;
            _downloadCts = null;
            if (_snapshot.Phase == UpdatePhase.Downloading)
            {
                _snapshot = _snapshot with
                {
                    Phase = UpdatePhase.Available,
                    BytesDownloaded = 0,
                    ErrorMessage = null
                };
            }
        }

        try { cts?.Cancel(); } catch { }
        try { cts?.Dispose(); } catch { }
        NotifyStateChanged();
    }

    private async Task DownloadWorkerAsync(string url, string targetVer, long expectedAssetSize, CancellationTokenSource cts)
    {
        Directory.CreateDirectory(_updatesDir);
        string safeVer = SanitizeFileToken(targetVer);
        string finalName = $"Cueway-Setup-x64-{safeVer}.exe";
        string finalPath = Path.Combine(_updatesDir, finalName);
        string partPath = finalPath + ".part";

        CleanupUpdatesDirectory(keepFileName: finalName);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await GetOrCreateHttpClient().SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            long totalBytes = resp.Content.Headers.ContentLength ?? expectedAssetSize;
            if (totalBytes <= 0)
                totalBytes = expectedAssetSize;

            long downloaded = 0;
            long lastNotifyTick = Environment.TickCount64;

            await using (var remote = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false))
            await using (var fs = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                byte[] buffer = new byte[81920];
                while (true)
                {
                    int read = await remote.ReadAsync(buffer, cts.Token).ConfigureAwait(false);
                    if (read <= 0)
                        break;

                    await fs.WriteAsync(buffer.AsMemory(0, read), cts.Token).ConfigureAwait(false);
                    downloaded += read;

                    long now = Environment.TickCount64;
                    if (now - lastNotifyTick >= 65)
                    {
                        lastNotifyTick = now;
                        lock (_syncLock)
                        {
                            if (!ReferenceEquals(_downloadCts, cts))
                                return;
                            _snapshot = _snapshot with
                            {
                                Phase = UpdatePhase.Downloading,
                                BytesDownloaded = downloaded,
                                TotalBytes = totalBytes
                            };
                        }
                        NotifyStateChanged();
                    }
                }
                await fs.FlushAsync(cts.Token).ConfigureAwait(false);
            }

            long expectedExact = resp.Content.Headers.ContentLength ?? (expectedAssetSize > 0 ? expectedAssetSize : 0);
            if (!IsValidPeExecutable(partPath, _minDownloadBytes, expectedExact))
                throw new InvalidDataException("Downloaded update package failed integrity verification.");

            File.Move(partPath, finalPath, overwrite: true);

            lock (_syncLock)
            {
                if (!ReferenceEquals(_downloadCts, cts))
                    return;
                _downloadCts = null;
                long finalSize = new FileInfo(finalPath).Length;
                _snapshot = new UpdateSnapshot(
                    UpdatePhase.ReadyToInstall,
                    _currentVersion,
                    targetVer,
                    finalSize,
                    finalSize,
                    finalPath,
                    null,
                    false);
            }
            NotifyStateChanged();
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(partPath);
        }
        catch
        {
            TryDeleteFile(partPath);
            bool notifyFail = false;
            lock (_syncLock)
            {
                if (ReferenceEquals(_downloadCts, cts))
                {
                    _downloadCts = null;
                    _snapshot = _snapshot with
                    {
                        Phase = UpdatePhase.Failed,
                        BytesDownloaded = 0,
                        ErrorMessage = Strings.UpdateFailedBanner,
                        Dismissed = false
                    };
                    notifyFail = true;
                }
            }
            if (notifyFail)
                NotifyStateChanged();
        }
        finally
        {
            try { cts.Dispose(); } catch { }
        }
    }

    public bool TryLaunchInstaller(out string? errorMessage)
    {
        string? path;
        lock (_syncLock)
        {
            if (_snapshot.Phase != UpdatePhase.ReadyToInstall)
            {
                errorMessage = Strings.UpdateFailedBanner;
                return false;
            }
            path = _snapshot.DownloadedFilePath;
        }

        if (string.IsNullOrWhiteSpace(path) || !IsValidPeExecutable(path, _minDownloadBytes))
        {
            errorMessage = Strings.UpdateFailedBanner;
            lock (_syncLock)
            {
                _snapshot = _snapshot with
                {
                    Phase = UpdatePhase.Failed,
                    DownloadedFilePath = null,
                    ErrorMessage = Strings.UpdateFailedBanner,
                    Dismissed = false
                };
            }
            NotifyStateChanged();
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                Arguments = "/SP- /SILENT /SUPPRESSMSGBOXES /NORESTART",
                UseShellExecute = true
            };
            Process.Start(psi);
            errorMessage = null;
            return true;
        }
        catch (Win32Exception)
        {
            errorMessage = Strings.MsgUpdateLaunchCancelled;
            return false;
        }
        catch
        {
            errorMessage = Strings.UpdateFailedBanner;
            return false;
        }
    }

#if DEBUG
    public void ApplyDebugPreview(string mode)
    {
        lock (_syncLock)
        {
            _snapshot = mode.ToLowerInvariant() switch
            {
                "available" => new UpdateSnapshot(UpdatePhase.Available, _currentVersion, "1.0.5", 0, 4_299_161, null, null, false),
                "downloading" => new UpdateSnapshot(UpdatePhase.Downloading, _currentVersion, "1.0.5", 2_665_479, 4_299_161, null, null, false),
                "ready" => new UpdateSnapshot(UpdatePhase.ReadyToInstall, _currentVersion, "1.0.5", 4_299_161, 4_299_161, null, null, false),
                "failed" => new UpdateSnapshot(UpdatePhase.Failed, _currentVersion, "1.0.5", 0, 4_299_161, null, Strings.UpdateFailedBanner, false),
                _ => _snapshot
            };
        }
        NotifyStateChanged();
    }
#endif

    public static string FormatMegabytes(long bytes)
    {
        double mb = Math.Max(0, bytes) / (1024.0 * 1024.0);
        return mb.ToString("0.0", CultureInfo.InvariantCulture);
    }

    internal static bool IsNewerVersion(string? remoteTag, string currentVersion, out string normalizedRemote)
    {
        normalizedRemote = NormalizeVersionString(remoteTag);
        if (!TryParseVersion(normalizedRemote, out var remoteVer))
            return false;
        if (!TryParseVersion(currentVersion, out var currentVer))
            return false;
        return remoteVer > currentVer;
    }

    internal static bool TryParseVersion(string? raw, out Version parsed)
    {
        parsed = new Version(0, 0, 0);
        string norm = NormalizeVersionString(raw);
        if (string.IsNullOrEmpty(norm))
            return false;

        if (!Version.TryParse(norm, out var v))
            return false;

        int build = Math.Max(0, v.Build);
        parsed = new Version(v.Major, Math.Max(0, v.Minor), build);
        return true;
    }

    private static string NormalizeVersionString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        string s = raw.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
            s = s[1..];

        int suffixIdx = s.IndexOfAny(['-', '+']);
        if (suffixIdx >= 0)
            s = s[..suffixIdx];

        return s.Trim();
    }

    internal static string GetCurrentAppVersion() => ReadCurrentAssemblyVersion();

    private static string ReadCurrentAssemblyVersion()
    {
        var v = typeof(AppUpdateService).Assembly.GetName().Version;
        if (v == null || v.Major < 0)
            return "1.0.5";
        return $"{v.Major}.{Math.Max(0, v.Minor)}.{Math.Max(0, v.Build)}";
    }

    private static GitHubReleaseAssetDto? FindSetupAsset(List<GitHubReleaseAssetDto>? assets)
    {
        if (assets == null || assets.Count == 0)
            return null;

        foreach (var a in assets)
        {
            if (string.Equals(a.Name, "Cueway-Setup-x64.exe", StringComparison.OrdinalIgnoreCase))
                return a;
        }
        foreach (var a in assets)
        {
            if (!string.IsNullOrWhiteSpace(a.Name) &&
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
            {
                return a;
            }
        }
        return null;
    }

    private bool IsAllowedDownloadUrl(string url)
        => url.StartsWith(_allowedDownloadPrefix, StringComparison.OrdinalIgnoreCase);

    private string? TryFindValidCachedInstaller(string version, long expectedSize)
    {
        string path = Path.Combine(_updatesDir, $"Cueway-Setup-x64-{SanitizeFileToken(version)}.exe");
        return IsValidPeExecutable(path, _minDownloadBytes, expectedSize > 0 ? expectedSize : 0) ? path : null;
    }

    private void CleanupUpdatesDirectory(string keepFileName)
    {
        try
        {
            if (!Directory.Exists(_updatesDir))
                return;

            foreach (string file in Directory.EnumerateFiles(_updatesDir))
            {
                string name = Path.GetFileName(file);
                if (string.Equals(name, keepFileName, StringComparison.OrdinalIgnoreCase))
                    continue;
                TryDeleteFile(file);
            }
        }
        catch { }
    }

    private static bool IsValidPeExecutable(string path, long minBytes, long expectedExactSize = 0)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length < minBytes)
                return false;
            if (expectedExactSize > 0 && fi.Length != expectedExactSize)
                return false;

            Span<byte> hdr = stackalloc byte[2];
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return fs.Read(hdr) == 2 && hdr[0] == (byte)'M' && hdr[1] == (byte)'Z';
        }
        catch
        {
            return false;
        }
    }

    private static string SanitizeFileToken(string token)
    {
        Span<char> buf = stackalloc char[Math.Min(token.Length, 32)];
        int n = 0;
        foreach (char c in token)
        {
            if (n >= buf.Length) break;
            buf[n++] = (char.IsAsciiLetterOrDigit(c) || c == '.') ? c : '_';
        }
        return n > 0 ? new string(buf[..n]) : "latest";
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private void NotifyStateChanged()
    {
        UpdateSnapshot snap;
        lock (_syncLock)
            snap = _snapshot;
        StateChanged?.Invoke(snap);
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;
        HttpClient? http;
        lock (_syncLock)
        {
            if (_disposed) return;
            _disposed = true;
            cts = _downloadCts;
            _downloadCts = null;
            http = _http;
            _http = null;
        }
        try { cts?.Cancel(); } catch { }
        try { cts?.Dispose(); } catch { }
        try { http?.Dispose(); } catch { }
    }
}
