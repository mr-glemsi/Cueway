using System.Text.Json;
using Cueway.Input;
using Cueway.Localization;

namespace Cueway.Config;

internal enum GlobalHotkeySlot
{
    ToggleWindow = 0,
    ManualLock = 1,
    SoloMode = 2,
    PlayPause = 3
}

internal sealed class StateStore
{
    private readonly object _lock = new();
    private readonly string _filePath;
    private AppState _state;
    private volatile AppSettings _cachedSettingsSnapshot = new();
    private volatile Dictionary<string, int> _priorityIndexCache = new(StringComparer.OrdinalIgnoreCase);
    private volatile Dictionary<string, (string DisplayName, string ExePath, long LastSeenTick)> _discoveredFastCache = new(StringComparer.OrdinalIgnoreCase);

    public event Action? StateChanged;

    public string FilePath => _filePath;

    public StateStore()
    {
        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string baseDir = Path.Combine(localApp, "Cueway");
        Directory.CreateDirectory(baseDir);
        _filePath = Path.Combine(baseDir, "cueway_state.json");

        if (!File.Exists(_filePath))
        {
            try
            {
                string legacyPath = Path.Combine(localApp, "WinMediaRouter", "mediarouter_state.json");
                if (File.Exists(legacyPath))
                {
                    File.Copy(legacyPath, _filePath, overwrite: false);
                }
            }
            catch
            {
                // Ignore legacy state migration errors and fall back to defaults
            }
        }

        _state = LoadInternal();
        lock (_lock)
        {
            RebuildCachesLocked();
        }
        Strings.SetLanguage(_state.Settings.Language);
        UI.Theme.SetTheme(_state.Settings.Theme);
    }

    private AppState LoadInternal()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                string json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppState);
                if (loaded != null)
                {
                    loaded.Settings ??= new AppSettings();
                    loaded.PriorityApps ??= [];
                    loaded.Settings.GameBlacklistExes ??= [];
                    NormalizeSettings(loaded.Settings);
                    foreach (var app in loaded.PriorityApps)
                    {
                        app.Hotkey = HotkeyHelper.Normalize(app.Hotkey);
                    }
                    return loaded;
                }
            }
        }
        catch
        {
            // Fall back cleanly to fresh state if corrupted
        }

        var fresh = new AppState();
        SaveInternal(fresh);
        return fresh;
    }

    private static void NormalizeSettings(AppSettings s)
    {
        s.Language = Strings.NormalizeCode(s.Language);
        s.Theme = UI.Theme.NormalizeId(s.Theme);
        s.VolumeStepPercent = Math.Clamp(s.VolumeStepPercent, 1, 20);
        s.SeekSeconds = Math.Clamp(s.SeekSeconds, 1, 120);
        s.LongPressThresholdMs = Math.Clamp(s.LongPressThresholdMs, 150, 1500);
        s.HotkeyToggleWindow = HotkeyHelper.Normalize(s.HotkeyToggleWindow);
        s.HotkeyManualLock = HotkeyHelper.Normalize(s.HotkeyManualLock);
        s.HotkeySoloMode = HotkeyHelper.Normalize(s.HotkeySoloMode);
        s.HotkeyPlayPause = HotkeyHelper.Normalize(s.HotkeyPlayPause, allowAnySingleKey: true);
        if (string.IsNullOrWhiteSpace(s.MpvPipeName))
            s.MpvPipeName = "mpvsocket";
    }

    private void SaveInternal(AppState state)
    {
        try
        {
            string json = JsonSerializer.Serialize(state, AppJsonContext.Default.AppState);
            string tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch
        {
            // Ignore transient IO failures
        }
    }

    private void RebuildCachesLocked()
    {
        var s = _state.Settings;
        _cachedSettingsSnapshot = new AppSettings
        {
            Language = s.Language,
            Theme = s.Theme,
            StartMinimized = s.StartMinimized,
            MinimizeToTrayOnClose = s.MinimizeToTrayOnClose,
            AlwaysOnTop = s.AlwaysOnTop,
            AutoCheckUpdates = s.AutoCheckUpdates,
            MediaHookEnabled = s.MediaHookEnabled,
            TrayWheelVolumeEnabled = s.TrayWheelVolumeEnabled,
            VolumeStepPercent = s.VolumeStepPercent,
            OsdEnabled = s.OsdEnabled,
            AutoPauseEnabled = s.AutoPauseEnabled,
            HeadphoneGuardEnabled = s.HeadphoneGuardEnabled,
            SoloModeEnabled = s.SoloModeEnabled,
            GameFullscreenGuardEnabled = s.GameFullscreenGuardEnabled,
            TimelineScrubbingEnabled = s.TimelineScrubbingEnabled,
            SeekSeconds = s.SeekSeconds,
            LongPressThresholdMs = s.LongPressThresholdMs,
            HotkeyToggleWindow = s.HotkeyToggleWindow,
            HotkeyManualLock = s.HotkeyManualLock,
            HotkeySoloMode = s.HotkeySoloMode,
            HotkeyPlayPause = s.HotkeyPlayPause,
            MpvPipeName = s.MpvPipeName,
            GameBlacklistExes = [.. s.GameBlacklistExes]
        };

        long nowTick = Environment.TickCount64;
        var prio = new Dictionary<string, int>(_state.PriorityApps.Count, StringComparer.OrdinalIgnoreCase);
        var disc = new Dictionary<string, (string DisplayName, string ExePath, long LastSeenTick)>(_state.PriorityApps.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _state.PriorityApps.Count; i++)
        {
            var app = _state.PriorityApps[i];
            if (!prio.ContainsKey(app.AppKey))
                prio[app.AppKey] = i;
            disc[app.AppKey] = (app.DisplayName, app.ExePath, nowTick);
        }
        _priorityIndexCache = prio;
        _discoveredFastCache = disc;
    }

    public AppSettings GetSettingsSnapshot() => _cachedSettingsSnapshot;

    public List<DiscoveredApp> GetPriorityAppsSnapshot()
    {
        lock (_lock)
        {
            var list = new List<DiscoveredApp>(_state.PriorityApps.Count);
            foreach (var app in _state.PriorityApps)
            {
                list.Add(new DiscoveredApp
                {
                    AppKey = app.AppKey,
                    DisplayName = app.DisplayName,
                    ExePath = app.ExePath,
                    Hotkey = app.Hotkey,
                    LastSeenUtc = app.LastSeenUtc
                });
            }
            return list;
        }
    }

    public void UpdateSettings(Action<AppSettings> mutator)
    {
        lock (_lock)
        {
            mutator(_state.Settings);
            NormalizeSettings(_state.Settings);
            Strings.SetLanguage(_state.Settings.Language);
            UI.Theme.SetTheme(_state.Settings.Theme);
            RebuildCachesLocked();
            SaveInternal(_state);
        }
        StateChanged?.Invoke();
    }

    public void ResetSettingsToDefaults()
    {
        lock (_lock)
        {
            string currentLang = _state.Settings.Language;
            string currentTheme = _state.Settings.Theme;
            _state.Settings = new AppSettings
            {
                Language = Strings.NormalizeCode(currentLang),
                Theme = UI.Theme.NormalizeId(currentTheme)
            };
            ClearDuplicateHotkeyLocked(_state.Settings.HotkeyToggleWindow, exceptGlobalSlot: GlobalHotkeySlot.ToggleWindow);
            ClearDuplicateHotkeyLocked(_state.Settings.HotkeyManualLock, exceptGlobalSlot: GlobalHotkeySlot.ManualLock);
            ClearDuplicateHotkeyLocked(_state.Settings.HotkeySoloMode, exceptGlobalSlot: GlobalHotkeySlot.SoloMode);
            UI.Theme.SetTheme(_state.Settings.Theme);
            RebuildCachesLocked();
            SaveInternal(_state);
        }
        StateChanged?.Invoke();
    }

    public bool RecordDiscoveredApp(string appKey, string displayName, string exePath)
    {
        if (string.IsNullOrWhiteSpace(appKey))
            return false;

        if (_discoveredFastCache.TryGetValue(appKey, out var cached) &&
            (string.IsNullOrWhiteSpace(exePath) || string.Equals(cached.ExePath, exePath, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(displayName) || string.Equals(cached.DisplayName, displayName, StringComparison.Ordinal)) &&
            Environment.TickCount64 - cached.LastSeenTick < 60_000)
        {
            return false;
        }

        bool changed = false;
        lock (_lock)
        {
            var existing = _state.PriorityApps.Find(a =>
                string.Equals(a.AppKey, appKey, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                if (!string.IsNullOrWhiteSpace(exePath) &&
                    !string.Equals(existing.ExePath, exePath, StringComparison.OrdinalIgnoreCase))
                {
                    existing.ExePath = exePath;
                    changed = true;
                }
                if (!string.IsNullOrWhiteSpace(displayName) &&
                    !string.Equals(existing.DisplayName, displayName, StringComparison.Ordinal))
                {
                    existing.DisplayName = displayName;
                    changed = true;
                }
                existing.LastSeenUtc = DateTimeOffset.UtcNow;
                RebuildCachesLocked();
                if (changed)
                    SaveInternal(_state);
            }
            else
            {
                _state.PriorityApps.Add(new DiscoveredApp
                {
                    AppKey = appKey,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? appKey : displayName,
                    ExePath = exePath,
                    LastSeenUtc = DateTimeOffset.UtcNow
                });
                RebuildCachesLocked();
                SaveInternal(_state);
                changed = true;
            }
        }

        if (changed)
            StateChanged?.Invoke();
        return changed;
    }

    public int GetPriorityIndex(string appKey)
        => _priorityIndexCache.TryGetValue(appKey, out int idx) ? idx : int.MaxValue;

    public bool MovePriorityApp(string appKey, int delta)
    {
        bool moved = false;
        lock (_lock)
        {
            int idx = _state.PriorityApps.FindIndex(a =>
                string.Equals(a.AppKey, appKey, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                int target = idx + delta;
                if (target >= 0 && target < _state.PriorityApps.Count && target != idx)
                {
                    var item = _state.PriorityApps[idx];
                    _state.PriorityApps.RemoveAt(idx);
                    _state.PriorityApps.Insert(target, item);
                    RebuildCachesLocked();
                    SaveInternal(_state);
                    moved = true;
                }
            }
        }
        if (moved)
            StateChanged?.Invoke();
        return moved;
    }

    public bool MovePriorityAppToTop(string appKey)
    {
        bool moved = false;
        lock (_lock)
        {
            int idx = _state.PriorityApps.FindIndex(a =>
                string.Equals(a.AppKey, appKey, StringComparison.OrdinalIgnoreCase));
            if (idx > 0)
            {
                var item = _state.PriorityApps[idx];
                _state.PriorityApps.RemoveAt(idx);
                _state.PriorityApps.Insert(0, item);
                RebuildCachesLocked();
                SaveInternal(_state);
                moved = true;
            }
        }
        if (moved)
            StateChanged?.Invoke();
        return moved;
    }

    public bool SetGlobalHotkey(GlobalHotkeySlot slot, string hotkey)
    {
        string normalized = HotkeyHelper.Normalize(hotkey, allowAnySingleKey: slot == GlobalHotkeySlot.PlayPause);
        lock (_lock)
        {
            ClearDuplicateHotkeyLocked(normalized, exceptGlobalSlot: slot);
            switch (slot)
            {
                case GlobalHotkeySlot.ToggleWindow:
                    _state.Settings.HotkeyToggleWindow = normalized;
                    break;
                case GlobalHotkeySlot.ManualLock:
                    _state.Settings.HotkeyManualLock = normalized;
                    break;
                case GlobalHotkeySlot.SoloMode:
                    _state.Settings.HotkeySoloMode = normalized;
                    break;
                case GlobalHotkeySlot.PlayPause:
                    _state.Settings.HotkeyPlayPause = normalized;
                    break;
            }
            RebuildCachesLocked();
            SaveInternal(_state);
        }
        StateChanged?.Invoke();
        return true;
    }

    public bool SetPriorityAppHotkey(string appKey, string hotkey)
    {
        if (string.IsNullOrWhiteSpace(appKey))
            return false;

        string normalized = HotkeyHelper.Normalize(hotkey);
        bool updated = false;
        lock (_lock)
        {
            var app = _state.PriorityApps.Find(a =>
                string.Equals(a.AppKey, appKey, StringComparison.OrdinalIgnoreCase));
            if (app != null)
            {
                ClearDuplicateHotkeyLocked(normalized, exceptAppKey: appKey);
                app.Hotkey = normalized;
                RebuildCachesLocked();
                SaveInternal(_state);
                updated = true;
            }
        }
        if (updated)
            StateChanged?.Invoke();
        return updated;
    }

    public (string DisplayName, string ExePath)? GetPriorityAppInfo(string appKey)
    {
        lock (_lock)
        {
            var app = _state.PriorityApps.Find(a =>
                string.Equals(a.AppKey, appKey, StringComparison.OrdinalIgnoreCase));
            return app != null ? (app.DisplayName, app.ExePath) : null;
        }
    }

    private void ClearDuplicateHotkeyLocked(
        string normalizedHotkey,
        GlobalHotkeySlot? exceptGlobalSlot = null,
        string? exceptAppKey = null)
    {
        if (string.IsNullOrEmpty(normalizedHotkey))
            return;

        if (exceptGlobalSlot != GlobalHotkeySlot.ToggleWindow &&
            string.Equals(_state.Settings.HotkeyToggleWindow, normalizedHotkey, StringComparison.OrdinalIgnoreCase))
            _state.Settings.HotkeyToggleWindow = string.Empty;

        if (exceptGlobalSlot != GlobalHotkeySlot.ManualLock &&
            string.Equals(_state.Settings.HotkeyManualLock, normalizedHotkey, StringComparison.OrdinalIgnoreCase))
            _state.Settings.HotkeyManualLock = string.Empty;

        if (exceptGlobalSlot != GlobalHotkeySlot.SoloMode &&
            string.Equals(_state.Settings.HotkeySoloMode, normalizedHotkey, StringComparison.OrdinalIgnoreCase))
            _state.Settings.HotkeySoloMode = string.Empty;

        if (exceptGlobalSlot != GlobalHotkeySlot.PlayPause &&
            string.Equals(_state.Settings.HotkeyPlayPause, normalizedHotkey, StringComparison.OrdinalIgnoreCase))
            _state.Settings.HotkeyPlayPause = string.Empty;

        foreach (var other in _state.PriorityApps)
        {
            if (exceptAppKey != null &&
                string.Equals(other.AppKey, exceptAppKey, StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(other.Hotkey, normalizedHotkey, StringComparison.OrdinalIgnoreCase))
                other.Hotkey = string.Empty;
        }
    }

    public bool RemovePriorityApp(string appKey)
    {
        bool removed = false;
        lock (_lock)
        {
            int count = _state.PriorityApps.RemoveAll(a =>
                string.Equals(a.AppKey, appKey, StringComparison.OrdinalIgnoreCase));
            if (count > 0)
            {
                RebuildCachesLocked();
                SaveInternal(_state);
                removed = true;
            }
        }
        if (removed)
            StateChanged?.Invoke();
        return removed;
    }

    public void ClearPriorityHistory()
    {
        lock (_lock)
        {
            if (_state.PriorityApps.Count == 0)
                return;
            _state.PriorityApps.Clear();
            RebuildCachesLocked();
            SaveInternal(_state);
        }
        StateChanged?.Invoke();
    }
}
