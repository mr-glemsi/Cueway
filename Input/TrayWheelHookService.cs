using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cueway.Config;
using Cueway.Interop;
using Cueway.Routing;

namespace Cueway.Input;

internal sealed unsafe class TrayWheelHookService : IDisposable
{
    private static TrayWheelHookService? _instance;

    private readonly StateStore _stateStore;
    private readonly Func<Win32.RECT?> _getTrayIconRect;
    private readonly MediaRouterEngine _engine;
    private readonly Action<Action> _dispatchToUiThread;
    private nint _hookHandle;
    private volatile bool _trayWheelVolumeEnabled = true;
    private volatile int _volumeStepPercent = 5;
    private bool _hasCachedRect;
    private Win32.RECT _cachedRect;
    private long _cachedRectTick;
    private long _lastQueryTick;
    private int _rectRefreshPending;
    private int _wheelDeltaAccum;
    private bool _disposed;

    public TrayWheelHookService(
        StateStore stateStore,
        Func<Win32.RECT?> getTrayIconRect,
        MediaRouterEngine engine,
        Action<Action> dispatchToUiThread)
    {
        _stateStore = stateStore;
        _getTrayIconRect = getTrayIconRect;
        _engine = engine;
        _dispatchToUiThread = dispatchToUiThread;
        _instance = this;
        var s = _stateStore.GetSettingsSnapshot();
        _trayWheelVolumeEnabled = s.TrayWheelVolumeEnabled;
        _volumeStepPercent = s.VolumeStepPercent;
        _stateStore.StateChanged += SyncCachedSettings;
    }

    private void SyncCachedSettings()
    {
        var s = _stateStore.GetSettingsSnapshot();
        bool prev = _trayWheelVolumeEnabled;
        _trayWheelVolumeEnabled = s.TrayWheelVolumeEnabled;
        _volumeStepPercent = s.VolumeStepPercent;

        if (!_disposed && prev != _trayWheelVolumeEnabled)
        {
            _dispatchToUiThread(() =>
            {
                if (_disposed) return;
                if (_trayWheelVolumeEnabled)
                    EnsureHookInstalled(forceSwap: false);
                else
                    UninstallHook();
            });
        }
    }

    public void Start()
    {
        if (_trayWheelVolumeEnabled)
        {
            EnsureHookInstalled(forceSwap: false);
            QueueRectRefresh();
        }
    }

    public void InvalidateRectCache()
    {
        _hasCachedRect = false;
        _cachedRectTick = 0;
        _lastQueryTick = 0;
        _wheelDeltaAccum = 0;
        if (!_disposed && _trayWheelVolumeEnabled)
            QueueRectRefresh();
    }

    public void OnSystemWakeOrUnlock()
    {
        InvalidateRectCache();
        if (_trayWheelVolumeEnabled)
            EnsureHookInstalled(forceSwap: true);
    }

    private void QueueRectRefresh()
    {
        if (_disposed || Interlocked.CompareExchange(ref _rectRefreshPending, 1, 0) != 0)
            return;
        _dispatchToUiThread(() =>
        {
            Interlocked.Exchange(ref _rectRefreshPending, 0);
            if (_disposed || !_trayWheelVolumeEnabled)
                return;
            var maybeRect = _getTrayIconRect();
            if (maybeRect.HasValue)
            {
                _cachedRect = maybeRect.Value;
                _hasCachedRect = true;
                _cachedRectTick = Environment.TickCount64;
            }
        });
    }

    private void UninstallHook()
    {
        if (_trayWheelVolumeEnabled) return;
        InvalidateRectCache();
        if (_hookHandle != 0)
        {
            Win32.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = 0;
        }
    }

    private void EnsureHookInstalled(bool forceSwap)
    {
        if (_disposed || !_trayWheelVolumeEnabled) return;
        if (_hookHandle != 0 && !forceSwap) return;

        nint hMod = Win32.GetModuleHandleW(null);
        nint newHook = Win32.SetWindowsHookExW(Win32.WH_MOUSE_LL, &LowLevelMouseProc, hMod, 0);
        if (newHook != 0)
        {
            nint oldHook = _hookHandle;
            _hookHandle = newHook;
            if (oldHook != 0)
                Win32.UnhookWindowsHookEx(oldHook);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint LowLevelMouseProc(int nCode, nuint wParam, nint lParam)
    {
        var inst = _instance;
        try
        {
            if (nCode >= 0 && wParam == Win32.WM_MOUSEWHEEL && inst != null && !inst._disposed)
            {
                var ms = *(Win32.MSLLHOOKSTRUCT*)lParam;
                if (inst.TryHandleTrayWheel(ms))
                    return 1; // Swallow wheel scroll over tray icon
            }
        }
        catch { }
        return Win32.CallNextHookEx(inst?._hookHandle ?? 0, nCode, wParam, lParam);
    }

    private bool TryHandleTrayWheel(Win32.MSLLHOOKSTRUCT ms)
    {
        if (!_trayWheelVolumeEnabled)
            return false;

        long now = Environment.TickCount64;
        if (now - _lastQueryTick >= 1500)
        {
            _lastQueryTick = now;
            QueueRectRefresh();
        }

        if (!_hasCachedRect)
            return false;

        var rect = _cachedRect;
        const int pad = 4;
        bool inside =
            ms.pt.X >= rect.Left - pad && ms.pt.X <= rect.Right + pad &&
            ms.pt.Y >= rect.Top - pad && ms.pt.Y <= rect.Bottom + pad;

        if (!inside)
        {
            _wheelDeltaAccum = 0;
            return false;
        }

        return DispatchWheel(ms);
    }

    private bool DispatchWheel(Win32.MSLLHOOKSTRUCT ms)
    {
        short deltaRaw = unchecked((short)(ms.mouseData >> 16));
        if (deltaRaw == 0)
            return true;

        if ((deltaRaw > 0 && _wheelDeltaAccum < 0) || (deltaRaw < 0 && _wheelDeltaAccum > 0))
            _wheelDeltaAccum = 0;

        _wheelDeltaAccum += deltaRaw;
        int notches = _wheelDeltaAccum / 120;
        if (notches == 0)
            return true;

        _wheelDeltaAccum -= notches * 120;
        float step = notches * (_volumeStepPercent / 100f);
        _ = Task.Run(() => _engine.AdjustActiveTargetVolume(step));
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stateStore.StateChanged -= SyncCachedSettings;
        if (_hookHandle != 0)
        {
            Win32.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = 0;
        }
        if (ReferenceEquals(_instance, this))
            _instance = null;
    }
}
