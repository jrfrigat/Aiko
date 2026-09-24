using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Aiko.Mcp.Specs;

/// <summary>
/// A daemon a spec starts, tied to the life of the test host rather than to the spec that started it.
/// </summary>
/// <remarks>
/// The daemon is a process of its own, and its end used to live only in managed code - the fixture's
/// <c>DisposeAsync</c>, or a spec's <c>finally</c>. When the test host dies without unwinding (a cancelled
/// run, a crashed host, a task whose console went away), none of that runs, and the daemon stays alive
/// holding the files under <c>src/Aiko.Server/bin</c> - which is the <c>MSB3021</c> the next publish then
/// hits. Every daemon is therefore put in a Windows job object with
/// <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>: the job handle lives in this process, so the system kills the
/// daemons when the test host ends, whatever ends it. One job per host rather than one per daemon, and it is
/// configured before the first daemon is assigned to it.
/// </remarks>
internal sealed class SpecDaemon : IDisposable
{
    private readonly Process _process;
    private bool _disposed;

    private SpecDaemon(Process process) => _process = process;

    /// <summary>The running process, for a spec that checks it stopped on its own.</summary>
    public Process Process => _process;

    /// <summary>
    /// Starts <paramref name="serverDll"/> in <paramref name="workingDirectory"/> with the given
    /// environment, and assigns it to the host's job object before returning.
    /// </summary>
    /// <param name="serverDll">The built <c>Aiko.Server.dll</c> to run.</param>
    /// <param name="workingDirectory">The throwaway root the daemon runs in.</param>
    /// <param name="environment">The <c>AIKO_*</c> variables this daemon should get.</param>
    public static SpecDaemon Start(
        string serverDll,
        string workingDirectory,
        params (string Name, string Value)[] environment)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{serverDll}\"",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        AikoServerFixture.ClearInheritedAikoVariables(startInfo);
        foreach (var (name, value) in environment)
        {
            startInfo.EnvironmentVariables[name] = value;
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The Aiko daemon did not start.");
        AssignToHostJob(process);
        return new SpecDaemon(process);
    }

    /// <summary>
    /// Ends the daemon: the process tree first, because that is what makes the spec's own teardown prompt,
    /// and the job object after it as the guarantee that nothing survives the test host.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (!_process.HasExited)
            {
                // A spec that stopped the daemon through its own endpoint leaves it already gone - and a
                // second kill of a process that has exited is an error, not a no-op.
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(10_000);
            }
        }
        catch (InvalidOperationException)
        {
            // The process ended between the check and the kill.
        }
        catch (NotSupportedException)
        {
            // A process this host cannot observe; the job object still holds it.
        }

        _process.Dispose();
    }

    /// <summary>
    /// The job every daemon of this test host is assigned to, or <see cref="IntPtr.Zero"/> where the platform
    /// has no job objects - there the managed teardown is the only thing that ends a daemon.
    /// </summary>
    /// <remarks>
    /// Created on first use and deliberately never closed: the handle is what ties the daemons to this
    /// process, so closing it early would kill them mid-run.
    /// </remarks>
    private static readonly IntPtr HostJob = CreateKillOnCloseJob();

    private static IntPtr CreateKillOnCloseJob()
    {
        if (!OperatingSystem.IsWindows())
        {
            return IntPtr.Zero;
        }

        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var information = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };

        var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(information, pointer, fDeleteOld: false);
            if (!SetInformationJobObject(job, ExtendedLimitInformationClass, pointer, (uint)size))
            {
                CloseHandle(job);
                return IntPtr.Zero;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }

        return job;
    }

    private static void AssignToHostJob(Process process)
    {
        if (HostJob == IntPtr.Zero)
        {
            return;
        }

        // A daemon the system refused to take into the job is still ended by Dispose, so a failure is not
        // worth failing the spec over - the guarantee is simply narrower for that one process.
        AssignProcessToJobObject(HostJob, process.Handle);
    }

    /// <summary>Kills every process left in the job when its last handle closes.</summary>
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    /// <summary><c>JobObjectExtendedLimitInformation</c>, the class that carries that flag.</summary>
    private const int ExtendedLimitInformationClass = 9;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        IntPtr job,
        int informationClass,
        IntPtr information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
