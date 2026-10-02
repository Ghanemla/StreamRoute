using System.Runtime.InteropServices;

namespace StreamRoute.Interop;

internal static class WinRtFactory
{
    private const int RpcEChangedMode = unchecked((int)0x80010106);
    private const uint RoInitSingleThreaded = 0;

    [DllImport("combase.dll")]
    private static extern int RoInitialize(uint initType);

    [DllImport("combase.dll")]
    private static extern void RoUninitialize();

    [DllImport("combase.dll", PreserveSig = false)]
    private static extern void RoGetActivationFactory(
        IntPtr activatableClassId,
        ref Guid iid,
        out IntPtr factory);

    public static bool InitializeForCurrentThread()
    {
        var hr = RoInitialize(RoInitSingleThreaded);
        if (hr >= 0)
        {
            // S_OK and S_FALSE must both be balanced by RoUninitialize.
            return true;
        }

        if (hr == RpcEChangedMode)
        {
            // The thread already has a COM apartment with a different concurrency model.
            // Activation may still work; do not uninitialize something we did not initialize.
            return false;
        }

        Marshal.ThrowExceptionForHR(hr);
        return false;
    }

    public static void UninitializeForCurrentThread() => RoUninitialize();

    public static object? GetFactory(string runtimeClassName, Guid iid)
    {
        var classNameHandle = HString.Create(runtimeClassName);
        try
        {
            RoGetActivationFactory(classNameHandle, ref iid, out var pFactory);
            if (pFactory == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.GetObjectForIUnknown(pFactory);
            }
            finally
            {
                Marshal.Release(pFactory);
            }
        }
        finally
        {
            HString.Delete(classNameHandle);
        }
    }
}
