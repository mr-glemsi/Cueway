using System.Runtime.InteropServices;
using Cueway.Interop;

namespace Cueway.Audio;

internal readonly record struct AudioProcessState(
    bool HasSession,
    bool HasActiveSession,
    bool IsMuted,
    float Volume,
    float Peak);

internal sealed class CoreAudioService : IDisposable
{
    private readonly object _lock = new();
    private readonly HashSet<uint> _soloMutedPids = [];
    private CoreAudio.NotificationClientCcw? _ccw;
    private nint _notificationEnumerator;
    private long _lastDeviceEventTick;
    private bool _disposed;

    public event Action<string>? DefaultOutputDeviceChanged;

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
    }

    private void OnEndpointCallback(string reason)
    {
        long now = Environment.TickCount64;
        long prev = Interlocked.Read(ref _lastDeviceEventTick);
        if (now - prev < 450)
            return;

        Interlocked.Exchange(ref _lastDeviceEventTick, now);
        DefaultOutputDeviceChanged?.Invoke(reason);
    }

    public Dictionary<uint, AudioProcessState> GetAudioStatesByPid()
    {
        var result = new Dictionary<uint, AudioProcessState>();
        ForEachAudioSession((pid, isActive, volPtr, peak) =>
        {
            CoreAudio.SimpleAudioVolume_GetMute(volPtr, out bool isMuted);
            CoreAudio.SimpleAudioVolume_GetMasterVolume(volPtr, out float level);

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

        int coHr = Win32.CoInitializeEx(0, Win32.COINIT_APARTMENTTHREADED);
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

    public void ApplySoloMode(bool enabled, IReadOnlySet<int> allowedPids)
    {
        lock (_lock)
        {
            if (!enabled)
            {
                if (_soloMutedPids.Count == 0)
                    return;

                ForEachAudioSession((_, _, volPtr, _) =>
                {
                    CoreAudio.SimpleAudioVolume_SetMute(volPtr, false);
                }, filterPids: _soloMutedPids);
                _soloMutedPids.Clear();
                return;
            }

            var allowedSet = new HashSet<uint>();
            foreach (int p in allowedPids)
            {
                if (p > 0) allowedSet.Add((uint)p);
            }

            var seenPids = new HashSet<uint>();
            ForEachAudioSession((pid, _, volPtr, _) =>
            {
                seenPids.Add(pid);
                if (CoreAudio.SimpleAudioVolume_GetMute(volPtr, out bool currentlyMuted) != 0)
                    return;

                if (allowedSet.Contains(pid))
                {
                    if (_soloMutedPids.Contains(pid) && currentlyMuted)
                    {
                        CoreAudio.SimpleAudioVolume_SetMute(volPtr, false);
                    }
                    _soloMutedPids.Remove(pid);
                }
                else
                {
                    if (!currentlyMuted)
                    {
                        if (CoreAudio.SimpleAudioVolume_SetMute(volPtr, true) == 0)
                        {
                            _soloMutedPids.Add(pid);
                        }
                    }
                }
            });

            // Prune exited processes from solo tracking
            _soloMutedPids.RemoveWhere(p => !seenPids.Contains(p));
        }
    }

    private static void ForEachAudioSession(
        Action<uint, bool, nint, float> callback,
        bool includePeak = false,
        HashSet<uint>? filterPids = null)
    {
        int coHr = Win32.CoInitializeEx(0, Win32.COINIT_APARTMENTTHREADED);
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
        }

        // Restore any solo-muted apps on shutdown
        try { ApplySoloMode(false, new HashSet<int>()); } catch { }

        lock (_lock)
        {
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
