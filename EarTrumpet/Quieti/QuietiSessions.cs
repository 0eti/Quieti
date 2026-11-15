using EarTrumpet.DataModel.Audio;
using EarTrumpet.DataModel.WindowsAudio.Internal;
using System.Collections.Generic;
using System.Linq;

namespace EarTrumpet.Quieti;

/// <summary>
///     Helpers for carrying a Quieti mute over when the app's mute state is copied
///     between sessions (the copy sites only see IAudioDeviceSession groups).
/// </summary>
internal static class QuietiSessions
{
    /// <summary>Flattens a session or session group down to the real AudioDeviceSession instances.</summary>
    public static IEnumerable<AudioDeviceSession> RealSessions(IAudioDeviceSession session)
    {
        switch (session)
        {
            case AudioDeviceSession real:
                yield return real;
                break;
            case AudioDeviceSessionGroup group:
                foreach (var child in group.Sessions)
                {
                    foreach (var real in RealSessions(child))
                    {
                        yield return real;
                    }
                }
                break;
        }
    }

    /// <summary>
    ///     True when the source has at least one live real session and the engine counts all of them as
    ///     Quieti-muted (QuietiService.IsOurs). Expired sessions are skipped: the engine no longer tracks them.
    /// </summary>
    public static bool AllMutedByQuieti(IAudioDeviceSession source)
    {
        var any = false;
        foreach (var real in RealSessions(source))
        {
            if (real.State == SessionState.Expired)
            {
                continue;
            }
            if (!QuietiService.Current.IsOurs(real.Id))
            {
                return false;
            }
            any = true;
        }
        return any;
    }

    /// <summary>Mutes every real session under target in the Quieti context (skips ones already muted).</summary>
    public static void MuteByQuieti(IAudioDeviceSession target)
    {
        foreach (var real in RealSessions(target).ToArray())
        {
            if (!real.IsMuted)
            {
                real.SetMute(true, QuietiContext.Mute);
            }
        }
    }
}
