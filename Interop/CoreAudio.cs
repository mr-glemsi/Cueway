using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Cueway.Interop;

internal static unsafe partial class CoreAudio
{
    public const uint CLSCTX_ALL = 23;
    public const int eRender = 0;
    public const int eCapture = 1;
    public const int eConsole = 0;
    public const int eMultimedia = 1;
    public const int eCommunications = 2;
    public const uint DEVICE_STATE_ACTIVE = 0x00000001;
    public const uint STGM_READ = 0;
    public const ushort VT_LPWSTR = 31;

    public const int AudioSessionStateInactive = 0;
    public const int AudioSessionStateActive = 1;
    public const int AudioSessionStateExpired = 2;

    public static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    public static readonly Guid IID_IMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    public static readonly Guid IID_IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    public static readonly Guid IID_IAudioSessionControl2 = new("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D");
    public static readonly Guid IID_ISimpleAudioVolume = new("87CE5498-68D6-44E5-9215-6DA47EF883D8");
    public static readonly Guid IID_IAudioMeterInformation = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");
    public static readonly Guid IID_IMMNotificationClient = new("7991EEC9-7E89-4D85-8390-6C703CEC60C0");
    public static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    public static readonly Guid CLSID_PolicyConfigClient = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");
    public static readonly Guid IID_IPolicyConfig = new("F8679F50-850A-41CF-9C72-430F290290C8");
    public static readonly Guid IID_IPolicyConfigVista = new("568B9108-44BF-40B4-9006-86AFE5B5A620");

    public const string AudioPolicyConfigClassName = "Windows.Media.Internal.AudioPolicyConfig";
    public const string MmDevApiPrefix = @"\\?\SWD#MMDEVAPI#";
    public const string DevInterfaceAudioRenderSuffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    public static readonly Guid IID_IAudioPolicyConfigFactory_21H2 = new("AB3D4648-E242-459F-B02F-541C70306324");
    public static readonly Guid IID_IAudioPolicyConfigFactory_Downlevel = new("2A59116D-6C4F-45E0-A74F-707E3FEF9258");

    public static readonly PROPERTYKEY PKEY_Device_FriendlyName = new()
    {
        fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        pid = 14
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPVARIANT
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public nint data1;
        public nint data2;
    }

    public static uint AddRef(nint comPtr)
    {
        if (comPtr == 0) return 0;
        nint* vtbl = *(nint**)comPtr;
        return ((delegate* unmanaged[Stdcall]<nint, uint>)vtbl[1])(comPtr);
    }

    public static void Release(nint comPtr)
    {
        if (comPtr == 0) return;
        nint* vtbl = *(nint**)comPtr;
        ((delegate* unmanaged[Stdcall]<nint, uint>)vtbl[2])(comPtr);
    }

    public static int QueryInterface(nint comPtr, in Guid iid, out nint ppv)
    {
        ppv = 0;
        if (comPtr == 0) return unchecked((int)0x80004003);
        Guid g = iid;
        nint* vtbl = *(nint**)comPtr;
        fixed (nint* p = &ppv)
        {
            return ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)vtbl[0])(comPtr, &g, p);
        }
    }

    public static int CreateDeviceEnumerator(out nint ppEnumerator)
    {
        Guid clsid = CLSID_MMDeviceEnumerator;
        Guid iid = IID_IMMDeviceEnumerator;
        return Win32.CoCreateInstance(&clsid, 0, CLSCTX_ALL, &iid, out ppEnumerator);
    }

    public static int Enumerator_EnumAudioEndpoints(nint enumerator, int dataFlow, uint stateMask, out nint ppDevices)
    {
        nint* vtbl = *(nint**)enumerator;
        fixed (nint* p = &ppDevices)
        {
            return ((delegate* unmanaged[Stdcall]<nint, int, uint, nint*, int>)vtbl[3])(enumerator, dataFlow, stateMask, p);
        }
    }

    public static int DeviceCollection_GetCount(nint collection, out uint count)
    {
        nint* vtbl = *(nint**)collection;
        fixed (uint* p = &count)
        {
            return ((delegate* unmanaged[Stdcall]<nint, uint*, int>)vtbl[3])(collection, p);
        }
    }

    public static int DeviceCollection_Item(nint collection, uint index, out nint ppDevice)
    {
        nint* vtbl = *(nint**)collection;
        fixed (nint* p = &ppDevice)
        {
            return ((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)vtbl[4])(collection, index, p);
        }
    }

    public static int Enumerator_GetDefaultAudioEndpoint(nint enumerator, int dataFlow, int role, out nint ppEndpoint)
    {
        nint* vtbl = *(nint**)enumerator;
        fixed (nint* p = &ppEndpoint)
        {
            return ((delegate* unmanaged[Stdcall]<nint, int, int, nint*, int>)vtbl[4])(enumerator, dataFlow, role, p);
        }
    }

    public static int Enumerator_RegisterEndpointNotificationCallback(nint enumerator, nint pClient)
    {
        nint* vtbl = *(nint**)enumerator;
        return ((delegate* unmanaged[Stdcall]<nint, nint, int>)vtbl[6])(enumerator, pClient);
    }

    public static int Enumerator_UnregisterEndpointNotificationCallback(nint enumerator, nint pClient)
    {
        nint* vtbl = *(nint**)enumerator;
        return ((delegate* unmanaged[Stdcall]<nint, nint, int>)vtbl[7])(enumerator, pClient);
    }

    public static int Device_Activate(nint device, in Guid iid, uint clsCtx, out nint ppInterface)
    {
        Guid g = iid;
        nint* vtbl = *(nint**)device;
        fixed (nint* p = &ppInterface)
        {
            return ((delegate* unmanaged[Stdcall]<nint, Guid*, uint, nint, nint*, int>)vtbl[3])(device, &g, clsCtx, 0, p);
        }
    }

    public static int Device_OpenPropertyStore(nint device, uint stgmAccess, out nint ppProperties)
    {
        nint* vtbl = *(nint**)device;
        fixed (nint* p = &ppProperties)
        {
            return ((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)vtbl[4])(device, stgmAccess, p);
        }
    }

    public static int Device_GetId(nint device, out string id)
    {
        id = string.Empty;
        nint* vtbl = *(nint**)device;
        char* pId = null;
        int hr = ((delegate* unmanaged[Stdcall]<nint, char**, int>)vtbl[5])(device, &pId);
        if (pId != null)
        {
            try
            {
                if (hr == 0)
                    id = Marshal.PtrToStringUni((nint)pId) ?? string.Empty;
            }
            finally
            {
                Win32.CoTaskMemFree((nint)pId);
            }
        }
        return hr;
    }

    public static int PropertyStore_GetValue(nint store, in PROPERTYKEY key, out PROPVARIANT pv)
    {
        pv = default;
        PROPERTYKEY k = key;
        nint* vtbl = *(nint**)store;
        fixed (PROPVARIANT* p = &pv)
        {
            return ((delegate* unmanaged[Stdcall]<nint, PROPERTYKEY*, PROPVARIANT*, int>)vtbl[5])(store, &k, p);
        }
    }

    public static bool PolicyConfig_SetDefaultEndpoint(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return false;

        Guid clsid = CLSID_PolicyConfigClient;
        Guid iid = IID_IPolicyConfig;
        int hr = Win32.CoCreateInstance(&clsid, 0, CLSCTX_ALL, &iid, out nint policy);
        if (hr != 0 || policy == 0)
        {
            iid = IID_IPolicyConfigVista;
            hr = Win32.CoCreateInstance(&clsid, 0, CLSCTX_ALL, &iid, out policy);
            if (hr != 0 || policy == 0)
                return false;
        }

        try
        {
            nint* vtbl = *(nint**)policy;
            var setDefault = (delegate* unmanaged[Stdcall]<nint, char*, int, int>)vtbl[13];
            fixed (char* pId = deviceId)
            {
                int hrConsole = setDefault(policy, pId, eConsole);
                int hrMultimedia = setDefault(policy, pId, eMultimedia);
                setDefault(policy, pId, eCommunications);
                return hrConsole == 0 || hrMultimedia == 0;
            }
        }
        finally
        {
            Release(policy);
        }
    }

    public static bool TryCreateAudioPolicyConfigFactory(out nint factory)
    {
        factory = 0;
        if (Win32.WindowsCreateString(AudioPolicyConfigClassName, (uint)AudioPolicyConfigClassName.Length, out nint classHStr) != 0 || classHStr == 0)
            return false;

        try
        {
            Guid iid21h2 = IID_IAudioPolicyConfigFactory_21H2;
            int hr = Win32.RoGetActivationFactory(classHStr, &iid21h2, out factory);
            if (hr == 0 && factory != 0)
                return true;

            Guid iidDownlevel = IID_IAudioPolicyConfigFactory_Downlevel;
            hr = Win32.RoGetActivationFactory(classHStr, &iidDownlevel, out factory);
            if (hr == 0 && factory != 0)
                return true;

            factory = 0;
            return false;
        }
        finally
        {
            Win32.WindowsDeleteString(classHStr);
        }
    }

    public static bool AudioPolicyConfig_SetPersistedDefaultAudioEndpoint(nint factory, uint processId, string? rawDeviceId)
    {
        if (factory == 0 || processId == 0)
            return false;

        nint devHStr = 0;
        if (!string.IsNullOrWhiteSpace(rawDeviceId))
        {
            string packed = $"{MmDevApiPrefix}{rawDeviceId.Trim()}{DevInterfaceAudioRenderSuffix}";
            if (Win32.WindowsCreateString(packed, (uint)packed.Length, out devHStr) != 0)
                devHStr = 0;
        }

        try
        {
            nint* vtbl = *(nint**)factory;
            // vtable: 0..2 IUnknown, 3..5 IInspectable, 6..24 19 factory methods, 25 SetPersistedDefaultAudioEndpoint
            var setEndpoint = (delegate* unmanaged[Stdcall]<nint, uint, int, int, nint, int>)vtbl[25];
            int hrMultimedia = setEndpoint(factory, processId, eRender, eMultimedia, devHStr);
            int hrConsole = setEndpoint(factory, processId, eRender, eConsole, devHStr);
            return hrMultimedia == 0 || hrConsole == 0;
        }
        finally
        {
            if (devHStr != 0)
                Win32.WindowsDeleteString(devHStr);
        }
    }

    public static string? AudioPolicyConfig_GetPersistedDefaultAudioEndpoint(nint factory, uint processId)
    {
        if (factory == 0 || processId == 0)
            return null;

        nint* vtbl = *(nint**)factory;
        // vtable: 26 GetPersistedDefaultAudioEndpoint
        var getEndpoint = (delegate* unmanaged[Stdcall]<nint, uint, int, int, nint*, int>)vtbl[26];

        nint devHStr = 0;
        try
        {
            int hr = getEndpoint(factory, processId, eRender, eMultimedia, &devHStr);
            if (hr != 0 || devHStr == 0)
            {
                if (devHStr != 0)
                {
                    Win32.WindowsDeleteString(devHStr);
                    devHStr = 0;
                }
                hr = getEndpoint(factory, processId, eRender, eConsole, &devHStr);
            }

            if (hr != 0 || devHStr == 0)
                return null;

            nint buf = Win32.WindowsGetStringRawBuffer(devHStr, out uint len);
            if (buf == 0 || len == 0)
                return null;

            string raw = Marshal.PtrToStringUni(buf, (int)len).Trim();
            if (string.IsNullOrEmpty(raw))
                return null;

            if (raw.StartsWith(MmDevApiPrefix, StringComparison.OrdinalIgnoreCase))
                raw = raw[MmDevApiPrefix.Length..];
            if (raw.EndsWith(DevInterfaceAudioRenderSuffix, StringComparison.OrdinalIgnoreCase))
                raw = raw[..^DevInterfaceAudioRenderSuffix.Length];

            return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        }
        finally
        {
            if (devHStr != 0)
                Win32.WindowsDeleteString(devHStr);
        }
    }

    public static int SessionManager2_GetSessionEnumerator(nint mgr, out nint ppEnum)
    {
        nint* vtbl = *(nint**)mgr;
        fixed (nint* p = &ppEnum)
        {
            return ((delegate* unmanaged[Stdcall]<nint, nint*, int>)vtbl[5])(mgr, p);
        }
    }

    public static int SessionEnumerator_GetCount(nint enumerator, out int count)
    {
        nint* vtbl = *(nint**)enumerator;
        fixed (int* p = &count)
        {
            return ((delegate* unmanaged[Stdcall]<nint, int*, int>)vtbl[3])(enumerator, p);
        }
    }

    public static int SessionEnumerator_GetSession(nint enumerator, int index, out nint ppSession)
    {
        nint* vtbl = *(nint**)enumerator;
        fixed (nint* p = &ppSession)
        {
            return ((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)vtbl[4])(enumerator, index, p);
        }
    }

    public static int SessionControl_GetState(nint ctrl, out int state)
    {
        nint* vtbl = *(nint**)ctrl;
        fixed (int* p = &state)
        {
            return ((delegate* unmanaged[Stdcall]<nint, int*, int>)vtbl[3])(ctrl, p);
        }
    }

    public static int SessionControl2_GetProcessId(nint ctrl2, out uint pid)
    {
        nint* vtbl = *(nint**)ctrl2;
        fixed (uint* p = &pid)
        {
            return ((delegate* unmanaged[Stdcall]<nint, uint*, int>)vtbl[14])(ctrl2, p);
        }
    }

    public static int SessionControl2_IsSystemSoundsSession(nint ctrl2)
    {
        nint* vtbl = *(nint**)ctrl2;
        return ((delegate* unmanaged[Stdcall]<nint, int>)vtbl[15])(ctrl2);
    }

    public static int SimpleAudioVolume_SetMasterVolume(nint vol, float level)
    {
        nint* vtbl = *(nint**)vol;
        Guid ctx = Guid.Empty;
        return ((delegate* unmanaged[Stdcall]<nint, float, Guid*, int>)vtbl[3])(vol, level, &ctx);
    }

    public static int SimpleAudioVolume_GetMasterVolume(nint vol, out float level)
    {
        nint* vtbl = *(nint**)vol;
        fixed (float* p = &level)
        {
            return ((delegate* unmanaged[Stdcall]<nint, float*, int>)vtbl[4])(vol, p);
        }
    }

    public static int SimpleAudioVolume_SetMute(nint vol, bool mute)
    {
        nint* vtbl = *(nint**)vol;
        Guid ctx = Guid.Empty;
        return ((delegate* unmanaged[Stdcall]<nint, int, Guid*, int>)vtbl[5])(vol, mute ? 1 : 0, &ctx);
    }

    public static int SimpleAudioVolume_GetMute(nint vol, out bool mute)
    {
        nint* vtbl = *(nint**)vol;
        int m = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, int*, int>)vtbl[6])(vol, &m);
        mute = m != 0;
        return hr;
    }

    public static int AudioMeter_GetPeakValue(nint meter, out float peak)
    {
        nint* vtbl = *(nint**)meter;
        fixed (float* p = &peak)
        {
            return ((delegate* unmanaged[Stdcall]<nint, float*, int>)vtbl[3])(meter, p);
        }
    }

    /// <summary>
    /// Native AOT-safe COM Callable Wrapper (CCW) for IMMNotificationClient.
    /// Lives in unmanaged memory so the GC never relocates its vtable or instance pointer.
    /// </summary>
    public sealed class NotificationClientCcw : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeObject
        {
            public nint Vtbl;
            public int RefCount;
            public nint ManagedHandle;
        }

        private readonly NativeObject* _native;
        private readonly nint* _vtbl;
        private GCHandle _selfHandle;
        private readonly Action<string> _onDeviceChange;

        public nint ComPtr => (nint)_native;

        public NotificationClientCcw(Action<string> onDeviceChange)
        {
            _onDeviceChange = onDeviceChange;
            _selfHandle = GCHandle.Alloc(this);

            _vtbl = (nint*)NativeMemory.AllocZeroed(8, (nuint)sizeof(nint));
            _vtbl[0] = (nint)(delegate* unmanaged[Stdcall]<NativeObject*, Guid*, nint*, int>)&QueryInterfaceImpl;
            _vtbl[1] = (nint)(delegate* unmanaged[Stdcall]<NativeObject*, uint>)&AddRefImpl;
            _vtbl[2] = (nint)(delegate* unmanaged[Stdcall]<NativeObject*, uint>)&ReleaseImpl;
            _vtbl[3] = (nint)(delegate* unmanaged[Stdcall]<NativeObject*, char*, uint, int>)&OnDeviceStateChangedImpl;
            _vtbl[4] = (nint)(delegate* unmanaged[Stdcall]<NativeObject*, char*, int>)&OnDeviceAddedImpl;
            _vtbl[5] = (nint)(delegate* unmanaged[Stdcall]<NativeObject*, char*, int>)&OnDeviceRemovedImpl;
            _vtbl[6] = (nint)(delegate* unmanaged[Stdcall]<NativeObject*, int, int, char*, int>)&OnDefaultDeviceChangedImpl;
            _vtbl[7] = (nint)(delegate* unmanaged[Stdcall]<NativeObject*, char*, PROPERTYKEY, int>)&OnPropertyValueChangedImpl;

            _native = (NativeObject*)NativeMemory.AllocZeroed((nuint)sizeof(NativeObject));
            _native->Vtbl = (nint)_vtbl;
            _native->RefCount = 1;
            _native->ManagedHandle = GCHandle.ToIntPtr(_selfHandle);
        }

        private void RaiseChange(string reason)
        {
            try { _onDeviceChange(reason); } catch { }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int QueryInterfaceImpl(NativeObject* self, Guid* riid, nint* ppv)
        {
            if (ppv == null || riid == null) return unchecked((int)0x80004003);
            if (*riid == IID_IUnknown || *riid == IID_IMMNotificationClient)
            {
                *ppv = (nint)self;
                Interlocked.Increment(ref self->RefCount);
                return 0;
            }
            *ppv = 0;
            return unchecked((int)0x80004002); // E_NOINTERFACE
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint AddRefImpl(NativeObject* self)
            => (uint)Interlocked.Increment(ref self->RefCount);

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint ReleaseImpl(NativeObject* self)
            => (uint)Math.Max(0, Interlocked.Decrement(ref self->RefCount));

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int OnDeviceStateChangedImpl(NativeObject* self, char* pwstrDeviceId, uint dwNewState)
        {
            if (self->ManagedHandle != 0)
            {
                var target = GCHandle.FromIntPtr(self->ManagedHandle).Target as NotificationClientCcw;
                target?.RaiseChange(dwNewState != DEVICE_STATE_ACTIVE ? "DeviceStateChanged" : "DeviceEnabled");
            }
            return 0;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int OnDeviceAddedImpl(NativeObject* self, char* pwstrDeviceId)
        {
            if (self->ManagedHandle != 0)
            {
                var target = GCHandle.FromIntPtr(self->ManagedHandle).Target as NotificationClientCcw;
                target?.RaiseChange("DeviceAdded");
            }
            return 0;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int OnDeviceRemovedImpl(NativeObject* self, char* pwstrDeviceId)
        {
            if (self->ManagedHandle != 0)
            {
                var target = GCHandle.FromIntPtr(self->ManagedHandle).Target as NotificationClientCcw;
                target?.RaiseChange("DeviceRemoved");
            }
            return 0;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int OnDefaultDeviceChangedImpl(NativeObject* self, int flow, int role, char* pwstrDefaultDeviceId)
        {
            if (flow == eRender && (role == eMultimedia || role == eConsole) && self->ManagedHandle != 0)
            {
                var target = GCHandle.FromIntPtr(self->ManagedHandle).Target as NotificationClientCcw;
                target?.RaiseChange("DefaultOutputChanged");
            }
            return 0;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int OnPropertyValueChangedImpl(NativeObject* self, char* pwstrDeviceId, PROPERTYKEY key) => 0;

        public void Dispose()
        {
            if (_selfHandle.IsAllocated)
                _selfHandle.Free();
            if (_native != null)
            {
                _native->ManagedHandle = 0;
                NativeMemory.Free(_native);
            }
            if (_vtbl != null)
            {
                NativeMemory.Free(_vtbl);
            }
        }
    }
}
