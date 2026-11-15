namespace EarTrumpet.Quieti;

/// <summary>음소거 엔진: 사건을 받아 명령(세션 음소거/해제)을 낸다. Win32 없이 시험한다.</summary>
public sealed class MuteEngine
{
    private sealed class KeyComparer : IComparer<SessionKey>
    {
        public int Compare(SessionKey x, SessionKey y) => string.CompareOrdinal(x.Value, y.Value);
    }

    private sealed class Session
    {
        public Session(SessionInfo info)
        {
            Info = info;
            App = info.App;
            Muted = info.Muted;
        }

        public SessionInfo Info { get; }
        public SessionKey Key => Info.Key;
        public string App { get; set; }
        public bool Muted { get; set; }
        /// <summary>Quieti가 음소거함.</summary>
        public bool Ours { get; set; }
        /// <summary>음소거가 실패함(다시 시도하지 않는다).</summary>
        public bool Failed { get; set; }
        /// <summary>완전 음소거: 사용자의 음소거(Quieti 규칙이 건드리지 않는다).</summary>
        public bool Full { get; set; }
        /// <summary>사용자가 직접 켬: 그때의 마지막 진짜 앱. 다른 앱이 앞에 올 때까지 건드리지 않는다.</summary>
        public string? OverrideAnchor { get; set; }
    }

    private readonly SortedDictionary<SessionKey, Session> _sessions = new(new KeyComparer());
    private readonly HashSet<string> _armed;
    /// <summary>세션이 없는(아직 안 나타난, 또는 꺼진) Quieti 음소거의 식별자.</summary>
    private readonly HashSet<string> _recovered;
    private Fg? _fg;

    public MuteEngine(IEnumerable<string> armed, IEnumerable<string> recovered)
    {
        _armed = new HashSet<string>(armed, StringComparer.Ordinal);
        _recovered = new HashSet<string>(recovered, StringComparer.Ordinal);
    }

    public IReadOnlySet<string> Armed => _armed;

    public bool IsArmed(string app) => _armed.Contains(app);

    /// <summary>저장할 되살리기 식별자: Quieti가 음소거한 세션 + 세션이 없는 Quieti 음소거.</summary>
    public IReadOnlySet<string> Ours()
        => _sessions.Values.Where(s => s.Ours).Select(s => s.Info.Identifier)
            .Concat(_recovered).ToHashSet(StringComparer.Ordinal);

    public AppState StateOf(string app)
    {
        var ss = _sessions.Values.Where(s => s.App == app).ToList();
        if (ss.Count == 0)
        {
            return AppState.NotRunning;
        }
        if (ss.Any(s => s.Failed))
        {
            return AppState.CannotMute;
        }
        if (ss.All(s => s.Muted))
        {
            return ss.Any(s => s.Full) ? AppState.FullMuted : AppState.MutedByQuieti;
        }
        return AppState.Playing;
    }

    public IReadOnlyList<SetMuteCommand> Handle(EngineEvent ev)
    {
        var cmds = new List<SetMuteCommand>();
        switch (ev)
        {
            case EngineEvent.SessionAdded added:
                OnAdded(added.Info, cmds);
                break;
            case EngineEvent.SessionGone gone:
                // 꺼진 앱도 Windows가 음소거를 기억한다: 다시 나타나면 이어 간다
                if (_sessions.Remove(gone.Key, out var removed) && removed.Ours)
                {
                    _recovered.Add(removed.Info.Identifier);
                }
                break;
            case EngineEvent.MuteChanged c:
                OnMuteChanged(c);
                break;
            case EngineEvent.MuteFailed f:
                if (_sessions.TryGetValue(f.Key, out var failed))
                {
                    failed.Failed = true;
                    failed.Ours = false;
                    failed.Muted = false;
                }
                break;
            case EngineEvent.Foreground fg:
                foreach (var s in _sessions.Values)
                {
                    if (s.OverrideAnchor != null && s.OverrideAnchor != fg.Fg.App)
                    {
                        s.OverrideAnchor = null;
                    }
                }
                _fg = fg.Fg;
                Decide(cmds);
                break;
            case EngineEvent.SetArmed a:
                if (a.On)
                {
                    _armed.Add(a.App);
                    Decide(cmds);
                }
                else
                {
                    Unarm(a.App, cmds);
                }
                break;
            case EngineEvent.SetFullMute m:
                if (m.On)
                {
                    FullMuteOn(m.App, cmds);
                }
                else
                {
                    FullMuteOff(m.App, cmds);
                }
                break;
            case EngineEvent.Shutdown:
                foreach (var s in _sessions.Values.Where(s => s.Ours))
                {
                    s.Ours = false;
                    s.Muted = false;
                    cmds.Add(new SetMuteCommand(s.Key, false, MuteContext.Quieti));
                }
                break;
        }
        return cmds;
    }

    private void OnAdded(SessionInfo info, List<SetMuteCommand> cmds)
    {
        var s = new Session(info);
        var id = info.Identifier;
        // 프로세스가 끝난 세션은 이름을 못 읽는다: 식별자 속 exe로
        if (s.App.Length == 0)
        {
            s.App = AppRules.AppOfIdentifier(id);
        }
        // 같은 식별자의 세션이 이미 Quieti 음소거면(다른 장치·같은 exe의 다른 프로세스, Windows가 기억한 음소거로
        // 음소거된 채 시작한 새 세션) 이것도 Quieti 것이다.
        var sibling = _sessions.Values.Any(o => o.Ours && o.Info.Identifier == id);
        var recovered = _recovered.Remove(id);
        if ((recovered || sibling) && s.Muted)
        {
            if (_armed.Contains(s.App))
            {
                s.Ours = true;
            }
            else
            {
                // 체크를 푼 앱에 남은 Quieti 음소거: 푼다
                s.Muted = false;
                cmds.Add(new SetMuteCommand(s.Key, false, MuteContext.Quieti));
            }
        }
        // 처음부터 사용자가 꺼 둔 세션은 완전 음소거로 친다
        s.Full = s.Muted && !s.Ours;
        _sessions[s.Key] = s;
        Decide(cmds);
    }

    private void OnMuteChanged(EngineEvent.MuteChanged c)
    {
        if (!_sessions.TryGetValue(c.Key, out var s))
        {
            return;
        }
        s.Muted = c.Muted;
        if (c.Ours)
        {
            // Quieti가 바꾼 것(복사된 음소거 포함)
            s.Ours = c.Muted;
            return;
        }
        s.Ours = false;
        s.Full = c.Muted; // 바깥(볼륨 믹서 등)에서 끄면 완전 음소거와 같다
        if (!c.Muted)
        {
            s.OverrideAnchor = _fg?.App ?? "";
        }
    }

    /// <summary>체크를 풂: 그 앱의 Quieti 음소거만 푼다. 세션이 없는 Quieti 음소거(recovered)는 그 앱이 나타날 때 푼다.</summary>
    private void Unarm(string app, List<SetMuteCommand> cmds)
    {
        _armed.Remove(app);
        foreach (var s in _sessions.Values.Where(s => s.App == app && s.Ours))
        {
            s.Ours = false;
            s.Muted = false;
            cmds.Add(new SetMuteCommand(s.Key, false, MuteContext.Quieti));
        }
    }

    private void FullMuteOn(string app, List<SetMuteCommand> cmds)
    {
        foreach (var s in _sessions.Values.Where(s => s.App == app))
        {
            if (s.Ours)
            {
                // 이미 꺼져 있다: 표시만 사용자 것으로
                s.Ours = false;
            }
            else if (!s.Muted)
            {
                s.Muted = true;
                cmds.Add(new SetMuteCommand(s.Key, true, MuteContext.User));
            }
            s.Full = true;
        }
    }

    private void FullMuteOff(string app, List<SetMuteCommand> cmds)
    {
        foreach (var s in _sessions.Values.Where(s => s.App == app && (s.Full || (s.Muted && !s.Ours))))
        {
            s.Muted = false;
            s.Full = false;
            s.OverrideAnchor = null;
            cmds.Add(new SetMuteCommand(s.Key, false, MuteContext.User));
        }
        Decide(cmds);
    }

    private bool IsActive(Session s)
        => _fg != null && ((_fg.App.Length > 0 && s.App == _fg.App) || s.Info.Ancestors.Contains(_fg.Pid));

    /// <summary>체크한 앱 세션마다 설계의 네 칸 표대로.</summary>
    private void Decide(List<SetMuteCommand> cmds)
    {
        foreach (var s in _sessions.Values)
        {
            if (!_armed.Contains(s.App) || s.Failed || s.OverrideAnchor != null)
            {
                continue;
            }
            var active = IsActive(s);
            if (!active && !s.Muted)
            {
                s.Muted = true;
                s.Ours = true;
                cmds.Add(new SetMuteCommand(s.Key, true, MuteContext.Quieti));
            }
            else if (active && s.Muted && s.Ours)
            {
                s.Muted = false;
                s.Ours = false;
                cmds.Add(new SetMuteCommand(s.Key, false, MuteContext.Quieti));
            }
        }
    }
}
