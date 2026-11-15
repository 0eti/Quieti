using EarTrumpet.Quieti;

namespace EarTrumpet.Quieti.Tests;

public class EchoFilterTests
{
    private static readonly SessionKey K = new("s");

    [Fact]
    public void external_notice_passes_as_not_ours()
    {
        var f = new EchoFilter();
        f.Issued(K, true, false);
        Assert.Equal(new EngineEvent.MuteChanged(K, false, false), f.Accept(K, false, NoticeKind.External));
        // 바깥 알림은 기대를 남겨 둔다
        Assert.Equal(new EngineEvent.MuteChanged(K, true, true), f.Accept(K, true, NoticeKind.Quieti));
    }

    [Fact]
    public void own_quieti_echo_passes_as_ours_once()
    {
        var f = new EchoFilter();
        f.Issued(K, true, false);
        Assert.Equal(new EngineEvent.MuteChanged(K, true, true), f.Accept(K, true, NoticeKind.Quieti));
        // 소비했다: 다른 값의 Quieti 알림도 이제 옛 메아리로 버려지지 않고 복사로 지나간다
        Assert.Equal(new EngineEvent.MuteChanged(K, false, true), f.Accept(K, false, NoticeKind.Quieti));
    }

    [Fact]
    public void user_mute_echo_is_dropped()
    {
        var f = new EchoFilter();
        f.Issued(K, true, false);
        Assert.Null(f.Accept(K, true, NoticeKind.UserMute));
        // 소비했다: 이제 Quieti 알림은 복사로 지나간다
        Assert.Equal(new EngineEvent.MuteChanged(K, false, true), f.Accept(K, false, NoticeKind.Quieti));
    }

    [Fact]
    public void stale_quieti_unmute_after_full_mute_is_dropped()
    {
        var f = new EchoFilter();
        f.Issued(K, false, true);
        f.Issued(K, true, false);
        Assert.Null(f.Accept(K, false, NoticeKind.Quieti));
        Assert.Null(f.Accept(K, true, NoticeKind.UserMute));
    }

    [Fact]
    public void quieti_copy_without_command_passes_as_ours()
    {
        var f = new EchoFilter();
        Assert.Equal(new EngineEvent.MuteChanged(K, true, true), f.Accept(K, true, NoticeKind.Quieti));
        Assert.Null(f.Accept(K, false, NoticeKind.UserMute));
    }

    [Fact]
    public void command_that_changes_nothing_sets_no_expectation()
    {
        var f = new EchoFilter();
        f.Issued(K, true, true);
        // 기대가 없으니 Quieti 알림은 값이 무엇이든 복사로 지나간다
        Assert.Equal(new EngineEvent.MuteChanged(K, false, true), f.Accept(K, false, NoticeKind.Quieti));
    }

    [Fact]
    public void forget_clears_expectation()
    {
        var f = new EchoFilter();
        f.Issued(K, true, false);
        f.Forget(K);
        Assert.Equal(new EngineEvent.MuteChanged(K, false, true), f.Accept(K, false, NoticeKind.Quieti));
    }
}
