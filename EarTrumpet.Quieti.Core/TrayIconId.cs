namespace EarTrumpet.Quieti;

/// <summary>
///     Tray icon GUID per exe path. Windows binds a tray icon GUID to the path of the exe that first
///     registered it, so the Debug and Release builds (same settings, different folders) each need their own.
/// </summary>
public static class TrayIconId
{
    /// <summary>Name-based GUID (RFC 4122 version 5 layout) from the base GUID and the lower-cased exe path.</summary>
    public static Guid For(Guid baseId, string exePath)
    {
        var name = System.Text.Encoding.UTF8.GetBytes(exePath.ToLowerInvariant());
        var data = new byte[16 + name.Length];
        baseId.ToByteArray().CopyTo(data, 0);
        name.CopyTo(data, 16);
        var hash = System.Security.Cryptography.SHA1.HashData(data);
        var bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50); // version 5 (Guid byte order: time_hi is little-endian)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes);
    }
}
