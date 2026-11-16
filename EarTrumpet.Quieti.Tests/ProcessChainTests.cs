using EarTrumpet.Quieti;

namespace EarTrumpet.Quieti.Tests;

public class ProcessChainTests
{
    private static Func<uint, ProcLookup> Table(params (uint Pid, uint Parent, string Exe, long CreatedAt)[] rows)
    {
        var map = rows.ToDictionary(r => r.Pid);
        return pid => map.TryGetValue(pid, out var r)
            ? ProcLookup.Found(r.Parent, r.Exe, r.CreatedAt)
            : ProcLookup.Gone;
    }

    private static uint[] Pids(IReadOnlyList<Proc>? chain) => chain!.Select(p => p.Pid).ToArray();

    [Fact]
    public void walks_up_to_and_including_explorer()
    {
        var chain = ProcessChain.Walk(30, Table(
            (30, 20, "msedgewebview2.exe", 0), (20, 10, "teams.exe", 0), (10, 5, "explorer.exe", 0), (5, 1, "userinit.exe", 0)));
        Assert.Equal(new uint[] { 30, 20, 10 }, Pids(chain));
        Assert.Equal("teams.exe", chain![1].Exe);
    }

    [Fact]
    public void missing_process_yields_itself_with_empty_exe()
    {
        var chain = ProcessChain.Walk(42, Table());
        Assert.Equal(new[] { new Proc(42, "") }, chain);
    }

    [Fact]
    public void stops_when_the_parent_is_gone()
    {
        var chain = ProcessChain.Walk(30, Table((30, 20, "a.exe", 0)));
        Assert.Equal(new uint[] { 30 }, Pids(chain));
    }

    [Fact]
    public void stops_at_parent_zero_or_self_parent()
    {
        Assert.Equal(new uint[] { 30 }, Pids(ProcessChain.Walk(30, Table((30, 0, "a.exe", 0)))));
        Assert.Equal(new uint[] { 30 }, Pids(ProcessChain.Walk(30, Table((30, 30, "a.exe", 0)))));
    }

    [Fact]
    public void stops_on_a_cycle()
    {
        var chain = ProcessChain.Walk(1, Table((1, 2, "a.exe", 0), (2, 1, "b.exe", 0)));
        Assert.Equal(new uint[] { 1, 2 }, Pids(chain));
    }

    [Fact]
    public void stops_after_max_chain()
    {
        var rows = Enumerable.Range(1, 100).Select(i => ((uint)i, (uint)(i + 1), "x.exe", 0L)).ToArray();
        Assert.Equal(ProcessChain.MaxChain, ProcessChain.Walk(1, Table(rows))!.Count);
    }

    [Fact]
    public void parent_younger_than_child_is_a_reused_pid_and_not_followed()
    {
        // pid 20 now belongs to a program started after pid 30: the real parent is gone.
        var chain = ProcessChain.Walk(30, Table((30, 20, "a.exe", 100), (20, 10, "unrelated.exe", 200), (10, 1, "explorer.exe", 50)));
        Assert.Equal(new uint[] { 30 }, Pids(chain));
    }

    [Fact]
    public void older_parent_is_followed_and_unknown_times_are_trusted()
    {
        Assert.Equal(new uint[] { 30, 20 }, Pids(ProcessChain.Walk(30, Table((30, 20, "a.exe", 100), (20, 0, "b.exe", 50)))));
        Assert.Equal(new uint[] { 30, 20 }, Pids(ProcessChain.Walk(30, Table((30, 20, "a.exe", 100), (20, 0, "b.exe", 0)))));
        Assert.Equal(new uint[] { 30, 20 }, Pids(ProcessChain.Walk(30, Table((30, 20, "a.exe", 0), (20, 0, "b.exe", 50)))));
    }

    [Fact]
    public void denied_process_itself_returns_null_so_the_caller_falls_back()
    {
        Func<uint, ProcLookup> deniedFirst = _ => ProcLookup.Denied;
        Assert.Null(ProcessChain.Walk(30, deniedFirst));
    }

    [Fact]
    public void denied_parent_ends_the_chain()
    {
        // An unreadable parent is a service, another user's or a protected process (often a reused pid):
        // never an app to group under.
        Func<uint, ProcLookup> deniedParent = pid => pid == 30 ? ProcLookup.Found(20, "a.exe", 0) : ProcLookup.Denied;
        Assert.Equal(new uint[] { 30 }, Pids(ProcessChain.Walk(30, deniedParent)));
    }
}
