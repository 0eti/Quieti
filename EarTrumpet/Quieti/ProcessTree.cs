using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.System.Diagnostics.ToolHelp;

namespace EarTrumpet.Quieti;

/// <summary>Process parent chains from one Toolhelp snapshot (port of the Rust process.rs::chain).</summary>
internal static class ProcessTree
{
    /// <summary>Longest chain followed (guards against cycles and odd chains).</summary>
    private const int MaxChain = 32;

    /// <summary>
    ///     The process itself followed by its parents, stopping after explorer.exe.
    ///     Exe is the AppRules.AppKey of the image name; Toolhelp reads it for elevated processes too.
    ///     A pid missing from the snapshot (it just ended) yields only itself with an empty Exe.
    /// </summary>
    public static IReadOnlyList<Proc> Chain(uint pid)
    {
        var table = ProcessTable();
        var chain = new List<Proc>();
        var cur = pid;
        while (chain.Count < MaxChain)
        {
            if (!table.TryGetValue(cur, out var entry))
            {
                if (chain.Count == 0)
                {
                    chain.Add(new Proc(pid, ""));
                }
                break;
            }
            if (chain.Exists(p => p.Pid == cur))
            {
                break;
            }
            chain.Add(new Proc(cur, entry.Exe));
            if (entry.Exe == AppRules.Shell || entry.Parent == 0 || entry.Parent == cur)
            {
                break;
            }
            cur = entry.Parent;
        }
        return chain;
    }

    /// <summary>pid → (parent pid, app key of the image name).</summary>
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
