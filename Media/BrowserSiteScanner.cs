using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cueway.Interop;

namespace Cueway.Media;

internal static unsafe partial class BrowserSiteScanner
{
    public readonly record struct OpenTabInfo(string SiteKey, string DisplayName, string Title, string Url, bool SoundPlaying);
    public readonly record struct ResolvedSiteInfo(string SiteKey, string DisplayName);
    private readonly record struct BrowserWindowInfo(nint Hwnd, string Title);

    private static readonly Dictionary<string, (long Tick, List<OpenTabInfo> Tabs)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (string Path, long LastWriteTicks, long Length, List<OpenTabInfo> Tabs)> GeckoFileCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (string Signature, List<OpenTabInfo> Tabs)> ChromiumFileCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    private static readonly Dictionary<string, ResolvedSiteInfo> StickySiteCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object StickyLock = new();

    private static readonly Dictionary<string, ResolvedSiteInfo> PwaAumidCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object PwaCacheLock = new();

    private static readonly Dictionary<string, ResolvedSiteInfo> KnownChromiumPwaIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cinhimbnkkaeohfgghhklpknlkffjgod"] = new("youtubemusic", "YouTube Music"),
        ["agimnkijcaahngcdmfeangaknmldooml"] = new("youtube", "YouTube"),
        ["pjibgclleladliembfgfagdaldikeohf"] = new("spotifyweb", "Spotify Web"),
        ["kippjfofjhjlffjecoapiogbkgbpmgej"] = new("x", "X"),
    };

    private static readonly object EnumLock = new();
    private static HashSet<int>? _enumTargetPids;
    private static List<BrowserWindowInfo>? _enumFoundWindows;

    private static readonly HashSet<string> GeckoBrowsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "zen", "firefox", "waterfox", "floorp", "librewolf"
    };

    private static readonly HashSet<string> MusicSiteKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtubemusic",
        "spotifyweb",
        "soundcloud"
    };

    [GeneratedRegex(@"(\bmusic\.youtube\.com\b|[-–—|·]\s*youtube\s+music\b|^youtube\s+music\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex YouTubeMusicRx();

    [GeneratedRegex(@"(\byoutube\.com\b|\byoutu\.be\b|[-–—|·]\s*youtube\b|^youtube\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex YouTubeRx();

    [GeneratedRegex(@"(\b([a-z0-9-]+\.)?(x|twitter)\.com\b|\b([a-z0-9-]+\.)?twimg\.com\b|\s/\s*x(\s+(ve|and|und|et|y|и)\s+\d+|\s*[-–—|·,:;()]|$)|\bon\s+x\s*:|\bx['’]te\b|\bx\s+üzerinde\s*:|\ben\s+x\s*:|\bauf\s+x\s*:|\bsur\s+x\s*:|\bв\s+x\s*:|x\s*上的|^x\s*[-–—|·])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex XRx();

    [GeneratedRegex(@"^(\(\d+\+?\)\s*)?x$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StandaloneXRx();

    [GeneratedRegex(@"(\btwitch\.tv\b|[-–—|·]\s*twitch\b|^twitch\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TwitchRx();

    [GeneratedRegex(@"(\bkick\.com\b|[-–—|·]\s*kick\b|^kick\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KickRx();

    [GeneratedRegex(@"(\bopen\.spotify\.com\b|\bspotify\s*[-–·—]|^spotify\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpotifyRx();

    [GeneratedRegex(@"(\bsoundcloud\.com\b|[-–—|·]\s*soundcloud\b|\bsoundcloud\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SoundCloudRx();

    [GeneratedRegex(@"(\bnetflix\.com\b|[-–—|·]\s*netflix\b|\bnetflix\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NetflixRx();

    [GeneratedRegex(@"(\binstagram\.com\b|[-–—|·•]\s*instagram\b|^instagram\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstagramRx();

    [GeneratedRegex(@"(\btiktok\.com\b|[-–—|·]\s*tiktok\b|^tiktok\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TikTokRx();

    [GeneratedRegex(@"(\breddit\.com\b|\bv\.redd\.it\b|[-–—|·]\s*reddit\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RedditRx();

    [GeneratedRegex(@"(\bfacebook\.com\b|\bfb\.watch\b|[-–—|·]\s*facebook\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FacebookRx();

    [GeneratedRegex(@"(\bdisneyplus\.com\b|disney\s*\+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DisneyRx();

    [GeneratedRegex(@"(\bprimevideo\.com\b|prime\s*video)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PrimeRx();

    [GeneratedRegex(@"(\bvimeo\.com\b|\bvimeo\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VimeoRx();

    [GeneratedRegex(@"(\bdailymotion\.com\b|\bdailymotion\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DailymotionRx();

    [GeneratedRegex(@"(\bbilibili\.com\b|\bbilibili\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BilibiliRx();

    [GeneratedRegex(@"\s*[-–—|]\s*(Microsoft\s*Edge|Google\s*Chrome|Brave|Zen\s*Browser|Zen|Mozilla\s*Firefox|Firefox|Opera(\s*GX)?|Vivaldi|Arc|Floorp|LibreWolf|Waterfox)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BrowserSuffixRx();

    [GeneratedRegex(@"\s*[-–—|]\s*(Kişisel|Personal|Work|İş|Profil\s*\d*|Profile\s*\d*)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EdgeProfileSuffixRx();

    [GeneratedRegex(@"\s+(ve\s+\d+\s+sayfa\s+daha|ve\s+diğer\s+\d+\s+sayfa|ve\s+\d+\s+diğer\s+sayfa|and\s+\d+\s+more\s+pages?|und\s+\d+\s+weitere\s+Seiten?|et\s+\d+\s+pages?\s+suppl[ée]mentaires?|y\s+\d+\s+p[áa]ginas?\s+m[áa]s|и\s+ещё\s+\d+\s+страниц\S*)(?:\s*[-–—|].*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EdgeMoreTabsTailRx();

    [GeneratedRegex(@"(?:\s*[-–—|,]\s*|\s*\()(Ses\s+çalıyor|Ses\s+çalınıyor|Ses\s+yürütülüyor|Ses\s+yürütüyor|Ses\s+oynatılıyor|Ses\s+oynatıyor|Sesi\s+kapatıldı|Sesi\s+kısıldı|Sessize\s+alındı|Uyku\s+modunda|Uyku\s+modundaki\s+sekme|Sabitlendi|Playing\s+audio|Audio\s+playing|Audio\s+is\s+playing|Tab\s+is\s+playing\s+audio|Audio\s+wird\s+wiedergegeben|Contenu\s+audio\s+en\s+cours\s+de\s+lecture|Reproduciendo\s+audio|Воспроизводится\s+звук|正在播放音频|正在播放聲音|Sleeping\s+tab|Pinned|Muted)\)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TabAudioStateRx();

    private static readonly HashSet<string> BuiltInKnownSites = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube", "youtubemusic", "x", "twitch", "kick",
        "spotifyweb", "soundcloud", "netflix", "instagram", "tiktok",
        "reddit", "facebook", "disneyplus", "primevideo", "vimeo",
        "dailymotion", "bilibili"
    };

    private static readonly HashSet<string> SecondLevelPublicSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "com.tr", "org.tr", "net.tr", "co.uk", "org.uk",
        "com.br", "co.jp", "com.au", "co.kr", "com.cn"
    };

    private static readonly HashSet<string> GenericSiteTitleWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "home", "index", "video", "player", "watch", "embed", "live", "anasayfa", "ana sayfa"
    };

    public static bool IsBuiltInKnownSite(string? siteKey)
        => !string.IsNullOrEmpty(siteKey) && BuiltInKnownSites.Contains(siteKey);

    public static bool IsMusicSite(string? siteKey)
        => !string.IsNullOrEmpty(siteKey) && MusicSiteKeys.Contains(siteKey);

    public static string? ExtractSiteKeyFromAppKey(string? appKey)
    {
        if (string.IsNullOrWhiteSpace(appKey))
            return null;
        int colon = appKey.IndexOf(':');
        if (colon <= 0 || colon + 1 >= appKey.Length)
            return null;
        return appKey[(colon + 1)..];
    }

    public static string? ExtractBrowserKeyFromAppKey(string? appKey)
    {
        if (string.IsNullOrWhiteSpace(appKey))
            return null;
        int colon = appKey.IndexOf(':');
        return colon > 0 ? appKey[..colon] : null;
    }

    public static string FormatSiteDisplayName(string siteKey)
    {
        return siteKey.ToLowerInvariant() switch
        {
            "youtube" => "YouTube",
            "youtubemusic" => "YouTube Music",
            "x" => "X",
            "twitch" => "Twitch",
            "kick" => "Kick",
            "spotifyweb" => "Spotify Web",
            "soundcloud" => "SoundCloud",
            "netflix" => "Netflix",
            "instagram" => "Instagram",
            "tiktok" => "TikTok",
            "reddit" => "Reddit",
            "facebook" => "Facebook",
            "disneyplus" => "Disney+",
            "primevideo" => "Prime Video",
            "vimeo" => "Vimeo",
            "dailymotion" => "Dailymotion",
            "bilibili" => "Bilibili",
            _ => char.ToUpperInvariant(siteKey[0]) + siteKey[1..]
        };
    }

    public static string ResolveSiteDisplayName(string siteKey, string? pageSiteName, string? pageTitle, string? url)
    {
        if (IsBuiltInKnownSite(siteKey))
            return FormatSiteDisplayName(siteKey);

        if (!string.IsNullOrWhiteSpace(pageSiteName))
        {
            string clean = pageSiteName.Trim();
            if (clean.Length is >= 2 and <= 32 &&
                !clean.Contains("://", StringComparison.Ordinal) &&
                !GenericSiteTitleWords.Contains(clean))
            {
                return clean;
            }
        }

        if (!string.IsNullOrWhiteSpace(pageTitle))
        {
            var (label, joined) = ExtractHostLabels(url);
            string labelNorm = NormalizeAlphaNum(label);
            string joinedNorm = NormalizeAlphaNum(joined);

            string[] segs = pageTitle.Split(
                [" | ", " - ", " — ", " – ", " · ", " • ", " : "],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segs.Length >= 2)
            {
                string[] candidates = [segs[^1], segs[0]];
                foreach (string seg in candidates)
                {
                    if (seg.Length is < 2 or > 32 || GenericSiteTitleWords.Contains(seg))
                        continue;
                    string segNorm = NormalizeAlphaNum(seg);
                    if (string.IsNullOrEmpty(segNorm))
                        continue;

                    if (segNorm == labelNorm || segNorm == joinedNorm)
                        return seg;

                    if (labelNorm.Length >= 4 &&
                        (segNorm.StartsWith(labelNorm, StringComparison.Ordinal) ||
                         labelNorm.StartsWith(segNorm, StringComparison.Ordinal)) &&
                        Math.Abs(segNorm.Length - labelNorm.Length) <= 4)
                    {
                        return seg;
                    }
                }
            }
        }

        return FormatSiteDisplayName(siteKey);
    }

    private static string NormalizeAlphaNum(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s)
        {
            if (char.IsAsciiLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    private static (string Label, string Joined) ExtractHostLabels(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return (string.Empty, string.Empty);

        string host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        if (host.StartsWith("m.", StringComparison.Ordinal)) host = host[2..];
        if (host.StartsWith("mobile.", StringComparison.Ordinal)) host = host[7..];

        string[] parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return (string.Empty, string.Empty);

        if (parts.Length >= 3)
        {
            string tailTwo = $"{parts[^2]}.{parts[^1]}";
            if (SecondLevelPublicSuffixes.Contains(tailTwo))
                return (parts[^3], parts[^3]);
        }

        if (parts.Length >= 2)
            return (parts[^2], parts[^2] + parts[^1]);

        return (parts[0], parts[0]);
    }

    public static string NormalizeBrowserWindowTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        string s = title
            .Replace("\u200B", string.Empty, StringComparison.Ordinal)
            .Replace("\uFEFF", string.Empty, StringComparison.Ordinal)
            .Replace("\u200E", string.Empty, StringComparison.Ordinal)
            .Replace("\u200F", string.Empty, StringComparison.Ordinal)
            .Replace("\u202A", string.Empty, StringComparison.Ordinal)
            .Replace("\u202B", string.Empty, StringComparison.Ordinal)
            .Replace("\u202C", string.Empty, StringComparison.Ordinal)
            .Trim();

        for (int pass = 0; pass < 4; pass++)
        {
            string prev = s;
            s = TabAudioStateRx().Replace(s, string.Empty).Trim();
            s = BrowserSuffixRx().Replace(s, string.Empty).Trim();
            s = EdgeProfileSuffixRx().Replace(s, string.Empty).Trim();
            s = EdgeMoreTabsTailRx().Replace(s, string.Empty).Trim();
            if (string.Equals(s, prev, StringComparison.Ordinal))
                break;
        }

        return s;
    }

    public static string? DetectSite(string? text, bool allowStandaloneX = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        string? hit = DetectSiteCore(text, allowStandaloneX);
        if (!string.IsNullOrEmpty(hit))
            return hit;

        string normalized = NormalizeBrowserWindowTitle(text);
        if (!string.Equals(normalized, text, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(normalized))
            return DetectSiteCore(normalized, allowStandaloneX);

        return null;
    }

    private static string? DetectSiteCore(string text, bool allowStandaloneX)
    {
        if (YouTubeMusicRx().IsMatch(text)) return "youtubemusic";
        if (YouTubeRx().IsMatch(text)) return "youtube";
        if (XRx().IsMatch(text)) return "x";
        if (TwitchRx().IsMatch(text)) return "twitch";
        if (KickRx().IsMatch(text)) return "kick";
        if (SpotifyRx().IsMatch(text)) return "spotifyweb";
        if (SoundCloudRx().IsMatch(text)) return "soundcloud";
        if (NetflixRx().IsMatch(text)) return "netflix";
        if (InstagramRx().IsMatch(text)) return "instagram";
        if (TikTokRx().IsMatch(text)) return "tiktok";
        if (RedditRx().IsMatch(text)) return "reddit";
        if (FacebookRx().IsMatch(text)) return "facebook";
        if (DisneyRx().IsMatch(text)) return "disneyplus";
        if (PrimeRx().IsMatch(text)) return "primevideo";
        if (VimeoRx().IsMatch(text)) return "vimeo";
        if (DailymotionRx().IsMatch(text)) return "dailymotion";
        if (BilibiliRx().IsMatch(text)) return "bilibili";

        if (allowStandaloneX && StandaloneXRx().IsMatch(text.Trim()))
            return "x";

        return null;
    }

    private static string? ExtractCrxAppId(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid))
            return null;

        int idx = aumid.IndexOf("_crx_", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        int start = idx + 5;
        if (start >= aumid.Length)
            return null;

        int end = start;
        while (end < aumid.Length)
        {
            char c = aumid[end];
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                end++;
            else
                break;
        }

        int len = end - start;
        return len >= 16 ? aumid.Substring(start, len).ToLowerInvariant() : null;
    }

    private static ResolvedSiteInfo? TryResolvePwaSiteFromAumid(string browserAppKey, string? aumid)
    {
        string? crxId = ExtractCrxAppId(aumid);
        if (string.IsNullOrEmpty(crxId))
            return null;

        if (KnownChromiumPwaIds.TryGetValue(crxId, out var known))
            return known;

        string cacheKey = $"{browserAppKey.ToLowerInvariant()}|{crxId}";
        lock (PwaCacheLock)
        {
            if (PwaAumidCache.TryGetValue(cacheKey, out var cached))
                return string.IsNullOrEmpty(cached.SiteKey) ? null : cached;
        }

        ResolvedSiteInfo resolved = default;
        string userDataRoot = GetChromiumUserDataRoot(browserAppKey);
        if (!string.IsNullOrEmpty(userDataRoot) && Directory.Exists(userDataRoot))
        {
            try
            {
                string targetFolder = $"_crx_{crxId}";
                var candidateDirs = new List<string>();

                string direct = Path.Combine(userDataRoot, "Web Applications", targetFolder);
                if (Directory.Exists(direct))
                    candidateDirs.Add(direct);

                foreach (string dir in Directory.EnumerateDirectories(userDataRoot))
                {
                    string webAppDir = Path.Combine(dir, "Web Applications", targetFolder);
                    if (Directory.Exists(webAppDir))
                        candidateDirs.Add(webAppDir);
                }

                foreach (string appDir in candidateDirs)
                {
                    foreach (string icoPath in Directory.EnumerateFiles(appDir, "*.ico"))
                    {
                        string iconName = Path.GetFileNameWithoutExtension(icoPath);
                        string? site = DetectSite(iconName, allowStandaloneX: true);
                        if (!string.IsNullOrEmpty(site))
                        {
                            resolved = new ResolvedSiteInfo(site, FormatSiteDisplayName(site));
                            break;
                        }
                    }
                    if (!string.IsNullOrEmpty(resolved.SiteKey))
                        break;
                }
            }
            catch
            {
                // Ignore transient profile read errors for PWA lookup
            }
        }

        lock (PwaCacheLock)
        {
            if (PwaAumidCache.Count >= 128)
                PwaAumidCache.Clear();
            PwaAumidCache[cacheKey] = resolved;
        }

        return string.IsNullOrEmpty(resolved.SiteKey) ? null : resolved;
    }

    public static ResolvedSiteInfo? TryResolveQuickBrowserSite(
        string browserAppKey,
        string? aumid,
        string? smtcTitle,
        string? smtcArtist,
        string? smtcAlbum)
    {
        var pwaSite = TryResolvePwaSiteFromAumid(browserAppKey, aumid);
        if (pwaSite.HasValue)
            return pwaSite.Value;

        string? directSite = DetectSite(smtcAlbum) ?? DetectSite(smtcTitle) ?? DetectSite(smtcArtist);
        if (!string.IsNullOrEmpty(directSite))
            return new ResolvedSiteInfo(directSite, FormatSiteDisplayName(directSite));

        return null;
    }

    public static ResolvedSiteInfo? ResolveBrowserSite(
        string browserAppKey,
        string? aumid,
        string? smtcTitle,
        string? smtcArtist,
        string? smtcAlbum,
        bool sessionIsPlaying,
        IReadOnlyList<RunningProcessInfo> browserProcs,
        IReadOnlyList<Win32.VisibleWindowInfo>? allVisibleWindows = null)
    {
        // 0. Chromium PWA AUMID check & direct SMTC metadata check (zero disk/UIA)
        var pwaSite = TryResolvePwaSiteFromAumid(browserAppKey, aumid);
        if (pwaSite.HasValue)
            return pwaSite.Value;

        bool isPwaAumid = !string.IsNullOrEmpty(aumid) && aumid.Contains("_crx_", StringComparison.OrdinalIgnoreCase);

        string? directSite = DetectSite(smtcAlbum) ?? DetectSite(smtcTitle) ?? DetectSite(smtcArtist);
        if (!string.IsNullOrEmpty(directSite))
        {
            var info = new ResolvedSiteInfo(directSite, FormatSiteDisplayName(directSite));
            RememberSticky(browserAppKey, smtcTitle, info);
            return info;
        }

        // Gather visible top-level browser windows from existing snapshot when available
        var pids = browserProcs.Select(p => p.Pid).Where(p => p > 0).ToList();
        var visibleWindows = GetVisibleBrowserWindows(pids, allVisibleWindows);

        // 1. Live top-level browser window titles via snapshot + MainWindowTitle (zero disk/UIA)
        var windowTitles = new List<string>(visibleWindows.Count + browserProcs.Count);
        foreach (var w in visibleWindows)
            windowTitles.Add(w.Title);
        foreach (var proc in browserProcs)
        {
            if (!string.IsNullOrWhiteSpace(proc.MainWindowTitle))
                windowTitles.Add(proc.MainWindowTitle);
        }

        string? bestWinSite = null;
        int bestWinScore = -1;
        foreach (string rawWinTitle in windowTitles)
        {
            string normWinTitle = NormalizeBrowserWindowTitle(rawWinTitle);
            string? winSite = DetectSite(rawWinTitle, allowStandaloneX: true) ?? DetectSite(normWinTitle, allowStandaloneX: true);
            if (string.IsNullOrEmpty(winSite))
                continue;

            int score = ScoreTitleText(normWinTitle, smtcTitle, smtcArtist);
            if (score > bestWinScore)
            {
                bestWinScore = score;
                bestWinSite = winSite;
            }
        }

        if (!string.IsNullOrEmpty(bestWinSite) && bestWinScore >= 20)
        {
            var info = new ResolvedSiteInfo(bestWinSite, FormatSiteDisplayName(bestWinSite));
            RememberSticky(browserAppKey, smtcTitle, info);
            return info;
        }

        // 2. Sticky cache hit for this exact track/video title BEFORE triggering tab/disk scans
        if (!isPwaAumid && TryGetSticky(browserAppKey, smtcTitle, out var sticky))
            return sticky;

        // 3. Open tab inspection (skipped for PWA AUMIDs so main browser tabs never hijack a PWA)
        List<OpenTabInfo> openTabs = [];
        if (!isPwaAumid)
        {
            openTabs = GeckoBrowsers.Contains(browserAppKey)
                ? GetGeckoOpenTabs(browserAppKey)
                : GetChromiumOpenTabs(browserAppKey, visibleWindows);

            if (openTabs.Count > 0)
            {
                OpenTabInfo? bestTab = null;
                int bestScore = 0;

                foreach (var tab in openTabs)
                {
                    int score = ScoreTabMatch(tab, smtcTitle, smtcArtist);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestTab = tab;
                    }
                }

                if (bestTab.HasValue && bestScore >= 10)
                {
                    var info = new ResolvedSiteInfo(bestTab.Value.SiteKey, bestTab.Value.DisplayName);
                    RememberSticky(browserAppKey, smtcTitle, info);
                    return info;
                }
            }
        }

        // 4. Prioritize sound-playing open tab when session is actively playing (beats unrelated score-0 window titles)
        if (sessionIsPlaying && openTabs.Count > 0)
        {
            OpenTabInfo? bestPlayingTab = null;
            int bestPlayingScore = -1;
            foreach (var tab in openTabs)
            {
                if (!tab.SoundPlaying)
                    continue;

                int score = ScoreTabMatch(tab, smtcTitle, smtcArtist);
                if (score > bestPlayingScore)
                {
                    bestPlayingScore = score;
                    bestPlayingTab = tab;
                }
            }

            if (bestPlayingTab.HasValue)
                return new ResolvedSiteInfo(bestPlayingTab.Value.SiteKey, bestPlayingTab.Value.DisplayName);
        }

        // 5. Fallback window title match (score < 20)
        if (!string.IsNullOrEmpty(bestWinSite))
            return new ResolvedSiteInfo(bestWinSite, FormatSiteDisplayName(bestWinSite));

        // 6. Final fallback: any sound-playing tab
        if (openTabs.Count > 0)
        {
            var soundTab = openTabs.FirstOrDefault(t => t.SoundPlaying);
            if (!string.IsNullOrEmpty(soundTab.SiteKey))
                return new ResolvedSiteInfo(soundTab.SiteKey, soundTab.DisplayName);
        }

        return null;
    }

    public static bool IsSiteTabStillOpen(
        string browserAppKey,
        string? siteKey,
        IReadOnlyList<RunningProcessInfo> browserProcs,
        IReadOnlyList<Win32.VisibleWindowInfo>? allVisibleWindows = null)
    {
        if (string.IsNullOrWhiteSpace(siteKey) || browserProcs.Count == 0)
            return false;

        var pids = browserProcs.Select(p => p.Pid).Where(p => p > 0).ToList();
        var visibleWindows = GetVisibleBrowserWindows(pids, allVisibleWindows);

        foreach (var w in visibleWindows)
        {
            string? ws = DetectSite(w.Title, allowStandaloneX: true) ??
                         DetectSite(NormalizeBrowserWindowTitle(w.Title), allowStandaloneX: true);
            if (!string.IsNullOrEmpty(ws) && ws.Equals(siteKey, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        List<OpenTabInfo> openTabs = GeckoBrowsers.Contains(browserAppKey)
            ? GetGeckoOpenTabs(browserAppKey)
            : GetChromiumOpenTabs(browserAppKey, visibleWindows);

        if (openTabs.Count == 0)
            return true;

        foreach (var tab in openTabs)
        {
            if (tab.SiteKey.Equals(siteKey, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void RememberSticky(string browserAppKey, string? smtcTitle, ResolvedSiteInfo info)
    {
        if (string.IsNullOrWhiteSpace(smtcTitle)) return;
        string trimmed = smtcTitle.Trim();
        if (trimmed.Length < 5) return;

        string key = $"{browserAppKey.ToLowerInvariant()}|{trimmed}";
        lock (StickyLock)
        {
            if (StickySiteCache.Count >= 256 && !StickySiteCache.ContainsKey(key))
                StickySiteCache.Clear();
            StickySiteCache[key] = info;
        }
    }

    private static bool TryGetSticky(string browserAppKey, string? smtcTitle, out ResolvedSiteInfo info)
    {
        info = default;
        if (string.IsNullOrWhiteSpace(smtcTitle)) return false;
        string key = $"{browserAppKey.ToLowerInvariant()}|{smtcTitle.Trim()}";
        lock (StickyLock)
        {
            return StickySiteCache.TryGetValue(key, out info);
        }
    }

    private static int ScoreTabMatch(OpenTabInfo tab, string? smtcTitle, string? smtcArtist)
    {
        int score = tab.SoundPlaying ? 5 : 0;
        return score + ScoreTitleText(tab.Title, smtcTitle, smtcArtist);
    }

    private static int ScoreTitleText(string candidateTitle, string? smtcTitle, string? smtcArtist)
    {
        if (string.IsNullOrWhiteSpace(candidateTitle))
            return 0;

        int score = 0;
        if (!string.IsNullOrWhiteSpace(smtcTitle))
        {
            string t = smtcTitle.Trim();
            if (t.Length >= 3)
            {
                if (candidateTitle.Contains(t, StringComparison.OrdinalIgnoreCase))
                    score += 100;
                else if (candidateTitle.Length >= 6 && t.Contains(candidateTitle, StringComparison.OrdinalIgnoreCase))
                    score += 80;
                else
                {
                    string[] words = t.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                    int matched = 0;
                    int eligible = 0;
                    foreach (string w in words)
                    {
                        if (w.Length < 4) continue;
                        eligible++;
                        if (candidateTitle.Contains(w, StringComparison.OrdinalIgnoreCase))
                            matched++;
                    }
                    if (eligible > 0 && matched >= Math.Min(2, eligible))
                        score += 40 + matched * 5;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(smtcArtist))
        {
            string a = smtcArtist.Trim();
            if (a.Length >= 3 && candidateTitle.Contains(a, StringComparison.OrdinalIgnoreCase))
                score += 25;
        }

        return score;
    }

    private static List<BrowserWindowInfo> GetVisibleBrowserWindows(
        IReadOnlyCollection<int> pids,
        IReadOnlyList<Win32.VisibleWindowInfo>? allVisibleWindows = null)
    {
        var results = new List<BrowserWindowInfo>();
        if (pids.Count == 0)
            return results;

        var pidSet = new HashSet<int>(pids);
        if (allVisibleWindows != null)
        {
            foreach (var w in allVisibleWindows)
            {
                if (w.Pid != 0 && pidSet.Contains((int)w.Pid) && !string.IsNullOrWhiteSpace(w.Title))
                    results.Add(new BrowserWindowInfo(w.Hwnd, w.Title));
            }
            return results;
        }

        lock (EnumLock)
        {
            _enumTargetPids = pidSet;
            _enumFoundWindows = results;
            try
            {
                Win32.EnumWindows(&EnumBrowserWindowProc, 0);
            }
            finally
            {
                _enumTargetPids = null;
                _enumFoundWindows = null;
            }
        }
        return results;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int EnumBrowserWindowProc(nint hwnd, nint lParam)
    {
        try
        {
            var pids = _enumTargetPids;
            var list = _enumFoundWindows;
            if (pids == null || list == null)
                return 0;

            if (!Win32.IsWindowVisible(hwnd))
                return 1;

            Win32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != 0 && pids.Contains((int)pid))
            {
                string title = Win32.GetWindowText(hwnd);
                if (!string.IsNullOrWhiteSpace(title))
                    list.Add(new BrowserWindowInfo(hwnd, title));
            }
            return 1;
        }
        catch
        {
            return 1;
        }
    }

    private static List<OpenTabInfo> GetGeckoOpenTabs(string browserAppKey)
    {
        lock (CacheLock)
        {
            long now = Environment.TickCount64;
            if (Cache.TryGetValue(browserAppKey, out var cached) && now - cached.Tick < 4000)
                return cached.Tabs;
        }

        var tabs = ScanGeckoProfiles(browserAppKey);
        lock (CacheLock)
        {
            Cache[browserAppKey] = (Environment.TickCount64, tabs);
        }
        return tabs;
    }

    private static List<OpenTabInfo> GetChromiumOpenTabs(string browserAppKey, List<BrowserWindowInfo> visibleWindows)
    {
        lock (CacheLock)
        {
            long now = Environment.TickCount64;
            if (Cache.TryGetValue(browserAppKey, out var cached) && now - cached.Tick < 4500)
                return cached.Tabs;
        }

        var combined = new List<OpenTabInfo>();

        // Primary for Edge/Chrome/Brave: live UI Automation TabItem inspection (works with zero disk delay)
        if (visibleWindows.Count > 0)
        {
            var hwnds = visibleWindows.Select(w => w.Hwnd).ToList();
            var tabNames = UiAutomation.GetTabNamesForWindows(hwnds);
            foreach (string rawTab in tabNames)
            {
                bool soundPlaying =
                    rawTab.Contains("Ses çalıyor", StringComparison.OrdinalIgnoreCase) ||
                    rawTab.Contains("Ses çalınıyor", StringComparison.OrdinalIgnoreCase) ||
                    rawTab.Contains("Ses yürütülüyor", StringComparison.OrdinalIgnoreCase) ||
                    rawTab.Contains("Ses yürütüyor", StringComparison.OrdinalIgnoreCase) ||
                    rawTab.Contains("Ses oynatılıyor", StringComparison.OrdinalIgnoreCase) ||
                    rawTab.Contains("Ses oynatıyor", StringComparison.OrdinalIgnoreCase) ||
                    rawTab.Contains("Playing audio", StringComparison.OrdinalIgnoreCase) ||
                    rawTab.Contains("Audio playing", StringComparison.OrdinalIgnoreCase) ||
                    rawTab.Contains("Audio is playing", StringComparison.OrdinalIgnoreCase) ||
                    rawTab.Contains("Tab is playing audio", StringComparison.OrdinalIgnoreCase) ||
                    rawTab.Contains("Audio wird wiedergegeben", StringComparison.OrdinalIgnoreCase) ||
                    rawTab.Contains("Contenu audio en cours de lecture", StringComparison.OrdinalIgnoreCase) ||
                    rawTab.Contains("Reproduciendo audio", StringComparison.OrdinalIgnoreCase) ||
                    rawTab.Contains("Воспроизводится звук", StringComparison.OrdinalIgnoreCase) ||
                    rawTab.Contains("正在播放音频", StringComparison.OrdinalIgnoreCase) ||
                    rawTab.Contains("正在播放聲音", StringComparison.OrdinalIgnoreCase);

                string cleanTitle = NormalizeBrowserWindowTitle(rawTab);
                string? site = DetectSite(rawTab, allowStandaloneX: true) ?? DetectSite(cleanTitle, allowStandaloneX: true);
                if (!string.IsNullOrEmpty(site))
                {
                    combined.Add(new OpenTabInfo(site, FormatSiteDisplayName(site), cleanTitle, string.Empty, soundPlaying));
                }
            }
        }

        // Secondary fallback: Chromium SNSS Sessions files only when UIA yielded no tabs (e.g. minimized windows)
        if (combined.Count == 0)
        {
            var snssTabs = ScanChromiumProfiles(browserAppKey);
            combined.AddRange(snssTabs);
        }

        lock (CacheLock)
        {
            Cache[browserAppKey] = (Environment.TickCount64, combined);
        }
        return combined;
    }

    private static string GetChromiumUserDataRoot(string browserAppKey)
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        return browserAppKey.ToLowerInvariant() switch
        {
            "msedge" => Path.Combine(localAppData, "Microsoft", "Edge", "User Data"),
            "chrome" => Path.Combine(localAppData, "Google", "Chrome", "User Data"),
            "brave" => Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data"),
            "vivaldi" => Path.Combine(localAppData, "Vivaldi", "User Data"),
            "thorium" => Path.Combine(localAppData, "Thorium", "User Data"),
            "opera" => Path.Combine(appData, "Opera Software", "Opera Stable"),
            "opera_gx" => Path.Combine(appData, "Opera Software", "Opera GX Stable"),
            _ => string.Empty
        };
    }

    private static List<OpenTabInfo> ScanChromiumProfiles(string browserAppKey)
    {
        var result = new List<OpenTabInfo>();
        string userDataRoot = GetChromiumUserDataRoot(browserAppKey);

        if (string.IsNullOrEmpty(userDataRoot) || !Directory.Exists(userDataRoot))
            return result;

        var profileDirs = new List<string>();
        try
        {
            string directSessions = Path.Combine(userDataRoot, "Sessions");
            if (Directory.Exists(directSessions))
                profileDirs.Add(userDataRoot);

            foreach (string dir in Directory.EnumerateDirectories(userDataRoot))
            {
                string name = Path.GetFileName(dir);
                if (name.Equals("System Profile", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Guest Profile", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (Directory.Exists(Path.Combine(dir, "Sessions")))
                    profileDirs.Add(dir);
            }
        }
        catch
        {
            return result;
        }

        var fileTargets = new List<(string Path, byte CommandId, long Ticks, long Length)>();
        var sigBuilder = new StringBuilder();
        foreach (string profileDir in profileDirs)
        {
            string sessionsDir = Path.Combine(profileDir, "Sessions");
            try
            {
                var files = new DirectoryInfo(sessionsDir).GetFiles();
                var latestTabs = files
                    .Where(f => f.Name.StartsWith("Tabs_", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();
                var latestSession = files
                    .Where(f => f.Name.StartsWith("Session_", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (latestTabs != null)
                {
                    long ticks = latestTabs.LastWriteTimeUtc.Ticks;
                    long len = latestTabs.Length;
                    fileTargets.Add((latestTabs.FullName, 1, ticks, len));
                    sigBuilder.Append(latestTabs.FullName).Append('|').Append(ticks).Append('|').Append(len).Append("|1;");
                }
                if (latestSession != null)
                {
                    long ticks = latestSession.LastWriteTimeUtc.Ticks;
                    long len = latestSession.Length;
                    fileTargets.Add((latestSession.FullName, 6, ticks, len));
                    sigBuilder.Append(latestSession.FullName).Append('|').Append(ticks).Append('|').Append(len).Append("|6;");
                }
            }
            catch
            {
                // Ignore transient locked/partial profile scans
            }
        }

        if (fileTargets.Count == 0)
            return result;

        string signature = sigBuilder.ToString();
        lock (CacheLock)
        {
            if (ChromiumFileCache.TryGetValue(browserAppKey, out var cached) &&
                string.Equals(cached.Signature, signature, StringComparison.Ordinal))
            {
                return cached.Tabs;
            }
        }

        var tabMap = new Dictionary<int, (string Url, string Title)>();
        foreach (var target in fileTargets)
        {
            ParseSnssFile(target.Path, expectedCommandId: target.CommandId, tabMap);
        }

        foreach (var entry in tabMap.Values)
        {
            string? site = DetectSite(entry.Url) ?? DetectSite(entry.Title, allowStandaloneX: true);
            if (!string.IsNullOrEmpty(site))
            {
                result.Add(new OpenTabInfo(site, FormatSiteDisplayName(site), entry.Title, entry.Url, SoundPlaying: false));
            }
        }

        lock (CacheLock)
        {
            ChromiumFileCache[browserAppKey] = (signature, result);
        }

        return result;
    }

    private static void ParseSnssFile(string path, byte expectedCommandId, Dictionary<int, (string Url, string Title)> tabMap)
    {
        try
        {
            byte[] data;
            int readTotal = 0;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long len = fs.Length;
                if (len < 12 || len > 16 * 1024 * 1024)
                    return;

                data = new byte[(int)len];
                while (readTotal < data.Length)
                {
                    int n = fs.Read(data, readTotal, data.Length - readTotal);
                    if (n <= 0) break;
                    readTotal += n;
                }
                if (readTotal < 12)
                    return;
            }

            if (data[0] != (byte)'S' || data[1] != (byte)'N' || data[2] != (byte)'S' || data[3] != (byte)'S')
                return;

            int pos = 8;
            while (pos + 2 <= readTotal)
            {
                ushort size = (ushort)(data[pos] | (data[pos + 1] << 8));
                pos += 2;
                if (size == 0 || pos + size > readTotal)
                    break;

                byte cmdId = data[pos];
                if (cmdId == expectedCommandId && size > 13)
                {
                    TryParseUpdateTabNavigation(data.AsSpan(pos + 1, size - 1), tabMap);
                }

                pos += size;
            }
        }
        catch
        {
            // Ignore transient reads while Chromium appends to Sessions file
        }
    }

    private static void TryParseUpdateTabNavigation(ReadOnlySpan<byte> payload, Dictionary<int, (string Url, string Title)> tabMap)
    {
        if (payload.Length < 16)
            return;

        uint pickleSize = (uint)(payload[0] | (payload[1] << 8) | (payload[2] << 16) | (payload[3] << 24));
        if (pickleSize < 12 || 4 + pickleSize > (uint)payload.Length)
            return;

        int p = 4;
        int end = 4 + (int)pickleSize;

        if (p + 8 > end) return;
        int tabId = BitConverter.ToInt32(payload.Slice(p, 4));
        p += 4;
        p += 4; // skip index

        if (p + 4 > end) return;
        int urlByteLen = BitConverter.ToInt32(payload.Slice(p, 4));
        p += 4;
        if (urlByteLen < 0 || urlByteLen > 8192) return;
        int urlAligned = (urlByteLen + 3) & ~3;
        if (p + urlAligned > end) return;
        string url = urlByteLen > 0 ? Encoding.UTF8.GetString(payload.Slice(p, urlByteLen)) : string.Empty;
        p += urlAligned;

        string title = string.Empty;
        if (p + 4 <= end)
        {
            int titleChars = BitConverter.ToInt32(payload.Slice(p, 4));
            p += 4;
            if (titleChars >= 0 && titleChars <= 4096)
            {
                int titleBytes = titleChars * 2;
                int titleAligned = (titleBytes + 3) & ~3;
                if (p + titleAligned <= end && titleBytes > 0)
                    title = Encoding.Unicode.GetString(payload.Slice(p, titleBytes));
            }
        }

        if (!string.IsNullOrWhiteSpace(url) &&
            (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            tabMap[tabId] = (url, title);
        }
    }

    private static List<OpenTabInfo> ScanGeckoProfiles(string browserAppKey)
    {
        var result = new List<OpenTabInfo>();
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string root = browserAppKey.ToLowerInvariant() switch
        {
            "zen" => Path.Combine(appData, "zen", "Profiles"),
            "firefox" => Path.Combine(appData, "Mozilla", "Firefox", "Profiles"),
            "waterfox" => Path.Combine(appData, "Waterfox", "Profiles"),
            "floorp" => Path.Combine(appData, "Floorp", "Profiles"),
            "librewolf" => Path.Combine(appData, "LibreWolf", "Profiles"),
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            return result;

        try
        {
            FileInfo? bestFile = null;
            long bestTicks = 0;
            long bestLength = 0;
            foreach (string dir in Directory.EnumerateDirectories(root))
            {
                string f = Path.Combine(dir, "sessionstore-backups", "recovery.jsonlz4");
                var fi = new FileInfo(f);
                if (!fi.Exists) continue;
                long wt = fi.LastWriteTimeUtc.Ticks;
                if (bestFile == null || wt > bestTicks)
                {
                    bestTicks = wt;
                    bestLength = fi.Length;
                    bestFile = fi;
                }
            }

            if (bestFile == null) return result;

            lock (CacheLock)
            {
                if (GeckoFileCache.TryGetValue(browserAppKey, out var cached) &&
                    string.Equals(cached.Path, bestFile.FullName, StringComparison.OrdinalIgnoreCase) &&
                    cached.LastWriteTicks == bestTicks &&
                    cached.Length == bestLength)
                {
                    return cached.Tabs;
                }
            }

            if (bestLength <= 12 || bestLength > 16 * 1024 * 1024)
                return result;

            byte[] raw;
            using (var fs = new FileStream(bestFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long len = fs.Length;
                if (len <= 12 || len > 16 * 1024 * 1024) return result;
                raw = new byte[(int)len];
                int read = 0;
                while (read < raw.Length)
                {
                    int n = fs.Read(raw, read, raw.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
            }

            byte[]? jsonBytes = DecompressMozLz4(raw);
            if (jsonBytes == null) return result;

            using var doc = JsonDocument.Parse(jsonBytes);
            if (!doc.RootElement.TryGetProperty("windows", out var windows) || windows.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var win in windows.EnumerateArray())
            {
                if (!win.TryGetProperty("tabs", out var tabs) || tabs.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var tab in tabs.EnumerateArray())
                {
                    if (!tab.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
                        continue;

                    int count = entries.GetArrayLength();
                    if (count == 0) continue;

                    int idx = tab.TryGetProperty("index", out var idxProp) && idxProp.TryGetInt32(out int i)
                        ? Math.Clamp(i - 1, 0, count - 1)
                        : count - 1;

                    var entry = entries[idx];
                    string url = entry.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                    string title = entry.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    bool soundPlaying = tab.TryGetProperty("soundPlaying", out var sp) && sp.ValueKind == JsonValueKind.True;

                    string? site = DetectSite(url) ?? DetectSite(title, allowStandaloneX: true);
                    if (string.IsNullOrEmpty(site) && soundPlaying)
                        site = ExtractHostSiteKey(url);

                    if (!string.IsNullOrEmpty(site))
                    {
                        result.Add(new OpenTabInfo(site, FormatSiteDisplayName(site), title, url, soundPlaying));
                    }
                }
            }

            lock (CacheLock)
            {
                GeckoFileCache[browserAppKey] = (bestFile.FullName, bestTicks, bestLength, result);
            }
        }
        catch
        {
            // Session file can be written concurrently by browser; safely ignore transient read errors
        }

        return result;
    }

    internal static string? ExtractHostSiteKey(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;
        if (uri.Scheme is not ("http" or "https"))
            return null;

        string host = uri.Host.ToLowerInvariant();
        if (host is "localhost" or "127.0.0.1" || string.IsNullOrEmpty(host))
            return null;

        var (label, _) = ExtractHostLabels(url);
        if (label is "google" or "bing" or "duckduckgo" or "yandex" or "github" or "chatgpt" or "claude")
            return null;

        return label.Length >= 2 ? label : null;
    }

    private static byte[]? DecompressMozLz4(byte[] src)
    {
        if (src.Length < 12 ||
            src[0] != (byte)'m' || src[1] != (byte)'o' || src[2] != (byte)'z' ||
            src[3] != (byte)'L' || src[4] != (byte)'z' || src[5] != (byte)'4' ||
            src[6] != (byte)'0' || src[7] != 0)
        {
            return null;
        }

        int outLen = src[8] | (src[9] << 8) | (src[10] << 16) | (src[11] << 24);
        if (outLen <= 0 || outLen > 64 * 1024 * 1024)
            return null;

        byte[] dst = new byte[outLen];
        int s = 12, d = 0;

        while (s < src.Length && d < outLen)
        {
            byte token = src[s++];
            int litLen = token >> 4;
            if (litLen == 15)
            {
                byte b;
                do
                {
                    if (s >= src.Length) return null;
                    b = src[s++];
                    litLen += b;
                    if (litLen > outLen - d) return null;
                } while (b == 255);
            }

            if (s + litLen > src.Length || d + litLen > outLen) return null;
            Buffer.BlockCopy(src, s, dst, d, litLen);
            s += litLen;
            d += litLen;

            if (s >= src.Length || d >= outLen) break;

            if (s + 2 > src.Length) return null;
            int offset = src[s] | (src[s + 1] << 8);
            s += 2;
            if (offset <= 0 || d - offset < 0) return null;

            int matchLen = (token & 0x0F) + 4;
            if (matchLen == 19)
            {
                byte b;
                do
                {
                    if (s >= src.Length) return null;
                    b = src[s++];
                    matchLen += b;
                    if (matchLen > outLen - d) return null;
                } while (b == 255);
            }

            if (d + matchLen > outLen) return null;
            for (int i = 0; i < matchLen; i++)
            {
                dst[d] = dst[d - offset];
                d++;
            }
        }

        return dst;
    }
}
