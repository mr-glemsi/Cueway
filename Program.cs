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
using Cueway.Localization;
using Cueway.UI;

namespace Cueway;

internal static unsafe class Program
{
    private const nuint TIMER_REFRESH_POLL = 8001;
    private const nuint TIMER_SLEEP_TICK = 8002;
    private const uint POLL_INTERVAL_VISIBLE_MS = 2000;
    private const uint POLL_INTERVAL_HIDDEN_ACTIVE_MS = 3500;
    private const uint POLL_INTERVAL_HIDDEN_IDLE_MS = 6000;

    [STAThread]
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog("UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        bool startMinimized = args.Any(a =>
            a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-minimized", StringComparison.OrdinalIgnoreCase));
        bool forceShowSettings = args.Any(a =>
            a.Equals("--show-settings", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-show-settings", StringComparison.OrdinalIgnoreCase));
        bool forceShow = forceShowSettings || args.Any(a =>
            a.Equals("--show", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-show", StringComparison.OrdinalIgnoreCase));

        EnsureSingleInstanceCleanly();
        using var instanceMutex = new Mutex(true, @"Local\Cueway_SingleInstance_Mutex", out _);

        int coHr = Win32.CoInitializeEx(0, Win32.COINIT_APARTMENTTHREADED);
        bool coInit = coHr == 0 || coHr == 1;

        nuint gdipToken = 0;
        var gdipInput = new GdiPlus.GdiplusStartupInput { GdiplusVersion = 1 };
        GdiPlus.GdiplusStartup(out gdipToken, &gdipInput, 0);

        try
        {
            using var bridgeServer = new BrowserBridgeServer();
            bridgeServer.Start();
            using var stateStore = new StateStore();
            var startupService = new StartupService();
            using var updateService = new AppUpdateService();
            using var audioService = new CoreAudioService();
            using var sessionManager = new MediaSessionManager(stateStore, audioService, bridgeServer);
            using var engine = new MediaRouterEngine(stateStore, audioService, sessionManager);
            using var iconRenderer = new DynamicIconRenderer();

            using var mainWindow = new MainWindow(stateStore, sessionManager, engine, iconRenderer, startupService, bridgeServer, updateService);
            using var tray = new TrayController(mainWindow.Handle, iconRenderer, stateStore, updateService, () => engine.GetSleepTimerRemaining());
            using var flyout = new TrayFlyoutWindow(stateStore, engine, iconRenderer, () => tray.TryGetIconRect());
            using var osd = new OsdOverlayWindow(iconRenderer);
            using var keyboardHook = new KeyboardHookService(mainWindow.Handle, stateStore, engine);
            mainWindow.ConfigureKeyboardHook(keyboardHook);
            keyboardHook.ToggleWindowRequested += () => mainWindow.BeginInvoke(() => mainWindow.ToggleVisibility());
            using var trayWheelHook = new TrayWheelHookService(stateStore, () => tray.TryGetIconRect(), engine, mainWindow.BeginInvoke);

            bool isWindowVisible = false;
            bool hasLiveSessions = false;
            uint currentPollMs = 0;
            bool sleepTimerActive = false;

            void UpdatePollTimer()
            {
                bool micWatch = stateStore.GetSettingsSnapshot().MicDuckingEnabled;
                uint desired = isWindowVisible
                    ? POLL_INTERVAL_VISIBLE_MS
                    : (hasLiveSessions ? (micWatch ? POLL_INTERVAL_VISIBLE_MS : POLL_INTERVAL_HIDDEN_ACTIVE_MS) : POLL_INTERVAL_HIDDEN_IDLE_MS);
                if (desired != currentPollMs && mainWindow.Handle != 0)
                {
                    currentPollMs = desired;
                    Win32.SetTimer(mainWindow.Handle, TIMER_REFRESH_POLL, desired, 0);
                }
            }

            void ShutdownApp()
            {
                Win32.KillTimer(mainWindow.Handle, TIMER_REFRESH_POLL);
                Win32.KillTimer(mainWindow.Handle, TIMER_SLEEP_TICK);
                Win32.PostQuitMessage(0);
            }

            mainWindow.ExitRequested += ShutdownApp;
            mainWindow.VisibilityChanged += visible =>
            {
                isWindowVisible = visible;
                if (visible)
                    flyout.Hide();
                UpdatePollTimer();
            };

            mainWindow.ConfigureHostHooks(
                onTimer: timerId =>
                {
                    if (timerId == TIMER_REFRESH_POLL)
                    {
                        _ = sessionManager.RefreshAsync();
                        return true;
                    }
                    if (timerId == TIMER_SLEEP_TICK)
                    {
                        engine.TickSleepTimer();
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

            tray.ToggleFlyoutRequested += () =>
            {
                _ = sessionManager.RefreshAsync();
                flyout.Toggle();
            };
            tray.ShowMainWindowRequested += () =>
            {
                flyout.Hide();
                mainWindow.ShowAndFocus();
            };
            tray.OpenSettingsRequested += () =>
            {
                flyout.Hide();
                mainWindow.ShowAndFocus(settingsTab: true);
            };
            tray.OpenUpdateRequested += () =>
            {
                flyout.Hide();
                mainWindow.ShowAndFocus();
            };
            flyout.OpenDashboardRequested += () => mainWindow.ShowAndFocus(settingsTab: false);
            flyout.OpenSettingsRequested += () => mainWindow.ShowAndFocus(settingsTab: true);
            tray.ToggleManualLockRequested += () => engine.ToggleManualLockOnActiveOrSelected();
            tray.SleepTimerSelected += mins => engine.StartSleepTimer(mins);
            tray.SleepTimerCancelRequested += () => engine.CancelSleepTimer();
            tray.ExitRequested += ShutdownApp;

            stateStore.StateChanged += () => mainWindow.BeginInvoke(() => UpdatePollTimer());
            audioService.OutputDevicesChanged += () => mainWindow.BeginInvoke(() =>
            {
                flyout.RefreshAudioDevices();
                mainWindow.RefreshOutputDevices();
            });
            engine.SleepTimerChanged += () => mainWindow.BeginInvoke(() =>
            {
                bool active = engine.IsSleepTimerActive;
                if (active != sleepTimerActive && mainWindow.Handle != 0)
                {
                    sleepTimerActive = active;
                    if (active)
                        Win32.SetTimer(mainWindow.Handle, TIMER_SLEEP_TICK, 1000, 0);
                    else
                        Win32.KillTimer(mainWindow.Handle, TIMER_SLEEP_TICK);
                }
                tray.Update(engine.GetSnapshot());
                mainWindow.RefreshVisuals(clearIcons: false);
            });

            engine.StateChanged += snap => mainWindow.BeginInvoke(() =>
            {
                tray.Update(snap);
                if (snap.Sessions.Count > 0)
                    iconRenderer.WarmupIconsAsync(snap.Sessions.Select(s => ((string?)s.AppKey, (string?)s.ExePath)));
                bool live = snap.Sessions.Count > 0;
                if (live != hasLiveSessions)
                {
                    hasLiveSessions = live;
                    UpdatePollTimer();
                }
            });
            engine.OsdRequested += msg => mainWindow.BeginInvoke(() => osd.ShowMessage(msg));
            sessionManager.SiteIconReceived += (siteKey, iconDataUrl) =>
            {
                if (iconRenderer.CacheSiteIconFromDataUrl(siteKey, iconDataUrl))
                {
                    mainWindow.BeginInvoke(() =>
                    {
                        tray.ForceRefresh(engine.GetSnapshot());
                        mainWindow.InvalidateIconCache();
                        flyout.InvalidateIconCache();
                        iconRenderer.FlushRetiredIcons();
                        mainWindow.RefreshVisuals(clearIcons: false);
                        flyout.RefreshVisuals();
                    });
                }
            };
            updateService.UpdateAvailableNotified += ver => mainWindow.BeginInvoke(() =>
            {
                if (!Win32.IsWindowVisible(mainWindow.Handle))
                    osd.ShowMessage(new OsdMessage(Strings.OsdUpdateTitle, Strings.OsdUpdateBody(ver), null));
            });

            // Start remaining services
            audioService.StartEndpointNotifications();
            tray.AddOrRestore(engine.GetSnapshot());
            keyboardHook.Start();
            trayWheelHook.Start();

            bool showAtStart = forceShow || (!startMinimized && !stateStore.GetSettingsSnapshot().StartMinimized);
            isWindowVisible = showAtStart;
            UpdatePollTimer();
            _ = sessionManager.InitializeAsync();
            _ = startupService.RefreshScheduledTaskStatusAsync();
            if (!showAtStart)
            {
                _ = Task.Delay(6000).ContinueWith(static _ => Win32.TrimWorkingSet());
            }

#if DEBUG
            string? previewArg = args.FirstOrDefault(a =>
                a.StartsWith("--preview-update=", StringComparison.OrdinalIgnoreCase));
            if (previewArg != null)
            {
                updateService.ApplyDebugPreview(previewArg["--preview-update=".Length..]);
            }
            else if (stateStore.GetSettingsSnapshot().AutoCheckUpdates)
            {
                _ = updateService.CheckForUpdatesAsync(silent: true, initialDelayMs: showAtStart ? 1200 : 5000);
            }
#else
            if (stateStore.GetSettingsSnapshot().AutoCheckUpdates)
            {
                _ = updateService.CheckForUpdatesAsync(silent: true, initialDelayMs: showAtStart ? 1200 : 5000);
            }
#endif

            if (showAtStart)
            {
                mainWindow.ShowAndFocus(settingsTab: forceShowSettings);
            }

            // Win32 STA message loop
            var msg = new Win32.MSG();
            int gm;
            while ((gm = Win32.GetMessageW(&msg, 0, 0, 0)) > 0)
            {
                try
                {
                    Win32.TranslateMessage(&msg);
                    Win32.DispatchMessageW(&msg);
                }
                catch (Exception ex)
                {
                    WriteCrashLog("MessageLoop", ex);
                }
            }
            if (gm == -1)
            {
                WriteCrashLog("GetMessageW(-1)", null);
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

    private static void WriteCrashLog(string source, Exception? ex)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Cueway");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "crash.log");
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{source}] {ex?.ToString() ?? "Unknown error"}{Environment.NewLine}{Environment.NewLine}";
            if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024)
            {
                File.WriteAllText(path, entry);
            }
            else
            {
                File.AppendAllText(path, entry);
            }
        }
        catch
        {
            // Ignore logging errors during shutdown/crash
        }
    }

    private static void EnsureSingleInstanceCleanly()
    {
        Mutex? startupMutex = null;
        bool mutexAcquired = false;
        try
        {
            startupMutex = new Mutex(false, @"Local\Cueway_Startup_Mutex");
            try
            {
                mutexAcquired = startupMutex.WaitOne(3000);
            }
            catch (AbandonedMutexException)
            {
                mutexAcquired = true;
            }
        }
        catch
        {
            startupMutex?.Dispose();
            startupMutex = null;
        }

        try
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

            // Ask any previous Cueway (or legacy WinMediaRouter) window to exit gracefully (restores ducked audio & removes tray icon)
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
        finally
        {
            if (mutexAcquired)
            {
                try { startupMutex?.ReleaseMutex(); }
                catch { }
            }
            startupMutex?.Dispose();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int NotifyOldWindowProc(nint hwnd, nint lParam)
    {
        try
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
        }
        catch { }
        return 1;
    }
}
