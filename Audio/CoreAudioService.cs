using System.Runtime.InteropServices;
using Cueway.Interop;

namespace Cueway.Audio;

internal readonly record struct AudioProcessState(
    bool HasSession,
    bool HasActiveSession,
    bool IsMuted,
    float Volume,
    float Peak);

internal readonly record struct AudioOutputDevice(
    string Id,
    string Name,
    bool IsDefault);

internal sealed class CoreAudioService : IDisposable
{
    private readonly object _lock = new();
    private readonly object _devicesCacheLock = new();
    private CoreAudio.NotificationClientCcw? _ccw;
    private nint _notificationEnumerator;
    private long _lastListEventTick;
    private long _lastGuardEventTick;
    private long _suppressDeviceGuardUntilTick;
    private int _perAppRoutingSupported = -1;
    private List<AudioOutputDevice>? _cachedOutputDevices;
    private long _cachedOutputDevicesTick;
    private int _devicesRefreshInFlight;
    private volatile HashSet<uint> _lastKnownAudioPids = [];
    private bool _disposed;

    public event Action<string>? DefaultOutputDeviceChanged;
    public event Action? OutputDevicesChanged;

    public void StartEndpointNotifications()
    {
        lock (_lock)
        {
            if (_disposed || _notificationEnumerator != 0)
                return;

            try
            {
                int hr = CoreAudio.CreateDeviceEnumerator(out nint enumerator);
                if (hr != 0 || enumerator == 0)
                    return;

                _ccw = new CoreAudio.NotificationClientCcw(OnEndpointCallback);
                hr = CoreAudio.Enumerator_RegisterEndpointNotificationCallback(enumerator, _ccw.ComPtr);
                if (hr == 0)
                {
                    _notificationEnumerator = enumerator;
                }
                else
                {
                    CoreAudio.Release(enumerator);
                    _ccw.Dispose();
                    _ccw = null;
                }
            }
            catch
            {
                // Ignore if audio service unavailable at startup
            }
        }

        _ = Task.Run(RefreshOutputDevicesCache);
    }

    private static bool TryAtomicThrottle(ref long lastTick, long now, long cooldownMs)
    {
        while (true)
        {
            long prev = Volatile.Read(ref lastTick);
            if (prev != 0 && now - prev < cooldownMs)
                return false;
            if (Interlocked.CompareExchange(ref lastTick, now, prev) == prev)
                return true;
        }
    }

    private bool IsRenderDeviceId(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return true;

        nint enumerator = Volatile.Read(ref _notificationEnumerator);
        if (enumerator != 0)
        {
            bool? queried = CoreAudio.TryGetDeviceDataFlow(enumerator, deviceId, out int flow);
            if (queried == true)
                return flow == CoreAudio.eRender;
        }

        // Capture endpoint IDs start with {0.0.1.; render endpoint IDs start with {0.0.0.
        return !deviceId.StartsWith("{0.0.1.", StringComparison.OrdinalIgnoreCase);
    }

    private void OnEndpointCallback(string reason, string? deviceId)
    {
        if (_disposed)
            return;

        long now = Environment.TickCount64;
        if (TryAtomicThrottle(ref _lastListEventTick, now, 300))
        {
            _ = Task.Run(() =>
            {
                RefreshOutputDevicesCache();
                if (!_disposed)
                    OutputDevicesChanged?.Invoke();
            });
        }

        if (reason is "DeviceAdded" or "DeviceEnabled")
            return;

        if (!IsRenderDeviceId(deviceId))
            return;

        if (reason == "DefaultOutputChanged" && now <= Interlocked.Read(ref _suppressDeviceGuardUntilTick))
            return;

        if (!TryAtomicThrottle(ref _lastGuardEventTick, now, 450))
            return;

        DefaultOutputDeviceChanged?.Invoke(reason);
    }

    public List<AudioOutputDevice> GetOutputDevicesCached()
    {
        List<AudioOutputDevice>? snap;
        long age;
        lock (_devicesCacheLock)
        {
            snap = _cachedOutputDevices;
            age = snap != null ? Environment.TickCount64 - _cachedOutputDevicesTick : long.MaxValue;
        }

        if (snap == null || age > 10_000)
        {
            if (Interlocked.CompareExchange(ref _devicesRefreshInFlight, 1, 0) == 0)
            {
                _ = Task.Run(() =>
                {
                    try { RefreshOutputDevicesCache(); }
                    finally { Interlocked.Exchange(ref _devicesRefreshInFlight, 0); }
                });
            }
        }

        return snap != null ? [.. snap] : [];
    }

    private void RefreshOutputDevicesCache()
    {
        if (_disposed)
            return;
        var list = GetOutputDevices();
        lock (_devicesCacheLock)
        {
            _cachedOutputDevices = list;
            _cachedOutputDevicesTick = Environment.TickCount64;
        }
    }

    public string? GetDefaultOutputDeviceId()
    {
        int coHr = Win32.CoInitializeEx(0, Win32.COINIT_MULTITHREADED);
        bool mustUninit = coHr == 0 || coHr == 1;

        nint enumerator = 0;
        nint defaultDevice = 0;
        try
        {
            if (CoreAudio.CreateDeviceEnumerator(out enumerator) != 0 || enumerator == 0)
                return null;
            if (CoreAudio.Enumerator_GetDefaultAudioEndpoint(enumerator, CoreAudio.eRender, CoreAudio.eMultimedia, out defaultDevice) != 0 || defaultDevice == 0)
                return null;
            return CoreAudio.Device_GetId(defaultDevice, out string id) == 0 && !string.IsNullOrWhiteSpace(id)
                ? id.Trim()
                : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (defaultDevice != 0) CoreAudio.Release(defaultDevice);
            if (enumerator != 0) CoreAudio.Release(enumerator);
            if (mustUninit) Win32.CoUninitialize();
        }
    }

    public unsafe List<AudioOutputDevice> GetOutputDevices()
    {
        var devices = new List<AudioOutputDevice>();
        int coHr = Win32.CoInitializeEx(0, Win32.COINIT_MULTITHREADED);
        bool mustUninit = coHr == 0 || coHr == 1;

        nint enumerator = 0;
        nint collection = 0;
        nint defaultDevice = 0;

        try
        {
            if (CoreAudio.CreateDeviceEnumerator(out enumerator) != 0 || enumerator == 0)
                return devices;

            string defaultId = string.Empty;
            if (CoreAudio.Enumerator_GetDefaultAudioEndpoint(enumerator, CoreAudio.eRender, CoreAudio.eMultimedia, out defaultDevice) == 0 &&
                defaultDevice != 0)
            {
                CoreAudio.Device_GetId(defaultDevice, out defaultId);
            }

            if (CoreAudio.Enumerator_EnumAudioEndpoints(enumerator, CoreAudio.eRender, CoreAudio.DEVICE_STATE_ACTIVE, out collection) == 0 &&
                collection != 0 &&
                CoreAudio.DeviceCollection_GetCount(collection, out uint devCount) == 0 &&
                devCount > 0)
            {
                uint cap = Math.Min(devCount, 32);
                for (uint d = 0; d < cap; d++)
                {
                    nint dev = 0;
                    nint store = 0;
                    CoreAudio.PROPVARIANT pv = default;
                    bool pvValid = false;
                    try
                    {
                        if (CoreAudio.DeviceCollection_Item(collection, d, out dev) != 0 || dev == 0)
                            continue;
                        if (CoreAudio.Device_GetId(dev, out string devId) != 0 || string.IsNullOrWhiteSpace(devId))
                            continue;

                        string name = devId;
                        if (CoreAudio.Device_OpenPropertyStore(dev, CoreAudio.STGM_READ, out store) == 0 && store != 0)
                        {
                            if (CoreAudio.PropertyStore_GetValue(store, in CoreAudio.PKEY_Device_FriendlyName, out pv) == 0)
                            {
                                pvValid = true;
                                if (pv.vt == CoreAudio.VT_LPWSTR && pv.data1 != 0)
                                {
                                    string? friendly = Marshal.PtrToStringUni(pv.data1);
                                    if (!string.IsNullOrWhiteSpace(friendly))
                                        name = friendly.Trim();
                                }
                            }
                        }

                        bool isDef = !string.IsNullOrEmpty(defaultId) &&
                                     string.Equals(devId, defaultId, StringComparison.OrdinalIgnoreCase);
                        devices.Add(new AudioOutputDevice(devId, name, isDef));
                    }
                    finally
                    {
                        if (pvValid) Win32.PropVariantClear(&pv);
                        if (store != 0) CoreAudio.Release(store);
                        if (dev != 0) CoreAudio.Release(dev);
                    }
                }
            }

            devices.Sort((a, b) =>
            {
                int defCmp = b.IsDefault.CompareTo(a.IsDefault);
                return defCmp != 0 ? defCmp : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }
        catch
        {
            // Ignore transient audio service errors during hardware switch
        }
        finally
        {
            if (defaultDevice != 0) CoreAudio.Release(defaultDevice);
            if (collection != 0) CoreAudio.Release(collection);
            if (enumerator != 0) CoreAudio.Release(enumerator);
            if (mustUninit) Win32.CoUninitialize();
        }

        return devices;
    }

    public bool SetDefaultOutputDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return false;

        Interlocked.Exchange(ref _suppressDeviceGuardUntilTick, Environment.TickCount64 + 1500);
        int coHr = Win32.CoInitializeEx(0, Win32.COINIT_MULTITHREADED);
        bool mustUninit = coHr == 0 || coHr == 1;
        try
        {
            bool ok = CoreAudio.PolicyConfig_SetDefaultEndpoint(deviceId);
            if (ok)
            {
                _ = Task.Run(() =>
                {
                    RefreshOutputDevicesCache();
                    if (!_disposed)
                        OutputDevicesChanged?.Invoke();
                });
            }
            else
            {
                Interlocked.Exchange(ref _suppressDeviceGuardUntilTick, 0);
            }
            return ok;
        }
        catch
        {
            Interlocked.Exchange(ref _suppressDeviceGuardUntilTick, 0);
            return false;
        }
        finally
        {
            if (mustUninit) Win32.CoUninitialize();
        }
    }

    public AudioOutputDevice? CycleDefaultOutputDevice()
    {
        var devices = GetOutputDevices();
        if (devices.Count < 2)
            return null;

        int defIdx = devices.FindIndex(d => d.IsDefault);
        int nextIdx = defIdx >= 0 ? (defIdx + 1) % devices.Count : 0;
        var next = devices[nextIdx];
        return SetDefaultOutputDevice(next.Id) ? next with { IsDefault = true } : null;
    }

    public bool IsPerAppAudioRoutingSupported()
    {
        int cached = Volatile.Read(ref _perAppRoutingSupported);
        if (cached >= 0)
            return cached == 1;

        int coHr = Win32.CoInitializeEx(0, Win32.COINIT_MULTITHREADED);
        bool mustUninit = coHr == 0 || coHr == 1;
        nint factory = 0;
        bool ok = false;
        try
        {
            ok = CoreAudio.TryCreateAudioPolicyConfigFactory(out factory) && factory != 0;
        }
        catch
        {
            ok = false;
        }
        finally
        {
            if (factory != 0) CoreAudio.Release(factory);
            if (mustUninit) Win32.CoUninitialize();
        }

        Interlocked.CompareExchange(ref _perAppRoutingSupported, ok ? 1 : 0, -1);
        return ok;
    }

    public Dictionary<int, string?> GetAppOutputDeviceIds(IReadOnlyCollection<int> pids)
    {
        var result = new Dictionary<int, string?>();
        if (pids.Count == 0 || !IsPerAppAudioRoutingSupported())
            return result;

        int coHr = Win32.CoInitializeEx(0, Win32.COINIT_MULTITHREADED);
        bool mustUninit = coHr == 0 || coHr == 1;
        nint factory = 0;
        try
        {
            if (!CoreAudio.TryCreateAudioPolicyConfigFactory(out factory) || factory == 0)
                return result;

            foreach (int p in pids)
            {
                if (p <= 0 || result.ContainsKey(p))
                    continue;
                result[p] = CoreAudio.AudioPolicyConfig_GetPersistedDefaultAudioEndpoint(factory, (uint)p);
            }
        }
        catch
        {
            // Ignore transient policy query errors
        }
        finally
        {
            if (factory != 0) CoreAudio.Release(factory);
            if (mustUninit) Win32.CoUninitialize();
        }

        return result;
    }

    public bool SetAppOutputDevice(IReadOnlyCollection<int> pids, string? deviceId)
    {
        if (pids.Count == 0 || !IsPerAppAudioRoutingSupported())
            return false;

        Interlocked.Exchange(ref _suppressDeviceGuardUntilTick, Environment.TickCount64 + 2500);

        var targetPids = new List<int>(4);
        var audioByPid = GetAudioStatesByPid();
        var seenAll = new HashSet<int>();
        int firstPositive = 0;
        foreach (int p in pids)
        {
            if (p <= 0 || !seenAll.Add(p))
                continue;
            if (firstPositive == 0)
                firstPositive = p;
            if (audioByPid.TryGetValue((uint)p, out var state))
            {
                if (state.HasActiveSession)
                    targetPids.Insert(0, p);
                else
                    targetPids.Add(p);
            }
        }
        if (targetPids.Count == 0 && firstPositive != 0)
            targetPids.Add(firstPositive);
        if (targetPids.Count == 0)
            return false;

        int coHr = Win32.CoInitializeEx(0, Win32.COINIT_MULTITHREADED);
        bool mustUninit = coHr == 0 || coHr == 1;
        nint factory = 0;
        try
        {
            if (!CoreAudio.TryCreateAudioPolicyConfigFactory(out factory) || factory == 0)
            {
                Interlocked.Exchange(ref _suppressDeviceGuardUntilTick, 0);
                return false;
            }

            bool anyOk = false;
            int limit = Math.Min(targetPids.Count, 4);
            for (int i = 0; i < limit; i++)
            {
                if (CoreAudio.AudioPolicyConfig_SetPersistedDefaultAudioEndpoint(factory, (uint)targetPids[i], deviceId))
                    anyOk = true;
            }
            if (!anyOk)
                Interlocked.Exchange(ref _suppressDeviceGuardUntilTick, 0);
            return anyOk;
        }
        catch
        {
            Interlocked.Exchange(ref _suppressDeviceGuardUntilTick, 0);
            return false;
        }
        finally
        {
            if (factory != 0) CoreAudio.Release(factory);
            if (mustUninit) Win32.CoUninitialize();
        }
    }

    public bool IsAnyMicrophoneSessionActive()
    {
        int coHr = Win32.CoInitializeEx(0, Win32.COINIT_MULTITHREADED);
        bool mustUninit = coHr == 0 || coHr == 1;
        uint selfPid = (uint)Environment.ProcessId;

        nint enumerator = 0;
        nint collection = 0;
        try
        {
            if (CoreAudio.CreateDeviceEnumerator(out enumerator) != 0 || enumerator == 0)
                return false;

            if (CoreAudio.Enumerator_EnumAudioEndpoints(enumerator, CoreAudio.eCapture, CoreAudio.DEVICE_STATE_ACTIVE, out collection) != 0 ||
                collection == 0 ||
                CoreAudio.DeviceCollection_GetCount(collection, out uint devCount) != 0 ||
                devCount == 0)
            {
                return false;
            }

            uint cap = Math.Min(devCount, 16);
            for (uint d = 0; d < cap; d++)
            {
                nint device = 0;
                nint mgr = 0;
                nint sessionEnum = 0;
                try
                {
                    if (CoreAudio.DeviceCollection_Item(collection, d, out device) != 0 || device == 0)
                        continue;
                    if (CoreAudio.Device_Activate(device, in CoreAudio.IID_IAudioSessionManager2, CoreAudio.CLSCTX_ALL, out mgr) != 0 || mgr == 0)
                        continue;
                    if (CoreAudio.SessionManager2_GetSessionEnumerator(mgr, out sessionEnum) != 0 || sessionEnum == 0)
                        continue;
                    if (CoreAudio.SessionEnumerator_GetCount(sessionEnum, out int count) != 0 || count <= 0)
                        continue;

                    for (int i = 0; i < count; i++)
                    {
                        nint ctrl = 0;
                        nint ctrl2 = 0;
                        try
                        {
                            if (CoreAudio.SessionEnumerator_GetSession(sessionEnum, i, out ctrl) != 0 || ctrl == 0)
                                continue;
                            if (CoreAudio.SessionControl_GetState(ctrl, out int state) != 0 || state != CoreAudio.AudioSessionStateActive)
                                continue;
                            if (CoreAudio.QueryInterface(ctrl, in CoreAudio.IID_IAudioSessionControl2, out ctrl2) != 0 || ctrl2 == 0)
                                continue;
                            if (CoreAudio.SessionControl2_IsSystemSoundsSession(ctrl2) == 0)
                                continue;
                            if (CoreAudio.SessionControl2_GetProcessId(ctrl2, out uint pid) != 0 || pid == 0 || pid == selfPid)
                                continue;
                            return true;
                        }
                        finally
                        {
                            if (ctrl2 != 0) CoreAudio.Release(ctrl2);
                            if (ctrl != 0) CoreAudio.Release(ctrl);
                        }
                    }
                }
                finally
                {
                    if (sessionEnum != 0) CoreAudio.Release(sessionEnum);
                    if (mgr != 0) CoreAudio.Release(mgr);
                    if (device != 0) CoreAudio.Release(device);
                }
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            if (collection != 0) CoreAudio.Release(collection);
            if (enumerator != 0) CoreAudio.Release(enumerator);
            if (mustUninit) Win32.CoUninitialize();
        }

        return false;
    }

    public HashSet<uint> GetKnownAudioPidsSnapshot() => _lastKnownAudioPids;

    public Dictionary<uint, AudioProcessState> GetAudioStatesByPid()
    {
        var result = new Dictionary<uint, AudioProcessState>();
        var seenPids = new HashSet<uint>();
        ForEachAudioSession((pid, isActive, volPtr, peak) =>
        {
            seenPids.Add(pid);
            if (CoreAudio.SimpleAudioVolume_GetMute(volPtr, out bool isMuted) != 0)
                return;
            if (CoreAudio.SimpleAudioVolume_GetMasterVolume(volPtr, out float level) != 0 || !float.IsFinite(level))
                return;

            if (result.TryGetValue(pid, out var existing))
            {
                // Prefer active session status; process is only muted if all active (or all) sessions are muted
                bool hasActive = existing.HasActiveSession || isActive;
                bool combinedMuted;
                float combinedVol;
                float combinedPeak = MathF.Max(existing.Peak, peak);

                if (isActive && !existing.HasActiveSession)
                {
                    combinedMuted = isMuted;
                    combinedVol = level;
                }
                else if (!isActive && existing.HasActiveSession)
                {
                    combinedMuted = existing.IsMuted;
                    combinedVol = existing.Volume;
                }
                else
                {
                    combinedMuted = existing.IsMuted && isMuted;
                    combinedVol = MathF.Max(existing.Volume, level);
                }

                result[pid] = new AudioProcessState(true, hasActive, combinedMuted, combinedVol, combinedPeak);
            }
            else
            {
                result[pid] = new AudioProcessState(true, isActive, isMuted, level, peak);
            }
        }, includePeak: true);
        _lastKnownAudioPids = seenPids;
        return result;
    }

    public int? AdjustAppVolume(IReadOnlyCollection<int> pids, float delta)
    {
        if (pids.Count == 0)
            return null;

        var pidSet = new HashSet<uint>();
        foreach (int p in pids)
        {
            if (p > 0) pidSet.Add((uint)p);
        }
        if (pidSet.Count == 0)
            return null;

        float? updatedLevel = null;
        ForEachAudioSession((_, _, volPtr, _) =>
        {
            if (CoreAudio.SimpleAudioVolume_GetMasterVolume(volPtr, out float current) != 0)
                return;

            float next = Math.Clamp(current + delta, 0f, 1f);
            if (CoreAudio.SimpleAudioVolume_SetMasterVolume(volPtr, next) == 0)
            {
                if (delta > 0f && CoreAudio.SimpleAudioVolume_GetMute(volPtr, out bool muted) == 0 && muted)
                {
                    CoreAudio.SimpleAudioVolume_SetMute(volPtr, false);
                }
                updatedLevel ??= next;
            }
        }, filterPids: pidSet);

        return updatedLevel.HasValue ? (int)MathF.Round(updatedLevel.Value * 100f) : null;
    }

    public int? SetAppVolume(IReadOnlyCollection<int> pids, float level)
    {
        if (pids.Count == 0)
            return null;

        var pidSet = new HashSet<uint>();
        foreach (int p in pids)
        {
            if (p > 0) pidSet.Add((uint)p);
        }
        if (pidSet.Count == 0)
            return null;

        float clamped = Math.Clamp(level, 0f, 1f);
        bool anyUpdated = false;
        ForEachAudioSession((_, _, volPtr, _) =>
        {
            if (CoreAudio.SimpleAudioVolume_SetMasterVolume(volPtr, clamped) == 0)
            {
                if (clamped > 0.005f && CoreAudio.SimpleAudioVolume_GetMute(volPtr, out bool muted) == 0 && muted)
                {
                    CoreAudio.SimpleAudioVolume_SetMute(volPtr, false);
                }
                anyUpdated = true;
            }
        }, filterPids: pidSet);

        return anyUpdated ? (int)MathF.Round(clamped * 100f) : null;
    }

    public bool? ToggleAppMute(IReadOnlyCollection<int> pids)
    {
        if (pids.Count == 0)
            return null;

        var pidSet = new HashSet<uint>();
        foreach (int p in pids)
        {
            if (p > 0) pidSet.Add((uint)p);
        }
        if (pidSet.Count == 0)
            return null;

        int coHr = Win32.CoInitializeEx(0, Win32.COINIT_MULTITHREADED);
        bool mustUninit = coHr == 0 || coHr == 1;
        var matchedVolumes = new List<nint>(4);
        bool found = false;
        bool allMuted = true;
        bool anySet = false;

        try
        {
            ForEachAudioSession((_, _, volPtr, _) =>
            {
                if (CoreAudio.SimpleAudioVolume_GetMute(volPtr, out bool muted) == 0)
                {
                    found = true;
                    if (!muted) allMuted = false;
                    CoreAudio.AddRef(volPtr);
                    matchedVolumes.Add(volPtr);
                }
            }, filterPids: pidSet);

            if (!found)
                return null;

            bool nextMuted = !allMuted;
            foreach (nint volPtr in matchedVolumes)
            {
                if (CoreAudio.SimpleAudioVolume_SetMute(volPtr, nextMuted) == 0)
                {
                    anySet = true;
                    if (!nextMuted &&
                        CoreAudio.SimpleAudioVolume_GetMasterVolume(volPtr, out float vol) == 0 &&
                        vol <= 0.005f)
                    {
                        CoreAudio.SimpleAudioVolume_SetMasterVolume(volPtr, 0.5f);
                    }
                }
            }

            return anySet ? nextMuted : null;
        }
        finally
        {
            foreach (nint volPtr in matchedVolumes)
                CoreAudio.Release(volPtr);
            if (mustUninit) Win32.CoUninitialize();
        }
    }

    private static void ForEachAudioSession(
        Action<uint, bool, nint, float> callback,
        bool includePeak = false,
        HashSet<uint>? filterPids = null)
    {
        int coHr = Win32.CoInitializeEx(0, Win32.COINIT_MULTITHREADED);
        bool mustUninit = coHr == 0 || coHr == 1;

        nint enumerator = 0;
        nint collection = 0;

        try
        {
            if (CoreAudio.CreateDeviceEnumerator(out enumerator) != 0 || enumerator == 0)
                return;

            if (CoreAudio.Enumerator_EnumAudioEndpoints(enumerator, CoreAudio.eRender, CoreAudio.DEVICE_STATE_ACTIVE, out collection) == 0 &&
                collection != 0 &&
                CoreAudio.DeviceCollection_GetCount(collection, out uint devCount) == 0 &&
                devCount > 0)
            {
                uint cap = Math.Min(devCount, 32);
                for (uint d = 0; d < cap; d++)
                {
                    nint device = 0;
                    try
                    {
                        if (CoreAudio.DeviceCollection_Item(collection, d, out device) == 0 && device != 0)
                        {
                            EnumerateDeviceSessions(device, callback, includePeak, filterPids);
                        }
                    }
                    finally
                    {
                        if (device != 0) CoreAudio.Release(device);
                    }
                }
            }
            else
            {
                nint device = 0;
                try
                {
                    if (CoreAudio.Enumerator_GetDefaultAudioEndpoint(enumerator, CoreAudio.eRender, CoreAudio.eMultimedia, out device) == 0 &&
                        device != 0)
                    {
                        EnumerateDeviceSessions(device, callback, includePeak, filterPids);
                    }
                }
                finally
                {
                    if (device != 0) CoreAudio.Release(device);
                }
            }
        }
        catch
        {
            // Device might be unavailable during audio hardware reconfiguration
        }
        finally
        {
            if (collection != 0) CoreAudio.Release(collection);
            if (enumerator != 0) CoreAudio.Release(enumerator);
            if (mustUninit) Win32.CoUninitialize();
        }
    }

    private static void EnumerateDeviceSessions(
        nint device,
        Action<uint, bool, nint, float> callback,
        bool includePeak,
        HashSet<uint>? filterPids)
    {
        nint mgr = 0;
        nint sessionEnum = 0;

        try
        {
            if (CoreAudio.Device_Activate(device, in CoreAudio.IID_IAudioSessionManager2, CoreAudio.CLSCTX_ALL, out mgr) != 0 || mgr == 0)
                return;

            if (CoreAudio.SessionManager2_GetSessionEnumerator(mgr, out sessionEnum) != 0 || sessionEnum == 0)
                return;

            if (CoreAudio.SessionEnumerator_GetCount(sessionEnum, out int count) != 0 || count <= 0)
                return;

            for (int i = 0; i < count; i++)
            {
                nint ctrl = 0;
                nint ctrl2 = 0;
                nint vol = 0;
                nint meter = 0;
                try
                {
                    if (CoreAudio.SessionEnumerator_GetSession(sessionEnum, i, out ctrl) != 0 || ctrl == 0)
                        continue;

                    if (CoreAudio.SessionControl_GetState(ctrl, out int state) != 0 ||
                        state == CoreAudio.AudioSessionStateExpired)
                        continue;

                    if (CoreAudio.QueryInterface(ctrl, in CoreAudio.IID_IAudioSessionControl2, out ctrl2) != 0 || ctrl2 == 0)
                        continue;

                    // S_OK (0) means this is the system sounds session; skip it
                    if (CoreAudio.SessionControl2_IsSystemSoundsSession(ctrl2) == 0)
                        continue;

                    if (CoreAudio.SessionControl2_GetProcessId(ctrl2, out uint pid) != 0 || pid == 0)
                        continue;

                    if (filterPids != null && !filterPids.Contains(pid))
                        continue;

                    if (CoreAudio.QueryInterface(ctrl, in CoreAudio.IID_ISimpleAudioVolume, out vol) != 0 || vol == 0)
                        continue;

                    bool isActive = state == CoreAudio.AudioSessionStateActive;
                    float peak = 0f;
                    if (includePeak && isActive &&
                        CoreAudio.QueryInterface(ctrl, in CoreAudio.IID_IAudioMeterInformation, out meter) == 0 && meter != 0)
                    {
                        if (CoreAudio.AudioMeter_GetPeakValue(meter, out float p) == 0 && float.IsFinite(p))
                            peak = Math.Clamp(p, 0f, 1f);
                    }

                    callback(pid, isActive, vol, peak);
                }
                finally
                {
                    if (meter != 0) CoreAudio.Release(meter);
                    if (vol != 0) CoreAudio.Release(vol);
                    if (ctrl2 != 0) CoreAudio.Release(ctrl2);
                    if (ctrl != 0) CoreAudio.Release(ctrl);
                }
            }
        }
        catch
        {
            // Ignore transient endpoint errors
        }
        finally
        {
            if (sessionEnum != 0) CoreAudio.Release(sessionEnum);
            if (mgr != 0) CoreAudio.Release(mgr);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            if (_notificationEnumerator != 0 && _ccw != null)
            {
                try
                {
                    CoreAudio.Enumerator_UnregisterEndpointNotificationCallback(_notificationEnumerator, _ccw.ComPtr);
                }
                catch { }
                CoreAudio.Release(_notificationEnumerator);
                _notificationEnumerator = 0;
            }
            _ccw?.Dispose();
            _ccw = null;
        }
    }
}
