using Windows.Media;
using Windows.Media.Control;
using Cueway.Audio;
using Cueway.Config;
using Cueway.Interop;

namespace Cueway.Media;

internal sealed class MediaSessionManager : IDisposable
{
    private readonly StateStore _stateStore;
    private readonly CoreAudioService _audioService;
    private readonly BrowserBridgeServer _bridgeServer;
    private readonly MpvIpcClient _mpvClient = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private int _refreshPending;

    private readonly object _sessionsLock = new();
    private GlobalSystemMediaTransportControlsSessionManager? _smtcManager;
    private long _lastSmtcRetryTick;
    private readonly Dictionary<string, GlobalSystemMediaTransportControlsSession> _smtcSessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string ConnectionId, int TabId)> _bridgeTargets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (bool ExpectedPlaying, long UntilTick)> _optimisticPlayback = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (float Volume, bool IsMuted, long UntilTick)> _optimisticVolume = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _audioLastPeakTick = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _audioLastHeardTick = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (MediaSessionInfo Info, long LastSeenTick)> _recentSmtcBrowserTabs = new(StringComparer.OrdinalIgnoreCase);
    private List<MediaSessionInfo> _currentSessions = [];
    private bool _disposed;

    public event Action? SessionsUpdated;

    private static readonly HashSet<string> KnownVideoExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "mpv", "mpvnet", "vlc", "stremio", "stremio-shell-ng", "stremio-desktop",
        "mpc-hc64", "mpc-hc", "mpc-be64", "mpc-be",
        "potplayer", "potplayermini64", "potplayermini",
        "kmplayer", "kodi", "jellyfinmediaplayer", "plex", "plexhtpc",
        "smplayer", "gom", "gom64", "bsplayer", "wmplayer"
    };

    public MediaSessionManager(StateStore stateStore, CoreAudioService audioService, BrowserBridgeServer bridgeServer)
    {
        _stateStore = stateStore;
        _audioService = audioService;
        _bridgeServer = bridgeServer;
        _bridgeServer.SessionsChanged += OnBridgeSessionsChanged;
    }

    private void OnBridgeSessionsChanged() => _ = RefreshAsync();

    public async Task InitializeAsync()
    {
        await TryEnsureSmtcManagerAsync().ConfigureAwait(false);
        await RefreshAsync();
    }

    private async Task TryEnsureSmtcManagerAsync()
    {
        if (_disposed || _smtcManager != null)
            return;

        long now = Environment.TickCount64;
        long prev = Interlocked.Read(ref _lastSmtcRetryTick);
        if (prev != 0 && now - prev < 3000)
            return;
        Interlocked.Exchange(ref _lastSmtcRetryTick, now);

        try
        {
            var mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().ConfigureAwait(false);
            if (mgr != null && !_disposed)
            {
                mgr.SessionsChanged += OnSmtcSessionsChanged;
                mgr.CurrentSessionChanged += OnSmtcCurrentSessionChanged;
                _smtcManager = mgr;
            }
        }
        catch
        {
            // SMTC may fail transiently at early boot or session transition; will retry cleanly
        }
    }

    private void OnSmtcSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        => _ = RefreshAsync();

    private void OnSmtcCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        => _ = RefreshAsync();

    public List<MediaSessionInfo> GetSessionsSnapshot()
    {
        lock (_sessionsLock)
        {
            return [.. _currentSessions];
        }
    }

    public async Task RefreshAsync()
    {
        if (_disposed) return;
        if (!await _refreshLock.WaitAsync(0))
        {
            Interlocked.Exchange(ref _refreshPending, 1);
            return;
        }

        do
        {
            Interlocked.Exchange(ref _refreshPending, 0);
            try
            {
                await RefreshCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                _refreshLock.Release();
            }
        }
        while (!_disposed &&
               Interlocked.Exchange(ref _refreshPending, 0) == 1 &&
               await _refreshLock.WaitAsync(0));
    }

    private async Task RefreshCoreAsync()
    {
        if (_disposed) return;
        {
            if (_smtcManager == null)
                await TryEnsureSmtcManagerAsync().ConfigureAwait(false);

            var settings = _stateStore.GetSettingsSnapshot();
            var (procSnapshot, visibleWindows) = ProcessResolver.TakeSnapshotWithWindows();
            var procsByName = ProcessResolver.BuildNameIndex(procSnapshot);
            var audioByPid = _audioService.GetAudioStatesByPid();

            var collected = new List<MediaSessionInfo>();
            var liveSmtcMap = new Dictionary<string, GlobalSystemMediaTransportControlsSession>(StringComparer.Ordinal);
            var liveBridgeMap = new Dictionary<string, (string ConnectionId, int TabId)>(StringComparer.Ordinal);
            var bridgeByBrowser = new Dictionary<string, List<MediaSessionInfo>>(StringComparer.OrdinalIgnoreCase);
            bool mpvSeenViaSmtc = false;

            // 1. Companion browser extension sessions (each media tab is an independent live session)
            var bridgeTabs = _bridgeServer.GetAllTabsSnapshot();
            if (bridgeTabs.Count > 0)
            {
                bool hasFirefoxProc = procsByName.ContainsKey("firefox");
                bool hasZenProc = procsByName.ContainsKey("zen");
                var seenBridgeIds = new Dictionary<string, int>(StringComparer.Ordinal);

                foreach (var rec in bridgeTabs)
                {
                    var tab = rec.Tab;
                    string browserKey = string.IsNullOrWhiteSpace(rec.BrowserKey)
                        ? "chrome"
                        : rec.BrowserKey.Trim().ToLowerInvariant();
                    if (browserKey == "firefox" && !hasFirefoxProc && hasZenProc)
                        browserKey = "zen";

                    if (!procsByName.TryGetValue(browserKey, out var browserProcs) || browserProcs.Count == 0)
                    {
                        if (browserKey == "opera" &&
                            procsByName.TryGetValue("opera_gx", out var operaGxProcs) &&
                            operaGxProcs.Count > 0)
                        {
                            browserProcs = operaGxProcs;
                            browserKey = "opera_gx";
                        }
                        else
                        {
                            continue;
                        }
                    }

                    var (pids, exePath) = ExtractPidsAndExe(browserProcs);

                    string? siteKey = BrowserSiteScanner.DetectSite(tab.Url) ??
                                      BrowserSiteScanner.DetectSite(tab.Title, allowStandaloneX: true) ??
                                      BrowserSiteScanner.ExtractHostSiteKey(tab.Url);

                    string appKey = !string.IsNullOrEmpty(siteKey) ? $"{browserKey}:{siteKey}" : browserKey;
                    string displayName = !string.IsNullOrEmpty(siteKey)
                        ? BrowserSiteScanner.FormatSiteDisplayName(siteKey)
                        : ProcessResolver.FormatDisplayName(browserKey, exePath);

                    string baseId = $"bridge:{browserKey}:{tab.TabId}";
                    seenBridgeIds.TryGetValue(baseId, out int occ);
                    seenBridgeIds[baseId] = occ + 1;
                    string sessionId = occ == 0 ? baseId : $"{baseId}#{occ + 1}";

                    var (hasAudio, _, procMuted, procVolume, _) = AggregateAudioState(pids, audioByPid);
                    float tabVol = Math.Clamp((float)tab.Volume, 0f, 1f);
                    bool tabMuted = tab.IsMuted || tabVol <= 0.005f;
                    ApplyOptimisticVolumeLatch(sessionId, ref tabVol, ref tabMuted);

                    bool isMuted = tabMuted || procMuted;
                    float volume = tabVol;
                    bool isPlaying = tab.IsPlaying;
                    bool isEffectivelyPlaying = isPlaying && !isMuted && volume > 0.005f && (!hasAudio || procVolume > 0.005f);

                    ApplyOptimisticLatch(sessionId, ref isPlaying, ref isEffectivelyPlaying, hasAudio, isMuted, volume);

                    bool isVideoLikely = !BrowserSiteScanner.IsMusicSite(siteKey) && tab.HasVideo;

                    liveBridgeMap[sessionId] = (rec.ConnectionId, tab.TabId);

                    var info = new MediaSessionInfo
                    {
                        SessionId = sessionId,
                        AppKey = appKey,
                        SiteKey = siteKey,
                        DisplayName = displayName,
                        ExePath = exePath,
                        ProcessIds = pids,
                        Title = tab.Title ?? string.Empty,
                        Artist = tab.Artist ?? string.Empty,
                        IsPlaying = isPlaying,
                        IsEffectivelyPlaying = isEffectivelyPlaying,
                        IsBrowser = true,
                        IsVideoLikely = isVideoLikely,
                        IsMuted = isMuted,
                        Volume = volume,
                        Source = SessionSource.BrowserBridge
                    };

                    collected.Add(info);
                    if (!bridgeByBrowser.TryGetValue(browserKey, out var bList))
                    {
                        bList = [];
                        bridgeByBrowser[browserKey] = bList;
                    }
                    bList.Add(info);

                    _stateStore.RecordDiscoveredApp(info.AppKey, info.DisplayName, info.ExePath);
                }
            }

            if (_smtcManager != null)
            {
                IReadOnlyList<GlobalSystemMediaTransportControlsSession> rawSessions;
                try
                {
                    rawSessions = _smtcManager.GetSessions();
                }
                catch
                {
                    rawSessions = [];
                }

                var seenIds = new Dictionary<string, int>(StringComparer.Ordinal);

                foreach (var session in rawSessions)
                {
                    try
                    {
                        var playback = session.GetPlaybackInfo();
                        if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed)
                            continue;

                        string aumid = session.SourceAppUserModelId ?? string.Empty;
                        string title = string.Empty;
                        string artist = string.Empty;
                        string album = string.Empty;
                        MediaPlaybackType? playbackType = playback.PlaybackType;

                        try
                        {
                            var props = await session.TryGetMediaPropertiesAsync().AsTask();
                            if (props != null)
                            {
                                title = props.Title ?? string.Empty;
                                artist = props.Artist ?? string.Empty;
                                album = props.AlbumTitle ?? string.Empty;
                                if (playbackType == null || playbackType == MediaPlaybackType.Unknown)
                                    playbackType = props.PlaybackType;
                            }
                        }
                        catch
                        {
                            // Some apps throw transiently while switching tracks
                        }

                        var resolved = ProcessResolver.ResolveSmtcAumid(aumid, title, procSnapshot);
                        // Ghost session filter: skip sessions whose process has exited
                        if (!resolved.IsAlive)
                            continue;

                        if (resolved.AppKey.Equals("mpv", StringComparison.OrdinalIgnoreCase))
                            mpvSeenViaSmtc = true;

                        string appKey = resolved.AppKey;
                        string displayName = resolved.DisplayName;
                        string? siteKey = null;
                        bool isPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                        if (resolved.IsBrowser)
                        {
                            // Skip expensive UIA/profile tab scanning when BrowserBridge already covers this browser & tab/site
                            if (bridgeByBrowser.TryGetValue(resolved.AppKey, out var bSessions) && bSessions.Count > 0)
                            {
                                var quickSite = BrowserSiteScanner.TryResolveQuickBrowserSite(resolved.AppKey, aumid, title, artist, album);
                                bool quickCovered = bSessions.Any(b =>
                                    (quickSite.HasValue &&
                                     string.Equals(b.SiteKey, quickSite.Value.SiteKey, StringComparison.OrdinalIgnoreCase)) ||
                                    (!string.IsNullOrWhiteSpace(title) &&
                                     !string.IsNullOrWhiteSpace(b.Title) &&
                                     (b.Title.Contains(title, StringComparison.OrdinalIgnoreCase) ||
                                      title.Contains(b.Title, StringComparison.OrdinalIgnoreCase))));

                                if (quickCovered || (!quickSite.HasValue && (!isPlaying || string.IsNullOrWhiteSpace(title))))
                                    continue;
                            }

                            IReadOnlyList<RunningProcessInfo> browserProcs = procsByName.TryGetValue(resolved.AppKey, out var bProcsList)
                                ? bProcsList
                                : [];
                            var site = BrowserSiteScanner.ResolveBrowserSite(
                                resolved.AppKey, aumid, title, artist, album, isPlaying, browserProcs, visibleWindows);
                            if (site.HasValue)
                            {
                                siteKey = site.Value.SiteKey;
                                appKey = $"{resolved.AppKey}:{siteKey}";
                                displayName = site.Value.DisplayName;
                            }

                            // Suppress duplicate SMTC entry ONLY when the browser extension bridge already covers that same tab/site
                            if (bridgeByBrowser.TryGetValue(resolved.AppKey, out bSessions) && bSessions.Count > 0)
                            {
                                bool covered = bSessions.Any(b =>
                                    (!string.IsNullOrEmpty(siteKey) &&
                                     string.Equals(b.SiteKey, siteKey, StringComparison.OrdinalIgnoreCase)) ||
                                    (!string.IsNullOrWhiteSpace(title) &&
                                     !string.IsNullOrWhiteSpace(b.Title) &&
                                     (b.Title.Contains(title, StringComparison.OrdinalIgnoreCase) ||
                                      title.Contains(b.Title, StringComparison.OrdinalIgnoreCase))));

                                if (covered || (string.IsNullOrEmpty(siteKey) && (!isPlaying || string.IsNullOrWhiteSpace(title))))
                                    continue;
                            }
                        }

                        var (hasAudio, _, isMuted, volume, _) = AggregateAudioState(resolved.ProcessIds, audioByPid);

                        string baseId = $"smtc:{appKey}";
                        seenIds.TryGetValue(baseId, out int occurrence);
                        seenIds[baseId] = occurrence + 1;
                        string sessionId = occurrence == 0 ? baseId : $"{baseId}#{occurrence + 1}";

                        ApplyOptimisticVolumeLatch(sessionId, ref volume, ref isMuted);

                        // Muted or 0-volume sessions do not count as actively playing
                        bool isEffectivelyPlaying = isPlaying && (!hasAudio || (!isMuted && volume > 0.005f));

                        bool isVideoLikely = !BrowserSiteScanner.IsMusicSite(siteKey) &&
                                             (playbackType == MediaPlaybackType.Video ||
                                              KnownVideoExes.Contains(resolved.AppKey) ||
                                              resolved.IsBrowser);

                        ApplyOptimisticLatch(sessionId, ref isPlaying, ref isEffectivelyPlaying, hasAudio, isMuted, volume);

                        liveSmtcMap[sessionId] = session;

                        var info = new MediaSessionInfo
                        {
                            SessionId = sessionId,
                            AppKey = appKey,
                            SiteKey = siteKey,
                            DisplayName = displayName,
                            ExePath = resolved.ExePath,
                            ProcessIds = resolved.ProcessIds,
                            Title = title,
                            Artist = artist,
                            IsPlaying = isPlaying,
                            IsEffectivelyPlaying = isEffectivelyPlaying,
                            IsBrowser = resolved.IsBrowser,
                            IsVideoLikely = isVideoLikely,
                            IsMuted = isMuted,
                            Volume = volume,
                            Source = SessionSource.Smtc
                        };

                        collected.Add(info);

                        // Auto-discover live non-ghost apps with valid media so priority order is immediately editable
                        if (isEffectivelyPlaying || isPlaying || !string.IsNullOrWhiteSpace(title))
                        {
                            _stateStore.RecordDiscoveredApp(info.AppKey, info.DisplayName, info.ExePath);
                        }

                        if (resolved.IsBrowser && !string.IsNullOrEmpty(siteKey) && !string.IsNullOrWhiteSpace(title))
                        {
                            lock (_sessionsLock)
                            {
                                _recentSmtcBrowserTabs[info.AppKey] = (info, Environment.TickCount64);
                            }
                        }
                    }
                    catch
                    {
                        // Ignore session that vanished mid-query
                    }
                }
            }

            // Retain recently played multi-site browser tabs when Companion Extension isn't connected for that browser
            AppendRetainedSmtcBrowserTabs(collected, bridgeByBrowser, procsByName, visibleWindows, audioByPid);

            // Optional mpv Named Pipe IPC integration (only query pipe when an mpv/mpvnet process is running)
            if (!mpvSeenViaSmtc)
            {
                procsByName.TryGetValue("mpv", out var mpvList);
                procsByName.TryGetValue("mpvnet", out var mpvnetList);
                if ((mpvList != null && mpvList.Count > 0) || (mpvnetList != null && mpvnetList.Count > 0))
                {
                    var mpvProcs = new List<RunningProcessInfo>((mpvList?.Count ?? 0) + (mpvnetList?.Count ?? 0));
                    if (mpvList != null) mpvProcs.AddRange(mpvList);
                    if (mpvnetList != null) mpvProcs.AddRange(mpvnetList);

                    var mpvState = _mpvClient.QueryState(settings.MpvPipeName);
                    if (mpvState.IsConnected)
                    {
                        var (mpvPids, mpvExePath) = ExtractPidsAndExe(mpvProcs);

                        var (hasAudio, _, isMuted, volume, _) = AggregateAudioState(mpvPids, audioByPid);
                        ApplyOptimisticVolumeLatch("mpv:ipc", ref volume, ref isMuted);
                        bool isPlaying = !mpvState.IsPaused;
                        bool isEffectivelyPlaying = isPlaying && (!hasAudio || (!isMuted && volume > 0.005f));
                        ApplyOptimisticLatch("mpv:ipc", ref isPlaying, ref isEffectivelyPlaying, hasAudio, isMuted, volume);

                        var mpvInfo = new MediaSessionInfo
                        {
                            SessionId = "mpv:ipc",
                            AppKey = "mpv",
                            DisplayName = "mpv",
                            ExePath = mpvExePath,
                            ProcessIds = mpvPids,
                            Title = mpvState.Title,
                            Artist = string.Empty,
                            IsPlaying = isPlaying,
                            IsEffectivelyPlaying = isEffectivelyPlaying,
                            IsBrowser = false,
                            IsVideoLikely = mpvState.HasVideo,
                            IsMuted = isMuted,
                            Volume = volume,
                            Source = SessionSource.MpvIpc
                        };

                        collected.Add(mpvInfo);

                        if (isEffectivelyPlaying || isPlaying)
                        {
                            _stateStore.RecordDiscoveredApp(mpvInfo.AppKey, mpvInfo.DisplayName, mpvInfo.ExePath);
                        }
                    }
                }
            }

            // 4. Universal WASAPI Audio Session Discovery (catches apps without SMTC such as Stremio, VLC, etc.)
            CollectAudioFallbackSessions(collected, procSnapshot, visibleWindows, audioByPid);

            lock (_sessionsLock)
            {
                _smtcSessions.Clear();
                foreach (var kv in liveSmtcMap)
                    _smtcSessions[kv.Key] = kv.Value;

                _bridgeTargets.Clear();
                foreach (var kv in liveBridgeMap)
                    _bridgeTargets[kv.Key] = kv.Value;

                _currentSessions = collected;
            }

            SessionsUpdated?.Invoke();
        }
    }

    private void AppendRetainedSmtcBrowserTabs(
        List<MediaSessionInfo> collected,
        Dictionary<string, List<MediaSessionInfo>> bridgeByBrowser,
        Dictionary<string, List<RunningProcessInfo>> procsByName,
        IReadOnlyList<Win32.VisibleWindowInfo> visibleWindows,
        Dictionary<uint, AudioProcessState> audioByPid)
    {
        long now = Environment.TickCount64;
        List<(string Key, MediaSessionInfo Info, long LastSeenTick)> candidates;

        lock (_sessionsLock)
        {
            if (_recentSmtcBrowserTabs.Count == 0)
                return;

            // If BrowserBridge is actively supplying tabs for a browser, prune passive SMTC tab cache for that browser
            if (bridgeByBrowser.Count > 0)
            {
                List<string>? bridgedKeys = null;
                foreach (string k in _recentSmtcBrowserTabs.Keys)
                {
                    string? bk = BrowserSiteScanner.ExtractBrowserKeyFromAppKey(k);
                    if (!string.IsNullOrEmpty(bk) && bridgeByBrowser.ContainsKey(bk))
                        (bridgedKeys ??= []).Add(k);
                }
                if (bridgedKeys != null)
                {
                    foreach (string k in bridgedKeys)
                        _recentSmtcBrowserTabs.Remove(k);
                }
            }

            candidates = new List<(string, MediaSessionInfo, long)>(_recentSmtcBrowserTabs.Count);
            foreach (var kv in _recentSmtcBrowserTabs)
                candidates.Add((kv.Key, kv.Value.Info, kv.Value.LastSeenTick));
        }

        var collectedKeys = new HashSet<string>(collected.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var s in collected)
            collectedKeys.Add(s.AppKey);
        List<string>? toRemove = null;

        foreach (var (key, cached, lastSeen) in candidates)
        {
            if (now - lastSeen > 600_000)
            {
                (toRemove ??= []).Add(key);
                continue;
            }

            if (collectedKeys.Contains(cached.AppKey))
                continue;

            string? browserKey = BrowserSiteScanner.ExtractBrowserKeyFromAppKey(cached.AppKey);
            if (string.IsNullOrEmpty(browserKey))
            {
                (toRemove ??= []).Add(key);
                continue;
            }

            if (!procsByName.TryGetValue(browserKey, out var browserProcs) || browserProcs.Count == 0)
            {
                (toRemove ??= []).Add(key);
                continue;
            }

            if (now - lastSeen > 25_000 &&
                !BrowserSiteScanner.IsSiteTabStillOpen(browserKey, cached.SiteKey, browserProcs, visibleWindows))
            {
                (toRemove ??= []).Add(key);
                continue;
            }

            var (pids, _) = ExtractPidsAndExe(browserProcs);
            var (_, _, isMuted, volume, _) = AggregateAudioState(pids, audioByPid);
            string sessionId = $"smtc-tab:{cached.AppKey}";
            ApplyOptimisticVolumeLatch(sessionId, ref volume, ref isMuted);

            collected.Add(new MediaSessionInfo
            {
                SessionId = sessionId,
                AppKey = cached.AppKey,
                SiteKey = cached.SiteKey,
                DisplayName = cached.DisplayName,
                ExePath = cached.ExePath,
                ProcessIds = pids,
                Title = cached.Title,
                Artist = cached.Artist,
                IsPlaying = false,
                IsEffectivelyPlaying = false,
                IsBrowser = true,
                IsVideoLikely = cached.IsVideoLikely,
                IsMuted = isMuted,
                Volume = volume,
                Source = SessionSource.Smtc,
                IsPassiveTab = true
            });
            collectedKeys.Add(cached.AppKey);
        }

        if (toRemove != null && toRemove.Count > 0)
        {
            lock (_sessionsLock)
            {
                foreach (string k in toRemove)
                    _recentSmtcBrowserTabs.Remove(k);
            }
        }
    }

    private static (int[] Pids, string ExePath) ExtractPidsAndExe(IReadOnlyList<RunningProcessInfo> procs)
    {
        var pids = new int[procs.Count];
        string exePath = string.Empty;
        for (int i = 0; i < procs.Count; i++)
        {
            var p = procs[i];
            pids[i] = p.Pid;
            if (exePath.Length == 0 && !string.IsNullOrEmpty(p.ExePath))
                exePath = p.ExePath;
        }
        return (pids, exePath);
    }

    private void CollectAudioFallbackSessions(
        List<MediaSessionInfo> collected,
        IReadOnlyList<RunningProcessInfo> procSnapshot,
        IReadOnlyList<Win32.VisibleWindowInfo> visibleWindows,
        Dictionary<uint, AudioProcessState> audioByPid)
    {
        bool hasStickyTracking;
        lock (_sessionsLock)
        {
            hasStickyTracking = _audioLastPeakTick.Count > 0 || _audioLastHeardTick.Count > 0;
        }

        if (hasStickyTracking)
        {
            var runningGroups = new HashSet<string>(64, StringComparer.OrdinalIgnoreCase);
            foreach (var p in procSnapshot)
            {
                string gk = ProcessResolver.NormalizeAppGroupKey(p.Name);
                if (!string.IsNullOrEmpty(gk))
                    runningGroups.Add(gk);
            }

            lock (_sessionsLock)
            {
                PruneMissingKeys(_audioLastPeakTick, runningGroups);
                PruneMissingKeys(_audioLastHeardTick, runningGroups);
            }
        }

        if (audioByPid.Count == 0)
            return;

        var coveredPids = new HashSet<int>(collected.Count * 2);
        var coveredKeys = new HashSet<string>(collected.Count * 2, StringComparer.OrdinalIgnoreCase);

        foreach (var existing in collected)
        {
            foreach (int pid in existing.ProcessIds)
            {
                if (pid > 0) coveredPids.Add(pid);
            }

            if (!string.IsNullOrWhiteSpace(existing.AppKey))
            {
                coveredKeys.Add(existing.AppKey);
                coveredKeys.Add(ProcessResolver.NormalizeAppGroupKey(existing.AppKey));
                string? bKey = BrowserSiteScanner.ExtractBrowserKeyFromAppKey(existing.AppKey);
                if (!string.IsNullOrEmpty(bKey))
                    coveredKeys.Add(bKey);
            }
        }

        // First pass: identify candidate app group keys that actually own an uncovered WASAPI session
        var audioGroupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in procSnapshot)
        {
            if (p.Pid <= 0 || coveredPids.Contains(p.Pid) || !audioByPid.ContainsKey((uint)p.Pid))
                continue;

            string groupKey = ProcessResolver.NormalizeAppGroupKey(p.Name);
            if (string.IsNullOrEmpty(groupKey) ||
                ProcessResolver.IsIgnoredAudioProcess(groupKey) ||
                coveredKeys.Contains(groupKey))
            {
                continue;
            }

            audioGroupKeys.Add(groupKey);
        }

        if (audioGroupKeys.Count == 0)
            return;

        // Second pass: collect all sibling processes for candidate audio groups only (e.g. stremio + stremio-shell-ng)
        var groups = new Dictionary<string, List<RunningProcessInfo>>(audioGroupKeys.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var p in procSnapshot)
        {
            string groupKey = ProcessResolver.NormalizeAppGroupKey(p.Name);
            if (!audioGroupKeys.Contains(groupKey))
                continue;

            if (!groups.TryGetValue(groupKey, out var list))
            {
                list = [];
                groups[groupKey] = list;
            }
            list.Add(p);
        }

        long now = Environment.TickCount64;

        foreach (var kv in groups)
        {
            string groupKey = kv.Key;
            var groupProcs = kv.Value;

            if (ProcessResolver.IsIgnoredAudioProcess(groupKey) || coveredKeys.Contains(groupKey))
                continue;

            bool anyPidCovered = false;
            var pidList = new List<int>(groupProcs.Count);
            foreach (var p in groupProcs)
            {
                if (coveredPids.Contains(p.Pid))
                {
                    anyPidCovered = true;
                    break;
                }
                pidList.Add(p.Pid);
            }
            if (anyPidCovered || pidList.Count == 0)
                continue;

            int[] groupPids = [.. pidList];
            var (hasAnyWasapiSession, hasActiveSession, isMuted, volume, maxPeak) = AggregateAudioState(groupPids, audioByPid);
            if (!hasAnyWasapiSession)
                continue;

            bool isKnownMedia = ProcessResolver.IsKnownMediaAudioProcess(groupKey);
            bool hasPeakNow = hasActiveSession && maxPeak > 0.0008f && !isMuted && volume > 0.005f;

            long lastPeakTick;
            long lastHeardTick;
            lock (_sessionsLock)
            {
                if (hasPeakNow)
                {
                    _audioLastPeakTick[groupKey] = now;
                    _audioLastHeardTick[groupKey] = now;
                }
                _audioLastPeakTick.TryGetValue(groupKey, out lastPeakTick);
                _audioLastHeardTick.TryGetValue(groupKey, out lastHeardTick);
            }

            nint mainHwnd = Win32.FindMainWindowInSnapshot(visibleWindows, groupPids, null, out string matchedWinTitle);
            bool recentlyHeard = lastHeardTick > 0 && (now - lastHeardTick) <= 600_000;

            bool shouldAdmit = isKnownMedia
                ? (hasActiveSession || recentlyHeard || mainHwnd != 0)
                : (mainHwnd != 0 && (hasPeakNow || recentlyHeard));
            if (!shouldAdmit)
                continue;

            bool isPlaying = hasActiveSession && (maxPeak > 0.0008f || (lastPeakTick > 0 && (now - lastPeakTick) <= 2400));
            string sessionId = $"audio:{groupKey}";

            ApplyOptimisticVolumeLatch(sessionId, ref volume, ref isMuted);
            bool isEffectivelyPlaying = isPlaying && !isMuted && volume > 0.005f;
            ApplyOptimisticLatch(sessionId, ref isPlaying, ref isEffectivelyPlaying, true, isMuted, volume);

            string exePath = ResolvePreferredExePath(groupProcs, mainHwnd);
            string displayName = ProcessResolver.FormatDisplayName(groupKey, exePath);
            string rawTitle = !string.IsNullOrWhiteSpace(matchedWinTitle)
                ? matchedWinTitle
                : (groupProcs.Select(p => p.MainWindowTitle).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? string.Empty);
            string title = CleanAppWindowTitle(rawTitle, displayName);

            bool isBrowser = ProcessResolver.IsBrowserProcessName(groupKey);
            bool isVideoLikely = KnownVideoExes.Contains(groupKey) || isBrowser;

            var info = new MediaSessionInfo
            {
                SessionId = sessionId,
                AppKey = groupKey,
                DisplayName = displayName,
                ExePath = exePath,
                ProcessIds = groupPids,
                Title = title,
                Artist = string.Empty,
                IsPlaying = isPlaying,
                IsEffectivelyPlaying = isEffectivelyPlaying,
                IsBrowser = isBrowser,
                IsVideoLikely = isVideoLikely,
                IsMuted = isMuted,
                Volume = volume,
                Source = SessionSource.AudioSession
            };

            collected.Add(info);

            if (isEffectivelyPlaying || isPlaying || isKnownMedia)
            {
                _stateStore.RecordDiscoveredApp(info.AppKey, info.DisplayName, info.ExePath);
            }
        }
    }

    private static void PruneMissingKeys(Dictionary<string, long> dict, HashSet<string> runningGroups)
    {
        if (dict.Count == 0)
            return;
        List<string>? stale = null;
        foreach (string k in dict.Keys)
        {
            if (!runningGroups.Contains(k))
                (stale ??= []).Add(k);
        }
        if (stale != null)
        {
            foreach (string k in stale)
                dict.Remove(k);
        }
    }

    private static string ResolvePreferredExePath(List<RunningProcessInfo> procs, nint mainHwnd)
    {
        if (mainHwnd != 0)
        {
            Win32.GetWindowThreadProcessId(mainHwnd, out uint winPid);
            if (winPid != 0)
            {
                foreach (var p in procs)
                {
                    if (p.Pid == (int)winPid && !string.IsNullOrWhiteSpace(p.ExePath))
                        return p.ExePath;
                }
            }
        }

        foreach (var p in procs)
        {
            if (!string.IsNullOrWhiteSpace(p.ExePath))
                return p.ExePath;
        }
        return string.Empty;
    }

    private static string CleanAppWindowTitle(string rawTitle, string displayName)
    {
        string title = (rawTitle ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(title) || string.Equals(title, displayName, StringComparison.OrdinalIgnoreCase))
            return title;

        string suffixDash = $" - {displayName}";
        if (title.EndsWith(suffixDash, StringComparison.OrdinalIgnoreCase) && title.Length > suffixDash.Length)
            return title[..^suffixDash.Length].Trim();

        string suffixEm = $" — {displayName}";
        if (title.EndsWith(suffixEm, StringComparison.OrdinalIgnoreCase) && title.Length > suffixEm.Length)
            return title[..^suffixEm.Length].Trim();

        string prefixDash = $"{displayName} - ";
        if (title.StartsWith(prefixDash, StringComparison.OrdinalIgnoreCase) && title.Length > prefixDash.Length)
            return title[prefixDash.Length..].Trim();

        return title;
    }

    private void ApplyOptimisticLatch(
        string sessionId,
        ref bool isPlaying,
        ref bool isEffectivelyPlaying,
        bool hasAudio,
        bool isMuted,
        float volume)
    {
        lock (_sessionsLock)
        {
            if (_optimisticPlayback.TryGetValue(sessionId, out var opt))
            {
                long nowTick = Environment.TickCount64;
                if (nowTick < opt.UntilTick)
                {
                    if (isPlaying == opt.ExpectedPlaying)
                    {
                        _optimisticPlayback.Remove(sessionId);
                    }
                    else
                    {
                        isPlaying = opt.ExpectedPlaying;
                        isEffectivelyPlaying = isPlaying && (!hasAudio || (!isMuted && volume > 0.005f));
                    }
                }
                else
                {
                    _optimisticPlayback.Remove(sessionId);
                }
            }
        }
    }

    private void ApplyOptimisticVolumeLatch(string sessionId, ref float volume, ref bool isMuted)
    {
        lock (_sessionsLock)
        {
            if (_optimisticVolume.TryGetValue(sessionId, out var opt))
            {
                long nowTick = Environment.TickCount64;
                if (nowTick < opt.UntilTick)
                {
                    if (MathF.Abs(volume - opt.Volume) <= 0.02f && isMuted == opt.IsMuted)
                    {
                        _optimisticVolume.Remove(sessionId);
                    }
                    else
                    {
                        volume = opt.Volume;
                        isMuted = opt.IsMuted;
                    }
                }
                else
                {
                    _optimisticVolume.Remove(sessionId);
                }
            }
        }
    }

    private static (bool HasAudio, bool HasActiveSession, bool IsMuted, float Volume, float MaxPeak) AggregateAudioState(
        int[] pids,
        Dictionary<uint, AudioProcessState> audioByPid)
    {
        bool foundAny = false;
        bool allMuted = true;
        float maxVolume = 0f;
        float maxPeak = 0f;

        bool foundActive = false;
        bool activeAllMuted = true;
        float activeMaxVol = 0f;
        float activeMaxPeak = 0f;

        foreach (int pid in pids)
        {
            if (pid <= 0) continue;
            if (!audioByPid.TryGetValue((uint)pid, out var state)) continue;

            foundAny = true;
            if (!state.IsMuted) allMuted = false;
            if (state.Volume > maxVolume) maxVolume = state.Volume;
            if (state.Peak > maxPeak) maxPeak = state.Peak;

            if (state.HasActiveSession)
            {
                foundActive = true;
                if (!state.IsMuted) activeAllMuted = false;
                if (state.Volume > activeMaxVol) activeMaxVol = state.Volume;
                if (state.Peak > activeMaxPeak) activeMaxPeak = state.Peak;
            }
        }

        if (!foundAny)
            return (false, false, false, 1f, 0f);

        if (foundActive)
            return (true, true, activeAllMuted, activeMaxVol, activeMaxPeak);

        return (true, false, allMuted, maxVolume, maxPeak);
    }

    public async Task<bool> TogglePlayPauseAsync(string sessionId)
    {
        var info = FindSessionInfo(sessionId);
        if (info == null) return false;

        if (info.Source == SessionSource.BrowserBridge && TryGetBridgeTarget(sessionId, out var bTarget))
        {
            bool curPlaying = _bridgeServer.TryGetTabIsPlaying(bTarget.ConnectionId, bTarget.TabId) ?? info.IsPlaying;
            bool ok = await _bridgeServer.SendCommandAsync(bTarget.ConnectionId, bTarget.TabId, curPlaying ? "pause" : "play");
            if (ok)
            {
                UpdateCachedPlaybackState(sessionId, !curPlaying);
                SchedulePostCommandRefresh();
            }
            return ok;
        }

        if (info.Source == SessionSource.MpvIpc)
        {
            bool nextPlaying = !info.IsPlaying;
            bool ok = _mpvClient.TogglePlayPause(_stateStore.GetSettingsSnapshot().MpvPipeName);
            if (ok)
            {
                UpdateCachedPlaybackState(sessionId, nextPlaying);
                SchedulePostCommandRefresh();
            }
            return ok;
        }

        if (info.Source == SessionSource.AudioSession)
        {
            nint hwnd = Win32.TryFindMainWindowForPids(info.ProcessIds, info.Title);
            if (hwnd == 0) return false;
            bool nextPlaying = !info.IsPlaying;
            bool ok = Win32.TrySendAppCommand(hwnd, Win32.APPCOMMAND_MEDIA_PLAY_PAUSE);
            if (ok)
            {
                TouchAudioPeak(info.AppKey, nextPlaying);
                UpdateCachedPlaybackState(sessionId, nextPlaying);
                SchedulePostCommandRefresh();
            }
            return ok;
        }

        if (TryGetSmtcSession(sessionId, out var smtc))
        {
            try
            {
                var status = smtc.GetPlaybackInfo().PlaybackStatus;
                bool isCurrentlyPlaying = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                bool ok = isCurrentlyPlaying
                    ? await smtc.TryPauseAsync().AsTask()
                    : await smtc.TryPlayAsync().AsTask();

                if (!ok)
                {
                    ok = await smtc.TryTogglePlayPauseAsync().AsTask();
                }

                if (ok)
                {
                    UpdateCachedPlaybackState(sessionId, !isCurrentlyPlaying);
                    SchedulePostCommandRefresh();
                }
                return ok;
            }
            catch { }
        }
        return false;
    }

    public async Task<bool> PlayAsync(string sessionId)
    {
        var info = FindSessionInfo(sessionId);
        if (info == null) return false;

        if (info.Source == SessionSource.BrowserBridge && TryGetBridgeTarget(sessionId, out var bTarget))
        {
            bool ok = await _bridgeServer.SendCommandAsync(bTarget.ConnectionId, bTarget.TabId, "play");
            if (ok)
            {
                UpdateCachedPlaybackState(sessionId, true);
                SchedulePostCommandRefresh();
            }
            return ok;
        }

        if (info.Source == SessionSource.MpvIpc)
        {
            bool ok = _mpvClient.SetPause(_stateStore.GetSettingsSnapshot().MpvPipeName, false);
            if (ok)
            {
                UpdateCachedPlaybackState(sessionId, true);
                SchedulePostCommandRefresh();
            }
            return ok;
        }

        if (info.Source == SessionSource.AudioSession)
        {
            if (info.IsPlaying) return true;
            nint hwnd = Win32.TryFindMainWindowForPids(info.ProcessIds, info.Title);
            if (hwnd == 0) return false;
            bool ok = Win32.TrySendAppCommand(hwnd, Win32.APPCOMMAND_MEDIA_PLAY_PAUSE);
            if (ok)
            {
                TouchAudioPeak(info.AppKey, true);
                UpdateCachedPlaybackState(sessionId, true);
                SchedulePostCommandRefresh();
            }
            return ok;
        }

        if (TryGetSmtcSession(sessionId, out var smtc))
        {
            try
            {
                bool ok = await smtc.TryPlayAsync().AsTask();
                if (!ok)
                {
                    var status = smtc.GetPlaybackInfo().PlaybackStatus;
                    if (status != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                        ok = await smtc.TryTogglePlayPauseAsync().AsTask();
                }
                if (ok)
                {
                    UpdateCachedPlaybackState(sessionId, true);
                    SchedulePostCommandRefresh();
                }
                return ok;
            }
            catch { }
        }
        return false;
    }

    public async Task<bool> PauseAsync(string sessionId)
    {
        var info = FindSessionInfo(sessionId);
        if (info == null) return false;

        if (info.Source == SessionSource.BrowserBridge && TryGetBridgeTarget(sessionId, out var bTarget))
        {
            bool ok = await _bridgeServer.SendCommandAsync(bTarget.ConnectionId, bTarget.TabId, "pause");
            if (ok)
            {
                UpdateCachedPlaybackState(sessionId, false);
                SchedulePostCommandRefresh();
            }
            return ok;
        }

        if (info.Source == SessionSource.MpvIpc)
        {
            bool ok = _mpvClient.SetPause(_stateStore.GetSettingsSnapshot().MpvPipeName, true);
            if (ok)
            {
                UpdateCachedPlaybackState(sessionId, false);
                SchedulePostCommandRefresh();
            }
            return ok;
        }

        if (info.Source == SessionSource.AudioSession)
        {
            if (!info.IsPlaying) return true;
            nint hwnd = Win32.TryFindMainWindowForPids(info.ProcessIds, info.Title);
            if (hwnd == 0) return false;
            bool ok = Win32.TrySendAppCommand(hwnd, Win32.APPCOMMAND_MEDIA_PLAY_PAUSE);
            if (ok)
            {
                TouchAudioPeak(info.AppKey, false);
                UpdateCachedPlaybackState(sessionId, false);
                SchedulePostCommandRefresh();
            }
            return ok;
        }

        if (TryGetSmtcSession(sessionId, out var smtc))
        {
            try
            {
                bool ok = await smtc.TryPauseAsync().AsTask();
                if (!ok)
                {
                    var status = smtc.GetPlaybackInfo().PlaybackStatus;
                    if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                        ok = await smtc.TryTogglePlayPauseAsync().AsTask();
                }
                if (ok)
                {
                    UpdateCachedPlaybackState(sessionId, false);
                    SchedulePostCommandRefresh();
                }
                return ok;
            }
            catch { }
        }
        return false;
    }

    private void UpdateCachedPlaybackState(string sessionId, bool isPlaying)
    {
        bool updated = false;
        lock (_sessionsLock)
        {
            _optimisticPlayback[sessionId] = (isPlaying, Environment.TickCount64 + 650);
            int idx = _currentSessions.FindIndex(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));
            if (idx >= 0)
            {
                var cur = _currentSessions[idx];
                bool eff = isPlaying && !cur.IsMuted && cur.Volume > 0.005f;
                if (cur.IsPlaying != isPlaying || cur.IsEffectivelyPlaying != eff)
                {
                    _currentSessions[idx] = new MediaSessionInfo
                    {
                        SessionId = cur.SessionId,
                        AppKey = cur.AppKey,
                        SiteKey = cur.SiteKey,
                        DisplayName = cur.DisplayName,
                        ExePath = cur.ExePath,
                        ProcessIds = cur.ProcessIds,
                        Title = cur.Title,
                        Artist = cur.Artist,
                        IsPlaying = isPlaying,
                        IsEffectivelyPlaying = eff,
                        IsBrowser = cur.IsBrowser,
                        IsVideoLikely = cur.IsVideoLikely,
                        IsMuted = cur.IsMuted,
                        Volume = cur.Volume,
                        Source = cur.Source,
                        IsPassiveTab = cur.IsPassiveTab
                    };
                    updated = true;
                }
            }
        }
        if (updated)
            SessionsUpdated?.Invoke();
    }

    public bool? TryGetLiveIsPlaying(string sessionId)
    {
        GlobalSystemMediaTransportControlsSession? smtc;
        (string ConnectionId, int TabId) bridgeTarget = default;
        bool hasBridge;
        lock (_sessionsLock)
        {
            if (_optimisticPlayback.TryGetValue(sessionId, out var opt))
            {
                if (Environment.TickCount64 < opt.UntilTick)
                    return opt.ExpectedPlaying;
                _optimisticPlayback.Remove(sessionId);
            }

            hasBridge = _bridgeTargets.TryGetValue(sessionId, out bridgeTarget);
            _smtcSessions.TryGetValue(sessionId, out smtc);
        }

        if (hasBridge)
            return _bridgeServer.TryGetTabIsPlaying(bridgeTarget.ConnectionId, bridgeTarget.TabId);

        if (smtc == null)
        {
            var info = FindSessionInfo(sessionId);
            return info?.Source == SessionSource.AudioSession ? info.IsPlaying : null;
        }

        try
        {
            var status = smtc.GetPlaybackInfo().PlaybackStatus;
            return status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch
        {
            return null;
        }
    }

    private void SchedulePostCommandRefresh()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(240).ConfigureAwait(false);
            if (!_disposed)
                await RefreshAsync().ConfigureAwait(false);

            await Task.Delay(510).ConfigureAwait(false);
            if (!_disposed)
                await RefreshAsync().ConfigureAwait(false);
        });
    }

    public async Task<bool> NextAsync(string sessionId)
    {
        var info = FindSessionInfo(sessionId);
        if (info == null) return false;

        if (info.Source == SessionSource.BrowserBridge && TryGetBridgeTarget(sessionId, out var bTarget))
        {
            bool ok = await _bridgeServer.SendCommandAsync(bTarget.ConnectionId, bTarget.TabId, "next");
            if (ok) SchedulePostCommandRefresh();
            return ok;
        }

        if (info.Source == SessionSource.MpvIpc)
        {
            bool ok = _mpvClient.Next(_stateStore.GetSettingsSnapshot().MpvPipeName);
            if (ok) _ = RefreshAsync();
            return ok;
        }

        if (info.Source == SessionSource.AudioSession)
        {
            nint hwnd = Win32.TryFindMainWindowForPids(info.ProcessIds, info.Title);
            bool ok = hwnd != 0 && Win32.TrySendAppCommand(hwnd, Win32.APPCOMMAND_MEDIA_NEXTTRACK);
            if (ok) SchedulePostCommandRefresh();
            return ok;
        }

        if (TryGetSmtcSession(sessionId, out var smtc))
        {
            try
            {
                bool ok = await smtc.TrySkipNextAsync().AsTask();
                if (ok) _ = RefreshAsync();
                return ok;
            }
            catch { }
        }
        return false;
    }

    public async Task<bool> PreviousAsync(string sessionId)
    {
        var info = FindSessionInfo(sessionId);
        if (info == null) return false;

        if (info.Source == SessionSource.BrowserBridge && TryGetBridgeTarget(sessionId, out var bTarget))
        {
            bool ok = await _bridgeServer.SendCommandAsync(bTarget.ConnectionId, bTarget.TabId, "previous");
            if (ok) SchedulePostCommandRefresh();
            return ok;
        }

        if (info.Source == SessionSource.MpvIpc)
        {
            bool ok = _mpvClient.Previous(_stateStore.GetSettingsSnapshot().MpvPipeName);
            if (ok) _ = RefreshAsync();
            return ok;
        }

        if (info.Source == SessionSource.AudioSession)
        {
            nint hwnd = Win32.TryFindMainWindowForPids(info.ProcessIds, info.Title);
            bool ok = hwnd != 0 && Win32.TrySendAppCommand(hwnd, Win32.APPCOMMAND_MEDIA_PREVIOUSTRACK);
            if (ok) SchedulePostCommandRefresh();
            return ok;
        }

        if (TryGetSmtcSession(sessionId, out var smtc))
        {
            try
            {
                bool ok = await smtc.TrySkipPreviousAsync().AsTask();
                if (ok) _ = RefreshAsync();
                return ok;
            }
            catch { }
        }
        return false;
    }

    public async Task<bool> SeekAsync(string sessionId, int deltaSeconds)
    {
        var info = FindSessionInfo(sessionId);
        if (info == null) return false;

        if (info.Source == SessionSource.BrowserBridge && TryGetBridgeTarget(sessionId, out var bTarget))
        {
            return await _bridgeServer.SendCommandAsync(bTarget.ConnectionId, bTarget.TabId, "seek", deltaSeconds);
        }

        if (info.Source == SessionSource.MpvIpc)
        {
            bool ok = _mpvClient.Seek(_stateStore.GetSettingsSnapshot().MpvPipeName, deltaSeconds);
            return ok;
        }

        if (TryGetSmtcSession(sessionId, out var smtc))
        {
            try
            {
                var timeline = smtc.GetTimelineProperties();
                var playback = smtc.GetPlaybackInfo();
                if (timeline != null && timeline.EndTime > timeline.StartTime)
                {
                    TimeSpan current = timeline.Position;
                    if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing &&
                        timeline.LastUpdatedTime > DateTimeOffset.MinValue)
                    {
                        var elapsed = DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
                        if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromMinutes(1))
                            current += elapsed;
                    }

                    long targetTicks = current.Ticks + TimeSpan.FromSeconds(deltaSeconds).Ticks;
                    targetTicks = Math.Clamp(targetTicks, timeline.StartTime.Ticks, timeline.EndTime.Ticks);
                    return await smtc.TryChangePlaybackPositionAsync(targetTicks).AsTask();
                }
            }
            catch { }
        }
        return false;
    }

    public async Task<int?> SetSessionVolumeAsync(string sessionId, float level)
    {
        var info = FindSessionInfo(sessionId);
        if (info == null) return null;

        float clamped = Math.Clamp(level, 0f, 1f);
        int pct = (int)MathF.Round(clamped * 100f);

        if (info.Source == SessionSource.BrowserBridge && TryGetBridgeTarget(sessionId, out var bTarget))
        {
            bool nextMuted = pct == 0;
            UpdateCachedVolumeState(sessionId, clamped, nextMuted);
            bool ok = await _bridgeServer.SendCommandAsync(bTarget.ConnectionId, bTarget.TabId, "set_volume", pct).ConfigureAwait(false);

            // Older v1 extensions (before browser reload) did not handle set_volume, so fall back to browser WASAPI volume
            if (!ok || _bridgeServer.GetConnectionProtocolVersion(bTarget.ConnectionId) < 2)
            {
                _audioService.SetAppVolume(info.ProcessIds, clamped);
            }
            return ok ? pct : null;
        }

        bool desktopMuted = clamped > 0.005f ? false : info.IsMuted;
        UpdateCachedVolumeState(sessionId, clamped, desktopMuted);
        return _audioService.SetAppVolume(info.ProcessIds, clamped);
    }

    public async Task<int?> AdjustSessionVolumeAsync(string sessionId, float delta)
    {
        var info = FindSessionInfo(sessionId);
        if (info == null) return null;

        if (info.Source == SessionSource.BrowserBridge)
        {
            float baseVol = info.IsMuted && delta > 0f && info.Volume <= 0.005f ? 0f : info.Volume;
            float next = Math.Clamp(baseVol + delta, 0f, 1f);
            return await SetSessionVolumeAsync(sessionId, next).ConfigureAwait(false);
        }

        int? pct = _audioService.AdjustAppVolume(info.ProcessIds, delta);
        if (pct.HasValue)
        {
            float nextVol = Math.Clamp(pct.Value / 100f, 0f, 1f);
            bool nextMuted = delta > 0f ? false : info.IsMuted;
            UpdateCachedVolumeState(sessionId, nextVol, nextMuted);
        }
        return pct;
    }

    public async Task<bool?> ToggleSessionMuteAsync(string sessionId)
    {
        var info = FindSessionInfo(sessionId);
        if (info == null) return null;

        if (info.Source == SessionSource.BrowserBridge && TryGetBridgeTarget(sessionId, out var bTarget))
        {
            bool nextMuted = !info.IsMuted;
            float nextVol = (!nextMuted && info.Volume <= 0.005f) ? 0.5f : info.Volume;
            UpdateCachedVolumeState(sessionId, nextVol, nextMuted);
            bool ok = await _bridgeServer.SendCommandAsync(bTarget.ConnectionId, bTarget.TabId, "toggle_mute").ConfigureAwait(false);

            if (!ok || _bridgeServer.GetConnectionProtocolVersion(bTarget.ConnectionId) < 2)
            {
                _audioService.ToggleAppMute(info.ProcessIds);
            }
            return ok ? nextMuted : null;
        }

        bool? result = _audioService.ToggleAppMute(info.ProcessIds);
        if (result.HasValue)
        {
            float nextVol = (!result.Value && info.Volume <= 0.005f) ? 0.5f : info.Volume;
            UpdateCachedVolumeState(sessionId, nextVol, result.Value);
        }
        return result;
    }

    public async Task<bool> FocusSessionAsync(string sessionId)
    {
        var info = FindSessionInfo(sessionId);
        if (info == null) return false;

        if (info.Source == SessionSource.BrowserBridge && TryGetBridgeTarget(sessionId, out var bTarget))
        {
            await _bridgeServer.SendCommandAsync(bTarget.ConnectionId, bTarget.TabId, "focus").ConfigureAwait(false);
            await Task.Delay(120).ConfigureAwait(false);

            if (Win32.TryFocusProcessWindow(info.ProcessIds, info.Title))
            {
                nint fg = Win32.GetForegroundWindow();
                string fgTitle = fg != 0 ? Win32.GetWindowText(fg) : string.Empty;
                if (string.IsNullOrWhiteSpace(info.Title) ||
                    fgTitle.Contains(info.Title, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            nint uiaHwnd = await Task.Run(() =>
                UiAutomation.TrySelectBrowserTab(Win32.GetVisibleWindowsForPids(info.ProcessIds), info.Title, info.DisplayName, info.SiteKey)
            ).ConfigureAwait(false);
            if (uiaHwnd != 0)
                return Win32.TryFocusWindow(uiaHwnd);

            return Win32.TryFocusProcessWindow(info.ProcessIds, info.Title);
        }

        if (info.IsBrowser)
        {
            string? bKey = BrowserSiteScanner.ExtractBrowserKeyFromAppKey(info.AppKey) ?? info.AppKey;
            if (_bridgeServer.TryFindMatchingTab(bKey, info.SiteKey, info.Title, out string connId, out int tabId))
            {
                await _bridgeServer.SendCommandAsync(connId, tabId, "focus").ConfigureAwait(false);
                await Task.Delay(120).ConfigureAwait(false);
            }

            nint tabHwnd = await Task.Run(() =>
                UiAutomation.TrySelectBrowserTab(Win32.GetVisibleWindowsForPids(info.ProcessIds), info.Title, info.DisplayName, info.SiteKey)
            ).ConfigureAwait(false);
            if (tabHwnd != 0)
                return Win32.TryFocusWindow(tabHwnd);
        }

        return Win32.TryFocusProcessWindow(info.ProcessIds, info.Title);
    }

    private void TouchAudioPeak(string appKey, bool isPlaying)
    {
        string gKey = ProcessResolver.NormalizeAppGroupKey(appKey);
        lock (_sessionsLock)
        {
            if (isPlaying)
                _audioLastPeakTick[gKey] = Environment.TickCount64;
            else
                _audioLastPeakTick.Remove(gKey);
        }
    }

    private void UpdateCachedVolumeState(string sessionId, float volume, bool isMuted)
    {
        bool updated = false;
        lock (_sessionsLock)
        {
            _optimisticVolume[sessionId] = (volume, isMuted, Environment.TickCount64 + 900);
            int idx = _currentSessions.FindIndex(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));
            if (idx >= 0)
            {
                var cur = _currentSessions[idx];
                bool eff = cur.IsPlaying && !isMuted && volume > 0.005f;
                if (MathF.Abs(cur.Volume - volume) > 0.001f || cur.IsMuted != isMuted || cur.IsEffectivelyPlaying != eff)
                {
                    _currentSessions[idx] = new MediaSessionInfo
                    {
                        SessionId = cur.SessionId,
                        AppKey = cur.AppKey,
                        SiteKey = cur.SiteKey,
                        DisplayName = cur.DisplayName,
                        ExePath = cur.ExePath,
                        ProcessIds = cur.ProcessIds,
                        Title = cur.Title,
                        Artist = cur.Artist,
                        IsPlaying = cur.IsPlaying,
                        IsEffectivelyPlaying = eff,
                        IsBrowser = cur.IsBrowser,
                        IsVideoLikely = cur.IsVideoLikely,
                        IsMuted = isMuted,
                        Volume = volume,
                        Source = cur.Source,
                        IsPassiveTab = cur.IsPassiveTab
                    };
                    updated = true;
                }
            }
        }
        if (updated)
            SessionsUpdated?.Invoke();
    }

    private MediaSessionInfo? FindSessionInfo(string sessionId)
    {
        lock (_sessionsLock)
        {
            return _currentSessions.Find(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));
        }
    }

    private bool TryGetSmtcSession(string sessionId, out GlobalSystemMediaTransportControlsSession session)
    {
        lock (_sessionsLock)
        {
            return _smtcSessions.TryGetValue(sessionId, out session!);
        }
    }

    private bool TryGetBridgeTarget(string sessionId, out (string ConnectionId, int TabId) target)
    {
        lock (_sessionsLock)
        {
            return _bridgeTargets.TryGetValue(sessionId, out target);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _bridgeServer.SessionsChanged -= OnBridgeSessionsChanged;
        if (_smtcManager != null)
        {
            try
            {
                _smtcManager.SessionsChanged -= OnSmtcSessionsChanged;
                _smtcManager.CurrentSessionChanged -= OnSmtcCurrentSessionChanged;
            }
            catch { }
        }
        _refreshLock.Dispose();
    }
}
