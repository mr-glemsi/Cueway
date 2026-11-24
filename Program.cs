using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cueway.Audio;
using Cueway.Config;
using Cueway.Input;
using Cueway.Interop;
using Cueway.Media;
using Cueway.Routing;
using Cueway.Startup;
using Cueway.UI;

namespace Cueway;

internal static unsafe class Program
{
    private const nuint TIMER_REFRESH_POLL = 8001;
    private const uint POLL_INTERVAL_VISIBLE_MS = 2000;
    private const uint POLL_INTERVAL_HIDDEN_MS = 3500;

    [STAThread]
    private static int Main(string[] args)
    {
        bool startMinimized = args.Any(a =>
            a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-minimized", StringComparison.OrdinalIgnoreCase));

        EnsureSingleInstanceCleanly();
        using var instanceMutex = new Mutex(true, @"Local\Cueway_SingleInstance_Mutex", out _);

        int coHr = Win32.CoInitializeEx(0, Win32.COINIT_APARTMENTTHREADED);
        bool coInit = coHr == 0 || coHr == 1;

        nuint gdipToken = 0;
        var gdipInput = new GdiPlus.GdiplusStartupInput { GdiplusVersion = 1 };
        GdiPlus.GdiplusStartup(out gdipToken, &gdipInput, 0);

        try
        {
            var stateStore = new StateStore();
            var startupService = new StartupService();
            using var audioService = new CoreAudioService();
            using var bridgeServer = new BrowserBridgeServer();
            using var sessionManager = new MediaSessionManager(stateStore, audioService, bridgeServer);
            using var engine = new MediaRouterEngine(stateStore, audioService, sessionManager);
            using var iconRenderer = new DynamicIconRenderer();

            using var mainWindow = new MainWindow(stateStore, sessionManager, engine, iconRenderer, startupService, bridgeServer);
            using var tray = new TrayController(mainWindow.Handle, iconRenderer);
            using var osd = new OsdOverlayWindow(iconRenderer);
            using var keyboardHook = new KeyboardHookService(mainWindow.Handle, stateStore, engine);
            using var trayWheelHook = new TrayWheelHookService(stateStore, () => tray.TryGetIconRect(), engine, mainWindow.BeginInvoke);

            void ShutdownApp()
            {
                Win32.KillTimer(mainWindow.Handle, TIMER_REFRESH_POLL);
                Win32.PostQuitMessage(0);
            }

            mainWindow.ExitRequested += ShutdownApp;
            mainWindow.VisibilityChanged += visible =>
            {
                Win32.SetTimer(
                    mainWindow.Handle,
                    TIMER_REFRESH_POLL,
                    visible ? POLL_INTERVAL_VISIBLE_MS : POLL_INTERVAL_HIDDEN_MS,
                    0);
            };

            mainWindow.ConfigureHostHooks(
                onTimer: timerId =>
                {
                    if (timerId == TIMER_REFRESH_POLL)
                    {
                        _ = sessionManager.RefreshAsync();
                        return true;
                    }
                    keyboardHook.HandleTimer(timerId);
                    return false;
                },
                onWakeOrUnlock: () =>
                {
                    keyboardHook.OnSystemWakeOrUnlock();
                    trayWheelHook.OnSystemWakeOrUnlock();
                    _ = sessionManager.RefreshAsync();
                },
                onTaskbarCreated: () =>
                {
                    trayWheelHook.InvalidateRectCache();
                    tray.AddOrRestore(engine.GetSnapshot());
                },
                onTrayMessage: (wp, lp) => tray.HandleTrayMessage(wp, lp, engine.GetSnapshot()),
                onRefreshHooks: () =>
                {
                    keyboardHook.ForceRefreshHook();
                    trayWheelHook.OnSystemWakeOrUnlock();
                });

            tray.ShowMainWindowRequested += () => mainWindow.ShowAndFocus();
            tray.ToggleMainWindowRequested += () => mainWindow.ToggleVisibility();
            tray.OpenSettingsRequested += () => mainWindow.ShowAndFocus(settingsTab: true);
            tray.ToggleManualLockRequested += () => engine.ToggleManualLockOnActiveOrSelected();
            tray.ToggleSoloModeRequested += () => engine.ToggleSoloMode();
            tray.ExitRequested += ShutdownApp;

            engine.StateChanged += snap => mainWindow.BeginInvoke(() => tray.Update(snap));
            engine.OsdRequested += msg => mainWindow.BeginInvoke(() => osd.ShowMessage(msg));

            // Start services
            bridgeServer.Start();
            audioService.StartEndpointNotifications();
            tray.AddOrRestore(engine.GetSnapshot());
            keyboardHook.Start();
            trayWheelHook.Start();

            bool showAtStart = !startMinimized && !stateStore.GetSettingsSnapshot().StartMinimized;
            Win32.SetTimer(
                mainWindow.Handle,
                TIMER_REFRESH_POLL,
                showAtStart ? POLL_INTERVAL_VISIBLE_MS : POLL_INTERVAL_HIDDEN_MS,
                0);
            _ = sessionManager.InitializeAsync();
            _ = startupService.RefreshScheduledTaskStatusAsync();

            if (showAtStart)
            {
                mainWindow.ShowAndFocus();
            }

            // Win32 STA message loop
            var msg = new Win32.MSG();
            while (Win32.GetMessageW(&msg, 0, 0, 0) > 0)
            {
                Win32.TranslateMessage(&msg);
                Win32.DispatchMessageW(&msg);
            }
        }
        finally
        {
            if (gdipToken != 0)
                GdiPlus.GdiplusShutdown(gdipToken);
            if (coInit)
                Win32.CoUninitialize();
        }

        return 0;
    }

    private static void EnsureSingleInstanceCleanly()
    {
        int currentPid = Environment.ProcessId;
        string currentName;
        using (var self = Process.GetCurrentProcess())
            currentName = self.ProcessName;

        Process[] existing;
        try
        {
            existing = Process.GetProcessesByName(currentName);
        }
        catch
        {
            return;
        }

        var others = new List<Process>();
        foreach (var p in existing)
        {
            if (p.Id != currentPid)
                others.Add(p);
            else
                p.Dispose();
        }

        if (others.Count == 0)
            return;

        // Ask any previous Cueway (or legacy WinMediaRouter) window to exit gracefully (restores Solo audio & removes tray icon)
        Win32.EnumWindows(&NotifyOldWindowProc, 0);

        var deadline = Environment.TickCount64 + 1200;
        foreach (var proc in others)
        {
            try
            {
                int waitMs = (int)Math.Max(50, deadline - Environment.TickCount64);
                if (!proc.WaitForExit(waitMs))
                {
                    proc.Kill();
                    proc.WaitForExit(500);
                }
            }
            catch { }
            finally
            {
                proc.Dispose();
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int NotifyOldWindowProc(nint hwnd, nint lParam)
    {
        string cls = Win32.GetWindowClassName(hwnd);
        if (cls == "Cueway_MainWindow" || cls == "WinMediaRouter_MainWindow")
        {
            Win32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != 0 && pid != Environment.ProcessId)
            {
                Win32.PostMessageW(hwnd, MainWindow.WM_APP_SHUTDOWN_INSTANCE, 0, 0);
            }
        }
        return 1;
    }
}
