using System.Diagnostics;
using Cueway.Interop;
using Cueway.Localization;
using Cueway.Media;

namespace Cueway.Startup;

internal static class ExtensionSetupService
{
    public const string EdgeStoreUrl = "https://microsoftedge.microsoft.com/addons/detail/cueway-companion/cebckhofejojabmcfeinkdclglibafmk";

    public static string GetExtensionRootDir()
    {
        return Path.Combine(AppContext.BaseDirectory, "BrowserExtension");
    }

    public static string GetChromiumExtensionDir()
    {
        return Path.Combine(GetExtensionRootDir(), "Chromium");
    }

    public static string GetFirefoxExtensionDir()
    {
        return Path.Combine(GetExtensionRootDir(), "Firefox");
    }

    public static string GetSignedFirefoxXpiPath()
    {
        return Path.Combine(GetFirefoxExtensionDir(), "cueway-companion.xpi");
    }

    public static bool OpenExtensionFolder()
    {
        string dir = GetExtensionRootDir();
        if (!Directory.Exists(dir))
            return false;

        return LaunchExplorerFolder(dir);
    }

    public static bool OpenSpecificExtensionFolder(bool isFirefox)
    {
        string dir = isFirefox ? GetFirefoxExtensionDir() : GetChromiumExtensionDir();
        if (!Directory.Exists(dir))
            return false;

        return LaunchExplorerFolder(dir);
    }

    public static (bool Success, string Message) CopyExtensionPath(bool isFirefox)
    {
        if (isFirefox)
        {
            string manifestPath = Path.Combine(GetFirefoxExtensionDir(), "manifest.json");
            if (!File.Exists(manifestPath))
                return (false, Strings.MsgBridgeFolderMissing);

            return Win32.TrySetClipboardText(manifestPath)
                ? (true, Strings.MsgPathCopied)
                : (false, Strings.MsgBridgeFolderMissing);
        }

        string extDir = GetChromiumExtensionDir();
        if (!Directory.Exists(extDir) || !File.Exists(Path.Combine(extDir, "manifest.json")))
            return (false, Strings.MsgBridgeFolderMissing);

        return Win32.TrySetClipboardText(extDir)
            ? (true, Strings.MsgPathCopied)
            : (false, Strings.MsgBridgeFolderMissing);
    }

    public static (bool Success, string Message) LaunchEdgeStoreInstall()
    {
        string? edgeExe = FindEdgeBrowser();
        if (!string.IsNullOrEmpty(edgeExe))
        {
            TryLaunchBrowserUrl(edgeExe, EdgeStoreUrl);
            return (true, Strings.MsgBridgeEdgeStoreOpened);
        }

        try
        {
            Process.Start(new ProcessStartInfo(EdgeStoreUrl) { UseShellExecute = true });
            return (true, Strings.MsgBridgeEdgeStoreOpened);
        }
        catch
        {
            return (false, Strings.MsgBridgeEdgeStoreFailed);
        }
    }

    public static (bool Success, string Message) LaunchChromiumGuidedInstall()
    {
        string extDir = GetChromiumExtensionDir();
        if (!Directory.Exists(extDir) || !File.Exists(Path.Combine(extDir, "manifest.json")))
            return (false, Strings.MsgBridgeFolderMissing);

        Win32.TrySetClipboardText(extDir);
        LaunchExplorerFolder(extDir);

        var (browserName, exePath, extUrl) = FindPreferredChromiumBrowser();
        if (!string.IsNullOrEmpty(exePath))
        {
            TryLaunchBrowserUrl(exePath, extUrl);
        }

        return (true, Strings.MsgBridgeChromiumOpened(browserName));
    }

    public static (bool Success, string Message) LaunchFirefoxGuidedInstall()
    {
        string xpiPath = GetSignedFirefoxXpiPath();
        if (File.Exists(xpiPath))
        {
            string? geckoExe = FindPreferredGeckoBrowser();
            if (!string.IsNullOrEmpty(geckoExe))
            {
                TryLaunchBrowserUrl(geckoExe, xpiPath);
            }
            else
            {
                try
                {
                    Process.Start(new ProcessStartInfo(xpiPath) { UseShellExecute = true });
                }
                catch { }
            }

            return (true, Strings.MsgBridgeFirefoxOpened);
        }

        string extDir = GetFirefoxExtensionDir();
        string manifestPath = Path.Combine(extDir, "manifest.json");
        if (!Directory.Exists(extDir) || !File.Exists(manifestPath))
            return (false, Strings.MsgBridgeFolderMissing);

        Win32.TrySetClipboardText(manifestPath);
        LaunchExplorerFolder(extDir);

        string? exePath = FindPreferredGeckoBrowser();
        if (!string.IsNullOrEmpty(exePath))
        {
            TryLaunchBrowserUrl(exePath, "about:debugging#/runtime/this-firefox");
        }

        return (true, Strings.MsgBridgeFirefoxOpened);
    }

    private static string? FindEdgeBrowser()
    {
        var procs = ProcessResolver.TakeSnapshot();
        string? runningEdge = FindRunningExe(procs, "msedge");
        if (!string.IsNullOrEmpty(runningEdge))
            return runningEdge;

        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        return FirstExistingFile(
            Path.Combine(pf86, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(pf, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(localApp, "Microsoft", "Edge", "Application", "msedge.exe"));
    }

    private static (string Name, string? ExePath, string ExtUrl) FindPreferredChromiumBrowser()
    {
        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var procs = ProcessResolver.TakeSnapshot();
        string? runningChrome = FindRunningExe(procs, "chrome");
        if (!string.IsNullOrEmpty(runningChrome))
            return ("Google Chrome", runningChrome, "chrome://extensions");

        string? runningBrave = FindRunningExe(procs, "brave");
        if (!string.IsNullOrEmpty(runningBrave))
            return ("Brave", runningBrave, "brave://extensions");

        string? chromePath = FirstExistingFile(
            Path.Combine(pf, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(pf86, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(localApp, "Google", "Chrome", "Application", "chrome.exe"));
        if (chromePath != null)
            return ("Google Chrome", chromePath, "chrome://extensions");

        string? bravePath = FirstExistingFile(
            Path.Combine(pf, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
            Path.Combine(localApp, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"));
        if (bravePath != null)
            return ("Brave", bravePath, "brave://extensions");

        string? edgePath = FindEdgeBrowser();
        if (edgePath != null)
            return ("Microsoft Edge", edgePath, "edge://extensions");

        return ("Chrome / Chromium", null, string.Empty);
    }

    private static string? FindPreferredGeckoBrowser()
    {
        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var procs = ProcessResolver.TakeSnapshot();
        string? runningZen = FindRunningExe(procs, "zen");
        if (!string.IsNullOrEmpty(runningZen))
            return runningZen;

        string? runningFf = FindRunningExe(procs, "firefox");
        if (!string.IsNullOrEmpty(runningFf))
            return runningFf;

        return FirstExistingFile(
            Path.Combine(pf, "Zen Browser", "zen.exe"),
            Path.Combine(localApp, "Zen Browser", "zen.exe"),
            Path.Combine(pf, "Mozilla Firefox", "firefox.exe"),
            Path.Combine(pf86, "Mozilla Firefox", "firefox.exe"),
            Path.Combine(localApp, "Mozilla Firefox", "firefox.exe"));
    }

    private static string? FindRunningExe(IReadOnlyList<RunningProcessInfo> procs, string procName)
    {
        foreach (var p in procs)
        {
            if (p.Name.Equals(procName, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(p.ExePath) &&
                File.Exists(p.ExePath))
            {
                return p.ExePath;
            }
        }
        return null;
    }

    private static string? FirstExistingFile(params string[] paths)
    {
        foreach (string p in paths)
        {
            if (!string.IsNullOrWhiteSpace(p) && File.Exists(p))
                return p;
        }
        return null;
    }

    private static bool LaunchExplorerFolder(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"")
            {
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryLaunchBrowserUrl(string exePath, string url)
    {
        try
        {
            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false
            };
            psi.ArgumentList.Add(url);
            Process.Start(psi);
        }
        catch
        {
            // Ignore transient browser launch errors
        }
    }
}
