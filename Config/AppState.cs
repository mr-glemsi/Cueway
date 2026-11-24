namespace Cueway.Config;

public sealed class AppSettings
{
    public string Language { get; set; } = "tr";
    public string Theme { get; set; } = "dark";
    public bool StartMinimized { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = false;
    public bool MediaHookEnabled { get; set; } = true;
    public bool TrayWheelVolumeEnabled { get; set; } = true;
    public int VolumeStepPercent { get; set; } = 5;
    public bool OsdEnabled { get; set; } = false;
    public bool AutoPauseEnabled { get; set; } = true;
    public bool HeadphoneGuardEnabled { get; set; } = true;
    public bool SoloModeEnabled { get; set; } = false;
    public bool GameFullscreenGuardEnabled { get; set; } = true;
    public bool TimelineScrubbingEnabled { get; set; } = true;
    public int SeekSeconds { get; set; } = 10;
    public int LongPressThresholdMs { get; set; } = 400;
    public string MpvPipeName { get; set; } = "mpvsocket";
    public List<string> GameBlacklistExes { get; set; } = [];
}

public sealed class DiscoveredApp
{
    public string AppKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AppState
{
    public AppSettings Settings { get; set; } = new();
    // Intentionally empty by default: only applications actually played by the user are recorded.
    public List<DiscoveredApp> PriorityApps { get; set; } = [];
}
