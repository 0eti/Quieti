namespace EarTrumpet.Quieti;

/// <summary>One process as a chain walk sees it.</summary>
public readonly record struct ProcLookup(ProcLookup.Kind Status, uint Parent, string Exe, long CreatedAt)
{
    public enum Kind { Found, Gone, Denied }

    /// <param name="exe">AppRules.AppKey of the image name.</param>
    /// <param name="createdAt">Creation time (FILETIME ticks), 0 when unknown.</param>
    public static ProcLookup Found(uint parent, string exe, long createdAt) => new(Kind.Found, parent, exe, createdAt);

    /// <summary>No such process (it ended).</summary>
    public static readonly ProcLookup Gone = new(Kind.Gone, 0, "", 0);

    /// <summary>It exists but could not be read; the caller should use another source.</summary>
    public static readonly ProcLookup Denied = new(Kind.Denied, 0, "", 0);
}

/// <summary>Process parent chains (port of the Rust process.rs::chain).</summary>
public static class ProcessChain
{
    /// <summary>Longest chain followed (guards against cycles and odd chains).</summary>
    public const int MaxChain = 32;

    /// <summary>
    ///     The process itself followed by its parents, stopping after explorer.exe.
    ///     A pid that is gone yields only itself with an empty Exe. An unreadable parent ends the chain.
    ///     Null when the process itself could not be read.
    /// </summary>
    public static IReadOnlyList<Proc>? Walk(uint pid, Func<uint, ProcLookup> lookup)
    {
        var chain = new List<Proc>();
        var cur = pid;
        long childCreatedAt = 0;
        while (chain.Count < MaxChain)
        {
            var entry = lookup(cur);
            if (entry.Status == ProcLookup.Kind.Denied)
            {
                // The process itself must be read some other way; an unreadable parent (a service, another
                // user's or a protected process, often a reused pid) is never an app to group under.
                if (chain.Count == 0)
                {
                    return null;
                }
                break;
            }
            if (entry.Status == ProcLookup.Kind.Gone)
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
            // A parent started after its child is a reused pid: the real parent has ended.
            if (chain.Count > 0 && childCreatedAt != 0 && entry.CreatedAt != 0 && entry.CreatedAt > childCreatedAt)
            {
                break;
            }
            chain.Add(new Proc(cur, entry.Exe));
            if (entry.Exe == AppRules.Shell || entry.Parent == 0 || entry.Parent == cur)
            {
                break;
            }
            childCreatedAt = entry.CreatedAt;
            cur = entry.Parent;
        }
        return chain;
    }
}
