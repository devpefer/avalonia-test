using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AvaloniaTest2.Helpers;

public static class FileSizeHelper
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

    public static Task<long> GetPhysicalSizeAsync(string path)
    {
        return Task.Run(() => GetPhysicalSize(path));
    }
    
    private static long GetPhysicalSize(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                uint high;
                uint low = GetCompressedFileSizeW(path, out high);
                long size = ((long)high << 32) + low;

                if (size == -1)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != 0) return new FileInfo(path).Length;
                }

                return size;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (stat_linux(path, out var st) == 0)
                    return st.st_blocks * 512L;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (stat_osx(path, out var st) == 0)
                    return st.st_blocks * 512L;
            }

            return new FileInfo(path).Length;
        }
        catch
        {
            return new FileInfo(path).Length;
        }
    }

    // ==================
    // Linux (glibc)
    // ==================
    [StructLayout(LayoutKind.Sequential)]
    private struct StatLinux
    {
        public ulong st_dev;
        public ulong st_ino;
        public ulong st_nlink;
        public uint st_mode;
        public uint st_uid;
        public uint st_gid;
        public ulong st_rdev;
        public long st_size;
        public long st_blksize;
        public long st_blocks;
    }
    [DllImport("libc", SetLastError = true, EntryPoint = "stat")]
    private static extern int stat_linux(string path, out StatLinux buf);

    // ==================
    // macOS (Darwin)
    // ==================
    [StructLayout(LayoutKind.Sequential)]
    private struct StatOSX
    {
        public uint st_dev;
        public ushort st_mode;
        public ushort st_nlink;
        public ulong st_ino;
        public uint st_uid;
        public uint st_gid;
        public uint st_rdev;
        public long st_atime;
        public long st_atime_nsec;
        public long st_mtime;
        public long st_mtime_nsec;
        public long st_ctime;
        public long st_ctime_nsec;
        public long st_birthtime;
        public long st_birthtime_nsec;
        public long st_size;
        public long st_blocks;
        public long st_blksize;
    }
    [DllImport("libc", SetLastError = true, EntryPoint = "stat")]
    private static extern int stat_osx(string path, out StatOSX buf);
}