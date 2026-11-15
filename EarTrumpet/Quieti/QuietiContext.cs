using System;

namespace EarTrumpet.Quieti;

/// <summary>
///     EventContext GUIDs that Quieti passes to ISimpleAudioVolume.SetMute so that
///     the resulting notification can be attributed to Quieti.
/// </summary>
public static class QuietiContext
{
    /// <summary>Marks a mute applied by Quieti because the app is in the background.</summary>
    public static readonly Guid Mute = new("3f1c9a52-7d4e-4b8a-9c21-5e0d8b7a6f13");

    /// <summary>
    ///     Marks mutes Quieti applies on the user's behalf (full mute).
    ///     They are user mutes, not Quieti mutes.
    /// </summary>
    public static readonly Guid UserMute = new("2b5f8e2a-222f-4086-b60a-a918220264ed");
}
