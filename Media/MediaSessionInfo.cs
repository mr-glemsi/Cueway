using Cueway.Localization;

namespace Cueway.Media;

internal enum SessionSource
{
    Smtc,
    MpvIpc,
    BrowserBridge,
    AudioSession
}

internal sealed class MediaSessionInfo
{
    public required string SessionId { get; init; }
    public required string AppKey { get; init; }
    public string? SiteKey { get; init; }
    public required string DisplayName { get; init; }
    public required string ExePath { get; init; }
    public required int[] ProcessIds { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public required bool IsPlaying { get; init; }
    public required bool IsEffectivelyPlaying { get; init; }
    public required bool IsBrowser { get; init; }
    public required bool IsVideoLikely { get; init; }
    public required bool IsMuted { get; init; }
    public required float Volume { get; init; }
    public required SessionSource Source { get; init; }
    public bool IsPassiveTab { get; init; }

    public string FormattedMediaTitle
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Title))
                return string.IsNullOrWhiteSpace(Artist) ? Strings.MediaPlayingFallback : Artist;
            if (string.IsNullOrWhiteSpace(Artist))
                return Title;
            if (Title.Contains(Artist, StringComparison.OrdinalIgnoreCase))
                return Title;
            return $"{Artist} — {Title}";
        }
    }
}
