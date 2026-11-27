using Cueway.Interop;

namespace Cueway.Media;

internal readonly record struct RunningProcessInfo(
    int Pid,
    string Name,
    string ExePath,
    string MainWindowTitle);

internal readonly record struct ResolvedAppInfo(
    bool IsAlive,
    string AppKey,
    string DisplayName,
    string ExePath,
    int[] ProcessIds,
    bool IsBrowser);

internal static unsafe class ProcessResolver
{
    private static readonly HashSet<string> BrowserExeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "zen",
        "firefox",
        "chrome",
        "msedge",
        "brave",
        "opera",
        "opera_gx",
        "vivaldi",
        "floorp",
        "librewolf",
        "waterfox",
        "arc",
        "thorium",
        "chromium",
        "helium"
    };

    private static readonly HashSet<string> GeckoBrowserExeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "zen",
        "firefox",
        "floorp",
        "librewolf",
        "waterfox"
    };

    private static readonly Dictionary<string, (string AppKey, string DisplayName, bool IsBrowser)> KnownAumidPrefixes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["SpotifyAB.SpotifyMusic"] = ("spotify", "Spotify", false),
            ["AppleInc.AppleMusicWin"] = ("applemusic", "Apple Music", false),
            ["Microsoft.ZuneMusic"] = ("microsoft.media.player", "Windows Media Player", false),
            ["Microsoft.ZuneVideo"] = ("video", "Movies & TV", false),
            ["TIDAL"] = ("tidal", "TIDAL", false),
            ["Deezer"] = ("deezer", "Deezer", false),
            ["AmazonMusic"] = ("amazonmusic", "Amazon Music", false),
            ["Mozilla.Firefox"] = ("firefox", "Firefox", true),
            ["MozillaFirefox"] = ("firefox", "Firefox", true),
            ["Zen"] = ("zen", "Zen Browser", true),
            ["Chrome"] = ("chrome", "Google Chrome", true),
            ["MSEdge"] = ("msedge", "Microsoft Edge", true),
            ["Brave"] = ("brave", "Brave", true),
        };

    private static readonly HashSet<string> IgnoredAudioProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "cueway",
        "winmediarouter",
        "explorer",
        "shellexperiencehost",
        "startmenuexperiencehost",
        "searchhost",
        "searchapp",
        "applicationframehost",
        "systemsettings",
        "runtimebroker",
        "audiodg",
        "svchost",
        "dwm",
        "csrss",
        "winlogon",
        "services",
        "lsass",
        "conhost",
        "windowsterminal",
        "cmd",
        "powershell",
        "pwsh",
        "taskmgr",
        "mmc",
        "msiexec",
        "smartscreen",
        "lockapp",
        "sihost",
        "ctfmon",
        "textinputhost",
        "widgetservice",
        "widgets",
        "msedgewebview2",
        "nvcontainer",
        "nvidia share",
        "nvdisplay.container",
        "rtkauduservice64",
        "ravbg64",
        "nahimicsvc32",
        "nahimicsvc64",
        "lghub_agent",
        "icue",
        "razercentral",
        "steelseriessonar",
        "voicemeeter",
        "voicemeeterpro",
        "voicemeeter8",
        "discord",
        "discordptb",
        "discordcanary",
        "teams",
        "ms-teams",
        "zoom",
        "ts3client_win64",
        "teamspeak",
        "mumble"
    };

    private static readonly HashSet<string> KnownMediaAudioProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "stremio", "stremio-shell-ng", "stremio-runtime", "stremio-desktop",
        "vlc", "potplayer", "potplayermini64", "potplayermini",
        "mpc-hc64", "mpc-hc", "mpc-be64", "mpc-be",
        "kodi", "plex", "plexhtpc", "jellyfinmediaplayer",
        "foobar2000", "musicbee", "aimp", "winamp", "itunes",
        "tidal", "deezer", "qobuz", "amazon music", "spotify", "applemusic",
        "mpv", "mpvnet", "audacious", "clementine", "strawberry",
        "gom", "gom64", "smplayer", "bsplayer", "wmplayer", "microsoft.media.player"
    };

    private static readonly object _pidCacheLock = new();
    private static readonly Dictionary<int, (string Name, string ExePath, int Gen)> _pidCache = new(256);
    private static readonly List<int> _stalePids = new(64);
    private static int _pidCacheGen;

    public static List<RunningProcessInfo> TakeSnapshot()
        => TakeSnapshotWithWindows().Processes;

    public static (List<RunningProcessInfo> Processes, List<Win32.VisibleWindowInfo> VisibleWindows) TakeSnapshotWithWindows()
    {
        var visibleWindows = Win32.SnapshotVisibleWindows();
        var bestTitleByPid = BuildBestTitleMap(visibleWindows);

        var list = new List<RunningProcessInfo>(256);
        nint snap = Win32.CreateToolhelp32Snapshot(Win32.TH32CS_SNAPPROCESS, 0);
        if (snap == 0 || snap == Win32.INVALID_HANDLE_VALUE)
            return (list, visibleWindows);

        List<(int Pid, string Name)>? newPids = null;
        int gen;
        lock (_pidCacheLock)
        {
            gen = ++_pidCacheGen;
            try
            {
                var entry = new Win32.PROCESSENTRY32W
                {
                    dwSize = (uint)sizeof(Win32.PROCESSENTRY32W)
                };

                if (Win32.Process32FirstW(snap, &entry))
                {
                    do
                    {
                        int pid = unchecked((int)entry.th32ProcessID);
                        if (pid <= 4)
                            continue;

                        ReadOnlySpan<char> rawSpan = new(entry.szExeFile, 260);
                        int nullIdx = rawSpan.IndexOf('\0');
                        ReadOnlySpan<char> nameSpan = nullIdx >= 0 ? rawSpan[..nullIdx] : rawSpan;
                        if (nameSpan.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            nameSpan = nameSpan[..^4];
                        if (nameSpan.IsEmpty)
                            continue;

                        if (_pidCache.TryGetValue(pid, out var cached) &&
                            nameSpan.Equals(cached.Name.AsSpan(), StringComparison.OrdinalIgnoreCase))
                        {
                            _pidCache[pid] = (cached.Name, cached.ExePath, gen);
                            string title = bestTitleByPid.TryGetValue(pid, out var bt) ? bt.Title : string.Empty;
                            list.Add(new RunningProcessInfo(pid, cached.Name, cached.ExePath, title));
                        }
                        else
                        {
                            (newPids ??= new List<(int, string)>(16)).Add((pid, nameSpan.ToString()));
                        }
                    }
                    while (Win32.Process32NextW(snap, &entry));
                }
            }
            finally
            {
                Win32.CloseHandle(snap);
            }

            if (newPids == null || newPids.Count == 0)
            {
                PruneStalePidsLocked(gen);
            }
        }

        if (newPids != null && newPids.Count > 0)
        {
            var resolvedNew = new List<(int Pid, string Name, string ExePath)>(newPids.Count);
            foreach (var (pid, name) in newPids)
            {
                string exePath = Win32.TryGetProcessImagePath((uint)pid) ?? string.Empty;
                resolvedNew.Add((pid, name, exePath));
                string title = bestTitleByPid.TryGetValue(pid, out var bt) ? bt.Title : string.Empty;
                list.Add(new RunningProcessInfo(pid, name, exePath, title));
            }

            lock (_pidCacheLock)
            {
                foreach (var item in resolvedNew)
                {
                    _pidCache[item.Pid] = (item.Name, item.ExePath, gen);
                }
                PruneStalePidsLocked(gen);
            }
        }

        return (list, visibleWindows);
    }

    private static void PruneStalePidsLocked(int activeGen)
    {
        _stalePids.Clear();
        foreach (var kv in _pidCache)
        {
            if (kv.Value.Gen != activeGen)
                _stalePids.Add(kv.Key);
        }
        foreach (int stalePid in _stalePids)
            _pidCache.Remove(stalePid);
    }

    public static string? TryGetCachedProcessName(int pid)
    {
        if (pid <= 4) return null;
        lock (_pidCacheLock)
        {
            return _pidCache.TryGetValue(pid, out var cached) ? cached.Name : null;
        }
    }

    public static Dictionary<string, List<RunningProcessInfo>> BuildNameIndex(IReadOnlyList<RunningProcessInfo> processes)
    {
        var index = new Dictionary<string, List<RunningProcessInfo>>(64, StringComparer.OrdinalIgnoreCase);
        foreach (var p in processes)
        {
            if (string.IsNullOrEmpty(p.Name))
                continue;
            if (!index.TryGetValue(p.Name, out var list))
            {
                list = new List<RunningProcessInfo>(2);
                index[p.Name] = list;
            }
            list.Add(p);
        }
        return index;
    }

    private static Dictionary<int, (int Score, string Title)> BuildBestTitleMap(IReadOnlyList<Win32.VisibleWindowInfo> windows)
    {
        var map = new Dictionary<int, (int Score, string Title)>(windows.Count);
        foreach (var w in windows)
        {
            if (string.IsNullOrWhiteSpace(w.Title))
                continue;

            int score = 120;
            if (!w.IsToolWindow && !w.HasOwner)
                score += 300;
            if (w.IsIconic)
                score += 80;
            else if (w.Width >= 60 && w.Height >= 40)
                score += (int)Math.Clamp(((long)w.Width * w.Height) / 20000L, 1L, 150L);
            else
                score -= 200;

            if (w.Pid != 0)
                UpdateBestTitle(map, (int)w.Pid, score, w.Title);
            if (w.CorePid != 0 && w.CorePid != w.Pid)
                UpdateBestTitle(map, (int)w.CorePid, score, w.Title);
        }
        return map;
    }

    private static void UpdateBestTitle(Dictionary<int, (int Score, string Title)> map, int pid, int score, string title)
    {
        if (!map.TryGetValue(pid, out var cur) || score > cur.Score)
            map[pid] = (score, title);
    }

    public static bool IsBrowserProcessName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string normalized = NormalizeExeKey(name);
        return BrowserExeNames.Contains(normalized);
    }

    public static string? ResolveBrowserDisplayName(MediaSessionInfo session)
    {
        string? browserKey = BrowserSiteScanner.ExtractBrowserKeyFromAppKey(session.AppKey);
        if (string.IsNullOrEmpty(browserKey) && !string.IsNullOrWhiteSpace(session.ExePath))
        {
            string exeKey = NormalizeExeKey(session.ExePath);
            if (IsBrowserProcessName(exeKey))
                browserKey = exeKey;
        }
        if (string.IsNullOrEmpty(browserKey) && session.IsBrowser)
        {
            browserKey = NormalizeExeKey(session.AppKey);
        }
        if (string.IsNullOrEmpty(browserKey))
            return null;

        return FormatDisplayName(browserKey, session.ExePath);
    }

    public static string NormalizeExeKey(string exeOrName)
    {
        string file = Path.GetFileNameWithoutExtension(exeOrName.Trim());
        return file.ToLowerInvariant();
    }

    public static string NormalizeAppGroupKey(string processName)
    {
        string key = NormalizeExeKey(processName);
        return key switch
        {
            "stremio" or "stremio-shell-ng" or "stremio-runtime" or "stremio-desktop" => "stremio",
            "potplayer" or "potplayermini64" or "potplayermini" => "potplayer",
            "mpc-hc64" or "mpc-hc" => "mpc-hc",
            "mpc-be64" or "mpc-be" => "mpc-be",
            "discord" or "discordptb" or "discordcanary" => "discord",
            "plex" or "plexhtpc" => "plex",
            _ => key
        };
    }

    public static bool IsIgnoredAudioProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return true;
        string key = NormalizeExeKey(processName);
        return IgnoredAudioProcesses.Contains(key) || IgnoredAudioProcesses.Contains(NormalizeAppGroupKey(key));
    }

    public static bool IsKnownMediaAudioProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        string key = NormalizeExeKey(processName);
        return KnownMediaAudioProcesses.Contains(key) || KnownMediaAudioProcesses.Contains(NormalizeAppGroupKey(key));
    }

    public static string FormatDisplayName(string appKey, string exePath)
    {
        return appKey.ToLowerInvariant() switch
        {
            "zen" => "Zen Browser",
            "firefox" => "Firefox",
            "chrome" => "Google Chrome",
            "msedge" => "Microsoft Edge",
            "brave" => "Brave",
            "opera" => "Opera",
            "opera_gx" => "Opera GX",
            "vivaldi" => "Vivaldi",
            "floorp" => "Floorp",
            "librewolf" => "LibreWolf",
            "waterfox" => "Waterfox",
            "arc" => "Arc",
            "thorium" => "Thorium",
            "chromium" => "Chromium",
            "helium" => "Helium",
            "spotify" => "Spotify",
            "mpv" => "mpv",
            "mpvnet" => "mpv.net",
            "vlc" => "VLC",
            "stremio" or "stremio-shell-ng" or "stremio-runtime" or "stremio-desktop" => "Stremio",
            "foobar2000" => "foobar2000",
            "musicbee" => "MusicBee",
            "aimp" => "AIMP",
            "applemusic" => "Apple Music",
            "potplayer" or "potplayermini64" or "potplayermini" => "PotPlayer",
            "mpc-hc64" or "mpc-hc" => "MPC-HC",
            "mpc-be64" or "mpc-be" => "MPC-BE",
            "kodi" => "Kodi",
            "plex" or "plexhtpc" => "Plex",
            "jellyfinmediaplayer" => "Jellyfin",
            "discord" or "discordptb" or "discordcanary" => "Discord",
            "telegram" => "Telegram",
            "whatsapp" => "WhatsApp",
            "tidal" => "TIDAL",
            "deezer" => "Deezer",
            "qobuz" => "Qobuz",
            "winamp" => "Winamp",
            "itunes" => "iTunes",
            "smplayer" => "SMPlayer",
            "gom" or "gom64" => "GOM Player",
            "audacious" => "Audacious",
            "strawberry" => "Strawberry",
            "clementine" => "Clementine",
            "wmplayer" or "microsoft.media.player" => "Windows Media Player",
            "youtube music" or "youtubemusic" or "youtube music desktop app" or "ytmdesktop" or "pear-desktop" => "YouTube Music",
            _ => !string.IsNullOrWhiteSpace(exePath)
                ? TitleCaseToken(Path.GetFileNameWithoutExtension(exePath))
                : TitleCaseToken(appKey)
        };
    }

    private static string TitleCaseToken(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "Media App";
        return char.ToUpperInvariant(s[0]) + s[1..];
    }

    public static ResolvedAppInfo ResolveSmtcAumid(
        string? aumid,
        string? mediaTitle,
        IReadOnlyList<RunningProcessInfo> snapshot)
    {
        string raw = (aumid ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return new ResolvedAppInfo(false, string.Empty, string.Empty, string.Empty, [], false);
        }

        // 1. Direct .exe AUMID or full file path (most Win32 apps: "zen.exe", "Spotify.exe", "C:\...\vlc.exe")
        if (raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || raw.Contains('\\') || raw.Contains('/'))
        {
            string key = NormalizeExeKey(raw);
            var matches = FindByProcessName(snapshot, key);
            if (matches.Count > 0)
            {
                string path = SelectBestPath(matches);
                return new ResolvedAppInfo(
                    true,
                    key,
                    FormatDisplayName(key, path),
                    path,
                    ExtractPids(matches),
                    BrowserExeNames.Contains(key));
            }
            return new ResolvedAppInfo(false, key, FormatDisplayName(key, raw), string.Empty, [], BrowserExeNames.Contains(key));
        }

        // 2. Gecko / Zen Browser / Firefox AUMID hashes or branded AUMIDs
        // Firefox & Zen register AppUserModelIds such as:
        // - "308046B0AF4A39CB" (standard Firefox CityHash) or another 12-20 hex hash for Zen Browser
        // - "MozillaFirefox-308046B0AF4A39CB", "Zen-...", "Mozilla.Firefox..."
        if (TryResolveBrowserAumid(raw, mediaTitle, snapshot, out var browserInfo))
        {
            return browserInfo;
        }

        // 3. Microsoft Store / MSIX AUMIDs: "<PackageFamilyName>!<AppId>"
        int bangIdx = raw.IndexOf('!');
        if (bangIdx > 0)
        {
            string packagePrefix = raw[..bangIdx];
            string appId = raw[(bangIdx + 1)..];
            int underscoreIdx = packagePrefix.IndexOf('_');
            string packageName = underscoreIdx > 0 ? packagePrefix[..underscoreIdx] : packagePrefix;

            // Match running process whose ExePath sits inside WindowsApps\<packageName>...
            var storeMatches = snapshot.Where(p =>
                !string.IsNullOrEmpty(p.ExePath) &&
                p.ExePath.Contains(packageName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (storeMatches.Count > 0)
            {
                string path = SelectBestPath(storeMatches);
                string key = NormalizeExeKey(storeMatches[0].Name);
                return new ResolvedAppInfo(
                    true,
                    key,
                    FormatDisplayName(key, path),
                    path,
                    ExtractPids(storeMatches),
                    BrowserExeNames.Contains(key));
            }

            // Check known Store app mappings
            foreach (var kv in KnownAumidPrefixes)
            {
                if (packageName.Contains(kv.Key, StringComparison.OrdinalIgnoreCase) ||
                    appId.Equals(kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    var byName = FindByProcessName(snapshot, kv.Value.AppKey);
                    if (byName.Count > 0)
                    {
                        string path = SelectBestPath(byName);
                        return new ResolvedAppInfo(
                            true,
                            kv.Value.AppKey,
                            kv.Value.DisplayName,
                            path,
                            ExtractPids(byName),
                            kv.Value.IsBrowser);
                    }
                    return new ResolvedAppInfo(false, kv.Value.AppKey, kv.Value.DisplayName, string.Empty, [], kv.Value.IsBrowser);
                }
            }

            // Fallback match by AppId (e.g. "!Spotify", "!App")
            if (!appId.Equals("App", StringComparison.OrdinalIgnoreCase))
            {
                var byAppId = FindByProcessName(snapshot, appId);
                if (byAppId.Count > 0)
                {
                    string path = SelectBestPath(byAppId);
                    string key = NormalizeExeKey(byAppId[0].Name);
                    return new ResolvedAppInfo(true, key, FormatDisplayName(key, path), path, ExtractPids(byAppId), BrowserExeNames.Contains(key));
                }
            }
        }

        // 4. Generic AUMID token matching against running processes (e.g. "Spotify", "Chrome", "com.squirrel.Discord.Discord")
        string cleaned = raw.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
        var direct = FindByProcessName(snapshot, cleaned);
        if (direct.Count > 0)
        {
            string path = SelectBestPath(direct);
            string key = NormalizeExeKey(direct[0].Name);
            return new ResolvedAppInfo(true, key, FormatDisplayName(key, path), path, ExtractPids(direct), BrowserExeNames.Contains(key));
        }

        // Segment match splitting only on '.' and '!' first so multi-word process names
        // like "com.github.th-ch.youtube-music" match running "YouTube Music" cleanly
        string[] dotSegments = cleaned.Split(['.', '!'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = dotSegments.Length - 1; i >= 0; i--)
        {
            string seg = dotSegments[i];
            if (seg.Length < 3) continue;

            string spaced = seg.Replace('-', ' ').Replace('_', ' ').Trim();
            var spacedMatches = FindByProcessName(snapshot, spaced);
            if (spacedMatches.Count > 0)
            {
                string path = SelectBestPath(spacedMatches);
                string key = NormalizeExeKey(spacedMatches[0].Name);
                return new ResolvedAppInfo(true, key, FormatDisplayName(key, path), path, ExtractPids(spacedMatches), BrowserExeNames.Contains(key));
            }

            string compactSeg = NormalizeCompact(seg);
            if (compactSeg.Length >= 4)
            {
                var compactMatches = FindByCompactProcessName(snapshot, compactSeg);
                if (compactMatches.Count > 0)
                {
                    string path = SelectBestPath(compactMatches);
                    string key = NormalizeExeKey(compactMatches[0].Name);
                    return new ResolvedAppInfo(true, key, FormatDisplayName(key, path), path, ExtractPids(compactMatches), BrowserExeNames.Contains(key));
                }
            }
        }

        // Tokenize by '.', '-', '_', '!'
        string[] tokens = cleaned.Split(['.', '-', '_', '!'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = tokens.Length - 1; i >= 0; i--)
        {
            string token = tokens[i];
            if (token.Length < 3) continue;
            var tokenMatches = FindByProcessName(snapshot, token);
            if (tokenMatches.Count > 0)
            {
                string path = SelectBestPath(tokenMatches);
                string key = NormalizeExeKey(tokenMatches[0].Name);
                return new ResolvedAppInfo(true, key, FormatDisplayName(key, path), path, ExtractPids(tokenMatches), BrowserExeNames.Contains(key));
            }
        }

        // If nothing matched in live processes, it's a stale/ghost SMTC entry
        return new ResolvedAppInfo(false, raw.ToLowerInvariant(), raw, string.Empty, [], false);
    }

    private static bool TryResolveBrowserAumid(
        string raw,
        string? mediaTitle,
        IReadOnlyList<RunningProcessInfo> snapshot,
        out ResolvedAppInfo resolved)
    {
        resolved = default;

        // Direct browser keyword match in AUMID
        string? explicitBrowser = null;
        if (raw.Contains("zen", StringComparison.OrdinalIgnoreCase))
            explicitBrowser = "zen";
        else if (raw.Contains("floorp", StringComparison.OrdinalIgnoreCase))
            explicitBrowser = "floorp";
        else if (raw.Contains("librewolf", StringComparison.OrdinalIgnoreCase))
            explicitBrowser = "librewolf";
        else if (raw.Contains("waterfox", StringComparison.OrdinalIgnoreCase))
            explicitBrowser = "waterfox";
        else if (raw.Contains("firefox", StringComparison.OrdinalIgnoreCase) || raw.Contains("mozilla", StringComparison.OrdinalIgnoreCase))
            explicitBrowser = "firefox";
        else if (raw.Contains("chrome", StringComparison.OrdinalIgnoreCase))
            explicitBrowser = "chrome";
        else if (raw.Contains("msedge", StringComparison.OrdinalIgnoreCase) || raw.Contains("edge", StringComparison.OrdinalIgnoreCase))
            explicitBrowser = "msedge";
        else if (raw.Contains("brave", StringComparison.OrdinalIgnoreCase))
            explicitBrowser = "brave";
        else if (raw.Contains("vivaldi", StringComparison.OrdinalIgnoreCase))
            explicitBrowser = "vivaldi";
        else if (raw.Contains("opera", StringComparison.OrdinalIgnoreCase))
            explicitBrowser = "opera";

        if (explicitBrowser != null)
        {
            var matches = FindByProcessName(snapshot, explicitBrowser);
            if (matches.Count > 0)
            {
                string path = SelectBestPath(matches);
                resolved = new ResolvedAppInfo(true, explicitBrowser, FormatDisplayName(explicitBrowser, path), path, ExtractPids(matches), true);
                return true;
            }

            // Zen Browser sometimes inherits Mozilla/Firefox AUMID prefixes on certain builds
            if (explicitBrowser == "firefox")
            {
                var zenFallback = FindByProcessName(snapshot, "zen");
                if (zenFallback.Count > 0)
                {
                    string path = SelectBestPath(zenFallback);
                    resolved = new ResolvedAppInfo(true, "zen", "Zen Browser", path, ExtractPids(zenFallback), true);
                    return true;
                }
            }

            resolved = new ResolvedAppInfo(false, explicitBrowser, FormatDisplayName(explicitBrowser, string.Empty), string.Empty, [], true);
            return true;
        }

        // Check for Gecko hex hash AUMID (e.g. "308046B0AF4A39CB", "E7CF176E110C211B", etc.)
        if (IsHexHashAumid(raw))
        {
            var runningGeckoBrowsers = snapshot
                .Where(p => GeckoBrowserExeNames.Contains(p.Name))
                .GroupBy(p => p.Name.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            if (runningGeckoBrowsers.Count == 0)
            {
                resolved = new ResolvedAppInfo(false, "gecko", "Gecko Browser", string.Empty, [], true);
                return true;
            }

            string chosenKey;
            if (runningGeckoBrowsers.Count == 1)
            {
                chosenKey = runningGeckoBrowsers.Keys.First();
            }
            else
            {
                // Disambiguate when multiple Gecko browsers are open simultaneously
                // 308046B0AF4A39CB is Mozilla Firefox's canonical Program Files hash
                if (raw.StartsWith("308046B", StringComparison.OrdinalIgnoreCase) &&
                    runningGeckoBrowsers.ContainsKey("firefox") &&
                    !AnyTitleMatches(runningGeckoBrowsers.GetValueOrDefault("zen"), mediaTitle))
                {
                    chosenKey = "firefox";
                }
                else if (!string.IsNullOrWhiteSpace(mediaTitle) &&
                         runningGeckoBrowsers.TryGetValue("zen", out var zenProcs) &&
                         AnyTitleMatches(zenProcs, mediaTitle))
                {
                    chosenKey = "zen";
                }
                else if (!string.IsNullOrWhiteSpace(mediaTitle) &&
                         runningGeckoBrowsers.TryGetValue("firefox", out var ffProcs) &&
                         AnyTitleMatches(ffProcs, mediaTitle))
                {
                    chosenKey = "firefox";
                }
                else
                {
                    chosenKey = runningGeckoBrowsers.ContainsKey("zen")
                        ? "zen"
                        : runningGeckoBrowsers.Keys.First();
                }
            }

            var list = runningGeckoBrowsers[chosenKey];
            string path = SelectBestPath(list);
            resolved = new ResolvedAppInfo(true, chosenKey, FormatDisplayName(chosenKey, path), path, ExtractPids(list), true);
            return true;
        }

        return false;
    }

    private static bool AnyTitleMatches(List<RunningProcessInfo>? procs, string? mediaTitle)
    {
        if (procs == null || string.IsNullOrWhiteSpace(mediaTitle)) return false;
        foreach (var p in procs)
        {
            if (!string.IsNullOrWhiteSpace(p.MainWindowTitle) &&
                p.MainWindowTitle.Contains(mediaTitle, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsHexHashAumid(string aumid)
    {
        string core = aumid;
        int dash = core.LastIndexOf('-');
        if (dash >= 0 && dash + 1 < core.Length)
            core = core[(dash + 1)..];

        if (core.Length < 12 || core.Length > 24)
            return false;

        foreach (char c in core)
        {
            bool isHex = (c >= '0' && c <= '9') ||
                         (c >= 'a' && c <= 'f') ||
                         (c >= 'A' && c <= 'F');
            if (!isHex) return false;
        }
        return true;
    }

    private static List<RunningProcessInfo> FindByProcessName(IReadOnlyList<RunningProcessInfo> snapshot, string name)
    {
        string targetGroup = NormalizeAppGroupKey(name);
        var result = new List<RunningProcessInfo>();
        foreach (var p in snapshot)
        {
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                result.Add(p);
        }
        if (result.Count > 0 || string.IsNullOrEmpty(targetGroup))
            return result;

        foreach (var p in snapshot)
        {
            if (NormalizeAppGroupKey(p.Name).Equals(targetGroup, StringComparison.OrdinalIgnoreCase))
                result.Add(p);
        }
        return result;
    }

    private static List<RunningProcessInfo> FindByCompactProcessName(IReadOnlyList<RunningProcessInfo> snapshot, string compactSeg)
    {
        bool isYtmdAlias = compactSeg is "ytmd" or "ytmdesktop";
        var result = new List<RunningProcessInfo>();
        foreach (var p in snapshot)
        {
            string procCompact = NormalizeCompact(p.Name);
            if (procCompact.Length == 0) continue;

            bool match = procCompact.Equals(compactSeg, StringComparison.Ordinal) ||
                         (compactSeg.Length >= 6 && procCompact.StartsWith(compactSeg, StringComparison.Ordinal)) ||
                         (isYtmdAlias && procCompact is "youtubemusicdesktopapp" or "ytmdesktop");

            if (match)
                result.Add(p);
        }
        return result;
    }

    private static string NormalizeCompact(string value)
    {
        Span<char> buf = stackalloc char[Math.Min(value.Length, 128)];
        int n = 0;
        foreach (char c in value)
        {
            if (n >= buf.Length) break;
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                buf[n++] = c;
            else if (c >= 'A' && c <= 'Z')
                buf[n++] = (char)(c + ('a' - 'A'));
        }
        return new string(buf[..n]);
    }

    private static string SelectBestPath(List<RunningProcessInfo> procs)
    {
        foreach (var p in procs)
        {
            if (!string.IsNullOrWhiteSpace(p.ExePath))
                return p.ExePath;
        }
        return string.Empty;
    }

    public static bool IsLikelyDaemonExeName(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return false;

        try
        {
            string? dir = Path.GetDirectoryName(exePath);
            string stem = Path.GetFileNameWithoutExtension(exePath);
            if (string.IsNullOrEmpty(stem))
                return false;

            ReadOnlySpan<string> suffixes = ["daemon", "service", "svc", "helper", "agent"];
            for (int i = 0; i < suffixes.Length; i++)
            {
                if (stem.Length > suffixes[i].Length &&
                    stem.EndsWith(suffixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (stem.Length > 2 &&
                stem.EndsWith("d", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(dir) &&
                File.Exists(Path.Combine(dir, stem[..^1] + ".exe")))
            {
                return true;
            }
        }
        catch
        {
            // Ignore malformed paths
        }
        return false;
    }

    public static string? TryResolveCompanionGuiExePath(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return null;

        try
        {
            string? dir = Path.GetDirectoryName(exePath);
            string stem = Path.GetFileNameWithoutExtension(exePath);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(stem))
                return null;

            string? CheckCandidate(string cand)
                => !string.IsNullOrEmpty(cand) &&
                   !string.Equals(cand, exePath, StringComparison.OrdinalIgnoreCase) &&
                   File.Exists(cand)
                    ? cand
                    : null;

            if (stem.Length > 2 && stem.EndsWith("d", StringComparison.OrdinalIgnoreCase))
            {
                if (CheckCandidate(Path.Combine(dir, stem[..^1] + ".exe")) is { } hit)
                    return hit;
            }

            ReadOnlySpan<string> suffixes = ["daemon", "service", "svc", "helper", "agent"];
            for (int i = 0; i < suffixes.Length; i++)
            {
                string sfx = suffixes[i];
                if (stem.Length > sfx.Length && stem.EndsWith(sfx, StringComparison.OrdinalIgnoreCase))
                {
                    string stripped = stem[..^sfx.Length].TrimEnd('-', '_');
                    if (stripped.Length > 0 &&
                        CheckCandidate(Path.Combine(dir, stripped + ".exe")) is { } hit)
                    {
                        return hit;
                    }
                    break;
                }
            }

            string? parentDirName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(parentDirName) &&
                CheckCandidate(Path.Combine(dir, parentDirName + ".exe")) is { } dirHit)
            {
                return dirHit;
            }
        }
        catch
        {
            // Ignore filesystem/path edge cases
        }
        return null;
    }

    private static int[] ExtractPids(List<RunningProcessInfo> procs)
    {
        var pids = new int[procs.Count];
        for (int i = 0; i < procs.Count; i++)
            pids[i] = procs[i].Pid;
        return pids;
    }
}
