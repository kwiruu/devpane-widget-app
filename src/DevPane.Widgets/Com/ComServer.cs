using System.Runtime.InteropServices;

namespace DevPane.Widgets.Com;

/// <summary>
/// Registers the provider's class factory so Windows can create <see cref="WidgetProvider"/> objects in this process.
/// </summary>
internal static class ComServer
{
    private const uint ClsctxLocalServer = 0x4;
    private const uint RegclsMultipleUse = 0x1;

    public static uint Register(Guid clsid, object classFactory)
    {
        int hr = CoRegisterClassObject(clsid, classFactory, ClsctxLocalServer, RegclsMultipleUse, out uint cookie);
        Marshal.ThrowExceptionForHR(hr);
        return cookie;
    }

    public static void Revoke(uint cookie) => CoRevokeClassObject(cookie);

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
        [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        uint dwClsContext,
        uint flags,
        out uint lpdwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint dwRegister);
}
