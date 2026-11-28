using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cueway.Audio;
using Cueway.Config;
using Cueway.Interop;
using Cueway.Localization;
using Cueway.Media;
using Cueway.Routing;

namespace Cueway.UI;

/// <summary>
/// GPU-accelerated Direct2D tray flyout matching Cueway's Aura Deck design with VSync-paced animations.
/// </summary>
internal sealed unsafe class TrayFlyoutWindow : IDisposable
{
    private const string ClassName = "Cueway_TrayFlyout";
    private const uint WM_APP_FLYOUT_REFRESH = Win32.WM_APP + 216;
    private const uint WM_APP_ANIM_FRAME = Win32.WM_APP + 217;
    private const nuint TIMER_UI_ANIM = 9002;
    private const nuint TIMER_IDLE_RELEASE = 9003;
    private const uint IDLE_RELEASE_MS = 90_000;
    private const uint UI_ANIM_FALLBACK_MS = 8;

    private const int FlyoutWidthPx = 392;
    private const int HeaderHeightPx = 52;
    private const int FooterHeightPx = 48;
    private const int CardHeightPx = 96;
    private const int CardGapPx = 10;
    private const int MaxVisibleCards = 4;

    private const int OPEN_ANIM_MS = 190;
    private const int DRAWER_ANIM_MS = 170;
    private const int CARD_STAGGER_STEP_MS = 22;

    private enum FlyoutDrawer
    {
        None = 0,
        AudioDevices = 1,
        SleepTimer = 2,
        AppOutputDevice = 3
    }

    private sealed record HitRegion(Win32.RECT Bounds, string Id, Action OnClick, bool IsButton = true);

    private sealed record SliderRegion(
        Win32.RECT Bounds,
        string Id,
        string SessionId,
        int TrackLeft,
        int TrackWidth);

    private static TrayFlyoutWindow? _instance;

    private readonly StateStore _stateStore;
    private readonly MediaRouterEngine _engine;
    private readonly DynamicIconRenderer _iconRenderer;
    private readonly Func<Win32.RECT?> _getTrayRect;
    private readonly D2DRenderer _d2d = new();

    private readonly List<HitRegion> _hitRegions = [];
    private readonly List<SliderRegion> _sliderRegions = [];
    private readonly Dictionary<string, float> _hoverAnim = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _volAnim = new(StringComparer.Ordinal);
    private readonly List<string> _animKeyScratch = new(32);
    private readonly List<string> _animPruneScratch = new(16);

    private nint _hwnd;
    private nint _cursorArrow;
    private nint _cursorHand;
    private uint _dpi = 96;
    private int _clientW = FlyoutWidthPx;
    private int _clientH = 200;
    private int _scrollY;
    private float _scrollYFloat;
    private int _scrollTargetY;
    private int _contentTotalH;

    private string? _hoverId;
    private string? _pressedHitId;
    private bool _trackingMouse;
    private bool _hoverIsHand;
    private int _wheelAccum;
    private string? _draggingSliderId;
    private string? _draggingSessionId;
    private int _dragTrackLeft;
    private int _dragTrackWidth;
    private float _dragPreviewVolume;
    private long _lastSliderDispatchTick;
    private long _lastDeactivateTick;

    private RouterStateSnapshot _snapshot;
    private AppSettings _settings;
    private string _lastLanguage = string.Empty;
    private uint _lastDwmBorder = uint.MaxValue;
    private bool _lastDwmIsLight;

    private FlyoutDrawer _openDrawer = FlyoutDrawer.None;
    private string? _drawerTargetSessionId;
    private string? _drawerTargetAppName;
    private string? _drawerTargetBrowserName;
    private List<AudioOutputDevice> _outputDevices = [];
    private Dictionary<string, uint> _deviceColors = new(StringComparer.OrdinalIgnoreCase);
    private string? _defaultOutputDeviceId;
    private bool _deviceColorsForLight;

    private double _openAnimStartMs;
    private double _drawerAnimStartMs;
    private double _lastFrameMs;
    private float _lastFrameDt = 0.016f;
    private bool _anySpringAnimating;
    private int _vsyncPending;
    private bool _uiAnimTimerActive;
    private static readonly WaitCallback VSyncWaitCallback = static state =>
    {
        var self = (TrayFlyoutWindow)state!;
        int hr = Win32.DwmFlush();
        nint targetHwnd = self._hwnd;
        if (self._disposed || targetHwnd == 0)
        {
            Interlocked.Exchange(ref self._vsyncPending, 0);
            return;
        }
        if (!Win32.PostMessageW(targetHwnd, WM_APP_ANIM_FRAME, hr == 0 ? (nuint)0 : (nuint)1, 0))
            Interlocked.Exchange(ref self._vsyncPending, 0);
    };
    private byte _lastAppliedAlpha = 255;
    private bool _disposed;

    public event Action? OpenDashboardRequested;
    public event Action? OpenSettingsRequested;

    public TrayFlyoutWindow(
        StateStore stateStore,
        MediaRouterEngine engine,
        DynamicIconRenderer iconRenderer,
        Func<Win32.RECT?> getTrayRect)
    {
        _stateStore = stateStore;
        _engine = engine;
        _iconRenderer = iconRenderer;
        _getTrayRect = getTrayRect;
        _snapshot = _engine.GetSnapshot();
        _settings = _stateStore.GetSettingsSnapshot();
        _lastLanguage = _settings.Language;
        _instance = this;

        CreateFlyoutWindow();
        _engine.StateChanged += OnStateChanged;
        _engine.SleepTimerChanged += OnSleepTimerChanged;
        _stateStore.StateChanged += OnStoreChanged;
        _iconRenderer.IconsUpdated += OnIconsUpdated;
    }

    public bool IsVisible => _hwnd != 0 && Win32.IsWindowVisible(_hwnd);

    private static double NowMs() => Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency);

    private int S(int px) => Theme.Scale(px, _dpi);
    private float SF(float px) => Theme.ScaleF(px, _dpi);

    private void RebuildOutputDeviceColorCache()
    {
        _defaultOutputDeviceId = AudioDeviceColors.GetDefaultDeviceId(_outputDevices);
        _deviceColors = AudioDeviceColors.BuildDeviceColorMap(_outputDevices, Theme.IsLight);
        _deviceColorsForLight = Theme.IsLight;
    }

    private uint GetEffectiveDeviceColor(string? deviceId)
    {
        if (_deviceColorsForLight != Theme.IsLight)
            RebuildOutputDeviceColorCache();
        string? effective = string.IsNullOrWhiteSpace(deviceId) ? _defaultOutputDeviceId : deviceId;
        return effective != null && _deviceColors.TryGetValue(effective, out uint color) ? color : Theme.Accent;
    }

    private void CreateFlyoutWindow()
    {
        _cursorArrow = Win32.LoadCursorW(0, Win32.IDC_ARROW);
        _cursorHand = Win32.LoadCursorW(0, Win32.IDC_HAND);
        nint hInst = Win32.GetModuleHandleW(null);

        fixed (char* cls = ClassName)
        {
            var wc = new Win32.WNDCLASSEXW
            {
                cbSize = (uint)sizeof(Win32.WNDCLASSEXW),
                style = Win32.CS_HREDRAW | Win32.CS_VREDRAW,
                lpfnWndProc = &WndProc,
                hInstance = hInst,
                hCursor = _cursorArrow,
                lpszClassName = cls
            };
            Win32.RegisterClassExW(&wc);
        }

        _hwnd = Win32.CreateWindowExW(
            Win32.WS_EX_TOPMOST | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_LAYERED,
            ClassName,
            "Cueway",
            Win32.WS_POPUP,
            0, 0, FlyoutWidthPx, 200,
            0, 0, hInst, 0);

        if (_hwnd != 0)
        {
            Win32.SetLayeredWindowAttributes(_hwnd, 0, 255, Win32.LWA_ALPHA);
            _lastAppliedAlpha = 255;
            uint d = Win32.GetDpiForWindow(_hwnd);
            if (d >= 96) _dpi = d;
            ApplyDwmStyle();
        }
    }

    private void ApplyDwmStyle()
    {
        if (_hwnd == 0) return;
        int dark = Theme.IsLight ? 0 : 1;
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(int));
        int corner = Win32.DWMWCP_ROUND;
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(int));
        uint borderColor = Theme.ToColorRef(Theme.BorderStrong);
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_BORDER_COLOR, &borderColor, sizeof(uint));
        _lastDwmIsLight = Theme.IsLight;
        _lastDwmBorder = Theme.BorderStrong;
    }

    private void SyncDwmStyleIfNeeded()
    {
        if (_hwnd == 0) return;
        if (_lastDwmIsLight != Theme.IsLight || _lastDwmBorder != Theme.BorderStrong)
            ApplyDwmStyle();
    }

    private void SetWindowAlpha(byte alpha)
    {
        if (_hwnd == 0 || _lastAppliedAlpha == alpha)
            return;
        _lastAppliedAlpha = alpha;
        Win32.SetLayeredWindowAttributes(_hwnd, 0, alpha, Win32.LWA_ALPHA);
    }

    public void Toggle()
    {
        if (_disposed || _hwnd == 0) return;
        if (IsVisible)
        {
            Hide();
            return;
        }

        // Ignore the tray click that just caused WM_ACTIVATE(WA_INACTIVE) to close the flyout
        if (Environment.TickCount64 - _lastDeactivateTick < 260)
            return;

        Show();
    }

    public void Show()
    {
        if (_disposed || _hwnd == 0) return;
        Win32.KillTimer(_hwnd, TIMER_IDLE_RELEASE);
        _snapshot = _engine.GetSnapshot();
        _settings = _stateStore.GetSettingsSnapshot();
        if (!string.Equals(_lastLanguage, _settings.Language, StringComparison.OrdinalIgnoreCase))
        {
            _lastLanguage = _settings.Language;
            _d2d.ClearTextFormatCache();
        }

        _openDrawer = FlyoutDrawer.None;
        _drawerAnimStartMs = 0;
        _outputDevices = _engine.GetOutputDevicesCached();
        RebuildOutputDeviceColorCache();
        _scrollY = 0;
        _scrollYFloat = 0f;
        _scrollTargetY = 0;
        _hoverId = null;
        _hoverIsHand = false;
        _wheelAccum = 0;
        _pressedHitId = null;
        _hoverAnim.Clear();
        _volAnim.Clear();
        _anySpringAnimating = false;

        ApplyDwmStyle();
        PositionAndResize();

        if (!_settings.ReducedAnimations)
        {
            _openAnimStartMs = -1.0;
            _lastFrameMs = 0;
            SetWindowAlpha(12);
        }
        else
        {
            _openAnimStartMs = 0;
            _lastFrameMs = 0;
            SetWindowAlpha(255);
        }

        Win32.ShowWindow(_hwnd, Win32.SW_SHOW);
        Win32.TryFocusWindow(_hwnd);
        KickAnimations();
    }

    public void Hide()
    {
        if (_hwnd == 0 || !Win32.IsWindowVisible(_hwnd)) return;

        if (_draggingSliderId != null)
        {
            _draggingSliderId = null;
            _draggingSessionId = null;
            Win32.ReleaseCapture();
        }

        StopUiAnimTimer();
        _anySpringAnimating = false;
        _openAnimStartMs = 0;
        _drawerAnimStartMs = 0;
        _lastFrameMs = 0;
        _openDrawer = FlyoutDrawer.None;
        _hoverId = null;
        _hoverIsHand = false;
        _wheelAccum = 0;
        _pressedHitId = null;
        _hoverAnim.Clear();
        _volAnim.Clear();

        Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
        _hitRegions.Clear();
        _hitRegions.TrimExcess();
        _sliderRegions.Clear();
        _sliderRegions.TrimExcess();
        Win32.SetTimer(_hwnd, TIMER_IDLE_RELEASE, IDLE_RELEASE_MS, 0);
    }

    public void InvalidateIconCache() => _d2d.ClearIconCache();

    private void OnIconsUpdated() => NotifyVisualRefresh(clearIcons: true);

    public void RefreshVisuals()
    {
        if (_disposed || _hwnd == 0) return;
        if (Win32.IsWindowVisible(_hwnd))
            Win32.PostMessageW(_hwnd, WM_APP_FLYOUT_REFRESH, 1, 0);
    }

    public void RefreshAudioDevices()
    {
        if (_disposed || _hwnd == 0) return;
        _outputDevices = _engine.GetOutputDevices();
        RebuildOutputDeviceColorCache();
        if (Win32.IsWindowVisible(_hwnd))
        {
            if (_openDrawer == FlyoutDrawer.AudioDevices || _openDrawer == FlyoutDrawer.AppOutputDevice)
                PositionAndResize();
            KickAnimations();
        }
    }

    private void OnSleepTimerChanged() => NotifyVisualRefresh(clearIcons: false);
    private void OnStateChanged(RouterStateSnapshot snap) => NotifyVisualRefresh(clearIcons: false);
    private void OnStoreChanged() => NotifyVisualRefresh(clearIcons: false);

    private void NotifyVisualRefresh(bool clearIcons)
    {
        if (!_disposed && _hwnd != 0 && Win32.IsWindowVisible(_hwnd))
            Win32.PostMessageW(_hwnd, WM_APP_FLYOUT_REFRESH, clearIcons ? (nuint)1 : 0, 0);
    }

    private void HandleAsyncRefresh(bool clearIcons)
    {
        if (_disposed || _hwnd == 0 || !Win32.IsWindowVisible(_hwnd))
            return;

        if (clearIcons)
            _d2d.ClearIconCache();

        _settings = _stateStore.GetSettingsSnapshot();
        if (!string.Equals(_lastLanguage, _settings.Language, StringComparison.OrdinalIgnoreCase))
        {
            _lastLanguage = _settings.Language;
            _d2d.ClearTextFormatCache();
        }

        int prevCount = _snapshot.Sessions.Count;
        _snapshot = _engine.GetSnapshot();
        if (_snapshot.Sessions.Count != prevCount)
            PositionAndResize();
        else
            SyncDwmStyleIfNeeded();

        KickAnimations();
    }

    private void OpenOrToggleDrawer(FlyoutDrawer target)
    {
        if (_openDrawer == target && target != FlyoutDrawer.AppOutputDevice)
        {
            _openDrawer = FlyoutDrawer.None;
            _drawerAnimStartMs = 0;
        }
        else
        {
            _openDrawer = target;
            _drawerAnimStartMs = _settings.ReducedAnimations ? 0 : -1.0;
        }
        PositionAndResize();
        KickAnimations();
    }

    private bool ShouldShowBrowserOutputNotice()
        => !string.IsNullOrEmpty(_drawerTargetBrowserName) &&
           !_settings.SuppressBrowserOutputNotice;

    private int ComputeDrawerHeightPx() => _openDrawer switch
    {
        FlyoutDrawer.AudioDevices => Math.Max(1, Math.Min(_outputDevices.Count, 5)) * 34 + 14,
        FlyoutDrawer.SleepTimer => 50,
        FlyoutDrawer.AppOutputDevice => 26 + Math.Min(_outputDevices.Count + 1, 6) * 32 + 14 + (ShouldShowBrowserOutputNotice() ? 72 : 0),
        _ => 0
    };

    private int ComputeDesiredHeightPx()
    {
        int extra = FooterHeightPx + ComputeDrawerHeightPx();
        int count = _snapshot.Sessions.Count;
        int padV = 12;
        if (count == 0)
            return HeaderHeightPx + padV * 2 + 104 + extra;

        int visible = Math.Min(count, MaxVisibleCards);
        return HeaderHeightPx + padV * 2 + visible * CardHeightPx + Math.Max(0, visible - 1) * CardGapPx + extra;
    }

    private void PositionAndResize()
    {
        if (_hwnd == 0) return;

        Win32.RECT? trayRect = _getTrayRect();
        Win32.POINT anchorPt;
        if (trayRect.HasValue && trayRect.Value.Width > 0 && trayRect.Value.Height > 0)
        {
            anchorPt = new Win32.POINT
            {
                X = (trayRect.Value.Left + trayRect.Value.Right) / 2,
                Y = (trayRect.Value.Top + trayRect.Value.Bottom) / 2
            };
        }
        else
        {
            Win32.GetCursorPos(out anchorPt);
        }

        nint hMon = Win32.MonitorFromPoint(anchorPt, Win32.MONITOR_DEFAULTTONEAREST);
        uint monDpi = Win32.GetMonitorDpi(hMon, _dpi >= 96 ? _dpi : 96u);
        if (monDpi >= 96 && monDpi != _dpi)
        {
            _dpi = monDpi;
            _d2d.ClearTextFormatCache();
            _d2d.ClearIconCache();
        }

        var mi = new Win32.MONITORINFO { cbSize = (uint)sizeof(Win32.MONITORINFO) };
        Win32.GetMonitorInfoW(hMon, &mi);

        int w = S(FlyoutWidthPx);
        int h = S(ComputeDesiredHeightPx());
        int margin = S(10);

        int maxW = Math.Max(S(280), mi.rcWork.Width - margin * 2);
        int maxH = Math.Max(S(160), mi.rcWork.Height - margin * 2);
        w = Math.Min(w, maxW);
        h = Math.Min(h, maxH);

        int x;
        int y;
        if (trayRect.HasValue && trayRect.Value.Width > 0 && trayRect.Value.Height > 0)
        {
            var tr = trayRect.Value;
            if (tr.Top >= mi.rcWork.Bottom)
            {
                x = tr.Left + tr.Width / 2 - w / 2;
                y = mi.rcWork.Bottom - h - margin;
            }
            else if (tr.Bottom <= mi.rcWork.Top)
            {
                x = tr.Left + tr.Width / 2 - w / 2;
                y = mi.rcWork.Top + margin;
            }
            else if (tr.Left >= mi.rcWork.Right)
            {
                x = mi.rcWork.Right - w - margin;
                y = tr.Top + tr.Height / 2 - h / 2;
            }
            else if (tr.Right <= mi.rcWork.Left)
            {
                x = mi.rcWork.Left + margin;
                y = tr.Top + tr.Height / 2 - h / 2;
            }
            else
            {
                x = mi.rcWork.Right - w - margin;
                y = mi.rcWork.Bottom - h - margin;
            }
        }
        else
        {
            x = mi.rcWork.Right - w - margin;
            y = mi.rcWork.Bottom - h - margin;
        }

        x = Math.Clamp(x, mi.rcWork.Left + margin, Math.Max(mi.rcWork.Left + margin, mi.rcWork.Right - w - margin));
        y = Math.Clamp(y, mi.rcWork.Top + margin, Math.Max(mi.rcWork.Top + margin, mi.rcWork.Bottom - h - margin));

        _clientW = w;
        _clientH = h;
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, y, w, h, Win32.SWP_NOACTIVATE);
        _d2d.Resize(w, h);
        ClampScroll();
    }

    private void RequestRepaint()
    {
        if (_hwnd != 0 && Win32.IsWindowVisible(_hwnd))
            Win32.InvalidateRect(_hwnd, null, false);
    }

    private void KickAnimations()
    {
        if (!_settings.ReducedAnimations && _hwnd != 0 && Win32.IsWindowVisible(_hwnd) && _lastFrameMs <= 0)
            _lastFrameMs = NowMs();
        RequestRepaint();
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
            return;
        }

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

        if (_openAnimStartMs > 0)
        {
            int staggerWindow = OPEN_ANIM_MS + Math.Min(_snapshot.Sessions.Count, MaxVisibleCards) * CARD_STAGGER_STEP_MS;
            if ((nowMs - _openAnimStartMs) >= staggerWindow)
                _openAnimStartMs = 0;
        }
        if (_drawerAnimStartMs > 0 && (nowMs - _drawerAnimStartMs) >= DRAWER_ANIM_MS)
            _drawerAnimStartMs = 0;
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

    private bool HasActiveUiAnimation(double nowMs)
    {
        if (_settings.ReducedAnimations)
            return false;

        int openWindow = OPEN_ANIM_MS + Math.Min(_snapshot.Sessions.Count, MaxVisibleCards) * CARD_STAGGER_STEP_MS;
        return _anySpringAnimating ||
               _openAnimStartMs < 0 || (_openAnimStartMs > 0 && (nowMs - _openAnimStartMs) < openWindow) ||
               _drawerAnimStartMs < 0 || (_drawerAnimStartMs > 0 && (nowMs - _drawerAnimStartMs) < DRAWER_ANIM_MS);
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

    private void ScheduleNextVSyncFrame()
    {
        nint hwnd = _hwnd;
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

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        var inst = _instance;
        if (inst == null || hwnd != inst._hwnd)
            return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);

        try
        {
            switch (msg)
            {
                case WM_APP_FLYOUT_REFRESH:
                    inst.HandleAsyncRefresh(wParam != 0);
                    return 0;

                case WM_APP_ANIM_FRAME:
                    Interlocked.Exchange(ref inst._vsyncPending, 0);
                    if (wParam != 0)
                    {
                        inst.StartUiAnimTimer();
                    }
                    else if (Win32.IsWindowVisible(hwnd) && inst.HasActiveUiAnimation(NowMs()))
                    {
                        Win32.InvalidateRect(hwnd, null, false);
                    }
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
                    break;

                case Win32.WM_DPICHANGED:
                    inst._dpi = (uint)(wParam & 0xFFFF);
                    inst._d2d.ClearTextFormatCache();
                    inst._d2d.ClearIconCache();
                    inst.PositionAndResize();
                    Win32.InvalidateRect(hwnd, null, false);
                    return 0;

                case Win32.WM_SIZE:
                    inst._clientW = Math.Max(1, (int)(lParam & 0xFFFF));
                    inst._clientH = Math.Max(1, (int)((lParam >> 16) & 0xFFFF));
                    inst._d2d.Resize(inst._clientW, inst._clientH);
                    inst.ClampScroll();
                    Win32.InvalidateRect(hwnd, null, false);
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
                    inst.HandleLeftButtonUp(lParam);
                    return 0;

                case Win32.WM_MOUSEWHEEL:
                    short delta = unchecked((short)((long)wParam >> 16));
                    inst.HandleMouseWheel(delta);
                    return 0;

                case Win32.WM_KEYDOWN:
                    if (wParam == Win32.VK_ESCAPE)
                    {
                        inst.Hide();
                        return 0;
                    }
                    break;

                case Win32.WM_ACTIVATE:
                    if ((wParam & 0xFFFF) == Win32.WA_INACTIVE)
                    {
                        inst._pressedHitId = null;
                        inst._lastDeactivateTick = Environment.TickCount64;
                        inst.Hide();
                    }
                    return 0;

                case Win32.WM_CLOSE:
                    inst.Hide();
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

        // Check embedded left mute button BEFORE slider hit region so clicking speaker toggles mute cleanly
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

        var slider = FindSliderHit(x, y);
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

        var slider = FindSliderHit(x, y);
        if (slider != null && (topHit == null || !topHit.IsButton))
        {
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

        _pressedHitId = topHit?.Id;
        return false;
    }

    private void HandleLeftButtonUp(nint lParam)
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
            _ = _engine.SetSessionVolumeAsync(targetSession, finalVol, commitMemory: true);
            RequestRepaint();
            return;
        }

        string? pressedId = _pressedHitId;
        _pressedHitId = null;

        var hit = FindHit(x, y);
        if (hit != null && pressedId != null && string.Equals(pressedId, hit.Id, StringComparison.Ordinal))
        {
            try { hit.OnClick(); } catch { }
            RequestRepaint();
        }
    }

    private void HandleMouseWheel(short delta)
    {
        _wheelAccum += delta;
        int step = S(36);
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

    private void ClampScroll()
    {
        int headerH = S(HeaderHeightPx);
        int bottomH = S(FooterHeightPx + ComputeDrawerHeightPx());
        int viewH = Math.Max(1, _clientH - headerH - bottomH);
        int maxScroll = Math.Max(0, _contentTotalH - viewH);
        _scrollTargetY = Math.Clamp(_scrollTargetY, 0, maxScroll);
        _scrollYFloat = Math.Clamp(_scrollYFloat, 0f, maxScroll);
        _scrollY = Math.Clamp((int)MathF.Round(_scrollYFloat), 0, maxScroll);
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

    private void HandlePaint(nint hwnd)
    {
        SyncDwmStyleIfNeeded();
        var ps = new Win32.PAINTSTRUCT();
        nint hdc = Win32.BeginPaint(hwnd, &ps);
        if (hdc == 0) return;

        int w = Math.Max(1, _clientW);
        int h = Math.Max(1, _clientH);

        try
        {
            double nowMs = NowMs();
            if (_openAnimStartMs < 0) _openAnimStartMs = nowMs;
            if (_drawerAnimStartMs < 0) _drawerAnimStartMs = nowMs;
            float dt = _lastFrameMs > 0 ? Math.Clamp((float)((nowMs - _lastFrameMs) / 1000.0), 0.001f, 0.05f) : 0.016f;
            _lastFrameMs = nowMs;
            StepAnimations(dt, nowMs);

            bool animatingOpen = !_settings.ReducedAnimations && _openAnimStartMs > 0;
            if (animatingOpen)
            {
                float openEase = EaseOutCubic(_openAnimStartMs, nowMs, OPEN_ANIM_MS);
                SetWindowAlpha((byte)Math.Clamp((int)MathF.Round(255f * openEase), 16, 255));
            }
            else
            {
                SetWindowAlpha(255);
            }

            if (_d2d.BeginFrame(hwnd, w, h))
            {
                _hitRegions.Clear();
                _sliderRegions.Clear();

                _d2d.Clear(Theme.BgWindow);
                _d2d.SetTextAntialiasGrayscale(animatingOpen);

                if (animatingOpen)
                {
                    float openEase = EaseOutCubic(_openAnimStartMs, nowMs, OPEN_ANIM_MS);
                    if (openEase < 0.999f)
                    {
                        float dy = SF(10) * (1f - openEase);
                        float scale = 0.975f + 0.025f * openEase;
                        _d2d.SetTransform(Direct2D.D2D1_MATRIX_3X2_F.TranslateAndScaleAt(0f, dy, scale, scale, w * 0.5f, h * 0.5f));
                    }
                }

                // Ambient Aura Deck backdrop glow + top specular gradient rim
                int headerH = S(HeaderHeightPx);
                _d2d.PushClip(0, 0, w, h);
                _d2d.FillRadialAura(S(68), S(20), S(130), S(56), Theme.Accent, Theme.IsLight ? 0.08f : 0.13f);
                _d2d.FillSpecularRim(S(14), 0, Math.Max(1f, w - S(28)), MathF.Max(1.5f, SF(1.8f)), Theme.Accent, Theme.Green, 0.72f);
                _d2d.PopClip();

                int footerH = S(FooterHeightPx);
                int drawerH = S(ComputeDrawerHeightPx());
                int bodyBottom = Math.Max(headerH + 1, h - footerH - drawerH);

                DrawBody(w, bodyBottom, headerH, nowMs);
                DrawHeader(w, headerH);
                if (drawerH > 0)
                    DrawDrawer(w, bodyBottom, drawerH, nowMs);
                DrawFooter(w, h - footerH, footerH);

                _d2d.ResetTransform();
                _d2d.DrawRoundedRect(0.5f, 0.5f, w - 1f, h - 1f, SF(10), Theme.BorderStrong, 1f, 0.72f);
                _d2d.SetTextAntialiasGrayscale(false);

                _d2d.EndFrame(out bool deviceLost);
                if (deviceLost)
                {
                    RequestRepaint();
                    return;
                }
            }
        }
        finally
        {
            Win32.EndPaint(hwnd, &ps);
        }

        double afterMs = NowMs();
        if (Win32.IsWindowVisible(hwnd) && HasActiveUiAnimation(afterMs))
        {
            StopUiAnimTimer();
            ScheduleNextVSyncFrame();
        }
        else
        {
            StopUiAnimTimer();
            _lastFrameMs = 0;
        }
    }

    private void DrawHeader(int w, int headerH)
    {
        _d2d.FillRect(0, headerH - 1, w, 1, Theme.BorderSubtle, 0.85f);

        int padX = S(14);
        int icoBox = S(28);
        int icoBoxY = (headerH - icoBox) / 2;
        _d2d.FillAndStrokeRoundedRect(padX, icoBoxY, icoBox, icoBox, SF(8),
            Theme.BlendRgb(Theme.BgCardElevated, Theme.Accent, Theme.IsLight ? 0.08f : 0.14f), 1f,
            Theme.BlendRgb(Theme.BorderSubtle, Theme.Accent, 0.35f), 1f, 1f);

        int iconSz = S(18);
        nint brandIcon = _iconRenderer.GetBrandIcon(iconSz);
        if (brandIcon != 0)
            _d2d.DrawIcon(brandIcon, padX + (icoBox - iconSz) * 0.5f, icoBoxY + (icoBox - iconSz) * 0.5f, iconSz);
        else
            _d2d.FillRoundedRect(padX + S(6), icoBoxY + S(6), icoBox - S(12), icoBox - S(12), SF(4), Theme.Accent);

        int titleX = padX + icoBox + S(10);
        int btnH = S(28);
        int btnY = (headerH - btnH) / 2;
        int setW = Math.Clamp(_d2d.MeasureTextWidth(Strings.FlyoutBtnSettings, 10, Win32.FW_SEMIBOLD, _dpi) + S(20), S(64), S(96));
        int dashW = Math.Clamp(_d2d.MeasureTextWidth(Strings.FlyoutBtnDashboard, 10, Win32.FW_SEMIBOLD, _dpi) + S(22), S(78), S(112));
        int setX = w - padX - setW;
        int dashX = setX - S(6) - dashW;

        DrawHeaderPillButton("fly_dash", Strings.FlyoutBtnDashboard, dashX, btnY, dashW, btnH, () =>
        {
            Hide();
            OpenDashboardRequested?.Invoke();
        });
        DrawHeaderPillButton("fly_set", Strings.FlyoutBtnSettings, setX, btnY, setW, btnH, () =>
        {
            Hide();
            OpenSettingsRequested?.Invoke();
        });

        int titleRight = dashX - S(8);
        _d2d.DrawText("Cueway", titleX, 0, titleRight, headerH, 13, Win32.FW_BOLD, Theme.TextPrimary, _dpi);
    }

    private void DrawHeaderPillButton(string id, string text, int x, int y, int w, int h, Action onClick)
    {
        float hovT = GetHoverT(id);
        uint bg = Theme.BlendRgb(Theme.BgCard, Theme.BgControlHover, hovT);
        uint border = Theme.BlendRgb(Theme.BorderSubtle, Theme.Accent, hovT * 0.65f);
        uint fg = Theme.BlendRgb(Theme.TextSecondary, Theme.TextPrimary, hovT);

        _d2d.FillAndStrokeRoundedRect(x, y, w, h, h * 0.5f, bg, 1f, border, 1f, 1f);
        _d2d.DrawText(text, x + S(6), y, x + w - S(6), y + h, 10, Win32.FW_SEMIBOLD, fg, _dpi, centered: true);
        _hitRegions.Add(new HitRegion(new Win32.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h }, id, onClick));
    }

    private void DrawBody(int w, int bodyBottom, int headerH, double nowMs)
    {
        int viewH = Math.Max(1, bodyBottom - headerH);
        _d2d.PushClip(0, headerH, w, viewH);

        int padX = S(12);
        int padV = S(12);
        int contentW = w - padX * 2;
        int y = headerH + padV - _scrollY;
        int startY = y;

        var sessions = _snapshot.Sessions;
        if (sessions.Count == 0)
        {
            int emptyH = S(104);
            _d2d.FillAndStrokeRoundedRect(padX, y, contentW, emptyH, SF(12), Theme.BgCard, 1f, Theme.BorderSubtle, 1f);
            _d2d.DrawText(Strings.FlyoutEmptyTitle, padX + S(16), y + S(18), padX + contentW - S(16), y + S(42),
                12, Win32.FW_SEMIBOLD, Theme.TextPrimary, _dpi);
            _d2d.DrawWrappedText(Strings.FlyoutEmptySub, padX + S(16), y + S(46), padX + contentW - S(16), y + emptyH - S(12),
                11, Win32.FW_NORMAL, Theme.TextSecondary, _dpi);
            _contentTotalH = emptyH + padV * 2;
            _d2d.PopClip();
            return;
        }

        int cardH = S(CardHeightPx);
        int gap = S(CardGapPx);
        for (int i = 0; i < sessions.Count; i++)
        {
            float staggerOffsetY = 0f;
            if (!_settings.ReducedAnimations && _openAnimStartMs > 0 && i < MaxVisibleCards)
            {
                float cardEase = EaseOutCubic(_openAnimStartMs + i * CARD_STAGGER_STEP_MS, nowMs, OPEN_ANIM_MS);
                staggerOffsetY = SF(8) * (1f - cardEase);
            }

            y = DrawSessionCard(padX, y, contentW, cardH, sessions[i], headerH, bodyBottom, staggerOffsetY);
            if (i < sessions.Count - 1)
                y += gap;
        }

        _contentTotalH = (y - startY) + padV * 2;
        ClampScroll();

        if (_contentTotalH > viewH && viewH > 20)
        {
            float ratio = (float)viewH / _contentTotalH;
            int thumbH = Math.Max(S(24), (int)(viewH * ratio));
            int maxScroll = Math.Max(1, _contentTotalH - viewH);
            int thumbY = headerH + (int)((viewH - thumbH) * ((float)_scrollY / maxScroll));
            _d2d.FillRoundedRect(w - S(5), thumbY, S(3), thumbH, SF(1.5f), Theme.BorderStrong, 0.72f);
        }

        _d2d.PopClip();
    }

    private int DrawSessionCard(
        int x, int y, int w, int cardH, MediaSessionInfo s, int headerH, int bodyBottom, float staggerOffsetY)
    {
        bool isTarget = _snapshot.ActiveTarget != null &&
                        string.Equals(_snapshot.ActiveTarget.SessionId, s.SessionId, StringComparison.Ordinal);
        bool isLocked = _snapshot.IsManualLock && isTarget;

        string focusId = $"fly_focus_{s.SessionId}";
        float focusHov = GetHoverT(focusId);

        string? assignedOutId = _engine.GetSessionOutputDeviceId(s.SessionId);
        uint devColor = GetEffectiveDeviceColor(assignedOutId);

        uint tint = isLocked
            ? Theme.Amber
            : (s.IsEffectivelyPlaying ? Theme.Green : Theme.Accent);
        uint cardBg = isTarget
            ? Theme.BlendRgb(Theme.BgCardElevated, tint, (Theme.IsLight ? 0.06f : 0.11f) + 0.03f * focusHov)
            : Theme.BlendRgb(Theme.BgCard, Theme.BgCardElevated, focusHov * 0.65f);
        uint border = isTarget
            ? Theme.BlendRgb(tint, Theme.Accent, isLocked ? 0f : focusHov * 0.2f)
            : Theme.BlendRgb(Theme.BorderSubtle, Theme.Accent, focusHov * 0.55f);

        float drawY = y + staggerOffsetY;
        _d2d.FillAndStrokeRoundedRect(x, drawY, w, cardH, SF(12),
            cardBg, 1f, border, isTarget ? 0.90f : 1f, isTarget ? 1.35f : 1f);

        if (isTarget)
        {
            _d2d.PushClip(x + 1, drawY + 1, w - 2, cardH - 2);
            _d2d.FillRadialAura(x + S(32), drawY + S(28), S(96), S(56), tint, Theme.IsLight ? 0.08f : 0.14f);
            _d2d.PopClip();
            _d2d.FillRoundedRect(x + S(3), drawY + S(10), S(3), cardH - S(20), SF(1.5f), tint, 0.90f);
        }

        int pad = S(11);
        int icoBox = S(34);
        int icoX = x + pad + (isTarget ? S(3) : 0);
        int icoY = (int)MathF.Round(drawY + S(10));
        _d2d.FillAndStrokeRoundedRect(icoX, icoY, icoBox, icoBox, SF(9),
            Theme.BgControl, 0.85f,
            Theme.BlendRgb(Theme.BorderSubtle, tint, isTarget ? 0.45f : focusHov * 0.45f), 1f);

        int icoSz = S(20);
        DrawAppIconOrMonogram(s.AppKey, s.ExePath, s.DisplayName,
            icoX + (icoBox - icoSz) * 0.5f, icoY + (icoBox - icoSz) * 0.5f, icoSz, SF(6));

        int btnSz = S(26);
        int playSz = S(28);
        int btnY = (int)MathF.Round(drawY + S(13));
        int playY = btnY - (playSz - btnSz) / 2;
        int lockX = x + w - pad - btnSz;
        int nextX = lockX - S(4) - btnSz;
        int playX = nextX - S(4) - playSz;
        int prevX = playX - S(4) - btnSz;

        int textX = icoX + icoBox + S(10);
        int textRight = prevX - S(8);
        bool isWeb = s.IsBrowser || BrowserSiteScanner.ExtractBrowserKeyFromAppKey(s.AppKey) != null;
        int globeSz = isWeb ? S(11) : 0;
        int globeGap = isWeb ? S(4) : 0;
        int dotSz = S(7);
        int dotReserve = dotSz + S(5);
        int titleMeasured = _d2d.MeasureTextWidth(s.DisplayName, 12, Win32.FW_SEMIBOLD, _dpi) + S(4);
        int nameMaxRight = Math.Clamp(textX + titleMeasured, textX, Math.Max(textX, textRight - globeSz - globeGap - dotReserve));

        _d2d.DrawText(s.DisplayName, textX, drawY + S(9), textRight - globeSz - globeGap - dotReserve, drawY + S(27),
            12, Win32.FW_SEMIBOLD, Theme.BlendRgb(Theme.TextPrimary, Theme.Accent, focusHov), _dpi);

        int metaX = nameMaxRight + S(3);
        if (isWeb && metaX + globeSz <= textRight - dotReserve)
        {
            _d2d.DrawGlobeGlyph(metaX, drawY + SF(12.5f), globeSz, Theme.TextMuted, _dpi);
            metaX += globeSz + globeGap;
        }
        uint stDotColor = s.IsEffectivelyPlaying
            ? Theme.Green
            : (s.IsMuted || s.Volume <= 0.005f ? Theme.Red : Theme.TextMuted);
        if (metaX + dotSz <= textRight)
        {
            _d2d.FillCircle(metaX, drawY + SF(15f), dotSz, stDotColor);
        }

        _d2d.DrawText(s.FormattedMediaTitle, textX, drawY + S(28), textRight, drawY + S(46),
            11, Win32.FW_NORMAL, Theme.TextSecondary, _dpi);

        int botY = (int)MathF.Round(drawY + cardH - pad - S(28));
        if (y + cardH > headerH && y < bodyBottom)
        {
            var captured = s;
            RegisterScrollableHit(
                new Win32.RECT { Left = x, Top = y, Right = prevX - S(4), Bottom = y + S(52) },
                focusId,
                () =>
                {
                    _ = _engine.FocusSessionAsync(captured.SessionId);
                    Hide();
                },
                headerH,
                bodyBottom);

            DrawTransportBtn($"fly_prev_{s.SessionId}", 0, prevX, btnY, btnSz, accent: false, circular: false, headerH, bodyBottom,
                () => _ = _engine.DispatchMediaActionToSessionAsync(captured.SessionId, MediaActionKind.Previous));
            DrawTransportBtn($"fly_play_{s.SessionId}", captured.IsPlaying ? 2 : 1, playX, playY, playSz, accent: true, circular: true, headerH, bodyBottom,
                () => _ = _engine.DispatchMediaActionToSessionAsync(captured.SessionId, MediaActionKind.PlayPause));
            DrawTransportBtn($"fly_next_{s.SessionId}", 3, nextX, btnY, btnSz, accent: false, circular: false, headerH, bodyBottom,
                () => _ = _engine.DispatchMediaActionToSessionAsync(captured.SessionId, MediaActionKind.Next));
            DrawLockBtn($"fly_lock_{s.SessionId}", lockX, btnY, btnSz, isLocked, headerH, bodyBottom,
                () => _engine.ToggleManualLockOnActiveOrSelected(captured.SessionId));

            DrawControlCenterVolumeBar($"fly_vol_{s.SessionId}", captured,
                x + pad + (isTarget ? S(3) : 0), botY, w - pad * 2 - (isTarget ? S(3) : 0), S(28),
                devColor, headerH, bodyBottom);
        }

        return y + cardH;
    }

    private void DrawTransportBtn(
        string id, int kind, int x, int y, int sz, bool accent, bool circular,
        int headerH, int bodyBottom, Action onClick)
    {
        float hovT = GetHoverT(id);
        float radius = circular ? sz * 0.5f : SF(8);
        uint fg = accent ? Theme.OnAccent : Theme.TextPrimary;

        if (accent)
        {
            _d2d.FillLinearGradientRoundedRect(x, y, sz, sz, radius,
                Theme.BlendRgb(Theme.Accent, Theme.AccentHover, hovT * 0.6f), 1f, Theme.AccentHover, 1f);
            if (hovT > 0.01f)
                _d2d.DrawRoundedRect(x, y, sz, sz, radius, Theme.AccentHover, 1.2f, hovT);
        }
        else
        {
            uint bg = Theme.BlendRgb(Theme.BgControl, Theme.BgControlHover, hovT);
            uint border = Theme.BlendRgb(Theme.BorderStrong, Theme.Accent, hovT * 0.55f);
            _d2d.FillAndStrokeRoundedRect(x, y, sz, sz, radius, bg, 1f, border, 1f, 1f);
        }

        float cx = x + sz * 0.5f;
        float cy = y + sz * 0.5f;
        float r = sz * 0.18f;
        switch (kind)
        {
            case 0: // Previous
                float prevBarW = MathF.Max(1.8f, sz * 0.075f);
                _d2d.FillRoundedRect(cx - r * 0.9f, cy - r * 0.85f, prevBarW, r * 1.7f, 0.8f, fg);
                _d2d.FillTriangle(cx + r * 0.9f, cy - r * 0.85f, cx + r * 0.9f, cy + r * 0.85f, cx - r * 0.45f, cy, fg);
                break;
            case 1: // Play
                _d2d.FillTriangle(cx - r * 0.45f, cy - r * 1.05f, cx - r * 0.45f, cy + r * 1.05f, cx + r * 1.15f, cy, fg);
                break;
            case 2: // Pause
                float bw = MathF.Max(2.1f, sz * 0.088f);
                float bh = r * 1.9f;
                _d2d.FillRoundedRect(cx - bw * 1.45f, cy - bh * 0.5f, bw, bh, 0.9f, fg);
                _d2d.FillRoundedRect(cx + bw * 0.45f, cy - bh * 0.5f, bw, bh, 0.9f, fg);
                break;
            case 3: // Next
                _d2d.FillTriangle(cx - r * 0.9f, cy - r * 0.85f, cx - r * 0.9f, cy + r * 0.85f, cx + r * 0.45f, cy, fg);
                _d2d.FillRoundedRect(cx + r * 0.55f, cy - r * 0.85f, MathF.Max(1.8f, sz * 0.075f), r * 1.7f, 0.8f, fg);
                break;
        }

        RegisterScrollableHit(new Win32.RECT { Left = x, Top = y, Right = x + sz, Bottom = y + sz }, id, onClick, headerH, bodyBottom);
    }

    private void DrawLockBtn(string id, int x, int y, int sz, bool isLocked, int headerH, int bodyBottom, Action onClick)
    {
        float hovT = GetHoverT(id);
        uint bg = isLocked
            ? Theme.AmberSoft
            : Theme.BlendRgb(Theme.BgControl, Theme.BgControlHover, hovT);
        uint border = isLocked
            ? Theme.Amber
            : Theme.BlendRgb(Theme.BorderStrong, Theme.Accent, hovT * 0.55f);
        uint fg = isLocked
            ? Theme.Amber
            : Theme.BlendRgb(Theme.TextSecondary, Theme.TextPrimary, hovT);

        _d2d.FillAndStrokeRoundedRect(x, y, sz, sz, SF(8), bg, 1f, border, 1f, 1f);
        _d2d.DrawLockGlyph(x + sz * 0.5f, y + sz * 0.5f, sz, fg, _dpi);

        RegisterScrollableHit(new Win32.RECT { Left = x, Top = y, Right = x + sz, Bottom = y + sz }, id, onClick, headerH, bodyBottom);
    }

    private void DrawControlCenterVolumeBar(
        string idPrefix, MediaSessionInfo session,
        int x, int y, int w, int h, uint deviceRgb, int headerH, int bodyBottom)
    {
        string sessionId = session.SessionId;
        bool showOutBtn = _engine.IsPerAppAudioRoutingSupported &&
                          _outputDevices.Count > 1 &&
                          session.ProcessIds.Length > 0;
        int outSz = showOutBtn ? S(28) : 0;
        int outGap = showOutBtn ? S(6) : 0;
        int barW = Math.Max(S(80), w - outSz - outGap);
        int outX = x + barW + outGap;

        string sliderId = $"{idPrefix}_slider";
        string muteId = $"{idPrefix}_mute";
        bool isDraggingThis = string.Equals(_draggingSliderId, sliderId, StringComparison.Ordinal);
        float sliderHovT = isDraggingThis ? 1f : GetHoverT(sliderId);
        float muteHovT = GetHoverT(muteId);

        float targetVol = session.IsMuted ? 0f : Math.Clamp(session.Volume, 0f, 1f);
        float dispVol = GetAnimatedVolume(sessionId, targetVol, isDraggingThis, _dragPreviewVolume);

        float radius = SF(9);
        _d2d.FillAndStrokeRoundedRect(x, y, barW, h, radius,
            Theme.BgControl, 0.88f,
            Theme.BlendRgb(Theme.BorderSubtle, deviceRgb, sliderHovT), 0.72f + 0.28f * sliderHovT, 1f);

        int muteW = Math.Min(barW / 2, S(28));
        float fillW = dispVol <= 0.003f ? 0f : muteW + Math.Max(0, barW - muteW) * dispVol;
        if (fillW > 2f)
        {
            uint fillStart = (session.IsMuted && !isDraggingThis) ? Theme.Red : Theme.BlendRgb(Theme.BgControl, deviceRgb, 0.65f);
            uint fillEnd = (session.IsMuted && !isDraggingThis) ? Theme.Red : deviceRgb;
            _d2d.PushClip(x, y, MathF.Min(barW, fillW), h);
            _d2d.FillLinearGradientRoundedRect(x, y, barW, h, radius, fillStart, 0.92f, fillEnd, 0.96f);
            _d2d.PopClip();
        }
        uint speakerColor = session.IsMuted && !isDraggingThis
            ? Theme.Red
            : (dispVol > 0.14f && !Theme.IsLight ? 0xFFFFFFu : Theme.TextPrimary);
        if (muteHovT > 0.01f)
            _d2d.FillRoundedRect(x + S(2), y + S(2), muteW - S(4), h - S(4), SF(6), 0xFFFFFF, (Theme.IsLight ? 0.32f : 0.16f) * muteHovT);

        _d2d.DrawSpeakerGlyph(x + (muteW - S(18)) * 0.5f, y + (h - S(18)) * 0.5f, S(18), session.IsMuted && !isDraggingThis, speakerColor, _dpi);

        int dispPct = (int)MathF.Round(dispVol * 100f);
        string pctStr = Strings.FormatPercent(dispPct);
        uint pctFg = session.IsMuted && !isDraggingThis
            ? Theme.Red
            : (dispVol > 0.80f && !Theme.IsLight ? 0xFFFFFFu : Theme.TextPrimary);
        _d2d.DrawText(pctStr, x + muteW + S(4), y, x + barW - S(9), y + h, 10, Win32.FW_SEMIBOLD,
            pctFg, _dpi, align: D2DRenderer.TextAlign.Right);

        RegisterScrollableHit(
            new Win32.RECT { Left = x, Top = y, Right = x + muteW, Bottom = y + h },
            muteId,
            () => _ = _engine.ToggleSessionMuteAsync(sessionId),
            headerH,
            bodyBottom);

        int trackLeft = x + muteW;
        int trackW = Math.Max(S(24), barW - muteW);
        int hitTop = Math.Max(headerH, y);
        int hitBottom = Math.Min(bodyBottom, y + h);
        if (hitBottom > hitTop)
        {
            _sliderRegions.Add(new SliderRegion(
                new Win32.RECT { Left = trackLeft, Top = hitTop, Right = x + barW, Bottom = hitBottom },
                sliderId,
                sessionId,
                trackLeft,
                trackW));
        }

        if (showOutBtn)
        {
            string outId = $"{idPrefix}_out";
            bool isDrawerForThis = _openDrawer == FlyoutDrawer.AppOutputDevice &&
                                   string.Equals(_drawerTargetSessionId, sessionId, StringComparison.Ordinal);
            float outHovT = isDrawerForThis ? 1f : GetHoverT(outId);
            uint outBg = Theme.BlendRgb(Theme.BlendRgb(Theme.BgControl, deviceRgb, Theme.IsLight ? 0.10f : 0.14f), deviceRgb, isDrawerForThis ? 1f : outHovT * 0.85f);
            uint outBorder = Theme.BlendRgb(Theme.BorderStrong, deviceRgb, 0.72f + 0.28f * outHovT);
            uint outFg = (isDrawerForThis || outHovT > 0.5f) ? Theme.OnAccent : deviceRgb;

            _d2d.FillAndStrokeRoundedRect(outX, y, outSz, h, SF(8), outBg, 1f, outBorder, 1f, 1.1f);
            _d2d.DrawAudioSwitchGlyph(outX + outSz * 0.5f, y + h * 0.5f, outSz * 0.78f, outFg, _dpi);

            string appName = session.DisplayName;
            string? browserName = ProcessResolver.ResolveBrowserDisplayName(session);
            RegisterScrollableHit(
                new Win32.RECT { Left = outX, Top = y, Right = outX + outSz, Bottom = y + h },
                outId,
                () =>
                {
                    if (_openDrawer == FlyoutDrawer.AppOutputDevice &&
                        string.Equals(_drawerTargetSessionId, sessionId, StringComparison.Ordinal))
                    {
                        OpenOrToggleDrawer(FlyoutDrawer.None);
                    }
                    else
                    {
                        _drawerTargetSessionId = sessionId;
                        _drawerTargetAppName = appName;
                        _drawerTargetBrowserName = browserName;
                        _outputDevices = _engine.GetOutputDevices();
                        RebuildOutputDeviceColorCache();
                        OpenOrToggleDrawer(FlyoutDrawer.AppOutputDevice);
                    }
                },
                headerH,
                bodyBottom);
        }
    }

    private void RegisterScrollableHit(Win32.RECT bounds, string id, Action onClick, int headerH, int bodyBottom = int.MaxValue)
    {
        if (bounds.Top < headerH)
            bounds.Top = headerH;
        if (bounds.Bottom > bodyBottom)
            bounds.Bottom = bodyBottom;
        if (bounds.Bottom > bounds.Top && bounds.Right > bounds.Left)
            _hitRegions.Add(new HitRegion(bounds, id, onClick));
    }

    private void DrawDrawer(int w, int drawerY, int drawerH, double nowMs)
    {
        float drawerEase = (!_settings.ReducedAnimations && _drawerAnimStartMs > 0)
            ? EaseOutCubic(_drawerAnimStartMs, nowMs, DRAWER_ANIM_MS)
            : 1f;

        _d2d.PushClip(0, drawerY, w, drawerH);
        _d2d.FillRect(0, drawerY, w, drawerH, Theme.BgWindow);
        _d2d.FillRect(0, drawerY, w, 1, Theme.BorderSubtle, 0.85f);

        float offsetY = SF(8) * (1f - drawerEase);
        int padX = S(12);

        if (_openDrawer == FlyoutDrawer.AudioDevices)
        {
            if (_outputDevices.Count == 0)
            {
                _d2d.DrawText(Strings.FlyoutNoAudioDevices, padX, drawerY + offsetY, w - padX, drawerY + drawerH + offsetY,
                    11, Win32.FW_NORMAL, Theme.TextSecondary, _dpi, centered: true, alpha: drawerEase);
                _d2d.PopClip();
                return;
            }

            int rowH = S(30);
            int rowGap = S(4);
            int maxShow = Math.Min(_outputDevices.Count, 5);
            float ry = drawerY + S(7) + offsetY;
            for (int i = 0; i < maxShow; i++)
            {
                var dev = _outputDevices[i];
                string id = $"fly_dev_{i}";
                float hovT = GetHoverT(id);
                uint devColor = GetEffectiveDeviceColor(dev.Id);
                uint bg = dev.IsDefault
                    ? Theme.BlendRgb(Theme.BgCardElevated, devColor, (Theme.IsLight ? 0.12f : 0.16f) + 0.05f * hovT)
                    : Theme.BlendRgb(Theme.BgCard, Theme.BgControlHover, hovT);
                uint border = dev.IsDefault ? devColor : Theme.BlendRgb(Theme.BorderSubtle, devColor, hovT * 0.55f);

                _d2d.FillAndStrokeRoundedRect(padX, ry, w - padX * 2, rowH, SF(8),
                    bg, drawerEase, border, drawerEase * (dev.IsDefault ? 0.9f : 1f), 1f);

                int dotSz = S(7);
                float dotX = padX + S(10);
                float dotY = ry + (rowH - dotSz) * 0.5f;
                if (dev.IsDefault)
                    _d2d.FillCircle(dotX, dotY, dotSz, devColor, drawerEase);
                else
                    _d2d.StrokeCircle(dotX, dotY, dotSz, devColor, MathF.Max(1.1f, SF(1.1f)), drawerEase);

                _d2d.DrawText(dev.Name, dotX + dotSz + S(9), ry, w - padX - S(10), ry + rowH,
                    11, dev.IsDefault ? Win32.FW_SEMIBOLD : Win32.FW_NORMAL,
                    dev.IsDefault ? devColor : Theme.TextPrimary, _dpi, alpha: drawerEase);

                var capturedDev = dev;
                int hitTop = (int)MathF.Round(ry);
                _hitRegions.Add(new HitRegion(
                    new Win32.RECT { Left = padX, Top = hitTop, Right = w - padX, Bottom = hitTop + rowH },
                    id,
                    () =>
                    {
                        _engine.SelectOutputDevice(capturedDev.Id, capturedDev.Name);
                        OpenOrToggleDrawer(FlyoutDrawer.None);
                        _outputDevices = _engine.GetOutputDevices();
                        RebuildOutputDeviceColorCache();
                    }));

                ry += rowH + rowGap;
            }
        }
        else if (_openDrawer == FlyoutDrawer.SleepTimer)
        {
            int[] presets = [15, 30, 45, 60];
            bool hasActive = _engine.GetSleepTimerRemaining().HasValue;
            int count = presets.Length + (hasActive ? 1 : 0);
            int gap = S(6);
            int availW = w - padX * 2;
            int pillW = (availW - gap * (count - 1)) / count;
            int pillH = S(30);
            float pillY = drawerY + (drawerH - pillH) * 0.5f + offsetY;
            int px = padX;

            foreach (int mins in presets)
            {
                string id = $"fly_sleep_set_{mins}";
                float hovT = GetHoverT(id);
                uint bg = Theme.BlendRgb(Theme.BgCard, Theme.BgControlHover, hovT);
                uint border = Theme.BlendRgb(Theme.BorderStrong, Theme.Accent, hovT);
                uint fg = Theme.BlendRgb(Theme.TextPrimary, Theme.Accent, hovT);

                _d2d.FillAndStrokeRoundedRect(px, pillY, pillW, pillH, SF(8), bg, drawerEase, border, drawerEase);
                _d2d.DrawText(Strings.FlyoutSleepMinutes(mins), px + S(2), pillY, px + pillW - S(2), pillY + pillH,
                    10, Win32.FW_SEMIBOLD, fg, _dpi, centered: true, alpha: drawerEase);

                int capturedMins = mins;
                int hitY = (int)MathF.Round(pillY);
                _hitRegions.Add(new HitRegion(
                    new Win32.RECT { Left = px, Top = hitY, Right = px + pillW, Bottom = hitY + pillH },
                    id,
                    () =>
                    {
                        _engine.StartSleepTimer(capturedMins);
                        OpenOrToggleDrawer(FlyoutDrawer.None);
                    }));
                px += pillW + gap;
            }

            if (hasActive)
            {
                const string cancelId = "fly_sleep_cancel";
                float hovT = GetHoverT(cancelId);
                uint bg = Theme.BlendRgb(Theme.BgCard, Theme.RedSoft, hovT);
                int hitY = (int)MathF.Round(pillY);

                _d2d.FillAndStrokeRoundedRect(px, pillY, pillW, pillH, SF(8), bg, drawerEase, Theme.Red, drawerEase);
                _d2d.DrawText(Strings.FlyoutSleepCancel, px + S(2), pillY, px + pillW - S(2), pillY + pillH,
                    10, Win32.FW_SEMIBOLD, Theme.Red, _dpi, centered: true, alpha: drawerEase);
                _hitRegions.Add(new HitRegion(
                    new Win32.RECT { Left = px, Top = hitY, Right = px + pillW, Bottom = hitY + pillH },
                    cancelId,
                    () =>
                    {
                        _engine.CancelSleepTimer();
                        OpenOrToggleDrawer(FlyoutDrawer.None);
                    }));
            }
        }
        else if (_openDrawer == FlyoutDrawer.AppOutputDevice)
        {
            string targetSid = _drawerTargetSessionId ?? string.Empty;
            string headerTitle = Strings.FlyoutAppAudioTitle(_drawerTargetAppName ?? Strings.FlyoutAudioDeviceFallback);
            _d2d.DrawText(headerTitle, padX + S(2), drawerY + S(5) + offsetY, w - padX, drawerY + S(24) + offsetY,
                10, Win32.FW_BOLD, Theme.TextMuted, _dpi, alpha: drawerEase);

            string? assignedId = _engine.GetSessionOutputDeviceId(targetSid);
            int rowH = S(28);
            int rowGap = S(4);
            float ry = drawerY + S(26) + offsetY;
            int totalRows = Math.Min(_outputDevices.Count + 1, 6);

            void DrawAppOutRow(string id, string label, bool selected, uint rowColor, Action onPick)
            {
                float hovT = GetHoverT(id);
                uint bg = selected
                    ? Theme.BlendRgb(Theme.BgCardElevated, rowColor, (Theme.IsLight ? 0.12f : 0.16f) + 0.05f * hovT)
                    : Theme.BlendRgb(Theme.BgCard, Theme.BgControlHover, hovT);
                uint border = selected ? rowColor : Theme.BlendRgb(Theme.BorderSubtle, rowColor, hovT * 0.55f);

                _d2d.FillAndStrokeRoundedRect(padX, ry, w - padX * 2, rowH, SF(8), bg, drawerEase, border, drawerEase);

                int dotSz = S(7);
                float dotX = padX + S(10);
                float dotY = ry + (rowH - dotSz) * 0.5f;
                if (selected)
                    _d2d.FillCircle(dotX, dotY, dotSz, rowColor, drawerEase);
                else
                    _d2d.StrokeCircle(dotX, dotY, dotSz, rowColor, MathF.Max(1.1f, SF(1.1f)), drawerEase);

                _d2d.DrawText(label, dotX + dotSz + S(9), ry, w - padX - S(10), ry + rowH,
                    11, selected ? Win32.FW_SEMIBOLD : Win32.FW_NORMAL,
                    selected ? rowColor : Theme.TextPrimary, _dpi, alpha: drawerEase);

                int hitY = (int)MathF.Round(ry);
                _hitRegions.Add(new HitRegion(
                    new Win32.RECT { Left = padX, Top = hitY, Right = w - padX, Bottom = hitY + rowH },
                    id,
                    onPick));
                ry += rowH + rowGap;
            }

            DrawAppOutRow("fly_appout_default", Strings.AudioDeviceSystemDefault, assignedId == null, GetEffectiveDeviceColor(null), () =>
            {
                _engine.SetSessionOutputDevice(targetSid, null, Strings.AudioDeviceSystemDefault);
                OpenOrToggleDrawer(FlyoutDrawer.None);
            });

            for (int i = 0; i < Math.Min(_outputDevices.Count, totalRows - 1); i++)
            {
                var dev = _outputDevices[i];
                bool selected = assignedId != null && string.Equals(assignedId, dev.Id, StringComparison.OrdinalIgnoreCase);
                var capturedDev = dev;
                DrawAppOutRow($"fly_appout_{i}", dev.Name, selected, GetEffectiveDeviceColor(dev.Id), () =>
                {
                    _engine.SetSessionOutputDevice(targetSid, capturedDev.Id, capturedDev.Name);
                    OpenOrToggleDrawer(FlyoutDrawer.None);
                });
            }

            if (ShouldShowBrowserOutputNotice() && _drawerTargetBrowserName != null)
            {
                float noticeY = ry + S(2);
                int noticeH = S(66);
                int noticeW = w - padX * 2;
                _d2d.FillAndStrokeRoundedRect(padX, noticeY, noticeW, noticeH, SF(8),
                    Theme.AmberSoft, drawerEase, Theme.Amber, 0.55f * drawerEase);

                const string dismissId = "fly_appout_dismiss_notice";
                string dismissText = Strings.BtnDontShowAgain;
                int dismissW = Math.Clamp(_d2d.MeasureTextWidth(dismissText, 9, Win32.FW_SEMIBOLD, _dpi) + S(16), S(88), noticeW - S(16));
                int dismissH = S(19);
                int dismissX = padX + noticeW - S(8) - dismissW;
                float dismissY = noticeY + noticeH - S(6) - dismissH;

                string noticeText = Strings.BrowserOutputScopeNotice(_drawerTargetBrowserName);
                _d2d.DrawWrappedText(noticeText, padX + S(10), noticeY + S(5), padX + noticeW - S(10), dismissY - S(4),
                    9, Win32.FW_MEDIUM, Theme.TextPrimary, _dpi, alpha: drawerEase);
                float dismissHov = GetHoverT(dismissId);

                _d2d.FillRoundedRect(dismissX, dismissY, dismissW, dismissH, SF(5),
                    Theme.Amber, (0.12f + 0.14f * dismissHov) * drawerEase);
                _d2d.DrawText(dismissText, dismissX, dismissY, dismissX + dismissW, dismissY + dismissH,
                    9, Win32.FW_SEMIBOLD, Theme.Amber, _dpi, centered: true, alpha: drawerEase);

                int hitDismissY = (int)MathF.Round(dismissY);
                _hitRegions.Add(new HitRegion(
                    new Win32.RECT { Left = dismissX, Top = hitDismissY, Right = dismissX + dismissW, Bottom = hitDismissY + dismissH },
                    dismissId,
                    () =>
                    {
                        _stateStore.UpdateSettings(s => s.SuppressBrowserOutputNotice = true);
                        _settings = _stateStore.GetSettingsSnapshot();
                        PositionAndResize();
                    }));
            }
        }

        _d2d.PopClip();
    }

    private void DrawFooter(int w, int footerY, int footerH)
    {
        _d2d.FillRect(0, footerY, w, 1, Theme.BorderSubtle, 0.85f);

        int padX = S(12);
        int btnH = S(30);
        int btnY = footerY + (footerH - btnH) / 2;

        var sleepRem = _engine.GetSleepTimerRemaining();
        string sleepLabel = sleepRem.HasValue
            ? $"{(int)sleepRem.Value.TotalMinutes:D2}:{sleepRem.Value.Seconds:D2}"
            : Strings.FlyoutSleepLabel;

        int sleepW = Math.Clamp(_d2d.MeasureTextWidth(sleepLabel, 10, Win32.FW_SEMIBOLD, _dpi) + S(34), S(82), S(116));
        int sleepX = w - padX - sleepW;
        int devX = padX;
        int devW = Math.Max(S(110), sleepX - S(8) - devX);

        string activeDevName = Strings.FlyoutAudioDeviceFallback;
        uint activeDevColor = Theme.Accent;
        foreach (var dev in _outputDevices)
        {
            if (dev.IsDefault)
            {
                activeDevName = dev.Name;
                activeDevColor = GetEffectiveDeviceColor(dev.Id);
                break;
            }
        }

        const string devBtnId = "fly_footer_audio";
        bool devOpen = _openDrawer == FlyoutDrawer.AudioDevices;
        float devHovT = devOpen ? 1f : GetHoverT(devBtnId);
        uint devBg = devOpen
            ? Theme.BlendRgb(Theme.BgCardElevated, activeDevColor, Theme.IsLight ? 0.14f : 0.20f)
            : Theme.BlendRgb(Theme.BgCard, Theme.BgControlHover, devHovT);
        uint devBorder = Theme.BlendRgb(Theme.BorderSubtle, activeDevColor, devOpen ? 0.9f : devHovT * 0.65f);
        uint devFg = devOpen ? activeDevColor : Theme.TextPrimary;

        _d2d.FillAndStrokeRoundedRect(devX, btnY, devW, btnH, btnH * 0.5f, devBg, 1f, devBorder, 1f);
        _d2d.DrawSpeakerGlyph(devX + S(10), btnY + (btnH - S(16)) * 0.5f, S(16), isMuted: false, devOpen ? activeDevColor : devFg, _dpi);
        _d2d.DrawText(activeDevName, devX + S(30), btnY, devX + devW - S(22), btnY + btnH,
            10, Win32.FW_SEMIBOLD, devFg, _dpi);
        _d2d.DrawDropdownChevron(devX + devW - S(13), btnY + btnH * 0.5f, pointingUp: !devOpen,
            devOpen ? activeDevColor : Theme.TextSecondary, _dpi);

        _hitRegions.Add(new HitRegion(
            new Win32.RECT { Left = devX, Top = btnY, Right = devX + devW, Bottom = btnY + btnH },
            devBtnId,
            () =>
            {
                if (_openDrawer != FlyoutDrawer.AudioDevices)
                {
                    _outputDevices = _engine.GetOutputDevices();
                    RebuildOutputDeviceColorCache();
                }
                OpenOrToggleDrawer(FlyoutDrawer.AudioDevices);
            }));

        const string sleepBtnId = "fly_footer_sleep";
        bool sleepOpen = _openDrawer == FlyoutDrawer.SleepTimer;
        bool sleepActive = sleepRem.HasValue;
        float sleepHovT = (sleepOpen || sleepActive) ? 1f : GetHoverT(sleepBtnId);
        uint sleepBg = sleepActive
            ? Theme.AmberSoft
            : (sleepOpen ? Theme.AccentSoft : Theme.BlendRgb(Theme.BgCard, Theme.BgControlHover, sleepHovT));
        uint sleepBorder = sleepActive
            ? Theme.Amber
            : (sleepOpen ? Theme.Accent : Theme.BlendRgb(Theme.BorderSubtle, Theme.Accent, sleepHovT * 0.6f));
        uint sleepFg = sleepActive ? Theme.Amber : (sleepOpen ? Theme.Accent : Theme.BlendRgb(Theme.TextSecondary, Theme.TextPrimary, sleepHovT));

        _d2d.FillAndStrokeRoundedRect(sleepX, btnY, sleepW, btnH, btnH * 0.5f, sleepBg, 1f, sleepBorder, 1f);
        _d2d.DrawClockGlyph(sleepX + S(10), btnY + (btnH - S(13)) * 0.5f, S(13), sleepFg, _dpi);
        _d2d.DrawText(sleepLabel, sleepX + S(25), btnY, sleepX + sleepW - S(8), btnY + btnH,
            10, Win32.FW_SEMIBOLD, sleepFg, _dpi, centered: true);

        _hitRegions.Add(new HitRegion(
            new Win32.RECT { Left = sleepX, Top = btnY, Right = sleepX + sleepW, Bottom = btnY + btnH },
            sleepBtnId,
            () => OpenOrToggleDrawer(FlyoutDrawer.SleepTimer)));
    }

    private void DrawAppIconOrMonogram(
        string? appKey, string? exePath, string? displayName,
        float x, float y, float size, float radius)
    {
        nint hIcon = _iconRenderer.GetIconForAppNonBlocking(appKey, exePath);
        if (hIcon != 0 && _d2d.DrawIcon(hIcon, x, y, size))
            return;

        _d2d.FillAndStrokeRoundedRect(x, y, size, size, radius, Theme.AccentSoft, 1f, Theme.Accent, 0.42f);

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.StateChanged -= OnStateChanged;
        _engine.SleepTimerChanged -= OnSleepTimerChanged;
        _stateStore.StateChanged -= OnStoreChanged;
        _iconRenderer.IconsUpdated -= OnIconsUpdated;

        if (_hwnd != 0)
        {
            StopUiAnimTimer();
            Win32.KillTimer(_hwnd, TIMER_IDLE_RELEASE);
            Win32.DestroyWindow(_hwnd);
            _hwnd = 0;
        }
        _d2d.Dispose();
        if (ReferenceEquals(_instance, this))
            _instance = null;
    }
}
