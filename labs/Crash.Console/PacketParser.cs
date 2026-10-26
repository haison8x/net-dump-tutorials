using System.Runtime.InteropServices;

namespace CrashLab;

// Native crash: a P/Invoke into the C runtime with a null pointer. The access
// violation happens INSIDE native code (strlen), so the interesting frames are
// native ones - dotnet-dump cannot show them, WinDbg can (docs/10-appendix-windbg.md).
public sealed class PacketParser
{
    public int HeaderLength(string? header)
    {
        var buffer = header is null ? IntPtr.Zero : Marshal.StringToHGlobalAnsi(header);
        try
        {
            // BUG: no null check - strlen(NULL) dereferences address 0 inside the C runtime.
            return (int)(OperatingSystem.IsWindows() ? WindowsCrt.strlen(buffer) : Libc.strlen(buffer));
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    private static class WindowsCrt
    {
        [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern nuint strlen(IntPtr str);
    }

    private static class Libc
    {
        [DllImport("libc", CallingConvention = CallingConvention.Cdecl)]
        public static extern nuint strlen(IntPtr str);
    }
}
