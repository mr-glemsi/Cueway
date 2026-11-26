using System.Text.Json;
using Cueway.Input;
using Cueway.Localization;

namespace Cueway.Config;

internal enum GlobalHotkeySlot
{
    ToggleWindow = 0,
    ManualLock = 1,
    SoloMode = 2,
    PlayPause = 3,
    PromotePlaying = 4
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
        UI.Theme.SetCustomThemes(_state.Settings.CustomThemes);
        UI.Theme.SetTheme(_state.Settings.Theme);
    }

    private AppState LoadInternal()
    {
        bool fileExists = File.Exists(_filePath);
        if (fileExists)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var sr = new StreamReader(fs);
                    string json = sr.ReadToEnd();
                    var loaded = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppState);
                    if (loaded != null)
                    {
                        loaded.Settings ??= new AppSettings();
                        loaded.PriorityApps ??= [];
                        loaded.Settings.GameBlacklistExes ??= [];
                        loaded.Settings.CustomThemes ??= [];
                        NormalizeSettings(loaded.Settings);
                        foreach (var app in loaded.PriorityApps)
                        {
                            app.Hotkey = HotkeyHelper.Normalize(app.Hotkey);
                        }
                        return loaded;
                    }
                    break;
                }
                catch (IOException) when (attempt < 3)
                {
                    Thread.Sleep(50);
                }
                catch (IOException)
                {
                    // File is temporarily locked (e.g. during instance swap); do NOT overwrite user settings on disk
                    var memFallback = new AppState();
                    NormalizeSettings(memFallback.Settings);
                    return memFallback;
                }
                catch
                {
                    break;
                }
            }
        }

        var fresh = new AppState();
        NormalizeSettings(fresh.Settings);
        SaveInternal(fresh);
        return fresh;
    }

    private static void NormalizeSettings(AppSettings s)
    {
        s.Language = Strings.NormalizeCode(s.Language);
        s.CustomThemes ??= [];
        SanitizeCustomThemes(s.CustomThemes);
        UI.Theme.SetCustomThemes(s.CustomThemes);
        s.Theme = UI.Theme.NormalizeId(s.Theme);
        s.VolumeStepPercent = Math.Clamp(s.VolumeStepPercent, 1, 20);
        s.SeekSeconds = Math.Clamp(s.SeekSeconds, 1, 120);
        s.LongPressThresholdMs = Math.Clamp(s.LongPressThresholdMs, 150, 1500);
        s.HotkeyToggleWindow = HotkeyHelper.Normalize(s.HotkeyToggleWindow);
        s.HotkeyManualLock = HotkeyHelper.Normalize(s.HotkeyManualLock);
        s.HotkeySoloMode = HotkeyHelper.Normalize(s.HotkeySoloMode);
        s.HotkeyPlayPause = HotkeyHelper.Normalize(s.HotkeyPlayPause, allowAnySingleKey: true);
        s.HotkeyPromotePlaying = HotkeyHelper.Normalize(s.HotkeyPromotePlaying, allowAnySingleKey: true);
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
            HotkeyPromotePlaying = s.HotkeyPromotePlaying,
            MpvPipeName = s.MpvPipeName,
            GameBlacklistExes = [.. s.GameBlacklistExes],
            CustomThemes = [.. s.CustomThemes.Select(CloneCustomTheme)]
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
            UI.Theme.SetCustomThemes(_state.Settings.CustomThemes);
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
            var customThemes = _state.Settings.CustomThemes.Select(CloneCustomTheme).ToList();
            _state.Settings = new AppSettings
            {
                Language = Strings.NormalizeCode(currentLang),
                CustomThemes = customThemes,
                Theme = currentTheme
            };
            NormalizeSettings(_state.Settings);
            ClearDuplicateHotkeyLocked(_state.Settings.HotkeyToggleWindow, exceptGlobalSlot: GlobalHotkeySlot.ToggleWindow);
            ClearDuplicateHotkeyLocked(_state.Settings.HotkeyManualLock, exceptGlobalSlot: GlobalHotkeySlot.ManualLock);
            ClearDuplicateHotkeyLocked(_state.Settings.HotkeySoloMode, exceptGlobalSlot: GlobalHotkeySlot.SoloMode);
            ClearDuplicateHotkeyLocked(_state.Settings.HotkeyPromotePlaying, exceptGlobalSlot: GlobalHotkeySlot.PromotePlaying);
            UI.Theme.SetCustomThemes(_state.Settings.CustomThemes);
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
        string normalized = HotkeyHelper.Normalize(
            hotkey,
            allowAnySingleKey: slot is GlobalHotkeySlot.PlayPause or GlobalHotkeySlot.PromotePlaying);
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
                case GlobalHotkeySlot.PromotePlaying:
                    _state.Settings.HotkeyPromotePlaying = normalized;
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

        if (exceptGlobalSlot != GlobalHotkeySlot.PromotePlaying &&
            string.Equals(_state.Settings.HotkeyPromotePlaying, normalizedHotkey, StringComparison.OrdinalIgnoreCase))
            _state.Settings.HotkeyPromotePlaying = string.Empty;

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

    public CustomThemePreset SaveCustomTheme(string? existingId, string name, uint bgRgb, uint cardRgb, uint accentRgb, bool activate = true)
    {
        string cleanName = string.IsNullOrWhiteSpace(name) ? Strings.ThemeDefaultCustomName : name.Trim();
        if (cleanName.Length > 28)
            cleanName = cleanName[..28].Trim();
        if (string.IsNullOrWhiteSpace(cleanName))
            cleanName = Strings.ThemeDefaultCustomName;

        string bgHex = UI.Theme.FormatHex(bgRgb);
        string cardHex = UI.Theme.FormatHex(cardRgb);
        string accentHex = UI.Theme.FormatHex(accentRgb);

        CustomThemePreset saved;
        lock (_lock)
        {
            _state.Settings.CustomThemes ??= [];
            CustomThemePreset? target = null;
            if (!string.IsNullOrWhiteSpace(existingId))
            {
                target = _state.Settings.CustomThemes.Find(c =>
                    string.Equals(c.Id, existingId, StringComparison.OrdinalIgnoreCase));
            }

            if (target != null)
            {
                target.Name = cleanName;
                target.BgHex = bgHex;
                target.CardHex = cardHex;
                target.AccentHex = accentHex;
                saved = CloneCustomTheme(target);
            }
            else
            {
                string id = $"custom:{Guid.NewGuid().ToString("N")[..8]}";
                var entry = new CustomThemePreset
                {
                    Id = id,
                    Name = cleanName,
                    BgHex = bgHex,
                    CardHex = cardHex,
                    AccentHex = accentHex
                };
                if (_state.Settings.CustomThemes.Count >= 20)
                    _state.Settings.CustomThemes.RemoveAt(0);
                _state.Settings.CustomThemes.Add(entry);
                saved = CloneCustomTheme(entry);
            }

            if (activate)
                _state.Settings.Theme = saved.Id;

            NormalizeSettings(_state.Settings);
            UI.Theme.SetCustomThemes(_state.Settings.CustomThemes);
            UI.Theme.SetTheme(_state.Settings.Theme);
            RebuildCachesLocked();
            SaveInternal(_state);
        }

        StateChanged?.Invoke();
        return saved;
    }

    public bool DeleteCustomTheme(string themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId))
            return false;

        bool deleted = false;
        lock (_lock)
        {
            _state.Settings.CustomThemes ??= [];
            int count = _state.Settings.CustomThemes.RemoveAll(c =>
                string.Equals(c.Id, themeId, StringComparison.OrdinalIgnoreCase));
            if (count > 0)
            {
                deleted = true;
                if (string.Equals(_state.Settings.Theme, themeId, StringComparison.OrdinalIgnoreCase))
                    _state.Settings.Theme = "dark";

                NormalizeSettings(_state.Settings);
                UI.Theme.SetCustomThemes(_state.Settings.CustomThemes);
                UI.Theme.SetTheme(_state.Settings.Theme);
                RebuildCachesLocked();
                SaveInternal(_state);
            }
        }

        if (deleted)
            StateChanged?.Invoke();
        return deleted;
    }

    private static CustomThemePreset CloneCustomTheme(CustomThemePreset src) => new()
    {
        Id = src.Id,
        Name = src.Name,
        BgHex = src.BgHex,
        CardHex = src.CardHex,
        AccentHex = src.AccentHex
    };

    private static void SanitizeCustomThemes(List<CustomThemePreset> list)
    {
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var item = list[i];
            if (item == null || string.IsNullOrWhiteSpace(item.Id))
            {
                list.RemoveAt(i);
                continue;
            }
            string id = item.Id.Trim();
            if (!id.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
                id = "custom:" + id;
            if (!seenIds.Add(id))
            {
                list.RemoveAt(i);
                continue;
            }
            item.Id = id;
            string name = string.IsNullOrWhiteSpace(item.Name) ? "Custom" : item.Name.Trim();
            item.Name = name.Length > 28 ? name[..28].Trim() : name;
            uint bg = UI.Theme.TryParseHex(item.BgHex, out uint b) ? b : 0x13161Fu;
            uint card = UI.Theme.TryParseHex(item.CardHex, out uint c) ? c : UI.Theme.SuggestCardColor(bg);
            uint accent = UI.Theme.TryParseHex(item.AccentHex, out uint a) ? a : 0x6366F1u;
            item.BgHex = UI.Theme.FormatHex(bg);
            item.CardHex = UI.Theme.FormatHex(card);
            item.AccentHex = UI.Theme.FormatHex(accent);
        }
        if (list.Count > 20)
            list.RemoveRange(0, list.Count - 20);
    }
}
