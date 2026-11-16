using System;
using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace EarTrumpet.Quieti;

/// <summary>
///     Foreground window hook (EVENT_SYSTEM_FOREGROUND) and foreground reading. The store app frame
///     (ApplicationFrameHost) is unwrapped to the real app inside it. Port of the Rust foreground.rs.
/// </summary>
internal static class ForegroundWatcher
{
    private const string StoreAppFrameExe = "applicationframehost.exe";

    // Rooted for as long as the hook may call back (the native side only holds a function pointer).
    private static WINEVENTPROC s_proc;
    private static HWINEVENTHOOK s_hook;
    private static Action s_onChange;

    /// <summary>
    ///     Calls onChange whenever the foreground window changes. Install on the UI thread: with
    ///     WINEVENT_OUTOFCONTEXT the callback runs on the installing thread while it pumps messages.
    ///     Our own windows are reported too (they are neutral and ignored by the caller).
    /// </summary>
    public static void Install(Action onChange)
    {
        Uninstall();
        s_onChange = onChange;
        s_proc ??= OnEvent;
        s_hook = PInvoke.SetWinEventHook(
            PInvoke.EVENT_SYSTEM_FOREGROUND,
            PInvoke.EVENT_SYSTEM_FOREGROUND,
            default(HMODULE),
            s_proc,
            0,
            0,
            PInvoke.WINEVENT_OUTOFCONTEXT);
        if ((IntPtr)s_hook == IntPtr.Zero)
        {
            Trace.WriteLine("Quieti ForegroundWatcher SetWinEventHook failed");
        }
    }

    public static void Uninstall()
    {
        if ((IntPtr)s_hook != IntPtr.Zero)
        {
            PInvoke.UnhookWinEvent(s_hook);
            s_hook = default;
        }
        s_onChange = null;
    }

    private static void OnEvent(HWINEVENTHOOK hook, uint @event, HWND hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        // A managed exception must not cross the native user32 callback (it would take the process down).
        try
        {
            unsafe
            {
                var now = PInvoke.GetForegroundWindow();
                Trace.WriteLine($"Quieti foreground event hwnd=0x{(nint)hwnd.Value:x} {ClassOf(hwnd)} now=0x{(nint)now.Value:x} {ClassOf(now)}");
            }
            s_onChange?.Invoke();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Quieti ForegroundWatcher callback failed: {ex}");
        }
    }

    /// <summary>Handle of the current foreground window (0 when there is none). Cheap: for polling.</summary>
    public static unsafe nint CurrentHandle() => (nint)PInvoke.GetForegroundWindow().Value;

    /// <summary>The current foreground window, or null when there is none. ownPid is this process.</summary>
    public static (uint Pid, FgWindow Window)? Read(uint ownPid)
    {
        var hwnd = PInvoke.GetForegroundWindow();
        if ((IntPtr)hwnd == IntPtr.Zero)
        {
            return null;
        }
        var windowClass = ClassOf(hwnd);
        var pid = PidOf(hwnd);
        var exe = ExeOf(pid);
        if (exe == StoreAppFrameExe)
        {
            // Store app: the child window owned by a different process is the real app.
            var inner = InnerPid(hwnd, pid);
            if (inner != 0)
            {
                pid = inner;
                exe = ExeOf(pid);
            }
        }
        return (pid, new FgWindow(exe, windowClass, pid == ownPid));
    }

    private static uint InnerPid(HWND frame, uint framePid)
    {
        uint found = 0;
        WNDENUMPROC callback = (child, _) =>
        {
            var childPid = PidOf(child);
            if (childPid != framePid)
            {
                found = childPid;
                return false;
            }
            return true;
        };
        PInvoke.EnumChildWindows(frame, callback, default);
        GC.KeepAlive(callback);
        return found;
    }

    private static unsafe uint PidOf(HWND hwnd)
    {
        uint pid = 0;
        _ = PInvoke.GetWindowThreadProcessId(hwnd, &pid);
        return pid;
    }

    private static unsafe string ClassOf(HWND hwnd)
    {
        const int capacity = 256;
        var buffer = stackalloc char[capacity];
        var length = PInvoke.GetClassName(hwnd, buffer, capacity);
        return length > 0 ? new string(buffer, 0, length) : "";
    }

    private static string ExeOf(uint pid)
    {
        var chain = ProcessTree.Chain(pid);
        return chain.Count > 0 ? chain[0].Exe : "";
    }
}
