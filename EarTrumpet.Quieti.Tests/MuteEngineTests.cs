using EarTrumpet.Quieti;

namespace EarTrumpet.Quieti.Tests;

public class MuteEngineTests
{
    private const string Game = "game.exe";
    private const string Video = "video.exe";
    private const string Music = "music.exe";
    private const string Browser = "browser.exe";

    private static SessionKey K(string k) => new(k);

    private static string IdOf(string app)
        => $@"{{0.0.0.00000000}}.{{dev}}|\Device\X\{app}%b{{00000000-0000-0000-0000-000000000000}}";

    private static SessionInfo Sess(string k, uint pid, string app, bool muted)
        => new(K(k), pid, app, app, Array.Empty<uint>(), IdOf(app), muted);

    private static EngineEvent Added(string k, uint pid, string app, bool muted)
        => new EngineEvent.SessionAdded(Sess(k, pid, app, muted));

    private static EngineEvent Fore(uint pid, string app) => new EngineEvent.Foreground(new Fg(pid, app));

    private static SetMuteCommand Mute(string k, bool m, MuteContext c = MuteContext.Quieti) => new(K(k), m, c);

    private static EngineEvent User(string k, bool muted) => new EngineEvent.MuteChanged(K(k), muted, false);

    private static EngineEvent Arm(string app, bool on) => new EngineEvent.SetArmed(app, on);

    private static EngineEvent Full(string app, bool on) => new EngineEvent.SetFullMute(app, on);

    private static MuteEngine Engine(params string[] armed) => new(armed, Array.Empty<string>());

    private static MuteEngine EngineRec(string[] armed, string[] recovered) => new(armed, recovered);

    private static HashSet<string> Set(params string[] items) => new(items);

    private static void Cmds(IReadOnlyList<SetMuteCommand> actual, params SetMuteCommand[] expected)
        => Assert.Equal(expected, actual);

    [Fact]
    public void background_armed_app_is_muted_and_marked()
    {
        var e = Engine(Game, Video);
        e.Handle(Fore(1, Game));
        Cmds(e.Handle(Added("g", 1, Game, false)));
        Cmds(e.Handle(Added("v", 2, Video, false)), Mute("v", true));
        Assert.Equal(AppState.MutedByQuieti, e.StateOf(Video));
        Assert.Equal(AppState.Playing, e.StateOf(Game));
    }

    [Fact]
    public void foreground_app_is_unmuted_when_ours()
    {
        var e = Engine(Game, Video);
        e.Handle(Added("v", 2, Video, false));
        e.Handle(Fore(1, Game));
        Cmds(e.Handle(Fore(2, Video)), Mute("v", false));
        Assert.Equal(AppState.Playing, e.StateOf(Video));
    }

    [Fact]
    public void user_muted_session_is_never_unmuted()
    {
        var e = Engine(Video);
        e.Handle(Added("v", 2, Video, false));
        e.Handle(Fore(2, Video));
        e.Handle(User("v", true));
        Cmds(e.Handle(Fore(9, Game)));
        Cmds(e.Handle(Fore(2, Video)));
        Assert.Equal(AppState.FullMuted, e.StateOf(Video));
    }

    [Fact]
    public void unarmed_app_is_never_touched()
    {
        var e = Engine(Game);
        e.Handle(Added("m", 3, Music, false));
        Cmds(e.Handle(Fore(1, Game)));
        Cmds(e.Handle(Fore(9, Browser)));
    }

    [Fact]
    public void user_unmute_survives_neutral_trip_and_same_app()
    {
        var e = Engine(Video);
        e.Handle(Fore(2, Video));
        e.Handle(Added("v", 2, Video, false));
        Cmds(e.Handle(Fore(1, Game)), Mute("v", true));
        // 볼륨 믹서(중립 창)에서 직접 켬: 중립 창은 엔진에 오지 않는다
        Cmds(e.Handle(User("v", false)));
        // 같은 게임으로 돌아옴
        Cmds(e.Handle(Fore(1, Game)));
        Assert.Equal(AppState.Playing, e.StateOf(Video));
        // 다른 앱으로 바꿈: 다시 규칙대로
        Cmds(e.Handle(Fore(5, Browser)), Mute("v", true));
    }

    [Fact]
    public void user_unmute_override_survives_new_session_event()
    {
        var e = Engine(Video, Music);
        e.Handle(Added("v", 2, Video, false));
        e.Handle(Fore(1, Game));
        e.Handle(User("v", false));
        Cmds(e.Handle(Added("m", 3, Music, false)), Mute("m", true));
        Cmds(e.Handle(Arm(Game, true)));
    }

    [Fact]
    public void user_unmute_of_user_muted_session_is_not_reversed()
    {
        var e = Engine(Video);
        e.Handle(Added("v", 2, Video, true)); // 처음부터 사용자가 꺼 둠
        e.Handle(Fore(1, Game));
        Cmds(e.Handle(User("v", false)));
        Cmds(e.Handle(Added("x", 7, Music, false)));
        Cmds(e.Handle(Fore(5, Browser)), Mute("v", true));
    }

    [Fact]
    public void new_session_of_background_app_is_muted_immediately()
    {
        var e = Engine(Video);
        e.Handle(Fore(1, Game));
        Cmds(e.Handle(Added("v", 2, Video, false)), Mute("v", true));
    }

    [Fact]
    public void child_session_is_active_when_parent_is_foreground()
    {
        var e = Engine(Video);
        var s = Sess("v", 30, Video, false) with { Ancestors = new uint[] { 20 } };
        e.Handle(Fore(20, "chrome.exe"));
        Cmds(e.Handle(new EngineEvent.SessionAdded(s)));
        Cmds(e.Handle(Fore(1, Game)), Mute("v", true));
    }

    [Fact]
    public void same_name_app_is_active_even_without_ancestry()
    {
        var e = Engine(Video);
        e.Handle(Fore(77, Video));
        Cmds(e.Handle(Added("v", 30, Video, false)));
    }

    [Fact]
    public void unknown_foreground_app_mutes_armed_apps()
    {
        var e = Engine(Video);
        e.Handle(Fore(2, Video));
        e.Handle(Added("v", 2, Video, false));
        e.Handle(Added("u", 8, "", false));
        Cmds(e.Handle(Fore(99, "")), Mute("v", true));
    }

    [Fact]
    public void no_foreground_yet_means_all_inactive()
    {
        var e = Engine(Video);
        Cmds(e.Handle(Added("v", 2, Video, false)), Mute("v", true));
    }

    [Fact]
    public void all_sessions_of_app_are_muted()
    {
        var e = Engine(Video);
        e.Handle(Fore(1, Game));
        e.Handle(Added("v1", 2, Video, false));
        Cmds(e.Handle(Added("v2", 3, Video, false)), Mute("v2", true));
        Cmds(e.Handle(Fore(3, Video)), Mute("v1", false), Mute("v2", false));
    }

    [Fact]
    public void unarming_unmutes_only_ours()
    {
        var e = Engine(Video, Music);
        e.Handle(Added("v", 2, Video, false));
        e.Handle(Added("m", 3, Music, true));
        e.Handle(Fore(1, Game));
        Cmds(e.Handle(Arm(Video, false)), Mute("v", false));
        Cmds(e.Handle(Arm(Music, false)));
        Assert.Empty(e.Armed);
    }

    [Fact]
    public void arming_mutes_background_app()
    {
        var e = Engine();
        e.Handle(Added("v", 2, Video, false));
        e.Handle(Fore(1, Game));
        Cmds(e.Handle(Arm(Video, true)), Mute("v", true));
        Assert.True(e.IsArmed(Video));
    }

    [Fact]
    public void shutdown_unmutes_only_ours_and_clears_ours()
    {
        var e = Engine(Video, Music);
        e.Handle(Added("v", 2, Video, false));
        e.Handle(Added("m", 3, Music, true));
        e.Handle(Fore(1, Game));
        Assert.Equal(Set(IdOf(Video)), e.Ours());
        Cmds(e.Handle(new EngineEvent.Shutdown()), Mute("v", false));
        Assert.Empty(e.Ours());
    }

    [Fact]
    public void shutdown_keeps_identifiers_of_apps_not_running()
    {
        var e = EngineRec(new[] { Video }, new[] { IdOf(Video) });
        Cmds(e.Handle(new EngineEvent.Shutdown()));
        Assert.Equal(Set(IdOf(Video)), e.Ours());
    }

    [Fact]
    public void recovered_identifier_muted_in_background_stays_ours()
    {
        var e = EngineRec(new[] { Video }, new[] { IdOf(Video) });
        e.Handle(Fore(1, Game));
        Cmds(e.Handle(Added("v", 2, Video, true)));
        Assert.Equal(AppState.MutedByQuieti, e.StateOf(Video));
        Assert.Contains(IdOf(Video), e.Ours());
        Cmds(e.Handle(Fore(2, Video)), Mute("v", false));
    }

    [Fact]
    public void recovered_identifier_in_foreground_is_unmuted()
    {
        var e = EngineRec(new[] { Video }, new[] { IdOf(Video) });
        e.Handle(Fore(2, Video));
        Cmds(e.Handle(Added("v", 2, Video, true)), Mute("v", false));
        Assert.Empty(e.Ours());
    }

    [Fact]
    public void recovered_identifier_of_unarmed_app_is_unmuted_and_dropped()
    {
        var e = EngineRec(Array.Empty<string>(), new[] { IdOf(Video) });
        e.Handle(Fore(1, Game));
        Cmds(e.Handle(Added("v", 2, Video, true)), Mute("v", false));
        Assert.Empty(e.Ours());
    }

    [Fact]
    public void recovered_identifier_found_unmuted_is_dropped()
    {
        var e = EngineRec(Array.Empty<string>(), new[] { IdOf(Video) });
        Cmds(e.Handle(Added("v", 2, Video, false)));
        Assert.Empty(e.Ours());
    }

    [Fact]
    public void unarming_keeps_recovered_identifier_until_app_appears()
    {
        var e = EngineRec(new[] { Video }, new[] { IdOf(Video) });
        e.Handle(Arm(Video, false));
        Assert.Contains(IdOf(Video), e.Ours());
        e.Handle(Fore(1, Game));
        Cmds(e.Handle(Added("v", 2, Video, true)), Mute("v", false));
        Assert.Empty(e.Ours());
    }

    [Fact]
    public void same_identifier_on_new_device_stays_ours()
    {
        var e = Engine(Video);
        e.Handle(Fore(1, Game));
        e.Handle(Added("dev1|v", 2, Video, false));
        Cmds(e.Handle(new EngineEvent.SessionGone(K("dev1|v"))));
        Assert.Contains(IdOf(Video), e.Ours());
        Cmds(e.Handle(Added("dev2|v", 2, Video, true)));
        Assert.Equal(AppState.MutedByQuieti, e.StateOf(Video));
        Cmds(e.Handle(Fore(2, Video)), Mute("dev2|v", false));
    }

    [Fact]
    public void two_muted_sessions_with_recovered_identifier_are_both_ours()
    {
        var e = EngineRec(new[] { Video }, new[] { IdOf(Video) });
        e.Handle(Fore(1, Game));
        e.Handle(Added("v1", 2, Video, true));
        e.Handle(Added("v2", 3, Video, true));
        Assert.Equal(AppState.MutedByQuieti, e.StateOf(Video));
        Cmds(e.Handle(Fore(2, Video)), Mute("v1", false), Mute("v2", false));
    }

    [Fact]
    public void live_sibling_appearing_muted_is_ours()
    {
        // Windows가 앱별로 기억한 음소거로 새 세션이 음소거된 채 시작한다
        var e = Engine(Video);
        e.Handle(Fore(1, Game));
        e.Handle(Added("v1", 2, Video, false));
        Cmds(e.Handle(Added("v2", 3, Video, true)));
        Cmds(e.Handle(new EngineEvent.Shutdown()), Mute("v1", false), Mute("v2", false));
    }

    [Fact]
    public void stale_session_without_app_name_uses_identifier_app()
    {
        var e = EngineRec(new[] { Video }, new[] { IdOf(Video) });
        e.Handle(Fore(1, Game));
        // 프로세스가 끝난 세션: 이름을 못 읽지만 식별자에는 exe 경로가 있다
        var stale = Sess("old", 2, "", true) with { Identifier = IdOf(Video) };
        Cmds(e.Handle(new EngineEvent.SessionAdded(stale)));
        Cmds(e.Handle(Added("v", 3, Video, true)));
        Assert.Equal(AppState.MutedByQuieti, e.StateOf(Video));
        Cmds(e.Handle(Fore(3, Video)), Mute("old", false), Mute("v", false));
    }

    [Fact]
    public void mute_failed_shows_cannot_mute_and_is_not_retried()
    {
        var e = Engine(Video);
        e.Handle(Fore(1, Game));
        e.Handle(Added("v", 2, Video, false));
        e.Handle(new EngineEvent.MuteFailed(K("v")));
        Assert.Equal(AppState.CannotMute, e.StateOf(Video));
        Cmds(e.Handle(Fore(5, Browser)));
        Assert.Empty(e.Ours());
    }

    [Fact]
    public void our_own_change_report_keeps_state()
    {
        var e = Engine(Video);
        e.Handle(Fore(1, Game));
        e.Handle(Added("v", 2, Video, false));
        Cmds(e.Handle(new EngineEvent.MuteChanged(K("v"), true, true)));
        Assert.Equal(AppState.MutedByQuieti, e.StateOf(Video));
    }

    [Fact]
    public void state_of_app_without_sessions_is_not_running()
    {
        Assert.Equal(AppState.NotRunning, Engine(Video).StateOf(Video));
    }

    // ---- 완전 음소거 ----

    [Fact]
    public void full_mute_on_quieti_muted_app_keeps_it_muted()
    {
        var e = Engine(Video);
        e.Handle(Fore(1, Game));
        Cmds(e.Handle(Added("v", 2, Video, false)), Mute("v", true));
        Cmds(e.Handle(Full(Video, true)));
        Assert.Equal(AppState.FullMuted, e.StateOf(Video));
        Cmds(e.Handle(Fore(2, Video)));
    }

    [Fact]
    public void full_mute_off_returns_to_rules()
    {
        var e = Engine(Video);
        e.Handle(Fore(1, Game));
        e.Handle(Added("v", 2, Video, false));
        e.Handle(Full(Video, true));
        e.Handle(Fore(2, Video));
        e.Handle(Fore(1, Game));
        Cmds(e.Handle(Full(Video, false)), Mute("v", false, MuteContext.User), Mute("v", true));
        Assert.Equal(AppState.MutedByQuieti, e.StateOf(Video));
    }

    [Fact]
    public void full_mute_off_in_foreground_plays()
    {
        var e = Engine(Video);
        e.Handle(Fore(2, Video));
        e.Handle(Added("v", 2, Video, false));
        Cmds(e.Handle(Full(Video, true)), Mute("v", true, MuteContext.User));
        Assert.Equal(AppState.FullMuted, e.StateOf(Video));
        Cmds(e.Handle(Full(Video, false)), Mute("v", false, MuteContext.User));
        Assert.Equal(AppState.Playing, e.StateOf(Video));
    }

    [Fact]
    public void full_mute_on_unarmed_app_is_plain_mute()
    {
        var e = Engine();
        e.Handle(Fore(1, Game));
        e.Handle(Added("v", 2, Video, false));
        Cmds(e.Handle(Full(Video, true)), Mute("v", true, MuteContext.User));
        Assert.Equal(AppState.FullMuted, e.StateOf(Video));
        Cmds(e.Handle(Full(Video, false)), Mute("v", false, MuteContext.User));
        Assert.Equal(AppState.Playing, e.StateOf(Video));
    }

    [Fact]
    public void unarming_keeps_full_mute()
    {
        var e = Engine(Video);
        e.Handle(Fore(1, Game));
        e.Handle(Added("v", 2, Video, false));
        e.Handle(Full(Video, true));
        Cmds(e.Handle(Arm(Video, false)));
        Assert.Equal(AppState.FullMuted, e.StateOf(Video));
    }

    [Fact]
    public void external_mute_counts_as_full_mute()
    {
        var e = Engine(Video);
        e.Handle(Fore(2, Video));
        e.Handle(Added("v", 2, Video, false));
        Cmds(e.Handle(User("v", true)));
        Assert.Equal(AppState.FullMuted, e.StateOf(Video));
        Cmds(e.Handle(Fore(1, Game)));
        Cmds(e.Handle(Fore(2, Video)));
    }

    [Fact]
    public void copied_quieti_mute_stays_ours()
    {
        var e = Engine(Video);
        e.Handle(Fore(1, Game));
        e.Handle(Added("v1", 2, Video, false));
        Cmds(e.Handle(Added("v2", 3, Video, false)), Mute("v2", true));
        // v2는 사용자가 켜서 엔진은 지시하지 않았는데, 그 뒤 Quieti 음소거 통지(복사된 음소거)가 온다
        Cmds(e.Handle(User("v2", false)));
        Cmds(e.Handle(new EngineEvent.MuteChanged(K("v2"), true, true)));
        Assert.Equal(AppState.MutedByQuieti, e.StateOf(Video));
        Cmds(e.Handle(Fore(2, Video)), Mute("v1", false), Mute("v2", false));
    }

    [Fact]
    public void late_quieti_echo_does_not_override_full_mute()
    {
        var e = Engine(Video);
        e.Handle(Fore(1, Game));
        Cmds(e.Handle(Added("v", 2, Video, false)), Mute("v", true));
        Cmds(e.Handle(Full(Video, true)));
        Cmds(e.Handle(new EngineEvent.MuteChanged(K("v"), true, true)));
        Assert.Equal(AppState.FullMuted, e.StateOf(Video));
        Cmds(e.Handle(Fore(2, Video)));
        Assert.Equal(AppState.FullMuted, e.StateOf(Video));
    }
    // ---- 누구의 음소거인가(IsOurs): 엔진이 하나뿐인 진실 ----

    [Fact]
    public void is_ours_after_quieti_mute()
    {
        var e = Engine(Video);
        e.Handle(Fore(1, Game));
        Cmds(e.Handle(Added("v", 2, Video, false)), Mute("v", true));
        Assert.True(e.IsOurs(K("v")));
    }

    [Fact]
    public void is_ours_for_recovered_session()
    {
        var e = EngineRec(new[] { Video }, new[] { IdOf(Video) });
        e.Handle(Fore(1, Game));
        e.Handle(Added("v", 2, Video, true));
        Assert.True(e.IsOurs(K("v")));
    }

    [Fact]
    public void is_ours_for_sibling_session()
    {
        var e = Engine(Video);
        e.Handle(Fore(1, Game));
        e.Handle(Added("v1", 2, Video, false));
        e.Handle(Added("v2", 3, Video, true));
        Assert.True(e.IsOurs(K("v2")));
    }

    [Fact]
    public void is_not_ours_after_full_mute_takes_over()
    {
        var e = Engine(Video);
        e.Handle(Fore(1, Game));
        e.Handle(Added("v", 2, Video, false));
        e.Handle(Full(Video, true));
        Assert.False(e.IsOurs(K("v")));
    }

    [Fact]
    public void is_not_ours_after_unarm()
    {
        var e = Engine(Video);
        e.Handle(Fore(1, Game));
        e.Handle(Added("v", 2, Video, false));
        e.Handle(Arm(Video, false));
        Assert.False(e.IsOurs(K("v")));
    }

    [Fact]
    public void is_not_ours_for_user_mute_or_unknown_session()
    {
        var e = Engine(Video);
        e.Handle(Fore(2, Video));
        e.Handle(Added("v", 2, Video, false));
        e.Handle(User("v", true));
        Assert.False(e.IsOurs(K("v")));
        Assert.False(e.IsOurs(K("nope")));
    }

    // ---- 다 풀기(Shutdown) 뒤에도 규칙이 이어진다(로그오프 취소) ----

    [Fact]
    public void rules_resume_after_release_all()
    {
        var e = Engine(Video);
        e.Handle(Fore(1, Game));
        Cmds(e.Handle(Added("v", 2, Video, false)), Mute("v", true));
        Cmds(e.Handle(new EngineEvent.Shutdown()), Mute("v", false));
        Assert.False(e.IsOurs(K("v")));
        Cmds(e.Handle(Fore(5, Browser)), Mute("v", true));
        Assert.True(e.IsOurs(K("v")));
        Assert.Equal(Set(IdOf(Video)), e.Ours());
        Cmds(e.Handle(Fore(2, Video)), Mute("v", false));
    }

    // ---- 완전 음소거 켜짐/꺼짐 판단(IsFullMuted) ----

    [Fact]
    public void full_mute_is_on_only_for_user_mute()
    {
        var e = Engine(Video);
        Assert.False(e.IsFullMuted(Video));
        e.Handle(Fore(1, Game));
        e.Handle(Added("v", 2, Video, false));
        Assert.False(e.IsFullMuted(Video)); // Quieti 음소거: 켜면 완전 음소거로 바뀐다
        e.Handle(Full(Video, true));
        Assert.True(e.IsFullMuted(Video));
        e.Handle(Full(Video, false));
        Assert.False(e.IsFullMuted(Video));
    }

    [Fact]
    public void full_mute_can_be_turned_off_despite_failed_session()
    {
        var e = Engine();
        e.Handle(Fore(1, Game));
        e.Handle(Added("a", 2, Video, false));
        e.Handle(Added("b", 3, Video, false));
        e.Handle(new EngineEvent.MuteFailed(K("b")));
        e.Handle(Full(Video, true));
        e.Handle(new EngineEvent.MuteFailed(K("b")));
        Assert.Equal(AppState.CannotMute, e.StateOf(Video));
        Assert.True(e.IsFullMuted(Video));
        Cmds(e.Handle(Full(Video, false)), Mute("a", false, MuteContext.User), Mute("b", false, MuteContext.User));
    }
}
