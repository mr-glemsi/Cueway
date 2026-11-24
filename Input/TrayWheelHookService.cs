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
            EnsureHookInstalled(forceSwap: false);
    }

    public void InvalidateRectCache()
    {
        _hasCachedRect = false;
        _cachedRectTick = 0;
        _lastQueryTick = 0;
    }

    public void OnSystemWakeOrUnlock()
    {
        InvalidateRectCache();
        if (_trayWheelVolumeEnabled)
            EnsureHookInstalled(forceSwap: true);
    }

    private void UninstallHook()
    {
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
        if (nCode >= 0 && wParam == Win32.WM_MOUSEWHEEL && inst != null && !inst._disposed)
        {
            var ms = *(Win32.MSLLHOOKSTRUCT*)lParam;
            if (inst.TryHandleTrayWheel(ms))
                return 1; // Swallow wheel scroll over tray icon
        }
        return Win32.CallNextHookEx(inst?._hookHandle ?? 0, nCode, wParam, lParam);
    }

    private bool TryHandleTrayWheel(Win32.MSLLHOOKSTRUCT ms)
    {
        if (!_trayWheelVolumeEnabled)
            return false;

        long now = Environment.TickCount64;
        if (_hasCachedRect)
        {
            const int hitPad = 3;
            var cached = _cachedRect;
            bool inCached =
                ms.pt.X >= cached.Left - hitPad && ms.pt.X <= cached.Right + hitPad &&
                ms.pt.Y >= cached.Top - hitPad && ms.pt.Y <= cached.Bottom + hitPad;
            if (inCached && now - _cachedRectTick < 3000)
                return DispatchWheel(ms);

            const int farPad = 260;
            bool farOutside =
                ms.pt.X < cached.Left - farPad || ms.pt.X > cached.Right + farPad ||
                ms.pt.Y < cached.Top - farPad || ms.pt.Y > cached.Bottom + farPad;
            if (farOutside && now - _cachedRectTick < 15_000)
                return false;
        }

        if (now - _lastQueryTick >= 800)
        {
            _lastQueryTick = now;
            var maybeRect = _getTrayIconRect();
            if (maybeRect.HasValue)
            {
                _cachedRect = maybeRect.Value;
                _hasCachedRect = true;
                _cachedRectTick = now;
            }
        }

        if (!_hasCachedRect)
            return false;

        var rect = _cachedRect;
        // Slight padding makes hitting the tray icon forgiving across DPI scales
        const int pad = 3;
        bool inside =
            ms.pt.X >= rect.Left - pad && ms.pt.X <= rect.Right + pad &&
            ms.pt.Y >= rect.Top - pad && ms.pt.Y <= rect.Bottom + pad;

        return inside && DispatchWheel(ms);
    }

    private bool DispatchWheel(Win32.MSLLHOOKSTRUCT ms)
    {
        short deltaRaw = unchecked((short)(ms.mouseData >> 16));
        if (deltaRaw == 0)
            return true;

        float magnitude = _volumeStepPercent / 100f;
        float step = deltaRaw > 0 ? magnitude : -magnitude;
        _engine.AdjustActiveTargetVolume(step);
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
