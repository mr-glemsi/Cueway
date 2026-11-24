namespace Cueway.Media;

internal sealed class BridgeIncomingMessage
{
    public string Type { get; set; } = string.Empty;
    public string Browser { get; set; } = string.Empty;
    public int Version { get; set; }
    public List<BridgeTabDto>? Tabs { get; set; }
}

internal sealed class BridgeTabDto
{
    public int TabId { get; set; }
    public int WindowId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public bool IsPlaying { get; set; }
    public bool IsPaused { get; set; }
    public bool IsMuted { get; set; }
    public double Volume { get; set; } = 1.0;
    public bool HasVideo { get; set; }
    public bool HasAudio { get; set; } = true;
    public double Duration { get; set; }
    public double CurrentTime { get; set; }
}

internal sealed class BridgeCommandMessage
{
    public string Type { get; set; } = "cmd";
    public int TabId { get; set; }
    public string Action { get; set; } = string.Empty;
    public int Delta { get; set; }
}
