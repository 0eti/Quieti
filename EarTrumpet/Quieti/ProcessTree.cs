using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.System.Diagnostics.ToolHelp;

namespace EarTrumpet.Quieti;

/// <summary>
///     Process parent chains (ProcessChain.Walk). Each process on the chain is asked for directly
///     (name, parent, creation time), which takes well under a millisecond. A full Toolhelp snapshot
///     costs tens of milliseconds on a busy system, so it is only the fallback for a process that
///     refuses to be queried.
/// </summary>
internal static class ProcessTree
{
    /// <summary>
    ///     The process itself followed by its parents, stopping after explorer.exe.
    ///     Exe is the AppRules.AppKey of the image name.
    ///     A pid that is gone yields only itself with an empty Exe.
    /// </summary>
    public static IReadOnlyList<Proc> Chain(uint pid)
    {
        var chain = ProcessChain.Walk(pid, Query);
        if (chain != null)
        {
            return chain;
        }
        var table = ProcessTable();
        return ProcessChain.Walk(pid, p => table.TryGetValue(p, out var e) ? ProcLookup.Found(e.Parent, e.Exe, 0) : ProcLookup.Gone)
            ?? [new Proc(pid, "")];
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int ERROR_INVALID_PARAMETER = 87;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder name, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(SafeProcessHandle process, int infoClass, ref PROCESS_BASIC_INFORMATION info, int size, out int returned);

    /// <summary>One process, asked for directly.</summary>
    private static ProcLookup Query(uint pid)
    {
        try
        {
            using var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process.IsInvalid)
            {
                // Gone (or pid 0); anything else (access denied: System, some protected processes) → fallback.
                return Marshal.GetLastWin32Error() == ERROR_INVALID_PARAMETER ? ProcLookup.Gone : ProcLookup.Denied;
            }

            var size = 1024u;
            var name = new StringBuilder((int)size);
            if (!QueryFullProcessImageNameW(process, 0, name, ref size))
            {
                return ProcLookup.Denied;
            }

            var info = new PROCESS_BASIC_INFORMATION();
            if (NtQueryInformationProcess(process, 0 /* ProcessBasicInformation */, ref info, Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _) != 0)
            {
                return ProcLookup.Denied;
            }

            var created = GetProcessTimes(process, out var creation, out _, out _, out _) ? creation : 0;
            return ProcLookup.Found((uint)info.InheritedFromUniqueProcessId.ToInt64(), AppRules.AppKey(Path.GetFileName(name.ToString())), created);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Quieti ProcessTree query {pid} failed {ex.Message}");
            return ProcLookup.Denied;
        }
    }

    /// <summary>pid → (parent pid, app key of the image name), from one Toolhelp snapshot (fallback).</summary>
    private static Dictionary<uint, (uint Parent, string Exe)> ProcessTable()
    {
        var map = new Dictionary<uint, (uint Parent, string Exe)>();
        try
        {
            var raw = PInvoke.CreateToolhelp32Snapshot(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);
            using var snapshot = new SafeFileHandle((IntPtr)raw, ownsHandle: true);
            if (snapshot.IsInvalid)
            {
                return map;
            }
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            var ok = PInvoke.Process32FirstW(snapshot, ref entry);
            while (ok)
            {
                var name = entry.szExeFile.ToString();
                var end = name.IndexOf('\0');
                map[entry.th32ProcessID] = (entry.th32ParentProcessID, AppRules.AppKey(end < 0 ? name : name[..end]));
                ok = PInvoke.Process32NextW(snapshot, ref entry);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Quieti ProcessTree snapshot failed {ex.Message}");
        }
        return map;
    }
}
