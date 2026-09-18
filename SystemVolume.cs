using System;
using System.Runtime.InteropServices;

namespace NotifyIsland;

internal static class SystemVolume
{
    public static string Status { get; private set; } = "Off";

    public static bool TryGet(out float scalar, out bool mute)
    {
        scalar = 0;
        mute = false;
        try
        {
            var vol = Endpoint();
            if (vol is null) return false;
            vol.GetMasterVolumeLevelScalar(out scalar);
            vol.GetMute(out var m);
            mute = m;
            Status = mute ? "Muted" : $"{scalar * 100:0}%";
            return true;
        }
        catch (Exception ex)
        {
            Status = "Unavailable (" + ex.GetType().Name + ")";
            return false;
        }
    }

    public static bool TrySet(float scalar)
    {
        try
        {
            var vol = Endpoint();
            if (vol is null) return false;
            var hr = vol.SetMasterVolumeLevelScalar(Math.Clamp(scalar, 0f, 1f), IntPtr.Zero);
            if (hr < 0) { Status = "Set HRESULT 0x" + hr.ToString("X8"); return false; }
            return TryGet(out _, out _);
        }
        catch (Exception ex)
        {
            Status = "Set failed (" + ex.GetType().Name + ")";
            return false;
        }
    }

    public static bool TrySetMute(bool mute)
    {
        try
        {
            var vol = Endpoint();
            if (vol is null) return false;
            var hr = vol.SetMute(mute, IntPtr.Zero);
            if (hr < 0) { Status = "Mute HRESULT 0x" + hr.ToString("X8"); return false; }
            return TryGet(out _, out _);
        }
        catch (Exception ex)
        {
            Status = "Mute failed (" + ex.GetType().Name + ")";
            return false;
        }
    }

    private static IAudioEndpointVolume? Endpoint()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        enumerator.GetDefaultAudioEndpoint(0, 0, out var device);
        if (device is null) { Status = "No default render device"; return null; }
        var iid = typeof(IAudioEndpointVolume).GUID;
        device.Activate(ref iid, 1, IntPtr.Zero, out var obj);
        return obj as IAudioEndpointVolume;
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, IntPtr eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, IntPtr eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float levelDb, IntPtr eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float level, IntPtr eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
