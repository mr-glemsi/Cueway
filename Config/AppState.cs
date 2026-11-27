namespace Cueway.Config;

public sealed class AppSettings
{
    public string Language { get; set; } = "tr";
    public string Theme { get; set; } = "dark";
    public bool StartMinimized { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = false;
    public bool AutoCheckUpdates { get; set; } = true;
    public string LastSeenVersion { get; set; } = string.Empty;
    public bool MediaHookEnabled { get; set; } = true;
    public bool TrayWheelVolumeEnabled { get; set; } = false;
    public int VolumeStepPercent { get; set; } = 5;
    public bool OsdEnabled { get; set; } = false;
    public bool AutoPauseEnabled { get; set; } = false;
    public bool AutoDuckingEnabled { get; set; } = false;
    public bool MicDuckingEnabled { get; set; } = false;
    public int DuckVolumePercent { get; set; } = 25;
    public bool RememberSiteVolumeEnabled { get; set; } = true;
    public bool HeadphoneGuardEnabled { get; set; } = false;
    public bool SoloModeEnabled { get; set; } = false;
    public bool GameFullscreenGuardEnabled { get; set; } = false;
    public bool TimelineScrubbingEnabled { get; set; } = true;
    public int SeekSeconds { get; set; } = 10;
    public int LongPressThresholdMs { get; set; } = 400;
    public string HotkeyToggleWindow { get; set; } = string.Empty;
    public string HotkeyManualLock { get; set; } = string.Empty;
    public string HotkeySoloMode { get; set; } = string.Empty;
    public string HotkeyPlayPause { get; set; } = string.Empty;
    public string HotkeyPromotePlaying { get; set; } = string.Empty;
    public string HotkeyVolumeUp { get; set; } = string.Empty;
    public string HotkeyVolumeDown { get; set; } = string.Empty;
    public string HotkeyVolumeMute { get; set; } = string.Empty;
    public string HotkeyCycleAudioDevice { get; set; } = string.Empty;
    public string MpvPipeName { get; set; } = "mpvsocket";
    public List<string> GameBlacklistExes { get; set; } = [];
    public List<IgnoredAppEntry> IgnoredApps { get; set; } = [];
    public List<SavedSiteVolumeEntry> SavedSiteVolumes { get; set; } = [];
    public List<CustomThemePreset> CustomThemes { get; set; } = [];
}

public sealed class SavedSiteVolumeEntry
{
    public string AppKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int VolumePercent { get; set; } = 100;
}

public sealed class IgnoredAppEntry
{
    public string AppKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
}

public sealed class CustomThemePreset
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BgHex { get; set; } = "#13161F";
    public string CardHex { get; set; } = "#1C212E";
    public string AccentHex { get; set; } = "#6366F1";
}

public sealed class DiscoveredApp
{
    public string AppKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public string Hotkey { get; set; } = string.Empty;
    public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AppState
{
    public AppSettings Settings { get; set; } = new();
    // Intentionally empty by default: only applications actually played by the user are recorded.
    public List<DiscoveredApp> PriorityApps { get; set; } = [];
}
