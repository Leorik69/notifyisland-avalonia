using System;
using System.Runtime.InteropServices;

namespace NotifyIsland;

/// <summary>
/// Raises <see cref="Published"/> whenever Windows shows a toast, from any app.
/// <para>
/// <c>UserNotificationListener.NotificationChanged</c> is the documented way to hear this, but it
/// throws <c>0x80070490</c> (element not found) in an unpackaged process — checked on Windows 11
/// 26100 — and this app ships unpackaged. The shell does publish the same moment through the
/// Windows Notification Facility state <c>WNF_SHEL_TOAST_PUBLISHED</c>, which any process can
/// subscribe to. It is undocumented, so this class is strictly fail-soft: if the subscription is
/// refused, <see cref="IsLive"/> stays false and the listener keeps its plain 2 s poll.
/// </para>
/// <para>
/// The callback arrives on an OS thread-pool thread and only raises the event; it never touches
/// WinRT or UI state itself. The subscription also delivers the current state once right after
/// subscribing — that is one extra read at startup, harmless, and not worth an extra undocumented
/// query to suppress.
/// </para>
/// </summary>
public sealed class ToastPublishedSignal : IDisposable
{
    private const ulong WnfShelToastPublished = 0x0D83063EA3BC1035UL;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int WnfCallback(ulong stateName, uint changeStamp, IntPtr typeId,
        IntPtr context, IntPtr buffer, uint bufferSize);

    [DllImport("ntdll.dll")]
    private static extern int RtlSubscribeWnfStateChangeNotification(out IntPtr subscription,
        ulong stateName, uint changeStamp, WnfCallback callback, IntPtr context, IntPtr typeId,
        uint serializationGroup, uint unknown);

    [DllImport("ntdll.dll")]
    private static extern int RtlUnsubscribeWnfStateChangeNotification(IntPtr subscription);

    // Held in a field: the OS keeps only the raw function pointer, so a collected delegate would
    // be a callback into freed memory.
    private readonly WnfCallback _callback;
    private IntPtr _subscription;

    /// <summary>Raised on an OS thread-pool thread each time a toast is published.</summary>
    public event Action? Published;

    /// <summary>True while the OS accepted the subscription.</summary>
    public bool IsLive => _subscription != IntPtr.Zero;

    public ToastPublishedSignal()
    {
        _callback = OnChanged;
        try
        {
            var status = RtlSubscribeWnfStateChangeNotification(out _subscription,
                WnfShelToastPublished, 0, _callback, IntPtr.Zero, IntPtr.Zero, 0, 0);
            if (status != 0)
            {
                _subscription = IntPtr.Zero;
                AppLog.Info($"ToastPublishedSignal: subscribe refused (0x{status:X8}) — falling back to polling");
            }
        }
        catch (Exception ex)
        {
            _subscription = IntPtr.Zero;
            AppLog.Info($"ToastPublishedSignal: unavailable ({ex.GetType().Name}) — falling back to polling");
        }
    }

    private int OnChanged(ulong stateName, uint changeStamp, IntPtr typeId, IntPtr context,
        IntPtr buffer, uint bufferSize)
    {
        try { Published?.Invoke(); }
        catch (Exception ex) { AppLog.Warn("ToastPublishedSignal subscriber failed", ex); }
        return 0;
    }

    public void Dispose()
    {
        var sub = _subscription;
        _subscription = IntPtr.Zero;
        if (sub == IntPtr.Zero) return;
        try { RtlUnsubscribeWnfStateChangeNotification(sub); }
        catch (Exception ex) { AppLog.Warn("ToastPublishedSignal unsubscribe failed", ex); }
        GC.KeepAlive(_callback);
    }
}