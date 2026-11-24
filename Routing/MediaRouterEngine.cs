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
    public required MediaSessionInfo? SecondaryTarget { get; init; }
    public required List<MediaSessionInfo> Sessions { get; init; }
    public required bool IsManualLock { get; init; }
    public required bool SoloModeEnabled { get; init; }
    public required bool GameGuardActive { get; init; }
}

internal sealed class MediaRouterEngine : IDisposable
{
    private static readonly HashSet<int> EmptyPidSet = [];

    private readonly StateStore _stateStore;
    private readonly CoreAudioService _audioService;
    private readonly MediaSessionManager _sessionManager;
    private readonly object _lock = new();

    private string? _lockedSessionId;
    private string? _lockedAppKey;
    private bool _gameGuardActive;

    // Auto-pause / Auto-resume tracking
    private readonly Dictionary<string, bool> _prevEffectivePlaying = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _autoPausedBy = new(StringComparer.Ordinal); // pausedSessionId -> triggerSessionId
    private readonly HashSet<string> _suppressNextTransition = new(StringComparer.Ordinal);

    private RouterStateSnapshot _snapshot = new()
    {
        ActiveTarget = null,
        SecondaryTarget = null,
        Sessions = [],
        IsManualLock = false,
        SoloModeEnabled = false,
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

        List<string> toAutoPause = [];
        List<string> toAutoResume = [];
        string? autoPauseTriggerId = null;

        RouterStateSnapshot prevSnapshot;
        RouterStateSnapshot nextSnapshot;

        lock (_lock)
        {
            _gameGuardActive = gameActive;

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

            // Auto-Pause / Auto-Resume engine
            if (settings.AutoPauseEnabled && lockedSession == null)
            {
                EvaluateAutoPauseLocked(sessions, activeTarget, toAutoPause, toAutoResume, out autoPauseTriggerId);
            }
            else if (!settings.AutoPauseEnabled)
            {
                _autoPausedBy.Clear();
            }

            UpdatePrevPlayingLocked(sessions);

            prevSnapshot = _snapshot;
            nextSnapshot = new RouterStateSnapshot
            {
                ActiveTarget = activeTarget,
                SecondaryTarget = secondaryTarget,
                Sessions = sessions,
                IsManualLock = lockedSession != null,
                SoloModeEnabled = settings.SoloModeEnabled,
                GameGuardActive = gameActive
            };
            _snapshot = nextSnapshot;
        }

        // Apply Solo Mode synchronization outside lock
        SyncSoloMode(settings.SoloModeEnabled, nextSnapshot.ActiveTarget);

        // Execute auto-pause / auto-resume commands asynchronously
        if (toAutoPause.Count > 0 || toAutoResume.Count > 0)
        {
            _ = ExecuteAutoPauseResumeAsync(toAutoPause, toAutoResume, autoPauseTriggerId);
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
            a.SoloModeEnabled != b.SoloModeEnabled ||
            a.GameGuardActive != b.GameGuardActive)
            return false;

        if (!SessionEquivalent(a.ActiveTarget, b.ActiveTarget) ||
            !SessionEquivalent(a.SecondaryTarget, b.SecondaryTarget))
            return false;

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

        IEnumerable<MediaSessionInfo> pool = sessions;
        if (gameGuardActive)
        {
            var nonBrowsers = sessions.Where(s => !s.IsBrowser).ToList();
            if (nonBrowsers.Count > 0)
                pool = nonBrowsers;
        }

        return pool
            .OrderBy(s => s.IsPassiveTab)
            .ThenBy(s => s.IsMuted || s.Volume <= 0.005f)
            .ThenBy(s => _stateStore.GetPriorityIndex(s.AppKey))
            .ThenByDescending(s => s.IsEffectivelyPlaying)
            .ThenByDescending(s => s.IsPlaying)
            .ThenBy(s => s.IsBrowser)
            .FirstOrDefault();
    }

    private MediaSessionInfo? SelectSecondaryTarget(List<MediaSessionInfo> sessions, MediaSessionInfo? primary)
    {
        if (sessions.Count < 2)
            return null;

        return sessions
            .Where(s => primary == null || !string.Equals(s.SessionId, primary.SessionId, StringComparison.Ordinal))
            .OrderBy(s => s.IsPassiveTab)
            .ThenBy(s => s.IsMuted || s.Volume <= 0.005f)
            .ThenBy(s => _stateStore.GetPriorityIndex(s.AppKey))
            .ThenByDescending(s => s.IsEffectivelyPlaying)
            .ThenByDescending(s => s.IsPlaying)
            .FirstOrDefault();
    }

    private void EvaluateAutoPauseLocked(
        List<MediaSessionInfo> sessions,
        MediaSessionInfo? activeTarget,
        List<string> toAutoPause,
        List<string> toAutoResume,
        out string? triggerId)
    {
        triggerId = null;

        var sessionMap = new Dictionary<string, MediaSessionInfo>(StringComparer.Ordinal);
        foreach (var s in sessions)
            sessionMap[s.SessionId] = s;

        // Clean up disappeared sessions from _autoPausedBy
        var staleKeys = _autoPausedBy.Keys.Where(k => !sessionMap.ContainsKey(k)).ToList();
        foreach (string k in staleKeys)
            _autoPausedBy.Remove(k);

        // 1. Check if any session newly started effectively playing and holds higher priority than other playing sessions
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

        // 2. Check if any trigger session stopped playing or disappeared -> resume auto-paused sessions
        if (_autoPausedBy.Count > 0)
        {
            var resumable = new List<string>();
            foreach (var kv in _autoPausedBy)
            {
                string pausedId = kv.Key;
                string trigId = kv.Value;

                bool triggerStillPlaying = sessionMap.TryGetValue(trigId, out var trigSession) && trigSession.IsEffectivelyPlaying;
                if (!triggerStillPlaying)
                {
                    if (sessionMap.TryGetValue(pausedId, out var pausedSession) && !pausedSession.IsPlaying)
                    {
                        resumable.Add(pausedId);
                    }
                    else
                    {
                        resumable.Add(pausedId); // remove entry anyway
                    }
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

    private void SyncSoloMode(bool soloEnabled, MediaSessionInfo? activeTarget)
    {
        if (!soloEnabled || activeTarget == null)
        {
            _audioService.ApplySoloMode(false, EmptyPidSet);
            return;
        }

        var allowed = new HashSet<int>(activeTarget.ProcessIds);
        _audioService.ApplySoloMode(true, allowed);
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

    public void ToggleSoloMode()
    {
        bool nextSolo = false;
        _stateStore.UpdateSettings(s =>
        {
            s.SoloModeEnabled = !s.SoloModeEnabled;
            nextSolo = s.SoloModeEnabled;
        });

        var snap = GetSnapshot();
        NotifyOsd(
            Strings.OsdSoloTitle,
            nextSolo
                ? Strings.OsdSoloEnabled(snap.ActiveTarget?.DisplayName ?? Strings.OsdSoloNoTarget)
                : Strings.OsdSoloDisabled,
            snap.ActiveTarget?.ExePath,
            appKey: snap.ActiveTarget?.AppKey);
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
                            }
                            return;
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

    public void AdjustActiveTargetVolume(float delta)
    {
        var target = GetSnapshot().ActiveTarget;
        if (target == null)
            return;

        _ = AdjustTargetVolumeInternalAsync(target, delta);
    }

    private async Task AdjustTargetVolumeInternalAsync(MediaSessionInfo target, float delta)
    {
        int? pct = await _sessionManager.AdjustSessionVolumeAsync(target.SessionId, delta).ConfigureAwait(false);
        if (pct.HasValue)
        {
            NotifyOsd(target.DisplayName, Strings.OsdAppVolume(pct.Value), target.ExePath, appKey: target.AppKey);
        }
    }

    public Task<int?> SetSessionVolumeAsync(string sessionId, float level)
        => _sessionManager.SetSessionVolumeAsync(sessionId, level);

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
    }
}
