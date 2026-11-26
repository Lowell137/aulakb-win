using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace AulaKb.Core.Hid;

/// <summary>
/// HID input/output reports on Windows. The 2.4G receiver has a collection with usage page
/// 0xFF02, report id 0x13 and 20-byte in/out reports; unlike the cable it uses no feature
/// reports, so reads need a timeout (a sleeping keyboard sends nothing).
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsHidStream : IReportDevice
{
    readonly SafeFileHandle _handle;

    public string Path { get; }

    public WindowsHidStream(string path)
    {
        Path = path;
        _handle = CreateFileW(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (_handle.IsInvalid)
            throw new IOException($"Could not open HID device (win32 error {Marshal.GetLastWin32Error()}).");
    }

    public bool WriteReport(byte[] report) => Transfer(report, write: true, 1000) == report.Length;

    public int ReadReport(byte[] buffer, int timeoutMs) => Transfer(buffer, write: false, timeoutMs);

    int Transfer(byte[] buffer, bool write, int timeoutMs)
    {
        IntPtr overlap = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
        IntPtr ev = CreateEventW(IntPtr.Zero, true, false, null);
        try
        {
            Marshal.StructureToPtr(new NativeOverlapped { EventHandle = ev }, overlap, false);
            bool ok = write
                ? WriteFile(_handle, buffer, buffer.Length, out _, overlap)
                : ReadFile(_handle, buffer, buffer.Length, out _, overlap);
            if (!ok && Marshal.GetLastWin32Error() != ERROR_IO_PENDING)
                return -1;
            if (ok)
                return GetOverlappedResult(_handle, overlap, out int done, false) ? done : -1;
            if (WaitForSingleObject(ev, (uint)Math.Max(1, timeoutMs)) != WAIT_OBJECT_0)
            {
                CancelIoEx(_handle, overlap);
                return -1;
            }
            return GetOverlappedResult(_handle, overlap, out int count, false) ? count : -1;
        }
        finally
        {
            CloseHandle(ev);
            Marshal.FreeHGlobal(overlap);
        }
    }

    public void Dispose() => _handle.Dispose();

    const uint GENERIC_READ = 0x80000000;
    const uint GENERIC_WRITE = 0x40000000;
    const uint FILE_SHARE_READ = 0x1;
    const uint FILE_SHARE_WRITE = 0x2;
    const uint OPEN_EXISTING = 3;
    const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    const int ERROR_IO_PENDING = 997;
    const uint WAIT_OBJECT_0 = 0;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool ReadFile(SafeFileHandle file, byte[] buffer, int count, out int read, IntPtr overlap);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool WriteFile(SafeFileHandle file, byte[] buffer, int count, out int written, IntPtr overlap);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool GetOverlappedResult(SafeFileHandle file, IntPtr overlap, out int transferred, [MarshalAs(UnmanagedType.U1)] bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlap);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateEventW(IntPtr security, [MarshalAs(UnmanagedType.U1)] bool manualReset,
        [MarshalAs(UnmanagedType.U1)] bool initialState, string? name);

    [DllImport("kernel32.dll")]
    static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool CloseHandle(IntPtr handle);
}
