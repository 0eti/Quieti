namespace EarTrumpet.Quieti;

/// <summary>음소거 알림의 문맥: 바깥(사용자·다른 프로그램), Quieti 음소거, Quieti가 사용자 대신 낸 음소거(완전 음소거).</summary>
public enum NoticeKind { External, Quieti, UserMute }

/// <summary>
///     세션 음소거 알림 중 Quieti가 낸 명령의 메아리를 거른다.
///     엔진은 명령을 낼 때 이미 제 상태를 바꾸므로 메아리는 걸러야 하고, 늦게 온 옛 명령의 메아리는 버려야 한다.
/// </summary>
public sealed class EchoFilter
{
    /// <summary>세션마다 마지막으로 낸 명령이 만들 값(알림을 기다리는 중).</summary>
    private readonly Dictionary<SessionKey, bool> _expected = new();

    /// <summary>
    ///     세션에 SetMuteCommand를 실행한 뒤 부른다. 값이 바뀌는 명령(mute != currentMuted)만 기대를 남긴다:
    ///     바뀌지 않으면 알림이 오지 않는다. 새 명령은 옛 기대를 덮는다.
    /// </summary>
    public void Issued(SessionKey key, bool mute, bool currentMuted)
    {
        if (mute != currentMuted)
        {
            _expected[key] = mute;
        }
    }

    /// <summary>엔진에 넣을 사건, 버릴 알림이면 null.</summary>
    public EngineEvent.MuteChanged? Accept(SessionKey key, bool muted, NoticeKind kind)
    {
        if (kind == NoticeKind.External)
        {
            // 바깥에서 바꿈: 기대는 그대로 둔다
            return new EngineEvent.MuteChanged(key, muted, false);
        }
        if (_expected.TryGetValue(key, out var expected))
        {
            if (muted != expected)
            {
                // 더 옛 명령의 늦은 메아리
                return null;
            }
            _expected.Remove(key);
            return kind == NoticeKind.Quieti ? new EngineEvent.MuteChanged(key, muted, true) : null;
        }
        // 명령 없이 온 Quieti 알림은 EarTrumpet이 Quieti 음소거를 복사한 것이다
        return kind == NoticeKind.Quieti ? new EngineEvent.MuteChanged(key, muted, true) : null;
    }

    /// <summary>세션이 사라짐.</summary>
    public void Forget(SessionKey key) => _expected.Remove(key);
}
