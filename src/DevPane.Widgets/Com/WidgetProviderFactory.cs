using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace DevPane.Widgets.Com;

[ComImport]
[ComVisible(false)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("00000001-0000-0000-C000-000000000046")]
internal interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);

    [PreserveSig]
    int LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
}

/// <summary>
/// Creates a <see cref="WidgetProvider"/> whenever the Widgets Board asks for one.
/// </summary>
[ComVisible(true)]
internal sealed class WidgetProviderFactory : IClassFactory
{
    private const int ClassENoAggregation = unchecked((int)0x80040110);

    public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
    {
        ppvObject = IntPtr.Zero;
        if (pUnkOuter != IntPtr.Zero)
        {
            return ClassENoAggregation;
        }

        IntPtr inspectable = MarshalInspectable<IWidgetProvider>.FromManaged(new WidgetProvider());
        try
        {
            // Hand back whichever interface Windows asked for (IUnknown, IWidgetProvider, ...).
            return Marshal.QueryInterface(inspectable, ref riid, out ppvObject);
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }

    public int LockServer(bool fLock) => 0;
}
