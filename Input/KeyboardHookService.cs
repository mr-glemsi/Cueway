using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cueway.Config;
using Cueway.Interop;
using Cueway.Routing;

namespace Cueway.Input;

internal sealed unsafe class KeyboardHookService : IDisposable
{
    private sealed record AppHotkeyEntry(HotkeyBinding Binding, string AppKey);

    private sealed record HotkeySnapshot(
        HotkeyBinding ToggleWindow,
        HotkeyBinding ManualLock,
        HotkeyBinding PlayPause,
        HotkeyBinding PromotePlaying,
        HotkeyBinding VolumeUp,
        HotkeyBinding VolumeDown,
        HotkeyBinding VolumeMute,
        HotkeyBinding CycleAudioDevice,
        AppHotkeyEntry[] AppHotkeys);

    private const nuint TIMER_WATCHDOG = 9001;
    private const nuint TIMER_LONG_PRESS = 9002;
    private const nuint TIMER_DOUBLE_TAP = 9003;
    private const uint DOUBLE_TAP_WINDOW_MS = 260;

    private static KeyboardHookService? _instance;

    private readonly nint _hwndHost;
    private readonly StateStore _stateStore;
    private readonly MediaRouterEngine _engine;

    private nint _hookHandle;
    private long _lastHookRefreshTick;
    private long _lastMediaKeyActivityTick;

    // Key hold & double-tap scrubbing state (accessed only on UI message loop thread)
    private uint _heldMediaVk;
    private bool _heldIsSecondary;
    private bool _heldIsVideo;
    private long _heldStartTick;
    private bool _longPressTriggered;

    private uint _pendingTapVk;
    private bool _pendingTapSecondary;
    private long _pendingTapTick;

    private bool _playPauseHeld;
    private long _lastPlayPauseDownTick;
    private bool _stopHeld;
    private long _lastStopDownTick;
    private readonly HashSet<uint> _heldCustomVks = [];
    private readonly HashSet<uint> _swallowedCaptureVks = [];
    private Action<string?>? _onCaptureResult;
    private Action<string>? _onCapturePreview;
    private Action? _onCaptureNeedsModifier;
    private bool _captureAllowAnySingleKey;
    private bool _disposed;

    private volatile bool _mediaHookEnabled = true;
    private volatile bool _timelineScrubbingEnabled = true;
    private volatile int _longPressThresholdMs = 400;
    private volatile int _volumeStepPercent = 5;
    private long _lastVolHotkeyTick;
    private volatile HotkeySnapshot _hotkeys = new(default, default, default, default, default, default, default, default, []);

    public event Action? ToggleWindowRequested;
    public bool IsCapturingHotkey => _onCaptureResult != null;

    public KeyboardHookService(nint hwndHost, StateStore stateStore, MediaRouterEngine engine)
    {
        _hwndHost = hwndHost;
        _stateStore = stateStore;
        _engine = engine;
        _instance = this;
        SyncCachedSettings();
        _stateStore.StateChanged += SyncCachedSettings;
    }

    private void SyncCachedSettings()
    {
        var s = _stateStore.GetSettingsSnapshot();
        var apps = _stateStore.GetPriorityAppsSnapshot();

        _mediaHookEnabled = s.MediaHookEnabled;
        _timelineScrubbingEnabled = s.TimelineScrubbingEnabled;
        _longPressThresholdMs = s.LongPressThresholdMs;
        _volumeStepPercent = s.VolumeStepPercent;

        HotkeyHelper.TryParse(s.HotkeyToggleWindow, out var toggleWin);
        HotkeyHelper.TryParse(s.HotkeyManualLock, out var manualLock);
        HotkeyHelper.TryParse(s.HotkeyPlayPause, out var playPause, allowAnySingleKey: true);
        HotkeyHelper.TryParse(s.HotkeyPromotePlaying, out var promotePlaying, allowAnySingleKey: true);
        HotkeyHelper.TryParse(s.HotkeyVolumeUp, out var volUp);
        HotkeyHelper.TryParse(s.HotkeyVolumeDown, out var volDown);
        HotkeyHelper.TryParse(s.HotkeyVolumeMute, out var volMute);
        HotkeyHelper.TryParse(s.HotkeyCycleAudioDevice, out var cycleAudioDevice);

        var appEntries = new List<AppHotkeyEntry>();
        foreach (var app in apps)
        {
            if (HotkeyHelper.TryParse(app.Hotkey, out var appBinding))
                appEntries.Add(new AppHotkeyEntry(appBinding, app.AppKey));
        }

        _hotkeys = new HotkeySnapshot(toggleWin, manualLock, playPause, promotePlaying, volUp, volDown, volMute, cycleAudioDevice, [.. appEntries]);
    }

    public void BeginHotkeyCapture(
        Action<string?> onResult,
        Action<string>? onPreview = null,
        Action? onNeedsModifier = null,
        bool allowAnySingleKey = false)
    {
        _onCaptureResult = onResult;
        _onCapturePreview = onPreview;
        _onCaptureNeedsModifier = onNeedsModifier;
        _captureAllowAnySingleKey = allowAnySingleKey;
        _heldCustomVks.Clear();
    }

    public void CancelHotkeyCapture()
    {
        _onCaptureResult = null;
        _onCapturePreview = null;
        _onCaptureNeedsModifier = null;
        _captureAllowAnySingleKey = false;
    }

    private void FinishHotkeyCapture(string? result)
    {
        var cb = _onCaptureResult;
        _onCaptureResult = null;
        _onCapturePreview = null;
        _onCaptureNeedsModifier = null;
        _captureAllowAnySingleKey = false;
        cb?.Invoke(result);
    }

    public void ForceRefreshHook() => EnsureHookInstalled(forceSwap: true);

    public void Start()
    {
        EnsureHookInstalled(forceSwap: false);
        Win32.SetTimer(_hwndHost, TIMER_WATCHDOG, 60_000, 0);
    }

    public void OnSystemWakeOrUnlock()
    {
        EnsureHookInstalled(forceSwap: true);
    }

    public void HandleTimer(nuint timerId)
    {
        if (_disposed) return;

        switch (timerId)
        {
            case TIMER_WATCHDOG:
                long now = Environment.TickCount64;
                // Periodically refresh hook cleanly when idle (>180s since last refresh)
                if (_hookHandle == 0 ||
                    (now - _lastHookRefreshTick >= 180_000 && now - _lastMediaKeyActivityTick >= 3_000 && _heldMediaVk == 0))
                {
                    EnsureHookInstalled(forceSwap: true);
                }
                break;

            case TIMER_LONG_PRESS:
                Win32.KillTimer(_hwndHost, TIMER_LONG_PRESS);
                if (_heldMediaVk != 0 && _heldIsVideo && !_heldIsSecondary)
                {
                    _longPressTriggered = true;
                    ClearPendingTap();
                    var action = _heldMediaVk == Win32.VK_MEDIA_NEXT_TRACK
                        ? MediaActionKind.SeekForward
                        : MediaActionKind.SeekBackward;
                    _ = _engine.DispatchMediaActionAsync(action, secondary: false);
                }
                break;

            case TIMER_DOUBLE_TAP:
                Win32.KillTimer(_hwndHost, TIMER_DOUBLE_TAP);
                if (_pendingTapVk != 0)
                {
                    uint vk = _pendingTapVk;
                    bool sec = _pendingTapSecondary;
                    _pendingTapVk = 0;
                    var action = vk == Win32.VK_MEDIA_NEXT_TRACK
                        ? MediaActionKind.Next
                        : MediaActionKind.Previous;
                    _ = _engine.DispatchMediaActionAsync(action, sec);
                }
                break;
        }
    }

    private void ClearPendingTap()
    {
        _pendingTapVk = 0;
        Win32.KillTimer(_hwndHost, TIMER_DOUBLE_TAP);
    }

    private void EnsureHookInstalled(bool forceSwap)
    {
        if (_disposed) return;
        if (_hookHandle != 0 && !forceSwap) return;

        nint hMod = Win32.GetModuleHandleW(null);
        // Install the replacement hook BEFORE removing the old one so no keypress falls through
        nint newHook = Win32.SetWindowsHookExW(Win32.WH_KEYBOARD_LL, &LowLevelKeyboardProc, hMod, 0);
        if (newHook != 0)
        {
            nint oldHook = _hookHandle;
            _hookHandle = newHook;
            _lastHookRefreshTick = Environment.TickCount64;
            if (oldHook != 0)
                Win32.UnhookWindowsHookEx(oldHook);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint LowLevelKeyboardProc(int nCode, nuint wParam, nint lParam)
    {
        var inst = _instance;
        if (nCode >= 0 && inst != null && !inst._disposed)
        {
            var kb = *(Win32.KBDLLHOOKSTRUCT*)lParam;
            bool isInjected = (kb.flags & Win32.LLKHF_INJECTED) != 0;
            bool isMediaKey = kb.vkCode is Win32.VK_MEDIA_PLAY_PAUSE
                or Win32.VK_MEDIA_NEXT_TRACK
                or Win32.VK_MEDIA_PREV_TRACK
                or Win32.VK_MEDIA_STOP;

            // Wireless/USB/Bluetooth headsets often deliver VK_MEDIA_* via Windows hidserv/AVRCP
            // with LLKHF_INJECTED set, and laptop Fn layers/macros can inject single-key hotkeys.
            // Because Cueway uses SMTC/IPC (never SendInput), allow injected events during capture
            // and for the PlayPause shortcut while keeping the guard for other shortcuts.
            bool allowInjected = isMediaKey ||
                                 inst.IsCapturingHotkey ||
                                 (!inst._hotkeys.PlayPause.IsEmpty && inst._hotkeys.PlayPause.VkCode == kb.vkCode) ||
                                 (!inst._hotkeys.PromotePlaying.IsEmpty && inst._hotkeys.PromotePlaying.VkCode == kb.vkCode);

            if (!isInjected || allowInjected)
            {
                bool isDown = wParam == Win32.WM_KEYDOWN || wParam == Win32.WM_SYSKEYDOWN;
                bool isUp = wParam == Win32.WM_KEYUP || wParam == Win32.WM_SYSKEYUP;

                if (inst.ProcessKey(kb.vkCode, isDown, isUp, isInjected))
                    return 1; // Swallow event so Windows default handler never runs
            }
        }
        return Win32.CallNextHookEx(inst?._hookHandle ?? 0, nCode, wParam, lParam);
    }

    private bool ProcessKey(uint vkCode, bool isDown, bool isUp, bool isInjected)
    {
        if (isUp && _swallowedCaptureVks.Remove(vkCode))
            return true;

        if (_onCaptureResult != null && !HotkeyHelper.IsMediaVk(vkCode))
        {
            if (HotkeyHelper.IsModifierVk(vkCode))
            {
                HotkeyModifiers liveMods = GetModifiersForTransition(vkCode, isDown);
                _onCapturePreview?.Invoke(HotkeyHelper.FormatModifierPreview(liveMods));
                return false;
            }

            if (isDown)
            {
                HotkeyModifiers mods = GetCurrentModifiers();
                if (mods == HotkeyModifiers.Alt && (vkCode == Win32.VK_TAB || vkCode == Win32.VK_F1 + 3))
                    return false;

                _swallowedCaptureVks.Add(vkCode);

                if (vkCode == Win32.VK_ESCAPE && mods == HotkeyModifiers.None)
                {
                    FinishHotkeyCapture(null);
                    return true;
                }

                if ((vkCode == Win32.VK_BACK || vkCode == Win32.VK_DELETE) && mods == HotkeyModifiers.None)
                {
                    FinishHotkeyCapture(string.Empty);
                    return true;
                }

                if (HotkeyHelper.TryCreate(mods, vkCode, out var captured, out bool needsModifier, _captureAllowAnySingleKey))
                {
                    FinishHotkeyCapture(captured.NormalizedText);
                }
                else if (needsModifier)
                {
                    _onCaptureNeedsModifier?.Invoke();
                }
                return true;
            }

            return false;
        }

        if (!HotkeyHelper.IsMediaVk(vkCode))
        {
            if (isUp && _heldCustomVks.Remove(vkCode))
                return true;

            if (isDown)
            {
                var snap = _hotkeys;
                HotkeyModifiers mods = GetCurrentModifiers();

                if (!snap.PlayPause.IsEmpty && snap.PlayPause.VkCode == vkCode && snap.PlayPause.Modifiers == mods)
                {
                    if (_heldCustomVks.Add(vkCode))
                        _ = Task.Run(() => _engine.DispatchMediaActionAsync(MediaActionKind.PlayPause, secondary: false));
                    return true;
                }

                if (!snap.PromotePlaying.IsEmpty && snap.PromotePlaying.VkCode == vkCode && snap.PromotePlaying.Modifiers == mods)
                {
                    if (_heldCustomVks.Add(vkCode))
                        _ = Task.Run(() => _engine.PromoteNowPlayingToTop());
                    return true;
                }

                if (!isInjected)
                {
                    if (!snap.ToggleWindow.IsEmpty && snap.ToggleWindow.VkCode == vkCode && snap.ToggleWindow.Modifiers == mods)
                    {
                        if (_heldCustomVks.Add(vkCode))
                            ToggleWindowRequested?.Invoke();
                        return true;
                    }

                    if (!snap.ManualLock.IsEmpty && snap.ManualLock.VkCode == vkCode && snap.ManualLock.Modifiers == mods)
                    {
                        if (_heldCustomVks.Add(vkCode))
                            _ = Task.Run(() => _engine.ToggleManualLockOnActiveOrSelected());
                        return true;
                    }

                    if (!snap.VolumeUp.IsEmpty && snap.VolumeUp.VkCode == vkCode && snap.VolumeUp.Modifiers == mods)
                    {
                        bool firstPress = _heldCustomVks.Add(vkCode);
                        long nowTick = Environment.TickCount64;
                        if (firstPress || nowTick - _lastVolHotkeyTick >= 110)
                        {
                            _lastVolHotkeyTick = nowTick;
                            float step = _volumeStepPercent / 100f;
                            _ = Task.Run(() => _engine.AdjustActiveTargetVolume(step));
                        }
                        return true;
                    }

                    if (!snap.VolumeDown.IsEmpty && snap.VolumeDown.VkCode == vkCode && snap.VolumeDown.Modifiers == mods)
                    {
                        bool firstPress = _heldCustomVks.Add(vkCode);
                        long nowTick = Environment.TickCount64;
                        if (firstPress || nowTick - _lastVolHotkeyTick >= 110)
                        {
                            _lastVolHotkeyTick = nowTick;
                            float step = -(_volumeStepPercent / 100f);
                            _ = Task.Run(() => _engine.AdjustActiveTargetVolume(step));
                        }
                        return true;
                    }

                    if (!snap.VolumeMute.IsEmpty && snap.VolumeMute.VkCode == vkCode && snap.VolumeMute.Modifiers == mods)
                    {
                        if (_heldCustomVks.Add(vkCode))
                            _ = Task.Run(() => _engine.ToggleActiveTargetMute());
                        return true;
                    }

                    if (!snap.CycleAudioDevice.IsEmpty && snap.CycleAudioDevice.VkCode == vkCode && snap.CycleAudioDevice.Modifiers == mods)
                    {
                        if (_heldCustomVks.Add(vkCode))
                            _ = Task.Run(() => _engine.CycleOutputDevice());
                        return true;
                    }

                    foreach (var entry in snap.AppHotkeys)
                    {
                        if (!entry.Binding.IsEmpty && entry.Binding.VkCode == vkCode && entry.Binding.Modifiers == mods)
                        {
                            if (_heldCustomVks.Add(vkCode))
                            {
                                string promotedKey = entry.AppKey;
                                _ = Task.Run(() => _engine.PromoteAppToTop(promotedKey));
                            }
                            return true;
                        }
                    }
                }
            }
        }

        if (vkCode == Win32.VK_MEDIA_PLAY_PAUSE)
        {
            if (!_mediaHookEnabled) return false;
            long now = Environment.TickCount64;
            _lastMediaKeyActivityTick = now;
            if (isDown)
            {
                // Recover quickly if a wireless/BT headset HID bridge omits KEYUP while preventing hardware auto-repeat double toggles
                long resetMs = isInjected ? 350 : 1200;
                if (_playPauseHeld && now - _lastPlayPauseDownTick >= resetMs)
                    _playPauseHeld = false;

                if (!_playPauseHeld)
                {
                    _playPauseHeld = true;
                    _lastPlayPauseDownTick = now;
                    bool secondary = IsKeyDown(Win32.VK_CONTROL) || IsKeyDown(Win32.VK_SHIFT);
                    _ = Task.Run(() => _engine.DispatchMediaActionAsync(MediaActionKind.PlayPause, secondary));
                }
            }
            else if (isUp)
            {
                _playPauseHeld = false;
            }
            return true;
        }

        if (vkCode == Win32.VK_MEDIA_STOP)
        {
            if (!_mediaHookEnabled) return false;
            long now = Environment.TickCount64;
            _lastMediaKeyActivityTick = now;
            if (isDown)
            {
                long resetMs = isInjected ? 350 : 1200;
                if (_stopHeld && now - _lastStopDownTick >= resetMs)
                    _stopHeld = false;

                if (!_stopHeld)
                {
                    _stopHeld = true;
                    _lastStopDownTick = now;
                    var snap = _engine.GetSnapshot();
                    if (snap.Sessions.Any(s => s.IsPlaying || s.IsEffectivelyPlaying))
                    {
                        _ = Task.Run(() => _engine.DispatchMediaActionAsync(MediaActionKind.PlayPause, secondary: false));
                    }
                }
            }
            else if (isUp)
            {
                _stopHeld = false;
            }
            return true;
        }

        if (vkCode == Win32.VK_MEDIA_NEXT_TRACK || vkCode == Win32.VK_MEDIA_PREV_TRACK)
        {
            if (!_mediaHookEnabled) return false;
            long now = Environment.TickCount64;
            _lastMediaKeyActivityTick = now;

            if (isDown)
            {
                // Recover cleanly if a previous injected HID event lacked a matching KEYUP
                if (_heldMediaVk == vkCode && isInjected && now - _heldStartTick >= 1500 && !_longPressTriggered)
                    _heldMediaVk = 0;

                if (_heldMediaVk != vkCode)
                {
                    _heldMediaVk = vkCode;
                    _heldIsSecondary = IsKeyDown(Win32.VK_CONTROL) || IsKeyDown(Win32.VK_SHIFT);
                    _heldIsVideo = !_heldIsSecondary && _timelineScrubbingEnabled && _engine.IsActiveTargetVideoCapable();
                    _heldStartTick = now;
                    _longPressTriggered = false;

                    if (_heldIsSecondary || !_heldIsVideo)
                    {
                        var action = vkCode == Win32.VK_MEDIA_NEXT_TRACK
                            ? MediaActionKind.Next
                            : MediaActionKind.Previous;
                        bool sec = _heldIsSecondary;
                        _ = Task.Run(() => _engine.DispatchMediaActionAsync(action, sec));
                    }
                    else
                    {
                        uint threshold = (uint)_longPressThresholdMs;
                        Win32.SetTimer(_hwndHost, TIMER_LONG_PRESS, threshold, 0);
                    }
                }
                else if (_longPressTriggered && _heldIsVideo && !_heldIsSecondary && now - _heldStartTick >= 350)
                {
                    // Repeat seek smoothly while holding down on a video timeline
                    _heldStartTick = now;
                    var action = vkCode == Win32.VK_MEDIA_NEXT_TRACK
                        ? MediaActionKind.SeekForward
                        : MediaActionKind.SeekBackward;
                    _ = Task.Run(() => _engine.DispatchMediaActionAsync(action, secondary: false));
                }
                return true;
            }

            if (isUp)
            {
                Win32.KillTimer(_hwndHost, TIMER_LONG_PRESS);
                bool wasLong = _longPressTriggered;
                bool wasSecondary = _heldIsSecondary;
                bool wasVideo = _heldIsVideo;

                _heldMediaVk = 0;
                _longPressTriggered = false;

                if (!wasLong && !wasSecondary && wasVideo)
                {
                    if (_pendingTapVk == vkCode && now - _pendingTapTick <= DOUBLE_TAP_WINDOW_MS)
                    {
                        // Double-tap -> Timeline scrub
                        ClearPendingTap();
                        var seekAction = vkCode == Win32.VK_MEDIA_NEXT_TRACK
                            ? MediaActionKind.SeekForward
                            : MediaActionKind.SeekBackward;
                        _ = _engine.DispatchMediaActionAsync(seekAction, secondary: false);
                    }
                    else
                    {
                        // First tap -> Wait briefly to distinguish double-tap vs single-tap
                        _pendingTapVk = vkCode;
                        _pendingTapSecondary = false;
                        _pendingTapTick = now;
                        Win32.SetTimer(_hwndHost, TIMER_DOUBLE_TAP, DOUBLE_TAP_WINDOW_MS, 0);
                    }
                }
                return true;
            }
        }

        return false;
    }

    private static HotkeyModifiers GetCurrentModifiers()
    {
        HotkeyModifiers mods = HotkeyModifiers.None;
        if (IsKeyDown(Win32.VK_CONTROL)) mods |= HotkeyModifiers.Ctrl;
        if (IsKeyDown(Win32.VK_MENU)) mods |= HotkeyModifiers.Alt;
        if (IsKeyDown(Win32.VK_SHIFT)) mods |= HotkeyModifiers.Shift;
        if (IsKeyDown(Win32.VK_LWIN) || IsKeyDown(Win32.VK_RWIN)) mods |= HotkeyModifiers.Win;
        return mods;
    }

    private static HotkeyModifiers GetModifiersForTransition(uint vkCode, bool isDown)
    {
        bool ctrl = (vkCode == Win32.VK_LCONTROL || vkCode == Win32.VK_CONTROL ? isDown : IsKeyDown(Win32.VK_LCONTROL)) ||
                    (vkCode == Win32.VK_RCONTROL ? isDown : IsKeyDown(Win32.VK_RCONTROL));
        bool alt = (vkCode == Win32.VK_LMENU || vkCode == Win32.VK_MENU ? isDown : IsKeyDown(Win32.VK_LMENU)) ||
                   (vkCode == Win32.VK_RMENU ? isDown : IsKeyDown(Win32.VK_RMENU));
        bool shift = (vkCode == Win32.VK_LSHIFT || vkCode == Win32.VK_SHIFT ? isDown : IsKeyDown(Win32.VK_LSHIFT)) ||
                     (vkCode == Win32.VK_RSHIFT ? isDown : IsKeyDown(Win32.VK_RSHIFT));
        bool win = (vkCode == Win32.VK_LWIN ? isDown : IsKeyDown(Win32.VK_LWIN)) ||
                   (vkCode == Win32.VK_RWIN ? isDown : IsKeyDown(Win32.VK_RWIN));

        HotkeyModifiers mods = HotkeyModifiers.None;
        if (ctrl) mods |= HotkeyModifiers.Ctrl;
        if (alt) mods |= HotkeyModifiers.Alt;
        if (shift) mods |= HotkeyModifiers.Shift;
        if (win) mods |= HotkeyModifiers.Win;
        return mods;
    }

    private static bool IsKeyDown(int vKey) => (Win32.GetAsyncKeyState(vKey) & 0x8000) != 0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stateStore.StateChanged -= SyncCachedSettings;
        Win32.KillTimer(_hwndHost, TIMER_WATCHDOG);
        Win32.KillTimer(_hwndHost, TIMER_LONG_PRESS);
        Win32.KillTimer(_hwndHost, TIMER_DOUBLE_TAP);

        if (_hookHandle != 0)
        {
            Win32.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = 0;
        }
        if (ReferenceEquals(_instance, this))
            _instance = null;
    }
}
