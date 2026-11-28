using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cueway.Audio;
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
    public const uint WM_APP_ANIM_FRAME = Win32.WM_APP + 202;
    private const nuint TIMER_UI_ANIM = 8050;
    private const nuint TIMER_IDLE_RELEASE = 8051;
    private const uint IDLE_RELEASE_MS = 90_000;
    private const uint UI_ANIM_FALLBACK_MS = 8;
    private const int UI_HIGHLIGHT_DURATION_MS = 900;
    private const int UI_BRIDGE_PULSE_DURATION_MS = 1800;
    private const int UI_SLIDE_DURATION_MS = 240;
    private const int UI_POPOVER_DURATION_MS = 170;
    private const int UI_MODAL_DURATION_MS = 200;

    private enum TabKind
    {
        Control = 0,
        Apps = 1,
        AudioRules = 2,
        Hotkeys = 3,
        Settings = 4
    }

    private enum DropdownKind
    {
        None = 0,
        Language = 1,
        TaskInstall = 3,
        SessionOutputDevice = 4
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
    private readonly D2DRenderer _d2d = new();
    private ExtensionGuideWindow? _guideWindow;
    private ThemeEditorWindow? _themeEditorWindow;

    private readonly object _dispatchLock = new();
    private readonly Queue<Action> _uiQueue = new();
    private int _invokePosted;
    private int _wheelAccum;
    private bool _hoverIsHand;

    private nint _hwnd;
    private nint _cursorArrow;
    private nint _cursorHand;
    private bool _uiDataDirty;
    private nint _windowBigIcon;
    private nint _windowSmallIcon;
    private uint _lastDwmBgHeader = uint.MaxValue;
    private uint _lastDwmText = uint.MaxValue;
    private uint _lastDwmBorder = uint.MaxValue;
    private bool _lastDwmIsLight;
    private uint _dpi = 96;
    private int _clientW = 940;
    private int _clientH = 680;
    private int _scrollY;
    private float _scrollYFloat;
    private int _scrollTargetY;
    private int _contentTotalH;
    private int _bodyClipTop;
    private int _bodyClipBottom;
    private bool _pendingScrollToBrowserBridge;
    private int _settingsBridgeSectionOffset;
    private bool _trackingMouse;
    private string? _hoverId;
    private string? _pressedHitId;

    private TabKind _activeTab = TabKind.Control;
    private int _tabSlideDir;
    private double _tabAnimStartMs;
    private float _navFromX;
    private float _navFromW;
    private float _navCurX;
    private float _navCurW;
    private double _navAnimStartMs;
    private TabKind _navInitializedTab = (TabKind)(-1);

    private DropdownKind _openDropdown = DropdownKind.None;
    private double _dropdownOpenMs;
    private Win32.RECT _openDropdownAnchor;
    private string? _openDropdownSessionId;
    private string? _openDropdownSessionName;
    private string? _openDropdownBrowserName;
    private List<AudioOutputDevice> _openDropdownOutputDevices = [];
    private List<AudioOutputDevice> _outputDevices = [];
    private Dictionary<string, uint> _deviceColors = new(StringComparer.OrdinalIgnoreCase);
    private string? _defaultOutputDeviceId;
    private bool _deviceColorsForLight;
    private string? _statusBanner;
    private bool _statusBannerIsError;
    private long _statusBannerUntilTick;
    private bool _taskOpBusy;
    private KeyboardHookService? _keyboardHook;
    private string? _capturingTargetId;
    private string? _capturingHitId;
    private string? _capturingPreview;
    private bool _showWhatsNew;
    private double _whatsNewOpenMs;

    private readonly List<HitRegion> _hitRegions = [];
    private readonly List<SliderRegion> _sliderRegions = [];
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
    private double _highlightPrioStartMs;
    private string? _highlightTargetSessionId;
    private double _highlightTargetStartMs;
    private double _highlightBridgeStartMs;
    private bool _uiAnimTimerActive;
    private int _vsyncPending;
    private double _lastFrameMs;
    private static readonly WaitCallback VSyncWaitCallback = static state =>
    {
        var self = (MainWindow)state!;
        int hr = Win32.DwmFlush();
        nint targetHwnd = self._hwnd;
        if (self._disposed || targetHwnd == 0 || Win32.IsIconic(targetHwnd))
        {
            Interlocked.Exchange(ref self._vsyncPending, 0);
            return;
        }
        // wParam == 0 => exact VBlank repaint; wParam == 1 => DwmFlush unavailable, fall back to timer
        if (!Win32.PostMessageW(targetHwnd, WM_APP_ANIM_FRAME, hr == 0 ? (nuint)0 : (nuint)1, 0))
            Interlocked.Exchange(ref self._vsyncPending, 0);
    };
    private bool _anySpringAnimating;
    private float _lastFrameDt = 0.016f;
    private float _heroAuraR;
    private float _heroAuraG;
    private float _heroAuraB;
    private bool _heroAuraInit;

    private readonly Dictionary<string, float> _hoverAnim = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (float Value, bool Target)> _toggleAnim = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _volAnim = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _prioAnimY = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> _prioTargetY = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _animKeyScratch = new(32);
    private readonly List<string> _animPruneScratch = new(16);
    private static readonly uint[] WhatsNewCardAccents = [0x6366F1, 0x22C55E, 0x38BDF8, 0xF59E0B, 0xA855F7, 0xF43F5E];

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
        RefreshOutputDevicesCache();
        _instance = this;

        _wmTaskbarCreated = Win32.RegisterWindowMessageW("TaskbarCreated");
        CreateHostWindow();

        _engine.StateChanged += OnEngineStateChanged;
        _stateStore.StateChanged += OnStoreStateChanged;
        _startupService.StatusChanged += OnStartupStatusChanged;
        _bridgeServer.SessionsChanged += OnBridgeStatusChanged;
        _updateService.StateChanged += OnUpdateStateChanged;
        _iconRenderer.IconsUpdated += OnIconsUpdated;
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

    private void RefreshOutputDevicesCache(bool fresh = false)
    {
        _outputDevices = fresh ? _engine.GetOutputDevices() : _engine.GetOutputDevicesCached();
        _defaultOutputDeviceId = AudioDeviceColors.GetDefaultDeviceId(_outputDevices);
        _deviceColors = AudioDeviceColors.BuildDeviceColorMap(_outputDevices, Theme.IsLight);
        _deviceColorsForLight = Theme.IsLight;
    }

    private uint GetEffectiveDeviceColor(string? deviceId)
    {
        if (_deviceColorsForLight != Theme.IsLight)
        {
            _deviceColors = AudioDeviceColors.BuildDeviceColorMap(_outputDevices, Theme.IsLight);
            _deviceColorsForLight = Theme.IsLight;
        }
        string? effective = string.IsNullOrWhiteSpace(deviceId) ? _defaultOutputDeviceId : deviceId;
        return effective != null && _deviceColors.TryGetValue(effective, out uint color) ? color : Theme.Accent;
    }

    private string ResolveShortDeviceLabel(string? deviceId)
    {
        string? effective = string.IsNullOrWhiteSpace(deviceId) ? _defaultOutputDeviceId : deviceId;
        if (!string.IsNullOrWhiteSpace(effective))
        {
            foreach (var d in _outputDevices)
            {
                if (string.Equals(d.Id, effective, StringComparison.OrdinalIgnoreCase))
                    return ShortenDeviceName(d.Name);
            }
        }
        return Strings.DockDefaultOutput;
    }

    private static string ShortenDeviceName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Strings.DockDefaultOutput;
        string s = name.Trim();
        int parenIdx = s.IndexOf('(');
        if (parenIdx > 2 && parenIdx < 20)
        {
            int closeIdx = s.IndexOf(')', parenIdx);
            if (closeIdx > parenIdx + 1)
            {
                string prefix = s[..parenIdx].Trim();
                string inner = s[(parenIdx + 1)..closeIdx].Replace("(R)", "", StringComparison.OrdinalIgnoreCase).Trim();
                string fold = prefix.Replace('İ', 'i').Replace('I', 'i').ToLowerInvariant().Replace("\u0307", "");
                if (fold is "speakers" or "speaker" or "hoparlör" or "hoparlor" or "headphones" or "headphone" or "headset" or "kulaklık" or "kulaklik")
                {
                    s = string.IsNullOrEmpty(inner) ? prefix : $"{prefix} ({inner})";
                }
            }
        }
        return s.Length <= 19 ? s : $"{s[..17]}…";
    }

    public void RefreshOutputDevices()
    {
        RefreshOutputDevicesCache(fresh: true);
        if (_openDropdown == DropdownKind.SessionOutputDevice)
        {
            _openDropdownOutputDevices = _outputDevices;
        }
        RequestRepaint();
    }

    private void CloseOpenDropdown()
    {
        _openDropdown = DropdownKind.None;
        _openDropdownSessionId = null;
        _openDropdownSessionName = null;
        _openDropdownBrowserName = null;
        _openDropdownOutputDevices.Clear();
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

        CloseOpenDropdown();
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

        Win32.GetCursorPos(out var pt);
        nint hMon = Win32.MonitorFromPoint(pt, Win32.MONITOR_DEFAULTTONEAREST);
        var mi = new Win32.MONITORINFO { cbSize = (uint)sizeof(Win32.MONITORINFO) };
        Win32.GetMonitorInfoW(hMon, &mi);

        int initW = 940;
        int initH = 680;
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
            Win32.ChangeWindowMessageFilterEx(_hwnd, WM_APP_SHUTDOWN_INSTANCE, Win32.MSGFLT_ALLOW, 0);

            uint d = Win32.GetDpiForWindow(_hwnd);
            if (d >= 96) _dpi = d;

            int scaledW = Theme.Scale(940, _dpi);
            int scaledH = Theme.Scale(680, _dpi);
            int cx = mi.rcWork.Left + Math.Max(0, (mi.rcWork.Width - scaledW) / 2);
            int cy = mi.rcWork.Top + Math.Max(0, (mi.rcWork.Height - scaledH) / 2);
            Win32.SetWindowPos(_hwnd, 0, cx, cy, scaledW, scaledH, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);

            ApplyDwmTheme();
            ApplyAlwaysOnTop(_settings.AlwaysOnTop);
            Win32.WTSRegisterSessionNotification(_hwnd, Win32.NOTIFY_FOR_THIS_SESSION);
            ApplyBrandWindowIcon();
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

    private void SyncDwmThemeIfNeeded()
    {
        if (_hwnd == 0) return;
        if (Theme.BgHeader != _lastDwmBgHeader ||
            Theme.TextPrimary != _lastDwmText ||
            Theme.BorderSubtle != _lastDwmBorder ||
            Theme.IsLight != _lastDwmIsLight)
        {
            ApplyDwmTheme();
        }
    }

    private void ApplyDwmTheme()
    {
        if (_hwnd == 0) return;
        bool isLight = Theme.IsLight;
        uint bgHeader = Theme.BgHeader;
        uint textPrimary = Theme.TextPrimary;
        uint borderSubtle = Theme.BorderSubtle;

        int dark = isLight ? 0 : 1;
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(int));
        int corner = Win32.DWMWCP_ROUND;
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(int));
        uint captionColor = Theme.ToColorRef(bgHeader);
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_CAPTION_COLOR, &captionColor, sizeof(uint));
        uint textColor = Theme.ToColorRef(textPrimary);
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_TEXT_COLOR, &textColor, sizeof(uint));
        uint borderColor = Theme.ToColorRef(borderSubtle);
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_BORDER_COLOR, &borderColor, sizeof(uint));

        _lastDwmIsLight = isLight;
        _lastDwmBgHeader = bgHeader;
        _lastDwmText = textPrimary;
        _lastDwmBorder = borderSubtle;

        Win32.SetWindowPos(_hwnd, 0, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);
        Win32.RedrawWindow(_hwnd, null, 0, Win32.RDW_FRAME | Win32.RDW_INVALIDATE);
    }

    public void BeginInvoke(Action action)
    {
        if (_disposed || _hwnd == 0) return;
        lock (_dispatchLock)
            _uiQueue.Enqueue(action);
        if (Interlocked.Exchange(ref _invokePosted, 1) == 0)
            Win32.PostMessageW(_hwnd, WM_APP_DISPATCH, 0, 0);
    }

    private static double NowMs() => Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency);

    private void KickAnimations()
    {
        if (!_settings.ReducedAnimations && _hwnd != 0 && Win32.IsWindowVisible(_hwnd) && _lastFrameMs <= 0)
            _lastFrameMs = NowMs();
        RequestRepaint();
    }

    private void ToggleDropdown(DropdownKind kind)
    {
        if (_openDropdown == kind)
        {
            CloseOpenDropdown();
        }
        else
        {
            _openDropdown = kind;
            _dropdownOpenMs = _settings.ReducedAnimations ? 0 : -1.0;
            KickAnimations();
        }
    }

    private void OpenWhatsNew()
    {
        CloseOpenDropdown();
        _showWhatsNew = true;
        _whatsNewOpenMs = _settings.ReducedAnimations ? 0 : -1.0;
        KickAnimations();
    }

    private void SwitchToTab(TabKind nextTab)
    {
        CancelHotkeyCapture();
        CloseOpenDropdown();
        if (_activeTab != nextTab)
        {
            if (!_settings.ReducedAnimations)
            {
                _tabSlideDir = (int)nextTab > (int)_activeTab ? 1 : -1;
                _tabAnimStartMs = -1.0;
                _navFromX = _navCurX;
                _navFromW = _navCurW;
                _navAnimStartMs = -1.0;
            }
            else
            {
                _tabAnimStartMs = 0;
                _navAnimStartMs = 0;
            }
            _activeTab = nextTab;
        }
        _pendingScrollToBrowserBridge = false;
        _highlightBridgeStartMs = 0;
        _scrollY = 0;
        _scrollYFloat = 0f;
        _scrollTargetY = 0;
        _prioAnimY.Clear();
        _prioTargetY.Clear();
        KickAnimations();
    }

    public void ShowAndFocus(bool settingsTab = false)
    {
        if (_hwnd == 0) return;
        Win32.KillTimer(_hwnd, TIMER_IDLE_RELEASE);
        CloseOpenDropdown();
        RefreshOutputDevicesCache();
        if (settingsTab)
        {
            _activeTab = TabKind.Settings;
            _pendingScrollToBrowserBridge = false;
            _scrollY = 0;
            _scrollYFloat = 0f;
            _scrollTargetY = 0;
        }
        if (_uiDataDirty)
        {
            _priorityApps = _stateStore.GetPriorityAppsSnapshot();
            _uiDataDirty = false;
        }
        _settings = _stateStore.GetSettingsSnapshot();
        string curVer = !string.IsNullOrEmpty(_updateSnapshot.CurrentVersion)
            ? _updateSnapshot.CurrentVersion
            : AppUpdateService.GetCurrentAppVersion();
        if (!_showWhatsNew &&
            !string.IsNullOrEmpty(curVer) &&
            !string.Equals(_settings.LastSeenVersion, curVer, StringComparison.OrdinalIgnoreCase))
        {
            CancelHotkeyCapture();
            CloseOpenDropdown();
            _showWhatsNew = true;
            _whatsNewOpenMs = _settings.ReducedAnimations ? 0 : -1.0;
        }
        if (Win32.IsIconic(_hwnd))
            Win32.ShowWindow(_hwnd, Win32.SW_RESTORE);
        else
            Win32.ShowWindow(_hwnd, Win32.SW_SHOW);

        Win32.TryFocusWindow(_hwnd);
        VisibilityChanged?.Invoke(true);
        KickAnimations();

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
        _lastFrameMs = 0;
        _anySpringAnimating = false;
        _highlightPrioAppKey = null;
        _highlightPrioDir = 0;
        _highlightPrioStartMs = 0;
        _highlightTargetSessionId = null;
        _highlightTargetStartMs = 0;
        _highlightBridgeStartMs = 0;
        _tabAnimStartMs = 0;
        _navAnimStartMs = 0;
        _dropdownOpenMs = 0;
        _whatsNewOpenMs = 0;
        _heroAuraInit = false;
        _hoverAnim.Clear();
        _toggleAnim.Clear();
        _volAnim.Clear();
        _prioAnimY.Clear();
        _prioTargetY.Clear();
        CloseOpenDropdown();
        _themeEditorWindow?.CloseAndRevert();
        _guideWindow?.HideDialog();
        _hoverId = null;
        _pressedHitId = null;
        _hoverIsHand = false;
        Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
        _hitRegions.Clear();
        _hitRegions.TrimExcess();
        _sliderRegions.Clear();
        _sliderRegions.TrimExcess();
        VisibilityChanged?.Invoke(false);
        Win32.SetTimer(_hwnd, TIMER_IDLE_RELEASE, IDLE_RELEASE_MS, 0);
    }

    internal void TriggerPriorityMoveFeedback(string appKey, bool movedUp)
    {
        if (string.IsNullOrWhiteSpace(appKey) || _settings.ReducedAnimations)
            return;
        _highlightPrioAppKey = appKey;
        _highlightPrioDir = movedUp ? -1 : 1;
        _highlightPrioStartMs = NowMs();
        KickAnimations();
    }

    internal void TriggerTargetChangeFeedback(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || _settings.ReducedAnimations)
            return;
        _highlightTargetSessionId = sessionId;
        _highlightTargetStartMs = NowMs();
        KickAnimations();
    }

    private static float GetHighlightIntensity(double startMs, double nowMs)
    {
        if (startMs <= 0)
            return 0f;
        double elapsed = nowMs - startMs;
        if (elapsed < 0 || elapsed >= UI_HIGHLIGHT_DURATION_MS)
            return 0f;
        float t = (float)(elapsed / UI_HIGHLIGHT_DURATION_MS);
        float remain = 1f - t;
        return remain * remain;
    }

    private static bool IsBridgePulseActive(double startMs, double nowMs)
        => startMs > 0 && (nowMs - startMs) >= 0 && (nowMs - startMs) < UI_BRIDGE_PULSE_DURATION_MS;

    private static float GetBridgePulseIntensity(double startMs, double nowMs)
    {
        if (!IsBridgePulseActive(startMs, nowMs))
            return 0f;
        float phase = (float)((nowMs - startMs) / UI_BRIDGE_PULSE_DURATION_MS * 3.0);
        float wave = MathF.Sin(MathF.PI * (phase % 1f));
        return wave * wave;
    }

    private static float EaseOutCubic(double startMs, double nowMs, int durationMs)
    {
        if (startMs <= 0)
            return 1f;
        double elapsed = nowMs - startMs;
        if (elapsed <= 0)
            return 0f;
        if (elapsed >= durationMs)
            return 1f;
        float t = (float)(elapsed / durationMs);
        return 1f - MathF.Pow(1f - t, 3f);
    }

    private void StepAnimations(float dt, double nowMs)
    {
        _anySpringAnimating = false;
        _lastFrameDt = dt;

        if (_settings.ReducedAnimations)
        {
            _scrollYFloat = _scrollTargetY;
            _scrollY = _scrollTargetY;
            _hoverAnim.Clear();
            _prioAnimY.Clear();
            return;
        }

        // 1. Smooth scroll easing
        float scrollDiff = _scrollTargetY - _scrollYFloat;
        if (MathF.Abs(scrollDiff) > 0.35f)
        {
            _scrollYFloat += scrollDiff * (1f - MathF.Exp(-18f * dt));
            _anySpringAnimating = true;
        }
        else
        {
            _scrollYFloat = _scrollTargetY;
        }
        _scrollY = (int)MathF.Round(_scrollYFloat);

        // 2. Smooth hover decay/ramp
        if (_hoverId != null && !_hoverAnim.ContainsKey(_hoverId))
            _hoverAnim[_hoverId] = 0f;

        if (_hoverAnim.Count > 0)
        {
            _animKeyScratch.Clear();
            foreach (string k in _hoverAnim.Keys)
                _animKeyScratch.Add(k);
            _animPruneScratch.Clear();

            for (int i = 0; i < _animKeyScratch.Count; i++)
            {
                string key = _animKeyScratch[i];
                float prev = _hoverAnim[key];
                bool isHov = string.Equals(_hoverId, key, StringComparison.Ordinal);
                float target = isHov ? 1f : 0f;
                float speed = isHov ? 24f : 14f;
                float next = prev + (target - prev) * (1f - MathF.Exp(-speed * dt));
                if (MathF.Abs(target - next) <= 0.004f)
                {
                    if (!isHov)
                        _animPruneScratch.Add(key);
                    else
                        _hoverAnim[key] = 1f;
                }
                else
                {
                    _hoverAnim[key] = next;
                    _anySpringAnimating = true;
                }
            }

            for (int i = 0; i < _animPruneScratch.Count; i++)
                _hoverAnim.Remove(_animPruneScratch[i]);
        }

        // 3. Smooth toggle knob switches
        if (_toggleAnim.Count > 0)
        {
            _animKeyScratch.Clear();
            foreach (string k in _toggleAnim.Keys)
                _animKeyScratch.Add(k);

            for (int i = 0; i < _animKeyScratch.Count; i++)
            {
                string key = _animKeyScratch[i];
                var entry = _toggleAnim[key];
                float target = entry.Target ? 1f : 0f;
                float diff = target - entry.Value;
                if (MathF.Abs(diff) > 0.004f)
                {
                    float next = entry.Value + diff * (1f - MathF.Exp(-20f * dt));
                    _toggleAnim[key] = (next, entry.Target);
                    _anySpringAnimating = true;
                }
                else if (entry.Value != target)
                {
                    _toggleAnim[key] = (target, entry.Target);
                }
            }
        }

        // 4. Smooth FLIP priority row Y positions
        if (_prioTargetY.Count > 0)
        {
            foreach (var kv in _prioTargetY)
            {
                if (_prioAnimY.TryGetValue(kv.Key, out float curY))
                {
                    float diff = kv.Value - curY;
                    if (MathF.Abs(diff) > 0.35f)
                    {
                        _prioAnimY[kv.Key] = curY + diff * (1f - MathF.Exp(-16f * dt));
                        _anySpringAnimating = true;
                    }
                    else
                    {
                        _prioAnimY[kv.Key] = kv.Value;
                    }
                }
                else
                {
                    _prioAnimY[kv.Key] = kv.Value;
                }
            }
        }

        // 5. Clear completed timed animations cleanly
        if (_highlightPrioStartMs > 0 && GetHighlightIntensity(_highlightPrioStartMs, nowMs) <= 0.004f)
        {
            _highlightPrioAppKey = null;
            _highlightPrioDir = 0;
            _highlightPrioStartMs = 0;
        }
        if (_highlightTargetStartMs > 0 && GetHighlightIntensity(_highlightTargetStartMs, nowMs) <= 0.004f)
        {
            _highlightTargetSessionId = null;
            _highlightTargetStartMs = 0;
        }
        if (_highlightBridgeStartMs > 0 && !IsBridgePulseActive(_highlightBridgeStartMs, nowMs))
            _highlightBridgeStartMs = 0;
        if (_tabAnimStartMs > 0 && (nowMs - _tabAnimStartMs) >= UI_SLIDE_DURATION_MS)
            _tabAnimStartMs = 0;
        if (_navAnimStartMs > 0 && (nowMs - _navAnimStartMs) >= UI_SLIDE_DURATION_MS)
            _navAnimStartMs = 0;
        if (_dropdownOpenMs > 0 && (nowMs - _dropdownOpenMs) >= UI_POPOVER_DURATION_MS)
            _dropdownOpenMs = 0;
        if (_whatsNewOpenMs > 0 && (nowMs - _whatsNewOpenMs) >= UI_MODAL_DURATION_MS)
            _whatsNewOpenMs = 0;
    }

    private float GetHoverT(string id)
    {
        if (_settings.ReducedAnimations)
            return string.Equals(_hoverId, id, StringComparison.Ordinal) ? 1f : 0f;

        if (string.Equals(_hoverId, id, StringComparison.Ordinal))
        {
            if (!_hoverAnim.TryGetValue(id, out float v))
            {
                _hoverAnim[id] = 0f;
                _anySpringAnimating = true;
                return 0f;
            }
            return v;
        }
        return _hoverAnim.TryGetValue(id, out float decay) ? decay : 0f;
    }

    private float GetToggleAnim(string id, bool targetState)
    {
        if (_settings.ReducedAnimations)
            return targetState ? 1f : 0f;

        if (_toggleAnim.TryGetValue(id, out var entry))
        {
            if (entry.Target != targetState)
            {
                _toggleAnim[id] = (entry.Value, targetState);
                _anySpringAnimating = true;
            }
            return entry.Value;
        }

        float init = targetState ? 1f : 0f;
        _toggleAnim[id] = (init, targetState);
        return init;
    }

    private float GetAnimatedVolume(string sessionId, float targetVol, bool isDraggingThis, float dragVol)
    {
        if (isDraggingThis)
        {
            _volAnim[sessionId] = dragVol;
            return dragVol;
        }
        if (_settings.ReducedAnimations)
        {
            _volAnim[sessionId] = targetVol;
            return targetVol;
        }
        if (!_volAnim.TryGetValue(sessionId, out float cur))
        {
            _volAnim[sessionId] = targetVol;
            return targetVol;
        }
        float diff = targetVol - cur;
        if (MathF.Abs(diff) <= 0.003f)
        {
            _volAnim[sessionId] = targetVol;
            return targetVol;
        }
        float next = cur + diff * (1f - MathF.Exp(-18f * _lastFrameDt));
        _volAnim[sessionId] = next;
        _anySpringAnimating = true;
        return next;
    }

    private uint GetAnimatedHeroAura(uint targetRgb)
    {
        float tr = (targetRgb >> 16) & 0xFF;
        float tg = (targetRgb >> 8) & 0xFF;
        float tb = targetRgb & 0xFF;
        if (!_heroAuraInit || _settings.ReducedAnimations)
        {
            _heroAuraR = tr;
            _heroAuraG = tg;
            _heroAuraB = tb;
            _heroAuraInit = true;
            return targetRgb;
        }
        float dr = tr - _heroAuraR;
        float dg = tg - _heroAuraG;
        float db = tb - _heroAuraB;
        if (MathF.Abs(dr) + MathF.Abs(dg) + MathF.Abs(db) <= 1.5f)
        {
            _heroAuraR = tr;
            _heroAuraG = tg;
            _heroAuraB = tb;
            return targetRgb;
        }
        float k = 1f - MathF.Exp(-8f * _lastFrameDt);
        _heroAuraR += dr * k;
        _heroAuraG += dg * k;
        _heroAuraB += db * k;
        _anySpringAnimating = true;
        uint r = (uint)Math.Clamp((int)MathF.Round(_heroAuraR), 0, 255);
        uint g = (uint)Math.Clamp((int)MathF.Round(_heroAuraG), 0, 255);
        uint b = (uint)Math.Clamp((int)MathF.Round(_heroAuraB), 0, 255);
        return (r << 16) | (g << 8) | b;
    }

    private bool HasActiveUiAnimation(double nowMs)
        => !_settings.ReducedAnimations &&
           (_anySpringAnimating ||
            GetHighlightIntensity(_highlightPrioStartMs, nowMs) > 0.004f ||
            GetHighlightIntensity(_highlightTargetStartMs, nowMs) > 0.004f ||
            IsBridgePulseActive(_highlightBridgeStartMs, nowMs) ||
            _tabAnimStartMs < 0 || (_tabAnimStartMs > 0 && (nowMs - _tabAnimStartMs) < UI_SLIDE_DURATION_MS) ||
            _navAnimStartMs < 0 || (_navAnimStartMs > 0 && (nowMs - _navAnimStartMs) < UI_SLIDE_DURATION_MS) ||
            _dropdownOpenMs < 0 || (_dropdownOpenMs > 0 && (nowMs - _dropdownOpenMs) < UI_POPOVER_DURATION_MS) ||
            _whatsNewOpenMs < 0 || (_whatsNewOpenMs > 0 && (nowMs - _whatsNewOpenMs) < UI_MODAL_DURATION_MS));

    private void EnsureUiAnimTimerState()
    {
        if (_hwnd != 0 && Win32.IsWindowVisible(_hwnd) && HasActiveUiAnimation(NowMs()))
            KickAnimations();
        else
            StopUiAnimTimer();
    }

    private void StartUiAnimTimer()
    {
        if (_hwnd != 0 && Win32.IsWindowVisible(_hwnd) && !_uiAnimTimerActive)
        {
            Win32.SetTimer(_hwnd, TIMER_UI_ANIM, UI_ANIM_FALLBACK_MS, 0);
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

    private void ScheduleNextVSyncFrame(nint hwnd)
    {
        if (_disposed || hwnd == 0)
            return;
        if (Interlocked.CompareExchange(ref _vsyncPending, 1, 0) != 0)
            return;
        ThreadPool.UnsafeQueueUserWorkItem(VSyncWaitCallback, this);
    }

    private void HandleUiAnimTimer()
    {
        if (!Win32.IsWindowVisible(_hwnd) || !HasActiveUiAnimation(NowMs()))
        {
            StopUiAnimTimer();
            return;
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
            KickAnimations();
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
                NowMs() - _highlightPrioStartMs > 120.0)
            {
                TriggerPriorityMoveFeedback(_priorityApps[0].AppKey, movedUp: true);
            }
            if (topChanged)
                ApplyAlwaysOnTop(_settings.AlwaysOnTop);
            SyncDwmThemeIfNeeded();
            if (themeChanged)
            {
                _deviceColors = AudioDeviceColors.BuildDeviceColorMap(_outputDevices, Theme.IsLight);
                _deviceColorsForLight = Theme.IsLight;
                _iconRenderer.InvalidateThemeDependentIcons();
                _d2d.ClearIconCache();
                _iconRenderer.FlushRetiredIcons();
                _guideWindow?.RefreshLocalizationAndTheme();
                _themeEditorWindow?.RefreshLocalizationAndTheme();
            }
            if (langChanged && _hwnd != 0)
            {
                _d2d.ClearTextFormatCache();
                Win32.SetWindowTextW(_hwnd, Strings.WindowTitle);
                _guideWindow?.RefreshLocalizationAndTheme();
                _themeEditorWindow?.RefreshLocalizationAndTheme();
                _engine.Reevaluate();
            }
            KickAnimations();
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

    private void ApplyBrandWindowIcon()
    {
        if (_hwnd == 0) return;

        nint big = _iconRenderer.CreateBrandIcon(48);
        nint small = _iconRenderer.CreateBrandIcon(32);

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
    }

    private void DrawAppIconOrMonogram(
        string? appKey,
        string? exePath,
        string? displayName,
        float x,
        float y,
        float size,
        float radius)
    {
        nint hIcon = _iconRenderer.GetIconForAppNonBlocking(appKey, exePath);
        if (hIcon != 0 && _d2d.DrawIcon(hIcon, x, y, size))
            return;

        _d2d.FillAndStrokeRoundedRect(
            x, y, size, size, radius,
            Theme.AccentSoft, 1f,
            Theme.Accent, 0.37f, 1f);

        string glyph = "♪";
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            foreach (char ch in displayName.Trim())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    glyph = char.ToUpperInvariant(ch).ToString();
                    break;
                }
            }
        }

        int ptSize = Math.Max(9, (int)MathF.Round(size / (_dpi / 96f) * 0.44f));
        _d2d.DrawText(glyph, x, y, x + size, y + size, ptSize, Win32.FW_BOLD, Theme.Accent, _dpi, centered: true);
    }

    public void InvalidateIconCache() => _d2d.ClearIconCache();

    private void OnIconsUpdated() => BeginInvoke(() =>
    {
        _d2d.ClearIconCache();
        _iconRenderer.FlushRetiredIcons();
        RequestRepaint();
    });

    public void RefreshVisuals(bool clearIcons = true) => BeginInvoke(() =>
    {
        if (clearIcons)
        {
            _d2d.ClearIconCache();
            _iconRenderer.FlushRetiredIcons();
        }
        RequestRepaint();
    });

    private void RequestRepaint()
    {
        if (_hwnd != 0 && Win32.IsWindowVisible(_hwnd))
            Win32.InvalidateRect(_hwnd, null, false);
    }

    private void DrainDispatchQueue()
    {
        Interlocked.Exchange(ref _invokePosted, 0);
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

        try
        {
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

                case WM_APP_ANIM_FRAME:
                    Interlocked.Exchange(ref inst._vsyncPending, 0);
                    if (wParam != 0)
                    {
                        inst.StartUiAnimTimer();
                    }
                    else if (Win32.IsWindowVisible(hwnd) && !Win32.IsIconic(hwnd) && inst.HasActiveUiAnimation(NowMs()))
                    {
                        Win32.InvalidateRect(hwnd, null, false);
                    }
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
                    if (wParam == TIMER_IDLE_RELEASE)
                    {
                        Win32.KillTimer(hwnd, TIMER_IDLE_RELEASE);
                        if (!Win32.IsWindowVisible(hwnd))
                            inst._d2d.ReleaseDeviceResources();
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
                    if ((int)wParam == Win32.SIZE_MINIMIZED)
                    {
                        inst.StopUiAnimTimer();
                        return 0;
                    }
                    inst._clientW = Math.Max(1, (int)(lParam & 0xFFFF));
                    inst._clientH = Math.Max(1, (int)((lParam >> 16) & 0xFFFF));
                    inst._d2d.Resize(inst._clientW, inst._clientH);
                    inst.ClampScroll();
                    Win32.InvalidateRect(hwnd, null, false);
                    return 0;

                case Win32.WM_DPICHANGED:
                    inst._dpi = (uint)(wParam & 0xFFFF);
                    inst._d2d.ClearTextFormatCache();
                    inst._d2d.ClearIconCache();
                    var suggested = *(Win32.RECT*)lParam;
                    Win32.SetWindowPos(hwnd, 0, suggested.Left, suggested.Top, suggested.Width, suggested.Height,
                        Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
                    Win32.InvalidateRect(hwnd, null, false);
                    return 0;

                case Win32.WM_GETMINMAXINFO:
                    var mmi = (Win32.MINMAXINFO*)lParam;
                    mmi->ptMinTrackSize.X = Theme.Scale(760, inst._dpi);
                    mmi->ptMinTrackSize.Y = Theme.Scale(540, inst._dpi);
                    return 0;

                case Win32.WM_SETCURSOR:
                    if ((lParam & 0xFFFF) == Win32.HTCLIENT)
                    {
                        Win32.SetCursor(inst._hoverIsHand && inst._cursorHand != 0 ? inst._cursorHand : inst._cursorArrow);
                        return 1;
                    }
                    break;

                case Win32.WM_ERASEBKGND:
                    return 1;

                case Win32.WM_PAINT:
                    inst.HandlePaint(hwnd);
                    return 0;

                case Win32.WM_MOUSEMOVE:
                    inst.HandleMouseMove(lParam);
                    return 0;

                case Win32.WM_MOUSELEAVE:
                    inst._trackingMouse = false;
                    inst._hoverIsHand = false;
                    if (inst._hoverId != null && inst._draggingSliderId == null)
                    {
                        inst._hoverId = null;
                        inst.KickAnimations();
                    }
                    return 0;

                case Win32.WM_LBUTTONDOWN:
                    if (inst.HandleLeftButtonDown(lParam))
                        return 0;
                    if (inst._pressedHitId != null)
                        inst.RequestRepaint();
                    break;

                case Win32.WM_CAPTURECHANGED:
                    inst._pressedHitId = null;
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
                    {
                        inst._pressedHitId = null;
                        inst.CancelHotkeyCapture();
                    }
                    break;

                case Win32.WM_KILLFOCUS:
                    inst._pressedHitId = null;
                    inst.CancelHotkeyCapture();
                    break;

                case 0x0100: // WM_KEYDOWN
                    if (wParam == 0x1B && inst._showWhatsNew)
                    {
                        inst.DismissWhatsNew();
                        return 0;
                    }
                    if (wParam == 0x1B && inst._openDropdown != DropdownKind.None)
                    {
                        inst.CloseOpenDropdown();
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
                        inst.CloseOpenDropdown();
                        inst.ExitRequested?.Invoke();
                    }
                    return 0;

                case Win32.WM_DESTROY:
                    Win32.PostQuitMessage(0);
                    return 0;
            }

            return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
        catch
        {
            return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
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
            _hoverIsHand = true;
            Win32.SetCursor(_cursorHand != 0 ? _cursorHand : _cursorArrow);

            long nowTick = Environment.TickCount64;
            if (nowTick - _lastSliderDispatchTick >= 35)
            {
                _lastSliderDispatchTick = nowTick;
                _ = _engine.SetSessionVolumeAsync(_draggingSessionId, vol, commitMemory: false);
            }
            RequestRepaint();
            return;
        }

        // Check HitRegion before SliderRegion only when clicking top overlays or mute buttons inside bars
        var topHit = FindHit(x, y);
        if (topHit != null && topHit.Id.EndsWith("_mute", StringComparison.Ordinal))
        {
            _hoverIsHand = true;
            Win32.SetCursor(_cursorHand != 0 ? _cursorHand : _cursorArrow);
            if (!string.Equals(_hoverId, topHit.Id, StringComparison.Ordinal))
            {
                _hoverId = topHit.Id;
                KickAnimations();
            }
            return;
        }

        var slider = !_showWhatsNew && _openDropdown == DropdownKind.None ? FindSliderHit(x, y) : null;
        if (slider != null && (topHit == null || !topHit.IsButton))
        {
            _hoverIsHand = true;
            Win32.SetCursor(_cursorHand != 0 ? _cursorHand : _cursorArrow);
            if (!string.Equals(_hoverId, slider.Id, StringComparison.Ordinal))
            {
                _hoverId = slider.Id;
                KickAnimations();
            }
            return;
        }

        string? nextHover = topHit?.Id;
        _hoverIsHand = topHit != null && topHit.IsButton;
        Win32.SetCursor(_hoverIsHand && _cursorHand != 0 ? _cursorHand : _cursorArrow);

        if (!string.Equals(_hoverId, nextHover, StringComparison.Ordinal))
        {
            _hoverId = nextHover;
            KickAnimations();
        }
    }

    private bool HandleLeftButtonDown(nint lParam)
    {
        int x = unchecked((short)(lParam & 0xFFFF));
        int y = unchecked((short)((lParam >> 16) & 0xFFFF));

        var topHit = FindHit(x, y);
        if (topHit != null && topHit.Id.EndsWith("_mute", StringComparison.Ordinal))
        {
            _pressedHitId = topHit.Id;
            return false;
        }

        var slider = !_showWhatsNew && _openDropdown == DropdownKind.None ? FindSliderHit(x, y) : null;
        if (slider == null || (topHit != null && topHit.IsButton && !topHit.Id.Contains("_focus", StringComparison.Ordinal)))
        {
            _pressedHitId = topHit?.Id;
            return false;
        }

        _pressedHitId = null;
        float vol = Math.Clamp((float)(x - slider.TrackLeft) / Math.Max(1, slider.TrackWidth), 0f, 1f);
        _draggingSliderId = slider.Id;
        _draggingSessionId = slider.SessionId;
        _dragTrackLeft = slider.TrackLeft;
        _dragTrackWidth = slider.TrackWidth;
        _dragPreviewVolume = vol;
        _lastSliderDispatchTick = Environment.TickCount64;

        Win32.SetCapture(_hwnd);
        _ = _engine.SetSessionVolumeAsync(slider.SessionId, vol, commitMemory: false);
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
            _pressedHitId = null;
            Win32.ReleaseCapture();
            _ = _engine.SetSessionVolumeAsync(targetSession, finalVol, commitMemory: true);
            RequestRepaint();
            return;
        }

        string? pressedId = _pressedHitId;
        _pressedHitId = null;

        var hit = FindHit(x, y);
        if (_capturingTargetId != null)
        {
            if (hit == null || !string.Equals(hit.Id, _capturingHitId, StringComparison.Ordinal))
            {
                CancelHotkeyCapture();
                RequestRepaint();
                return;
            }
        }
        if (hit != null && pressedId != null && string.Equals(pressedId, hit.Id, StringComparison.Ordinal))
        {
            try { hit.OnClick(); } catch { }
            RequestRepaint();
        }
    }

    private void HandleMouseWheel(short delta)
    {
        if (_showWhatsNew)
            return;
        if (_openDropdown != DropdownKind.None)
        {
            _wheelAccum = 0;
            _pressedHitId = null;
            CloseOpenDropdown();
            KickAnimations();
            return;
        }
        _wheelAccum += delta;
        int step = S(48);
        int steps = _wheelAccum / 120;
        if (steps != 0)
        {
            _wheelAccum -= steps * 120;
            _scrollTargetY -= steps * step;
        }
        else if (Math.Abs(_wheelAccum) >= 24)
        {
            float frac = _wheelAccum / 120f;
            _wheelAccum = 0;
            _scrollTargetY -= (int)MathF.Round(frac * step);
        }
        ClampScroll();
        if (_settings.ReducedAnimations)
        {
            _scrollYFloat = _scrollTargetY;
            _scrollY = _scrollTargetY;
        }
        KickAnimations();
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
        => S(62) + (ShouldShowUpdateBar() ? S(44) : 0);

    private void ClampScroll()
    {
        int topH = GetTopChromeHeight();
        int viewH = Math.Max(1, _clientH - topH);
        int maxScroll = Math.Max(0, _contentTotalH - viewH);
        _scrollTargetY = Math.Clamp(_scrollTargetY, 0, maxScroll);
        _scrollYFloat = Math.Clamp(_scrollYFloat, 0f, maxScroll);
        _scrollY = Math.Clamp(_scrollY, 0, maxScroll);
    }

    private HitRegion? FindHit(int x, int y)
    {
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

    private void HandlePaint(nint hwnd)
    {
        SyncDwmThemeIfNeeded();
        var ps = new Win32.PAINTSTRUCT();
        nint hdc = Win32.BeginPaint(hwnd, &ps);
        if (hdc == 0) return;

        int w = Math.Max(1, _clientW);
        int h = Math.Max(1, _clientH);

        double nowMs = NowMs();
        if (_tabAnimStartMs < 0) _tabAnimStartMs = nowMs;
        if (_navAnimStartMs < 0) _navAnimStartMs = nowMs;
        if (_dropdownOpenMs < 0) _dropdownOpenMs = nowMs;
        if (_whatsNewOpenMs < 0) _whatsNewOpenMs = nowMs;
        float dt = _lastFrameMs > 0 ? Math.Clamp((float)((nowMs - _lastFrameMs) / 1000.0), 0.001f, 0.05f) : 0.016f;
        _lastFrameMs = nowMs;
        StepAnimations(dt, nowMs);

        try
        {
            if (!_d2d.BeginFrame(hwnd, w, h))
                return;

            _hitRegions.Clear();
            _sliderRegions.Clear();

            _d2d.Clear(Theme.BgWindow);

            int headerH = S(62);
            int barH = ShouldShowUpdateBar() ? S(44) : 0;
            int topChromeH = headerH + barH;
            int dockH = S(32);
            int dockY = h - S(14) - dockH;
            _bodyClipTop = topChromeH;
            _bodyClipBottom = Math.Max(topChromeH, dockY - S(4));

            DrawScrollableBody(w, h, topChromeH, nowMs);

            if (_activeTab == TabKind.Settings && _pendingScrollToBrowserBridge)
            {
                _pendingScrollToBrowserBridge = false;
                int viewH = Math.Max(1, h - topChromeH);
                int maxScroll = Math.Max(0, _contentTotalH - viewH);
                int centeredScroll = _settingsBridgeSectionOffset + S(82) - viewH / 2;
                int clamped = Math.Clamp(centeredScroll, 0, maxScroll);
                if (_scrollY != clamped)
                {
                    _scrollY = clamped;
                    _scrollTargetY = clamped;
                    _scrollYFloat = clamped;
                    _hitRegions.Clear();
                    _sliderRegions.Clear();
                    _d2d.Clear(Theme.BgWindow);
                    DrawScrollableBody(w, h, topChromeH, nowMs);
                }
            }

            _bodyClipTop = 0;
            _bodyClipBottom = h;

            DrawHeader(w, headerH, nowMs);
            if (barH > 0)
                DrawUpdateBannerBar(w, headerH, barH);

            DrawFloatingBottomDock(w, dockY, dockH);

            bool showDropdown = _openDropdown != DropdownKind.None &&
                ((_activeTab == TabKind.Settings && _openDropdown != DropdownKind.SessionOutputDevice) ||
                 (_activeTab == TabKind.Control && _openDropdown == DropdownKind.SessionOutputDevice));
            if (showDropdown)
                DrawOpenDropdownOverlay(w, h, topChromeH, nowMs);

            if (_showWhatsNew)
                DrawWhatsNewOverlay(w, h, nowMs);

            _d2d.EndFrame(out bool deviceLost);

            if (deviceLost)
            {
                RequestRepaint();
                return;
            }

            double afterMs = NowMs();
            if (Win32.IsWindowVisible(hwnd) && HasActiveUiAnimation(afterMs))
            {
                StopUiAnimTimer();
                ScheduleNextVSyncFrame(hwnd);
            }
            else
            {
                StopUiAnimTimer();
                _lastFrameMs = 0;
            }
        }
        finally
        {
            Win32.EndPaint(hwnd, &ps);
        }
    }

    private void DrawHeader(int w, int headerH, double nowMs)
    {
        _d2d.FillRect(0, 0, w, headerH, Theme.BgHeader);
        _d2d.FillSpecularRim(0, 0, w, MathF.Max(1.5f, SF(1.8f)),
            Theme.Accent, Theme.AccentHover, Theme.IsLight ? 0.50f : 0.78f);
        _d2d.FillRect(0, headerH - 1, w, 1, Theme.BorderSubtle);

        int padX = S(20);
        int iconSz = S(24);
        int iconY = (headerH - iconSz) / 2;
        nint titleIcon = _iconRenderer.GetBrandIcon(iconSz);
        if (titleIcon == 0 || !_d2d.DrawIcon(titleIcon, padX, iconY, iconSz))
            _d2d.FillRoundedRect(padX, iconY, iconSz, iconSz, SF(6), Theme.Accent);

        int titleX = padX + iconSz + S(10);
        _d2d.DrawText("Cueway", titleX, 0, titleX + S(90), headerH, 16, Win32.FW_BOLD, Theme.TextPrimary, _dpi);

        // Right cluster: optional Hook Off / Game Guard badge + Exit button
        int exitW = S(72);
        int exitH = S(32);
        int exitY = (headerH - exitH) / 2;
        int exitX = w - padX - exitW;
        int rightEdge = exitX - S(8);

        if (!_settings.MediaHookEnabled)
        {
            string hookOffText = Strings.BadgeMediaHookOff;
            int hookPillW = Math.Max(S(64), _d2d.MeasureTextWidth(hookOffText, 10, Win32.FW_SEMIBOLD, _dpi) + S(20));
            int hookX = rightEdge - hookPillW;
            int pillY = (headerH - S(24)) / 2;
            int pillH = S(24);
            bool hookHovered = string.Equals(_hoverId, "hdr_hook_off", StringComparison.Ordinal);
            _d2d.FillAndStrokeRoundedRect(hookX, pillY, hookPillW, pillH, pillH * 0.5f,
                Theme.RedSoft, 1f, Theme.Red, hookHovered ? 1f : 0.42f, 1.1f);
            _d2d.DrawText(hookOffText, hookX, pillY, hookX + hookPillW, pillY + pillH,
                10, Win32.FW_SEMIBOLD, Theme.Red, _dpi, centered: true);
            _hitRegions.Add(new HitRegion(
                new Win32.RECT { Left = hookX, Top = pillY, Right = hookX + hookPillW, Bottom = pillY + pillH },
                "hdr_hook_off",
                () => _stateStore.UpdateSettings(s => s.MediaHookEnabled = true)));
            rightEdge = hookX - S(8);
        }
        else if (_snapshot.GameGuardActive)
        {
            int ggW = Math.Max(S(64), _d2d.MeasureTextWidth(Strings.BadgeGameGuard, 10, Win32.FW_SEMIBOLD, _dpi) + S(18));
            int ggX = rightEdge - ggW;
            DrawPillBadge(ggX, (headerH - S(22)) / 2, Strings.BadgeGameGuard, Theme.Green, Theme.GreenSoft);
            rightEdge = ggX - S(8);
        }

        // Center 5-Tab Segmented Navigation Pill Bar
        ReadOnlySpan<(TabKind Kind, string Id, string Label)> tabs =
        [
            (TabKind.Control, "tab_ctrl", Strings.TabControl),
            (TabKind.Apps, "tab_apps", Strings.TabApps),
            (TabKind.AudioRules, "tab_rules", Strings.TabAudioRules),
            (TabKind.Hotkeys, "tab_keys", Strings.TabHotkeys),
            (TabKind.Settings, "tab_set", Strings.TabSettings)
        ];

        Span<int> tabWidths = stackalloc int[5];
        int totalTabsInnerW = 0;
        for (int i = 0; i < 5; i++)
        {
            tabWidths[i] = Math.Clamp(_d2d.MeasureTextWidth(tabs[i].Label, 11, Win32.FW_SEMIBOLD, _dpi) + S(22), S(62), S(142));
            totalTabsInnerW += tabWidths[i];
        }

        int pillPad = S(3);
        int tabGap = S(2);
        int navW = totalTabsInnerW + tabGap * 4 + pillPad * 2;
        int minNavX = titleX + S(82);
        int maxNavRight = rightEdge - S(4);
        if (minNavX + navW > maxNavRight && maxNavRight > minNavX + S(220))
        {
            float scale = (float)(maxNavRight - minNavX - pillPad * 2 - tabGap * 4) / Math.Max(1, totalTabsInnerW);
            totalTabsInnerW = 0;
            for (int i = 0; i < 5; i++)
            {
                tabWidths[i] = Math.Max(S(46), (int)MathF.Floor(tabWidths[i] * scale));
                totalTabsInnerW += tabWidths[i];
            }
            navW = totalTabsInnerW + tabGap * 4 + pillPad * 2;
        }

        int navX = Math.Clamp((w - navW) / 2, minNavX, Math.Max(minNavX, maxNavRight - navW));
        int navH = S(34);
        int navY = (headerH - navH) / 2;

        _d2d.FillAndStrokeRoundedRect(navX, navY, navW, navH, SF(11),
            Theme.BgCard, 1f, Theme.BorderSubtle, 1f);

        int btnY = navY + pillPad;
        int btnH = navH - pillPad * 2;
        int curX = navX + pillPad;
        int activeIdx = (int)_activeTab;
        float targetPillX = curX;
        float targetPillW = tabWidths[0];

        Span<int> tabXs = stackalloc int[5];
        for (int i = 0; i < 5; i++)
        {
            tabXs[i] = curX;
            if (i == activeIdx)
            {
                targetPillX = curX;
                targetPillW = tabWidths[i];
            }
            curX += tabWidths[i] + tabGap;
        }

        // Animated gliding pill behind the active tab
        if (_navInitializedTab != _activeTab && _navAnimStartMs <= 0)
        {
            _navCurX = targetPillX;
            _navCurW = targetPillW;
            _navInitializedTab = _activeTab;
        }
        else if (_navAnimStartMs > 0 && !_settings.ReducedAnimations)
        {
            float ease = EaseOutCubic(_navAnimStartMs, nowMs, UI_SLIDE_DURATION_MS);
            _navCurX = _navFromX + (targetPillX - _navFromX) * ease;
            _navCurW = _navFromW + (targetPillW - _navFromW) * ease;
        }
        else
        {
            _navCurX = targetPillX;
            _navCurW = targetPillW;
        }

        _d2d.FillLinearGradientRoundedRect(_navCurX, btnY, _navCurW, btnH, SF(8),
            Theme.Accent, 1f, Theme.AccentHover, 1f);

        for (int i = 0; i < 5; i++)
        {
            var t = tabs[i];
            int bx = tabXs[i];
            int bw = tabWidths[i];
            bool active = i == activeIdx;
            float hovT = active ? 0f : GetHoverT(t.Id);
            if (hovT > 0.01f)
                _d2d.FillRoundedRect(bx, btnY, bw, btnH, SF(8), Theme.BgControl, 0.72f * hovT);

            uint fg = active
                ? Theme.OnAccent
                : Theme.BlendRgb(Theme.TextSecondary, Theme.TextPrimary, hovT);
            _d2d.DrawText(t.Label, bx, btnY, bx + bw, btnY + btnH, 11,
                active ? Win32.FW_SEMIBOLD : Win32.FW_MEDIUM, fg, _dpi, centered: true);

            var capturedKind = t.Kind;
            _hitRegions.Add(new HitRegion(
                new Win32.RECT { Left = bx, Top = btnY, Right = bx + bw, Bottom = btnY + btnH },
                t.Id,
                () => SwitchToTab(capturedKind)));
        }

        DrawActionButton("btn_exit", Strings.BtnExit, exitX, exitY, exitW, exitH,
            Theme.BgCard, Theme.BgControlHover, Theme.TextSecondary, Theme.BorderSubtle,
            () => ExitRequested?.Invoke(), scrollable: false);
    }

    private void DrawFloatingBottomDock(int w, int dockY, int dockH)
    {
        var (browsers, tabs) = _bridgeServer.GetStatusSnapshot();
        string bridgeText = browsers > 0
            ? Strings.BadgeBridgeConnected(browsers, tabs)
            : Strings.BadgeBridgeDisconnected;
        uint bridgeCol = browsers > 0 ? Theme.Green : Theme.Amber;

        var activeTarget = _snapshot.ActiveTarget ?? _snapshot.NowPlayingTarget;
        string? assignedDevId = activeTarget != null ? _engine.GetSessionOutputDeviceId(activeTarget.SessionId) : null;
        string devShort = ResolveShortDeviceLabel(assignedDevId);
        uint devCol = GetEffectiveDeviceColor(assignedDevId);

        var sleepRem = _engine.GetSleepTimerRemaining();
        bool hasSleep = sleepRem.HasValue && sleepRem.Value > TimeSpan.Zero;
        string rightText = hasSleep
            ? $"{(int)sleepRem!.Value.TotalMinutes:D2}:{sleepRem.Value.Seconds:D2}"
            : (_settings.MediaHookEnabled ? Strings.DockMediaKeysOn : Strings.BadgeMediaHookOff);
        uint rightCol = hasSleep ? Theme.Amber : (_settings.MediaHookEnabled ? Theme.TextSecondary : Theme.Red);

        int seg1W = Math.Max(S(96), _d2d.MeasureTextWidth(bridgeText, 10, Win32.FW_SEMIBOLD, _dpi) + S(26));
        int seg2W = Math.Clamp(_d2d.MeasureTextWidth(devShort, 10, Win32.FW_SEMIBOLD, _dpi) + S(34), S(96), S(205));
        int seg3W = Math.Max(S(84), _d2d.MeasureTextWidth(rightText, 10, Win32.FW_SEMIBOLD, _dpi) + (hasSleep ? S(30) : S(20)));

        int padH = S(10);
        int divSpace = S(12);
        int maxInnerW = Math.Max(S(240), w - S(32) - padH * 2 - divSpace * 2);
        int sumSegW = seg1W + seg2W + seg3W;
        if (sumSegW > maxInnerW)
        {
            float scale = (float)maxInnerW / sumSegW;
            seg1W = Math.Max(S(64), (int)MathF.Floor(seg1W * scale));
            seg2W = Math.Max(S(64), (int)MathF.Floor(seg2W * scale));
            seg3W = Math.Max(S(56), maxInnerW - seg1W - seg2W);
        }
        int dockW = padH * 2 + seg1W + seg2W + seg3W + divSpace * 2;
        int dockX = (w - dockW) / 2;
        float radius = dockH * 0.5f;

        // Subtle drop shadow + glass capsule
        _d2d.FillRoundedRect(dockX - S(2), dockY + S(3), dockW + S(4), dockH + S(4), radius + S(2),
            0x000000, Theme.IsLight ? 0.14f : 0.42f);
        _d2d.FillAndStrokeRoundedRect(dockX, dockY, dockW, dockH, radius,
            Theme.BgCardElevated, 0.96f, Theme.BorderStrong, 0.9f, 1.1f);

        // Non-interactive capsule shield so underlying scroll items never leak clicks through gaps
        _hitRegions.Add(new HitRegion(
            new Win32.RECT { Left = dockX, Top = dockY, Right = dockX + dockW, Bottom = dockY + dockH },
            "dock_capsule",
            () => { },
            IsButton: false));

        int x1 = dockX + padH;
        int x2 = x1 + seg1W + divSpace;
        int x3 = x2 + seg2W + divSpace;
        int innerY = dockY + S(4);
        int innerH = dockH - S(8);

        // Segment 1: Browser Bridge (jump to Settings -> Browser Bridge card)
        float hov1 = GetHoverT("dock_bridge");
        if (hov1 > 0.01f)
            _d2d.FillRoundedRect(x1, innerY, seg1W, innerH, innerH * 0.5f, bridgeCol, 0.16f * hov1);

        int dotSz = S(6);
        _d2d.FillCircle(x1 + S(6), dockY + (dockH - dotSz) * 0.5f, dotSz, bridgeCol);
        _d2d.DrawText(bridgeText, x1 + S(16), dockY, x1 + seg1W - S(4), dockY + dockH,
            10, Win32.FW_SEMIBOLD, bridgeCol, _dpi);
        _hitRegions.Add(new HitRegion(
            new Win32.RECT { Left = x1, Top = dockY, Right = x1 + seg1W, Bottom = dockY + dockH },
            "dock_bridge",
            () =>
            {
                SwitchToTab(TabKind.Settings);
                _pendingScrollToBrowserBridge = true;
                int viewH = Math.Max(1, _clientH - GetTopChromeHeight());
                int destScroll = _settingsBridgeSectionOffset > 0
                    ? Math.Max(0, _settingsBridgeSectionOffset + S(82) - viewH / 2)
                    : 0;
                _scrollTargetY = destScroll;
                _scrollYFloat = destScroll;
                _scrollY = destScroll;
                if (!_settings.ReducedAnimations)
                {
                    _highlightBridgeStartMs = NowMs();
                    KickAnimations();
                }
            }));

        // Divider 1
        _d2d.FillRect(x1 + seg1W + divSpace / 2, dockY + S(8), 1, dockH - S(16), Theme.BorderSubtle);

        // Segment 2: Active Audio Device
        bool canPickDevice = activeTarget != null &&
                             !_snapshot.IsNowPlayingClosed &&
                             _engine.IsPerAppAudioRoutingSupported &&
                             activeTarget.ProcessIds.Length > 0;
        float hov2 = canPickDevice ? GetHoverT("dock_dev") : 0f;
        if (hov2 > 0.01f)
            _d2d.FillRoundedRect(x2, innerY, seg2W, innerH, innerH * 0.5f, devCol, 0.18f * hov2);

        _d2d.DrawAudioSwitchGlyph(x2 + S(13), dockY + dockH * 0.5f, S(14), devCol, _dpi);
        _d2d.DrawText(devShort, x2 + S(22), dockY, x2 + seg2W - S(6), dockY + dockH,
            10, Win32.FW_SEMIBOLD, devCol, _dpi);

        if (canPickDevice && activeTarget != null)
        {
            var captured = activeTarget;
            _hitRegions.Add(new HitRegion(
                new Win32.RECT { Left = x2, Top = dockY, Right = x2 + seg2W, Bottom = dockY + dockH },
                "dock_dev",
                () => OpenSessionOutputDeviceMenu(captured, new Win32.RECT { Left = x2, Top = dockY, Right = x2 + seg2W, Bottom = dockY + dockH })));
        }

        // Divider 2
        _d2d.FillRect(x2 + seg2W + divSpace / 2, dockY + S(8), 1, dockH - S(16), Theme.BorderSubtle);

        // Segment 3: Sleep Timer or Hook Status
        int seg3MaxRight = Math.Min(x3 + seg3W, dockX + dockW - padH);
        if (hasSleep)
        {
            float hov3 = GetHoverT("dock_sleep");
            if (hov3 > 0.01f)
                _d2d.FillRoundedRect(x3, innerY, seg3MaxRight - x3, innerH, innerH * 0.5f, Theme.Amber, 0.17f * hov3);
            _hitRegions.Add(new HitRegion(
                new Win32.RECT { Left = x3, Top = dockY, Right = seg3MaxRight, Bottom = dockY + dockH },
                "dock_sleep",
                () => _engine.CancelSleepTimer()));
        }
        else if (!_settings.MediaHookEnabled)
        {
            _hitRegions.Add(new HitRegion(
                new Win32.RECT { Left = x3, Top = dockY, Right = seg3MaxRight, Bottom = dockY + dockH },
                "dock_hook",
                () => _stateStore.UpdateSettings(s => s.MediaHookEnabled = true)));
        }

        if (hasSleep)
        {
            _d2d.DrawClockGlyph(x3 + S(8), dockY + (dockH - S(11)) * 0.5f, S(11), rightCol, _dpi);
            _d2d.DrawText(rightText, x3 + S(22), dockY, seg3MaxRight - S(6), dockY + dockH,
                10, Win32.FW_SEMIBOLD, rightCol, _dpi);
        }
        else
        {
            _d2d.DrawText(rightText, x3 + S(4), dockY, seg3MaxRight - S(4), dockY + dockH,
                10, Win32.FW_SEMIBOLD, rightCol, _dpi, centered: true);
        }
    }

    private void OpenSessionOutputDeviceMenu(MediaSessionInfo session, Win32.RECT anchor)
    {
        if (_openDropdown == DropdownKind.SessionOutputDevice &&
            string.Equals(_openDropdownSessionId, session.SessionId, StringComparison.Ordinal))
        {
            CloseOpenDropdown();
            return;
        }

        RefreshOutputDevicesCache(fresh: true);
        if (_outputDevices.Count == 0)
            return;

        if (_activeTab != TabKind.Control)
            SwitchToTab(TabKind.Control);

        _openDropdownOutputDevices = [.. _outputDevices];
        _openDropdownSessionId = session.SessionId;
        _openDropdownSessionName = session.DisplayName;
        _openDropdownBrowserName = ProcessResolver.ResolveBrowserDisplayName(session);
        _openDropdownAnchor = anchor;
        _openDropdown = DropdownKind.SessionOutputDevice;
        _dropdownOpenMs = NowMs();
        KickAnimations();
    }

    private void DrawUpdateBannerBar(int w, int y, int barH)
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

        _d2d.FillRect(0, y, w, barH, bgColor);
        if (snap.Phase == UpdatePhase.Available && string.Equals(_hoverId, "upd_bar_click", StringComparison.Ordinal))
            _d2d.FillRect(0, y, w, barH, Theme.Accent, 0.09f);
        _d2d.FillRect(0, y + barH - 1, w, 1, lineColor, 0.55f);

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
            int dlW = Math.Clamp(_d2d.MeasureTextWidth(Strings.UpdateBtnDownload, 12, Win32.FW_SEMIBOLD, _dpi) + S(26), S(96), S(148));
            int dlX = disX - S(8) - dlW;

            int badgeW = Math.Max(S(46), _d2d.MeasureTextWidth(Strings.UpdateBadgeNew, 10, Win32.FW_BOLD, _dpi) + S(16));
            int badgeH = S(20);
            int badgeY = y + (barH - badgeH) / 2;
            _d2d.FillRoundedRect(padX, badgeY, badgeW, badgeH, badgeH * 0.5f, Theme.Accent);
            _d2d.DrawText(Strings.UpdateBadgeNew, padX, badgeY, padX + badgeW, badgeY + badgeH, 10, Win32.FW_BOLD, Theme.OnAccent, _dpi, centered: true);

            string msg = Strings.UpdateAvailableBanner(snap.LatestVersion);
            _d2d.DrawText(msg, padX + badgeW + S(10), y, dlX - S(12), y + barH, 12, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi);

            bool dlHovered = string.Equals(_hoverId, "upd_btn_dl", StringComparison.Ordinal);
            _d2d.FillAndStrokeRoundedRect(dlX, btnY, dlW, btnH, SF(7),
                dlHovered ? Theme.AccentHover : Theme.Accent, 1f, Theme.Accent, 1f);
            _d2d.DrawText(Strings.UpdateBtnDownload, dlX, btnY, dlX + dlW, btnY + btnH, 12, Win32.FW_SEMIBOLD, Theme.OnAccent, _dpi, centered: true);
            _hitRegions.Add(new HitRegion(
                new Win32.RECT { Left = dlX, Top = btnY, Right = dlX + dlW, Bottom = btnY + btnH },
                "upd_btn_dl",
                () => _updateService.StartDownload()));

            DrawIconButton("upd_btn_dismiss", SmallButtonGlyph.Close, disX, y + (barH - disSz) / 2, disSz, true,
                () => _updateService.DismissBanner(), scrollable: false);
        }
        else if (snap.Phase == UpdatePhase.Downloading)
        {
            int cancelW = Math.Clamp(_d2d.MeasureTextWidth(Strings.UpdateBtnCancel, 12, Win32.FW_SEMIBOLD, _dpi) + S(22), S(82), S(120));
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

            _d2d.DrawText(text, padX, y, progX - S(14), y + barH, 12, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi);
            _d2d.FillRoundedRect(progX, progY, progW, progH, progH * 0.5f, Theme.BgControl);
            if (ratio > 0.005f)
            {
                float fillW = Math.Max(progH, progW * ratio);
                _d2d.FillRoundedRect(progX, progY, fillW, progH, progH * 0.5f, Theme.Accent);
            }
            else
            {
                float segW = progW * 0.28f;
                float phase = (float)((Environment.TickCount64 % 1400) / 1400.0);
                float segX = progX + (progW - segW) * phase;
                _d2d.FillRoundedRect(segX, progY, segW, progH, progH * 0.5f, Theme.Accent);
            }

            DrawActionButton("upd_btn_cancel", Strings.UpdateBtnCancel, cancelX, btnY, cancelW, btnH,
                Theme.BgControl, Theme.BgControlHover, Theme.TextPrimary, Theme.BorderStrong,
                () => _updateService.CancelDownload(), scrollable: false);
        }
        else if (snap.Phase == UpdatePhase.ReadyToInstall)
        {
            int disSz = S(24);
            int disX = w - padX - disSz;
            int instW = Math.Clamp(_d2d.MeasureTextWidth(Strings.UpdateBtnInstall, 12, Win32.FW_SEMIBOLD, _dpi) + S(28), S(162), S(230));
            int instX = disX - S(8) - instW;

            int dotSz = S(8);
            _d2d.FillCircle(padX, y + (barH - dotSz) * 0.5f, dotSz, Theme.Green);
            _d2d.DrawText(Strings.UpdateReadyBanner(snap.LatestVersion), padX + dotSz + S(10), y, instX - S(12), y + barH,
                12, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi);

            bool instHover = string.Equals(_hoverId, "upd_btn_install", StringComparison.Ordinal);
            _d2d.FillAndStrokeRoundedRect(instX, btnY, instW, btnH, SF(7),
                Theme.Green, instHover ? 1f : 0.90f, Theme.Green, 1f);
            _d2d.DrawText(Strings.UpdateBtnInstall, instX, btnY, instX + instW, btnY + btnH, 12, Win32.FW_SEMIBOLD, Theme.OnAccent, _dpi, centered: true);
            _hitRegions.Add(new HitRegion(
                new Win32.RECT { Left = instX, Top = btnY, Right = instX + instW, Bottom = btnY + btnH },
                "upd_btn_install",
                LaunchDownloadedUpdate));

            DrawIconButton("upd_btn_dismiss", SmallButtonGlyph.Close, disX, y + (barH - disSz) / 2, disSz, true,
                () => _updateService.DismissBanner(), scrollable: false);
        }
        else
        {
            int disSz = S(24);
            int disX = w - padX - disSz;
            int retryW = Math.Clamp(_d2d.MeasureTextWidth(Strings.UpdateBtnRetry, 12, Win32.FW_SEMIBOLD, _dpi) + S(26), S(106), S(156));
            int retryX = disX - S(8) - retryW;

            int dotSz = S(8);
            _d2d.FillCircle(padX, y + (barH - dotSz) * 0.5f, dotSz, Theme.Red);
            _d2d.DrawText(snap.ResolveErrorMessage() ?? Strings.UpdateFailedBanner, padX + dotSz + S(10), y, retryX - S(12), y + barH,
                12, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi);

            DrawActionButton("upd_btn_retry", Strings.UpdateBtnRetry, retryX, btnY, retryW, btnH,
                Theme.BgCard, Theme.BgControlHover, Theme.Red, Theme.Red,
                () => _updateService.StartDownload(), scrollable: false);

            DrawIconButton("upd_btn_dismiss", SmallButtonGlyph.Close, disX, y + (barH - disSz) / 2, disSz, true,
                () => _updateService.DismissBanner(), scrollable: false);
        }
    }

    private void LaunchDownloadedUpdate()
    {
        if (_updateService.TryLaunchInstaller(out string? err))
            ExitRequested?.Invoke();
        else if (!string.IsNullOrEmpty(err))
            ShowStatusBanner(err, isError: true);
    }

    private void DrawScrollableBody(int w, int h, int headerH, double nowMs)
    {
        int padX = S(22);
        int contentW = w - padX * 2;
        int y = headerH + S(16) - _scrollY;
        int startY = y;

        float slideOffsetX = 0f;
        if (_tabAnimStartMs > 0 && !_settings.ReducedAnimations)
        {
            float ease = EaseOutCubic(_tabAnimStartMs, nowMs, UI_SLIDE_DURATION_MS);
            slideOffsetX = _tabSlideDir * S(26) * (1f - ease);
        }

        _d2d.PushClip(0, headerH, w, h - headerH);
        bool didTabSlide = MathF.Abs(slideOffsetX) > 0.25f;
        if (didTabSlide)
            _d2d.PushTransform(Direct2D.D2D1_MATRIX_3X2_F.Translation(slideOffsetX, 0f));

        y = _activeTab switch
        {
            TabKind.Control => DrawControlTab(padX, y, contentW, headerH, nowMs),
            TabKind.Apps => DrawAppsTab(padX, y, contentW, headerH, nowMs),
            TabKind.AudioRules => DrawAudioRulesTab(padX, y, contentW, headerH),
            TabKind.Hotkeys => DrawHotkeysTab(padX, y, contentW, headerH),
            _ => DrawSettingsTab(padX, y, contentW, headerH)
        };

        if (didTabSlide)
            _d2d.PopTransform();
        _d2d.PopClip();

        // Bottom clearance keeps content readable above floating dock pill
        _contentTotalH = (y - startY) + S(74);
        ClampScroll();

        int viewH = h - headerH;
        if (_contentTotalH > viewH && viewH > 20)
        {
            float ratio = (float)viewH / _contentTotalH;
            int thumbH = Math.Max(S(32), (int)(viewH * ratio));
            int maxScroll = Math.Max(1, _contentTotalH - viewH);
            int thumbY = headerH + (int)((viewH - thumbH) * ((float)_scrollY / maxScroll));
            _d2d.FillRoundedRect(w - S(8), thumbY, S(4), thumbH, SF(2), Theme.BorderStrong, 0.70f);
        }
    }

    // =========================================================================
    // TAB 1: CONTROL (C1 Aura Deck)
    // =========================================================================
    private int DrawControlTab(int x, int y, int w, int headerH, double nowMs)
    {
        y = DrawStatusBannerIfActive(x, y, w);

        // 1. HERO CARD(S)
        int playingCount = _snapshot.NowPlayingSessions.Count;
        if (playingCount >= 2)
        {
            int shownCount = Math.Min(playingCount, 3);
            _d2d.DrawText(Strings.SecLivePlayersCount(shownCount), x, y, x + w, y + S(18), 11, Win32.FW_BOLD, Theme.TextMuted, _dpi);
            y += S(22);
            y = DrawMultiLivePlayerCards(x, y, w, headerH, _snapshot.NowPlayingSessions, nowMs);
        }
        else
        {
            bool isLivePlaying = _snapshot.NowPlayingTarget != null &&
                                 !_snapshot.IsNowPlayingClosed &&
                                 _snapshot.NowPlayingTarget.IsPlaying;
            _d2d.DrawText(isLivePlaying ? Strings.SecLivePlayer : Strings.SecNowPlaying, x, y, x + w, y + S(18), 11, Win32.FW_BOLD, Theme.TextMuted, _dpi);
            y += S(22);
            y = DrawNowPlayingHeroCard(x, y, w, headerH, nowMs);
        }
        y += S(18);

        // 2. LIVE MEDIA SESSIONS (2-Col C1 Mixer Deck)
        int liveHeaderY = y;
        int refW = Math.Clamp(_d2d.MeasureTextWidth(Strings.BtnRefresh, 11, Win32.FW_SEMIBOLD, _dpi) + S(38), S(92), S(160));
        int refH = S(26);
        int refX = x + w - refW;

        _d2d.DrawText($"{Strings.SecLiveSessions} ({_snapshot.Sessions.Count})", x, y, refX - S(10), y + S(24),
            11, Win32.FW_BOLD, Theme.TextMuted, _dpi);

        if (y + refH > headerH)
        {
            float refHovT = GetHoverT("btn_refresh");
            int refBtnY = liveHeaderY - S(2);
            _d2d.FillAndStrokeRoundedRect(refX, refBtnY, refW, refH, SF(8),
                Theme.BlendRgb(Theme.BgCard, Theme.BgControlHover, refHovT), 1f, Theme.BorderSubtle, 1f);
            _d2d.DrawRefreshGlyph(refX + S(14), refBtnY + refH * 0.5f, S(13), Theme.TextSecondary, _dpi);
            _d2d.DrawText(Strings.BtnRefresh, refX + S(24), refBtnY, refX + refW - S(6), refBtnY + refH,
                11, Win32.FW_SEMIBOLD, Theme.TextSecondary, _dpi, centered: true);
            RegisterScrollableHit(
                new Win32.RECT { Left = refX, Top = refBtnY, Right = refX + refW, Bottom = refBtnY + refH },
                "btn_refresh",
                () => _ = _sessionManager.RefreshAsync());
        }
        y += S(28);

        if (_snapshot.Sessions.Count == 0)
        {
            y = DrawEmptyCard(x, y, w, Strings.EmptySessionsTitle, Strings.EmptySessionsSub);
        }
        else
        {
            bool twoCol = w >= S(620);
            int colGap = S(10);
            int colW = twoCol ? (w - colGap) / 2 : w;
            int cardH = S(112);

            for (int i = 0; i < _snapshot.Sessions.Count; i++)
            {
                int col = twoCol ? (i % 2) : 0;
                int row = twoCol ? (i / 2) : i;
                int cx = x + col * (colW + colGap);
                int cw = twoCol && col == 1 ? (x + w - cx) : colW;
                int cy = y + row * (cardH + S(10));
                if (cy + cardH < _bodyClipTop || cy > _bodyClipBottom)
                    continue;
                DrawSessionMixerCard(cx, cy, cw, cardH, _snapshot.Sessions[i], headerH, nowMs);
            }

            int totalRows = twoCol ? (_snapshot.Sessions.Count + 1) / 2 : _snapshot.Sessions.Count;
            y += totalRows * cardH + Math.Max(0, totalRows - 1) * S(10);
        }

        // 3. INLINE QUICK PRIORITY STRIP (immediately accessible without tabbing away)
        if (_priorityApps.Count > 0)
        {
            y += S(14);
            y = DrawQuickPriorityStrip(x, y, w, headerH);
        }

        return y;
    }

    private int DrawMultiLivePlayerCards(int x, int y, int w, int headerH, IReadOnlyList<MediaSessionInfo> players, double nowMs)
    {
        int count = Math.Min(players.Count, 3);
        int gap = S(10);
        int cardH = S(108);
        int baseColW = (w - gap * (count - 1)) / count;

        for (int i = 0; i < count; i++)
        {
            int cx = x + i * (baseColW + gap);
            int cw = (i == count - 1) ? (x + w - cx) : baseColW;
            var s = players[i];

            bool isPriority = _snapshot.ActiveTarget != null &&
                              string.Equals(_snapshot.ActiveTarget.SessionId, s.SessionId, StringComparison.Ordinal);
            bool isLocked = _snapshot.IsManualLock && isPriority;
            string focusId = $"multi_focus_{s.SessionId}";
            bool focusHovered = string.Equals(_hoverId, focusId, StringComparison.Ordinal);

            uint borderRgb = isLocked ? Theme.Amber : (isPriority ? Theme.Accent : Theme.Green);
            uint cardBg = isPriority
                ? Theme.BlendRgb(Theme.BgCardElevated, borderRgb, Theme.IsLight ? 0.06f : 0.11f)
                : Theme.BgCardElevated;

            _d2d.FillAndStrokeRoundedRect(cx, y, cw, cardH, SF(12),
                cardBg, 1f, borderRgb, isPriority ? 0.88f : 0.68f, isPriority ? 1.5f : 1.15f);

            // Soft clipped aura highlight
            _d2d.PushClip(cx, y, cw, cardH);
            _d2d.FillRadialAura(cx + S(32), y + S(30), S(95), S(65), borderRgb, Theme.IsLight ? 0.08f : 0.14f);
            _d2d.PopClip();

            float cardGlow = isPriority && string.Equals(_highlightTargetSessionId, s.SessionId, StringComparison.Ordinal)
                ? GetHighlightIntensity(_highlightTargetStartMs, nowMs)
                : 0f;
            if (cardGlow > 0.01f)
                _d2d.DrawCardGlow(cx, y, cw, cardH, SF(12), borderRgb, cardGlow, Theme.IsLight, _dpi);

            int pad = S(12);
            int icoBox = S(36);
            int icoY = y + S(12);
            _d2d.FillAndStrokeRoundedRect(cx + pad, icoY, icoBox, icoBox, SF(9),
                Theme.BgCard, 1f, focusHovered ? Theme.Accent : Theme.BorderSubtle, 1f);

            int icoSz = S(22);
            DrawAppIconOrMonogram(s.AppKey, s.ExePath, s.DisplayName,
                cx + pad + (icoBox - icoSz) * 0.5f, icoY + (icoBox - icoSz) * 0.5f, icoSz, SF(6));

            bool showFullTransport = count == 2 && cw >= S(330);
            int ctrlLeft;
            if (showFullTransport)
            {
                int btnSz = S(28);
                int btnY = y + S(14);
                int nextX = cx + cw - pad - btnSz;
                int playX = nextX - S(4) - btnSz;
                int prevX = playX - S(4) - btnSz;
                ctrlLeft = prevX;

                if (y + cardH > headerH)
                {
                    var captured = s;
                    DrawTransportButton($"multi_prev_{s.SessionId}", TransportGlyph.Previous, prevX, btnY, btnSz,
                        () => _ = _engine.DispatchMediaActionToSessionAsync(captured.SessionId, MediaActionKind.Previous), circular: true);
                    DrawTransportButton($"multi_play_{s.SessionId}", TransportGlyph.Pause, playX, btnY, btnSz,
                        () => _ = _engine.DispatchMediaActionToSessionAsync(captured.SessionId, MediaActionKind.PlayPause), accent: true, circular: true);
                    DrawTransportButton($"multi_next_{s.SessionId}", TransportGlyph.Next, nextX, btnY, btnSz,
                        () => _ = _engine.DispatchMediaActionToSessionAsync(captured.SessionId, MediaActionKind.Next), circular: true);
                }
            }
            else
            {
                int btnSz = S(30);
                int btnX = cx + cw - pad - btnSz;
                int btnY = y + S(13);
                ctrlLeft = btnX;

                if (y + cardH > headerH)
                {
                    var captured = s;
                    DrawTransportButton($"multi_play_{s.SessionId}", TransportGlyph.Pause, btnX, btnY, btnSz,
                        () => _ = _engine.DispatchMediaActionToSessionAsync(captured.SessionId, MediaActionKind.PlayPause), accent: true, circular: true);
                }
            }

            int textX = cx + pad + icoBox + S(10);
            int textRight = ctrlLeft - S(8);
            bool isWeb = IsBrowserOrigin(s.AppKey, s.IsBrowser);
            int globeSz = isWeb ? S(11) : 0;
            int globeGap = isWeb ? S(4) : 0;
            string badgeText = isLocked ? Strings.BtnLocked : "#1";
            int badgePillW = isPriority
                ? Math.Max(S(26), _d2d.MeasureTextWidth(badgeText, 9, Win32.FW_BOLD, _dpi) + S(12))
                : 0;
            int arrowSz = S(10);
            int reserveRight = globeSz + globeGap + arrowSz + S(4) + (isPriority ? badgePillW + S(6) : 0);
            int titleW = _d2d.MeasureTextWidth(s.DisplayName, 13, Win32.FW_BOLD, _dpi) + S(4);
            int nameMaxRight = Math.Clamp(textX + titleW, textX, Math.Max(textX, textRight - reserveRight));

            _d2d.DrawText(s.DisplayName, textX, y + S(11), textRight - reserveRight, y + S(30), 13, Win32.FW_BOLD,
                focusHovered ? Theme.Accent : Theme.TextPrimary, _dpi);

            int curX = nameMaxRight + S(2);
            if (isWeb && curX + globeSz <= textRight)
            {
                _d2d.DrawGlobeGlyph(curX, y + S(15), globeSz, Theme.TextMuted, _dpi);
                curX += globeSz + globeGap;
            }
            if (curX + arrowSz <= textRight)
                _d2d.DrawFocusArrowGlyph(curX, y + S(16), arrowSz, focusHovered ? Theme.Accent : Theme.TextMuted, _dpi);

            if (isPriority)
            {
                int badgeX = curX + arrowSz + S(4);
                if (badgeX + badgePillW <= textRight)
                {
                    int badgeH = S(18);
                    int badgeY = y + S(12);
                    _d2d.FillRoundedRect(badgeX, badgeY, badgePillW, badgeH, badgeH * 0.5f,
                        isLocked ? Theme.AmberSoft : Theme.AccentSoft);
                    _d2d.DrawText(badgeText, badgeX, badgeY, badgeX + badgePillW, badgeY + badgeH,
                        9, Win32.FW_BOLD, isLocked ? Theme.Amber : Theme.Accent, _dpi, centered: true);
                }
            }

            _d2d.DrawText(s.FormattedMediaTitle, textX, y + S(31), textRight, y + S(49), 11, Win32.FW_NORMAL, Theme.TextSecondary, _dpi);

            if (y + cardH > headerH)
            {
                var captured = s;
                RegisterScrollableHit(
                    new Win32.RECT { Left = cx, Top = y, Right = ctrlLeft - S(4), Bottom = y + S(56) },
                    focusId,
                    () => _ = _engine.FocusSessionAsync(captured.SessionId));

                string? devId = _engine.GetSessionOutputDeviceId(s.SessionId);
                uint devCol = GetEffectiveDeviceColor(devId);
                DrawControlCenterVolumeBar($"multi_vol_{s.SessionId}", s.SessionId, s.Volume, s.IsMuted,
                    cx + pad, y + S(66), cw - pad * 2, S(28), devCol, headerH);
            }
        }

        return y + cardH;
    }

    private int DrawNowPlayingHeroCard(int x, int y, int w, int headerH, double nowMs)
    {
        int cardH = S(116);
        var playing = _snapshot.NowPlayingTarget;
        bool isClosed = playing != null && _snapshot.IsNowPlayingClosed;
        bool isLocked = !isClosed &&
                        playing != null &&
                        _snapshot.IsManualLock &&
                        _snapshot.ActiveTarget != null &&
                        string.Equals(_snapshot.ActiveTarget.SessionId, playing.SessionId, StringComparison.Ordinal);
        bool isPriorityTarget = !isClosed &&
                                playing != null &&
                                _snapshot.ActiveTarget != null &&
                                string.Equals(_snapshot.ActiveTarget.SessionId, playing.SessionId, StringComparison.Ordinal);
        float focusHov = (!isClosed && playing != null) ? GetHoverT("act_focus") : 0f;

        uint borderColor = playing == null || isClosed
            ? Theme.BorderSubtle
            : (isLocked
                ? Theme.Amber
                : (playing.IsEffectivelyPlaying ? Theme.Green : Theme.BorderStrong));

        string? devId = (!isClosed && playing != null) ? _engine.GetSessionOutputDeviceId(playing.SessionId) : null;
        uint devColor = GetEffectiveDeviceColor(devId);

        _d2d.FillAndStrokeRoundedRect(x, y, w, cardH, SF(14),
            Theme.BgCardElevated, 1f,
            borderColor, playing == null || isClosed ? 1f : 0.78f, 1.35f);

        // Soft clipped ambient Aura Deck radial glows (smoothly crossfading colors)
        _d2d.PushClip(x, y, w, cardH);
        uint targetAura = (playing != null && !isClosed && playing.IsEffectivelyPlaying) ? Theme.Green : Theme.Accent;
        uint leftAura = GetAnimatedHeroAura(targetAura);
        _d2d.FillRadialAura(x + S(52), y + cardH * 0.4f, S(180), S(120), leftAura, Theme.IsLight ? 0.10f : 0.16f);
        if (playing != null && !isClosed)
            _d2d.FillRadialAura(x + w - S(90), y + cardH * 0.65f, S(160), S(110), devColor, Theme.IsLight ? 0.08f : 0.13f);
        _d2d.PopClip();

        float targetGlow = !isClosed && playing != null && string.Equals(_highlightTargetSessionId, playing.SessionId, StringComparison.Ordinal)
            ? GetHighlightIntensity(_highlightTargetStartMs, nowMs)
            : 0f;
        if (targetGlow > 0.01f)
        {
            uint glowColor = isLocked ? Theme.Amber : (playing!.IsEffectivelyPlaying ? Theme.Green : Theme.Accent);
            _d2d.DrawCardGlow(x, y, w, cardH, SF(14), glowColor, targetGlow, Theme.IsLight, _dpi);
        }

        int pad = S(18);
        int iconBox = S(58);
        int iconBoxY = y + (cardH - iconBox) / 2;
        _d2d.FillAndStrokeRoundedRect(x + pad, iconBoxY, iconBox, iconBox, SF(14),
            Theme.BgCard, 1f, Theme.BlendRgb(Theme.BorderSubtle, Theme.Accent, focusHov), 1f);

        if (playing == null)
        {
            _d2d.DrawText(Strings.NoNowPlayingTitle, x + pad + iconBox + S(16), y + S(26), x + w - pad, y + S(52),
                15, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi);
            _d2d.DrawWrappedText(Strings.NoNowPlayingSub,
                x + pad + iconBox + S(16), y + S(54), x + w - pad, y + S(96),
                12, Win32.FW_NORMAL, Theme.TextSecondary, _dpi);
            return y + cardH;
        }

        int icoSz = S(36);
        DrawAppIconOrMonogram(playing.AppKey, playing.ExePath, playing.DisplayName,
            x + pad + (iconBox - icoSz) * 0.5f, iconBoxY + (iconBox - icoSz) * 0.5f, icoSz, SF(8));

        int ctrlRight = x + w - pad;
        int textRight;

        if (!isClosed)
        {
            // Right transport row: [⇄ Device Pill] [⏮] [(▶/⏸)] [⏭] [Kilitle]
            int playSz = S(40);
            int smallBtn = S(32);
            int lockW = Math.Clamp(_d2d.MeasureTextWidth(isLocked ? Strings.BtnUnlock : Strings.BtnLock, 11, Win32.FW_SEMIBOLD, _dpi) + S(24), S(82), S(116));
            int lockX = ctrlRight - lockW;
            int nextX = lockX - S(10) - smallBtn;
            int playX = nextX - S(6) - playSz;
            int prevX = playX - S(6) - smallBtn;

            int rowCenterY = y + cardH / 2;
            int playY = rowCenterY - playSz / 2;
            int smallY = rowCenterY - smallBtn / 2;

            bool showDevPill = _engine.IsPerAppAudioRoutingSupported && playing.ProcessIds.Length > 0;
            int devPillX = prevX;
            if (showDevPill)
            {
                string devLabel = ResolveShortDeviceLabel(devId);
                int devPillW = Math.Clamp(_d2d.MeasureTextWidth(devLabel, 10, Win32.FW_SEMIBOLD, _dpi) + S(34), S(96), S(162));
                devPillX = prevX - S(10) - devPillW;
                int devPillH = S(30);
                int devPillY = rowCenterY - devPillH / 2;
                bool devOpen = _openDropdown == DropdownKind.SessionOutputDevice &&
                               string.Equals(_openDropdownSessionId, playing.SessionId, StringComparison.Ordinal);
                float devHovT = devOpen ? 1f : GetHoverT("act_dev_pill");
                var devAnchor = new Win32.RECT { Left = devPillX, Top = devPillY, Right = devPillX + devPillW, Bottom = devPillY + devPillH };
                if (devOpen) _openDropdownAnchor = devAnchor;

                _d2d.FillAndStrokeRoundedRect(devPillX, devPillY, devPillW, devPillH, SF(10),
                    devColor, 0.14f + 0.11f * devHovT,
                    devColor, 0.42f + 0.45f * devHovT, 1.1f);
                _d2d.DrawAudioSwitchGlyph(devPillX + S(13), devPillY + devPillH * 0.5f, S(15), devColor, _dpi);
                _d2d.DrawText(devLabel, devPillX + S(23), devPillY, devPillX + devPillW - S(7), devPillY + devPillH,
                    10, Win32.FW_SEMIBOLD, devColor, _dpi);

                var capturedPlay = playing;
                RegisterScrollableHit(devAnchor, "act_dev_pill", () => OpenSessionOutputDeviceMenu(capturedPlay, devAnchor));
            }

            if (y + cardH > headerH)
            {
                RegisterScrollableHit(
                    new Win32.RECT { Left = x, Top = y, Right = devPillX - S(8), Bottom = y + cardH },
                    "act_focus",
                    () => _ = _engine.FocusSessionAsync(playing.SessionId));

                DrawTransportButton("act_prev", TransportGlyph.Previous, prevX, smallY, smallBtn,
                    () => _ = _engine.DispatchMediaActionToSessionAsync(playing.SessionId, MediaActionKind.Previous), circular: true);
                DrawTransportButton("act_play", playing.IsPlaying ? TransportGlyph.Pause : TransportGlyph.Play, playX, playY, playSz,
                    () => _ = _engine.DispatchMediaActionToSessionAsync(playing.SessionId, MediaActionKind.PlayPause), accent: true, circular: true);
                DrawTransportButton("act_next", TransportGlyph.Next, nextX, smallY, smallBtn,
                    () => _ = _engine.DispatchMediaActionToSessionAsync(playing.SessionId, MediaActionKind.Next), circular: true);

                DrawActionButton("act_lock",
                    isLocked ? Strings.BtnUnlock : Strings.BtnLock,
                    lockX, smallY, lockW, smallBtn,
                    isLocked ? Theme.AmberSoft : Theme.BgControl,
                    isLocked ? Theme.AmberSoft : Theme.BgControlHover,
                    isLocked ? Theme.Amber : Theme.TextPrimary,
                    isLocked ? Theme.Amber : Theme.BorderStrong,
                    () => _engine.ToggleManualLockOnActiveOrSelected(playing.SessionId));
            }

            textRight = devPillX - S(14);
        }
        else
        {
            textRight = ctrlRight;
        }

        int textX = x + pad + iconBox + S(16);
        bool isWeb = IsBrowserOrigin(playing.AppKey, playing.IsBrowser);
        int globeSz = isWeb ? S(13) : 0;
        int globeGap = isWeb ? S(5) : 0;
        int arrowSz = isClosed ? 0 : S(13);
        int arrowGap = isClosed ? 0 : S(6);
        int titleW = _d2d.MeasureTextWidth(playing.DisplayName, 16, Win32.FW_BOLD, _dpi) + S(6);
        int nameMaxRight = Math.Clamp(textX + titleW, textX, Math.Max(textX, textRight - globeSz - globeGap - arrowSz - arrowGap));

        _d2d.DrawText(playing.DisplayName, textX, y + S(18), nameMaxRight, y + S(44), 16, Win32.FW_BOLD,
            Theme.BlendRgb(Theme.TextPrimary, Theme.Accent, focusHov), _dpi);

        int bx = nameMaxRight + S(2);
        if (isWeb && bx + globeSz <= textRight)
        {
            _d2d.DrawGlobeGlyph(bx, y + S(24), globeSz, Theme.TextMuted, _dpi);
            bx += globeSz + globeGap;
        }
        if (!isClosed && bx + arrowSz <= textRight)
        {
            _d2d.DrawFocusArrowGlyph(bx, y + S(24), arrowSz, Theme.BlendRgb(Theme.TextMuted, Theme.Accent, focusHov), _dpi);
            bx += arrowSz + S(8);
        }
        else
        {
            bx += S(4);
        }

        (string statusText, uint statusFg, uint statusBg) = isClosed
            ? (Strings.StatusClosed, Theme.TextMuted, Theme.BgControl)
            : (playing.IsEffectivelyPlaying
                ? (Strings.StatusPlaying, Theme.Green, Theme.GreenSoft)
                : (playing.IsMuted || playing.Volume <= 0.005f
                    ? (Strings.StatusMuted, Theme.Red, Theme.RedSoft)
                    : (Strings.StatusPaused, Theme.TextSecondary, Theme.BgControl)));

        int statusPillW = Math.Max(S(54), _d2d.MeasureTextWidth(statusText, 10, Win32.FW_SEMIBOLD, _dpi) + S(18));
        if (bx + statusPillW <= textRight)
            bx += DrawPillBadge(bx, y + S(20), statusText, statusFg, statusBg) + S(6);

        if (isLocked || isPriorityTarget)
        {
            string badgeText = isLocked ? Strings.ModeManualLock : Strings.BadgePriorityTarget;
            int badgePillW = Math.Max(S(54), _d2d.MeasureTextWidth(badgeText, 10, Win32.FW_SEMIBOLD, _dpi) + S(18));
            if (bx + badgePillW <= textRight)
            {
                bx += DrawPillBadge(bx, y + S(20), badgeText,
                    isLocked ? Theme.Amber : Theme.Accent,
                    isLocked ? Theme.AmberSoft : Theme.AccentSoft) + S(8);
            }
        }

        // Playing equalizer badge (static to preserve 0% idle CPU)
        if (!isClosed && playing.IsEffectivelyPlaying && bx + S(20) <= textRight)
        {
            _d2d.DrawEqBars(bx, y + S(23), S(14), Theme.Green, 0, animate: false, _dpi);
        }

        _d2d.DrawText(playing.FormattedMediaTitle, textX, y + S(48), textRight, y + S(70), 13, Win32.FW_MEDIUM, Theme.TextSecondary, _dpi);

        string sourceLabel = playing.Source switch
        {
            SessionSource.MpvIpc => "mpv IPC",
            SessionSource.BrowserBridge => Strings.SourceBrowserBridge,
            SessionSource.AudioSession => Strings.SourceAudioSession,
            _ => "SMTC"
        };
        string metaLine = isClosed
            ? $"{Strings.MetaSourcePrefix}: {sourceLabel}"
            : $"{Strings.MetaSourcePrefix}: {sourceLabel}  •  {Strings.MetaVolumePrefix}: {Strings.FormatPercent(playing.IsMuted ? 0 : (int)MathF.Round(playing.Volume * 100f))}"
              + (playing.IsVideoLikely ? $"  •  {Strings.MetaVideoScrubReady}" : "");

        _d2d.DrawText(metaLine, textX, y + S(74), textRight, y + S(96), 11, Win32.FW_NORMAL, Theme.TextMuted, _dpi);

        return y + cardH;
    }

    private void DrawSessionMixerCard(int x, int y, int w, int h, MediaSessionInfo session, int headerH, double nowMs)
    {
        bool isTarget = _snapshot.ActiveTarget != null &&
                        string.Equals(_snapshot.ActiveTarget.SessionId, session.SessionId, StringComparison.Ordinal);
        bool isLocked = _snapshot.IsManualLock && isTarget;

        string focusId = $"sess_focus_{session.SessionId}";
        string playToggleId = $"sess_play_{session.SessionId}";
        string lockBtnId = $"sess_lock_{session.SessionId}";
        string outBtnId = $"sess_out_{session.SessionId}";
        string ignBtnId = $"sess_ign_{session.SessionId}";
        string volPrefix = $"sess_vol_{session.SessionId}";
        float focusHov = GetHoverT(focusId);

        string? assignedDevId = _engine.GetSessionOutputDeviceId(session.SessionId);
        uint devColor = GetEffectiveDeviceColor(assignedDevId);
        uint priorityColor = isLocked ? Theme.Amber : Theme.Accent;

        uint baseBg = isTarget
            ? Theme.BlendRgb(Theme.BgCardElevated, priorityColor, Theme.IsLight ? 0.06f : 0.11f)
            : Theme.BgCard;
        uint bg = Theme.BlendRgb(baseBg, Theme.BgCardHover, focusHov * 0.65f);
        uint border = isTarget
            ? priorityColor
            : Theme.BlendRgb(Theme.BorderSubtle, Theme.Accent, focusHov);

        _d2d.FillAndStrokeRoundedRect(x, y, w, h, SF(14),
            bg, 1f, border, isTarget ? 0.90f : 1f, isTarget ? 1.5f : 1f);

        float rowGlow = isTarget && string.Equals(_highlightTargetSessionId, session.SessionId, StringComparison.Ordinal)
            ? GetHighlightIntensity(_highlightTargetStartMs, nowMs)
            : 0f;
        if (rowGlow > 0.01f)
            _d2d.DrawCardGlow(x, y, w, h, SF(14), priorityColor, rowGlow, Theme.IsLight, _dpi);

        int pad = S(13);
        int icoBox = S(36);
        int icoY = y + S(13);
        _d2d.FillAndStrokeRoundedRect(x + pad, icoY, icoBox, icoBox, SF(10),
            Theme.BgCardElevated, 1f, Theme.BorderSubtle, 1f);
        int icoSz = S(22);
        DrawAppIconOrMonogram(session.AppKey, session.ExePath, session.DisplayName,
            x + pad + (icoBox - icoSz) * 0.5f, icoY + (icoBox - icoSz) * 0.5f, icoSz, SF(6));

        // Top-right pills: [Clickable Play/Pause Status] [Lock]
        int lockW = Math.Clamp(_d2d.MeasureTextWidth(isLocked ? Strings.BtnLocked : Strings.BtnLock, 10, Win32.FW_SEMIBOLD, _dpi) + S(16), S(54), S(78));
        int lockH = S(24);
        int lockX = x + w - pad - lockW;
        int lockY = y + S(12);

        (string stText, uint stFg, uint stBg) = session.IsEffectivelyPlaying
            ? (Strings.StatusPlaying, Theme.Green, Theme.GreenSoft)
            : (session.IsMuted || session.Volume <= 0.005f ? (Strings.StatusMuted, Theme.Red, Theme.RedSoft) : (Strings.StatusPaused, Theme.TextSecondary, Theme.BgControl));
        int statusW = Math.Clamp(_d2d.MeasureTextWidth(stText, 10, Win32.FW_SEMIBOLD, _dpi) + S(16), S(66), S(96));
        int statusX = lockX - S(6) - statusW;
        int statusY = lockY;

        // Left app title + rank + track title
        int textX = x + pad + icoBox + S(10);
        int textRight = statusX - S(8);
        string tag = session.IsVideoLikely ? $" • {Strings.TagVideo}" : "";
        string headerText = session.DisplayName + tag;

        int rank = -1;
        for (int p = 0; p < _priorityApps.Count; p++)
        {
            if (string.Equals(_priorityApps[p].AppKey, session.AppKey, StringComparison.OrdinalIgnoreCase))
            {
                rank = p + 1;
                break;
            }
        }

        bool isWeb = IsBrowserOrigin(session.AppKey, session.IsBrowser);
        int globeSz = isWeb ? S(11) : 0;
        int globeGap = isWeb ? S(4) : 0;
        string rankText = rank > 0 ? $"#{rank}" : "";
        int rankW = rank > 0 ? Math.Max(S(24), _d2d.MeasureTextWidth(rankText, 9, Win32.FW_BOLD, _dpi) + S(10)) : 0;
        int reserveRight = globeSz + globeGap + (rankW > 0 ? rankW + S(6) : 0);
        int titleMeasured = _d2d.MeasureTextWidth(headerText, 12, Win32.FW_SEMIBOLD, _dpi) + S(4);
        int titleRight = Math.Clamp(textX + titleMeasured, textX, Math.Max(textX, textRight - reserveRight));

        _d2d.DrawText(headerText, textX, y + S(11), textRight - reserveRight, y + S(31), 12, Win32.FW_SEMIBOLD,
            Theme.BlendRgb(Theme.TextPrimary, Theme.Accent, focusHov), _dpi);

        int curX = titleRight + S(3);
        if (isWeb && curX + globeSz <= textRight)
        {
            _d2d.DrawGlobeGlyph(curX, y + S(15), globeSz, Theme.TextMuted, _dpi);
            curX += globeSz + globeGap;
        }
        if (rank > 0 && curX + rankW <= textRight)
        {
            int rh = S(17);
            int ry = y + S(13);
            _d2d.FillRoundedRect(curX, ry, rankW, rh, SF(5), isTarget ? Theme.AccentSoft : Theme.BgControl);
            _d2d.DrawText(rankText, curX, ry, curX + rankW, ry + rh, 9, Win32.FW_BOLD,
                isTarget ? Theme.Accent : Theme.TextSecondary, _dpi, centered: true);
        }

        _d2d.DrawText(session.FormattedMediaTitle, textX, y + S(32), textRight, y + S(50), 11, Win32.FW_NORMAL, Theme.TextSecondary, _dpi);

        // Status pill (clickable Play/Pause toggle!)
        float statusHovT = GetHoverT(playToggleId);
        _d2d.FillAndStrokeRoundedRect(statusX, statusY, statusW, lockH, lockH * 0.5f,
            stBg, 1f, stFg, 0.28f + 0.60f * statusHovT, 1f);
        _d2d.DrawText(stText, statusX, statusY, statusX + statusW, statusY + lockH,
            10, Win32.FW_SEMIBOLD, stFg, _dpi, centered: true);

        // Lock button
        DrawActionButton(lockBtnId, isLocked ? Strings.BtnLocked : Strings.BtnLock,
            lockX, lockY, lockW, lockH,
            isLocked ? Theme.AmberSoft : Theme.BgControl,
            isLocked ? Theme.AmberSoft : Theme.BgControlHover,
            isLocked ? Theme.Amber : Theme.TextPrimary,
            isLocked ? Theme.Amber : Theme.BorderStrong,
            () => _engine.ToggleManualLockOnActiveOrSelected(session.SessionId),
            ptSize: 10);

        // Bottom control row: [Thick C1 Volume Bar] [⇄ Device Color Button] [⊘ Ignore]
        int botY = y + h - pad - S(28);
        int smallSz = S(28);
        int ignX = x + w - pad - smallSz;
        bool showOutBtn = _engine.IsPerAppAudioRoutingSupported && session.ProcessIds.Length > 0;
        int outX = showOutBtn ? ignX - S(6) - smallSz : ignX;
        int volRight = (showOutBtn ? outX : ignX) - S(8);
        int volX = x + pad;
        int volW = Math.Max(S(80), volRight - volX);

        if (y + h > headerH)
        {
            var captured = session;
            RegisterScrollableHit(
                new Win32.RECT { Left = x, Top = y, Right = statusX - S(4), Bottom = botY - S(4) },
                focusId,
                () => _ = _engine.FocusSessionAsync(captured.SessionId));

            RegisterScrollableHit(
                new Win32.RECT { Left = statusX, Top = statusY, Right = statusX + statusW, Bottom = statusY + lockH },
                playToggleId,
                () => _ = _engine.DispatchMediaActionToSessionAsync(captured.SessionId, MediaActionKind.PlayPause));

            DrawControlCenterVolumeBar(volPrefix, session.SessionId, session.Volume, session.IsMuted,
                volX, botY, volW, smallSz, devColor, headerH);

            if (showOutBtn)
            {
                bool isOutMenuOpen = _openDropdown == DropdownKind.SessionOutputDevice &&
                                     string.Equals(_openDropdownSessionId, captured.SessionId, StringComparison.Ordinal);
                var outAnchor = new Win32.RECT { Left = outX, Top = botY, Right = outX + smallSz, Bottom = botY + smallSz };
                if (isOutMenuOpen) _openDropdownAnchor = outAnchor;

                DrawIconButton(outBtnId, SmallButtonGlyph.AudioDevice, outX, botY, smallSz, true,
                    () => OpenSessionOutputDeviceMenu(captured, outAnchor),
                    active: isOutMenuOpen,
                    customAccent: devColor);
            }

            DrawIconButton(ignBtnId, SmallButtonGlyph.Hide, ignX, botY, smallSz, true,
                () =>
                {
                    if (_stateStore.IgnoreApp(captured.AppKey, captured.DisplayName, captured.ExePath))
                    {
                        _ = _sessionManager.RefreshAsync();
                        ShowStatusBanner(Strings.MsgAppIgnored(captured.DisplayName));
                    }
                }, danger: true);
        }
    }

    private void DrawControlCenterVolumeBar(
        string idPrefix, string sessionId, float volume, bool isMuted,
        int x, int y, int w, int h, uint deviceRgb, int headerH)
    {
        string sliderId = $"{idPrefix}_slider";
        string muteId = $"{idPrefix}_mute";
        bool isDraggingThis = string.Equals(_draggingSliderId, sliderId, StringComparison.Ordinal);
        float sliderHovT = isDraggingThis ? 1f : GetHoverT(sliderId);
        float muteHovT = GetHoverT(muteId);

        float targetVol = isMuted ? 0f : Math.Clamp(volume, 0f, 1f);
        float dispVol = GetAnimatedVolume(sessionId, targetVol, isDraggingThis, _dragPreviewVolume);

        float radius = SF(9);
        _d2d.FillAndStrokeRoundedRect(x, y, w, h, radius,
            Theme.BgControl, 0.85f,
            Theme.BlendRgb(Theme.BorderSubtle, deviceRgb, sliderHovT), 0.70f + 0.30f * sliderHovT, 1f);

        // Mute button hit region on left 28px of the bar
        int muteW = Math.Min(w / 2, S(28));

        // Filled bar clipped smoothly inside the rounded track aligned to slider thumb [x+muteW .. x+w]
        float fillW = dispVol <= 0.003f ? 0f : muteW + Math.Max(0, w - muteW) * dispVol;
        if (fillW > 2f)
        {
            uint fillStart = (isMuted && !isDraggingThis) ? Theme.Red : Theme.BlendRgb(Theme.BgControl, deviceRgb, 0.65f);
            uint fillEnd = (isMuted && !isDraggingThis) ? Theme.Red : deviceRgb;
            _d2d.PushClip(x, y, MathF.Min(w, fillW), h);
            _d2d.FillLinearGradientRoundedRect(x, y, w, h, radius, fillStart, 0.92f, fillEnd, 0.95f);
            _d2d.PopClip();
        }
        uint speakerColor = isMuted && !isDraggingThis
            ? Theme.Red
            : (dispVol > 0.14f && !Theme.IsLight ? 0xFFFFFFu : Theme.TextPrimary);
        if (muteHovT > 0.01f)
            _d2d.FillRoundedRect(x + S(2), y + S(2), muteW - S(4), h - S(4), SF(6), 0xFFFFFF, (Theme.IsLight ? 0.32f : 0.16f) * muteHovT);

        _d2d.DrawSpeakerGlyph(x + (muteW - S(20)) * 0.5f, y + (h - S(20)) * 0.5f, S(20), isMuted && !isDraggingThis, speakerColor, _dpi);

        int dispPct = (int)MathF.Round(dispVol * 100f);
        string pctStr = Strings.FormatPercent(dispPct);
        uint pctFg = isMuted && !isDraggingThis
            ? Theme.Red
            : (dispVol > 0.82f && !Theme.IsLight ? 0xFFFFFFu : Theme.TextPrimary);
        _d2d.DrawText(pctStr, x + muteW + S(4), y, x + w - S(9), y + h, 11, Win32.FW_SEMIBOLD,
            pctFg, _dpi, align: D2DRenderer.TextAlign.Right);

        if (y + h > headerH)
        {
            RegisterScrollableHit(
                new Win32.RECT { Left = x, Top = y, Right = x + muteW, Bottom = y + h },
                muteId,
                () => _ = _engine.ToggleSessionMuteAsync(sessionId));

            int trackLeft = x + muteW;
            int trackW = Math.Max(S(24), w - muteW);
            int hitTop = Math.Max(_bodyClipTop, y);
            int hitBottom = Math.Min(_bodyClipBottom, y + h);
            if (hitBottom > hitTop)
            {
                _sliderRegions.Add(new SliderRegion(
                    new Win32.RECT { Left = trackLeft, Top = hitTop, Right = x + w, Bottom = hitBottom },
                    sliderId,
                    sessionId,
                    trackLeft,
                    trackW));
            }
        }
    }

    private int DrawQuickPriorityStrip(int x, int y, int w, int headerH)
    {
        int stripH = S(42);
        _d2d.FillAndStrokeRoundedRect(x, y, w, stripH, SF(11),
            Theme.BgCard, 1f, Theme.BorderSubtle, 1f);

        int pad = S(14);
        string label = $"{Strings.QuickPriorityLabel}:";
        int labelW = _d2d.MeasureTextWidth(label, 10, Win32.FW_BOLD, _dpi) + S(8);
        _d2d.DrawText(label, x + pad, y, x + pad + labelW, y + stripH, 10, Win32.FW_BOLD, Theme.TextMuted, _dpi);

        int manageW = Math.Clamp(_d2d.MeasureTextWidth(Strings.BtnManageAll, 11, Win32.FW_SEMIBOLD, _dpi) + S(16), S(110), S(170));
        int manageX = x + w - pad - manageW;
        float manHovT = GetHoverT("qp_manage_all");
        _d2d.DrawText(Strings.BtnManageAll, manageX, y, manageX + manageW, y + stripH, 11, Win32.FW_SEMIBOLD,
            Theme.BlendRgb(Theme.Accent, Theme.AccentHover, manHovT), _dpi, align: D2DRenderer.TextAlign.Right);

        if (y + stripH > headerH)
        {
            RegisterScrollableHit(
                new Win32.RECT { Left = manageX, Top = y, Right = manageX + manageW, Bottom = y + stripH },
                "qp_manage_all",
                () => SwitchToTab(TabKind.Apps));
        }

        int curX = x + pad + labelW + S(4);
        int maxRight = manageX - S(10);
        int maxCount = Math.Min(_priorityApps.Count, 4);
        int pillH = S(26);
        int pillY = y + (stripH - pillH) / 2;

        for (int i = 0; i < maxCount; i++)
        {
            var app = _priorityApps[i];
            string chipText = $"#{i + 1} {app.DisplayName}";
            int chipW = Math.Clamp(_d2d.MeasureTextWidth(chipText, 11, Win32.FW_SEMIBOLD, _dpi) + S(18), S(56), S(142));
            if (curX + chipW > maxRight)
                break;

            bool isTop = i == 0;
            string hitId = $"qp_chip_{i}";
            float hovT = isTop ? 0f : GetHoverT(hitId);
            uint chipBg = isTop ? Theme.AccentSoft : Theme.BlendRgb(Theme.BgControl, Theme.BgControlHover, hovT);
            uint chipBorder = isTop ? Theme.Accent : Theme.BlendRgb(Theme.BorderSubtle, Theme.Accent, hovT);
            _d2d.FillAndStrokeRoundedRect(curX, pillY, chipW, pillH, SF(8),
                chipBg, 1f, chipBorder, isTop ? 0.5f : (0.9f + 0.1f * hovT), 1f);
            _d2d.DrawText(chipText, curX + S(6), pillY, curX + chipW - S(6), pillY + pillH, 11,
                Win32.FW_SEMIBOLD, isTop ? Theme.Accent : Theme.TextPrimary, _dpi, centered: true);

            if (!isTop && y + stripH > headerH)
            {
                var capturedKey = app.AppKey;
                RegisterScrollableHit(
                    new Win32.RECT { Left = curX, Top = pillY, Right = curX + chipW, Bottom = pillY + pillH },
                    hitId,
                    () =>
                    {
                        if (_stateStore.MovePriorityAppToTop(capturedKey))
                            TriggerPriorityMoveFeedback(capturedKey, movedUp: true);
                    });
            }

            curX += chipW;
            if (i < maxCount - 1 && curX + S(14) <= maxRight)
            {
                _d2d.DrawText("›", curX, pillY, curX + S(14), pillY + pillH, 12, Win32.FW_NORMAL, Theme.TextMuted, _dpi, centered: true);
                curX += S(14);
            }
        }

        return y + stripH;
    }

    // =========================================================================
    // TAB 2: APPS (Full Priority Queue with Smooth FLIP Cascade + Ignored Apps)
    // =========================================================================
    private int DrawAppsTab(int x, int y, int w, int headerH, double nowMs)
    {
        y = DrawStatusBannerIfActive(x, y, w);

        int clearW = Math.Clamp(_d2d.MeasureTextWidth(Strings.BtnResetHistory, 11, Win32.FW_SEMIBOLD, _dpi) + S(22), S(110), S(200));
        _d2d.DrawText($"{Strings.SecPriorityList} ({_priorityApps.Count})", x, y, x + w - clearW - S(10), y + S(24),
            11, Win32.FW_BOLD, Theme.TextMuted, _dpi);

        if (_priorityApps.Count > 0 && y + S(24) > headerH)
        {
            DrawActionButton("btn_clear_hist", Strings.BtnResetHistory, x + w - clearW, y - S(2), clearW, S(26),
                Theme.BgCard, Theme.RedSoft, Theme.Red, Theme.BorderSubtle,
                () => _stateStore.ClearPriorityHistory());
        }
        y += S(28);

        if (_priorityApps.Count == 0)
        {
            _prioAnimY.Clear();
            _prioTargetY.Clear();
            y = DrawEmptyCard(x, y, w, Strings.EmptyPriorityTitle, Strings.EmptyPrioritySub);
        }
        else
        {
            int listTopY = y;
            int rowH = S(54);
            int rowGap = S(8);

            // Sync FLIP target slots so every displaced row glides smoothly into place
            _prioTargetY.Clear();
            for (int i = 0; i < _priorityApps.Count; i++)
            {
                string key = _priorityApps[i].AppKey;
                float targetRelY = i * (rowH + rowGap);
                _prioTargetY[key] = targetRelY;
                if (!_prioAnimY.ContainsKey(key) || _settings.ReducedAnimations)
                {
                    _prioAnimY[key] = targetRelY;
                }
                else if (MathF.Abs(_prioAnimY[key] - targetRelY) > 0.35f)
                {
                    _anySpringAnimating = true;
                }
            }

            if (_prioAnimY.Count > _priorityApps.Count)
            {
                _animPruneScratch.Clear();
                foreach (string staleKey in _prioAnimY.Keys)
                {
                    if (!_prioTargetY.ContainsKey(staleKey))
                        _animPruneScratch.Add(staleKey);
                }
                for (int i = 0; i < _animPruneScratch.Count; i++)
                    _prioAnimY.Remove(_animPruneScratch[i]);
            }

            bool hasHighlight = !string.IsNullOrEmpty(_highlightPrioAppKey) &&
                                GetHighlightIntensity(_highlightPrioStartMs, nowMs) > 0.005f;
            int deferredIdx = -1;
            int deferredTargetY = 0;

            for (int i = 0; i < _priorityApps.Count; i++)
            {
                int slotY = listTopY + i * (rowH + rowGap);
                if (hasHighlight &&
                    deferredIdx < 0 &&
                    string.Equals(_highlightPrioAppKey, _priorityApps[i].AppKey, StringComparison.OrdinalIgnoreCase))
                {
                    deferredIdx = i;
                    deferredTargetY = slotY;
                    continue;
                }

                float animRel = _prioAnimY.TryGetValue(_priorityApps[i].AppKey, out float ay) ? ay : i * (rowH + rowGap);
                float drawY = listTopY + animRel;
                if (MathF.Max(slotY + rowH, drawY + rowH) < _bodyClipTop - rowH ||
                    MathF.Min(slotY, drawY) > _bodyClipBottom + rowH)
                    continue;
                DrawPriorityRow(x, slotY, drawY, w, _priorityApps[i], i, _priorityApps.Count, headerH, nowMs);
            }

            // Draw the highlighted/promoted row last so its shadow, scale lift, and halo render on top
            if (deferredIdx >= 0)
            {
                var defApp = _priorityApps[deferredIdx];
                float animRel = _prioAnimY.TryGetValue(defApp.AppKey, out float ay) ? ay : deferredIdx * (rowH + rowGap);
                DrawPriorityRow(x, deferredTargetY, listTopY + animRel, w, defApp, deferredIdx, _priorityApps.Count, headerH, nowMs);
            }

            y = listTopY + _priorityApps.Count * (rowH + rowGap) - rowGap;
        }

        y += S(18);
        y = DrawIgnoredAppsSection(x, y, w, headerH);
        return y;
    }

    private int DrawPriorityRow(
        int x, int hitY, float drawYFloat, int w,
        DiscoveredApp app, int index, int total, int headerH, double nowMs)
    {
        int rowH = S(54);
        int drawY = (int)MathF.Round(drawYFloat);
        bool isHighlighted = string.Equals(_highlightPrioAppKey, app.AppKey, StringComparison.OrdinalIgnoreCase);
        float glow = isHighlighted ? GetHighlightIntensity(_highlightPrioStartMs, nowMs) : 0f;
        bool isLiveActive = _snapshot.ActiveTarget != null &&
                            string.Equals(_snapshot.ActiveTarget.AppKey, app.AppKey, StringComparison.OrdinalIgnoreCase);

        bool didTransform = false;
        if (glow > 0.02f && !_settings.ReducedAnimations)
        {
            float travel = _prioAnimY.TryGetValue(app.AppKey, out float cy) && _prioTargetY.TryGetValue(app.AppKey, out float ty)
                ? Math.Clamp(MathF.Abs(ty - cy) / Math.Max(1f, S(36)), 0f, 1f)
                : 0f;
            float lift = 1f + 0.015f * MathF.Max(glow * 0.45f, travel);
            _d2d.PushTransform(Direct2D.D2D1_MATRIX_3X2_F.ScaleAt(lift, lift, x + w * 0.5f, drawYFloat + rowH * 0.5f));
            didTransform = true;
            _d2d.FillRoundedRect(x - S(2), drawYFloat + S(4), w + S(4), rowH + S(4), SF(12), 0x000000, 0.24f * glow);
        }

        uint cardBg = (glow > 0.01f || isLiveActive) ? Theme.BgCardElevated : Theme.BgCard;
        uint cardBorder = isLiveActive ? Theme.Accent : Theme.BorderSubtle;
        _d2d.FillAndStrokeRoundedRect(x, drawYFloat, w, rowH, SF(10),
            cardBg, 1f, cardBorder, isLiveActive ? 0.58f : 1f, isLiveActive ? 1.2f : 1f);

        if (glow > 0.01f)
        {
            _d2d.DrawCardGlow(x, drawYFloat, w, rowH, SF(10), Theme.Accent, glow, Theme.IsLight, _dpi);
            float barAlpha = Math.Clamp(glow * 1.4f, 0f, 1f);
            _d2d.FillRoundedRect(x + S(3), drawYFloat + S(9), S(3), rowH - S(18), SF(1.5f), Theme.Accent, barAlpha);
        }

        int pad = S(14);
        int rankW = S(30);
        int rankH = S(24);
        float rankY = drawYFloat + (rowH - rankH) * 0.5f;
        bool glowPill = glow > 0.05f;
        uint pillBg = glowPill ? Theme.Accent : (index == 0 ? Theme.AccentSoft : Theme.BgControl);
        uint pillFg = glowPill ? Theme.OnAccent : (index == 0 ? Theme.Accent : Theme.TextSecondary);
        _d2d.FillRoundedRect(x + pad, rankY, rankW, rankH, SF(6), pillBg);
        _d2d.DrawText($"#{index + 1}", x + pad, rankY, x + pad + rankW, rankY + rankH, 11, Win32.FW_BOLD, pillFg, _dpi, centered: true);

        int icoX = x + pad + rankW + S(10);
        int icoSz = S(24);
        DrawAppIconOrMonogram(app.AppKey, app.ExePath, app.DisplayName,
            icoX, drawYFloat + (rowH - icoSz) * 0.5f, icoSz, SF(6));

        int btnSz = S(28);
        int btnY = drawY + (rowH - btnSz) / 2;
        int hitBtnY = hitY + (rowH - btnSz) / 2;
        int delX = x + w - pad - btnSz;
        int ignX = delX - S(6) - btnSz;
        int downX = ignX - S(6) - btnSz;
        int upX = downX - S(6) - btnSz;
        int topX = upX - S(6) - btnSz;

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
        int pillW = Math.Clamp(_d2d.MeasureTextWidth(hkText, 11, Win32.FW_SEMIBOLD, _dpi) + S(20), S(86), S(168));
        int clrX = topX - S(10) - clrSz;
        int hkX = (hasHk && !isCapturingThis)
            ? (clrX - S(4) - pillW)
            : (topX - S(10) - pillW);

        bool showDirBadge = glowPill && _highlightPrioDir != 0;
        int dirBadgeSz = S(20);
        int dirBadgeX = hkX - S(8) - dirBadgeSz;
        if (showDirBadge)
        {
            float dirBadgeY = drawYFloat + (rowH - dirBadgeSz) * 0.5f;
            float badgeAlpha = Math.Clamp(glow, 0f, 1f);
            _d2d.FillAndStrokeRoundedRect(dirBadgeX, dirBadgeY, dirBadgeSz, dirBadgeSz, SF(6),
                Theme.AccentSoft, badgeAlpha, Theme.Accent, badgeAlpha, 1f);
            _d2d.DrawDirectionArrowGlyph(dirBadgeX + dirBadgeSz * 0.5f, dirBadgeY + dirBadgeSz * 0.5f,
                SF(10f), _highlightPrioDir < 0, Theme.Accent, badgeAlpha, _dpi);
        }

        int textX = icoX + icoSz + S(12);
        int textRight = showDirBadge ? (dirBadgeX - S(8)) : (hkX - S(12));
        bool isWeb = IsBrowserOrigin(app.AppKey);
        int globeSz = isWeb ? S(12) : 0;
        int globeGap = isWeb ? S(5) : 0;
        int nameReserve = globeSz + globeGap;
        int titleMeasured = _d2d.MeasureTextWidth(app.DisplayName, 13, Win32.FW_SEMIBOLD, _dpi) + S(4);
        int nameDrawRight = Math.Clamp(textX + titleMeasured, textX, Math.Max(textX, textRight - nameReserve));

        _d2d.DrawText(app.DisplayName, textX, drawYFloat + S(8), textRight - nameReserve, drawYFloat + S(28), 13, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi);
        if (isWeb && nameDrawRight + globeSz + S(2) <= textRight)
            _d2d.DrawGlobeGlyph(nameDrawRight + S(3), drawYFloat + S(12), globeSz, Theme.TextMuted, _dpi);

        string? browserKey = BrowserSiteScanner.ExtractBrowserKeyFromAppKey(app.AppKey);
        string exeSub = string.IsNullOrWhiteSpace(app.ExePath) ? app.AppKey : app.ExePath;
        if (!string.IsNullOrEmpty(browserKey))
            exeSub = $"{ProcessResolver.FormatDisplayName(browserKey, app.ExePath)} • {exeSub}";
        _d2d.DrawText(exeSub, textX, drawYFloat + S(27), textRight, drawYFloat + S(46), 11, Win32.FW_NORMAL, Theme.TextMuted, _dpi);

        if (hitY + rowH > headerH)
        {
            DrawHotkeyPillButton(hkHitId, hkText, hkX, btnY, pillW, btnSz,
                isCapturing: isCapturingThis,
                hasValue: hasHk,
                () => StartHotkeyCapture(hkTargetId, hkHitId, hk => _stateStore.SetPriorityAppHotkey(app.AppKey, hk)),
                hitY: hitBtnY);

            if (hasHk && !isCapturingThis)
            {
                DrawIconButton(hkClrHitId, SmallButtonGlyph.Close, clrX, drawY + (rowH - clrSz) / 2, clrSz, true,
                    () => _stateStore.SetPriorityAppHotkey(app.AppKey, string.Empty), danger: true, hitY: hitY + (rowH - clrSz) / 2);
            }

            DrawIconButton($"prio_top_{app.AppKey}", SmallButtonGlyph.Top, topX, btnY, btnSz, index > 0,
                () =>
                {
                    if (_stateStore.MovePriorityAppToTop(app.AppKey))
                        TriggerPriorityMoveFeedback(app.AppKey, movedUp: true);
                }, hitY: hitBtnY);
            DrawIconButton($"prio_up_{app.AppKey}", SmallButtonGlyph.Up, upX, btnY, btnSz, index > 0,
                () =>
                {
                    if (_stateStore.MovePriorityApp(app.AppKey, -1))
                        TriggerPriorityMoveFeedback(app.AppKey, movedUp: true);
                }, hitY: hitBtnY);
            DrawIconButton($"prio_dn_{app.AppKey}", SmallButtonGlyph.Down, downX, btnY, btnSz, index < total - 1,
                () =>
                {
                    if (_stateStore.MovePriorityApp(app.AppKey, 1))
                        TriggerPriorityMoveFeedback(app.AppKey, movedUp: false);
                }, hitY: hitBtnY);
            DrawIconButton($"prio_ign_{app.AppKey}", SmallButtonGlyph.Hide, ignX, btnY, btnSz, true,
                () =>
                {
                    if (_stateStore.IgnoreApp(app.AppKey, app.DisplayName, app.ExePath))
                    {
                        _ = _sessionManager.RefreshAsync();
                        ShowStatusBanner(Strings.MsgAppIgnored(app.DisplayName));
                    }
                }, danger: true, hitY: hitBtnY);
            DrawIconButton($"prio_rm_{app.AppKey}", SmallButtonGlyph.Close, delX, btnY, btnSz, true,
                () => _stateStore.RemovePriorityApp(app.AppKey), danger: true, hitY: hitBtnY);
        }

        if (didTransform)
            _d2d.PopTransform();

        return hitY + rowH;
    }

    // =========================================================================
    // TAB 3: AUDIO & RULES
    // =========================================================================
    private int DrawAudioRulesTab(int x, int y, int w, int headerH)
    {
        y = DrawStatusBannerIfActive(x, y, w);

        _d2d.DrawText(Strings.SecMediaAudio, x, y, x + w, y + S(20), 11, Win32.FW_BOLD, Theme.TextMuted, _dpi);
        y += S(24);

        y = DrawToggleSettingRow(x, y, w, headerH, "set_autopause",
            Strings.SetAutoPauseTitle, Strings.SetAutoPauseSub,
            _settings.AutoPauseEnabled,
            () => _stateStore.UpdateSettings(s => s.AutoPauseEnabled = !s.AutoPauseEnabled));
        y += S(8);

        y = DrawToggleSettingRow(x, y, w, headerH, "set_autoduck",
            Strings.SetAutoDuckTitle, Strings.SetAutoDuckSub,
            _settings.AutoDuckingEnabled,
            () => _stateStore.UpdateSettings(s => s.AutoDuckingEnabled = !s.AutoDuckingEnabled));
        y += S(8);

        y = DrawToggleSettingRow(x, y, w, headerH, "set_mic_duck",
            Strings.SetMicDuckTitle, Strings.SetMicDuckSub,
            _settings.MicDuckingEnabled,
            () => _stateStore.UpdateSettings(s => s.MicDuckingEnabled = !s.MicDuckingEnabled));
        y += S(8);

        if (_settings.AutoDuckingEnabled || _settings.MicDuckingEnabled)
        {
            y = DrawStepperSettingRow(x, y, w, headerH, "set_duck_vol",
                Strings.SetDuckVolTitle, Strings.SetDuckVolSub,
                Strings.FormatPercent(_settings.DuckVolumePercent),
                () => _stateStore.UpdateSettings(s => s.DuckVolumePercent = Math.Max(5, s.DuckVolumePercent - 5)),
                () => _stateStore.UpdateSettings(s => s.DuckVolumePercent = Math.Min(80, s.DuckVolumePercent + 5)));
            y += S(8);
        }

        y = DrawSiteVolumeMemoryRow(x, y, w, headerH);
        y += S(8);

        y = DrawToggleSettingRow(x, y, w, headerH, "set_headphone",
            Strings.SetHeadphoneTitle, Strings.SetHeadphoneSub,
            _settings.HeadphoneGuardEnabled,
            () => _stateStore.UpdateSettings(s => s.HeadphoneGuardEnabled = !s.HeadphoneGuardEnabled));
        y += S(8);

        y = DrawToggleSettingRow(x, y, w, headerH, "set_game",
            Strings.SetGameGuardTitle, Strings.SetGameGuardSub,
            _settings.GameFullscreenGuardEnabled,
            () => _stateStore.UpdateSettings(s => s.GameFullscreenGuardEnabled = !s.GameFullscreenGuardEnabled));
        y += S(8);

        y = DrawToggleSettingRow(x, y, w, headerH, "set_tray_wheel",
            Strings.SetTrayWheelTitle, Strings.SetTrayWheelSub,
            _settings.TrayWheelVolumeEnabled,
            () => _stateStore.UpdateSettings(s => s.TrayWheelVolumeEnabled = !s.TrayWheelVolumeEnabled));
        y += S(8);

        y = DrawStepperSettingRow(x, y, w, headerH, "set_vol_step",
            Strings.SetVolStepTitle, Strings.SetVolStepSub,
            Strings.FormatPercent(_settings.VolumeStepPercent),
            () => _stateStore.UpdateSettings(s => s.VolumeStepPercent = Math.Max(1, s.VolumeStepPercent - 1)),
            () => _stateStore.UpdateSettings(s => s.VolumeStepPercent = Math.Min(20, s.VolumeStepPercent + 1)));
        y += S(8);

        y = DrawToggleSettingRow(x, y, w, headerH, "set_osd",
            Strings.SetOsdTitle, Strings.SetOsdSub,
            _settings.OsdEnabled,
            () => _stateStore.UpdateSettings(s => s.OsdEnabled = !s.OsdEnabled));

        y += S(22);
        _d2d.DrawText(Strings.SecScrubbing, x, y, x + w, y + S(20), 11, Win32.FW_BOLD, Theme.TextMuted, _dpi);
        y += S(24);

        y = DrawToggleSettingRow(x, y, w, headerH, "set_scrub_enabled",
            Strings.SetScrubEnabledTitle, Strings.SetScrubEnabledSub,
            _settings.TimelineScrubbingEnabled,
            () => _stateStore.UpdateSettings(s => s.TimelineScrubbingEnabled = !s.TimelineScrubbingEnabled));
        y += S(8);

        y = DrawStepperSettingRow(x, y, w, headerH, "set_seek",
            Strings.SetSeekSecondsTitle, Strings.SetSeekSecondsSub,
            $"{_settings.SeekSeconds} {Strings.UnitSeconds}",
            () => _stateStore.UpdateSettings(s => s.SeekSeconds = Math.Max(1, s.SeekSeconds - 5)),
            () => _stateStore.UpdateSettings(s => s.SeekSeconds = Math.Min(120, s.SeekSeconds + 5)));
        y += S(8);

        y = DrawStepperSettingRow(x, y, w, headerH, "set_hold",
            Strings.SetLongPressTitle, Strings.SetLongPressSub,
            $"{_settings.LongPressThresholdMs} ms",
            () => _stateStore.UpdateSettings(s => s.LongPressThresholdMs = Math.Max(150, s.LongPressThresholdMs - 50)),
            () => _stateStore.UpdateSettings(s => s.LongPressThresholdMs = Math.Min(1500, s.LongPressThresholdMs + 50)));

        return y;
    }

    // =========================================================================
    // TAB 4: HOTKEYS (Global Hook & Keycap Bindings)
    // =========================================================================
    private int DrawHotkeysTab(int x, int y, int w, int headerH)
    {
        y = DrawStatusBannerIfActive(x, y, w);

        y = DrawToggleSettingRow(x, y, w, headerH, "set_hook_enabled",
            Strings.SetHookEnabledTitle, Strings.SetHookEnabledSub,
            _settings.MediaHookEnabled,
            () => _stateStore.UpdateSettings(s => s.MediaHookEnabled = !s.MediaHookEnabled));

        y += S(20);
        _d2d.DrawText(Strings.SecGlobalHotkeys, x, y, x + w, y + S(20), 11, Win32.FW_BOLD, Theme.TextMuted, _dpi);
        y += S(24);

        y = DrawHotkeySettingRow(x, y, w, headerH, "global:toggle",
            Strings.SetHkToggleTitle, Strings.SetHkToggleSub,
            _settings.HotkeyToggleWindow, hk => _stateStore.SetGlobalHotkey(GlobalHotkeySlot.ToggleWindow, hk));
        y += S(8);

        y = DrawHotkeySettingRow(x, y, w, headerH, "global:lock",
            Strings.SetHkLockTitle, Strings.SetHkLockSub,
            _settings.HotkeyManualLock, hk => _stateStore.SetGlobalHotkey(GlobalHotkeySlot.ManualLock, hk));
        y += S(8);

        y = DrawHotkeySettingRow(x, y, w, headerH, "global:playpause",
            Strings.SetHkPlayPauseTitle, Strings.SetHkPlayPauseSub,
            _settings.HotkeyPlayPause, hk => _stateStore.SetGlobalHotkey(GlobalHotkeySlot.PlayPause, hk),
            allowAnySingleKey: true);
        y += S(8);

        y = DrawHotkeySettingRow(x, y, w, headerH, "global:promote_playing",
            Strings.SetHkPromotePlayingTitle, Strings.SetHkPromotePlayingSub,
            _settings.HotkeyPromotePlaying, hk => _stateStore.SetGlobalHotkey(GlobalHotkeySlot.PromotePlaying, hk),
            allowAnySingleKey: true);
        y += S(8);

        y = DrawHotkeySettingRow(x, y, w, headerH, "global:vol_up",
            Strings.SetHkVolUpTitle, Strings.SetHkVolUpSub,
            _settings.HotkeyVolumeUp, hk => _stateStore.SetGlobalHotkey(GlobalHotkeySlot.VolumeUp, hk));
        y += S(8);

        y = DrawHotkeySettingRow(x, y, w, headerH, "global:vol_down",
            Strings.SetHkVolDownTitle, Strings.SetHkVolDownSub,
            _settings.HotkeyVolumeDown, hk => _stateStore.SetGlobalHotkey(GlobalHotkeySlot.VolumeDown, hk));
        y += S(8);

        y = DrawHotkeySettingRow(x, y, w, headerH, "global:vol_mute",
            Strings.SetHkVolMuteTitle, Strings.SetHkVolMuteSub,
            _settings.HotkeyVolumeMute, hk => _stateStore.SetGlobalHotkey(GlobalHotkeySlot.VolumeMute, hk));
        y += S(8);

        y = DrawHotkeySettingRow(x, y, w, headerH, "global:cycle_audio",
            Strings.SetHkCycleAudioTitle, Strings.SetHkCycleAudioSub,
            _settings.HotkeyCycleAudioDevice, hk => _stateStore.SetGlobalHotkey(GlobalHotkeySlot.CycleAudioDevice, hk));
        y += S(8);

        _d2d.DrawText(Strings.HotkeyHint, x + S(4), y, x + w - S(4), y + S(18), 10, Win32.FW_NORMAL, Theme.TextMuted, _dpi);
        y += S(22);

        int infoH = S(96);
        _d2d.FillAndStrokeRoundedRect(x, y, w, infoH, SF(10), Theme.BgCard, 1f, Theme.BorderSubtle, 1f);
        int ix = x + S(16);
        int ir = x + w - S(16);
        _d2d.DrawText(Strings.ShortcutSecondary, ix, y + S(12), ir, y + S(32), 12, Win32.FW_NORMAL, Theme.TextSecondary, _dpi);
        _d2d.DrawText(Strings.FormatShortcutToggles(_settings.HotkeyToggleWindow, _settings.HotkeyManualLock),
            ix, y + S(36), ir, y + S(56), 12, Win32.FW_NORMAL, Theme.TextSecondary, _dpi);
        _d2d.DrawText(Strings.ShortcutTrayWheel, ix, y + S(60), ir, y + S(80), 12, Win32.FW_NORMAL, Theme.TextSecondary, _dpi);

        return y + infoH;
    }

    // =========================================================================
    // TAB 5: SETTINGS (Appearance, Reduced Animations, Bridge, System, Tools)
    // =========================================================================
    private int DrawSettingsTab(int x, int y, int w, int headerH)
    {
        int initialY = y;
        y = DrawStatusBannerIfActive(x, y, w);

        // 0. APPEARANCE, THEME GALLERY & LANGUAGE
        _d2d.DrawText(Strings.SecAppearance, x, y, x + w, y + S(20), 11, Win32.FW_BOLD, Theme.TextMuted, _dpi);
        y += S(24);

        y = DrawThemeGalleryCard(x, y, w, headerH);
        y += S(8);

        y = DrawLanguageDropdownRow(x, y, w, headerH);
        y += S(8);

        y = DrawToggleSettingRow(x, y, w, headerH, "set_reduced_anim",
            Strings.SetReducedAnimTitle, Strings.SetReducedAnimSub,
            _settings.ReducedAnimations,
            () => _stateStore.UpdateSettings(s => s.ReducedAnimations = !s.ReducedAnimations));

        // 1. COMPANION BROWSER EXTENSION BRIDGE
        y += S(22);
        _settingsBridgeSectionOffset = Math.Max(0, y - initialY);
        _d2d.DrawText(Strings.SecBrowserBridge, x, y, x + w, y + S(20), 11, Win32.FW_BOLD, Theme.TextMuted, _dpi);
        y += S(24);

        y = DrawBrowserBridgeCard(x, y, w, headerH);

        // 2. SYSTEM & STARTUP
        y += S(22);
        _d2d.DrawText(Strings.SecSystemStartup, x, y, x + w, y + S(20), 11, Win32.FW_BOLD, Theme.TextMuted, _dpi);
        y += S(24);

        y = DrawToggleSettingRow(x, y, w, headerH, "set_startup",
            Strings.SetStartupTitle, Strings.SetStartupSub,
            _startupService.IsStartupEnabled,
            () =>
            {
                bool next = !_startupService.IsStartupEnabled;
                bool ok = _startupService.SetBasicStartup(next, _settings.StartMinimized);
                ShowStatusBanner(
                    ok ? (next ? Strings.MsgStartupOn : Strings.MsgStartupOff) : Strings.MsgStartupFail, !ok);
            });
        y += S(8);

        y = DrawElevationTaskCard(x, y, w, headerH);
        y += S(8);

        y = DrawToggleSettingRow(x, y, w, headerH, "set_start_min",
            Strings.SetStartMinTitle, Strings.SetStartMinSub,
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

        y = DrawToggleSettingRow(x, y, w, headerH, "set_min_close",
            Strings.SetMinCloseTitle, Strings.SetMinCloseSub,
            _settings.MinimizeToTrayOnClose,
            () => _stateStore.UpdateSettings(s => s.MinimizeToTrayOnClose = !s.MinimizeToTrayOnClose));
        y += S(8);

        y = DrawToggleSettingRow(x, y, w, headerH, "set_topmost",
            Strings.SetTopmostTitle, Strings.SetTopmostSub,
            _settings.AlwaysOnTop,
            () => _stateStore.UpdateSettings(s => s.AlwaysOnTop = !s.AlwaysOnTop));
        y += S(8);

        y = DrawToggleSettingRow(x, y, w, headerH, "set_auto_update",
            Strings.SetAutoUpdateTitle, Strings.SetAutoUpdateSub,
            _settings.AutoCheckUpdates,
            () => _stateStore.UpdateSettings(s => s.AutoCheckUpdates = !s.AutoCheckUpdates));
        y += S(8);

        y = DrawUpdateCheckRow(x, y, w, headerH);

        // 3. TOOLS & CONFIG
        y += S(22);
        _d2d.DrawText(Strings.SecToolsShortcuts, x, y, x + w, y + S(20), 11, Win32.FW_BOLD, Theme.TextMuted, _dpi);
        y += S(24);

        y = DrawUtilityActionsRow(x, y, w, headerH);
        y += S(10);

        int pathH = S(42);
        _d2d.FillAndStrokeRoundedRect(x, y, w, pathH, SF(10), Theme.BgCard, 1f, Theme.BorderSubtle, 1f);
        _d2d.DrawText($"{Strings.StateFileLabel}: {_stateStore.FilePath}",
            x + S(16), y, x + w - S(16), y + pathH, 11, Win32.FW_NORMAL, Theme.TextMuted, _dpi);

        return y + pathH;
    }

    private int DrawThemeGalleryCard(int x, int y, int w, int headerH)
    {
        var presets = Theme.Presets;
        var customs = Theme.CustomPalettes;
        int customCount = Math.Min(customs.Count, 12);
        int totalItems = presets.Length + customCount;

        int pad = S(16);
        int cols = w >= S(640) ? 4 : (w >= S(440) ? 3 : 2);
        int colGap = S(12);
        int rowGap = S(12);
        int previewH = S(58);
        int labelH = S(22);
        int tileH = previewH + labelH;
        int rows = Math.Max(1, (totalItems + cols - 1) / cols);
        int gridTopOffset = S(58);
        int cardH = gridTopOffset + rows * tileH + Math.Max(0, rows - 1) * rowGap + S(16);

        _d2d.FillAndStrokeRoundedRect(x, y, w, cardH, SF(11), Theme.BgCard, 1f, Theme.BorderSubtle, 1f);

        var active = Theme.FindById(Theme.CurrentId) ?? presets[0];
        int customBtnW = Math.Clamp(_d2d.MeasureTextWidth(Strings.BtnCustomTheme, 12, Win32.FW_SEMIBOLD, _dpi) + S(24), S(92), S(168));
        int activePillW = Math.Clamp(_d2d.MeasureTextWidth(Strings.ThemeName(active.Id), 11, Win32.FW_SEMIBOLD, _dpi) + S(44), S(128), S(200));
        int ctrlH = S(30);
        int customBtnX = x + w - pad - customBtnW;
        int ctrlY = y + S(12);
        int activePillX = customBtnX - S(8) - activePillW;
        int textRight = activePillX - S(14);

        _d2d.DrawText(Strings.ThemeTitle, x + pad, y + S(10), textRight, y + S(30), 13, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi);
        _d2d.DrawText(Strings.ThemeSub, x + pad, y + S(30), textRight, y + S(48), 11, Win32.FW_NORMAL, Theme.TextSecondary, _dpi);
        _d2d.FillAndStrokeRoundedRect(activePillX, ctrlY, activePillW, ctrlH, SF(8), Theme.BgCardElevated, 1f, Theme.BorderSubtle, 1f);
        int pillSwSz = S(14);
        int pillSwX = activePillX + S(10);
        int pillSwY = ctrlY + (ctrlH - pillSwSz) / 2;
        _d2d.FillCircle(pillSwX, pillSwY, pillSwSz, active.SwatchBg);
        _d2d.StrokeCircle(pillSwX, pillSwY, pillSwSz, Theme.BorderStrong, MathF.Max(1.1f, SF(1.1f)));
        _d2d.FillCircle(pillSwX + (pillSwSz - S(6)) * 0.5f, pillSwY + (pillSwSz - S(6)) * 0.5f, S(6), active.SwatchAccent);
        _d2d.DrawText(Strings.ThemeName(active.Id), pillSwX + pillSwSz + S(8), ctrlY, activePillX + activePillW - S(10), ctrlY + ctrlH,
            11, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi);

        if (ctrlY + ctrlH > headerH)
        {
            DrawActionButton("btn_open_theme_studio", Strings.BtnCustomTheme,
                customBtnX, ctrlY, customBtnW, ctrlH,
                Theme.BgControl, Theme.BgControlHover, Theme.Accent, Theme.BorderStrong,
                () => OpenThemeStudio(null));
        }

        int gridW = w - pad * 2;
        int colW = (gridW - colGap * (cols - 1)) / cols;
        int gridY = y + gridTopOffset;

        for (int i = 0; i < totalItems; i++)
        {
            var pal = i < presets.Length ? presets[i] : customs[i - presets.Length];
            int row = i / cols;
            int col = i % cols;
            int tx = x + pad + col * (colW + colGap);
            int tw = col == cols - 1 ? (x + w - pad - tx) : colW;
            int ty = gridY + row * (tileH + rowGap);
            if (ty + tileH < _bodyClipTop || ty > _bodyClipBottom)
                continue;

            bool selected = string.Equals(Theme.CurrentId, pal.Id, StringComparison.OrdinalIgnoreCase);
            string hitId = $"theme_card_{pal.Id}";
            float hovT = GetHoverT(hitId);

            if (selected || hovT > 0.01f)
            {
                _d2d.FillRoundedRect(tx - S(1), ty - S(1), tw + S(2), previewH + S(2), SF(9),
                    Theme.Accent, selected ? 0.22f : hovT * 0.12f);
            }

            uint frameStroke = selected
                ? Theme.Accent
                : Theme.BlendRgb(pal.BorderStrong, Theme.Accent, hovT * 0.45f);
            _d2d.FillAndStrokeRoundedRect(tx, ty, tw, previewH, SF(8),
                pal.BgWindow, 1f, frameStroke, 1f, selected ? 1.8f : 1.0f);

            // Mini UI preview inside tile
            int sideX = tx + S(4);
            int sideY = ty + S(4);
            int sideW = Math.Max(S(24), (int)(tw * 0.24f));
            int sideH = previewH - S(8);
            _d2d.FillRoundedRect(sideX, sideY, sideW, sideH, SF(5), pal.BgHeader);

            float sBarW = Math.Max(S(10), sideW - S(8));
            _d2d.FillRoundedRect(sideX + S(4), sideY + S(5), sBarW, SF(3f), SF(1.5f), pal.Accent);
            _d2d.FillRoundedRect(sideX + S(4), sideY + S(12), sBarW * 0.80f, SF(2.5f), SF(1.25f), pal.TextMuted, 0.55f);
            _d2d.FillRoundedRect(sideX + S(4), sideY + S(18), sBarW * 0.65f, SF(2.5f), SF(1.25f), pal.TextMuted, 0.45f);

            int mainX = sideX + sideW + S(5);
            int mainW = Math.Max(S(30), tx + tw - S(5) - mainX);
            int miniCardH = S(28);
            _d2d.FillRoundedRect(mainX, sideY, mainW, miniCardH, SF(5), pal.BgCard);
            _d2d.FillRoundedRect(mainX + S(6), sideY + S(7), Math.Max(S(14), (mainW - S(12)) * 0.64f), SF(3f), SF(1.5f), pal.TextPrimary, 0.85f);
            _d2d.FillRoundedRect(mainX + S(6), sideY + S(15), Math.Max(S(10), (mainW - S(12)) * 0.44f), SF(2.5f), SF(1.25f), pal.TextSecondary, 0.55f);

            // Mini bottom playback / volume bar
            int barY = ty + previewH - S(15);
            _d2d.FillCircle(mainX + S(1), barY, S(9), pal.Accent);
            int trackX = mainX + S(14);
            int trackW = Math.Max(S(14), mainW - S(25));
            float trackY = barY + SF(2.5f);
            _d2d.FillRoundedRect(trackX, trackY, trackW, SF(4f), SF(2f), pal.BgControl);
            _d2d.FillRoundedRect(trackX, trackY, trackW * 0.58f, SF(4f), SF(2f), pal.Accent);
            _d2d.FillCircle(mainX + mainW - S(6), barY + S(2), S(5), pal.Green);

            if (selected)
            {
                int chkSz = S(16);
                float chkX = tx + tw - chkSz - S(4);
                float chkY = ty + S(4);
                _d2d.FillCircle(chkX, chkY, chkSz, pal.Accent);
                float cx = chkX + chkSz * 0.5f;
                float cy = chkY + chkSz * 0.5f;
                _d2d.DrawLine(cx - SF(3.2f), cy + SF(0.2f), cx - SF(0.8f), cy + SF(2.6f), pal.OnAccent, SF(1.6f));
                _d2d.DrawLine(cx - SF(0.8f), cy + SF(2.6f), cx + SF(3.4f), cy - SF(2.4f), pal.OnAccent, SF(1.6f));
            }

            int labelY = ty + previewH + S(2);
            int delW = pal.IsCustom ? S(20) : 0;
            int labelRight = tx + tw - delW;
            string labelText = pal.IsCustom ? (pal.CustomName ?? pal.Id) : Strings.ThemeName(pal.Id);
            _d2d.DrawText(labelText, tx + S(2), labelY, labelRight, ty + tileH,
                11, selected ? Win32.FW_SEMIBOLD : Win32.FW_MEDIUM,
                selected ? Theme.Accent : Theme.TextPrimary, _dpi);

            if (ty + tileH > headerH)
            {
                string targetThemeId = pal.Id;
                RegisterScrollableHit(
                    new Win32.RECT { Left = tx, Top = ty, Right = tx + tw - delW, Bottom = ty + tileH },
                    hitId,
                    () => _stateStore.UpdateSettings(s => s.Theme = targetThemeId));

                if (pal.IsCustom)
                {
                    int delSz = S(18);
                    int delX = tx + tw - delSz;
                    int delY = labelY + (labelH - delSz) / 2;
                    string delId = $"theme_del_{pal.Id}";
                    bool delHovered = string.Equals(_hoverId, delId, StringComparison.Ordinal);

                    if (delHovered)
                        _d2d.FillRoundedRect(delX, delY, delSz, delSz, SF(4), Theme.RedSoft);
                    _d2d.DrawText("×", delX, delY, delX + delSz, delY + delSz,
                        11, Win32.FW_BOLD, delHovered ? Theme.Red : Theme.TextMuted, _dpi, centered: true);

                    string removedName = pal.CustomName ?? pal.Id;
                    string removeId = pal.Id;
                    RegisterScrollableHit(
                        new Win32.RECT { Left = delX, Top = delY, Right = delX + delSz, Bottom = delY + delSz },
                        delId,
                        () =>
                        {
                            Theme.SetPreviewPalette(null);
                            _stateStore.DeleteCustomTheme(removeId);
                            ApplyDwmTheme();
                            ShowStatusBanner(Strings.MsgCustomThemeDeleted(removedName), isError: false);
                        });
                }
            }
        }

        return y + cardH;
    }

    private int DrawStatusBannerIfActive(int x, int y, int w)
    {
        if (string.IsNullOrEmpty(_statusBanner) || Environment.TickCount64 > _statusBannerUntilTick)
            return y;

        int bannerH = S(38);
        uint bFg = _statusBannerIsError ? Theme.Red : Theme.Green;
        uint bBg = _statusBannerIsError ? Theme.RedSoft : Theme.GreenSoft;
        _d2d.FillAndStrokeRoundedRect(x, y, w, bannerH, SF(8), bBg, 1f, bFg, 0.62f, 1f);
        _d2d.DrawText(_statusBanner, x + S(14), y, x + w - S(14), y + bannerH, 12, Win32.FW_SEMIBOLD, bFg, _dpi);
        return y + bannerH + S(14);
    }

    private int DrawIgnoredAppsSection(int x, int y, int w, int headerH)
    {
        var ignored = _settings.IgnoredApps;
        _d2d.DrawText($"{Strings.SecIgnoredApps} ({ignored.Count})", x, y, x + w, y + S(20), 11, Win32.FW_BOLD, Theme.TextMuted, _dpi);
        y += S(24);

        if (ignored.Count == 0)
        {
            int emptyH = S(46);
            _d2d.FillAndStrokeRoundedRect(x, y, w, emptyH, SF(10), Theme.BgCard, 1f, Theme.BorderSubtle, 1f);
            _d2d.DrawText(Strings.IgnoredAppsEmpty, x + S(16), y, x + w - S(16), y + emptyH,
                11, Win32.FW_NORMAL, Theme.TextSecondary, _dpi);
            return y + emptyH;
        }

        for (int i = 0; i < ignored.Count; i++)
        {
            var entry = ignored[i];
            int rowH = S(48);
            _d2d.FillAndStrokeRoundedRect(x, y, w, rowH, SF(10), Theme.BgCard, 1f, Theme.BorderSubtle, 1f);

            int pad = S(14);
            int icoSz = S(22);
            DrawAppIconOrMonogram(entry.AppKey, entry.ExePath, entry.DisplayName,
                x + pad, y + (rowH - icoSz) * 0.5f, icoSz, SF(6));

            int btnW = Math.Clamp(_d2d.MeasureTextWidth(Strings.BtnRestoreIgnored, 11, Win32.FW_SEMIBOLD, _dpi) + S(24), S(86), S(132));
            int btnH = S(28);
            int btnX = x + w - pad - btnW;
            int btnY = y + (rowH - btnH) / 2;

            int textX = x + pad + icoSz + S(12);
            int textRight = btnX - S(12);
            bool isWeb = IsBrowserOrigin(entry.AppKey);
            int globeSz = isWeb ? S(11) : 0;
            int globeGap = isWeb ? S(5) : 0;
            int titleW = _d2d.MeasureTextWidth(entry.DisplayName, 12, Win32.FW_SEMIBOLD, _dpi) + S(4);
            int nameMaxRight = Math.Clamp(textX + titleW, textX, Math.Max(textX, textRight - globeSz - globeGap));

            _d2d.DrawText(entry.DisplayName, textX, y, textRight - globeSz - globeGap, y + rowH,
                12, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi);
            if (isWeb && nameMaxRight + globeSz + S(2) <= textRight)
                _d2d.DrawGlobeGlyph(nameMaxRight + S(3), y + (rowH - globeSz) * 0.5f, globeSz, Theme.TextMuted, _dpi);

            if (y + rowH > headerH)
            {
                var captured = entry;
                DrawActionButton($"ign_restore_{entry.AppKey}", Strings.BtnRestoreIgnored,
                    btnX, btnY, btnW, btnH,
                    Theme.BgControl, Theme.BgControlHover, Theme.TextPrimary, Theme.BorderStrong,
                    () =>
                    {
                        if (_stateStore.UnignoreApp(captured.AppKey))
                        {
                            _ = _sessionManager.RefreshAsync();
                            ShowStatusBanner(Strings.MsgAppRestored(captured.DisplayName));
                        }
                    });
            }

            y += rowH;
            if (i < ignored.Count - 1)
                y += S(6);
        }

        return y;
    }

    private int DrawHotkeySettingRow(
        int x, int y, int w, int headerH,
        string targetId, string title, string subtitle, string currentHotkey, Action<string> onCommit,
        bool allowAnySingleKey = false)
    {
        int rowH = S(62);
        _d2d.FillAndStrokeRoundedRect(x, y, w, rowH, SF(10), Theme.BgCard, 1f, Theme.BorderSubtle, 1f);

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
        int pillW = Math.Clamp(_d2d.MeasureTextWidth(pillText, 11, Win32.FW_SEMIBOLD, _dpi) + S(28), S(142), S(200));

        int clrX = x + w - pad - clrSz;
        int pillX = (hasValue && !isCapturingThis)
            ? (clrX - S(6) - pillW)
            : (x + w - pad - pillW);

        int textRight = pillX - S(16);
        _d2d.DrawText(title, x + pad, y + S(10), textRight, y + S(31), 13, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi);
        _d2d.DrawText(subtitle, x + pad, y + S(32), textRight, y + S(52), 11, Win32.FW_NORMAL, Theme.TextSecondary, _dpi);

        if (y + rowH > headerH)
        {
            DrawHotkeyPillButton(hitId, pillText, pillX, btnY, pillW, btnH,
                isCapturing: isCapturingThis,
                hasValue: hasValue,
                () => StartHotkeyCapture(targetId, hitId, onCommit, allowAnySingleKey: allowAnySingleKey));

            if (hasValue && !isCapturingThis)
            {
                DrawIconButton(clrHitId, SmallButtonGlyph.Close, clrX, y + (rowH - clrSz) / 2, clrSz, true,
                    () => onCommit(string.Empty), danger: true);
            }
        }

        return y + rowH;
    }

    private void DrawHotkeyPillButton(
        string id, string text,
        int x, int y, int w, int h, bool isCapturing, bool hasValue, Action onClick, int hitY = -1)
    {
        bool hovered = string.Equals(_hoverId, id, StringComparison.Ordinal);
        uint bg = isCapturing
            ? Theme.AccentSoft
            : (hasValue ? (hovered ? Theme.BgControlHover : Theme.AccentSoft) : (hovered ? Theme.BgControlHover : Theme.BgControl));
        uint border = isCapturing || hasValue ? Theme.Accent : Theme.BorderStrong;
        uint fg = isCapturing || hasValue ? Theme.Accent : (hovered ? Theme.TextPrimary : Theme.TextSecondary);

        _d2d.FillAndStrokeRoundedRect(x, y, w, h, SF(7), bg, 1f, border,
            isCapturing || hasValue ? 0.86f : 1f, isCapturing ? 1.4f : 1f);
        _d2d.DrawText(text, x + S(6), y, x + w - S(6), y + h, 11, Win32.FW_SEMIBOLD, fg, _dpi, centered: true);
        int regY = hitY >= 0 ? hitY : y;
        RegisterScrollableHit(new Win32.RECT { Left = x, Top = regY, Right = x + w, Bottom = regY + h }, id, onClick);
    }

    private int DrawUpdateCheckRow(int x, int y, int w, int headerH)
    {
        int rowH = S(62);
        _d2d.FillAndStrokeRoundedRect(x, y, w, rowH, SF(10), Theme.BgCard, 1f, Theme.BorderSubtle, 1f);

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
        int btnW = Math.Clamp(_d2d.MeasureTextWidth(btnLabel, 12, Win32.FW_SEMIBOLD, _dpi) + S(28), S(124), S(196));
        int btnX = x + w - pad - btnW;
        int whatsNewW = Math.Clamp(_d2d.MeasureTextWidth(Strings.BtnWhatsNew, 11, Win32.FW_SEMIBOLD, _dpi) + S(22), S(86), S(132));
        int whatsNewX = btnX - S(8) - whatsNewW;
        int rightLimit = whatsNewX - S(14);

        string titleText = Strings.SetUpdateCheckTitle;
        int titleW = _d2d.MeasureTextWidth(titleText, 13, Win32.FW_SEMIBOLD, _dpi) + S(8);
        int titleRight = Math.Min(rightLimit, x + pad + titleW);
        _d2d.DrawText(titleText, x + pad, y + S(10), titleRight, y + S(31), 13, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi);

        string verBadge = $"v{snap.CurrentVersion}";
        int bx = titleRight + S(6);
        int verPillW = _d2d.MeasureTextWidth(verBadge, 10, Win32.FW_SEMIBOLD, _dpi) + S(18);
        if (bx + verPillW <= rightLimit)
            DrawPillBadge(bx, y + S(10), verBadge, Theme.Accent, Theme.AccentSoft);

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
            UpdatePhase.Failed => snap.ResolveErrorMessage() ?? Strings.SetUpdateSubFailed,
            _ => Strings.SetUpdateSubCurrent(snap.CurrentVersion)
        };
        uint subFg = snap.Phase switch
        {
            UpdatePhase.Available => Theme.Accent,
            UpdatePhase.ReadyToInstall or UpdatePhase.UpToDate => Theme.Green,
            UpdatePhase.Failed => Theme.Red,
            _ => Theme.TextSecondary
        };
        _d2d.DrawText(subtitle, x + pad, y + S(32), rightLimit, y + S(52), 11, Win32.FW_NORMAL, subFg, _dpi);

        if (y + rowH > headerH)
        {
            DrawActionButton("set_upd_whatsnew", Strings.BtnWhatsNew, whatsNewX, btnY, whatsNewW, btnH,
                Theme.BgControl, Theme.BgControlHover, Theme.Accent, Theme.BorderStrong,
                OpenWhatsNew);

            DrawActionButton("set_upd_action", btnLabel, btnX, btnY, btnW, btnH,
                btnBg, btnBgHover, btnFg, btnBorder, onBtnClick);
        }

        return y + rowH;
    }

    private int DrawLanguageDropdownRow(int x, int y, int w, int headerH)
    {
        int rowH = S(62);
        _d2d.FillAndStrokeRoundedRect(x, y, w, rowH, SF(10), Theme.BgCard, 1f, Theme.BorderSubtle, 1f);

        int pad = S(16);
        int comboW = S(210);
        int comboH = S(34);
        int comboX = x + w - pad - comboW;
        int comboY = y + (rowH - comboH) / 2;

        int textRight = comboX - S(16);
        _d2d.DrawText(Strings.LanguageTitle, x + pad, y + S(10), textRight, y + S(31), 13, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi);
        _d2d.DrawText(Strings.LanguageSub, x + pad, y + S(32), textRight, y + S(52), 11, Win32.FW_NORMAL, Theme.TextSecondary, _dpi);

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
        float hovT = isOpen ? 1f : GetHoverT(triggerId);

        _d2d.FillAndStrokeRoundedRect(comboX, comboY, comboW, comboH, SF(7),
            Theme.BlendRgb(Theme.BgControl, Theme.BgControlHover, hovT), 1f,
            isOpen ? Theme.Accent : Theme.BorderStrong, 1f);

        _d2d.DrawText(active.NativeName, comboX + S(12), comboY, comboX + comboW - S(28), comboY + comboH,
            12, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi);
        _d2d.DrawDropdownChevron(comboX + comboW - S(16), comboY + comboH * 0.5f, isOpen,
            isOpen ? Theme.Accent : Theme.TextSecondary, _dpi);

        if (isOpen)
            _openDropdownAnchor = new Win32.RECT { Left = comboX, Top = comboY, Right = comboX + comboW, Bottom = comboY + comboH };

        if (y + rowH > headerH)
        {
            RegisterScrollableHit(
                new Win32.RECT { Left = comboX, Top = comboY, Right = comboX + comboW, Bottom = comboY + comboH },
                triggerId,
                () => ToggleDropdown(DropdownKind.Language));
        }

        return y + rowH;
    }

    private void OnThemePreviewUpdated()
    {
        if (_disposed || _hwnd == 0)
            return;
        ApplyDwmTheme();
        _guideWindow?.RefreshLocalizationAndTheme();
        RequestRepaint();
    }

    private void OpenThemeStudio(CustomThemePreset? editPreset = null)
    {
        CloseOpenDropdown();
        _themeEditorWindow ??= new ThemeEditorWindow(
            _hwnd,
            _stateStore,
            OnThemePreviewUpdated,
            (msg, err) => ShowStatusBanner(msg, err));

        if (editPreset != null)
            _themeEditorWindow.ShowEdit(editPreset);
        else
            _themeEditorWindow.ShowCreate();
    }

    private void DrawOpenDropdownOverlay(int w, int h, int headerH, double nowMs)
    {
        if (_openDropdown == DropdownKind.SessionOutputDevice &&
            _openDropdownAnchor.Bottom <= headerH)
        {
            // Anchor from floating dock is below body; allow it if anchor Top >= headerH
            if (_openDropdownAnchor.Top < headerH)
            {
                CloseOpenDropdown();
                return;
            }
        }
        else if (_openDropdownAnchor.Bottom <= headerH)
        {
            CloseOpenDropdown();
            return;
        }

        string? targetSessionId = null;
        string targetSessionName = string.Empty;
        if (_openDropdown == DropdownKind.SessionOutputDevice)
        {
            targetSessionId = _openDropdownSessionId;
            targetSessionName = _openDropdownSessionName ?? string.Empty;
            bool sessionExists = targetSessionId != null &&
                                 _snapshot.Sessions.Any(s => string.Equals(s.SessionId, targetSessionId, StringComparison.Ordinal));
            if (!sessionExists || _openDropdownOutputDevices.Count == 0)
            {
                CloseOpenDropdown();
                return;
            }
        }

        _hitRegions.Add(new HitRegion(
            new Win32.RECT { Left = 0, Top = 0, Right = w, Bottom = h },
            "dropdown_backdrop",
            CloseOpenDropdown,
            IsButton: false));

        int itemH = _openDropdown switch
        {
            DropdownKind.TaskInstall => S(58),
            DropdownKind.SessionOutputDevice => S(34),
            _ => S(32)
        };
        int padV = S(6);
        int sessHdrH = S(26);
        int sessMaxDevs = _openDropdown == DropdownKind.SessionOutputDevice
            ? Math.Min(_openDropdownOutputDevices.Count, 8)
            : 0;
        bool showBrowserNotice = _openDropdown == DropdownKind.SessionOutputDevice &&
                                 !string.IsNullOrEmpty(_openDropdownBrowserName) &&
                                 !_settings.SuppressBrowserOutputNotice;
        int sessNoticeGap = showBrowserNotice ? S(6) : 0;
        int sessNoticeH = showBrowserNotice ? S(62) : 0;

        int menuW;
        if (_openDropdown == DropdownKind.SessionOutputDevice)
        {
            string headerLabel = Strings.FlyoutAppAudioTitle(targetSessionName);
            int maxTextW = Math.Max(
                _d2d.MeasureTextWidth(headerLabel, 10, Win32.FW_BOLD, _dpi),
                _d2d.MeasureTextWidth(Strings.AudioDeviceSystemDefault, 12, Win32.FW_SEMIBOLD, _dpi));
            for (int i = 0; i < sessMaxDevs; i++)
                maxTextW = Math.Max(maxTextW, _d2d.MeasureTextWidth(_openDropdownOutputDevices[i].Name, 12, Win32.FW_SEMIBOLD, _dpi));

            int minW = showBrowserNotice ? S(304) : S(260);
            int maxAllowedW = Math.Max(minW, Math.Min(S(380), w - S(32)));
            menuW = Math.Clamp(maxTextW + S(56), minW, maxAllowedW);
        }
        else
        {
            menuW = _openDropdown switch
            {
                DropdownKind.TaskInstall => Math.Max(S(392), _openDropdownAnchor.Right - _openDropdownAnchor.Left),
                DropdownKind.Language => S(340),
                _ => Math.Max(S(216), _openDropdownAnchor.Right - _openDropdownAnchor.Left)
            };
        }

        int menuX = Math.Clamp(_openDropdownAnchor.Right - menuW, S(12), Math.Max(S(12), w - menuW - S(12)));

        int menuH;
        if (_openDropdown == DropdownKind.Language)
            menuH = ((Strings.Languages.Length + 1) / 2) * itemH + padV * 2;
        else if (_openDropdown == DropdownKind.SessionOutputDevice)
            menuH = padV * 2 + sessHdrH + S(5) + itemH + S(7) + sessMaxDevs * itemH + sessNoticeGap + sessNoticeH;
        else
            menuH = 2 * itemH + padV * 2;

        int menuY = _openDropdownAnchor.Bottom + S(6);
        if (menuY + menuH > h - S(12))
            menuY = _openDropdownAnchor.Top - S(6) - menuH;
        menuY = Math.Clamp(menuY, headerH + S(6), Math.Max(headerH + S(6), h - S(12) - menuH));

        float popEase = (!_settings.ReducedAnimations && _dropdownOpenMs > 0)
            ? EaseOutCubic(_dropdownOpenMs, nowMs, UI_POPOVER_DURATION_MS)
            : 1f;
        bool didPopTransform = popEase < 0.999f;
        if (didPopTransform)
        {
            float scale = 0.94f + 0.06f * popEase;
            float dy = -S(8) * (1f - popEase);
            _d2d.PushTransform(Direct2D.D2D1_MATRIX_3X2_F.TranslateAndScaleAt(0f, dy, scale, scale, menuX + menuW * 0.5f, menuY));
        }

        _d2d.FillRoundedRect(menuX - S(2), menuY + S(4), menuW + S(4), menuH + S(6), SF(12), 0x000000, 0.22f * popEase);
        _d2d.FillRoundedRect(menuX, menuY + S(2), menuW, menuH + S(2), SF(10), 0x000000, 0.12f * popEase);

        _d2d.FillAndStrokeRoundedRect(menuX, menuY, menuW, menuH, SF(11),
            Theme.BgCardElevated, 0.98f * popEase, Theme.BorderStrong, popEase);

        _hitRegions.Add(new HitRegion(
            new Win32.RECT { Left = menuX, Top = menuY, Right = menuX + menuW, Bottom = menuY + menuH },
            "dropdown_panel",
            () => { },
            IsButton: false));

        int itemPadX = S(6);
        int ix = menuX + itemPadX;
        int iw = menuW - itemPadX * 2;

        if (_openDropdown == DropdownKind.SessionOutputDevice && targetSessionId != null)
        {
            int curY = menuY + padV;
            string hdrText = Strings.FlyoutAppAudioTitle(targetSessionName);
            _d2d.DrawText(hdrText, ix + S(10), curY, ix + iw - S(10), curY + sessHdrH,
                10, Win32.FW_BOLD, Theme.TextMuted, _dpi);
            curY += sessHdrH;

            _d2d.FillRect(ix + S(4), curY + S(2), iw - S(8), 1, Theme.BorderSubtle);
            curY += S(5);

            string? assignedId = _engine.GetSessionOutputDeviceId(targetSessionId);

            void DrawOutputOptionRow(string rowId, string label, bool selected, string? deviceId, uint rowColor)
            {
                bool hovered = string.Equals(_hoverId, rowId, StringComparison.Ordinal);
                if (selected || hovered)
                {
                    _d2d.FillRoundedRect(ix, curY + S(1), iw, itemH - S(2), SF(7),
                        selected ? rowColor : Theme.BgControlHover,
                        selected ? (Theme.IsLight ? 0.12f : 0.16f) : 1f);
                }

                int dotSz = S(7);
                int dotX = ix + S(11);
                float dotY = curY + (itemH - dotSz) * 0.5f;
                if (selected)
                    _d2d.FillCircle(dotX, dotY, dotSz, rowColor);
                else
                    _d2d.StrokeCircle(dotX, dotY, dotSz, rowColor, MathF.Max(1.1f, SF(1.15f)));

                _d2d.DrawText(label, dotX + dotSz + S(9), curY, ix + iw - S(10), curY + itemH,
                    12, selected ? Win32.FW_SEMIBOLD : Win32.FW_MEDIUM, selected ? rowColor : Theme.TextPrimary, _dpi);

                string capturedSessId = targetSessionId;
                _hitRegions.Add(new HitRegion(
                    new Win32.RECT { Left = ix, Top = curY, Right = ix + iw, Bottom = curY + itemH },
                    rowId,
                    () =>
                    {
                        CloseOpenDropdown();
                        _engine.SetSessionOutputDevice(capturedSessId, deviceId, label);
                    }));

                curY += itemH;
            }

            DrawOutputOptionRow("dd_sess_out_default", Strings.AudioDeviceSystemDefault, assignedId == null, null, GetEffectiveDeviceColor(null));

            _d2d.FillRect(ix + S(4), curY + S(3), iw - S(8), 1, Theme.BorderSubtle);
            curY += S(7);

            for (int i = 0; i < sessMaxDevs; i++)
            {
                var dev = _openDropdownOutputDevices[i];
                bool selected = assignedId != null && string.Equals(assignedId, dev.Id, StringComparison.OrdinalIgnoreCase);
                DrawOutputOptionRow($"dd_sess_out_{i}", dev.Name, selected, dev.Id, GetEffectiveDeviceColor(dev.Id));
            }

            if (showBrowserNotice && _openDropdownBrowserName != null)
            {
                int noticeY = curY + sessNoticeGap;
                _d2d.FillAndStrokeRoundedRect(ix, noticeY, iw, sessNoticeH, SF(7),
                    Theme.AmberSoft, 1f, Theme.Amber, 0.48f);

                string noticeText = Strings.BrowserOutputScopeNotice(_openDropdownBrowserName);
                _d2d.DrawWrappedText(noticeText, ix + S(10), noticeY + S(6), ix + iw - S(10), noticeY + S(38),
                    10, Win32.FW_MEDIUM, Theme.TextPrimary, _dpi);

                const string dismissId = "dd_sess_out_dismiss_notice";
                string dismissText = Strings.BtnDontShowAgain;
                int dismissW = Math.Clamp(_d2d.MeasureTextWidth(dismissText, 10, Win32.FW_SEMIBOLD, _dpi) + S(16), S(92), iw - S(16));
                int dismissH = S(20);
                int dismissX = ix + iw - S(8) - dismissW;
                int dismissY = noticeY + sessNoticeH - S(5) - dismissH;
                bool dismissHover = string.Equals(_hoverId, dismissId, StringComparison.Ordinal);

                _d2d.FillRoundedRect(dismissX, dismissY, dismissW, dismissH, SF(5),
                    Theme.Amber, dismissHover ? 0.22f : 0.11f);
                _d2d.DrawText(dismissText, dismissX, dismissY, dismissX + dismissW, dismissY + dismissH,
                    10, Win32.FW_SEMIBOLD, Theme.Amber, _dpi, centered: true);

                _hitRegions.Add(new HitRegion(
                    new Win32.RECT { Left = dismissX, Top = dismissY, Right = dismissX + dismissW, Bottom = dismissY + dismissH },
                    dismissId,
                    () => _stateStore.UpdateSettings(s => s.SuppressBrowserOutputNotice = true)));
            }
        }
        else if (_openDropdown == DropdownKind.Language)
        {
            var langs = Strings.Languages;
            int colGap = S(6);
            int colW = (iw - colGap) / 2;
            for (int i = 0; i < langs.Length; i++)
            {
                var lang = langs[i];
                int row = i / 2;
                int col = i % 2;
                int cx = ix + col * (colW + colGap);
                int iy = menuY + padV + row * itemH;
                bool selected = Strings.Current == lang.Language;
                string itemId = $"dd_item_lang_{lang.Code}";
                bool hovered = string.Equals(_hoverId, itemId, StringComparison.Ordinal);

                if (selected || hovered)
                {
                    _d2d.FillRoundedRect(cx, iy + S(1), colW, itemH - S(2), SF(7),
                        selected ? Theme.AccentSoft : Theme.BgControlHover);
                }

                _d2d.DrawText(lang.NativeName, cx + S(10), iy, cx + colW - S(20), iy + itemH,
                    12, selected ? Win32.FW_SEMIBOLD : Win32.FW_MEDIUM, selected ? Theme.Accent : Theme.TextPrimary, _dpi);

                if (selected)
                {
                    int dotSz = S(6);
                    _d2d.FillCircle(cx + colW - S(13), iy + (itemH - dotSz) * 0.5f, dotSz, Theme.Accent);
                }

                string code = lang.Code;
                _hitRegions.Add(new HitRegion(
                    new Win32.RECT { Left = cx, Top = iy, Right = cx + colW, Bottom = iy + itemH },
                    itemId,
                    () =>
                    {
                        CloseOpenDropdown();
                        _stateStore.UpdateSettings(s => s.Language = code);
                    }));
            }
        }
        else if (_openDropdown == DropdownKind.TaskInstall)
        {
            DrawTaskInstallMenuItem(
                ix, menuY + padV, iw, itemH,
                id: "dd_task_elevated",
                dotColor: Theme.Green,
                title: Strings.TaskOptElevatedTitle,
                subtitle: Strings.TaskOptElevatedSub,
                onClick: () =>
                {
                    CloseOpenDropdown();
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
                ix, menuY + padV + itemH, iw, itemH,
                id: "dd_task_normal",
                dotColor: Theme.Accent,
                title: Strings.TaskOptNormalTitle,
                subtitle: Strings.TaskOptNormalSub,
                onClick: () =>
                {
                    CloseOpenDropdown();
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

        if (didPopTransform)
            _d2d.PopTransform();
    }

    private void DrawTaskInstallMenuItem(
        int ix, int iy, int iw, int itemH,
        string id, uint dotColor, string title, string subtitle, Action onClick)
    {
        bool hovered = string.Equals(_hoverId, id, StringComparison.Ordinal);
        if (hovered)
            _d2d.FillRoundedRect(ix, iy + S(1), iw, itemH - S(2), SF(7), Theme.BgControlHover);

        _d2d.FillCircle(ix + S(10), iy + S(16), S(8), dotColor);
        _d2d.DrawText(title, ix + S(26), iy + S(7), ix + iw - S(10), iy + S(28),
            12, Win32.FW_SEMIBOLD, hovered ? Theme.Accent : Theme.TextPrimary, _dpi);
        _d2d.DrawText(subtitle, ix + S(26), iy + S(29), ix + iw - S(10), iy + S(49),
            10, Win32.FW_NORMAL, Theme.TextSecondary, _dpi);

        _hitRegions.Add(new HitRegion(
            new Win32.RECT { Left = ix, Top = iy, Right = ix + iw, Bottom = iy + itemH },
            id,
            onClick));
    }

    private int DrawElevationTaskCard(int x, int y, int w, int headerH)
    {
        int rowH = S(74);
        _d2d.FillAndStrokeRoundedRect(x, y, w, rowH, SF(10), Theme.BgCard, 1f, Theme.BorderSubtle, 1f);

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
                DrawActionButton("set_restart_admin", Strings.BtnRestartAdmin, elevBtnX, btnY, elevBtnW, btnH,
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
            _openDropdownAnchor = new Win32.RECT { Left = taskBtnX, Top = btnY, Right = taskBtnX + taskBtnW, Bottom = btnY + btnH };

        if (y + rowH > headerH)
        {
            bool taskHovered = string.Equals(_hoverId, "set_sched_task", StringComparison.Ordinal);
            if (hasTask || _taskOpBusy)
            {
                DrawActionButton("set_sched_task",
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
                _d2d.FillAndStrokeRoundedRect(taskBtnX, btnY, taskBtnW, btnH, SF(7), btnBg, 1f, Theme.Accent, 1f);
                _d2d.DrawText(Strings.BtnTaskInstall, taskBtnX + S(10), btnY, taskBtnX + taskBtnW - S(22), btnY + btnH,
                    12, Win32.FW_SEMIBOLD, btnFg, _dpi, centered: true);
                _d2d.DrawDropdownChevron(taskBtnX + taskBtnW - S(15), btnY + btnH * 0.5f, taskOpen, btnFg, _dpi);
                RegisterScrollableHit(
                    new Win32.RECT { Left = taskBtnX, Top = btnY, Right = taskBtnX + taskBtnW, Bottom = btnY + btnH },
                    "set_sched_task",
                    () =>
                    {
                        CancelHotkeyCapture();
                        ToggleDropdown(DropdownKind.TaskInstall);
                    });
            }
        }

        string titleText = Strings.ElevCardTitle;
        int titleW = _d2d.MeasureTextWidth(titleText, 13, Win32.FW_SEMIBOLD, _dpi) + S(8);
        int titleRight = Math.Min(rightLimit, x + pad + titleW);
        _d2d.DrawText(titleText, x + pad, y + S(12), titleRight, y + S(34), 13, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi);

        int bx = titleRight + S(6);
        string adminText = isElev ? Strings.BadgeAdmin : Strings.BadgeStandard;
        int adminPillW = _d2d.MeasureTextWidth(adminText, 10, Win32.FW_SEMIBOLD, _dpi) + S(20);
        if (bx + adminPillW <= rightLimit)
        {
            bx += DrawPillBadge(bx, y + S(12),
                adminText,
                isElev ? Theme.Green : Theme.Amber,
                isElev ? Theme.GreenSoft : Theme.AmberSoft) + S(6);
        }

        string taskText = hasTask
            ? (isTaskElev ? Strings.BadgeTaskInstalledElevated : Strings.BadgeTaskInstalledNormal)
            : Strings.BadgeTaskMissing;
        uint taskPillFg = hasTask ? (isTaskElev ? Theme.Green : Theme.Accent) : Theme.TextSecondary;
        uint taskPillBg = hasTask ? (isTaskElev ? Theme.GreenSoft : Theme.AccentSoft) : Theme.BgControl;
        int taskPillW = _d2d.MeasureTextWidth(taskText, 10, Win32.FW_SEMIBOLD, _dpi) + S(20);
        if (bx + taskPillW <= rightLimit)
            DrawPillBadge(bx, y + S(12), taskText, taskPillFg, taskPillBg);

        _d2d.DrawText(Strings.ElevCardSub, x + pad, y + S(38), rightLimit, y + S(60), 11, Win32.FW_NORMAL, Theme.TextSecondary, _dpi);

        return y + rowH;
    }

    private int DrawBrowserBridgeCard(int x, int y, int w, int headerH)
    {
        int rowH = S(104);
        _d2d.FillAndStrokeRoundedRect(x, y, w, rowH, SF(10), Theme.BgCard, 1f, Theme.BorderSubtle, 1f);

        float bridgeGlow = GetBridgePulseIntensity(_highlightBridgeStartMs, NowMs());
        if (bridgeGlow > 0.01f)
            _d2d.DrawCardGlow(x, y, w, rowH, SF(10), Theme.Accent, MathF.Min(1f, bridgeGlow * 1.25f), Theme.IsLight, _dpi);

        int pad = S(16);
        var (browsers, tabs) = _bridgeServer.GetStatusSnapshot();
        bool isConnected = browsers > 0;

        string statusText = isConnected ? Strings.BadgeBridgeConnected(browsers, tabs) : Strings.BadgeBridgeDisconnected;
        int pillW = Math.Max(S(54), _d2d.MeasureTextWidth(statusText, 10, Win32.FW_SEMIBOLD, _dpi) + S(20));
        int titleW = _d2d.MeasureTextWidth(Strings.BridgeCardTitle, 13, Win32.FW_SEMIBOLD, _dpi) + S(8);
        int maxTitleRight = x + w - pad - pillW - S(8);
        int titleRight = Math.Min(Math.Max(x + pad + S(100), maxTitleRight), x + pad + titleW);
        _d2d.DrawText(Strings.BridgeCardTitle, x + pad, y + S(12), titleRight, y + S(34), 13, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi);

        int pillX = titleRight + S(6);
        if (pillX + pillW <= x + w - pad)
        {
            DrawPillBadge(pillX, y + S(12), statusText,
                isConnected ? Theme.Green : Theme.TextSecondary,
                isConnected ? Theme.GreenSoft : Theme.BgControl);
        }

        _d2d.DrawText(Strings.BridgeCardSub, x + pad, y + S(36), x + w - pad, y + S(56), 11, Win32.FW_NORMAL, Theme.TextSecondary, _dpi);

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
            var connectedBrowsers = _bridgeServer.GetConnectedBrowsersSnapshot();
            bool edgeConnected = connectedBrowsers.Contains("msedge") || connectedBrowsers.Contains("edge");
            bool chromiumConnected = connectedBrowsers.Contains("chrome") ||
                                     connectedBrowsers.Contains("brave") ||
                                     connectedBrowsers.Contains("opera") ||
                                     connectedBrowsers.Contains("vivaldi") ||
                                     connectedBrowsers.Contains("chromium");
            bool firefoxConnected = connectedBrowsers.Contains("firefox") ||
                                    connectedBrowsers.Contains("zen") ||
                                    connectedBrowsers.Contains("librewolf") ||
                                    connectedBrowsers.Contains("waterfox") ||
                                    connectedBrowsers.Contains("floorp");

            void DrawBridgeBrowserButton(string id, string label, int bx, int bw, bool isConnectedBrowser, Action onClick)
            {
                bool hovered = string.Equals(_hoverId, id, StringComparison.Ordinal);
                DrawActionButton(id, label, bx, btnY, bw, btnH,
                    isConnectedBrowser ? Theme.GreenSoft : Theme.BgControl,
                    isConnectedBrowser ? Theme.Green : Theme.BgControlHover,
                    isConnectedBrowser && hovered ? Theme.OnAccent : (isConnectedBrowser ? Theme.Green : Theme.TextPrimary),
                    isConnectedBrowser ? Theme.Green : Theme.BorderStrong,
                    onClick);
            }

            DrawBridgeBrowserButton("btn_ext_edge", Strings.BtnInstallEdge, x + pad, mainBtnW,
                edgeConnected,
                () =>
                {
                    var (ok, msg) = ExtensionSetupService.LaunchEdgeStoreInstall();
                    ShowStatusBanner(msg, isError: !ok);
                });

            DrawBridgeBrowserButton("btn_ext_chromium", Strings.BtnInstallChromium, x + pad + mainBtnW + gap, mainBtnW,
                chromiumConnected,
                () =>
                {
                    _guideWindow ??= new ExtensionGuideWindow(_hwnd, _bridgeServer);
                    _guideWindow.ShowFor(ExtensionGuideMode.Chromium);
                });

            DrawBridgeBrowserButton("btn_ext_firefox", Strings.BtnInstallFirefox, x + pad + (mainBtnW + gap) * 2, mainBtnW,
                firefoxConnected,
                () =>
                {
                    _guideWindow ??= new ExtensionGuideWindow(_hwnd, _bridgeServer);
                    _guideWindow.ShowFor(ExtensionGuideMode.Firefox);
                });

            DrawActionButton("btn_ext_folder", Strings.BtnOpenExtFolder,
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

    private int DrawUtilityActionsRow(int x, int y, int w, int headerH)
    {
        int rowH = S(56);
        _d2d.FillAndStrokeRoundedRect(x, y, w, rowH, SF(10), Theme.BgCard, 1f, Theme.BorderSubtle, 1f);

        int pad = S(14);
        int gap = S(10);
        int btnW = (w - pad * 2 - gap * 2) / 3;
        int btnH = S(34);
        int btnY = y + (rowH - btnH) / 2;

        if (y + rowH > headerH)
        {
            DrawActionButton("util_rehook", Strings.BtnRehook, x + pad, btnY, btnW, btnH,
                Theme.BgControl, Theme.BgControlHover, Theme.TextPrimary, Theme.BorderStrong,
                () =>
                {
                    _refreshHooksCallback?.Invoke();
                    ShowStatusBanner(Strings.MsgHooksRefreshed);
                });

            DrawActionButton("util_open_cfg", Strings.BtnOpenConfigFolder, x + pad + btnW + gap, btnY, btnW, btnH,
                Theme.BgControl, Theme.BgControlHover, Theme.TextPrimary, Theme.BorderStrong,
                () => StartupService.OpenConfigFolder(_stateStore.FilePath));

            DrawActionButton("util_reset_set", Strings.BtnResetDefaults, x + pad + (btnW + gap) * 2, btnY, btnW, btnH,
                Theme.BgControl, Theme.RedSoft, Theme.Red, Theme.BorderStrong,
                () =>
                {
                    _stateStore.ResetSettingsToDefaults();
                    ShowStatusBanner(Strings.MsgSettingsReset);
                });
        }

        return y + rowH;
    }

    private void DrawToggleSwitch(int swX, int swY, int swW, int swH, string id, bool value)
    {
        float p = GetToggleAnim(id, value);
        float hovT = GetHoverT(id);
        uint trackColor = Theme.BlendRgb(Theme.SwitchOffTrack, Theme.Accent, p);
        _d2d.FillRoundedRect(swX, swY, swW, swH, swH * 0.5f, trackColor);
        if (hovT > 0.01f)
        {
            _d2d.DrawRoundedRect(swX - SF(1.5f), swY - SF(1.5f), swW + SF(3f), swH + SF(3f),
                (swH + SF(3f)) * 0.5f,
                Theme.BlendRgb(Theme.BorderStrong, Theme.AccentHover, p),
                MathF.Max(1.3f, SF(1.4f)), hovT);
        }

        float knobD = swH - S(6);
        float stretch = S(4) * MathF.Sin(p * MathF.PI);
        float knobW = knobD + stretch;
        float travel = (swW - knobD - S(6));
        float knobX = swX + S(3) + travel * p - stretch * p;
        float knobY = swY + S(3);
        _d2d.FillRoundedRect(knobX, knobY, knobW, knobD, knobD * 0.5f, Theme.SwitchKnob);
    }

    private int DrawToggleSettingRow(
        int x, int y, int w, int headerH, string id,
        string title, string subtitle, bool value, Action onToggle)
    {
        int rowH = S(62);
        _d2d.FillAndStrokeRoundedRect(x, y, w, rowH, SF(10), Theme.BgCard, 1f, Theme.BorderSubtle, 1f);

        int pad = S(16);
        int swW = S(46);
        int swH = S(24);
        int swX = x + w - pad - swW;
        int swY = y + (rowH - swH) / 2;

        int textRight = swX - S(16);
        _d2d.DrawText(title, x + pad, y + S(10), textRight, y + S(31), 13, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi);
        _d2d.DrawText(subtitle, x + pad, y + S(32), textRight, y + S(52), 11, Win32.FW_NORMAL, Theme.TextSecondary, _dpi);

        DrawToggleSwitch(swX, swY, swW, swH, id, value);

        if (y + rowH > headerH)
        {
            RegisterScrollableHit(
                new Win32.RECT { Left = swX - S(10), Top = y + S(8), Right = x + w - S(6), Bottom = y + rowH - S(8) },
                id,
                onToggle);
        }

        return y + rowH;
    }

    private int DrawSiteVolumeMemoryRow(int x, int y, int w, int headerH)
    {
        int rowH = S(62);
        const string rowId = "set_site_vol_mem";
        _d2d.FillAndStrokeRoundedRect(x, y, w, rowH, SF(10), Theme.BgCard, 1f, Theme.BorderSubtle, 1f);

        int pad = S(16);
        int swW = S(46);
        int swH = S(24);
        int swX = x + w - pad - swW;
        int swY = y + (rowH - swH) / 2;

        int savedCount = _settings.SavedSiteVolumes.Count;
        int clearW = 0;
        int clearX = swX;
        if (savedCount > 0)
        {
            string clearLabel = Strings.BtnClearSavedVols(savedCount);
            clearW = Math.Clamp(_d2d.MeasureTextWidth(clearLabel, 11, Win32.FW_SEMIBOLD, _dpi) + S(20), S(78), S(136));
            clearX = swX - S(10) - clearW;
        }

        int textRight = clearX - S(16);
        _d2d.DrawText(Strings.SetSiteVolMemTitle, x + pad, y + S(10), textRight, y + S(31), 13, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi);
        _d2d.DrawText(Strings.SetSiteVolMemSub, x + pad, y + S(32), textRight, y + S(52), 11, Win32.FW_NORMAL, Theme.TextSecondary, _dpi);

        DrawToggleSwitch(swX, swY, swW, swH, rowId, _settings.RememberSiteVolumeEnabled);

        if (y + rowH > headerH)
        {
            RegisterScrollableHit(
                new Win32.RECT { Left = swX - S(10), Top = y + S(8), Right = x + w - S(6), Bottom = y + rowH - S(8) },
                rowId,
                () => _stateStore.UpdateSettings(s => s.RememberSiteVolumeEnabled = !s.RememberSiteVolumeEnabled));

            if (savedCount > 0)
            {
                int clearH = S(28);
                int clearY = y + (rowH - clearH) / 2;
                DrawActionButton("set_site_vol_clear", Strings.BtnClearSavedVols(savedCount),
                    clearX, clearY, clearW, clearH,
                    Theme.BgControl, Theme.RedSoft, Theme.TextSecondary, Theme.BorderStrong,
                    () =>
                    {
                        _stateStore.ClearSavedSiteVolumes();
                        ShowStatusBanner(Strings.MsgSavedVolsCleared, isError: false);
                    });
            }
        }

        return y + rowH;
    }

    private int DrawStepperSettingRow(
        int x, int y, int w, int headerH, string id,
        string title, string subtitle, string valueText, Action onDec, Action onInc)
    {
        int rowH = S(62);
        _d2d.FillAndStrokeRoundedRect(x, y, w, rowH, SF(10), Theme.BgCard, 1f, Theme.BorderSubtle, 1f);

        int pad = S(16);
        int btnSz = S(28);
        int valW = S(66);
        int btnY = y + (rowH - btnSz) / 2;
        int incX = x + w - pad - btnSz;
        int valX = incX - S(6) - valW;
        int decX = valX - S(6) - btnSz;

        int textRight = decX - S(16);
        _d2d.DrawText(title, x + pad, y + S(10), textRight, y + S(31), 13, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi);
        _d2d.DrawText(subtitle, x + pad, y + S(32), textRight, y + S(52), 11, Win32.FW_NORMAL, Theme.TextSecondary, _dpi);

        _d2d.DrawText(valueText, valX, y, valX + valW, y + rowH, 13, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi, centered: true);

        if (y + rowH > headerH)
        {
            DrawActionButton($"{id}_dec", "-", decX, btnY, btnSz, btnSz,
                Theme.BgControl, Theme.BgControlHover, Theme.TextPrimary, Theme.BorderStrong, onDec);
            DrawActionButton($"{id}_inc", "+", incX, btnY, btnSz, btnSz,
                Theme.BgControl, Theme.BgControlHover, Theme.TextPrimary, Theme.BorderStrong, onInc);
        }

        return y + rowH;
    }

    private int DrawEmptyCard(int x, int y, int w, string heading, string detail)
    {
        int h = S(76);
        _d2d.FillAndStrokeRoundedRect(x, y, w, h, SF(12), Theme.BgCard, 1f, Theme.BorderSubtle, 1f);
        _d2d.DrawText(heading, x + S(16), y + S(14), x + w - S(16), y + S(36), 13, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi);
        _d2d.DrawWrappedText(detail, x + S(16), y + S(38), x + w - S(16), y + S(66), 11, Win32.FW_NORMAL, Theme.TextSecondary, _dpi);
        return y + h;
    }

    private enum TransportGlyph { Previous, Play, Pause, Next }

    private void DrawTransportButton(
        string id, TransportGlyph glyph, int x, int y, int sz, Action onClick, bool accent = false, bool circular = false)
    {
        float hovT = GetHoverT(id);
        bool pressed = !_settings.ReducedAnimations && string.Equals(_pressedHitId, id, StringComparison.Ordinal);
        uint bg = accent
            ? Theme.BlendRgb(Theme.Accent, Theme.AccentHover, hovT)
            : Theme.BlendRgb(Theme.BgControl, Theme.BgControlHover, hovT);
        uint border = accent ? Theme.Accent : Theme.BlendRgb(Theme.BorderStrong, Theme.Accent, hovT * 0.55f);
        uint fg = accent ? Theme.OnAccent : Theme.TextPrimary;

        float cx = x + sz * 0.5f;
        float cy = y + sz * 0.5f;
        float scale = pressed ? 0.93f : (1f + 0.04f * hovT);
        bool didScale = !_settings.ReducedAnimations && MathF.Abs(scale - 1f) > 0.002f;
        if (didScale)
            _d2d.PushTransform(Direct2D.D2D1_MATRIX_3X2_F.ScaleAt(scale, scale, cx, cy));

        float radius = circular ? sz * 0.5f : SF(8);
        _d2d.FillAndStrokeRoundedRect(x, y, sz, sz, radius, bg, 1f, border, 1f);

        float r = sz * 0.18f;

        switch (glyph)
        {
            case TransportGlyph.Play:
                _d2d.FillTriangle(cx - r * 0.55f, cy - r, cx - r * 0.55f, cy + r, cx + r * 1.05f, cy, fg);
                break;

            case TransportGlyph.Pause:
                float bw = MathF.Max(2.2f, sz * 0.08f);
                float bh = r * 1.9f;
                _d2d.FillRoundedRect(cx - bw * 1.45f, cy - bh * 0.5f, bw, bh, 1f, fg);
                _d2d.FillRoundedRect(cx + bw * 0.45f, cy - bh * 0.5f, bw, bh, 1f, fg);
                break;

            case TransportGlyph.Next:
                _d2d.FillTriangle(cx - r * 0.9f, cy - r * 0.85f, cx - r * 0.9f, cy + r * 0.85f, cx + r * 0.45f, cy, fg);
                _d2d.FillRoundedRect(cx + r * 0.55f, cy - r * 0.85f, MathF.Max(2f, sz * 0.07f), r * 1.7f, 0.8f, fg);
                break;

            case TransportGlyph.Previous:
                float barW = MathF.Max(2f, sz * 0.07f);
                _d2d.FillRoundedRect(cx - r * 0.85f, cy - r * 0.85f, barW, r * 1.7f, 0.8f, fg);
                _d2d.FillTriangle(cx + r * 0.9f, cy - r * 0.85f, cx + r * 0.9f, cy + r * 0.85f, cx - r * 0.45f, cy, fg);
                break;
        }

        if (didScale)
            _d2d.PopTransform();

        RegisterScrollableHit(new Win32.RECT { Left = x, Top = y, Right = x + sz, Bottom = y + sz }, id, onClick);
    }

    private enum SmallButtonGlyph { Top, Up, Down, Close, Hide, AudioDevice }

    private void DrawIconButton(
        string id, SmallButtonGlyph glyph, int x, int y, int sz, bool enabled, Action onClick,
        bool danger = false, int hitY = -1, bool active = false, uint? customAccent = null, bool scrollable = true)
    {
        float hovT = enabled ? GetHoverT(id) : 0f;
        uint bgRgb;
        float bgAlpha = 1f;
        uint borderRgb;
        float borderAlpha = 1f;
        uint fg;
        float strokeW = 1f;

        if (customAccent.HasValue && enabled)
        {
            uint c = customAccent.Value;
            float baseA = Theme.IsLight ? 0.11f : 0.14f;
            float t = active ? 1f : hovT;
            bgRgb = c;
            bgAlpha = baseA + (1f - baseA) * t;
            borderRgb = c;
            borderAlpha = 0.85f + 0.15f * t;
            fg = t > 0.5f ? Theme.OnAccent : c;
            strokeW = 1.2f - 0.2f * t;
        }
        else
        {
            bgRgb = !enabled ? Theme.BgCard
                : (active ? Theme.BlendRgb(Theme.AccentSoft, Theme.Accent, hovT)
                : Theme.BlendRgb(Theme.BgControl, danger ? Theme.RedSoft : Theme.BgControlHover, hovT));
            fg = !enabled ? Theme.TextMuted
                : (active ? Theme.BlendRgb(Theme.Accent, Theme.OnAccent, hovT)
                : (danger ? Theme.BlendRgb(Theme.TextPrimary, Theme.Red, hovT) : Theme.TextPrimary));
            borderRgb = active ? Theme.Accent : Theme.BorderSubtle;
        }

        _d2d.FillAndStrokeRoundedRect(x, y, sz, sz, SF(7), bgRgb, bgAlpha, borderRgb, borderAlpha, strokeW);

        float cx = x + sz * 0.5f;
        float cy = y + sz * 0.5f;
        float d = sz * 0.16f;
        float sw = MathF.Max(1.4f, SF(1.55f));

        switch (glyph)
        {
            case SmallButtonGlyph.Top:
                _d2d.DrawLine(cx - d, cy - d * 0.95f, cx + d, cy - d * 0.95f, fg, sw);
                _d2d.DrawLine(cx - d, cy + d * 0.8f, cx, cy - d * 0.25f, fg, sw);
                _d2d.DrawLine(cx, cy - d * 0.25f, cx + d, cy + d * 0.8f, fg, sw);
                break;
            case SmallButtonGlyph.Up:
                _d2d.DrawLine(cx - d, cy + d * 0.45f, cx, cy - d * 0.55f, fg, sw);
                _d2d.DrawLine(cx, cy - d * 0.55f, cx + d, cy + d * 0.45f, fg, sw);
                break;
            case SmallButtonGlyph.Down:
                _d2d.DrawLine(cx - d, cy - d * 0.45f, cx, cy + d * 0.55f, fg, sw);
                _d2d.DrawLine(cx, cy + d * 0.55f, cx + d, cy - d * 0.45f, fg, sw);
                break;
            case SmallButtonGlyph.Close:
                _d2d.DrawLine(cx - d, cy - d, cx + d, cy + d, fg, sw);
                _d2d.DrawLine(cx + d, cy - d, cx - d, cy + d, fg, sw);
                break;
            case SmallButtonGlyph.Hide:
                float r = sz * 0.24f;
                _d2d.StrokeCircle(cx - r, cy - r, r * 2f, fg, sw);
                float sl = r * 0.72f;
                _d2d.DrawLine(cx - sl, cy + sl, cx + sl, cy - sl, fg, sw);
                break;
            case SmallButtonGlyph.AudioDevice:
                _d2d.DrawAudioSwitchGlyph(cx, cy, sz, fg, _dpi);
                break;
        }

        if (enabled)
        {
            int regY = hitY >= 0 ? hitY : y;
            var rect = new Win32.RECT { Left = x, Top = regY, Right = x + sz, Bottom = regY + sz };
            if (scrollable)
                RegisterScrollableHit(rect, id, onClick);
            else
                _hitRegions.Add(new HitRegion(rect, id, onClick));
        }
    }

    private void RegisterScrollableHit(Win32.RECT bounds, string id, Action onClick)
    {
        if (_bodyClipTop > 0 && bounds.Top < _bodyClipTop)
            bounds.Top = _bodyClipTop;
        if (_bodyClipBottom > 0 && bounds.Bottom > _bodyClipBottom)
            bounds.Bottom = _bodyClipBottom;
        if (bounds.Bottom > bounds.Top && bounds.Right > bounds.Left)
            _hitRegions.Add(new HitRegion(bounds, id, onClick));
    }

    private void DrawActionButton(
        string id, string text,
        int x, int y, int w, int h,
        uint bgNormal, uint bgHover, uint fg, uint border, Action onClick,
        bool scrollable = true, int ptSize = 12)
    {
        float hovT = GetHoverT(id);
        uint bg = Theme.BlendRgb(bgNormal, bgHover, hovT);
        _d2d.FillAndStrokeRoundedRect(x, y, w, h, SF(8), bg, 1f, border, 1f);
        _d2d.DrawText(text, x + S(4), y, x + w - S(4), y + h, ptSize, Win32.FW_SEMIBOLD, fg, _dpi, centered: true);
        var rect = new Win32.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
        if (scrollable)
            RegisterScrollableHit(rect, id, onClick);
        else
            _hitRegions.Add(new HitRegion(rect, id, onClick));
    }

    private int DrawPillBadge(int x, int y, string text, uint fg, uint bg)
    {
        int pillW = Math.Max(S(50), _d2d.MeasureTextWidth(text, 10, Win32.FW_SEMIBOLD, _dpi) + S(18));
        int pillH = S(22);
        _d2d.FillRoundedRect(x, y, pillW, pillH, pillH * 0.5f, bg);
        _d2d.DrawText(text, x, y, x + pillW, y + pillH, 10, Win32.FW_SEMIBOLD, fg, _dpi, centered: true);
        return pillW;
    }

    private static bool IsBrowserOrigin(string? appKey, bool sessionIsBrowser = false)
    {
        if (sessionIsBrowser)
            return true;
        if (string.IsNullOrWhiteSpace(appKey))
            return false;
        if (BrowserSiteScanner.ExtractBrowserKeyFromAppKey(appKey) != null)
            return true;

        return appKey.ToLowerInvariant() switch
        {
            "zen" or "firefox" or "msedge" or "chrome" or "brave" or
            "opera" or "opera_gx" or "vivaldi" or "arc" or
            "waterfox" or "floorp" or "librewolf" or "helium" or "thorium" or "chromium" => true,
            _ => false
        };
    }

    private void DismissWhatsNew()
    {
        _showWhatsNew = false;
        string curVer = !string.IsNullOrEmpty(_updateSnapshot.CurrentVersion)
            ? _updateSnapshot.CurrentVersion
            : AppUpdateService.GetCurrentAppVersion();
        if (!string.IsNullOrEmpty(curVer) &&
            !string.Equals(_settings.LastSeenVersion, curVer, StringComparison.OrdinalIgnoreCase))
        {
            _stateStore.UpdateSettings(s => s.LastSeenVersion = curVer);
        }
        EnsureUiAnimTimerState();
        RequestRepaint();
    }

    private void DrawWhatsNewOverlay(int w, int h, double nowMs)
    {
        float modalEase = (!_settings.ReducedAnimations && _whatsNewOpenMs > 0)
            ? EaseOutCubic(_whatsNewOpenMs, nowMs, UI_MODAL_DURATION_MS)
            : 1f;

        _d2d.FillRect(0, 0, w, h, 0x000000, (Theme.IsLight ? 0.42f : 0.64f) * modalEase);

        _hitRegions.Add(new HitRegion(
            new Win32.RECT { Left = 0, Top = 0, Right = w, Bottom = h },
            "whatsnew_backdrop",
            DismissWhatsNew,
            IsButton: false));

        string curVer = !string.IsNullOrEmpty(_updateSnapshot.CurrentVersion)
            ? _updateSnapshot.CurrentVersion
            : AppUpdateService.GetCurrentAppVersion();
        var release = WhatsNewCatalog.GetForVersion(curVer);

        bool twoCol = release.Items.Length > 4 && w >= S(620);
        int cols = twoCol ? 2 : 1;
        int rows = (release.Items.Length + cols - 1) / cols;
        int cardW = twoCol ? Math.Clamp(w - S(48), S(600), S(768)) : Math.Clamp(w - S(56), S(420), S(560));
        int itemH = twoCol ? S(112) : S(74);
        int itemGap = twoCol ? S(11) : S(9);
        int colGap = S(11);
        int headerBandH = S(82);
        int footerBandH = S(62);
        int padX = S(22);
        int listTopPad = S(14);
        int listBottomPad = S(12);
        int listH = rows * itemH + Math.Max(0, rows - 1) * itemGap;
        int desiredH = headerBandH + listTopPad + listH + listBottomPad + footerBandH;
        int cardH = Math.Min(desiredH, h - S(28));

        int cardX = (w - cardW) / 2;
        int cardY = (h - cardH) / 2;

        bool didModalTransform = modalEase < 0.999f;
        if (didModalTransform)
        {
            float scale = 0.94f + 0.06f * modalEase;
            float dy = S(10) * (1f - modalEase);
            _d2d.PushTransform(Direct2D.D2D1_MATRIX_3X2_F.TranslateAndScaleAt(0f, dy, scale, scale, cardX + cardW * 0.5f, cardY + cardH * 0.5f));
        }

        _d2d.FillRoundedRect(cardX - S(6), cardY + S(8), cardW + S(12), cardH + S(12), SF(18), 0x000000, 0.30f * modalEase);
        _d2d.FillRoundedRect(cardX - S(2), cardY + S(3), cardW + S(4), cardH + S(5), SF(16), 0x000000, 0.16f * modalEase);

        uint shellBg = Theme.BlendRgb(Theme.BgWindow, Theme.BgCardElevated, 0.55f);
        _d2d.FillAndStrokeRoundedRect(cardX, cardY, cardW, cardH, SF(16),
            shellBg, 1f, Theme.BorderStrong, 1f, 1.35f);

        // Ambient Aura Deck backdrop glow + top specular gradient rim inside the modal
        _d2d.PushClip(cardX + 1, cardY + 1, cardW - 2, cardH - 2);
        _d2d.FillRadialAura(cardX + S(85), cardY + S(36), S(190), S(95), Theme.Accent, Theme.IsLight ? 0.10f : 0.16f);
        _d2d.FillRadialAura(cardX + cardW - S(110), cardY + S(44), S(160), S(85), Theme.Green, Theme.IsLight ? 0.07f : 0.11f);
        _d2d.FillSpecularRim(cardX + S(16), cardY, cardW - S(32), MathF.Max(1.5f, SF(2)), Theme.Accent, Theme.Green, 0.72f);
        _d2d.PopClip();

        _hitRegions.Add(new HitRegion(
            new Win32.RECT { Left = cardX, Top = cardY, Right = cardX + cardW, Bottom = cardY + cardH },
            "whatsnew_panel",
            () => { },
            IsButton: false));

        int icoBox = S(44);
        int icoBoxX = cardX + padX;
        int icoBoxY = cardY + (headerBandH - icoBox) / 2;
        _d2d.FillAndStrokeRoundedRect(icoBoxX, icoBoxY, icoBox, icoBox, SF(12),
            Theme.BlendRgb(Theme.BgCard, Theme.Accent, Theme.IsLight ? 0.08f : 0.14f), 1f,
            Theme.BlendRgb(Theme.BorderStrong, Theme.Accent, 0.45f), 1f, 1.2f);

        int brandSz = S(26);
        nint brandIcon = _iconRenderer.GetBrandIcon(brandSz);
        if (brandIcon != 0)
            _d2d.DrawIcon(brandIcon, icoBoxX + (icoBox - brandSz) * 0.5f, icoBoxY + (icoBox - brandSz) * 0.5f, brandSz);

        int closeSz = S(30);
        int closeX = cardX + cardW - padX - closeSz;
        int closeY = cardY + (headerBandH - closeSz) / 2;
        DrawIconButton("whatsnew_close", SmallButtonGlyph.Close, closeX, closeY, closeSz, true, DismissWhatsNew, scrollable: false);

        int titleX = icoBoxX + icoBox + S(14);
        int titleRight = closeX - S(10);
        int titleW = _d2d.MeasureTextWidth(Strings.WhatsNewTitle, 17, Win32.FW_BOLD, _dpi) + S(6);
        int titleEnd = Math.Min(titleRight, titleX + titleW);
        _d2d.DrawText(Strings.WhatsNewTitle, titleX, cardY + S(17), titleEnd, cardY + S(43), 17, Win32.FW_BOLD, Theme.TextPrimary, _dpi);

        string verPill = $"v{release.Version}";
        int pillW = Math.Max(S(50), _d2d.MeasureTextWidth(verPill, 10, Win32.FW_BOLD, _dpi) + S(18));
        if (titleEnd + S(8) + pillW <= titleRight)
        {
            int pX = titleEnd + S(8);
            int pY = cardY + S(20);
            int pH = S(22);
            _d2d.FillLinearGradientRoundedRect(pX, pY, pillW, pH, pH * 0.5f, Theme.Accent, 1f, Theme.AccentHover, 1f);
            _d2d.DrawText(verPill, pX, pY, pX + pillW, pY + pH, 10, Win32.FW_BOLD, Theme.OnAccent, _dpi, centered: true);
        }

        _d2d.DrawText(Strings.WhatsNewSubtitle(release.Version), titleX, cardY + S(45), titleRight, cardY + S(66),
            12, Win32.FW_NORMAL, Theme.TextSecondary, _dpi);

        _d2d.FillRect(cardX + padX, cardY + headerBandH - 1, cardW - padX * 2, 1, Theme.BorderSubtle);

        int baseY = cardY + headerBandH + listTopPad;
        int innerW = cardW - padX * 2;
        int colW = twoCol ? (innerW - colGap) / 2 : innerW;
        int listMaxBottom = cardY + cardH - footerBandH - S(6);

        for (int i = 0; i < release.Items.Length; i++)
        {
            int r = i / cols;
            int c = i % cols;
            int ix = cardX + padX + c * (colW + colGap);
            int iy = baseY + r * (itemH + itemGap);
            if (iy + itemH > listMaxBottom)
                break;

            var item = release.Items[i];
            uint itemAccent = i == 0 ? Theme.Accent : WhatsNewCardAccents[i % WhatsNewCardAccents.Length];
            string cardHitId = $"wn_item_{i}";
            float cardHovT = GetHoverT(cardHitId);

            uint itemBg = Theme.BlendRgb(Theme.BgCard, itemAccent, (Theme.IsLight ? 0.03f : 0.05f) + 0.05f * cardHovT);
            uint itemBorder = Theme.BlendRgb(Theme.BorderSubtle, itemAccent, 0.25f + 0.50f * cardHovT);
            _d2d.FillAndStrokeRoundedRect(ix, iy, colW, itemH, SF(12), itemBg, 1f, itemBorder, 1f, 1.05f);

            // Left colored indicator bar matching the Aura Deck card language
            _d2d.FillRoundedRect(ix + S(4), iy + S(12), S(3), itemH - S(24), SF(1.5f), itemAccent, 0.85f);

            int badgeSz = S(28);
            int badgeX = ix + S(14);
            int badgeY = iy + S(12);
            uint badgeBg = Theme.BlendRgb(Theme.BgCardElevated, itemAccent, Theme.IsLight ? 0.16f : 0.24f);
            _d2d.FillAndStrokeRoundedRect(badgeX, badgeY, badgeSz, badgeSz, SF(8), badgeBg, 1f, itemAccent, 0.35f);
            _d2d.DrawText($"0{i + 1}", badgeX, badgeY, badgeX + badgeSz, badgeY + badgeSz, 10, Win32.FW_BOLD, itemAccent, _dpi, centered: true);

            int textX = badgeX + badgeSz + S(11);
            int textRight = ix + colW - S(12);
            _d2d.DrawText(item.Title, textX, iy + S(10), textRight, iy + S(31), 12, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi);
            _d2d.DrawWrappedText(item.Description, textX, iy + S(31), textRight, iy + itemH - S(7),
                11, Win32.FW_NORMAL, Theme.TextSecondary, _dpi);

            _hitRegions.Add(new HitRegion(
                new Win32.RECT { Left = ix, Top = iy, Right = ix + colW, Bottom = iy + itemH },
                cardHitId,
                () => { },
                IsButton: false));
        }

        int footY = cardY + cardH - footerBandH;
        _d2d.FillRect(cardX + padX, footY, cardW - padX * 2, 1, Theme.BorderSubtle);

        string footMeta = $"Cueway v{release.Version}";
        _d2d.DrawText(footMeta, cardX + padX, footY, cardX + cardW - padX - S(190), footY + footerBandH,
            11, Win32.FW_MEDIUM, Theme.TextMuted, _dpi);

        int gotW = Math.Clamp(_d2d.MeasureTextWidth(Strings.WhatsNewBtnGotIt, 12, Win32.FW_SEMIBOLD, _dpi) + S(42), S(120), S(176));
        int gotH = S(36);
        int gotX = cardX + cardW - padX - gotW;
        int gotY = footY + (footerBandH - gotH) / 2;

        float gotHovT = GetHoverT("whatsnew_gotit");
        _d2d.FillLinearGradientRoundedRect(gotX, gotY, gotW, gotH, SF(9),
            Theme.BlendRgb(Theme.Accent, Theme.AccentHover, gotHovT), 1f, Theme.AccentHover, 1f);
        _d2d.DrawText(Strings.WhatsNewBtnGotIt, gotX, gotY, gotX + gotW, gotY + gotH,
            12, Win32.FW_SEMIBOLD, Theme.OnAccent, _dpi, centered: true);
        _hitRegions.Add(new HitRegion(
            new Win32.RECT { Left = gotX, Top = gotY, Right = gotX + gotW, Bottom = gotY + gotH },
            "whatsnew_gotit",
            DismissWhatsNew));
        if (didModalTransform)
            _d2d.PopTransform();
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
        _iconRenderer.IconsUpdated -= OnIconsUpdated;
        _guideWindow?.Dispose();
        _guideWindow = null;
        _themeEditorWindow?.Dispose();
        _themeEditorWindow = null;

        if (_hwnd != 0)
        {
            StopUiAnimTimer();
            Win32.KillTimer(_hwnd, TIMER_IDLE_RELEASE);
            Win32.WTSUnRegisterSessionNotification(_hwnd);
            Win32.DestroyWindow(_hwnd);
            _hwnd = 0;
        }
        _d2d.Dispose();
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
