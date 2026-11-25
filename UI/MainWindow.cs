using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cueway.Config;
using Cueway.Input;
using Cueway.Interop;
using Cueway.Localization;
using Cueway.Media;
using Cueway.Routing;
using Cueway.Startup;

namespace Cueway.UI;

internal sealed unsafe class MainWindow : IDisposable
{
    private const string ClassName = "Cueway_MainWindow";
    public const uint WM_APP_DISPATCH = Win32.WM_APP + 200;
    public const uint WM_APP_SHUTDOWN_INSTANCE = Win32.WM_APP + 201;
    private const nuint TIMER_UI_ANIM = 8050;
    private const uint UI_ANIM_INTERVAL_MS = 16;
    private const int UI_HIGHLIGHT_DURATION_MS = 900;
    private const int UI_SLIDE_DURATION_MS = 180;

    private enum TabKind
    {
        Dashboard = 0,
        Settings = 1
    }

    private enum DropdownKind
    {
        None = 0,
        Language = 1,
        Theme = 2,
        TaskInstall = 3
    }

    private sealed record HitRegion(
        Win32.RECT Bounds,
        string Id,
        Action OnClick,
        bool IsButton = true);

    private sealed record SliderRegion(
        Win32.RECT Bounds,
        string Id,
        string SessionId,
        int TrackLeft,
        int TrackWidth);

    private static MainWindow? _instance;

    private readonly StateStore _stateStore;
    private readonly MediaSessionManager _sessionManager;
    private readonly MediaRouterEngine _engine;
    private readonly DynamicIconRenderer _iconRenderer;
    private readonly StartupService _startupService;
    private readonly BrowserBridgeServer _bridgeServer;
    private readonly AppUpdateService _updateService;
    private ExtensionGuideWindow? _guideWindow;

    private readonly object _dispatchLock = new();
    private readonly Queue<Action> _uiQueue = new();

    private nint _hwnd;
    private nint _cursorArrow;
    private nint _cursorHand;
    private nint _backDc;
    private nint _backBmp;
    private nint _backOldBmp;
    private int _backW;
    private int _backH;
    private bool _uiDataDirty;
    private nint _windowBigIcon;
    private nint _windowSmallIcon;
    private uint _dpi = 96;
    private int _clientW = 860;
    private int _clientH = 680;
    private int _scrollY;
    private int _contentTotalH;
    private int _bodyClipTop;
    private bool _trackingMouse;
    private string? _hoverId;
    private TabKind _activeTab = TabKind.Dashboard;
    private DropdownKind _openDropdown = DropdownKind.None;
    private Win32.RECT _openDropdownAnchor;
    private string? _statusBanner;
    private bool _statusBannerIsError;
    private long _statusBannerUntilTick;
    private bool _taskOpBusy;
    private KeyboardHookService? _keyboardHook;
    private string? _capturingTargetId;
    private string? _capturingHitId;
    private string? _capturingPreview;

    private readonly List<HitRegion> _hitRegions = [];
    private readonly List<SliderRegion> _sliderRegions = [];
    private readonly Dictionary<(int PtSize, int Weight, string Face, uint Dpi), nint> _fontCache = new();
    private string? _lastWinIconAppKey;
    private string? _lastWinIconExePath;
    private bool _lastWinIconPlaying;
    private bool _lastWinIconMuted;
    private bool _lastWinIconSolo;
    private string? _lastWinIconThemeId;
    private bool _windowIconInitialized;
    private bool _windowIconDirty;
    private string? _draggingSliderId;
    private string? _draggingSessionId;
    private int _dragTrackLeft;
    private int _dragTrackWidth;
    private float _dragPreviewVolume;
    private long _lastSliderDispatchTick;
    private RouterStateSnapshot _snapshot;
    private AppSettings _settings;
    private List<DiscoveredApp> _priorityApps;
    private UpdateSnapshot _updateSnapshot;

    private string? _highlightPrioAppKey;
    private int _highlightPrioDir;
    private long _highlightPrioStartTick;
    private string? _highlightTargetSessionId;
    private long _highlightTargetStartTick;
    private bool _uiAnimTimerActive;

    private Func<nuint, bool>? _timerCallback;
    private Action? _wakeOrUnlockCallback;
    private Action? _taskbarCreatedCallback;
    private Action<nuint, nint>? _trayMessageCallback;
    private Action? _refreshHooksCallback;
    private uint _wmTaskbarCreated;
    private bool _disposed;

    public nint Handle => _hwnd;
    public event Action<bool>? VisibilityChanged;
    public event Action? ExitRequested;

    public MainWindow(
        StateStore stateStore,
        MediaSessionManager sessionManager,
        MediaRouterEngine engine,
        DynamicIconRenderer iconRenderer,
        StartupService startupService,
        BrowserBridgeServer bridgeServer,
        AppUpdateService updateService)
    {
        _stateStore = stateStore;
        _sessionManager = sessionManager;
        _engine = engine;
        _iconRenderer = iconRenderer;
        _startupService = startupService;
        _bridgeServer = bridgeServer;
        _updateService = updateService;
        _snapshot = _engine.GetSnapshot();
        _settings = _stateStore.GetSettingsSnapshot();
        _priorityApps = _stateStore.GetPriorityAppsSnapshot();
        _updateSnapshot = _updateService.GetSnapshot();
        _instance = this;

        _wmTaskbarCreated = Win32.RegisterWindowMessageW("TaskbarCreated");
        CreateHostWindow();

        _engine.StateChanged += OnEngineStateChanged;
        _stateStore.StateChanged += OnStoreStateChanged;
        _startupService.StatusChanged += OnStartupStatusChanged;
        _bridgeServer.SessionsChanged += OnBridgeStatusChanged;
        _updateService.StateChanged += OnUpdateStateChanged;
    }

    public void ConfigureHostHooks(
        Func<nuint, bool> onTimer,
        Action onWakeOrUnlock,
        Action onTaskbarCreated,
        Action<nuint, nint> onTrayMessage,
        Action? onRefreshHooks = null)
    {
        _timerCallback = onTimer;
        _wakeOrUnlockCallback = onWakeOrUnlock;
        _taskbarCreatedCallback = onTaskbarCreated;
        _trayMessageCallback = onTrayMessage;
        _refreshHooksCallback = onRefreshHooks;
    }

    public void ConfigureKeyboardHook(KeyboardHookService keyboardHook)
    {
        _keyboardHook = keyboardHook;
    }

    private void StartHotkeyCapture(string targetId, string hitId, Action<string> onCommit, bool allowAnySingleKey = false)
    {
        if (_keyboardHook == null)
            return;

        if (string.Equals(_capturingTargetId, targetId, StringComparison.Ordinal))
        {
            CancelHotkeyCapture();
            return;
        }

        _openDropdown = DropdownKind.None;
        _capturingTargetId = targetId;
        _capturingHitId = hitId;
        _capturingPreview = null;

        _keyboardHook.BeginHotkeyCapture(
            onResult: result => BeginInvoke(() =>
            {
                if (!string.Equals(_capturingTargetId, targetId, StringComparison.Ordinal))
                    return;

                _capturingTargetId = null;
                _capturingHitId = null;
                _capturingPreview = null;

                if (result != null)
                    onCommit(result);

                RequestRepaint();
            }),
            onPreview: preview => BeginInvoke(() =>
            {
                if (!string.Equals(_capturingTargetId, targetId, StringComparison.Ordinal))
                    return;

                _capturingPreview = string.IsNullOrEmpty(preview) ? null : preview;
                RequestRepaint();
            }),
            onNeedsModifier: () => BeginInvoke(() =>
            {
                if (!string.Equals(_capturingTargetId, targetId, StringComparison.Ordinal))
                    return;

                _capturingPreview = null;
                ShowStatusBanner(Strings.MsgHotkeyNeedsModifier, isError: true);
            }),
            allowAnySingleKey: allowAnySingleKey);

        RequestRepaint();
    }

    private void CancelHotkeyCapture()
    {
        bool wasCapturing = _capturingTargetId != null;
        _capturingTargetId = null;
        _capturingHitId = null;
        _capturingPreview = null;
        _keyboardHook?.CancelHotkeyCapture();
        if (wasCapturing)
            RequestRepaint();
    }

    private void CreateHostWindow()
    {
        _cursorArrow = Win32.LoadCursorW(0, Win32.IDC_ARROW);
        _cursorHand = Win32.LoadCursorW(0, Win32.IDC_HAND);
        nint hInst = Win32.GetModuleHandleW(null);
        fixed (char* cls = ClassName)
        {
            var wc = new Win32.WNDCLASSEXW
            {
                cbSize = (uint)sizeof(Win32.WNDCLASSEXW),
                style = Win32.CS_HREDRAW | Win32.CS_VREDRAW | Win32.CS_DBLCLKS,
                lpfnWndProc = &WndProc,
                hInstance = hInst,
                hCursor = _cursorArrow,
                lpszClassName = cls
            };
            Win32.RegisterClassExW(&wc);
        }

        // Initial DPI from primary monitor cursor
        Win32.GetCursorPos(out var pt);
        nint hMon = Win32.MonitorFromPoint(pt, Win32.MONITOR_DEFAULTTONEAREST);
        var mi = new Win32.MONITORINFO { cbSize = (uint)sizeof(Win32.MONITORINFO) };
        Win32.GetMonitorInfoW(hMon, &mi);

        int initW = 880;
        int initH = 700;
        int x = mi.rcWork.Left + Math.Max(0, (mi.rcWork.Width - initW) / 2);
        int y = mi.rcWork.Top + Math.Max(0, (mi.rcWork.Height - initH) / 2);

        _hwnd = Win32.CreateWindowExW(
            Win32.WS_EX_APPWINDOW,
            ClassName,
            Strings.WindowTitle,
            Win32.WS_OVERLAPPEDWINDOW | Win32.WS_CLIPCHILDREN,
            x, y, initW, initH,
            0, 0, hInst, 0);

        if (_hwnd != 0)
        {
            // Allow standard-privilege restarted instances to signal an elevated instance cleanly
            Win32.ChangeWindowMessageFilterEx(_hwnd, WM_APP_SHUTDOWN_INSTANCE, Win32.MSGFLT_ALLOW, 0);

            uint d = Win32.GetDpiForWindow(_hwnd);
            if (d >= 96) _dpi = d;

            int scaledW = Theme.Scale(880, _dpi);
            int scaledH = Theme.Scale(700, _dpi);
            int cx = mi.rcWork.Left + Math.Max(0, (mi.rcWork.Width - scaledW) / 2);
            int cy = mi.rcWork.Top + Math.Max(0, (mi.rcWork.Height - scaledH) / 2);
            Win32.SetWindowPos(_hwnd, 0, cx, cy, scaledW, scaledH, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);

            ApplyDwmTheme();
            ApplyAlwaysOnTop(_settings.AlwaysOnTop);
            Win32.WTSRegisterSessionNotification(_hwnd, Win32.NOTIFY_FOR_THIS_SESSION);
            UpdateWindowIcon(_snapshot);
        }
    }

    private void ApplyAlwaysOnTop(bool topMost)
    {
        if (_hwnd == 0) return;
        nint insertAfter = topMost ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST;
        Win32.SetWindowPos(_hwnd, insertAfter, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    private void ShowStatusBanner(string text, bool isError = false, int durationMs = 4500)
    {
        _statusBanner = text;
        _statusBannerIsError = isError;
        _statusBannerUntilTick = Environment.TickCount64 + durationMs;
        RequestRepaint();
    }

    private void ApplyDwmTheme()
    {
        if (_hwnd == 0) return;
        int dark = Theme.IsLight ? 0 : 1;
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(int));
        int corner = Win32.DWMWCP_ROUND;
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(int));
        uint captionColor = Theme.ToColorRef(Theme.BgHeader);
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_CAPTION_COLOR, &captionColor, sizeof(uint));
        uint textColor = Theme.ToColorRef(Theme.TextPrimary);
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_TEXT_COLOR, &textColor, sizeof(uint));
        uint borderColor = Theme.ToColorRef(Theme.BorderSubtle);
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_BORDER_COLOR, &borderColor, sizeof(uint));
    }

    public void BeginInvoke(Action action)
    {
        if (_disposed || _hwnd == 0) return;
        lock (_dispatchLock)
            _uiQueue.Enqueue(action);
        Win32.PostMessageW(_hwnd, WM_APP_DISPATCH, 0, 0);
    }

    public void ShowAndFocus(bool settingsTab = false)
    {
        if (_hwnd == 0) return;
        _openDropdown = DropdownKind.None;
        if (settingsTab)
        {
            _activeTab = TabKind.Settings;
            _scrollY = 0;
        }
        if (_uiDataDirty)
        {
            _priorityApps = _stateStore.GetPriorityAppsSnapshot();
            _settings = _stateStore.GetSettingsSnapshot();
            _uiDataDirty = false;
        }
        if (Win32.IsIconic(_hwnd))
            Win32.ShowWindow(_hwnd, Win32.SW_RESTORE);
        else
            Win32.ShowWindow(_hwnd, Win32.SW_SHOW);

        if (_windowIconDirty)
            UpdateWindowIcon(_snapshot, forceVisibleCheck: false);

        Win32.SetForegroundWindow(_hwnd);
        VisibilityChanged?.Invoke(true);
        RequestRepaint();

        if (_settings.AutoCheckUpdates)
        {
            _ = _updateService.CheckIfStaleAsync();
        }
    }

    public void ToggleVisibility()
    {
        if (_hwnd == 0) return;
        if (Win32.IsWindowVisible(_hwnd) && !Win32.IsIconic(_hwnd))
        {
            HideToTray();
        }
        else
        {
            ShowAndFocus();
        }
    }

    private void HideToTray()
    {
        if (_hwnd == 0) return;
        CancelHotkeyCapture();
        StopUiAnimTimer();
        _highlightPrioAppKey = null;
        _highlightPrioDir = 0;
        _highlightPrioStartTick = 0;
        _highlightTargetSessionId = null;
        _highlightTargetStartTick = 0;
        _openDropdown = DropdownKind.None;
        _hoverId = null;
        Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
        ReleaseBackBuffer();
        ClearFontCache();
        _hitRegions.Clear();
        _hitRegions.TrimExcess();
        _sliderRegions.Clear();
        _sliderRegions.TrimExcess();
        VisibilityChanged?.Invoke(false);
        Win32.TrimWorkingSet();
    }

    internal void TriggerPriorityMoveFeedback(string appKey, bool movedUp)
    {
        if (string.IsNullOrWhiteSpace(appKey))
            return;
        _highlightPrioAppKey = appKey;
        _highlightPrioDir = movedUp ? -1 : 1;
        _highlightPrioStartTick = Environment.TickCount64;
        StartUiAnimTimer();
        RequestRepaint();
    }

    internal void TriggerTargetChangeFeedback(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;
        _highlightTargetSessionId = sessionId;
        _highlightTargetStartTick = Environment.TickCount64;
        StartUiAnimTimer();
        RequestRepaint();
    }

    private static float GetHighlightIntensity(long startTick, long now)
    {
        if (startTick == 0)
            return 0f;
        long elapsed = now - startTick;
        if (elapsed < 0 || elapsed >= UI_HIGHLIGHT_DURATION_MS)
            return 0f;
        float t = (float)elapsed / UI_HIGHLIGHT_DURATION_MS;
        float remain = 1f - t;
        return remain * remain;
    }

    private int GetSlideOffsetPx(long startTick, int dir, long now)
    {
        if (startTick == 0 || dir == 0)
            return 0;
        long elapsed = now - startTick;
        if (elapsed < 0 || elapsed >= UI_SLIDE_DURATION_MS)
            return 0;
        float t = (float)elapsed / UI_SLIDE_DURATION_MS;
        float inv = 1f - t;
        float ease = inv * inv * inv;
        return (int)MathF.Round(-dir * S(14) * ease);
    }

    private static bool HasActiveUiAnimation(long startPrio, long startTarget, long now)
        => GetHighlightIntensity(startPrio, now) > 0.005f ||
           GetHighlightIntensity(startTarget, now) > 0.005f;

    private void StartUiAnimTimer()
    {
        if (_hwnd != 0 && Win32.IsWindowVisible(_hwnd) && !_uiAnimTimerActive)
        {
            Win32.SetTimer(_hwnd, TIMER_UI_ANIM, UI_ANIM_INTERVAL_MS, 0);
            _uiAnimTimerActive = true;
        }
    }

    private void StopUiAnimTimer()
    {
        if (_uiAnimTimerActive && _hwnd != 0)
        {
            Win32.KillTimer(_hwnd, TIMER_UI_ANIM);
            _uiAnimTimerActive = false;
        }
    }

    private void HandleUiAnimTimer()
    {
        long now = Environment.TickCount64;
        if (!Win32.IsWindowVisible(_hwnd))
        {
            _highlightPrioAppKey = null;
            _highlightPrioDir = 0;
            _highlightPrioStartTick = 0;
            _highlightTargetSessionId = null;
            _highlightTargetStartTick = 0;
            StopUiAnimTimer();
            return;
        }

        if (GetHighlightIntensity(_highlightPrioStartTick, now) <= 0.005f)
        {
            _highlightPrioAppKey = null;
            _highlightPrioDir = 0;
            _highlightPrioStartTick = 0;
        }
        if (GetHighlightIntensity(_highlightTargetStartTick, now) <= 0.005f)
        {
            _highlightTargetSessionId = null;
            _highlightTargetStartTick = 0;
        }

        if (!HasActiveUiAnimation(_highlightPrioStartTick, _highlightTargetStartTick, now))
        {
            StopUiAnimTimer();
        }
        RequestRepaint();
    }

    private void OnEngineStateChanged(RouterStateSnapshot snap)
    {
        BeginInvoke(() =>
        {
            string? prevTargetId = _snapshot.ActiveTarget?.SessionId;
            _snapshot = snap;
            if (!Win32.IsWindowVisible(_hwnd))
            {
                _uiDataDirty = true;
                UpdateWindowIcon(snap);
                return;
            }
            _priorityApps = _stateStore.GetPriorityAppsSnapshot();
            _settings = _stateStore.GetSettingsSnapshot();
            _uiDataDirty = false;
            if (snap.ActiveTarget != null &&
                !string.Equals(prevTargetId, snap.ActiveTarget.SessionId, StringComparison.Ordinal))
            {
                TriggerTargetChangeFeedback(snap.ActiveTarget.SessionId);
            }
            UpdateWindowIcon(snap);
            RequestRepaint();
        });
    }

    private void OnStoreStateChanged()
    {
        BeginInvoke(() =>
        {
            var prevPriorityApps = _priorityApps;
            _priorityApps = _stateStore.GetPriorityAppsSnapshot();
            var nextSettings = _stateStore.GetSettingsSnapshot();
            bool topChanged = _settings.AlwaysOnTop != nextSettings.AlwaysOnTop;
            bool langChanged = !string.Equals(_settings.Language, nextSettings.Language, StringComparison.OrdinalIgnoreCase);
            bool themeChanged = !string.Equals(_settings.Theme, nextSettings.Theme, StringComparison.OrdinalIgnoreCase);
            _settings = nextSettings;
            if (Win32.IsWindowVisible(_hwnd) &&
                prevPriorityApps.Count > 1 &&
                _priorityApps.Count == prevPriorityApps.Count &&
                !string.Equals(prevPriorityApps[0].AppKey, _priorityApps[0].AppKey, StringComparison.OrdinalIgnoreCase) &&
                Environment.TickCount64 - _highlightPrioStartTick > 120)
            {
                TriggerPriorityMoveFeedback(_priorityApps[0].AppKey, movedUp: true);
            }
            if (topChanged)
                ApplyAlwaysOnTop(_settings.AlwaysOnTop);
            if (themeChanged)
            {
                ApplyDwmTheme();
                UpdateWindowIcon(_snapshot);
                _guideWindow?.RefreshLocalizationAndTheme();
            }
            if (langChanged && _hwnd != 0)
            {
                ClearFontCache();
                Win32.SetWindowTextW(_hwnd, Strings.WindowTitle);
                _guideWindow?.RefreshLocalizationAndTheme();
                _engine.Reevaluate();
            }
            RequestRepaint();
        });
    }

    private void OnStartupStatusChanged()
    {
        if (_disposed || _hwnd == 0 || !Win32.IsWindowVisible(_hwnd))
            return;
        BeginInvoke(RequestRepaint);
    }

    private void OnBridgeStatusChanged()
    {
        if (_disposed || _hwnd == 0 || !Win32.IsWindowVisible(_hwnd))
            return;
        BeginInvoke(RequestRepaint);
    }

    private void OnUpdateStateChanged(UpdateSnapshot snap)
    {
        BeginInvoke(() =>
        {
            _updateSnapshot = snap;
            ClampScroll();
            RequestRepaint();
        });
    }

    private void UpdateWindowIcon(RouterStateSnapshot snap, bool forceVisibleCheck = true)
    {
        if (_hwnd == 0) return;

        if (forceVisibleCheck && _windowIconInitialized && !Win32.IsWindowVisible(_hwnd))
        {
            _windowIconDirty = true;
            return;
        }

        var target = snap.ActiveTarget;
        string? appKey = target?.AppKey;
        string? exePath = target?.ExePath;
        bool isPlaying = target?.IsPlaying ?? false;
        bool isMuted = target?.IsMuted ?? false;
        bool soloMode = snap.SoloModeEnabled;
        string themeId = Theme.CurrentId;

        if (_windowIconInitialized &&
            _lastWinIconPlaying == isPlaying &&
            _lastWinIconMuted == isMuted &&
            _lastWinIconSolo == soloMode &&
            string.Equals(_lastWinIconAppKey, appKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_lastWinIconExePath, exePath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_lastWinIconThemeId, themeId, StringComparison.OrdinalIgnoreCase))
        {
            _windowIconDirty = false;
            return;
        }

        nint big = _iconRenderer.CreateCompositeIcon(appKey, exePath, isPlaying, isMuted, soloMode, size: 48);
        nint small = _iconRenderer.CreateCompositeIcon(appKey, exePath, isPlaying, isMuted, soloMode, size: 32);

        if (big != 0)
        {
            Win32.SendMessageW(_hwnd, Win32.WM_SETICON, Win32.ICON_BIG, big);
            if (_windowBigIcon != 0) Win32.DestroyIcon(_windowBigIcon);
            _windowBigIcon = big;
        }
        if (small != 0)
        {
            Win32.SendMessageW(_hwnd, Win32.WM_SETICON, Win32.ICON_SMALL, small);
            if (_windowSmallIcon != 0) Win32.DestroyIcon(_windowSmallIcon);
            _windowSmallIcon = small;
        }

        _lastWinIconAppKey = appKey;
        _lastWinIconExePath = exePath;
        _lastWinIconPlaying = isPlaying;
        _lastWinIconMuted = isMuted;
        _lastWinIconSolo = soloMode;
        _lastWinIconThemeId = themeId;
        _windowIconInitialized = true;
        _windowIconDirty = false;
    }

    private void RequestRepaint()
    {
        if (_hwnd != 0 && Win32.IsWindowVisible(_hwnd))
            Win32.InvalidateRect(_hwnd, null, false);
    }

    private void DrainDispatchQueue()
    {
        while (true)
        {
            Action? next = null;
            lock (_dispatchLock)
            {
                if (_uiQueue.Count > 0)
                    next = _uiQueue.Dequeue();
            }
            if (next == null) break;
            try { next(); } catch { }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        var inst = _instance;
        if (inst == null)
            return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);

        if (msg == inst._wmTaskbarCreated && inst._wmTaskbarCreated != 0)
        {
            inst._taskbarCreatedCallback?.Invoke();
            return 0;
        }

        switch (msg)
        {
            case WM_APP_DISPATCH:
                inst.DrainDispatchQueue();
                return 0;

            case WM_APP_SHUTDOWN_INSTANCE:
                inst.ExitRequested?.Invoke();
                return 0;

            case TrayController.WM_TRAYICON:
                inst._trayMessageCallback?.Invoke(wParam, lParam);
                return 0;

            case Win32.WM_TIMER:
                if (wParam == TIMER_UI_ANIM)
                {
                    inst.HandleUiAnimTimer();
                    return 0;
                }
                if (inst._timerCallback != null && inst._timerCallback(wParam))
                    return 0;
                break;

            case Win32.WM_POWERBROADCAST:
                if (wParam == Win32.PBT_APMRESUMESUSPEND || wParam == Win32.PBT_APMRESUMEAUTOMATIC)
                {
                    inst._wakeOrUnlockCallback?.Invoke();
                }
                return 1;

            case Win32.WM_WTSSESSION_CHANGE:
                if (wParam == Win32.WTS_SESSION_UNLOCK)
                {
                    inst._wakeOrUnlockCallback?.Invoke();
                }
                return 0;

            case Win32.WM_SIZE:
                inst._clientW = (int)(lParam & 0xFFFF);
                inst._clientH = (int)((lParam >> 16) & 0xFFFF);
                inst.ClampScroll();
                Win32.InvalidateRect(hwnd, null, false);
                return 0;

            case Win32.WM_DPICHANGED:
                inst._dpi = (uint)(wParam & 0xFFFF);
                inst.ClearFontCache();
                var suggested = *(Win32.RECT*)lParam;
                Win32.SetWindowPos(hwnd, 0, suggested.Left, suggested.Top, suggested.Width, suggested.Height,
                    Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
                Win32.InvalidateRect(hwnd, null, false);
                return 0;

            case Win32.WM_GETMINMAXINFO:
                var mmi = (Win32.MINMAXINFO*)lParam;
                mmi->ptMinTrackSize.X = Theme.Scale(720, inst._dpi);
                mmi->ptMinTrackSize.Y = Theme.Scale(540, inst._dpi);
                return 0;

            case Win32.WM_ERASEBKGND:
                return 1; // Suppress GDI background clear to prevent flicker

            case Win32.WM_PAINT:
                inst.HandlePaint(hwnd);
                return 0;

            case Win32.WM_MOUSEMOVE:
                inst.HandleMouseMove(lParam);
                return 0;

            case Win32.WM_MOUSELEAVE:
                inst._trackingMouse = false;
                if (inst._hoverId != null && inst._draggingSliderId == null)
                {
                    inst._hoverId = null;
                    inst.RequestRepaint();
                }
                return 0;

            case Win32.WM_LBUTTONDOWN:
                if (inst.HandleLeftButtonDown(lParam))
                    return 0;
                break;

            case Win32.WM_CAPTURECHANGED:
                if (inst._draggingSliderId != null)
                {
                    inst._draggingSliderId = null;
                    inst._draggingSessionId = null;
                    inst.RequestRepaint();
                }
                return 0;

            case Win32.WM_LBUTTONUP:
                inst.HandleLeftClick(lParam);
                return 0;

            case Win32.WM_MOUSEWHEEL:
                short delta = unchecked((short)((long)wParam >> 16));
                inst.HandleMouseWheel(delta);
                return 0;

            case Win32.WM_ACTIVATE:
                if ((wParam & 0xFFFF) == Win32.WA_INACTIVE)
                    inst.CancelHotkeyCapture();
                break;

            case Win32.WM_KILLFOCUS:
                inst.CancelHotkeyCapture();
                break;

            case 0x0100: // WM_KEYDOWN
                if (wParam == 0x1B && inst._openDropdown != DropdownKind.None) // VK_ESCAPE
                {
                    inst._openDropdown = DropdownKind.None;
                    inst.RequestRepaint();
                    return 0;
                }
                break;

            case Win32.WM_CLOSE:
                if (inst._settings.MinimizeToTrayOnClose)
                {
                    inst.HideToTray();
                }
                else
                {
                    inst.CancelHotkeyCapture();
                    inst._openDropdown = DropdownKind.None;
                    inst.ExitRequested?.Invoke();
                }
                return 0;

            case Win32.WM_DESTROY:
                Win32.PostQuitMessage(0);
                return 0;
        }

        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void HandleMouseMove(nint lParam)
    {
        if (!_trackingMouse)
        {
            var tme = new Win32.TRACKMOUSEEVENT
            {
                cbSize = (uint)sizeof(Win32.TRACKMOUSEEVENT),
                dwFlags = Win32.TME_LEAVE,
                hwndTrack = _hwnd
            };
            Win32.TrackMouseEvent(&tme);
            _trackingMouse = true;
        }

        int x = unchecked((short)(lParam & 0xFFFF));
        int y = unchecked((short)((lParam >> 16) & 0xFFFF));

        if (_draggingSliderId != null && _draggingSessionId != null)
        {
            float vol = Math.Clamp((float)(x - _dragTrackLeft) / Math.Max(1, _dragTrackWidth), 0f, 1f);
            _dragPreviewVolume = vol;
            Win32.SetCursor(_cursorHand != 0 ? _cursorHand : _cursorArrow);

            long nowTick = Environment.TickCount64;
            if (nowTick - _lastSliderDispatchTick >= 35)
            {
                _lastSliderDispatchTick = nowTick;
                _ = _engine.SetSessionVolumeAsync(_draggingSessionId, vol);
            }
            RequestRepaint();
            return;
        }

        var slider = _openDropdown == DropdownKind.None ? FindSliderHit(x, y) : null;
        if (slider != null)
        {
            Win32.SetCursor(_cursorHand != 0 ? _cursorHand : _cursorArrow);
            if (!string.Equals(_hoverId, slider.Id, StringComparison.Ordinal))
            {
                _hoverId = slider.Id;
                RequestRepaint();
            }
            return;
        }

        var hit = FindHit(x, y);
        string? nextHover = hit?.Id;

        Win32.SetCursor(hit != null && hit.IsButton && _cursorHand != 0 ? _cursorHand : _cursorArrow);

        if (!string.Equals(_hoverId, nextHover, StringComparison.Ordinal))
        {
            _hoverId = nextHover;
            RequestRepaint();
        }
    }

    private bool HandleLeftButtonDown(nint lParam)
    {
        int x = unchecked((short)(lParam & 0xFFFF));
        int y = unchecked((short)((lParam >> 16) & 0xFFFF));
        var slider = _openDropdown == DropdownKind.None ? FindSliderHit(x, y) : null;
        if (slider == null)
            return false;

        float vol = Math.Clamp((float)(x - slider.TrackLeft) / Math.Max(1, slider.TrackWidth), 0f, 1f);
        _draggingSliderId = slider.Id;
        _draggingSessionId = slider.SessionId;
        _dragTrackLeft = slider.TrackLeft;
        _dragTrackWidth = slider.TrackWidth;
        _dragPreviewVolume = vol;
        _lastSliderDispatchTick = Environment.TickCount64;

        Win32.SetCapture(_hwnd);
        _ = _engine.SetSessionVolumeAsync(slider.SessionId, vol);
        RequestRepaint();
        return true;
    }

    private void HandleLeftClick(nint lParam)
    {
        int x = unchecked((short)(lParam & 0xFFFF));
        int y = unchecked((short)((lParam >> 16) & 0xFFFF));

        if (_draggingSliderId != null && _draggingSessionId != null)
        {
            float finalVol = Math.Clamp((float)(x - _dragTrackLeft) / Math.Max(1, _dragTrackWidth), 0f, 1f);
            string targetSession = _draggingSessionId;
            _draggingSliderId = null;
            _draggingSessionId = null;
            Win32.ReleaseCapture();
            _ = _engine.SetSessionVolumeAsync(targetSession, finalVol);
            RequestRepaint();
            return;
        }

        var hit = FindHit(x, y);
        if (_capturingTargetId != null && (hit == null || !string.Equals(hit.Id, _capturingHitId, StringComparison.Ordinal)))
        {
            CancelHotkeyCapture();
        }
        if (hit != null)
        {
            hit.OnClick();
            RequestRepaint();
        }
    }

    private void HandleMouseWheel(short delta)
    {
        if (_openDropdown != DropdownKind.None)
        {
            _openDropdown = DropdownKind.None;
            RequestRepaint();
            return;
        }
        int step = Theme.Scale(44, _dpi);
        _scrollY -= (delta / 120) * step;
        ClampScroll();
        RequestRepaint();
    }

    private bool ShouldShowUpdateBar()
    {
        return !_updateSnapshot.Dismissed && _updateSnapshot.Phase is
            UpdatePhase.Available or
            UpdatePhase.Downloading or
            UpdatePhase.ReadyToInstall or
            UpdatePhase.Failed;
    }

    private int GetTopChromeHeight()
        => S(62) + (ShouldShowUpdateBar() ? S(46) : 0);

    private void ClampScroll()
    {
        int topH = GetTopChromeHeight();
        int viewH = Math.Max(1, _clientH - topH);
        int maxScroll = Math.Max(0, _contentTotalH - viewH);
        _scrollY = Math.Clamp(_scrollY, 0, maxScroll);
    }

    private HitRegion? FindHit(int x, int y)
    {
        // Search reverse so topmost/inner buttons win over parent row containers
        for (int i = _hitRegions.Count - 1; i >= 0; i--)
        {
            var r = _hitRegions[i].Bounds;
            if (x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom)
                return _hitRegions[i];
        }
        return null;
    }

    private SliderRegion? FindSliderHit(int x, int y)
    {
        for (int i = _sliderRegions.Count - 1; i >= 0; i--)
        {
            var r = _sliderRegions[i].Bounds;
            if (x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom)
                return _sliderRegions[i];
        }
        return null;
    }

    private int S(int px) => Theme.Scale(px, _dpi);
    private float SF(float px) => Theme.ScaleF(px, _dpi);

    private bool EnsureBackBuffer(nint referenceDc, int w, int h)
    {
        if (_backDc != 0 && _backBmp != 0 && _backW == w && _backH == h)
            return true;

        ReleaseBackBuffer();

        nint memDc = Win32.CreateCompatibleDC(referenceDc);
        if (memDc == 0)
            return false;

        var bmi = new Win32.BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)sizeof(Win32.BITMAPINFOHEADER);
        bmi.bmiHeader.biWidth = w;
        bmi.bmiHeader.biHeight = -h;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;

        nint hBmp = Win32.CreateDIBSection(memDc, &bmi, 0, out _, 0, 0);
        if (hBmp == 0)
        {
            Win32.DeleteDC(memDc);
            return false;
        }

        nint oldBmp = Win32.SelectObject(memDc, hBmp);
        Win32.SetBkMode(memDc, Win32.TRANSPARENT);
        _backDc = memDc;
        _backBmp = hBmp;
        _backOldBmp = oldBmp;
        _backW = w;
        _backH = h;
        return true;
    }

    private void ReleaseBackBuffer()
    {
        if (_backDc != 0)
        {
            if (_backOldBmp != 0)
            {
                Win32.SelectObject(_backDc, _backOldBmp);
                _backOldBmp = 0;
            }
            Win32.DeleteDC(_backDc);
            _backDc = 0;
        }
        if (_backBmp != 0)
        {
            Win32.DeleteObject(_backBmp);
            _backBmp = 0;
        }
        _backW = 0;
        _backH = 0;
    }

    private void HandlePaint(nint hwnd)
    {
        var ps = new Win32.PAINTSTRUCT();
        nint hdc = Win32.BeginPaint(hwnd, &ps);
        if (hdc == 0) return;

        int w = Math.Max(1, _clientW);
        int h = Math.Max(1, _clientH);

        try
        {
            if (!EnsureBackBuffer(hdc, w, h))
                return;

            _hitRegions.Clear();
            _sliderRegions.Clear();
            Win32.SetBkMode(_backDc, Win32.TRANSPARENT);

            if (GdiPlus.GdipCreateFromHDC(_backDc, out nint gfx) == 0 && gfx != 0)
            {
                try
                {
                    GdiPlus.GdipSetSmoothingMode(gfx, GdiPlus.SmoothingModeAntiAlias);
                    GdiPlus.GdipGraphicsClear(gfx, GdiPlus.Argb(Theme.BgWindow));

                    int headerH = S(62);
                    int barH = ShouldShowUpdateBar() ? S(46) : 0;
                    int topChromeH = headerH + barH;
                    _bodyClipTop = topChromeH;
                    DrawScrollableBody(_backDc, gfx, w, h, topChromeH);
                    _bodyClipTop = 0;
                    DrawHeader(_backDc, gfx, w, headerH);
                    if (barH > 0)
                        DrawUpdateBannerBar(_backDc, gfx, w, headerH, barH);
                    if (_activeTab == TabKind.Settings && _openDropdown != DropdownKind.None)
                        DrawOpenDropdownOverlay(_backDc, gfx, w, h, topChromeH);
                }
                finally
                {
                    GdiPlus.GdipDeleteGraphics(gfx);
                }
            }

            Win32.BitBlt(hdc, 0, 0, w, h, _backDc, 0, 0, Win32.SRCCOPY);
        }
        finally
        {
            Win32.EndPaint(hwnd, &ps);
        }
    }

    private void DrawHeader(nint dc, nint gfx, int w, int headerH)
    {
        // Solid header bar painted after body so scrolled content cleanly tucks underneath
        GdiPlus.FillRoundedRect(gfx, 0, 0, w, headerH, 0, GdiPlus.Argb(Theme.BgHeader));
        GdiPlus.FillRoundedRect(gfx, 0, headerH - 1, w, 1, 0, GdiPlus.Argb(Theme.BorderSubtle));

        int padX = S(22);
        // Header icon
        var target = _snapshot.ActiveTarget;
        nint titleIcon = _iconRenderer.GetIconForApp(target?.AppKey, target?.ExePath);
        int iconSz = S(22);
        int iconY = (headerH - iconSz) / 2;
        if (titleIcon != 0)
        {
            Win32.DrawIconEx(dc, padX, iconY, titleIcon, iconSz, iconSz, 0, 0, Win32.DI_NORMAL);
        }
        else
        {
            GdiPlus.FillRoundedRect(gfx, padX, iconY, iconSz, iconSz, SF(6), GdiPlus.Argb(Theme.Accent));
        }

        int titleX = padX + iconSz + S(10);
        DrawText(dc, "Cueway", titleX, 0, titleX + S(120), headerH, 16, Win32.FW_BOLD, Theme.TextPrimary);

        // Right side: Segmented Tab Switcher + Exit button
        int exitW = S(80);
        int tabGroupW = S(264);
        int tabH = S(34);
        int tabY = (headerH - tabH) / 2;
        int exitX = w - padX - exitW;
        int tabGroupX = exitX - S(10) - tabGroupW;

        // Status badges next to title (bounded so they never overlap tab switcher)
        int badgeX = titleX + S(96);
        int badgeMaxRight = tabGroupX - S(10);
        if (_snapshot.SoloModeEnabled && badgeX + S(70) <= badgeMaxRight)
        {
            badgeX += DrawPillBadge(dc, gfx, badgeX, (headerH - S(22)) / 2, Strings.BadgeSoloActive, Theme.Amber, Theme.AmberSoft) + S(8);
        }
        if (_snapshot.GameGuardActive && badgeX + S(80) <= badgeMaxRight)
        {
            badgeX += DrawPillBadge(dc, gfx, badgeX, (headerH - S(22)) / 2, Strings.BadgeGameGuard, Theme.Green, Theme.GreenSoft) + S(8);
        }

        GdiPlus.FillAndStrokeRoundedRect(gfx, tabGroupX, tabY, tabGroupW, tabH, SF(8),
            GdiPlus.Argb(Theme.BgCard), GdiPlus.Argb(Theme.BorderSubtle));

        int halfW = (tabGroupW - S(6)) / 2;
        DrawTabButton(dc, gfx, "tab_dash", Strings.TabDashboard, tabGroupX + S(3), tabY + S(3), halfW, tabH - S(6),
            _activeTab == TabKind.Dashboard, () =>
            {
                CancelHotkeyCapture();
                _openDropdown = DropdownKind.None;
                _activeTab = TabKind.Dashboard;
                _scrollY = 0;
            });
        DrawTabButton(dc, gfx, "tab_set", Strings.TabSettings, tabGroupX + S(3) + halfW, tabY + S(3), halfW, tabH - S(6),
            _activeTab == TabKind.Settings, () =>
            {
                CancelHotkeyCapture();
                _openDropdown = DropdownKind.None;
                _activeTab = TabKind.Settings;
                _scrollY = 0;
            });

        // Subtle Exit button
        DrawActionButton(dc, gfx, "btn_exit", Strings.BtnExit, exitX, tabY, exitW, tabH,
            Theme.BgCard, Theme.BgControlHover, Theme.TextSecondary, Theme.BorderSubtle,
            () => ExitRequested?.Invoke());
    }

    private void DrawUpdateBannerBar(nint dc, nint gfx, int w, int y, int barH)
    {
        var snap = _updateSnapshot;
        int padX = S(22);

        uint bgColor = snap.Phase switch
        {
            UpdatePhase.Available => Theme.AccentSoft,
            UpdatePhase.Downloading => Theme.BgCardElevated,
            UpdatePhase.ReadyToInstall => Theme.GreenSoft,
            _ => Theme.RedSoft
        };
        uint lineColor = snap.Phase switch
        {
            UpdatePhase.ReadyToInstall => Theme.Green,
            UpdatePhase.Failed => Theme.Red,
            _ => Theme.Accent
        };

        GdiPlus.FillRoundedRect(gfx, 0, y, w, barH, 0, GdiPlus.Argb(bgColor));
        if (snap.Phase == UpdatePhase.Available && string.Equals(_hoverId, "upd_bar_click", StringComparison.Ordinal))
        {
            GdiPlus.FillRoundedRect(gfx, 0, y, w, barH, 0, GdiPlus.Argb(Theme.Accent, 24));
        }
        GdiPlus.FillRoundedRect(gfx, 0, y + barH - 1, w, 1, 0, GdiPlus.Argb(lineColor, 130));

        int btnH = S(28);
        int btnY = y + (barH - btnH) / 2;

        if (snap.Phase == UpdatePhase.Available)
        {
            _hitRegions.Add(new HitRegion(
                new Win32.RECT { Left = 0, Top = y, Right = w, Bottom = y + barH },
                "upd_bar_click",
                () => _updateService.StartDownload()));

            int disSz = S(24);
            int disX = w - padX - disSz;
            int dlW = Math.Clamp(MeasureTextWidth(dc, Strings.UpdateBtnDownload, 12, Win32.FW_SEMIBOLD) + S(26), S(96), S(148));
            int dlX = disX - S(8) - dlW;

            int badgeW = Math.Max(S(46), MeasureTextWidth(dc, Strings.UpdateBadgeNew, 10, Win32.FW_BOLD) + S(16));
            int badgeH = S(20);
            int badgeY = y + (barH - badgeH) / 2;
            GdiPlus.FillRoundedRect(gfx, padX, badgeY, badgeW, badgeH, badgeH / 2f, GdiPlus.Argb(Theme.Accent));
            DrawText(dc, Strings.UpdateBadgeNew, padX, badgeY, padX + badgeW, badgeY + badgeH, 10, Win32.FW_BOLD, Theme.OnAccent, centered: true);

            string msg = Strings.UpdateAvailableBanner(snap.LatestVersion);
            DrawText(dc, msg, padX + badgeW + S(10), y, dlX - S(12), y + barH, 12, Win32.FW_SEMIBOLD, Theme.TextPrimary);

            bool dlHovered = string.Equals(_hoverId, "upd_btn_dl", StringComparison.Ordinal);
            GdiPlus.FillAndStrokeRoundedRect(gfx, dlX, btnY, dlW, btnH, SF(7),
                GdiPlus.Argb(dlHovered ? Theme.AccentHover : Theme.Accent),
                GdiPlus.Argb(Theme.Accent));
            DrawText(dc, Strings.UpdateBtnDownload, dlX, btnY, dlX + dlW, btnY + btnH, 12, Win32.FW_SEMIBOLD, Theme.OnAccent, centered: true);
            _hitRegions.Add(new HitRegion(
                new Win32.RECT { Left = dlX, Top = btnY, Right = dlX + dlW, Bottom = btnY + btnH },
                "upd_btn_dl",
                () => _updateService.StartDownload()));

            DrawIconButton(dc, gfx, "upd_btn_dismiss", SmallButtonGlyph.Close, disX, y + (barH - disSz) / 2, disSz, true,
                () => _updateService.DismissBanner());
        }
        else if (snap.Phase == UpdatePhase.Downloading)
        {
            int cancelW = Math.Clamp(MeasureTextWidth(dc, Strings.UpdateBtnCancel, 12, Win32.FW_SEMIBOLD) + S(22), S(82), S(120));
            int cancelX = w - padX - cancelW;
            int progW = S(220);
            int progH = S(8);
            int progX = cancelX - S(14) - progW;
            int progY = y + (barH - progH) / 2;

            string text;
            float ratio = 0f;
            if (snap.TotalBytes > 0)
            {
                int pct = (int)Math.Clamp(snap.BytesDownloaded * 100L / snap.TotalBytes, 0L, 100L);
                ratio = Math.Clamp((float)snap.BytesDownloaded / snap.TotalBytes, 0f, 1f);
                string progressMb = $"{AppUpdateService.FormatMegabytes(snap.BytesDownloaded)} / {AppUpdateService.FormatMegabytes(snap.TotalBytes)} MB";
                text = Strings.UpdateDownloadingText(snap.LatestVersion, pct, progressMb);
            }
            else
            {
                text = Strings.UpdateDownloadingIndetText(snap.LatestVersion, $"{AppUpdateService.FormatMegabytes(snap.BytesDownloaded)} MB");
            }

            DrawText(dc, text, padX, y, progX - S(14), y + barH, 12, Win32.FW_SEMIBOLD, Theme.TextPrimary);

            GdiPlus.FillRoundedRect(gfx, progX, progY, progW, progH, progH / 2f, GdiPlus.Argb(Theme.BgControl));
            if (ratio > 0.005f)
            {
                float fillW = Math.Max(progH, progW * ratio);
                GdiPlus.FillRoundedRect(gfx, progX, progY, fillW, progH, progH / 2f, GdiPlus.Argb(Theme.Accent));
            }
            else
            {
                float segW = progW * 0.28f;
                float phase = (float)((Environment.TickCount64 % 1400) / 1400.0);
                float segX = progX + (progW - segW) * phase;
                GdiPlus.FillRoundedRect(gfx, segX, progY, segW, progH, progH / 2f, GdiPlus.Argb(Theme.Accent));
            }

            bool cancelHover = string.Equals(_hoverId, "upd_btn_cancel", StringComparison.Ordinal);
            GdiPlus.FillAndStrokeRoundedRect(gfx, cancelX, btnY, cancelW, btnH, SF(7),
                GdiPlus.Argb(cancelHover ? Theme.BgControlHover : Theme.BgControl),
                GdiPlus.Argb(Theme.BorderStrong));
            DrawText(dc, Strings.UpdateBtnCancel, cancelX, btnY, cancelX + cancelW, btnY + btnH, 12, Win32.FW_SEMIBOLD, Theme.TextPrimary, centered: true);
            _hitRegions.Add(new HitRegion(
                new Win32.RECT { Left = cancelX, Top = btnY, Right = cancelX + cancelW, Bottom = btnY + btnH },
                "upd_btn_cancel",
                () => _updateService.CancelDownload()));
        }
        else if (snap.Phase == UpdatePhase.ReadyToInstall)
        {
            int disSz = S(24);
            int disX = w - padX - disSz;
            int instW = Math.Clamp(MeasureTextWidth(dc, Strings.UpdateBtnInstall, 12, Win32.FW_SEMIBOLD) + S(28), S(162), S(230));
            int instX = disX - S(8) - instW;

            int dotSz = S(8);
            FillCircleArgb(gfx, GdiPlus.Argb(Theme.Green), padX, y + (barH - dotSz) / 2f, dotSz);
            DrawText(dc, Strings.UpdateReadyBanner(snap.LatestVersion), padX + dotSz + S(10), y, instX - S(12), y + barH,
                12, Win32.FW_SEMIBOLD, Theme.TextPrimary);

            bool instHover = string.Equals(_hoverId, "upd_btn_install", StringComparison.Ordinal);
            GdiPlus.FillAndStrokeRoundedRect(gfx, instX, btnY, instW, btnH, SF(7),
                GdiPlus.Argb(Theme.Green, instHover ? (byte)255 : (byte)230),
                GdiPlus.Argb(Theme.Green));
            DrawText(dc, Strings.UpdateBtnInstall, instX, btnY, instX + instW, btnY + btnH, 12, Win32.FW_SEMIBOLD, Theme.OnAccent, centered: true);
            _hitRegions.Add(new HitRegion(
                new Win32.RECT { Left = instX, Top = btnY, Right = instX + instW, Bottom = btnY + btnH },
                "upd_btn_install",
                LaunchDownloadedUpdate));

            DrawIconButton(dc, gfx, "upd_btn_dismiss", SmallButtonGlyph.Close, disX, y + (barH - disSz) / 2, disSz, true,
                () => _updateService.DismissBanner());
        }
        else // Failed
        {
            int disSz = S(24);
            int disX = w - padX - disSz;
            int retryW = Math.Clamp(MeasureTextWidth(dc, Strings.UpdateBtnRetry, 12, Win32.FW_SEMIBOLD) + S(26), S(106), S(156));
            int retryX = disX - S(8) - retryW;

            int dotSz = S(8);
            FillCircleArgb(gfx, GdiPlus.Argb(Theme.Red), padX, y + (barH - dotSz) / 2f, dotSz);
            DrawText(dc, snap.ErrorMessage ?? Strings.UpdateFailedBanner, padX + dotSz + S(10), y, retryX - S(12), y + barH,
                12, Win32.FW_SEMIBOLD, Theme.TextPrimary);

            bool retryHover = string.Equals(_hoverId, "upd_btn_retry", StringComparison.Ordinal);
            GdiPlus.FillAndStrokeRoundedRect(gfx, retryX, btnY, retryW, btnH, SF(7),
                GdiPlus.Argb(retryHover ? Theme.BgControlHover : Theme.BgCard),
                GdiPlus.Argb(Theme.Red));
            DrawText(dc, Strings.UpdateBtnRetry, retryX, btnY, retryX + retryW, btnY + btnH, 12, Win32.FW_SEMIBOLD, Theme.Red, centered: true);
            _hitRegions.Add(new HitRegion(
                new Win32.RECT { Left = retryX, Top = btnY, Right = retryX + retryW, Bottom = btnY + btnH },
                "upd_btn_retry",
                () => _updateService.StartDownload()));

            DrawIconButton(dc, gfx, "upd_btn_dismiss", SmallButtonGlyph.Close, disX, y + (barH - disSz) / 2, disSz, true,
                () => _updateService.DismissBanner());
        }
    }

    private void LaunchDownloadedUpdate()
    {
        if (_updateService.TryLaunchInstaller(out string? err))
        {
            ExitRequested?.Invoke();
        }
        else if (!string.IsNullOrEmpty(err))
        {
            ShowStatusBanner(err, isError: true);
        }
    }

    private void DrawTabButton(
        nint dc, nint gfx, string id, string label,
        int x, int y, int w, int h, bool active, Action onClick)
    {
        bool hovered = string.Equals(_hoverId, id, StringComparison.Ordinal);
        uint bg = active ? Theme.Accent : (hovered ? Theme.BgControl : Theme.BgCard);
        uint fg = active ? Theme.OnAccent : Theme.TextSecondary;
        if (active || hovered)
        {
            GdiPlus.FillRoundedRect(gfx, x, y, w, h, SF(6), GdiPlus.Argb(bg));
        }
        DrawText(dc, label, x, y, x + w, y + h, 12, active ? Win32.FW_SEMIBOLD : Win32.FW_MEDIUM, fg, centered: true);
        _hitRegions.Add(new HitRegion(new Win32.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h }, id, onClick));
    }

    private void DrawScrollableBody(nint dc, nint gfx, int w, int h, int headerH)
    {
        int padX = S(22);
        int contentW = w - padX * 2;
        int y = headerH + S(18) - _scrollY;
        int startY = y;

        if (_activeTab == TabKind.Dashboard)
        {
            y = DrawDashboardTab(dc, gfx, padX, y, contentW, headerH);
        }
        else
        {
            y = DrawSettingsTab(dc, gfx, padX, y, contentW, headerH);
        }

        _contentTotalH = (y - startY) + S(24);
        ClampScroll();

        // Thin modern scrollbar when content overflows
        int viewH = h - headerH;
        if (_contentTotalH > viewH && viewH > 20)
        {
            float ratio = (float)viewH / _contentTotalH;
            int thumbH = Math.Max(S(32), (int)(viewH * ratio));
            int maxScroll = _contentTotalH - viewH;
            int thumbY = headerH + (int)((viewH - thumbH) * ((float)_scrollY / maxScroll));
            GdiPlus.FillRoundedRect(gfx, w - S(8), thumbY, S(4), thumbH, SF(2), GdiPlus.Argb(Theme.BorderStrong, 180));
        }
    }

    private int DrawDashboardTab(nint dc, nint gfx, int x, int y, int w, int headerH)
    {
        y = DrawStatusBannerIfActive(dc, gfx, x, y, w);

        // 1. ACTIVE TARGET CARD
        DrawText(dc, Strings.SecActiveTarget, x, y, x + w, y + S(20), 11, Win32.FW_BOLD, Theme.TextMuted);
        y += S(24);

        y = DrawActiveTargetCard(dc, gfx, x, y, w, headerH);
        y += S(22);

        // 2. LIVE MEDIA SESSIONS
        int liveHeaderY = y;
        int refW = S(92);
        int refH = S(26);
        int refX = x + w - refW;

        var (bridgeBrowsers, bridgeTabs) = _bridgeServer.GetStatusSnapshot();
        string bridgeBadgeText = bridgeBrowsers > 0
            ? Strings.BadgeBridgeConnected(bridgeBrowsers, bridgeTabs)
            : Strings.BadgeBridgeDisconnected;
        int bridgePillW = Math.Max(S(54), MeasureTextWidth(dc, bridgeBadgeText, 10, Win32.FW_SEMIBOLD) + S(20));
        int bridgePillX = refX - S(8) - bridgePillW;
        int bridgePillY = liveHeaderY + S(1);
        int titleMaxRight = bridgePillX - S(10);

        DrawText(dc, $"{Strings.SecLiveSessions} ({_snapshot.Sessions.Count})", x, y, Math.Max(x + S(80), titleMaxRight), y + S(24), 11, Win32.FW_BOLD, Theme.TextMuted);

        if (bridgePillX > x + S(110))
        {
            uint badgeFg = bridgeBrowsers > 0 ? Theme.Green : Theme.Amber;
            uint badgeBg = bridgeBrowsers > 0 ? Theme.GreenSoft : Theme.AmberSoft;
            DrawPillBadge(dc, gfx, bridgePillX, bridgePillY, bridgeBadgeText, badgeFg, badgeBg);

            if (liveHeaderY + refH > headerH)
            {
                RegisterScrollableHit(
                    new Win32.RECT { Left = bridgePillX, Top = bridgePillY, Right = bridgePillX + bridgePillW, Bottom = bridgePillY + S(22) },
                    "dash_bridge_status",
                    () =>
                    {
                        _activeTab = TabKind.Settings;
                        _scrollY = 0;
                    });
            }
        }

        if (y + refH > headerH)
        {
            DrawActionButton(dc, gfx, "btn_refresh", Strings.BtnRefresh, refX, liveHeaderY - S(2), refW, refH,
                Theme.BgCard, Theme.BgControlHover, Theme.TextSecondary, Theme.BorderSubtle,
                () => _ = _sessionManager.RefreshAsync());
        }
        y += S(28);

        if (_snapshot.Sessions.Count == 0)
        {
            y = DrawEmptyCard(dc, gfx, x, y, w, Strings.EmptySessionsTitle, Strings.EmptySessionsSub);
        }
        else
        {
            foreach (var session in _snapshot.Sessions)
            {
                y = DrawLiveSessionRow(dc, gfx, x, y, w, session, headerH);
                y += S(8);
            }
        }

        y += S(14);

        // 3. DISCOVERED APPLICATION PRIORITY LIST
        int clearW = S(140);
        DrawText(dc, $"{Strings.SecPriorityList} ({_priorityApps.Count})", x, y, x + w - clearW - S(10), y + S(24), 11, Win32.FW_BOLD, Theme.TextMuted);

        if (_priorityApps.Count > 0 && y + S(24) > headerH)
        {
            DrawActionButton(dc, gfx, "btn_clear_hist", Strings.BtnResetHistory, x + w - clearW, y - S(2), clearW, S(26),
                Theme.BgCard, Theme.RedSoft, Theme.Red, Theme.BorderSubtle,
                () => _stateStore.ClearPriorityHistory());
        }
        y += S(28);

        if (_priorityApps.Count == 0)
        {
            y = DrawEmptyCard(dc, gfx, x, y, w, Strings.EmptyPriorityTitle, Strings.EmptyPrioritySub);
        }
        else
        {
            long animNow = Environment.TickCount64;
            bool hasHighlight = !string.IsNullOrEmpty(_highlightPrioAppKey) &&
                                GetHighlightIntensity(_highlightPrioStartTick, animNow) > 0.005f;
            int deferredIdx = -1;
            int deferredY = 0;

            for (int i = 0; i < _priorityApps.Count; i++)
            {
                if (hasHighlight &&
                    deferredIdx < 0 &&
                    string.Equals(_highlightPrioAppKey, _priorityApps[i].AppKey, StringComparison.OrdinalIgnoreCase))
                {
                    deferredIdx = i;
                    deferredY = y;
                    y += S(54) + S(8);
                    continue;
                }

                y = DrawPriorityRow(dc, gfx, x, y, w, _priorityApps[i], i, _priorityApps.Count, headerH);
                y += S(8);
            }

            // Draw the highlighted row last so its outer halo & slide glide render cleanly over neighbors
            if (deferredIdx >= 0)
            {
                DrawPriorityRow(dc, gfx, x, deferredY, w, _priorityApps[deferredIdx], deferredIdx, _priorityApps.Count, headerH);
            }
        }

        return y;
    }

    private int DrawActiveTargetCard(nint dc, nint gfx, int x, int y, int w, int headerH)
    {
        int cardH = S(122);
        var target = _snapshot.ActiveTarget;
        bool isManual = _snapshot.IsManualLock;
        bool focusHovered = target != null && string.Equals(_hoverId, "act_focus", StringComparison.Ordinal);
        uint borderColor = target == null
            ? Theme.BorderSubtle
            : (isManual ? Theme.Amber : Theme.Accent);

        GdiPlus.FillAndStrokeRoundedRect(
            gfx, x, y, w, cardH, SF(12),
            GdiPlus.Argb(Theme.BgCardElevated),
            GdiPlus.Argb(borderColor, target == null ? (byte)255 : (byte)190),
            1.4f);

        float targetGlow = target != null && string.Equals(_highlightTargetSessionId, target.SessionId, StringComparison.Ordinal)
            ? GetHighlightIntensity(_highlightTargetStartTick, Environment.TickCount64)
            : 0f;
        if (targetGlow > 0.01f)
        {
            DrawCardGlow(gfx, x, y, w, cardH, SF(12), isManual ? Theme.Amber : Theme.Accent, targetGlow);
        }

        int pad = S(18);
        int iconBox = S(58);
        int iconBoxY = y + (cardH - iconBox) / 2;
        GdiPlus.FillAndStrokeRoundedRect(gfx, x + pad, iconBoxY, iconBox, iconBox, SF(12),
            GdiPlus.Argb(Theme.BgCard), GdiPlus.Argb(focusHovered ? Theme.Accent : Theme.BorderSubtle));

        if (target == null)
        {
            DrawText(dc, Strings.NoActiveTargetTitle, x + pad + iconBox + S(16), y + S(28), x + w - pad, y + S(54), 16, Win32.FW_SEMIBOLD, Theme.TextPrimary);
            DrawText(dc, Strings.NoActiveTargetSub,
                x + pad + iconBox + S(16), y + S(58), x + w - pad, y + S(88), 12, Win32.FW_NORMAL, Theme.TextSecondary);
            return y + cardH;
        }

        nint hIcon = _iconRenderer.GetIconForApp(target.AppKey, target.ExePath);
        int icoSz = S(36);
        if (hIcon != 0)
        {
            Win32.DrawIconEx(dc, x + pad + (iconBox - icoSz) / 2, iconBoxY + (iconBox - icoSz) / 2, hIcon, icoSz, icoSz, 0, 0, Win32.DI_NORMAL);
        }

        // Right controls: top row [Prev] [Play/Pause] [Next] + [Lock/Unlock]; bottom row [Mute] [Slider] [%]
        int btnSz = S(36);
        int lockW = S(116);
        int ctrlRight = x + w - pad;
        int lockX = ctrlRight - lockW;
        int nextX = lockX - S(10) - btnSz;
        int playX = nextX - S(6) - btnSz;
        int prevX = playX - S(6) - btnSz;
        int ctrlY = y + S(18);

        if (y + cardH > headerH)
        {
            RegisterScrollableHit(
                new Win32.RECT { Left = x, Top = y, Right = prevX - S(8), Bottom = y + cardH },
                "act_focus",
                () => _ = _engine.FocusSessionAsync(target.SessionId));

            bool anyPlaying = target.IsPlaying || _snapshot.Sessions.Any(s => s.IsPlaying || s.IsEffectivelyPlaying);
            DrawTransportButton(dc, gfx, "act_prev", TransportGlyph.Previous, prevX, ctrlY, btnSz,
                () => _ = _engine.DispatchMediaActionAsync(MediaActionKind.Previous));
            DrawTransportButton(dc, gfx, "act_play", anyPlaying ? TransportGlyph.Pause : TransportGlyph.Play, playX, ctrlY, btnSz,
                () => _ = _engine.DispatchMediaActionAsync(MediaActionKind.PlayPause), accent: true);
            DrawTransportButton(dc, gfx, "act_next", TransportGlyph.Next, nextX, ctrlY, btnSz,
                () => _ = _engine.DispatchMediaActionAsync(MediaActionKind.Next));

            DrawActionButton(
                dc, gfx, "act_lock",
                isManual ? Strings.BtnUnlock : Strings.BtnLock,
                lockX, ctrlY, lockW, btnSz,
                isManual ? Theme.AmberSoft : Theme.BgControl,
                isManual ? Theme.AmberSoft : Theme.BgControlHover,
                isManual ? Theme.Amber : Theme.TextPrimary,
                isManual ? Theme.Amber : Theme.BorderStrong,
                () => _engine.ToggleManualLockOnActiveOrSelected());

            DrawVolumeControls(dc, gfx, "act_vol", target.SessionId, target.Volume, target.IsMuted,
                prevX, y + S(72), ctrlRight - prevX, S(28), headerH);
        }

        // Center info (click anywhere on icon/title to focus app/tab window)
        int textX = x + pad + iconBox + S(16);
        int textRight = prevX - S(14);
        int arrowSz = S(13);
        int titleW = MeasureTextWidth(dc, target.DisplayName, 16, Win32.FW_BOLD) + S(6);
        int nameMaxRight = Math.Min(textRight - arrowSz - S(6), textX + titleW);

        DrawText(dc, target.DisplayName, textX, y + S(20), nameMaxRight, y + S(46), 16, Win32.FW_BOLD,
            focusHovered ? Theme.Accent : Theme.TextPrimary);

        DrawFocusArrowGlyph(gfx, nameMaxRight + S(2), y + S(26), arrowSz, focusHovered ? Theme.Accent : Theme.TextMuted);

        int bx = nameMaxRight + arrowSz + S(10);
        (string statusText, uint statusFg, uint statusBg) = target.IsEffectivelyPlaying
            ? (Strings.StatusPlaying, Theme.Green, Theme.GreenSoft)
            : (target.IsMuted || target.Volume <= 0.005f ? (Strings.StatusMuted, Theme.Red, Theme.RedSoft) : (Strings.StatusPaused, Theme.TextSecondary, Theme.BgControl));
        int statusPillW = MeasureTextWidth(dc, statusText, 10, Win32.FW_SEMIBOLD) + S(20);
        if (bx + statusPillW <= textRight)
        {
            bx += DrawPillBadge(dc, gfx, bx, y + S(22), statusText, statusFg, statusBg) + S(6);
        }

        string modeText = isManual ? Strings.ModeManualLock : Strings.ModeAuto;
        int modePillW = MeasureTextWidth(dc, modeText, 10, Win32.FW_SEMIBOLD) + S(20);
        if (bx + modePillW <= textRight)
        {
            DrawPillBadge(dc, gfx, bx, y + S(22),
                modeText,
                isManual ? Theme.Amber : Theme.Accent,
                isManual ? Theme.AmberSoft : Theme.AccentSoft);
        }

        string mediaLine = target.FormattedMediaTitle;
        DrawText(dc, mediaLine, textX, y + S(50), textRight, y + S(72), 13, Win32.FW_MEDIUM, Theme.TextSecondary);

        int volPct = target.IsMuted ? 0 : (int)MathF.Round(target.Volume * 100f);
        string volDisplay = Strings.FormatPercent(volPct);
        string sourceLabel = target.Source switch
        {
            SessionSource.MpvIpc => "mpv IPC",
            SessionSource.BrowserBridge => Strings.SourceBrowserBridge,
            SessionSource.AudioSession => Strings.SourceAudioSession,
            _ => "SMTC"
        };
        string metaLine = $"{Strings.MetaSourcePrefix}: {sourceLabel}  •  {Strings.MetaVolumePrefix}: {volDisplay}"
                          + (target.IsVideoLikely ? $"  •  {Strings.MetaVideoScrubReady}" : "");
        DrawText(dc, metaLine, textX, y + S(76), textRight, y + S(98), 11, Win32.FW_NORMAL, Theme.TextMuted);

        return y + cardH;
    }

    private int DrawLiveSessionRow(nint dc, nint gfx, int x, int y, int w, MediaSessionInfo session, int headerH)
    {
        int rowH = S(64);
        bool isTarget = _snapshot.ActiveTarget != null &&
                        string.Equals(_snapshot.ActiveTarget.SessionId, session.SessionId, StringComparison.Ordinal);
        bool isLocked = _snapshot.IsManualLock && isTarget;

        string focusId = $"sess_focus_{session.SessionId}";
        string lockBtnId = $"sess_lock_{session.SessionId}";
        string volPrefix = $"sess_vol_{session.SessionId}";
        bool focusHovered = string.Equals(_hoverId, focusId, StringComparison.Ordinal);
        bool anyRowControlHovered = focusHovered ||
                                    string.Equals(_hoverId, lockBtnId, StringComparison.Ordinal) ||
                                    (_hoverId != null && _hoverId.StartsWith(volPrefix, StringComparison.Ordinal));

        uint bg = isTarget ? Theme.BgCardElevated : (anyRowControlHovered ? Theme.BgCardHover : Theme.BgCard);
        uint border = isLocked ? Theme.Amber : (focusHovered || isTarget ? Theme.Accent : Theme.BorderSubtle);

        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, w, rowH, SF(10),
            GdiPlus.Argb(bg), GdiPlus.Argb(border, isTarget || focusHovered ? (byte)195 : (byte)255), isTarget ? 1.2f : 1f);

        float rowGlow = isTarget && string.Equals(_highlightTargetSessionId, session.SessionId, StringComparison.Ordinal)
            ? GetHighlightIntensity(_highlightTargetStartTick, Environment.TickCount64)
            : 0f;
        if (rowGlow > 0.01f)
        {
            DrawCardGlow(gfx, x, y, w, rowH, SF(10), isLocked ? Theme.Amber : Theme.Accent, rowGlow);
        }

        int pad = S(14);
        int icoSz = S(28);
        nint hIcon = _iconRenderer.GetIconForApp(session.AppKey, session.ExePath);
        if (hIcon != 0)
        {
            Win32.DrawIconEx(dc, x + pad, y + (rowH - icoSz) / 2, hIcon, icoSz, icoSz, 0, 0, Win32.DI_NORMAL);
        }
        else
        {
            GdiPlus.FillRoundedRect(gfx, x + pad, y + (rowH - icoSz) / 2, icoSz, icoSz, SF(7), GdiPlus.Argb(Theme.BgControl));
        }

        int btnW = S(92);
        int btnH = S(30);
        int btnX = x + w - pad - btnW;
        int btnY = y + (rowH - btnH) / 2;

        // Status pill to the left of the lock button
        int statusW = S(104);
        int statusX = btnX - S(8) - statusW;

        (string stText, uint stFg, uint stBg) = session.IsEffectivelyPlaying
            ? (Strings.StatusPlaying, Theme.Green, Theme.GreenSoft)
            : (session.IsMuted || session.Volume <= 0.005f ? (Strings.StatusMuted, Theme.Red, Theme.RedSoft) : (Strings.StatusPaused, Theme.TextSecondary, Theme.BgControl));

        DrawPillBadgeFixed(dc, gfx, statusX, y + (rowH - S(22)) / 2, statusW, stText, stFg, stBg);

        // Inline interactive volume control: [Mute] [Slider] [%]
        int volGroupW = S(154);
        int volGroupX = statusX - S(10) - volGroupW;
        int volGroupY = y + (rowH - S(28)) / 2;

        int textX = x + pad + icoSz + S(12);
        int textRight = volGroupX - S(10);
        string tag = session.IsVideoLikely ? $" • {Strings.TagVideo}" : "";
        string headerText = session.DisplayName + tag;

        int arrowSz = S(11);
        int titleMeasured = MeasureTextWidth(dc, headerText, 13, Win32.FW_SEMIBOLD) + S(4);
        int titleDrawRight = Math.Min(textRight - arrowSz - S(4), textX + titleMeasured);

        DrawText(dc, headerText, textX, y + S(11), textRight - arrowSz - S(4), y + S(31), 13, Win32.FW_SEMIBOLD,
            focusHovered ? Theme.Accent : Theme.TextPrimary);
        if (titleDrawRight + arrowSz + S(2) <= textRight)
        {
            DrawFocusArrowGlyph(gfx, titleDrawRight + S(2), y + S(16), arrowSz, focusHovered ? Theme.Accent : Theme.TextMuted);
        }

        DrawText(dc, session.FormattedMediaTitle, textX, y + S(33), textRight, y + S(53), 12, Win32.FW_NORMAL, Theme.TextSecondary);

        if (y + rowH > headerH)
        {
            RegisterScrollableHit(
                new Win32.RECT { Left = x, Top = y, Right = volGroupX - S(4), Bottom = y + rowH },
                focusId,
                () => _ = _engine.FocusSessionAsync(session.SessionId));

            DrawVolumeControls(dc, gfx, volPrefix, session.SessionId, session.Volume, session.IsMuted,
                volGroupX, volGroupY, volGroupW, S(28), headerH);

            DrawActionButton(
                dc, gfx, lockBtnId,
                isLocked ? Strings.BtnLocked : Strings.BtnLock,
                btnX, btnY, btnW, btnH,
                isLocked ? Theme.AmberSoft : Theme.BgControl,
                isLocked ? Theme.AmberSoft : Theme.BgControlHover,
                isLocked ? Theme.Amber : Theme.TextPrimary,
                isLocked ? Theme.Amber : Theme.BorderStrong,
                () => _engine.ToggleManualLockOnActiveOrSelected(session.SessionId));
        }

        return y + rowH;
    }

    private void DrawVolumeControls(
        nint dc, nint gfx, string idPrefix, string sessionId,
        float volume, bool isMuted, int x, int y, int w, int h, int headerH)
    {
        int muteSz = S(26);
        int muteX = x;
        int muteY = y + (h - muteSz) / 2;
        string muteId = $"{idPrefix}_mute";
        bool muteHovered = string.Equals(_hoverId, muteId, StringComparison.Ordinal);

        uint muteBg = isMuted
            ? Theme.RedSoft
            : (muteHovered ? Theme.BgControlHover : Theme.BgControl);
        uint muteBorder = isMuted ? Theme.Red : Theme.BorderSubtle;
        uint muteFg = isMuted ? Theme.Red : Theme.TextPrimary;

        GdiPlus.FillAndStrokeRoundedRect(gfx, muteX, muteY, muteSz, muteSz, SF(6),
            GdiPlus.Argb(muteBg), GdiPlus.Argb(muteBorder));
        DrawSpeakerGlyph(gfx, muteX, muteY, muteSz, isMuted, muteFg);

        if (y + h > headerH)
        {
            RegisterScrollableHit(
                new Win32.RECT { Left = muteX, Top = muteY, Right = muteX + muteSz, Bottom = muteY + muteSz },
                muteId,
                () => _ = _engine.ToggleSessionMuteAsync(sessionId));
        }

        int pctW = S(38);
        int pctX = x + w - pctW;
        int trackX = muteX + muteSz + S(7);
        int trackRight = pctX - S(5);
        int trackW = Math.Max(S(36), trackRight - trackX);
        int trackH = S(6);
        int trackY = y + (h - trackH) / 2;

        string sliderId = $"{idPrefix}_slider";
        bool isDraggingThis = string.Equals(_draggingSliderId, sliderId, StringComparison.Ordinal);
        bool sliderHovered = isDraggingThis || string.Equals(_hoverId, sliderId, StringComparison.Ordinal);

        float dispVol = isDraggingThis
            ? _dragPreviewVolume
            : (isMuted ? 0f : Math.Clamp(volume, 0f, 1f));

        // Track background
        GdiPlus.FillRoundedRect(gfx, trackX, trackY, trackW, trackH, trackH / 2f, GdiPlus.Argb(Theme.BgControl));

        // Active fill bar
        float fillW = trackW * dispVol;
        if (fillW > 1f)
        {
            uint fillColor = (isMuted && !isDraggingThis) ? Theme.Red : Theme.Accent;
            GdiPlus.FillRoundedRect(gfx, trackX, trackY, MathF.Max(trackH, fillW), trackH, trackH / 2f, GdiPlus.Argb(fillColor));
        }

        // Slider thumb
        float thumbD = sliderHovered ? S(13) : S(11);
        float cx = trackX + trackW * dispVol;
        float cy = y + h / 2f;
        FillCircleArgb(gfx, GdiPlus.Argb(Theme.SwitchKnob), cx - thumbD / 2f, cy - thumbD / 2f, thumbD);
        StrokeCircleArgb(gfx, GdiPlus.Argb(isDraggingThis ? Theme.Accent : Theme.BorderStrong), MathF.Max(1.2f, SF(1.2f)),
            cx - thumbD / 2f, cy - thumbD / 2f, thumbD);

        if (y + h > headerH)
        {
            int hitTop = Math.Max(_bodyClipTop, y);
            if (y + h > hitTop)
            {
                _sliderRegions.Add(new SliderRegion(
                    new Win32.RECT { Left = trackX - S(4), Top = hitTop, Right = trackX + trackW + S(4), Bottom = y + h },
                    sliderId,
                    sessionId,
                    trackX,
                    trackW));
            }
        }

        int dispPct = (int)MathF.Round(dispVol * 100f);
        DrawText(dc, Strings.FormatPercent(dispPct), pctX, y, pctX + pctW, y + h, 11, Win32.FW_SEMIBOLD,
            isMuted && !isDraggingThis ? Theme.Red : Theme.TextSecondary, centered: true);
    }

    private void DrawSpeakerGlyph(nint gfx, int x, int y, int sz, bool isMuted, uint colorRgb)
    {
        float cx = x + sz / 2f;
        float cy = y + sz / 2f;
        float u = sz * 0.22f;

        if (GdiPlus.GdipCreateSolidFill(GdiPlus.Argb(colorRgb), out nint brush) == 0 && brush != 0)
        {
            try
            {
                if (GdiPlus.GdipCreatePath(GdiPlus.FillModeAlternate, out nint path) == 0 && path != 0)
                {
                    try
                    {
                        float x0 = cx - u * 1.15f;
                        float x1 = cx - u * 0.55f;
                        float x2 = cx + u * 0.12f;
                        GdiPlus.GdipAddPathLine(path, x0, cy - u * 0.42f, x1, cy - u * 0.42f);
                        GdiPlus.GdipAddPathLine(path, x1, cy - u * 0.42f, x2, cy - u * 0.95f);
                        GdiPlus.GdipAddPathLine(path, x2, cy - u * 0.95f, x2, cy + u * 0.95f);
                        GdiPlus.GdipAddPathLine(path, x2, cy + u * 0.95f, x1, cy + u * 0.42f);
                        GdiPlus.GdipAddPathLine(path, x1, cy + u * 0.42f, x0, cy + u * 0.42f);
                        GdiPlus.GdipClosePathFigure(path);
                        GdiPlus.GdipFillPath(gfx, brush, path);
                    }
                    finally
                    {
                        GdiPlus.GdipDeletePath(path);
                    }
                }
            }
            finally
            {
                GdiPlus.GdipDeleteBrush(brush);
            }
        }

        if (GdiPlus.GdipCreatePen1(GdiPlus.Argb(colorRgb), MathF.Max(1.3f, SF(1.3f)), GdiPlus.UnitPixel, out nint pen) == 0 && pen != 0)
        {
            try
            {
                GdiPlus.GdipSetPenStartCap(pen, GdiPlus.LineCapRound);
                GdiPlus.GdipSetPenEndCap(pen, GdiPlus.LineCapRound);

                if (isMuted)
                {
                    float mx1 = cx + u * 0.48f;
                    float mx2 = cx + u * 1.15f;
                    GdiPlus.GdipDrawLine(gfx, pen, mx1, cy - u * 0.42f, mx2, cy + u * 0.42f);
                    GdiPlus.GdipDrawLine(gfx, pen, mx2, cy - u * 0.42f, mx1, cy + u * 0.42f);
                }
                else
                {
                    GdiPlus.GdipDrawArc(gfx, pen, cx - u * 0.25f, cy - u * 0.55f, u * 0.95f, u * 1.1f, -50f, 100f);
                    GdiPlus.GdipDrawArc(gfx, pen, cx - u * 0.45f, cy - u * 0.9f, u * 1.6f, u * 1.8f, -50f, 100f);
                }
            }
            finally
            {
                GdiPlus.GdipDeletePen(pen);
            }
        }
    }

    private void DrawFocusArrowGlyph(nint gfx, int x, int y, int sz, uint colorRgb)
    {
        if (GdiPlus.GdipCreatePen1(GdiPlus.Argb(colorRgb), MathF.Max(1.25f, SF(1.25f)), GdiPlus.UnitPixel, out nint pen) != 0 || pen == 0)
            return;

        try
        {
            GdiPlus.GdipSetPenStartCap(pen, GdiPlus.LineCapRound);
            GdiPlus.GdipSetPenEndCap(pen, GdiPlus.LineCapRound);

            float pad = sz * 0.16f;
            float left = x + pad;
            float top = y + pad;
            float right = x + sz - pad;
            float bottom = y + sz - pad;
            float midX = left + (right - left) * 0.52f;
            float midY = top + (bottom - top) * 0.48f;

            // Open box corner + diagonal arrow pointing up-right
            GdiPlus.GdipDrawLine(gfx, pen, midX, top + (bottom - top) * 0.15f, left, top + (bottom - top) * 0.15f);
            GdiPlus.GdipDrawLine(gfx, pen, left, top + (bottom - top) * 0.15f, left, bottom);
            GdiPlus.GdipDrawLine(gfx, pen, left, bottom, right - (right - left) * 0.15f, bottom);
            GdiPlus.GdipDrawLine(gfx, pen, right - (right - left) * 0.15f, bottom, right - (right - left) * 0.15f, midY);

            GdiPlus.GdipDrawLine(gfx, pen, midX - (right - left) * 0.08f, midY + (bottom - top) * 0.08f, right, top);
            GdiPlus.GdipDrawLine(gfx, pen, right, top, right - (right - left) * 0.36f, top);
            GdiPlus.GdipDrawLine(gfx, pen, right, top, right, top + (bottom - top) * 0.36f);
        }
        finally
        {
            GdiPlus.GdipDeletePen(pen);
        }
    }

    private int DrawPriorityRow(nint dc, nint gfx, int x, int y, int w, DiscoveredApp app, int index, int total, int headerH)
    {
        int rowH = S(54);
        long now = Environment.TickCount64;
        bool isHighlighted = string.Equals(_highlightPrioAppKey, app.AppKey, StringComparison.OrdinalIgnoreCase);
        float glow = isHighlighted ? GetHighlightIntensity(_highlightPrioStartTick, now) : 0f;
        int drawY = y + (isHighlighted ? GetSlideOffsetPx(_highlightPrioStartTick, _highlightPrioDir, now) : 0);
        bool isLiveActive = _snapshot.ActiveTarget != null &&
                            string.Equals(_snapshot.ActiveTarget.AppKey, app.AppKey, StringComparison.OrdinalIgnoreCase);

        uint cardBg = (glow > 0.01f || isLiveActive) ? Theme.BgCardElevated : Theme.BgCard;
        uint cardBorder = isLiveActive ? GdiPlus.Argb(Theme.Accent, 145) : GdiPlus.Argb(Theme.BorderSubtle);
        GdiPlus.FillAndStrokeRoundedRect(gfx, x, drawY, w, rowH, SF(10),
            GdiPlus.Argb(cardBg), cardBorder, isLiveActive ? 1.2f : 1f);

        if (glow > 0.01f)
        {
            DrawCardGlow(gfx, x, drawY, w, rowH, SF(10), Theme.Accent, glow);
            byte barAlpha = (byte)Math.Clamp(glow * 1.4f * 255f, 0f, 255f);
            GdiPlus.FillRoundedRect(gfx, x + S(3), drawY + S(9), S(3), rowH - S(18), SF(1.5f),
                GdiPlus.Argb(Theme.Accent, barAlpha));
        }

        int pad = S(14);
        int rankW = S(30);
        int rankH = S(24);
        int rankY = drawY + (rowH - rankH) / 2;
        bool glowPill = glow > 0.05f;
        uint pillBg = glowPill ? Theme.Accent : (index == 0 ? Theme.AccentSoft : Theme.BgControl);
        uint pillFg = glowPill ? Theme.OnAccent : (index == 0 ? Theme.Accent : Theme.TextSecondary);
        GdiPlus.FillRoundedRect(gfx, x + pad, rankY, rankW, rankH, SF(6), GdiPlus.Argb(pillBg));
        DrawText(dc, $"#{index + 1}", x + pad, rankY, x + pad + rankW, rankY + rankH, 11, Win32.FW_BOLD, pillFg, centered: true);

        int icoX = x + pad + rankW + S(10);
        int icoSz = S(24);
        nint hIcon = _iconRenderer.GetIconForApp(app.AppKey, app.ExePath);
        if (hIcon != 0)
        {
            Win32.DrawIconEx(dc, icoX, drawY + (rowH - icoSz) / 2, hIcon, icoSz, icoSz, 0, 0, Win32.DI_NORMAL);
        }
        else
        {
            GdiPlus.FillRoundedRect(gfx, icoX, drawY + (rowH - icoSz) / 2, icoSz, icoSz, SF(6), GdiPlus.Argb(Theme.BgControl));
        }

        int btnSz = S(28);
        int btnY = drawY + (rowH - btnSz) / 2;
        int hitBtnY = y + (rowH - btnSz) / 2;
        int delX = x + w - pad - btnSz;
        int downX = delX - S(6) - btnSz;
        int upX = downX - S(6) - btnSz;

        string hkTargetId = $"app:{app.AppKey}";
        string hkHitId = $"prio_hk_{app.AppKey}";
        string hkClrHitId = $"prio_hk_clr_{app.AppKey}";
        bool isCapturingThis = string.Equals(_capturingTargetId, hkTargetId, StringComparison.Ordinal);
        string formattedHk = HotkeyHelper.FormatDisplay(app.Hotkey);
        bool hasHk = !string.IsNullOrEmpty(formattedHk);
        string hkText = isCapturingThis
            ? (string.IsNullOrEmpty(_capturingPreview) ? Strings.HotkeyListening : _capturingPreview)
            : (hasHk ? formattedHk : Strings.HotkeyAssignShort);

        int clrSz = S(26);
        int pillW = Math.Clamp(MeasureTextWidth(dc, hkText, 11, Win32.FW_SEMIBOLD) + S(20), S(86), S(168));
        int clrX = upX - S(10) - clrSz;
        int hkX = (hasHk && !isCapturingThis)
            ? (clrX - S(4) - pillW)
            : (upX - S(10) - pillW);

        bool showDirBadge = glowPill && _highlightPrioDir != 0;
        int dirBadgeSz = S(20);
        int dirBadgeX = hkX - S(8) - dirBadgeSz;
        if (showDirBadge)
        {
            int dirBadgeY = drawY + (rowH - dirBadgeSz) / 2;
            byte badgeAlpha = (byte)Math.Clamp(glow * 255f, 0f, 255f);
            GdiPlus.FillRoundedRect(gfx, dirBadgeX, dirBadgeY, dirBadgeSz, dirBadgeSz, SF(6),
                GdiPlus.Argb(Theme.AccentSoft, badgeAlpha));
            GdiPlus.DrawRoundedRect(gfx, dirBadgeX, dirBadgeY, dirBadgeSz, dirBadgeSz, SF(6),
                GdiPlus.Argb(Theme.Accent, badgeAlpha), 1f);
            DrawDirectionArrowGlyph(gfx, dirBadgeX + dirBadgeSz / 2f, dirBadgeY + dirBadgeSz / 2f,
                SF(10f), _highlightPrioDir < 0, GdiPlus.Argb(Theme.Accent, badgeAlpha));
        }

        int textX = icoX + icoSz + S(12);
        int textRight = showDirBadge ? (dirBadgeX - S(8)) : (hkX - S(12));
        DrawText(dc, app.DisplayName, textX, drawY + S(8), textRight, drawY + S(28), 13, Win32.FW_SEMIBOLD, Theme.TextPrimary);
        string? browserKey = BrowserSiteScanner.ExtractBrowserKeyFromAppKey(app.AppKey);
        string exeSub = string.IsNullOrWhiteSpace(app.ExePath) ? app.AppKey : app.ExePath;
        if (!string.IsNullOrEmpty(browserKey))
            exeSub = $"{ProcessResolver.FormatDisplayName(browserKey, app.ExePath)} • {exeSub}";
        DrawText(dc, exeSub, textX, drawY + S(27), textRight, drawY + S(46), 11, Win32.FW_NORMAL, Theme.TextMuted);

        if (y + rowH > headerH)
        {
            DrawHotkeyPillButton(dc, gfx, hkHitId, hkText, hkX, btnY, pillW, btnSz,
                isCapturing: isCapturingThis,
                hasValue: hasHk,
                () => StartHotkeyCapture(hkTargetId, hkHitId, hk => _stateStore.SetPriorityAppHotkey(app.AppKey, hk)),
                hitY: hitBtnY);

            if (hasHk && !isCapturingThis)
            {
                DrawIconButton(dc, gfx, hkClrHitId, SmallButtonGlyph.Close, clrX, drawY + (rowH - clrSz) / 2, clrSz, true,
                    () => _stateStore.SetPriorityAppHotkey(app.AppKey, string.Empty), danger: true, hitY: y + (rowH - clrSz) / 2);
            }

            DrawIconButton(dc, gfx, $"prio_up_{app.AppKey}", SmallButtonGlyph.Up, upX, btnY, btnSz, index > 0,
                () =>
                {
                    if (_stateStore.MovePriorityApp(app.AppKey, -1))
                        TriggerPriorityMoveFeedback(app.AppKey, movedUp: true);
                }, hitY: hitBtnY);
            DrawIconButton(dc, gfx, $"prio_dn_{app.AppKey}", SmallButtonGlyph.Down, downX, btnY, btnSz, index < total - 1,
                () =>
                {
                    if (_stateStore.MovePriorityApp(app.AppKey, 1))
                        TriggerPriorityMoveFeedback(app.AppKey, movedUp: false);
                }, hitY: hitBtnY);
            DrawIconButton(dc, gfx, $"prio_rm_{app.AppKey}", SmallButtonGlyph.Close, delX, btnY, btnSz, true,
                () => _stateStore.RemovePriorityApp(app.AppKey), danger: true, hitY: hitBtnY);
        }

        return y + rowH;
    }

    private int DrawStatusBannerIfActive(nint dc, nint gfx, int x, int y, int w)
    {
        if (string.IsNullOrEmpty(_statusBanner) || Environment.TickCount64 > _statusBannerUntilTick)
            return y;

        int bannerH = S(38);
        uint bFg = _statusBannerIsError ? Theme.Red : Theme.Green;
        uint bBg = _statusBannerIsError ? Theme.RedSoft : Theme.GreenSoft;
        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, w, bannerH, SF(8),
            GdiPlus.Argb(bBg), GdiPlus.Argb(bFg, 160));
        DrawText(dc, _statusBanner, x + S(14), y, x + w - S(14), y + bannerH, 12, Win32.FW_SEMIBOLD, bFg);
        return y + bannerH + S(14);
    }

    private int DrawSettingsTab(nint dc, nint gfx, int x, int y, int w, int headerH)
    {
        y = DrawStatusBannerIfActive(dc, gfx, x, y, w);

        // 0. APPEARANCE & LANGUAGE
        DrawText(dc, Strings.SecAppearance, x, y, x + w, y + S(20), 11, Win32.FW_BOLD, Theme.TextMuted);
        y += S(24);

        y = DrawLanguageDropdownRow(dc, gfx, x, y, w, headerH);
        y += S(8);

        y = DrawThemeDropdownRow(dc, gfx, x, y, w, headerH);
        y += S(22);

        // 1. SYSTEM & STARTUP
        DrawText(dc, Strings.SecSystemStartup, x, y, x + w, y + S(20), 11, Win32.FW_BOLD, Theme.TextMuted);
        y += S(24);

        y = DrawToggleSettingRow(dc, gfx, x, y, w, headerH, "set_startup",
            Strings.SetStartupTitle,
            Strings.SetStartupSub,
            _startupService.IsStartupEnabled,
            () =>
            {
                bool next = !_startupService.IsStartupEnabled;
                bool ok = _startupService.SetBasicStartup(next, _settings.StartMinimized);
                ShowStatusBanner(
                    ok ? (next ? Strings.MsgStartupOn : Strings.MsgStartupOff)
                       : Strings.MsgStartupFail,
                    !ok);
            });
        y += S(8);

        y = DrawElevationTaskCard(dc, gfx, x, y, w, headerH);
        y += S(8);

        y = DrawToggleSettingRow(dc, gfx, x, y, w, headerH, "set_start_min",
            Strings.SetStartMinTitle,
            Strings.SetStartMinSub,
            _settings.StartMinimized,
            () =>
            {
                bool nextVal = false;
                _stateStore.UpdateSettings(s =>
                {
                    s.StartMinimized = !s.StartMinimized;
                    nextVal = s.StartMinimized;
                });
                _startupService.SyncStartupArgumentsIfEnabled(nextVal);
            });
        y += S(8);

        y = DrawToggleSettingRow(dc, gfx, x, y, w, headerH, "set_min_close",
            Strings.SetMinCloseTitle,
            Strings.SetMinCloseSub,
            _settings.MinimizeToTrayOnClose,
            () => _stateStore.UpdateSettings(s => s.MinimizeToTrayOnClose = !s.MinimizeToTrayOnClose));
        y += S(8);

        y = DrawToggleSettingRow(dc, gfx, x, y, w, headerH, "set_topmost",
            Strings.SetTopmostTitle,
            Strings.SetTopmostSub,
            _settings.AlwaysOnTop,
            () => _stateStore.UpdateSettings(s => s.AlwaysOnTop = !s.AlwaysOnTop));
        y += S(8);

        y = DrawToggleSettingRow(dc, gfx, x, y, w, headerH, "set_auto_update",
            Strings.SetAutoUpdateTitle,
            Strings.SetAutoUpdateSub,
            _settings.AutoCheckUpdates,
            () => _stateStore.UpdateSettings(s => s.AutoCheckUpdates = !s.AutoCheckUpdates));
        y += S(8);

        y = DrawUpdateCheckRow(dc, gfx, x, y, w, headerH);

        // 1.5 COMPANION BROWSER EXTENSION BRIDGE
        y += S(22);
        DrawText(dc, Strings.SecBrowserBridge, x, y, x + w, y + S(20), 11, Win32.FW_BOLD, Theme.TextMuted);
        y += S(24);

        y = DrawBrowserBridgeCard(dc, gfx, x, y, w, headerH);

        // 2. MEDIA & AUDIO CONTROL
        y += S(22);
        DrawText(dc, Strings.SecMediaAudio, x, y, x + w, y + S(20), 11, Win32.FW_BOLD, Theme.TextMuted);
        y += S(24);

        y = DrawToggleSettingRow(dc, gfx, x, y, w, headerH, "set_hook_enabled",
            Strings.SetHookEnabledTitle,
            Strings.SetHookEnabledSub,
            _settings.MediaHookEnabled,
            () => _stateStore.UpdateSettings(s => s.MediaHookEnabled = !s.MediaHookEnabled));
        y += S(8);

        y = DrawToggleSettingRow(dc, gfx, x, y, w, headerH, "set_tray_wheel",
            Strings.SetTrayWheelTitle,
            Strings.SetTrayWheelSub,
            _settings.TrayWheelVolumeEnabled,
            () => _stateStore.UpdateSettings(s => s.TrayWheelVolumeEnabled = !s.TrayWheelVolumeEnabled));
        y += S(8);

        y = DrawStepperSettingRow(dc, gfx, x, y, w, headerH, "set_vol_step",
            Strings.SetVolStepTitle,
            Strings.SetVolStepSub,
            Strings.FormatPercent(_settings.VolumeStepPercent),
            () => _stateStore.UpdateSettings(s => s.VolumeStepPercent = Math.Max(1, s.VolumeStepPercent - 1)),
            () => _stateStore.UpdateSettings(s => s.VolumeStepPercent = Math.Min(20, s.VolumeStepPercent + 1)));
        y += S(8);

        y = DrawToggleSettingRow(dc, gfx, x, y, w, headerH, "set_osd",
            Strings.SetOsdTitle,
            Strings.SetOsdSub,
            _settings.OsdEnabled,
            () => _stateStore.UpdateSettings(s => s.OsdEnabled = !s.OsdEnabled));
        y += S(8);

        y = DrawToggleSettingRow(dc, gfx, x, y, w, headerH, "set_autopause",
            Strings.SetAutoPauseTitle,
            Strings.SetAutoPauseSub,
            _settings.AutoPauseEnabled,
            () => _stateStore.UpdateSettings(s => s.AutoPauseEnabled = !s.AutoPauseEnabled));
        y += S(8);

        y = DrawToggleSettingRow(dc, gfx, x, y, w, headerH, "set_headphone",
            Strings.SetHeadphoneTitle,
            Strings.SetHeadphoneSub,
            _settings.HeadphoneGuardEnabled,
            () => _stateStore.UpdateSettings(s => s.HeadphoneGuardEnabled = !s.HeadphoneGuardEnabled));
        y += S(8);

        y = DrawToggleSettingRow(dc, gfx, x, y, w, headerH, "set_solo",
            Strings.SetSoloTitle,
            Strings.SetSoloSub,
            _settings.SoloModeEnabled,
            () => _engine.ToggleSoloMode());
        y += S(8);

        y = DrawToggleSettingRow(dc, gfx, x, y, w, headerH, "set_game",
            Strings.SetGameGuardTitle,
            Strings.SetGameGuardSub,
            _settings.GameFullscreenGuardEnabled,
            () => _stateStore.UpdateSettings(s => s.GameFullscreenGuardEnabled = !s.GameFullscreenGuardEnabled));

        // 3. TIMELINE SCRUBBING
        y += S(22);
        DrawText(dc, Strings.SecScrubbing, x, y, x + w, y + S(20), 11, Win32.FW_BOLD, Theme.TextMuted);
        y += S(24);

        y = DrawToggleSettingRow(dc, gfx, x, y, w, headerH, "set_scrub_enabled",
            Strings.SetScrubEnabledTitle,
            Strings.SetScrubEnabledSub,
            _settings.TimelineScrubbingEnabled,
            () => _stateStore.UpdateSettings(s => s.TimelineScrubbingEnabled = !s.TimelineScrubbingEnabled));
        y += S(8);

        y = DrawStepperSettingRow(dc, gfx, x, y, w, headerH, "set_seek",
            Strings.SetSeekSecondsTitle,
            Strings.SetSeekSecondsSub,
            $"{_settings.SeekSeconds} {Strings.UnitSeconds}",
            () => _stateStore.UpdateSettings(s => s.SeekSeconds = Math.Max(1, s.SeekSeconds - 5)),
            () => _stateStore.UpdateSettings(s => s.SeekSeconds = Math.Min(120, s.SeekSeconds + 5)));
        y += S(8);

        y = DrawStepperSettingRow(dc, gfx, x, y, w, headerH, "set_hold",
            Strings.SetLongPressTitle,
            Strings.SetLongPressSub,
            $"{_settings.LongPressThresholdMs} ms",
            () => _stateStore.UpdateSettings(s => s.LongPressThresholdMs = Math.Max(150, s.LongPressThresholdMs - 50)),
            () => _stateStore.UpdateSettings(s => s.LongPressThresholdMs = Math.Min(1500, s.LongPressThresholdMs + 50)));

        // 3.5 GLOBAL SHORTCUTS
        y += S(22);
        DrawText(dc, Strings.SecGlobalHotkeys, x, y, x + w, y + S(20), 11, Win32.FW_BOLD, Theme.TextMuted);
        y += S(24);

        y = DrawHotkeySettingRow(dc, gfx, x, y, w, headerH, "global:toggle",
            Strings.SetHkToggleTitle,
            Strings.SetHkToggleSub,
            _settings.HotkeyToggleWindow,
            hk => _stateStore.SetGlobalHotkey(GlobalHotkeySlot.ToggleWindow, hk));
        y += S(8);

        y = DrawHotkeySettingRow(dc, gfx, x, y, w, headerH, "global:lock",
            Strings.SetHkLockTitle,
            Strings.SetHkLockSub,
            _settings.HotkeyManualLock,
            hk => _stateStore.SetGlobalHotkey(GlobalHotkeySlot.ManualLock, hk));
        y += S(8);

        y = DrawHotkeySettingRow(dc, gfx, x, y, w, headerH, "global:solo",
            Strings.SetHkSoloTitle,
            Strings.SetHkSoloSub,
            _settings.HotkeySoloMode,
            hk => _stateStore.SetGlobalHotkey(GlobalHotkeySlot.SoloMode, hk));
        y += S(8);

        y = DrawHotkeySettingRow(dc, gfx, x, y, w, headerH, "global:playpause",
            Strings.SetHkPlayPauseTitle,
            Strings.SetHkPlayPauseSub,
            _settings.HotkeyPlayPause,
            hk => _stateStore.SetGlobalHotkey(GlobalHotkeySlot.PlayPause, hk),
            allowAnySingleKey: true);
        y += S(8);

        DrawText(dc, Strings.HotkeyHint, x + S(4), y, x + w - S(4), y + S(18), 10, Win32.FW_NORMAL, Theme.TextMuted);
        y += S(16);

        // 4. TOOLS & SHORTCUTS
        y += S(18);
        DrawText(dc, Strings.SecToolsShortcuts, x, y, x + w, y + S(20), 11, Win32.FW_BOLD, Theme.TextMuted);
        y += S(24);

        y = DrawUtilityActionsRow(dc, gfx, x, y, w, headerH);
        y += S(10);

        int infoH = S(132);
        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, w, infoH, SF(10),
            GdiPlus.Argb(Theme.BgCard), GdiPlus.Argb(Theme.BorderSubtle));

        int ix = x + S(16);
        int ir = x + w - S(16);
        DrawText(dc, Strings.ShortcutSecondary, ix, y + S(12), ir, y + S(32), 12, Win32.FW_NORMAL, Theme.TextSecondary);
        DrawText(dc, Strings.FormatShortcutToggles(_settings.HotkeyToggleWindow, _settings.HotkeyManualLock, _settings.HotkeySoloMode),
            ix, y + S(36), ir, y + S(56), 12, Win32.FW_NORMAL, Theme.TextSecondary);
        DrawText(dc, Strings.ShortcutTrayWheel, ix, y + S(60), ir, y + S(80), 12, Win32.FW_NORMAL, Theme.TextSecondary);
        DrawText(dc, $"{Strings.StateFileLabel}: {_stateStore.FilePath}",
            ix, y + S(92), ir, y + S(116), 11, Win32.FW_NORMAL, Theme.TextMuted);

        return y + infoH;
    }

    private int DrawHotkeySettingRow(
        nint dc, nint gfx, int x, int y, int w, int headerH,
        string targetId, string title, string subtitle, string currentHotkey, Action<string> onCommit,
        bool allowAnySingleKey = false)
    {
        int rowH = S(62);
        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, w, rowH, SF(10),
            GdiPlus.Argb(Theme.BgCard), GdiPlus.Argb(Theme.BorderSubtle));

        int pad = S(16);
        string hitId = $"hk_set_{targetId}";
        string clrHitId = $"hk_clr_{targetId}";
        bool isCapturingThis = string.Equals(_capturingTargetId, targetId, StringComparison.Ordinal);
        string formatted = HotkeyHelper.FormatDisplay(currentHotkey, spaced: true, allowAnySingleKey: allowAnySingleKey);
        bool hasValue = !string.IsNullOrEmpty(formatted);

        string pillText = isCapturingThis
            ? (string.IsNullOrEmpty(_capturingPreview) ? Strings.HotkeyListening : _capturingPreview)
            : (hasValue ? formatted : Strings.HotkeyUnassigned);

        int btnH = S(32);
        int btnY = y + (rowH - btnH) / 2;
        int clrSz = S(28);
        int pillW = Math.Clamp(MeasureTextWidth(dc, pillText, 11, Win32.FW_SEMIBOLD) + S(28), S(142), S(200));

        int clrX = x + w - pad - clrSz;
        int pillX = (hasValue && !isCapturingThis)
            ? (clrX - S(6) - pillW)
            : (x + w - pad - pillW);

        int textRight = pillX - S(16);
        DrawText(dc, title, x + pad, y + S(10), textRight, y + S(31), 13, Win32.FW_SEMIBOLD, Theme.TextPrimary);
        DrawText(dc, subtitle, x + pad, y + S(32), textRight, y + S(52), 11, Win32.FW_NORMAL, Theme.TextSecondary);

        if (y + rowH > headerH)
        {
            DrawHotkeyPillButton(dc, gfx, hitId, pillText, pillX, btnY, pillW, btnH,
                isCapturing: isCapturingThis,
                hasValue: hasValue,
                () => StartHotkeyCapture(targetId, hitId, onCommit, allowAnySingleKey: allowAnySingleKey));

            if (hasValue && !isCapturingThis)
            {
                DrawIconButton(dc, gfx, clrHitId, SmallButtonGlyph.Close, clrX, y + (rowH - clrSz) / 2, clrSz, true,
                    () => onCommit(string.Empty), danger: true);
            }
        }

        return y + rowH;
    }

    private void DrawHotkeyPillButton(
        nint dc, nint gfx, string id, string text,
        int x, int y, int w, int h, bool isCapturing, bool hasValue, Action onClick, int hitY = -1)
    {
        bool hovered = string.Equals(_hoverId, id, StringComparison.Ordinal);
        uint bg = isCapturing
            ? Theme.AccentSoft
            : (hasValue ? (hovered ? Theme.BgControlHover : Theme.AccentSoft) : (hovered ? Theme.BgControlHover : Theme.BgControl));
        uint border = isCapturing || hasValue ? Theme.Accent : Theme.BorderStrong;
        uint fg = isCapturing || hasValue ? Theme.Accent : (hovered ? Theme.TextPrimary : Theme.TextSecondary);

        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, w, h, SF(7),
            GdiPlus.Argb(bg), GdiPlus.Argb(border, isCapturing || hasValue ? (byte)220 : (byte)255),
            isCapturing ? 1.4f : 1f);
        DrawText(dc, text, x + S(6), y, x + w - S(6), y + h, 11, Win32.FW_SEMIBOLD, fg, centered: true);
        int regY = hitY >= 0 ? hitY : y;
        RegisterScrollableHit(new Win32.RECT { Left = x, Top = regY, Right = x + w, Bottom = regY + h }, id, onClick);
    }

    private int DrawUpdateCheckRow(nint dc, nint gfx, int x, int y, int w, int headerH)
    {
        int rowH = S(62);
        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, w, rowH, SF(10),
            GdiPlus.Argb(Theme.BgCard), GdiPlus.Argb(Theme.BorderSubtle));

        var snap = _updateSnapshot;
        int pad = S(16);

        string btnLabel;
        uint btnBg;
        uint btnBgHover;
        uint btnFg;
        uint btnBorder;
        Action onBtnClick;

        switch (snap.Phase)
        {
            case UpdatePhase.Checking:
                btnLabel = Strings.BtnCheckingUpdates;
                btnBg = Theme.BgControl;
                btnBgHover = Theme.BgControl;
                btnFg = Theme.TextSecondary;
                btnBorder = Theme.BorderSubtle;
                onBtnClick = () => { };
                break;

            case UpdatePhase.Available:
                btnLabel = Strings.UpdateBtnDownload;
                btnBg = Theme.AccentSoft;
                btnBgHover = Theme.Accent;
                btnFg = string.Equals(_hoverId, "set_upd_action", StringComparison.Ordinal) ? Theme.OnAccent : Theme.TextPrimary;
                btnBorder = Theme.Accent;
                onBtnClick = () => _updateService.StartDownload();
                break;

            case UpdatePhase.Downloading:
                btnLabel = Strings.UpdateBtnCancel;
                btnBg = Theme.BgControl;
                btnBgHover = Theme.BgControlHover;
                btnFg = Theme.TextPrimary;
                btnBorder = Theme.BorderStrong;
                onBtnClick = () => _updateService.CancelDownload();
                break;

            case UpdatePhase.ReadyToInstall:
                btnLabel = Strings.UpdateBtnInstall;
                btnBg = Theme.GreenSoft;
                btnBgHover = Theme.Green;
                btnFg = string.Equals(_hoverId, "set_upd_action", StringComparison.Ordinal) ? Theme.OnAccent : Theme.Green;
                btnBorder = Theme.Green;
                onBtnClick = LaunchDownloadedUpdate;
                break;

            default:
                btnLabel = Strings.BtnCheckUpdates;
                btnBg = Theme.BgControl;
                btnBgHover = Theme.BgControlHover;
                btnFg = Theme.TextPrimary;
                btnBorder = Theme.BorderStrong;
                onBtnClick = () => _ = _updateService.CheckForUpdatesAsync(silent: false);
                break;
        }

        int btnH = S(32);
        int btnY = y + (rowH - btnH) / 2;
        int btnW = Math.Clamp(MeasureTextWidth(dc, btnLabel, 12, Win32.FW_SEMIBOLD) + S(28), S(124), S(196));
        int btnX = x + w - pad - btnW;
        int rightLimit = btnX - S(16);

        string titleText = Strings.SetUpdateCheckTitle;
        int titleW = MeasureTextWidth(dc, titleText, 13, Win32.FW_SEMIBOLD) + S(8);
        int titleRight = Math.Min(rightLimit, x + pad + titleW);
        DrawText(dc, titleText, x + pad, y + S(10), titleRight, y + S(31), 13, Win32.FW_SEMIBOLD, Theme.TextPrimary);

        string verBadge = $"v{snap.CurrentVersion}";
        int bx = titleRight + S(6);
        int verPillW = MeasureTextWidth(dc, verBadge, 10, Win32.FW_SEMIBOLD) + S(18);
        if (bx + verPillW <= rightLimit)
        {
            DrawPillBadge(dc, gfx, bx, y + S(10), verBadge, Theme.Accent, Theme.AccentSoft);
        }

        string subtitle = snap.Phase switch
        {
            UpdatePhase.Checking => Strings.SetUpdateSubChecking,
            UpdatePhase.UpToDate => Strings.SetUpdateSubUpToDate(snap.CurrentVersion),
            UpdatePhase.Available => Strings.SetUpdateSubAvailable(snap.LatestVersion),
            UpdatePhase.Downloading => snap.TotalBytes > 0
                ? Strings.UpdateDownloadingText(
                    snap.LatestVersion,
                    (int)Math.Clamp(snap.BytesDownloaded * 100L / snap.TotalBytes, 0L, 100L),
                    $"{AppUpdateService.FormatMegabytes(snap.BytesDownloaded)} / {AppUpdateService.FormatMegabytes(snap.TotalBytes)} MB")
                : Strings.UpdateDownloadingIndetText(snap.LatestVersion, $"{AppUpdateService.FormatMegabytes(snap.BytesDownloaded)} MB"),
            UpdatePhase.ReadyToInstall => Strings.UpdateReadyBanner(snap.LatestVersion),
            UpdatePhase.Failed => snap.ErrorMessage ?? Strings.SetUpdateSubFailed,
            _ => Strings.SetUpdateSubCurrent(snap.CurrentVersion)
        };
        uint subFg = snap.Phase switch
        {
            UpdatePhase.Available => Theme.Accent,
            UpdatePhase.ReadyToInstall or UpdatePhase.UpToDate => Theme.Green,
            UpdatePhase.Failed => Theme.Red,
            _ => Theme.TextSecondary
        };
        DrawText(dc, subtitle, x + pad, y + S(32), rightLimit, y + S(52), 11, Win32.FW_NORMAL, subFg);

        if (y + rowH > headerH)
        {
            DrawActionButton(dc, gfx, "set_upd_action", btnLabel, btnX, btnY, btnW, btnH,
                btnBg, btnBgHover, btnFg, btnBorder, onBtnClick);
        }

        return y + rowH;
    }

    private int DrawLanguageDropdownRow(nint dc, nint gfx, int x, int y, int w, int headerH)
    {
        int rowH = S(62);
        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, w, rowH, SF(10),
            GdiPlus.Argb(Theme.BgCard), GdiPlus.Argb(Theme.BorderSubtle));

        int pad = S(16);
        int comboW = S(210);
        int comboH = S(34);
        int comboX = x + w - pad - comboW;
        int comboY = y + (rowH - comboH) / 2;

        int textRight = comboX - S(16);
        DrawText(dc, Strings.LanguageTitle, x + pad, y + S(10), textRight, y + S(31), 13, Win32.FW_SEMIBOLD, Theme.TextPrimary);
        DrawText(dc, Strings.LanguageSub, x + pad, y + S(32), textRight, y + S(52), 11, Win32.FW_NORMAL, Theme.TextSecondary);

        var active = Strings.Languages[0];
        foreach (var lang in Strings.Languages)
        {
            if (Strings.Current == lang.Language)
            {
                active = lang;
                break;
            }
        }

        const string triggerId = "dd_trigger_lang";
        bool isOpen = _openDropdown == DropdownKind.Language;
        bool hovered = string.Equals(_hoverId, triggerId, StringComparison.Ordinal);

        uint bg = isOpen || hovered ? Theme.BgControlHover : Theme.BgControl;
        uint border = isOpen ? Theme.Accent : Theme.BorderStrong;
        GdiPlus.FillAndStrokeRoundedRect(gfx, comboX, comboY, comboW, comboH, SF(7),
            GdiPlus.Argb(bg), GdiPlus.Argb(border));

        DrawText(dc, active.NativeName, comboX + S(12), comboY, comboX + comboW - S(28), comboY + comboH,
            12, Win32.FW_SEMIBOLD, Theme.TextPrimary);
        DrawDropdownChevron(gfx, comboX + comboW - S(16), comboY + comboH / 2, isOpen,
            isOpen ? Theme.Accent : Theme.TextSecondary);

        if (isOpen)
        {
            _openDropdownAnchor = new Win32.RECT { Left = comboX, Top = comboY, Right = comboX + comboW, Bottom = comboY + comboH };
        }

        if (y + rowH > headerH)
        {
            RegisterScrollableHit(
                new Win32.RECT { Left = comboX, Top = comboY, Right = comboX + comboW, Bottom = comboY + comboH },
                triggerId,
                () => _openDropdown = _openDropdown == DropdownKind.Language ? DropdownKind.None : DropdownKind.Language);
        }

        return y + rowH;
    }

    private int DrawThemeDropdownRow(nint dc, nint gfx, int x, int y, int w, int headerH)
    {
        int rowH = S(62);
        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, w, rowH, SF(10),
            GdiPlus.Argb(Theme.BgCard), GdiPlus.Argb(Theme.BorderSubtle));

        int pad = S(16);
        int comboW = S(210);
        int comboH = S(34);
        int comboX = x + w - pad - comboW;
        int comboY = y + (rowH - comboH) / 2;

        int textRight = comboX - S(16);
        DrawText(dc, Strings.ThemeTitle, x + pad, y + S(10), textRight, y + S(31), 13, Win32.FW_SEMIBOLD, Theme.TextPrimary);
        DrawText(dc, Strings.ThemeSub, x + pad, y + S(32), textRight, y + S(52), 11, Win32.FW_NORMAL, Theme.TextSecondary);

        var active = Theme.Presets[0];
        foreach (var preset in Theme.Presets)
        {
            if (string.Equals(Theme.CurrentId, preset.Id, StringComparison.OrdinalIgnoreCase))
            {
                active = preset;
                break;
            }
        }

        const string triggerId = "dd_trigger_theme";
        bool isOpen = _openDropdown == DropdownKind.Theme;
        bool hovered = string.Equals(_hoverId, triggerId, StringComparison.Ordinal);

        uint bg = isOpen || hovered ? Theme.BgControlHover : Theme.BgControl;
        uint border = isOpen ? Theme.Accent : Theme.BorderStrong;
        GdiPlus.FillAndStrokeRoundedRect(gfx, comboX, comboY, comboW, comboH, SF(7),
            GdiPlus.Argb(bg), GdiPlus.Argb(border));

        int swSz = S(16);
        int swX = comboX + S(12);
        int swY = comboY + (comboH - swSz) / 2;
        FillCircleArgb(gfx, GdiPlus.Argb(active.SwatchBg), swX, swY, swSz);
        StrokeCircleArgb(gfx, GdiPlus.Argb(Theme.BorderStrong), MathF.Max(1.2f, SF(1.2f)), swX, swY, swSz);
        int dotSz = S(7);
        FillCircleArgb(gfx, GdiPlus.Argb(active.SwatchAccent),
            swX + (swSz - dotSz) / 2f, swY + (swSz - dotSz) / 2f, dotSz);

        DrawText(dc, Strings.ThemeName(active.Id), swX + swSz + S(8), comboY, comboX + comboW - S(28), comboY + comboH,
            12, Win32.FW_SEMIBOLD, Theme.TextPrimary);
        DrawDropdownChevron(gfx, comboX + comboW - S(16), comboY + comboH / 2, isOpen,
            isOpen ? Theme.Accent : Theme.TextSecondary);

        if (isOpen)
        {
            _openDropdownAnchor = new Win32.RECT { Left = comboX, Top = comboY, Right = comboX + comboW, Bottom = comboY + comboH };
        }

        if (y + rowH > headerH)
        {
            RegisterScrollableHit(
                new Win32.RECT { Left = comboX, Top = comboY, Right = comboX + comboW, Bottom = comboY + comboH },
                triggerId,
                () => _openDropdown = _openDropdown == DropdownKind.Theme ? DropdownKind.None : DropdownKind.Theme);
        }

        return y + rowH;
    }

    private void DrawDropdownChevron(nint gfx, int cx, int cy, bool pointingUp, uint color)
    {
        if (GdiPlus.GdipCreatePen1(GdiPlus.Argb(color), MathF.Max(1.5f, SF(1.5f)), GdiPlus.UnitPixel, out nint pen) != 0 || pen == 0)
            return;

        try
        {
            GdiPlus.GdipSetPenStartCap(pen, GdiPlus.LineCapRound);
            GdiPlus.GdipSetPenEndCap(pen, GdiPlus.LineCapRound);
            float d = SF(4.2f);
            float dy = pointingUp ? -d * 0.55f : d * 0.55f;
            float baseY = pointingUp ? d * 0.45f : -d * 0.45f;
            GdiPlus.GdipDrawLine(gfx, pen, cx - d, cy + baseY, cx, cy + dy);
            GdiPlus.GdipDrawLine(gfx, pen, cx, cy + dy, cx + d, cy + baseY);
        }
        finally
        {
            GdiPlus.GdipDeletePen(pen);
        }
    }

    private void DrawOpenDropdownOverlay(nint dc, nint gfx, int w, int h, int headerH)
    {
        if (_openDropdownAnchor.Bottom <= headerH)
        {
            _openDropdown = DropdownKind.None;
            return;
        }

        // Click anywhere outside closes the open dropdown
        _hitRegions.Add(new HitRegion(
            new Win32.RECT { Left = 0, Top = 0, Right = w, Bottom = h },
            "dropdown_backdrop",
            () => _openDropdown = DropdownKind.None,
            IsButton: false));

        int menuW = _openDropdown == DropdownKind.TaskInstall
            ? Math.Max(S(392), _openDropdownAnchor.Right - _openDropdownAnchor.Left)
            : Math.Max(S(216), _openDropdownAnchor.Right - _openDropdownAnchor.Left);
        int menuX = Math.Clamp(_openDropdownAnchor.Right - menuW, S(12), Math.Max(S(12), w - menuW - S(12)));
        int itemH = _openDropdown == DropdownKind.TaskInstall ? S(58) : S(32);
        int padV = S(6);
        int itemCount = _openDropdown switch
        {
            DropdownKind.Language => Strings.Languages.Length,
            DropdownKind.TaskInstall => 2,
            _ => Theme.Presets.Length
        };
        int menuH = itemCount * itemH + padV * 2;

        int menuY = _openDropdownAnchor.Bottom + S(6);
        if (menuY + menuH > h - S(12))
            menuY = _openDropdownAnchor.Top - S(6) - menuH;
        menuY = Math.Clamp(menuY, headerH + S(6), Math.Max(headerH + S(6), h - S(12) - menuH));

        // 2-layer subtle elevation shadow
        GdiPlus.FillRoundedRect(gfx, menuX - S(2), menuY + S(4), menuW + S(4), menuH + S(6), SF(12), GdiPlus.Argb(0x000000, 48));
        GdiPlus.FillRoundedRect(gfx, menuX, menuY + S(2), menuW, menuH + S(2), SF(10), GdiPlus.Argb(0x000000, 22));

        GdiPlus.FillAndStrokeRoundedRect(gfx, menuX, menuY, menuW, menuH, SF(10),
            GdiPlus.Argb(Theme.BgCardElevated), GdiPlus.Argb(Theme.BorderStrong));

        _hitRegions.Add(new HitRegion(
            new Win32.RECT { Left = menuX, Top = menuY, Right = menuX + menuW, Bottom = menuY + menuH },
            "dropdown_panel",
            () => { },
            IsButton: false));

        int itemPadX = S(6);
        int ix = menuX + itemPadX;
        int iw = menuW - itemPadX * 2;

        if (_openDropdown == DropdownKind.Language)
        {
            var langs = Strings.Languages;
            for (int i = 0; i < langs.Length; i++)
            {
                var lang = langs[i];
                int iy = menuY + padV + i * itemH;
                bool selected = Strings.Current == lang.Language;
                string itemId = $"dd_item_lang_{lang.Code}";
                bool hovered = string.Equals(_hoverId, itemId, StringComparison.Ordinal);

                if (selected || hovered)
                {
                    uint bg = selected ? Theme.AccentSoft : Theme.BgControlHover;
                    GdiPlus.FillRoundedRect(gfx, ix, iy + S(1), iw, itemH - S(2), SF(7), GdiPlus.Argb(bg));
                }

                DrawText(dc, lang.NativeName, ix + S(10), iy, ix + iw - S(22), iy + itemH,
                    12, selected ? Win32.FW_SEMIBOLD : Win32.FW_MEDIUM, selected ? Theme.Accent : Theme.TextPrimary);

                if (selected)
                {
                    int dotSz = S(6);
                    FillCircleArgb(gfx, GdiPlus.Argb(Theme.Accent), ix + iw - S(14), iy + (itemH - dotSz) / 2f, dotSz);
                }

                string code = lang.Code;
                _hitRegions.Add(new HitRegion(
                    new Win32.RECT { Left = ix, Top = iy, Right = ix + iw, Bottom = iy + itemH },
                    itemId,
                    () =>
                    {
                        _openDropdown = DropdownKind.None;
                        _stateStore.UpdateSettings(s => s.Language = code);
                    }));
            }
        }
        else if (_openDropdown == DropdownKind.TaskInstall)
        {
            DrawTaskInstallMenuItem(
                dc, gfx, ix, menuY + padV, iw, itemH,
                id: "dd_task_elevated",
                dotColor: Theme.Green,
                title: Strings.TaskOptElevatedTitle,
                subtitle: Strings.TaskOptElevatedSub,
                onClick: () =>
                {
                    _openDropdown = DropdownKind.None;
                    if (_taskOpBusy) return;
                    _taskOpBusy = true;
                    _startupService.InstallScheduledTask(
                        elevated: true,
                        _settings.StartMinimized,
                        (success, message) => BeginInvoke(() =>
                        {
                            _taskOpBusy = false;
                            ShowStatusBanner(message, !success);
                        }));
                });

            DrawTaskInstallMenuItem(
                dc, gfx, ix, menuY + padV + itemH, iw, itemH,
                id: "dd_task_normal",
                dotColor: Theme.Accent,
                title: Strings.TaskOptNormalTitle,
                subtitle: Strings.TaskOptNormalSub,
                onClick: () =>
                {
                    _openDropdown = DropdownKind.None;
                    if (_taskOpBusy) return;
                    _taskOpBusy = true;
                    _startupService.InstallScheduledTask(
                        elevated: false,
                        _settings.StartMinimized,
                        (success, message) => BeginInvoke(() =>
                        {
                            _taskOpBusy = false;
                            ShowStatusBanner(message, !success);
                        }));
                });
        }
        else
        {
            var presets = Theme.Presets;
            for (int i = 0; i < presets.Length; i++)
            {
                var preset = presets[i];
                int iy = menuY + padV + i * itemH;
                bool selected = string.Equals(Theme.CurrentId, preset.Id, StringComparison.OrdinalIgnoreCase);
                string itemId = $"dd_item_theme_{preset.Id}";
                bool hovered = string.Equals(_hoverId, itemId, StringComparison.Ordinal);

                if (selected || hovered)
                {
                    uint bg = selected ? Theme.AccentSoft : Theme.BgControlHover;
                    GdiPlus.FillRoundedRect(gfx, ix, iy + S(1), iw, itemH - S(2), SF(7), GdiPlus.Argb(bg));
                }

                int swSz = S(16);
                int swX = ix + S(10);
                int swY = iy + (itemH - swSz) / 2;
                FillCircleArgb(gfx, GdiPlus.Argb(preset.SwatchBg), swX, swY, swSz);
                StrokeCircleArgb(gfx, GdiPlus.Argb(selected ? Theme.Accent : Theme.BorderStrong), MathF.Max(1.2f, SF(1.2f)), swX, swY, swSz);
                int dotSz = S(7);
                FillCircleArgb(gfx, GdiPlus.Argb(preset.SwatchAccent),
                    swX + (swSz - dotSz) / 2f, swY + (swSz - dotSz) / 2f, dotSz);

                DrawText(dc, Strings.ThemeName(preset.Id), swX + swSz + S(8), iy, ix + iw - S(22), iy + itemH,
                    12, selected ? Win32.FW_SEMIBOLD : Win32.FW_MEDIUM, selected ? Theme.Accent : Theme.TextPrimary);

                if (selected)
                {
                    int checkDot = S(6);
                    FillCircleArgb(gfx, GdiPlus.Argb(Theme.Accent), ix + iw - S(14), iy + (itemH - checkDot) / 2f, checkDot);
                }

                string themeId = preset.Id;
                _hitRegions.Add(new HitRegion(
                    new Win32.RECT { Left = ix, Top = iy, Right = ix + iw, Bottom = iy + itemH },
                    itemId,
                    () =>
                    {
                        _openDropdown = DropdownKind.None;
                        _stateStore.UpdateSettings(s => s.Theme = themeId);
                    }));
            }
        }
    }

    private void DrawTaskInstallMenuItem(
        nint dc, nint gfx, int ix, int iy, int iw, int itemH,
        string id, uint dotColor, string title, string subtitle, Action onClick)
    {
        bool hovered = string.Equals(_hoverId, id, StringComparison.Ordinal);
        if (hovered)
        {
            GdiPlus.FillRoundedRect(gfx, ix, iy + S(1), iw, itemH - S(2), SF(7), GdiPlus.Argb(Theme.BgControlHover));
        }

        int dotSz = S(8);
        FillCircleArgb(gfx, GdiPlus.Argb(dotColor), ix + S(10), iy + S(16), dotSz);
        DrawText(dc, title, ix + S(26), iy + S(7), ix + iw - S(10), iy + S(28),
            12, Win32.FW_SEMIBOLD, hovered ? Theme.Accent : Theme.TextPrimary);
        DrawText(dc, subtitle, ix + S(26), iy + S(29), ix + iw - S(10), iy + S(49),
            10, Win32.FW_NORMAL, Theme.TextSecondary);

        _hitRegions.Add(new HitRegion(
            new Win32.RECT { Left = ix, Top = iy, Right = ix + iw, Bottom = iy + itemH },
            id,
            onClick));
    }

    private static void FillCircleArgb(nint gfx, uint argb, float x, float y, float d)
    {
        if (GdiPlus.GdipCreateSolidFill(argb, out nint br) == 0 && br != 0)
        {
            try { GdiPlus.GdipFillEllipse(gfx, br, x, y, d, d); }
            finally { GdiPlus.GdipDeleteBrush(br); }
        }
    }

    private static void StrokeCircleArgb(nint gfx, uint argb, float width, float x, float y, float d)
    {
        if (GdiPlus.GdipCreatePen1(argb, width, GdiPlus.UnitPixel, out nint pen) == 0 && pen != 0)
        {
            try { GdiPlus.GdipDrawEllipse(gfx, pen, x, y, d, d); }
            finally { GdiPlus.GdipDeletePen(pen); }
        }
    }

    private int DrawElevationTaskCard(nint dc, nint gfx, int x, int y, int w, int headerH)
    {
        int rowH = S(74);
        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, w, rowH, SF(10),
            GdiPlus.Argb(Theme.BgCard), GdiPlus.Argb(Theme.BorderSubtle));

        int pad = S(16);
        bool isElev = _startupService.IsElevated;
        bool hasTask = _startupService.IsScheduledTaskInstalled;
        bool isTaskElev = _startupService.IsScheduledTaskElevated;

        int btnH = S(32);
        int btnY = y + (rowH - btnH) / 2;
        int taskBtnW = S(138);
        int taskBtnX = x + w - pad - taskBtnW;
        int rightLimit = taskBtnX - S(12);

        if (!isElev)
        {
            int elevBtnW = S(138);
            int elevBtnX = taskBtnX - S(8) - elevBtnW;
            rightLimit = elevBtnX - S(12);

            if (y + rowH > headerH)
            {
                DrawActionButton(dc, gfx, "set_restart_admin", Strings.BtnRestartAdmin, elevBtnX, btnY, elevBtnW, btnH,
                    Theme.AmberSoft, Theme.AmberSoft, Theme.Amber, Theme.Amber,
                    () =>
                    {
                        if (_startupService.RestartAsAdministrator())
                            ExitRequested?.Invoke();
                        else
                            ShowStatusBanner(Strings.MsgRestartAdminCancelled, isError: true);
                    });
            }
        }

        if (_openDropdown == DropdownKind.TaskInstall)
        {
            _openDropdownAnchor = new Win32.RECT { Left = taskBtnX, Top = btnY, Right = taskBtnX + taskBtnW, Bottom = btnY + btnH };
        }

        if (y + rowH > headerH)
        {
            bool taskHovered = string.Equals(_hoverId, "set_sched_task", StringComparison.Ordinal);
            if (hasTask || _taskOpBusy)
            {
                DrawActionButton(dc, gfx, "set_sched_task",
                    _taskOpBusy ? Strings.BtnTaskBusy : Strings.BtnTaskRemove,
                    taskBtnX, btnY, taskBtnW, btnH,
                    hasTask ? Theme.RedSoft : Theme.AccentSoft,
                    hasTask ? Theme.RedSoft : Theme.Accent,
                    hasTask ? Theme.Red : Theme.TextPrimary,
                    hasTask ? Theme.Red : Theme.Accent,
                    () =>
                    {
                        if (_taskOpBusy || !hasTask) return;
                        _taskOpBusy = true;
                        _startupService.RemoveScheduledTask((success, message) => BeginInvoke(() =>
                        {
                            _taskOpBusy = false;
                            ShowStatusBanner(message, !success);
                        }));
                    });
            }
            else
            {
                bool taskOpen = _openDropdown == DropdownKind.TaskInstall;
                uint btnBg = taskOpen || taskHovered ? Theme.Accent : Theme.AccentSoft;
                uint btnFg = taskOpen || taskHovered ? Theme.OnAccent : Theme.TextPrimary;
                GdiPlus.FillAndStrokeRoundedRect(gfx, taskBtnX, btnY, taskBtnW, btnH, SF(7),
                    GdiPlus.Argb(btnBg), GdiPlus.Argb(Theme.Accent));
                DrawText(dc, Strings.BtnTaskInstall, taskBtnX + S(10), btnY, taskBtnX + taskBtnW - S(22), btnY + btnH,
                    12, Win32.FW_SEMIBOLD, btnFg, centered: true);
                DrawDropdownChevron(gfx, taskBtnX + taskBtnW - S(15), btnY + btnH / 2, taskOpen, btnFg);
                RegisterScrollableHit(
                    new Win32.RECT { Left = taskBtnX, Top = btnY, Right = taskBtnX + taskBtnW, Bottom = btnY + btnH },
                    "set_sched_task",
                    () =>
                    {
                        CancelHotkeyCapture();
                        _openDropdown = _openDropdown == DropdownKind.TaskInstall ? DropdownKind.None : DropdownKind.TaskInstall;
                    });
            }
        }

        string titleText = Strings.ElevCardTitle;
        int titleW = MeasureTextWidth(dc, titleText, 13, Win32.FW_SEMIBOLD) + S(8);
        int titleRight = Math.Min(rightLimit, x + pad + titleW);
        DrawText(dc, titleText, x + pad, y + S(12), titleRight, y + S(34), 13, Win32.FW_SEMIBOLD, Theme.TextPrimary);

        int bx = titleRight + S(6);
        string adminText = isElev ? Strings.BadgeAdmin : Strings.BadgeStandard;
        int adminPillW = MeasureTextWidth(dc, adminText, 10, Win32.FW_SEMIBOLD) + S(20);
        if (bx + adminPillW <= rightLimit)
        {
            bx += DrawPillBadge(dc, gfx, bx, y + S(12),
                adminText,
                isElev ? Theme.Green : Theme.Amber,
                isElev ? Theme.GreenSoft : Theme.AmberSoft) + S(6);
        }

        string taskText = hasTask
            ? (isTaskElev ? Strings.BadgeTaskInstalledElevated : Strings.BadgeTaskInstalledNormal)
            : Strings.BadgeTaskMissing;
        uint taskPillFg = hasTask ? (isTaskElev ? Theme.Green : Theme.Accent) : Theme.TextSecondary;
        uint taskPillBg = hasTask ? (isTaskElev ? Theme.GreenSoft : Theme.AccentSoft) : Theme.BgControl;
        int taskPillW = MeasureTextWidth(dc, taskText, 10, Win32.FW_SEMIBOLD) + S(20);
        if (bx + taskPillW <= rightLimit)
        {
            DrawPillBadge(dc, gfx, bx, y + S(12), taskText, taskPillFg, taskPillBg);
        }

        DrawText(dc, Strings.ElevCardSub,
            x + pad, y + S(38), rightLimit, y + S(60), 11, Win32.FW_NORMAL, Theme.TextSecondary);

        return y + rowH;
    }

    private int DrawBrowserBridgeCard(nint dc, nint gfx, int x, int y, int w, int headerH)
    {
        int rowH = S(104);
        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, w, rowH, SF(10),
            GdiPlus.Argb(Theme.BgCard), GdiPlus.Argb(Theme.BorderSubtle));

        int pad = S(16);
        var (browsers, tabs) = _bridgeServer.GetStatusSnapshot();
        bool isConnected = browsers > 0;

        string statusText = isConnected ? Strings.BadgeBridgeConnected(browsers, tabs) : Strings.BadgeBridgeDisconnected;
        int pillW = Math.Max(S(54), MeasureTextWidth(dc, statusText, 10, Win32.FW_SEMIBOLD) + S(20));
        int titleW = MeasureTextWidth(dc, Strings.BridgeCardTitle, 13, Win32.FW_SEMIBOLD) + S(8);
        int maxTitleRight = x + w - pad - pillW - S(8);
        int titleRight = Math.Min(Math.Max(x + pad + S(100), maxTitleRight), x + pad + titleW);
        DrawText(dc, Strings.BridgeCardTitle, x + pad, y + S(12), titleRight, y + S(34), 13, Win32.FW_SEMIBOLD, Theme.TextPrimary);

        int pillX = titleRight + S(6);
        if (pillX + pillW <= x + w - pad)
        {
            DrawPillBadge(dc, gfx, pillX, y + S(12),
                statusText,
                isConnected ? Theme.Green : Theme.TextSecondary,
                isConnected ? Theme.GreenSoft : Theme.BgControl);
        }

        DrawText(dc, Strings.BridgeCardSub, x + pad, y + S(36), x + w - pad, y + S(56), 11, Win32.FW_NORMAL, Theme.TextSecondary);

        int gap = S(8);
        int btnY = y + S(60);
        int btnH = S(32);
        int folderW = S(104);
        int remW = w - pad * 2 - folderW - gap * 3;
        int mainBtnW = Math.Max(S(110), remW / 3);
        int folderX = x + pad + (mainBtnW + gap) * 3;
        int actualFolderW = Math.Max(folderW, (x + w - pad) - folderX);

        if (y + rowH > headerH)
        {
            var primaryBrowser = ExtensionSetupService.GetPrimaryInstalledBrowserKind();

            void DrawBridgeBrowserButton(string id, string label, int bx, int bw, bool isPrimary, Action onClick)
            {
                bool hovered = string.Equals(_hoverId, id, StringComparison.Ordinal);
                DrawActionButton(dc, gfx, id, label, bx, btnY, bw, btnH,
                    isPrimary ? Theme.AccentSoft : Theme.BgControl,
                    isPrimary ? Theme.Accent : Theme.BgControlHover,
                    isPrimary && hovered ? Theme.OnAccent : Theme.TextPrimary,
                    isPrimary ? Theme.Accent : Theme.BorderStrong,
                    onClick);
            }

            DrawBridgeBrowserButton("btn_ext_edge", Strings.BtnInstallEdge, x + pad, mainBtnW,
                primaryBrowser == DetectedBrowserKind.Edge,
                () =>
                {
                    var (ok, msg) = ExtensionSetupService.LaunchEdgeStoreInstall();
                    ShowStatusBanner(msg, isError: !ok);
                });

            DrawBridgeBrowserButton("btn_ext_chromium", Strings.BtnInstallChromium, x + pad + mainBtnW + gap, mainBtnW,
                primaryBrowser == DetectedBrowserKind.Chromium,
                () =>
                {
                    _guideWindow ??= new ExtensionGuideWindow(_hwnd, _bridgeServer);
                    _guideWindow.ShowFor(ExtensionGuideMode.Chromium);
                });

            DrawBridgeBrowserButton("btn_ext_firefox", Strings.BtnInstallFirefox, x + pad + (mainBtnW + gap) * 2, mainBtnW,
                primaryBrowser == DetectedBrowserKind.Firefox,
                () =>
                {
                    _guideWindow ??= new ExtensionGuideWindow(_hwnd, _bridgeServer);
                    _guideWindow.ShowFor(ExtensionGuideMode.Firefox);
                });

            DrawActionButton(dc, gfx, "btn_ext_folder", Strings.BtnOpenExtFolder,
                folderX, btnY, actualFolderW, btnH,
                Theme.BgControl, Theme.BgControlHover, Theme.TextPrimary, Theme.BorderStrong,
                () =>
                {
                    if (!ExtensionSetupService.OpenExtensionFolder())
                        ShowStatusBanner(Strings.MsgBridgeFolderMissing, isError: true);
                });
        }

        return y + rowH;
    }

    private int DrawUtilityActionsRow(nint dc, nint gfx, int x, int y, int w, int headerH)
    {
        int rowH = S(56);
        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, w, rowH, SF(10),
            GdiPlus.Argb(Theme.BgCard), GdiPlus.Argb(Theme.BorderSubtle));

        int pad = S(14);
        int gap = S(10);
        int btnW = (w - pad * 2 - gap * 2) / 3;
        int btnH = S(34);
        int btnY = y + (rowH - btnH) / 2;

        if (y + rowH > headerH)
        {
            DrawActionButton(dc, gfx, "util_rehook", Strings.BtnRehook, x + pad, btnY, btnW, btnH,
                Theme.BgControl, Theme.BgControlHover, Theme.TextPrimary, Theme.BorderStrong,
                () =>
                {
                    _refreshHooksCallback?.Invoke();
                    ShowStatusBanner(Strings.MsgHooksRefreshed);
                });

            DrawActionButton(dc, gfx, "util_open_cfg", Strings.BtnOpenConfigFolder, x + pad + btnW + gap, btnY, btnW, btnH,
                Theme.BgControl, Theme.BgControlHover, Theme.TextPrimary, Theme.BorderStrong,
                () => StartupService.OpenConfigFolder(_stateStore.FilePath));

            DrawActionButton(dc, gfx, "util_reset_set", Strings.BtnResetDefaults, x + pad + (btnW + gap) * 2, btnY, btnW, btnH,
                Theme.BgControl, Theme.RedSoft, Theme.Red, Theme.BorderStrong,
                () =>
                {
                    _stateStore.ResetSettingsToDefaults();
                    ShowStatusBanner(Strings.MsgSettingsReset);
                });
        }

        return y + rowH;
    }

    private int DrawToggleSettingRow(
        nint dc, nint gfx, int x, int y, int w, int headerH, string id,
        string title, string subtitle, bool value, Action onToggle)
    {
        int rowH = S(62);
        bool hovered = string.Equals(_hoverId, id, StringComparison.Ordinal);
        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, w, rowH, SF(10),
            GdiPlus.Argb(hovered ? Theme.BgCardHover : Theme.BgCard),
            GdiPlus.Argb(Theme.BorderSubtle));

        int pad = S(16);
        int swW = S(46);
        int swH = S(24);
        int swX = x + w - pad - swW;
        int swY = y + (rowH - swH) / 2;

        int textRight = swX - S(16);
        DrawText(dc, title, x + pad, y + S(10), textRight, y + S(31), 13, Win32.FW_SEMIBOLD, Theme.TextPrimary);
        DrawText(dc, subtitle, x + pad, y + S(32), textRight, y + S(52), 11, Win32.FW_NORMAL, Theme.TextSecondary);

        // Toggle switch pill
        uint trackColor = value ? Theme.Accent : Theme.SwitchOffTrack;
        GdiPlus.FillRoundedRect(gfx, swX, swY, swW, swH, swH / 2f, GdiPlus.Argb(trackColor));

        int knobD = swH - S(6);
        int knobX = value ? swX + swW - knobD - S(3) : swX + S(3);
        int knobY = swY + S(3);
        if (GdiPlus.GdipCreateSolidFill(GdiPlus.Argb(Theme.SwitchKnob), out nint knobBrush) == 0 && knobBrush != 0)
        {
            try { GdiPlus.GdipFillEllipse(gfx, knobBrush, knobX, knobY, knobD, knobD); }
            finally { GdiPlus.GdipDeleteBrush(knobBrush); }
        }

        if (y + rowH > headerH)
        {
            RegisterScrollableHit(
                new Win32.RECT { Left = x, Top = y, Right = x + w, Bottom = y + rowH },
                id,
                onToggle);
        }

        return y + rowH;
    }

    private int DrawStepperSettingRow(
        nint dc, nint gfx, int x, int y, int w, int headerH, string id,
        string title, string subtitle, string valueText, Action onDec, Action onInc)
    {
        int rowH = S(62);
        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, w, rowH, SF(10),
            GdiPlus.Argb(Theme.BgCard), GdiPlus.Argb(Theme.BorderSubtle));

        int pad = S(16);
        int btnSz = S(28);
        int valW = S(66);
        int btnY = y + (rowH - btnSz) / 2;
        int incX = x + w - pad - btnSz;
        int valX = incX - S(6) - valW;
        int decX = valX - S(6) - btnSz;

        int textRight = decX - S(16);
        DrawText(dc, title, x + pad, y + S(10), textRight, y + S(31), 13, Win32.FW_SEMIBOLD, Theme.TextPrimary);
        DrawText(dc, subtitle, x + pad, y + S(32), textRight, y + S(52), 11, Win32.FW_NORMAL, Theme.TextSecondary);

        DrawText(dc, valueText, valX, y, valX + valW, y + rowH, 13, Win32.FW_SEMIBOLD, Theme.TextPrimary, centered: true);

        if (y + rowH > headerH)
        {
            DrawActionButton(dc, gfx, $"{id}_dec", "−", decX, btnY, btnSz, btnSz,
                Theme.BgControl, Theme.BgControlHover, Theme.TextPrimary, Theme.BorderStrong, onDec);
            DrawActionButton(dc, gfx, $"{id}_inc", "+", incX, btnY, btnSz, btnSz,
                Theme.BgControl, Theme.BgControlHover, Theme.TextPrimary, Theme.BorderStrong, onInc);
        }

        return y + rowH;
    }

    private int DrawEmptyCard(nint dc, nint gfx, int x, int y, int w, string heading, string detail)
    {
        int h = S(76);
        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, w, h, SF(10),
            GdiPlus.Argb(Theme.BgCard), GdiPlus.Argb(Theme.BorderSubtle));
        DrawText(dc, heading, x + S(16), y + S(14), x + w - S(16), y + S(36), 13, Win32.FW_SEMIBOLD, Theme.TextPrimary);
        DrawText(dc, detail, x + S(16), y + S(38), x + w - S(16), y + S(64), 11, Win32.FW_NORMAL, Theme.TextSecondary);
        return y + h;
    }

    private enum TransportGlyph { Previous, Play, Pause, Next }

    private void DrawTransportButton(nint dc, nint gfx, string id, TransportGlyph glyph, int x, int y, int sz, Action onClick, bool accent = false)
    {
        bool hovered = string.Equals(_hoverId, id, StringComparison.Ordinal);
        uint bg = accent
            ? (hovered ? Theme.AccentHover : Theme.Accent)
            : (hovered ? Theme.BgControlHover : Theme.BgControl);
        uint border = accent ? Theme.Accent : Theme.BorderStrong;
        uint fg = accent ? Theme.OnAccent : Theme.TextPrimary;

        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, sz, sz, SF(8), GdiPlus.Argb(bg), GdiPlus.Argb(border));

        if (GdiPlus.GdipCreateSolidFill(GdiPlus.Argb(fg), out nint brush) == 0 && brush != 0)
        {
            try
            {
                float cx = x + sz / 2f;
                float cy = y + sz / 2f;
                float r = sz * 0.18f;

                switch (glyph)
                {
                    case TransportGlyph.Play:
                        DrawTriangle(gfx, brush, cx - r * 0.6f, cy - r, cx - r * 0.6f, cy + r, cx + r * 1.1f, cy);
                        break;

                    case TransportGlyph.Pause:
                        float bw = MathF.Max(2.2f, sz * 0.08f);
                        float bh = r * 1.9f;
                        GdiPlus.GdipFillRectangle(gfx, brush, cx - bw * 1.4f, cy - bh / 2f, bw, bh);
                        GdiPlus.GdipFillRectangle(gfx, brush, cx + bw * 0.4f, cy - bh / 2f, bw, bh);
                        break;

                    case TransportGlyph.Next:
                        DrawTriangle(gfx, brush, cx - r * 0.9f, cy - r * 0.85f, cx - r * 0.9f, cy + r * 0.85f, cx + r * 0.45f, cy);
                        GdiPlus.GdipFillRectangle(gfx, brush, cx + r * 0.55f, cy - r * 0.85f, MathF.Max(2f, sz * 0.07f), r * 1.7f);
                        break;

                    case TransportGlyph.Previous:
                        float barW = MathF.Max(2f, sz * 0.07f);
                        GdiPlus.GdipFillRectangle(gfx, brush, cx - r * 0.85f, cy - r * 0.85f, barW, r * 1.7f);
                        DrawTriangle(gfx, brush, cx + r * 0.9f, cy - r * 0.85f, cx + r * 0.9f, cy + r * 0.85f, cx - r * 0.45f, cy);
                        break;
                }
            }
            finally
            {
                GdiPlus.GdipDeleteBrush(brush);
            }
        }

        RegisterScrollableHit(new Win32.RECT { Left = x, Top = y, Right = x + sz, Bottom = y + sz }, id, onClick);
    }

    private static void DrawTriangle(nint gfx, nint brush, float x1, float y1, float x2, float y2, float x3, float y3)
    {
        if (GdiPlus.GdipCreatePath(GdiPlus.FillModeAlternate, out nint path) != 0 || path == 0) return;
        try
        {
            GdiPlus.GdipAddPathLine(path, x1, y1, x2, y2);
            GdiPlus.GdipAddPathLine(path, x2, y2, x3, y3);
            GdiPlus.GdipClosePathFigure(path);
            GdiPlus.GdipFillPath(gfx, brush, path);
        }
        finally
        {
            GdiPlus.GdipDeletePath(path);
        }
    }

    private void DrawCardGlow(nint gfx, float x, float y, float w, float h, float radius, uint colorRgb, float intensity)
    {
        if (intensity <= 0.01f)
            return;

        float e2 = SF(3.2f);
        GdiPlus.DrawRoundedRect(
            gfx,
            x - e2, y - e2, w + e2 * 2f, h + e2 * 2f,
            radius + e2,
            GdiPlus.Argb(colorRgb, (byte)Math.Clamp(intensity * (Theme.IsLight ? 48f : 68f), 0f, 255f)),
            MathF.Max(2.2f, SF(2.8f)));

        float e1 = SF(1.3f);
        GdiPlus.DrawRoundedRect(
            gfx,
            x - e1, y - e1, w + e1 * 2f, h + e1 * 2f,
            radius + e1,
            GdiPlus.Argb(colorRgb, (byte)Math.Clamp(intensity * (Theme.IsLight ? 115f : 155f), 0f, 255f)),
            MathF.Max(1.4f, SF(1.8f)));

        GdiPlus.FillRoundedRect(
            gfx,
            x, y, w, h,
            radius,
            GdiPlus.Argb(colorRgb, (byte)Math.Clamp(intensity * (Theme.IsLight ? 24f : 36f), 0f, 255f)));

        GdiPlus.DrawRoundedRect(
            gfx,
            x, y, w, h,
            radius,
            GdiPlus.Argb(colorRgb, (byte)Math.Clamp(intensity * 245f, 0f, 255f)),
            MathF.Max(1.5f, SF(1.7f)));
    }

    private void DrawDirectionArrowGlyph(nint gfx, float cx, float cy, float size, bool up, uint argb)
    {
        if (GdiPlus.GdipCreatePen1(argb, MathF.Max(1.4f, SF(1.5f)), GdiPlus.UnitPixel, out nint pen) != 0 || pen == 0)
            return;

        try
        {
            GdiPlus.GdipSetPenStartCap(pen, GdiPlus.LineCapRound);
            GdiPlus.GdipSetPenEndCap(pen, GdiPlus.LineCapRound);
            float h = size * 0.42f;
            float w = size * 0.34f;
            if (up)
            {
                GdiPlus.GdipDrawLine(gfx, pen, cx, cy - h, cx, cy + h);
                GdiPlus.GdipDrawLine(gfx, pen, cx - w, cy - h * 0.1f, cx, cy - h);
                GdiPlus.GdipDrawLine(gfx, pen, cx + w, cy - h * 0.1f, cx, cy - h);
            }
            else
            {
                GdiPlus.GdipDrawLine(gfx, pen, cx, cy - h, cx, cy + h);
                GdiPlus.GdipDrawLine(gfx, pen, cx - w, cy + h * 0.1f, cx, cy + h);
                GdiPlus.GdipDrawLine(gfx, pen, cx + w, cy + h * 0.1f, cx, cy + h);
            }
        }
        finally
        {
            GdiPlus.GdipDeletePen(pen);
        }
    }

    private enum SmallButtonGlyph { Up, Down, Close }

    private void DrawIconButton(nint dc, nint gfx, string id, SmallButtonGlyph glyph, int x, int y, int sz, bool enabled, Action onClick, bool danger = false, int hitY = -1)
    {
        bool hovered = enabled && string.Equals(_hoverId, id, StringComparison.Ordinal);
        uint bg = !enabled ? Theme.BgCard : (hovered ? (danger ? Theme.RedSoft : Theme.BgControlHover) : Theme.BgControl);
        uint fg = !enabled ? Theme.TextMuted : (danger && hovered ? Theme.Red : Theme.TextPrimary);

        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, sz, sz, SF(6), GdiPlus.Argb(bg), GdiPlus.Argb(Theme.BorderSubtle));

        if (GdiPlus.GdipCreatePen1(GdiPlus.Argb(fg), MathF.Max(1.4f, SF(1.6f)), GdiPlus.UnitPixel, out nint pen) == 0 && pen != 0)
        {
            try
            {
                GdiPlus.GdipSetPenStartCap(pen, GdiPlus.LineCapRound);
                GdiPlus.GdipSetPenEndCap(pen, GdiPlus.LineCapRound);
                float cx = x + sz / 2f;
                float cy = y + sz / 2f;
                float d = sz * 0.16f;

                switch (glyph)
                {
                    case SmallButtonGlyph.Up:
                        GdiPlus.GdipDrawLine(gfx, pen, cx - d, cy + d * 0.45f, cx, cy - d * 0.55f);
                        GdiPlus.GdipDrawLine(gfx, pen, cx, cy - d * 0.55f, cx + d, cy + d * 0.45f);
                        break;
                    case SmallButtonGlyph.Down:
                        GdiPlus.GdipDrawLine(gfx, pen, cx - d, cy - d * 0.45f, cx, cy + d * 0.55f);
                        GdiPlus.GdipDrawLine(gfx, pen, cx, cy + d * 0.55f, cx + d, cy - d * 0.45f);
                        break;
                    case SmallButtonGlyph.Close:
                        GdiPlus.GdipDrawLine(gfx, pen, cx - d, cy - d, cx + d, cy + d);
                        GdiPlus.GdipDrawLine(gfx, pen, cx + d, cy - d, cx - d, cy + d);
                        break;
                }
            }
            finally
            {
                GdiPlus.GdipDeletePen(pen);
            }
        }

        if (enabled)
        {
            int regY = hitY >= 0 ? hitY : y;
            RegisterScrollableHit(new Win32.RECT { Left = x, Top = regY, Right = x + sz, Bottom = regY + sz }, id, onClick);
        }
    }

    private void RegisterScrollableHit(Win32.RECT bounds, string id, Action onClick)
    {
        if (_bodyClipTop > 0 && bounds.Top < _bodyClipTop)
            bounds.Top = _bodyClipTop;
        if (bounds.Bottom > bounds.Top && bounds.Right > bounds.Left)
            _hitRegions.Add(new HitRegion(bounds, id, onClick));
    }

    private void DrawActionButton(
        nint dc, nint gfx, string id, string text,
        int x, int y, int w, int h,
        uint bgNormal, uint bgHover, uint fg, uint border, Action onClick)
    {
        bool hovered = string.Equals(_hoverId, id, StringComparison.Ordinal);
        GdiPlus.FillAndStrokeRoundedRect(gfx, x, y, w, h, SF(7),
            GdiPlus.Argb(hovered ? bgHover : bgNormal),
            GdiPlus.Argb(border));
        DrawText(dc, text, x, y, x + w, y + h, 12, Win32.FW_SEMIBOLD, fg, centered: true);
        RegisterScrollableHit(new Win32.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h }, id, onClick);
    }

    private int DrawPillBadge(nint dc, nint gfx, int x, int y, string text, uint fg, uint bg)
    {
        int pillW = Math.Max(S(54), MeasureTextWidth(dc, text, 10, Win32.FW_SEMIBOLD) + S(20));
        int pillH = S(22);
        GdiPlus.FillRoundedRect(gfx, x, y, pillW, pillH, pillH / 2f, GdiPlus.Argb(bg));
        DrawText(dc, text, x, y, x + pillW, y + pillH, 10, Win32.FW_SEMIBOLD, fg, centered: true);
        return pillW;
    }

    private void DrawPillBadgeFixed(nint dc, nint gfx, int x, int y, int pillW, string text, uint fg, uint bg)
    {
        int pillH = S(22);
        GdiPlus.FillRoundedRect(gfx, x, y, pillW, pillH, pillH / 2f, GdiPlus.Argb(bg));
        DrawText(dc, text, x, y, x + pillW, y + pillH, 10, Win32.FW_SEMIBOLD, fg, centered: true);
    }

    private nint GetOrCreateFont(int ptSize, int weight)
    {
        string face = Strings.PreferredFontName;
        var key = (ptSize, weight, face, _dpi);
        if (_fontCache.TryGetValue(key, out nint cached) && cached != 0)
            return cached;

        if (_fontCache.Count >= 64)
            ClearFontCache();

        nint created = Win32.CreateFontW(
            -S(ptSize), 0, 0, 0, weight, 0, 0, 0,
            1, 0, 0, Win32.CLEARTYPE_QUALITY, 0, face);
        if (created != 0)
            _fontCache[key] = created;
        return created;
    }

    private void ClearFontCache()
    {
        foreach (nint font in _fontCache.Values)
        {
            if (font != 0) Win32.DeleteObject(font);
        }
        _fontCache.Clear();
    }

    private int MeasureTextWidth(nint dc, string text, int ptSize, int weight)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        nint font = GetOrCreateFont(ptSize, weight);
        if (font == 0)
            return (int)MathF.Ceiling(text.Length * S(ptSize) * 0.6f);

        nint old = Win32.SelectObject(dc, font);
        try
        {
            var rc = new Win32.RECT { Left = 0, Top = 0, Right = 2000, Bottom = S(40) };
            Win32.DrawTextW(dc, text, -1, &rc, Win32.DT_CALCRECT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
            return Math.Max(0, rc.Right - rc.Left);
        }
        finally
        {
            Win32.SelectObject(dc, old);
        }
    }

    private void DrawText(nint dc, string text, int left, int top, int right, int bottom, int ptSize, int weight, uint rgb, bool centered = false)
    {
        if (right <= left || bottom <= top) return;
        nint font = GetOrCreateFont(ptSize, weight);
        if (font == 0) return;

        nint old = Win32.SelectObject(dc, font);
        try
        {
            Win32.SetTextColor(dc, Theme.ToColorRef(rgb));
            var rc = new Win32.RECT { Left = left, Top = top, Right = right, Bottom = bottom };
            uint fmt = (centered ? Win32.DT_CENTER : Win32.DT_LEFT)
                       | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_END_ELLIPSIS | Win32.DT_NOPREFIX;
            Win32.DrawTextW(dc, text, -1, &rc, fmt);
        }
        finally
        {
            Win32.SelectObject(dc, old);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.StateChanged -= OnEngineStateChanged;
        _stateStore.StateChanged -= OnStoreStateChanged;
        _startupService.StatusChanged -= OnStartupStatusChanged;
        _bridgeServer.SessionsChanged -= OnBridgeStatusChanged;
        _updateService.StateChanged -= OnUpdateStateChanged;
        _guideWindow?.Dispose();
        _guideWindow = null;

        if (_hwnd != 0)
        {
            StopUiAnimTimer();
            Win32.WTSUnRegisterSessionNotification(_hwnd);
            Win32.DestroyWindow(_hwnd);
            _hwnd = 0;
        }
        ClearFontCache();
        ReleaseBackBuffer();
        if (_windowBigIcon != 0)
        {
            Win32.DestroyIcon(_windowBigIcon);
            _windowBigIcon = 0;
        }
        if (_windowSmallIcon != 0)
        {
            Win32.DestroyIcon(_windowSmallIcon);
            _windowSmallIcon = 0;
        }
        if (ReferenceEquals(_instance, this))
            _instance = null;
    }
}
