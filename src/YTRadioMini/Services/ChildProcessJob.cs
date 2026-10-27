using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace YTRadioMini.Services;

/// <summary>
/// Windows Job Object（KILL_ON_JOB_CLOSE）：把本程序启动的 mpv / yt-dlp 放进同一个作业，
/// 本进程退出（包括被强制结束或崩溃）时由系统回收整个作业，包括 mpv 自己再启动的 yt-dlp。
/// 仅 Windows 有效，其他平台上 <see cref="TryAssign"/> 直接返回 false；失败不影响程序运行。
/// </summary>
public static class ChildProcessJob
{
    private static readonly object Gate = new();
    private static nint _job;
    private static bool _initFailed;

    public static bool TryAssign(Process process)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            return AssignWindows(process);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool AssignWindows(Process process)
    {
        lock (Gate)
        {
            if (_initFailed) return false;
            if (_job == 0) _initFailed = !CreateJob();
            if (_job == 0) return false;
            return AssignProcessToJobObject(_job, process.Handle);
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool CreateJob()
    {
        var job = CreateJobObject(0, null);
        if (job == 0) return false;

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

        var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)size))
            {
                CloseHandle(job);
                return false;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        _job = job; // 句柄故意不关闭：进程结束时由系统关闭，触发 KILL_ON_JOB_CLOSE。
        return true;
    }

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    private const int JobObjectExtendedLimitInformation = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObject(nint attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(nint job, int infoClass, nint info, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint job, nint process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
