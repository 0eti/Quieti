namespace EarTrumpet.Quieti;

/// <summary>프로세스 하나: pid와 앱 이름(AppRules.AppKey 결과, 못 읽으면 "").</summary>
public readonly record struct Proc(uint Pid, string Exe);

/// <summary>앞 창: 그 창 프로세스의 앱 이름, 창 클래스, 이 프로그램의 창인가.</summary>
public sealed record FgWindow(string Exe, string Class, bool Own);

/// <summary>오디오 세션 하나의 키(EarTrumpet AudioDeviceSession.Id, 장치마다 다름).</summary>
public readonly record struct SessionKey(string Value);

/// <summary>세션 정보. App = AppRules.KeyForApp 결과(""면 못 읽음), Identifier는 앱을 다시 켜도 같다.</summary>
public sealed record SessionInfo(SessionKey Key, uint Pid, string App, string Display,
    IReadOnlyList<uint> Ancestors, string Identifier, bool Muted);

/// <summary>마지막 진짜 앱(중립 창은 오지 않는다).</summary>
public sealed record Fg(uint Pid, string App);

public abstract record EngineEvent
{
    public sealed record SessionAdded(SessionInfo Info) : EngineEvent;
    public sealed record SessionGone(SessionKey Key) : EngineEvent;
    /// <summary>음소거가 바뀜. Ours면 Quieti가 바꾼 것.</summary>
    public sealed record MuteChanged(SessionKey Key, bool Muted, bool Ours) : EngineEvent;
    public sealed record MuteFailed(SessionKey Key) : EngineEvent;
    public sealed record Foreground(Fg Fg) : EngineEvent;
    /// <summary>"뒤에 있으면 음소거" 켜고 끄기.</summary>
    public sealed record SetArmed(string App, bool On) : EngineEvent;
    /// <summary>완전 음소거(오른쪽 클릭·M).</summary>
    public sealed record SetFullMute(string App, bool On) : EngineEvent;
    public sealed record Shutdown : EngineEvent;
}

public enum MuteContext { Quieti, User }

public sealed record SetMuteCommand(SessionKey Key, bool Mute, MuteContext Context);

public enum AppState { Playing, MutedByQuieti, FullMuted, CannotMute, NotRunning }
