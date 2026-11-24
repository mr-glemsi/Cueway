using System.Text.Json;
using Cueway.Localization;

namespace Cueway.Config;

internal sealed class StateStore
{
    private readonly object _lock = new();
    private readonly string _filePath;
    private AppState _state;

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

    public AppSettings GetSettingsSnapshot()
    {
        lock (_lock)
        {
            var s = _state.Settings;
            return new AppSettings
            {
                Language = s.Language,
                Theme = s.Theme,
                StartMinimized = s.StartMinimized,
                MinimizeToTrayOnClose = s.MinimizeToTrayOnClose,
                AlwaysOnTop = s.AlwaysOnTop,
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
                MpvPipeName = s.MpvPipeName,
                GameBlacklistExes = [.. s.GameBlacklistExes]
            };
        }
    }

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
            UI.Theme.SetTheme(_state.Settings.Theme);
            SaveInternal(_state);
        }
        StateChanged?.Invoke();
    }

    public bool RecordDiscoveredApp(string appKey, string displayName, string exePath)
    {
        if (string.IsNullOrWhiteSpace(appKey))
            return false;

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
                SaveInternal(_state);
                changed = true;
            }
        }

        if (changed)
            StateChanged?.Invoke();
        return changed;
    }

    public int GetPriorityIndex(string appKey)
    {
        lock (_lock)
        {
            for (int i = 0; i < _state.PriorityApps.Count; i++)
            {
                if (string.Equals(_state.PriorityApps[i].AppKey, appKey, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return int.MaxValue;
        }
    }

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
                    SaveInternal(_state);
                    moved = true;
                }
            }
        }
        if (moved)
            StateChanged?.Invoke();
        return moved;
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
            SaveInternal(_state);
        }
        StateChanged?.Invoke();
    }
}
