using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RimTube.Core
{
    // Windows closes the job when the game exits, including an abrupt crash.
    internal sealed class OwnedProcess : IDisposable
    {
        private IntPtr job;
        public Process Process { get; private set; }
        [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long PerProcess, PerJob; public uint Flags; public UIntPtr Min, Max; public uint Active; public UIntPtr Affinity; public uint Priority, Scheduling; }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcess, PeakJob; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(IntPtr job, int kind, ref ExtendedLimits limits, uint length);
        [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        public OwnedProcess(ProcessStartInfo start)
        {
            job = CreateJobObject(IntPtr.Zero, null);
            var limits = new ExtendedLimits(); limits.Basic.Flags = 0x2000; // KILL_ON_JOB_CLOSE
            if (job == IntPtr.Zero || !SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf(typeof(ExtendedLimits))))
            { Dispose(); throw new MediaException(SourceErrorCode.BackendUnavailable, LocalizedText.T("RimTube.CouldNotProtectThePlayerProcessMpvWas")); }
            try
            {
                Process = Process.Start(start);
                if (Process == null || !AssignProcessToJobObject(job, Process.Handle)) throw new InvalidOperationException();
            }
            catch { Dispose(); throw new MediaException(SourceErrorCode.BackendUnavailable, LocalizedText.T("RimTube.CouldNotStartOrProtectTheMpvChild")); }
        }
        public void Dispose()
        {
            if (job != IntPtr.Zero) { CloseHandle(job); job = IntPtr.Zero; }
            var p = Process; Process = null;
            if (p != null) { try { if (!p.HasExited) p.Kill(); } catch { } p.Dispose(); }
        }
    }
}
