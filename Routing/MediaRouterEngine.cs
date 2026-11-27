using Cueway.Audio;
using Cueway.Config;
using Cueway.Localization;
using Cueway.Media;

namespace Cueway.Routing;

internal enum MediaActionKind
{
    PlayPause,
    Next,
    Previous,
    SeekForward,
    SeekBackward
}

internal sealed record OsdMessage(string AppName, string Subtitle, string? ExePath, bool IsWarning = false, string? AppKey = null);

internal sealed class RouterStateSnapshot
{
    public required MediaSessionInfo? ActiveTarget { get; init; }
    public MediaSessionInfo? NowPlayingTarget { get; init; }
    public List<MediaSessionInfo> NowPlayingSessions { get; init; } = [];
    public bool IsNowPlayingClosed { get; init; }
    public required MediaSessionInfo? SecondaryTarget { get; init; }
    public required List<MediaSessionInfo> Sessions { get; init; }
    public required bool IsManualLock { get; init; }
    public required bool GameGuardActive { get; init; }
}

internal sealed class MediaRouterEngine : IDisposable
{
    private readonly StateStore _stateStore;
    private readonly CoreAudioService _audioService;
    private readonly MediaSessionManager _sessionManager;
    private readonly object _lock = new();

    private string? _lockedSessionId;
    private string? _lockedAppKey;
    private string? _explicitPromoteAppKey;
    private string? _lastPlayedSessionId;
    private string? _lastPlayedAppKey;
    private MediaSessionInfo? _lastPlayedSnapshot;
    private bool _gameGuardActive;

    private sealed record DuckedEntry(
        float OriginalVolume,
        string TriggerSessionId,
        SessionSource Source,
        string AppKey,
        int[] ProcessIds);

    // Auto-pause / Auto-resume / Auto-ducking tracking
    private readonly Dictionary<string, bool> _prevEffectivePlaying = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _autoPausedBy = new(StringComparer.Ordinal); // pausedSessionId -> triggerSessionId
    private readonly HashSet<string> _suppressNextTransition = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DuckedEntry> _autoDucked = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _volumeRampVersion = new(StringComparer.Ordinal);

    // Per-site volume memory, voice call mic auto-ducking & sleep timer state
    private readonly HashSet<string> _volumeMemoryAppliedSessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (float OriginalVolume, SessionSource Source)> _micDucked = new(StringComparer.Ordinal);
    private long _micStoppedSinceTick;
    private long _sleepTimerEndTick;
    private bool _sleepFadeActive;
    private readonly Dictionary<string, (float OriginalVolume, SessionSource Source)> _sleepFadedSessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _sessionOutputDeviceIds = new(StringComparer.Ordinal);

    private long BumpRampVersionLocked(string sessionId)
    {
        long next = _volumeRampVersion.TryGetValue(sessionId, out long cur) ? cur + 1 : 1;
        _volumeRampVersion[sessionId] = next;
        return next;
    }

    private bool IsRampVersionCurrent(string sessionId, long version)
    {
        lock (_lock)
            return _volumeRampVersion.TryGetValue(sessionId, out long cur) && cur == version;
    }

    public bool IsSleepTimerActive { get { lock (_lock) return _sleepTimerEndTick > 0; } }
    public bool IsMicDuckingActive { get { lock (_lock) return _micDucked.Count > 0; } }
    public bool IsPerAppAudioRoutingSupported => _audioService.IsPerAppAudioRoutingSupported();

    public event Action? SleepTimerChanged;

    private RouterStateSnapshot _snapshot = new()
    {
        ActiveTarget = null,
        NowPlayingTarget = null,
        NowPlayingSessions = [],
        IsNowPlayingClosed = false,
        SecondaryTarget = null,
        Sessions = [],
        IsManualLock = false,
        GameGuardActive = false
    };

    public event Action<RouterStateSnapshot>? StateChanged;
    public event Action<OsdMessage>? OsdRequested;

    public MediaRouterEngine(
        StateStore stateStore,
        CoreAudioService audioService,
        MediaSessionManager sessionManager)
    {
        _stateStore = stateStore;
        _audioService = audioService;
        _sessionManager = sessionManager;

        _sessionManager.SessionsUpdated += OnSessionsUpdated;
        _stateStore.StateChanged += OnStoreStateChanged;
        _audioService.DefaultOutputDeviceChanged += OnAudioOutputDeviceChanged;
    }

    public RouterStateSnapshot GetSnapshot()
    {
        lock (_lock)
            return _snapshot;
    }

    public void Reevaluate() => Evaluate(forceNotify: true);

    private void OnSessionsUpdated() => Evaluate(forceNotify: false);

    private void OnStoreStateChanged() => Evaluate(forceNotify: true);

    private void Evaluate(bool forceNotify)
    {
        var sessions = _sessionManager.GetSessionsSnapshot();
        var settings = _stateStore.GetSettingsSnapshot();
        bool gameActive = settings.GameFullscreenGuardEnabled && FullscreenGameDetector.IsFullscreenGameActive(settings);
        bool micActive = settings.MicDuckingEnabled && _audioService.IsAnyMicrophoneSessionActive();

        Dictionary<int, string?>? appOutMap = null;
        string? defaultOutId = null;
        if (sessions.Count > 0 && _audioService.IsPerAppAudioRoutingSupported())
        {
            var allPids = new List<int>(sessions.Count);
            foreach (var s in sessions)
            {
                foreach (int p in s.ProcessIds)
                {
                    if (p > 0) allPids.Add(p);
                }
            }
            if (allPids.Count > 0)
                appOutMap = _audioService.GetAppOutputDeviceIds(allPids);
        }
        if (sessions.Count > 1 && (settings.AutoDuckingEnabled || settings.AutoPauseEnabled))
        {
            defaultOutId = _audioService.GetDefaultOutputDeviceId();
        }

        List<string> toAutoPause = [];
        List<string> toAutoResume = [];
        string? autoPauseTriggerId = null;
        List<(string SessionId, float TargetVol, SessionSource Source, long RampVersion)> toDuck = [];
        List<(string SessionId, float RestoreVol, SessionSource Source, long RampVersion, int[]? FallbackPids)> toUnduck = [];
        List<(string SessionId, float TargetVol)> toApplySavedVol = [];

        RouterStateSnapshot prevSnapshot;
        RouterStateSnapshot nextSnapshot;

        lock (_lock)
        {
            _gameGuardActive = gameActive;

            if (!string.IsNullOrEmpty(_lockedAppKey) && _stateStore.IsAppIgnored(_lockedAppKey))
            {
                _lockedSessionId = null;
                _lockedAppKey = null;
            }
            if (!string.IsNullOrEmpty(_lastPlayedAppKey) && _stateStore.IsAppIgnored(_lastPlayedAppKey))
            {
                _lastPlayedSessionId = null;
                _lastPlayedAppKey = null;
                _lastPlayedSnapshot = null;
            }

            MediaSessionInfo? lockedSession = null;
            if (!string.IsNullOrEmpty(_lockedSessionId))
            {
                lockedSession = sessions.Find(s => string.Equals(s.SessionId, _lockedSessionId, StringComparison.Ordinal));
                if (lockedSession == null && !string.IsNullOrEmpty(_lockedAppKey))
                {
                    lockedSession = sessions.Find(s => string.Equals(s.AppKey, _lockedAppKey, StringComparison.OrdinalIgnoreCase));
                    if (lockedSession != null)
                        _lockedSessionId = lockedSession.SessionId;
                }

                if (lockedSession == null)
                {
                    // Locked app exited; fall back to Auto mode cleanly
                    _lockedSessionId = null;
                    _lockedAppKey = null;
                }
            }

            var activeTarget = lockedSession ?? SelectAutoTarget(sessions, gameActive);
            var secondaryTarget = SelectSecondaryTarget(sessions, activeTarget);
            var nowPlayingTarget = SelectNowPlayingTarget(sessions, activeTarget);
            bool nowPlayingClosed = false;
            if (nowPlayingTarget != null)
            {
                _lastPlayedSessionId = nowPlayingTarget.SessionId;
                _lastPlayedAppKey = nowPlayingTarget.AppKey;
                _lastPlayedSnapshot = nowPlayingTarget;
            }
            else
            {
                MediaSessionInfo? lastPlayed = null;
                if (!string.IsNullOrEmpty(_lastPlayedSessionId))
                    lastPlayed = sessions.Find(s => string.Equals(s.SessionId, _lastPlayedSessionId, StringComparison.Ordinal));
                if (lastPlayed == null && !string.IsNullOrEmpty(_lastPlayedAppKey))
                {
                    lastPlayed = sessions.Find(s => !s.IsPassiveTab && string.Equals(s.AppKey, _lastPlayedAppKey, StringComparison.OrdinalIgnoreCase));
                    if (lastPlayed != null)
                        _lastPlayedSessionId = lastPlayed.SessionId;
                }

                if (lastPlayed != null)
                {
                    nowPlayingTarget = lastPlayed;
                    _lastPlayedSnapshot = lastPlayed;
                }
                else if (_lastPlayedSnapshot != null)
                {
                    nowPlayingTarget = CreateClosedSnapshot(_lastPlayedSnapshot);
                    nowPlayingClosed = true;
                    _lastPlayedSessionId = null;
                }
            }

            sessions.Sort((a, b) =>
            {
                bool aIsTarget = activeTarget != null && string.Equals(a.SessionId, activeTarget.SessionId, StringComparison.Ordinal);
                bool bIsTarget = activeTarget != null && string.Equals(b.SessionId, activeTarget.SessionId, StringComparison.Ordinal);
                if (aIsTarget != bIsTarget) return aIsTarget ? -1 : 1;

                int c = a.IsPassiveTab.CompareTo(b.IsPassiveTab);
                if (c != 0) return c;

                int prioA = _stateStore.GetPriorityIndex(a.AppKey);
                int prioB = _stateStore.GetPriorityIndex(b.AppKey);
                if (prioA != prioB) return prioA.CompareTo(prioB);

                c = b.IsEffectivelyPlaying.CompareTo(a.IsEffectivelyPlaying);
                if (c != 0) return c;

                c = b.IsPlaying.CompareTo(a.IsPlaying);
                if (c != 0) return c;

                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });

            var nowPlayingSessions = new List<MediaSessionInfo>(3);
            for (int i = 0; i < sessions.Count && nowPlayingSessions.Count < 3; i++)
            {
                var s = sessions[i];
                if (!s.IsPassiveTab && s.IsEffectivelyPlaying)
                    nowPlayingSessions.Add(s);
            }

            _sessionOutputDeviceIds.Clear();
            if (appOutMap != null)
            {
                foreach (var s in sessions)
                {
                    string? assigned = null;
                    foreach (int p in s.ProcessIds)
                    {
                        if (appOutMap.TryGetValue(p, out string? devId) && !string.IsNullOrWhiteSpace(devId))
                        {
                            assigned = devId;
                            break;
                        }
                    }
                    _sessionOutputDeviceIds[s.SessionId] = assigned;
                }
            }

            // Auto-Pause / Auto-Resume engine (scoped to same effective output device)
            if (settings.AutoPauseEnabled && lockedSession == null)
            {
                EvaluateAutoPauseLocked(sessions, activeTarget, defaultOutId, toAutoPause, toAutoResume, out autoPauseTriggerId);
            }
            else if (!settings.AutoPauseEnabled)
            {
                _autoPausedBy.Clear();
            }

            // Auto-Ducking engine (scoped to same effective output device; always allow existing ducks to restore even when locked)
            if (settings.AutoDuckingEnabled)
            {
                EvaluateAutoDuckingLocked(
                    sessions,
                    toAutoPause,
                    defaultOutId,
                    settings.DuckVolumePercent / 100f,
                    allowNewDucks: lockedSession == null,
                    toDuck,
                    toUnduck);
            }
            else if (_autoDucked.Count > 0)
            {
                var sessionMap = new Dictionary<string, MediaSessionInfo>(sessions.Count, StringComparer.Ordinal);
                foreach (var s in sessions)
                    sessionMap[s.SessionId] = s;
                foreach (var kv in _autoDucked)
                {
                    long ver = BumpRampVersionLocked(kv.Key);
                    if (sessionMap.ContainsKey(kv.Key))
                    {
                        toUnduck.Add((kv.Key, kv.Value.OriginalVolume, kv.Value.Source, ver, kv.Value.ProcessIds));
                    }
                    else if (kv.Value.Source != SessionSource.BrowserBridge && kv.Value.ProcessIds.Length > 0)
                    {
                        toUnduck.Add((kv.Key, kv.Value.OriginalVolume, kv.Value.Source, ver, kv.Value.ProcessIds));
                    }
                }
                _autoDucked.Clear();
            }

            // Per-site volume memory (applied once when a new tab SessionId appears)
            if (!settings.RememberSiteVolumeEnabled)
            {
                _volumeMemoryAppliedSessions.Clear();
            }
            else
            {
                var liveIdSet = new HashSet<string>(sessions.Count, StringComparer.Ordinal);
                foreach (var s in sessions)
                    liveIdSet.Add(s.SessionId);
                _volumeMemoryAppliedSessions.RemoveWhere(id => !liveIdSet.Contains(id));

                foreach (var s in sessions)
                {
                    if (_volumeMemoryAppliedSessions.Add(s.SessionId) &&
                        s.Source == SessionSource.BrowserBridge &&
                        !s.IsMuted &&
                        !_autoDucked.ContainsKey(s.SessionId) &&
                        !_micDucked.ContainsKey(s.SessionId) &&
                        _stateStore.TryGetSavedSiteVolume(s.AppKey, out int savedPct))
                    {
                        float savedVol = savedPct / 100f;
                        if (MathF.Abs(s.Volume - savedVol) > 0.025f)
                        {
                            toApplySavedVol.Add((s.SessionId, savedVol));
                        }
                    }
                }
            }

            // Voice call / microphone auto-ducking
            EvaluateMicDuckingLocked(sessions, toAutoPause, settings.MicDuckingEnabled, micActive, settings.DuckVolumePercent / 100f, toApplySavedVol, toDuck, toUnduck);

            UpdatePrevPlayingLocked(sessions);

            prevSnapshot = _snapshot;
            nextSnapshot = new RouterStateSnapshot
            {
                ActiveTarget = activeTarget,
                NowPlayingTarget = nowPlayingTarget,
                NowPlayingSessions = nowPlayingSessions,
                IsNowPlayingClosed = nowPlayingClosed,
                SecondaryTarget = secondaryTarget,
                Sessions = sessions,
                IsManualLock = lockedSession != null,
                GameGuardActive = gameActive
            };
            _snapshot = nextSnapshot;
        }

        // Execute auto-pause / auto-resume commands asynchronously
        if (toAutoPause.Count > 0 || toAutoResume.Count > 0)
        {
            _ = ExecuteAutoPauseResumeAsync(toAutoPause, toAutoResume, autoPauseTriggerId);
        }

        // Execute smooth auto-ducking / unducking asynchronously
        if (toDuck.Count > 0 || toUnduck.Count > 0)
        {
            _ = ExecuteAutoDuckingAsync(toDuck, toUnduck);
        }

        // Apply saved per-site volumes for newly seen tabs asynchronously
        if (toApplySavedVol.Count > 0)
        {
            _ = Task.Run(async () =>
            {
                foreach (var (id, targetVol) in toApplySavedVol)
                    await _sessionManager.SetSessionVolumeAsync(id, targetVol).ConfigureAwait(false);
            });
        }

        if (forceNotify || !SnapshotsEquivalent(prevSnapshot, nextSnapshot))
        {
            StateChanged?.Invoke(nextSnapshot);
        }
    }

    private static bool SnapshotsEquivalent(RouterStateSnapshot a, RouterStateSnapshot b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a.IsManualLock != b.IsManualLock ||
            a.IsNowPlayingClosed != b.IsNowPlayingClosed ||
            a.GameGuardActive != b.GameGuardActive)
            return false;

        if (!SessionEquivalent(a.ActiveTarget, b.ActiveTarget) ||
            !SessionEquivalent(a.NowPlayingTarget, b.NowPlayingTarget) ||
            !SessionEquivalent(a.SecondaryTarget, b.SecondaryTarget))
            return false;

        if (a.NowPlayingSessions.Count != b.NowPlayingSessions.Count)
            return false;

        for (int i = 0; i < a.NowPlayingSessions.Count; i++)
        {
            if (!SessionEquivalent(a.NowPlayingSessions[i], b.NowPlayingSessions[i]))
                return false;
        }

        if (a.Sessions.Count != b.Sessions.Count)
            return false;

        for (int i = 0; i < a.Sessions.Count; i++)
        {
            if (!SessionEquivalent(a.Sessions[i], b.Sessions[i]))
                return false;
        }
        return true;
    }

    private static bool SessionEquivalent(MediaSessionInfo? x, MediaSessionInfo? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x == null || y == null) return false;
        return string.Equals(x.SessionId, y.SessionId, StringComparison.Ordinal) &&
               string.Equals(x.AppKey, y.AppKey, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(x.DisplayName, y.DisplayName, StringComparison.Ordinal) &&
               string.Equals(x.ExePath, y.ExePath, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(x.Title, y.Title, StringComparison.Ordinal) &&
               string.Equals(x.Artist, y.Artist, StringComparison.Ordinal) &&
               x.IsPlaying == y.IsPlaying &&
               x.IsEffectivelyPlaying == y.IsEffectivelyPlaying &&
               x.IsMuted == y.IsMuted &&
               (int)MathF.Round(x.Volume * 100f) == (int)MathF.Round(y.Volume * 100f) &&
               x.IsPassiveTab == y.IsPassiveTab &&
               x.Source == y.Source;
    }

    private MediaSessionInfo? SelectAutoTarget(List<MediaSessionInfo> sessions, bool gameGuardActive)
    {
        if (sessions.Count == 0)
            return null;

        bool filterNonBrowser = false;
        if (gameGuardActive)
        {
            for (int i = 0; i < sessions.Count; i++)
            {
                if (!sessions[i].IsBrowser)
                {
                    filterNonBrowser = true;
                    break;
                }
            }

            if (filterNonBrowser && !string.IsNullOrEmpty(_explicitPromoteAppKey))
            {
                for (int i = 0; i < sessions.Count; i++)
                {
                    var s = sessions[i];
                    if (!s.IsPassiveTab &&
                        string.Equals(s.AppKey, _explicitPromoteAppKey, StringComparison.OrdinalIgnoreCase) &&
                        _stateStore.GetPriorityIndex(s.AppKey) == 0)
                    {
                        filterNonBrowser = false;
                        break;
                    }
                }
            }
        }

        MediaSessionInfo? best = null;
        int bestPrio = int.MaxValue;
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            if (filterNonBrowser && s.IsBrowser)
                continue;

            int prio = _stateStore.GetPriorityIndex(s.AppKey);
            if (best == null || IsBetterTarget(s, prio, best, bestPrio, preferNonBrowser: true))
            {
                best = s;
                bestPrio = prio;
            }
        }
        return best;
    }

    private MediaSessionInfo? SelectSecondaryTarget(List<MediaSessionInfo> sessions, MediaSessionInfo? primary)
    {
        if (sessions.Count < 2)
            return null;

        MediaSessionInfo? best = null;
        int bestPrio = int.MaxValue;
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            if (primary != null && string.Equals(s.SessionId, primary.SessionId, StringComparison.Ordinal))
                continue;

            int prio = _stateStore.GetPriorityIndex(s.AppKey);
            if (best == null || IsBetterTarget(s, prio, best, bestPrio, preferNonBrowser: false))
            {
                best = s;
                bestPrio = prio;
            }
        }
        return best;
    }

    private MediaSessionInfo? SelectNowPlayingTarget(List<MediaSessionInfo> sessions, MediaSessionInfo? activeTarget)
    {
        if (activeTarget != null && activeTarget.IsEffectivelyPlaying)
            return activeTarget;

        MediaSessionInfo? best = null;
        int bestPrio = int.MaxValue;
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            if (s.IsPassiveTab || (!s.IsPlaying && !s.IsEffectivelyPlaying))
                continue;

            int prio = _stateStore.GetPriorityIndex(s.AppKey);
            if (best == null)
            {
                best = s;
                bestPrio = prio;
                continue;
            }

            int c = s.IsEffectivelyPlaying.CompareTo(best.IsEffectivelyPlaying);
            if (c != 0)
            {
                if (c > 0)
                {
                    best = s;
                    bestPrio = prio;
                }
                continue;
            }

            if (prio != bestPrio)
            {
                if (prio < bestPrio)
                {
                    best = s;
                    bestPrio = prio;
                }
                continue;
            }

            bool sIsActive = activeTarget != null && string.Equals(s.SessionId, activeTarget.SessionId, StringComparison.Ordinal);
            bool bestIsActive = activeTarget != null && string.Equals(best.SessionId, activeTarget.SessionId, StringComparison.Ordinal);
            if (sIsActive != bestIsActive)
            {
                if (sIsActive)
                {
                    best = s;
                    bestPrio = prio;
                }
                continue;
            }

            if (!s.IsBrowser && best.IsBrowser)
            {
                best = s;
                bestPrio = prio;
            }
        }
        return best;
    }

    private static MediaSessionInfo CreateClosedSnapshot(MediaSessionInfo src) => new()
    {
        SessionId = src.SessionId,
        AppKey = src.AppKey,
        SiteKey = src.SiteKey,
        DisplayName = src.DisplayName,
        ExePath = src.ExePath,
        ProcessIds = [],
        Title = src.Title,
        Artist = src.Artist,
        IsPlaying = false,
        IsEffectivelyPlaying = false,
        IsBrowser = src.IsBrowser,
        IsVideoLikely = src.IsVideoLikely,
        IsMuted = src.IsMuted,
        Volume = src.Volume,
        Source = src.Source,
        IsPassiveTab = true
    };

    private static bool IsBetterTarget(
        MediaSessionInfo cand,
        int candPrio,
        MediaSessionInfo curr,
        int currPrio,
        bool preferNonBrowser)
    {
        int c = cand.IsPassiveTab.CompareTo(curr.IsPassiveTab);
        if (c != 0) return c < 0;

        bool candSilent = cand.IsMuted || cand.Volume <= 0.005f;
        bool currSilent = curr.IsMuted || curr.Volume <= 0.005f;
        c = candSilent.CompareTo(currSilent);
        if (c != 0) return c < 0;

        if (candPrio != currPrio)
            return candPrio < currPrio;

        c = cand.IsEffectivelyPlaying.CompareTo(curr.IsEffectivelyPlaying);
        if (c != 0) return c > 0;

        c = cand.IsPlaying.CompareTo(curr.IsPlaying);
        if (c != 0) return c > 0;

        if (preferNonBrowser)
        {
            c = cand.IsBrowser.CompareTo(curr.IsBrowser);
            if (c != 0) return c < 0;
        }

        return false;
    }

    private string? ResolveEffectiveOutputDeviceLocked(string sessionId, string? defaultOutId)
    {
        if (_sessionOutputDeviceIds.TryGetValue(sessionId, out string? assigned) &&
            !string.IsNullOrWhiteSpace(assigned))
        {
            return assigned;
        }
        return defaultOutId;
    }

    private bool AreOnSameOutputDeviceLocked(string sessionA, string sessionB, string? defaultOutId)
    {
        string? devA = ResolveEffectiveOutputDeviceLocked(sessionA, defaultOutId);
        string? devB = ResolveEffectiveOutputDeviceLocked(sessionB, defaultOutId);
        return string.Equals(devA ?? string.Empty, devB ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private void EvaluateAutoPauseLocked(
        List<MediaSessionInfo> sessions,
        MediaSessionInfo? activeTarget,
        string? defaultOutId,
        List<string> toAutoPause,
        List<string> toAutoResume,
        out string? triggerId)
    {
        triggerId = null;

        if (sessions.Count <= 1 && _autoPausedBy.Count == 0)
            return;

        var sessionMap = new Dictionary<string, MediaSessionInfo>(StringComparer.Ordinal);
        foreach (var s in sessions)
            sessionMap[s.SessionId] = s;

        // Clean up disappeared sessions from _autoPausedBy
        var staleKeys = _autoPausedBy.Keys.Where(k => !sessionMap.ContainsKey(k)).ToList();
        foreach (string k in staleKeys)
            _autoPausedBy.Remove(k);

        // 1. Check if any session newly started effectively playing and holds higher priority than other playing sessions on the same output device
        foreach (var candidate in sessions)
        {
            if (_suppressNextTransition.Remove(candidate.SessionId))
                continue;

            bool wasPlaying = _prevEffectivePlaying.TryGetValue(candidate.SessionId, out bool prev) && prev;
            if (!wasPlaying && candidate.IsEffectivelyPlaying)
            {
                int candPrio = _stateStore.GetPriorityIndex(candidate.AppKey);
                foreach (var other in sessions)
                {
                    if (string.Equals(other.SessionId, candidate.SessionId, StringComparison.Ordinal))
                        continue;
                    if (!other.IsEffectivelyPlaying)
                        continue;
                    if (!AreOnSameOutputDeviceLocked(candidate.SessionId, other.SessionId, defaultOutId))
                        continue;

                    int otherPrio = _stateStore.GetPriorityIndex(other.AppKey);
                    bool candidateOutranks = candPrio < otherPrio ||
                                             (activeTarget != null && string.Equals(activeTarget.SessionId, candidate.SessionId, StringComparison.Ordinal) && candPrio == otherPrio);

                    if (candidateOutranks)
                    {
                        toAutoPause.Add(other.SessionId);
                        _autoPausedBy[other.SessionId] = candidate.SessionId;
                        _suppressNextTransition.Add(other.SessionId);
                        triggerId = candidate.SessionId;
                    }
                }
            }
        }

        // 2. Check if any trigger session stopped playing, disappeared, or moved to another output -> resume auto-paused sessions
        if (_autoPausedBy.Count > 0)
        {
            var resumable = new List<string>();
            foreach (var kv in _autoPausedBy)
            {
                string pausedId = kv.Key;
                string trigId = kv.Value;

                bool triggerStillPlayingSameOutput =
                    sessionMap.TryGetValue(trigId, out var trigSession) &&
                    trigSession.IsEffectivelyPlaying &&
                    AreOnSameOutputDeviceLocked(pausedId, trigId, defaultOutId);
                if (!triggerStillPlayingSameOutput)
                {
                    resumable.Add(pausedId);
                }
            }

            foreach (string id in resumable)
            {
                if (sessionMap.TryGetValue(id, out var pausedSession) && !pausedSession.IsPlaying)
                {
                    toAutoResume.Add(id);
                    _suppressNextTransition.Add(id);
                }
                _autoPausedBy.Remove(id);
            }
        }
    }

    private void UpdatePrevPlayingLocked(List<MediaSessionInfo> sessions)
    {
        _prevEffectivePlaying.Clear();
        foreach (var s in sessions)
            _prevEffectivePlaying[s.SessionId] = s.IsEffectivelyPlaying;
    }

    private async Task ExecuteAutoPauseResumeAsync(List<string> toPause, List<string> toResume, string? _)
    {
        foreach (string id in toPause)
            await _sessionManager.PauseAsync(id);
        foreach (string id in toResume)
            await _sessionManager.PlayAsync(id);
    }

    private void EvaluateAutoDuckingLocked(
        List<MediaSessionInfo> sessions,
        List<string> toAutoPause,
        string? defaultOutId,
        float duckTarget,
        bool allowNewDucks,
        List<(string SessionId, float TargetVol, SessionSource Source, long RampVersion)> toDuck,
        List<(string SessionId, float RestoreVol, SessionSource Source, long RampVersion, int[]? FallbackPids)> toUnduck)
    {
        if (sessions.Count <= 1 && _autoDucked.Count == 0)
            return;

        var sessionMap = new Dictionary<string, MediaSessionInfo>(sessions.Count, StringComparer.Ordinal);
        foreach (var s in sessions)
            sessionMap[s.SessionId] = s;

        // Migrate orphaned SessionIds (e.g. smtc: <-> audio:) or restore non-bridge WASAPI volume directly when a session vanished
        if (_autoDucked.Count > 0)
        {
            var missingKeys = _autoDucked.Keys.Where(k => !sessionMap.ContainsKey(k)).ToList();
            foreach (string oldKey in missingKeys)
            {
                if (!_autoDucked.Remove(oldKey, out var entry))
                    continue;

                MediaSessionInfo? successor = sessions.Find(s =>
                    string.Equals(s.AppKey, entry.AppKey, StringComparison.OrdinalIgnoreCase) &&
                    !_autoDucked.ContainsKey(s.SessionId));
                if (successor != null)
                {
                    _autoDucked[successor.SessionId] = entry with
                    {
                        Source = successor.Source,
                        ProcessIds = successor.ProcessIds
                    };
                }
                else if (entry.Source != SessionSource.BrowserBridge && entry.ProcessIds.Length > 0)
                {
                    toUnduck.Add((oldKey, entry.OriginalVolume, entry.Source, BumpRampVersionLocked(oldKey), entry.ProcessIds));
                }
            }
        }

        HashSet<string>? pendingPauseSet = toAutoPause.Count > 0
            ? new HashSet<string>(toAutoPause, StringComparer.Ordinal)
            : null;

        // Duck other effectively playing sessions on the same output device when a new media source starts playing
        if (allowNewDucks)
        {
            foreach (var candidate in sessions)
            {
                if (candidate.IsPassiveTab)
                    continue;
                if (candidate.Source == SessionSource.AudioSession &&
                    !ProcessResolver.IsKnownMediaAudioProcess(candidate.AppKey))
                    continue;

                bool wasPlaying = _prevEffectivePlaying.TryGetValue(candidate.SessionId, out bool prev) && prev;
                if (!wasPlaying && candidate.IsEffectivelyPlaying)
                {
                    foreach (var other in sessions)
                    {
                        if (string.Equals(other.SessionId, candidate.SessionId, StringComparison.Ordinal))
                            continue;
                        if (!other.IsEffectivelyPlaying)
                            continue;
                        if (!AreOnSameOutputDeviceLocked(candidate.SessionId, other.SessionId, defaultOutId))
                            continue;
                        if (pendingPauseSet != null && pendingPauseSet.Contains(other.SessionId))
                            continue;
                        if (_autoPausedBy.ContainsKey(other.SessionId))
                            continue;
                        if (_autoDucked.ContainsKey(other.SessionId) || _micDucked.ContainsKey(other.SessionId))
                            continue;
                        if (other.Volume > duckTarget + 0.02f)
                        {
                            _autoDucked[other.SessionId] = new DuckedEntry(
                                other.Volume,
                                candidate.SessionId,
                                other.Source,
                                other.AppKey,
                                other.ProcessIds);
                            long ver = BumpRampVersionLocked(other.SessionId);
                            toDuck.Add((other.SessionId, duckTarget, other.Source, ver));
                        }
                    }
                }
            }
        }

        // Restore volume once the triggering session stops playing, disappears, or moves to a different output device
        if (_autoDucked.Count > 0)
        {
            var restorable = new List<string>();
            foreach (var kv in _autoDucked)
            {
                bool triggerStillPlayingSameOutput =
                    sessionMap.TryGetValue(kv.Value.TriggerSessionId, out var trig) &&
                    !trig.IsPassiveTab &&
                    trig.IsEffectivelyPlaying &&
                    AreOnSameOutputDeviceLocked(kv.Key, kv.Value.TriggerSessionId, defaultOutId);
                if (!triggerStillPlayingSameOutput)
                {
                    restorable.Add(kv.Key);
                }
            }

            foreach (string id in restorable)
            {
                if (_autoDucked.Remove(id, out var entry))
                {
                    if (sessionMap.ContainsKey(id) ||
                        (entry.Source != SessionSource.BrowserBridge && entry.ProcessIds.Length > 0))
                    {
                        long ver = BumpRampVersionLocked(id);
                        toUnduck.Add((id, entry.OriginalVolume, entry.Source, ver, entry.ProcessIds));
                    }
                }
            }
        }
    }

    private async Task ExecuteAutoDuckingAsync(
        List<(string SessionId, float TargetVol, SessionSource Source, long RampVersion)> toDuck,
        List<(string SessionId, float RestoreVol, SessionSource Source, long RampVersion, int[]? FallbackPids)> toUnduck)
    {
        foreach (var (id, targetVol, source, rampVersion) in toDuck)
            await RampSessionVolumeAsync(id, targetVol, source, rampVersion, isRestore: false, fallbackPids: null).ConfigureAwait(false);

        foreach (var (id, restoreVol, source, rampVersion, fallbackPids) in toUnduck)
            await RampSessionVolumeAsync(id, restoreVol, source, rampVersion, isRestore: true, fallbackPids).ConfigureAwait(false);
    }

    private async Task RampSessionVolumeAsync(
        string sessionId,
        float targetVol,
        SessionSource source,
        long rampVersion,
        bool isRestore,
        int[]? fallbackPids)
    {
        if (!IsRampVersionCurrent(sessionId, rampVersion))
            return;

        targetVol = Math.Clamp(targetVol, 0f, 1f);
        if (source == SessionSource.BrowserBridge)
        {
            await _sessionManager.SetSessionVolumeAsync(sessionId, targetVol).ConfigureAwait(false);
            if (isRestore)
            {
                await Task.Delay(350).ConfigureAwait(false);
                if (!IsRampVersionCurrent(sessionId, rampVersion))
                    return;

                var live = GetSnapshot().Sessions.Find(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));
                if (live != null && live.Volume < targetVol - 0.08f)
                {
                    await _sessionManager.SetSessionVolumeAsync(sessionId, targetVol).ConfigureAwait(false);
                }
            }
            return;
        }

        float startVol = GetSnapshot().Sessions
            .Find(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal))?.Volume ?? targetVol;
        if (MathF.Abs(startVol - targetVol) <= 0.03f)
        {
            int? pct = await _sessionManager.SetSessionVolumeAsync(sessionId, targetVol).ConfigureAwait(false);
            if (isRestore && !pct.HasValue && fallbackPids is { Length: > 0 })
                _audioService.SetAppVolume(fallbackPids, targetVol);
            return;
        }

        const int Steps = 4;
        for (int i = 1; i <= Steps; i++)
        {
            if (!IsRampVersionCurrent(sessionId, rampVersion))
                return;

            float t = (float)i / Steps;
            float v = i == Steps ? targetVol : startVol + (targetVol - startVol) * t;
            int? pct = await _sessionManager.SetSessionVolumeAsync(sessionId, v).ConfigureAwait(false);
            if (isRestore && !pct.HasValue && fallbackPids is { Length: > 0 })
            {
                _audioService.SetAppVolume(fallbackPids, targetVol);
                return;
            }
            if (i < Steps)
                await Task.Delay(45).ConfigureAwait(false);
        }
    }

    private void EvaluateMicDuckingLocked(
        List<MediaSessionInfo> sessions,
        List<string> toAutoPause,
        bool enabled,
        bool micActive,
        float duckTarget,
        List<(string SessionId, float TargetVol)> toApplySavedVol,
        List<(string SessionId, float TargetVol, SessionSource Source, long RampVersion)> toDuck,
        List<(string SessionId, float RestoreVol, SessionSource Source, long RampVersion, int[]? FallbackPids)> toUnduck)
    {
        var sessionMap = new Dictionary<string, MediaSessionInfo>(sessions.Count, StringComparer.Ordinal);
        foreach (var s in sessions)
            sessionMap[s.SessionId] = s;

        if (_micDucked.Count > 0)
        {
            var stale = _micDucked.Keys.Where(k => !sessionMap.ContainsKey(k)).ToList();
            foreach (string k in stale)
                _micDucked.Remove(k);
        }

        if (!enabled)
        {
            foreach (var kv in _micDucked)
            {
                if (sessionMap.ContainsKey(kv.Key))
                    toUnduck.Add((kv.Key, kv.Value.OriginalVolume, kv.Value.Source, BumpRampVersionLocked(kv.Key), null));
            }
            _micDucked.Clear();
            _micStoppedSinceTick = 0;
            return;
        }

        if (micActive)
        {
            _micStoppedSinceTick = 0;
            HashSet<string>? pendingPauseSet = toAutoPause.Count > 0
                ? new HashSet<string>(toAutoPause, StringComparer.Ordinal)
                : null;

            foreach (var s in sessions)
            {
                if (s.IsPassiveTab || !s.IsEffectivelyPlaying)
                    continue;
                if (s.Source == SessionSource.AudioSession && !ProcessResolver.IsKnownMediaAudioProcess(s.AppKey))
                    continue;
                if (pendingPauseSet != null && pendingPauseSet.Contains(s.SessionId))
                    continue;
                if (_autoPausedBy.ContainsKey(s.SessionId) || _autoDucked.ContainsKey(s.SessionId) || _micDucked.ContainsKey(s.SessionId))
                    continue;

                float effectiveOrigVol = s.Volume;
                int savedIdx = toApplySavedVol.FindIndex(x => string.Equals(x.SessionId, s.SessionId, StringComparison.Ordinal));
                if (savedIdx >= 0)
                {
                    effectiveOrigVol = toApplySavedVol[savedIdx].TargetVol;
                }

                if (effectiveOrigVol > duckTarget + 0.02f)
                {
                    if (savedIdx >= 0)
                        toApplySavedVol.RemoveAt(savedIdx);
                    _micDucked[s.SessionId] = (effectiveOrigVol, s.Source);
                    toDuck.Add((s.SessionId, duckTarget, s.Source, BumpRampVersionLocked(s.SessionId)));
                }
            }
        }
        else if (_micDucked.Count > 0)
        {
            long now = Environment.TickCount64;
            if (_micStoppedSinceTick == 0)
                _micStoppedSinceTick = now;

            if (now - _micStoppedSinceTick >= 1500)
            {
                foreach (var kv in _micDucked)
                {
                    if (sessionMap.ContainsKey(kv.Key))
                        toUnduck.Add((kv.Key, kv.Value.OriginalVolume, kv.Value.Source, BumpRampVersionLocked(kv.Key), null));
                }
                _micDucked.Clear();
                _micStoppedSinceTick = 0;
            }
        }
    }

    private void OnAudioOutputDeviceChanged(string reason)
    {
        var settings = _stateStore.GetSettingsSnapshot();
        if (!settings.HeadphoneGuardEnabled)
            return;

        var sessions = _sessionManager.GetSessionsSnapshot();
        var playing = sessions.Where(s => s.IsPlaying).ToList();
        if (playing.Count == 0)
            return;

        _ = Task.Run(async () =>
        {
            foreach (var s in playing)
            {
                await _sessionManager.PauseAsync(s.SessionId);
            }
        });

        NotifyOsd(Strings.OsdHeadphoneTitle, Strings.OsdHeadphoneBody, null, isWarning: true);
    }

    public void ToggleManualLockOnActiveOrSelected(string? sessionId = null)
    {
        lock (_lock)
        {
            if (sessionId != null)
            {
                if (string.Equals(_lockedSessionId, sessionId, StringComparison.Ordinal))
                {
                    _lockedSessionId = null;
                    _lockedAppKey = null;
                }
                else
                {
                    var target = _snapshot.Sessions.Find(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));
                    if (target != null)
                    {
                        _lockedSessionId = target.SessionId;
                        _lockedAppKey = target.AppKey;
                        _explicitPromoteAppKey = null;
                        _stateStore.RecordDiscoveredApp(target.AppKey, target.DisplayName, target.ExePath);
                    }
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(_lockedSessionId))
                {
                    _lockedSessionId = null;
                    _lockedAppKey = null;
                }
                else if (_snapshot.ActiveTarget != null)
                {
                    _lockedSessionId = _snapshot.ActiveTarget.SessionId;
                    _lockedAppKey = _snapshot.ActiveTarget.AppKey;
                    _explicitPromoteAppKey = null;
                }
            }
        }

        Evaluate(forceNotify: true);

        var snap = GetSnapshot();
        if (snap.IsManualLock && snap.ActiveTarget != null)
            NotifyOsd(snap.ActiveTarget.DisplayName, Strings.OsdManualLockActive, snap.ActiveTarget.ExePath, appKey: snap.ActiveTarget.AppKey);
        else
            NotifyOsd("Cueway", Strings.OsdAutoMode, null);
    }

    public void PromoteAppToTop(string appKey)
    {
        if (string.IsNullOrWhiteSpace(appKey))
            return;

        lock (_lock)
        {
            if (!string.IsNullOrEmpty(_lockedAppKey) &&
                !string.Equals(_lockedAppKey, appKey, StringComparison.OrdinalIgnoreCase))
            {
                _lockedSessionId = null;
                _lockedAppKey = null;
            }
            _explicitPromoteAppKey = appKey;
        }

        bool moved = _stateStore.MovePriorityAppToTop(appKey);
        if (!moved)
            Evaluate(forceNotify: true);
        _ = _sessionManager.RefreshAsync();

        var snap = GetSnapshot();
        var matchingSession = snap.Sessions.Find(s =>
            string.Equals(s.AppKey, appKey, StringComparison.OrdinalIgnoreCase));

        string displayName;
        string? exePath;
        if (matchingSession != null)
        {
            displayName = matchingSession.DisplayName;
            exePath = matchingSession.ExePath;
        }
        else
        {
            var info = _stateStore.GetPriorityAppInfo(appKey);
            displayName = info?.DisplayName ?? appKey;
            exePath = info?.ExePath;
        }

        NotifyOsd(displayName, Strings.OsdPromotedTop, exePath, appKey: appKey);
    }

    public void PromoteNowPlayingToTop()
    {
        var snap = GetSnapshot();
        var playing = snap.NowPlayingTarget;
        if (playing == null)
        {
            NotifyOsd("Cueway", Strings.NoNowPlayingTitle, null, isWarning: true);
            return;
        }

        _stateStore.RecordDiscoveredApp(playing.AppKey, playing.DisplayName, playing.ExePath);
        PromoteAppToTop(playing.AppKey);
    }

    public bool IsActiveTargetVideoCapable()
    {
        var target = GetSnapshot().ActiveTarget;
        return target != null && target.IsVideoLikely;
    }

    public async Task DispatchMediaActionAsync(MediaActionKind action, bool secondary = false)
    {
        var snap = GetSnapshot();
        var target = secondary ? snap.SecondaryTarget : snap.ActiveTarget;
        if (target == null)
        {
            if (secondary)
                NotifyOsd(Strings.OsdSecondaryTitle, Strings.OsdSecondaryEmpty, null, isWarning: true);
            return;
        }

        var settings = _stateStore.GetSettingsSnapshot();
        bool ok = false;
        string actionText = string.Empty;

        switch (action)
        {
            case MediaActionKind.PlayPause:
                if (!secondary)
                {
                    bool? liveTargetPlaying = _sessionManager.TryGetLiveIsPlaying(target.SessionId);
                    bool targetIsPlaying = liveTargetPlaying ?? (target.IsPlaying || target.IsEffectivelyPlaying);

                    if (targetIsPlaying)
                    {
                        lock (_lock)
                        {
                            _autoPausedBy.Remove(target.SessionId);
                        }
                        ok = await _sessionManager.PauseAsync(target.SessionId);
                        actionText = Strings.OsdActionPaused;
                    }
                    else
                    {
                        // If target is paused while another session (e.g. YouTube in browser) is playing,
                        // pause the playing session(s) first so the next Play press starts the prioritized target.
                        bool IsOtherLivePlaying(MediaSessionInfo s)
                        {
                            if (string.Equals(s.SessionId, target.SessionId, StringComparison.Ordinal))
                                return false;
                            if (s.Source == SessionSource.AudioSession && !ProcessResolver.IsKnownMediaAudioProcess(s.AppKey))
                                return false;
                            if (s.IsMuted || s.Volume <= 0.005f)
                                return false;
                            bool? live = _sessionManager.TryGetLiveIsPlaying(s.SessionId);
                            return live ?? (s.IsEffectivelyPlaying || s.IsPlaying);
                        }

                        var otherPlaying = snap.Sessions
                            .Where(IsOtherLivePlaying)
                            .ToList();

                        if (otherPlaying.Count > 0)
                        {
                            bool pausedAny = false;
                            foreach (var playingSession in otherPlaying)
                            {
                                lock (_lock)
                                {
                                    _autoPausedBy.Remove(playingSession.SessionId);
                                }
                                if (await _sessionManager.PauseAsync(playingSession.SessionId))
                                    pausedAny = true;
                            }

                            if (pausedAny)
                            {
                                var pausedNames = string.Join(", ", otherPlaying.Select(s => s.DisplayName).Distinct());
                                NotifyOsd(pausedNames, Strings.OsdActionPaused, otherPlaying[0].ExePath, appKey: otherPlaying[0].AppKey);
                                return;
                            }
                        }

                        ok = await _sessionManager.PlayAsync(target.SessionId);
                        actionText = Strings.OsdActionPlaying;
                    }
                }
                else
                {
                    ok = await _sessionManager.TogglePlayPauseAsync(target.SessionId);
                    actionText = target.IsPlaying ? Strings.OsdActionPaused : Strings.OsdActionPlaying;
                }
                break;

            case MediaActionKind.Next:
                ok = await _sessionManager.NextAsync(target.SessionId);
                actionText = Strings.OsdActionNext;
                break;

            case MediaActionKind.Previous:
                ok = await _sessionManager.PreviousAsync(target.SessionId);
                actionText = Strings.OsdActionPrev;
                break;

            case MediaActionKind.SeekForward:
                ok = await _sessionManager.SeekAsync(target.SessionId, settings.SeekSeconds);
                actionText = Strings.OsdSeekForward(settings.SeekSeconds);
                break;

            case MediaActionKind.SeekBackward:
                ok = await _sessionManager.SeekAsync(target.SessionId, -settings.SeekSeconds);
                actionText = Strings.OsdSeekBackward(settings.SeekSeconds);
                break;
        }

        if (ok)
        {
            string prefix = secondary ? Strings.OsdSecondaryPrefix + " " : string.Empty;
            string mediaLine = string.IsNullOrWhiteSpace(target.Title)
                ? actionText
                : $"{actionText} • {target.FormattedMediaTitle}";
            NotifyOsd(prefix + target.DisplayName, mediaLine, target.ExePath, appKey: target.AppKey);
        }
    }

    public async Task DispatchMediaActionToSessionAsync(string sessionId, MediaActionKind action)
    {
        var snap = GetSnapshot();
        var target = snap.Sessions.Find(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));
        if (target == null)
            return;

        var settings = _stateStore.GetSettingsSnapshot();
        bool ok = false;
        string actionText = string.Empty;

        switch (action)
        {
            case MediaActionKind.PlayPause:
                bool? livePlaying = _sessionManager.TryGetLiveIsPlaying(target.SessionId);
                bool isPlaying = livePlaying ?? (target.IsPlaying || target.IsEffectivelyPlaying);
                if (isPlaying)
                {
                    lock (_lock)
                    {
                        _autoPausedBy.Remove(target.SessionId);
                    }
                    ok = await _sessionManager.PauseAsync(target.SessionId);
                    actionText = Strings.OsdActionPaused;
                }
                else
                {
                    ok = await _sessionManager.PlayAsync(target.SessionId);
                    actionText = Strings.OsdActionPlaying;
                }
                break;

            case MediaActionKind.Next:
                ok = await _sessionManager.NextAsync(target.SessionId);
                actionText = Strings.OsdActionNext;
                break;

            case MediaActionKind.Previous:
                ok = await _sessionManager.PreviousAsync(target.SessionId);
                actionText = Strings.OsdActionPrev;
                break;

            case MediaActionKind.SeekForward:
                ok = await _sessionManager.SeekAsync(target.SessionId, settings.SeekSeconds);
                actionText = Strings.OsdSeekForward(settings.SeekSeconds);
                break;

            case MediaActionKind.SeekBackward:
                ok = await _sessionManager.SeekAsync(target.SessionId, -settings.SeekSeconds);
                actionText = Strings.OsdSeekBackward(settings.SeekSeconds);
                break;
        }

        if (ok)
        {
            string mediaLine = string.IsNullOrWhiteSpace(target.Title)
                ? actionText
                : $"{actionText} • {target.FormattedMediaTitle}";
            NotifyOsd(target.DisplayName, mediaLine, target.ExePath, appKey: target.AppKey);
        }
    }

    public void AdjustActiveTargetVolume(float delta)
    {
        var target = GetSnapshot().ActiveTarget;
        if (target == null)
            return;

        lock (_lock)
        {
            _autoDucked.Remove(target.SessionId);
            _micDucked.Remove(target.SessionId);
            BumpRampVersionLocked(target.SessionId);
        }

        _ = AdjustTargetVolumeInternalAsync(target, delta);
    }

    public void ToggleActiveTargetMute()
    {
        var target = GetSnapshot().ActiveTarget;
        if (target == null)
            return;

        _ = ToggleSessionMuteAsync(target.SessionId);
    }

    private async Task AdjustTargetVolumeInternalAsync(MediaSessionInfo target, float delta)
    {
        int? pct = await _sessionManager.AdjustSessionVolumeAsync(target.SessionId, delta).ConfigureAwait(false);
        if (pct.HasValue)
        {
            if (pct.Value >= 1 &&
                target.Source == SessionSource.BrowserBridge &&
                _stateStore.GetSettingsSnapshot().RememberSiteVolumeEnabled)
            {
                _stateStore.SaveSiteVolume(target.AppKey, target.DisplayName, pct.Value);
            }
            NotifyOsd(target.DisplayName, Strings.OsdAppVolume(pct.Value), target.ExePath, appKey: target.AppKey);
        }
    }

    public async Task<int?> SetSessionVolumeAsync(string sessionId, float level, bool commitMemory = true)
    {
        lock (_lock)
        {
            _autoDucked.Remove(sessionId);
            _micDucked.Remove(sessionId);
            BumpRampVersionLocked(sessionId);
        }
        int? pct = await _sessionManager.SetSessionVolumeAsync(sessionId, level).ConfigureAwait(false);
        if (commitMemory && pct.HasValue && pct.Value >= 1 &&
            _stateStore.GetSettingsSnapshot().RememberSiteVolumeEnabled)
        {
            var session = GetSnapshot().Sessions.Find(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));
            if (session != null && session.Source == SessionSource.BrowserBridge)
            {
                _stateStore.SaveSiteVolume(session.AppKey, session.DisplayName, pct.Value);
            }
        }
        return pct;
    }

    public List<AudioOutputDevice> GetOutputDevices() => _audioService.GetOutputDevices();

    public bool SelectOutputDevice(string deviceId, string? displayName = null)
    {
        bool ok = _audioService.SetDefaultOutputDevice(deviceId);
        if (ok && !string.IsNullOrWhiteSpace(displayName))
        {
            NotifyOsd(Strings.OsdAudioDeviceTitle, displayName, null);
        }
        return ok;
    }

    public void CycleOutputDevice()
    {
        var next = _audioService.CycleDefaultOutputDevice();
        if (next.HasValue)
        {
            NotifyOsd(Strings.OsdAudioDeviceTitle, next.Value.Name, null);
        }
    }

    public string? GetSessionOutputDeviceId(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return null;
        lock (_lock)
        {
            return _sessionOutputDeviceIds.TryGetValue(sessionId, out string? id) ? id : null;
        }
    }

    public bool SetSessionOutputDevice(string sessionId, string? deviceId, string? deviceName = null)
    {
        var snap = GetSnapshot();
        var session = snap.Sessions.Find(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));
        if (session == null || session.ProcessIds.Length == 0)
            return false;

        string? normalizedId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim();
        if (string.Equals(GetSessionOutputDeviceId(sessionId), normalizedId, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!_audioService.SetAppOutputDevice(session.ProcessIds, normalizedId))
            return false;

        var changedPids = new HashSet<int>(session.ProcessIds);
        RouterStateSnapshot updatedSnap;
        lock (_lock)
        {
            foreach (var s in _snapshot.Sessions)
            {
                foreach (int p in s.ProcessIds)
                {
                    if (changedPids.Contains(p))
                    {
                        _sessionOutputDeviceIds[s.SessionId] = normalizedId;
                        break;
                    }
                }
            }
            updatedSnap = _snapshot;
        }

        StateChanged?.Invoke(updatedSnap);
        Evaluate(forceNotify: false);
        _ = _sessionManager.RefreshAsync();
        NotifyOsd(session.DisplayName, deviceName ?? Strings.AudioDeviceSystemDefault, session.ExePath, appKey: session.AppKey);
        return true;
    }

    public void StartSleepTimer(int minutes)
    {
        if (minutes <= 0)
        {
            CancelSleepTimer();
            return;
        }

        List<(string SessionId, float OriginalVolume)> toRestore;
        lock (_lock)
        {
            _sleepTimerEndTick = Environment.TickCount64 + minutes * 60_000L;
            _sleepFadeActive = false;
            toRestore = new List<(string, float)>(_sleepFadedSessions.Count);
            foreach (var kv in _sleepFadedSessions)
            {
                BumpRampVersionLocked(kv.Key);
                toRestore.Add((kv.Key, kv.Value.OriginalVolume));
            }
            _sleepFadedSessions.Clear();
        }

        if (toRestore.Count > 0)
        {
            _ = Task.Run(async () =>
            {
                foreach (var (id, origVol) in toRestore)
                    await _sessionManager.SetSessionVolumeAsync(id, origVol).ConfigureAwait(false);
            });
        }

        SleepTimerChanged?.Invoke();
        NotifyOsd(Strings.OsdSleepTimerTitle, Strings.OsdSleepTimerSet(minutes), null);
    }

    public void CancelSleepTimer()
    {
        List<(string SessionId, float OriginalVolume)> toRestore;
        bool wasActive;
        lock (_lock)
        {
            wasActive = _sleepTimerEndTick > 0;
            _sleepTimerEndTick = 0;
            _sleepFadeActive = false;
            toRestore = new List<(string, float)>(_sleepFadedSessions.Count);
            foreach (var kv in _sleepFadedSessions)
            {
                BumpRampVersionLocked(kv.Key);
                toRestore.Add((kv.Key, kv.Value.OriginalVolume));
            }
            _sleepFadedSessions.Clear();
        }

        if (toRestore.Count > 0)
        {
            _ = Task.Run(async () =>
            {
                foreach (var (id, origVol) in toRestore)
                    await _sessionManager.SetSessionVolumeAsync(id, origVol).ConfigureAwait(false);
            });
        }

        if (wasActive)
        {
            SleepTimerChanged?.Invoke();
            NotifyOsd(Strings.OsdSleepTimerTitle, Strings.OsdSleepTimerCancelled, null);
        }
    }

    public TimeSpan? GetSleepTimerRemaining()
    {
        lock (_lock)
        {
            if (_sleepTimerEndTick <= 0)
                return null;
            long remMs = Math.Max(0, _sleepTimerEndTick - Environment.TickCount64);
            return TimeSpan.FromMilliseconds(remMs);
        }
    }

    public void TickSleepTimer()
    {
        long remainingMs;
        List<(string SessionId, float TargetVol)> toFade = [];
        List<string> toPause = [];
        List<(string SessionId, float OriginalVolume)> toRestore = [];
        bool finished = false;

        lock (_lock)
        {
            if (_sleepTimerEndTick <= 0)
                return;

            remainingMs = _sleepTimerEndTick - Environment.TickCount64;
            if (remainingMs <= 0)
            {
                finished = true;
                _sleepTimerEndTick = 0;
                _sleepFadeActive = false;
                foreach (var kv in _sleepFadedSessions)
                {
                    BumpRampVersionLocked(kv.Key);
                    toRestore.Add((kv.Key, kv.Value.OriginalVolume));
                }
                _sleepFadedSessions.Clear();

                foreach (var s in _snapshot.Sessions)
                {
                    if (s.IsPlaying || s.IsEffectivelyPlaying)
                        toPause.Add(s.SessionId);
                }
            }
            else if (remainingMs <= 10_000)
            {
                if (!_sleepFadeActive)
                {
                    _sleepFadeActive = true;
                    _sleepFadedSessions.Clear();
                    foreach (var s in _snapshot.Sessions)
                    {
                        if (!s.IsPassiveTab && (s.IsPlaying || s.IsEffectivelyPlaying) && s.Volume > 0.01f)
                            _sleepFadedSessions[s.SessionId] = (s.Volume, s.Source);
                    }
                }

                float factor = Math.Clamp((float)remainingMs / 10_000f, 0.05f, 1f);
                foreach (var kv in _sleepFadedSessions)
                {
                    BumpRampVersionLocked(kv.Key);
                    toFade.Add((kv.Key, kv.Value.OriginalVolume * factor));
                }
            }
        }

        SleepTimerChanged?.Invoke();

        if (finished)
        {
            _ = Task.Run(async () =>
            {
                foreach (string id in toPause)
                    await _sessionManager.PauseAsync(id).ConfigureAwait(false);
                if (toRestore.Count > 0)
                {
                    await Task.Delay(250).ConfigureAwait(false);
                    foreach (var (id, origVol) in toRestore)
                        await _sessionManager.SetSessionVolumeAsync(id, origVol).ConfigureAwait(false);
                }
                NotifyOsd(Strings.OsdSleepTimerTitle, Strings.OsdSleepTimerDone, null);
            });
            return;
        }

        if (toFade.Count > 0)
        {
            _ = Task.Run(async () =>
            {
                foreach (var (id, vol) in toFade)
                    await _sessionManager.SetSessionVolumeAsync(id, vol).ConfigureAwait(false);
            });
        }
    }

    public async Task<bool?> ToggleSessionMuteAsync(string sessionId)
    {
        var snap = GetSnapshot();
        var session = snap.Sessions.Find(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));
        bool? muted = await _sessionManager.ToggleSessionMuteAsync(sessionId).ConfigureAwait(false);
        if (muted.HasValue && session != null)
        {
            NotifyOsd(session.DisplayName, muted.Value ? Strings.OsdMuted : Strings.OsdUnmuted, session.ExePath, appKey: session.AppKey);
        }
        return muted;
    }

    public Task<bool> FocusSessionAsync(string sessionId)
        => _sessionManager.FocusSessionAsync(sessionId);

    public void NotifyOsd(string appName, string subtitle, string? exePath, bool isWarning = false, string? appKey = null)
    {
        if (!_stateStore.GetSettingsSnapshot().OsdEnabled)
            return;
        OsdRequested?.Invoke(new OsdMessage(appName, subtitle, exePath, isWarning, appKey));
    }

    public void Dispose()
    {
        _sessionManager.SessionsUpdated -= OnSessionsUpdated;
        _stateStore.StateChanged -= OnStoreStateChanged;
        _audioService.DefaultOutputDeviceChanged -= OnAudioOutputDeviceChanged;

        List<(string SessionId, float OriginalVolume)> toRestore;
        lock (_lock)
        {
            _sleepTimerEndTick = 0;
            _sleepFadeActive = false;
            toRestore = new List<(string, float)>(_autoDucked.Count + _micDucked.Count + _sleepFadedSessions.Count);
            foreach (var kv in _autoDucked)
            {
                BumpRampVersionLocked(kv.Key);
                toRestore.Add((kv.Key, kv.Value.OriginalVolume));
            }
            foreach (var kv in _micDucked)
            {
                BumpRampVersionLocked(kv.Key);
                toRestore.Add((kv.Key, kv.Value.OriginalVolume));
            }
            foreach (var kv in _sleepFadedSessions)
            {
                BumpRampVersionLocked(kv.Key);
                toRestore.Add((kv.Key, kv.Value.OriginalVolume));
            }
            _autoDucked.Clear();
            _micDucked.Clear();
            _sleepFadedSessions.Clear();
        }

        foreach (var (id, origVol) in toRestore)
        {
            try
            {
                _sessionManager.SetSessionVolumeAsync(id, origVol).Wait(300);
            }
            catch
            {
                // Best-effort volume restore during shutdown
            }
        }
    }
}
