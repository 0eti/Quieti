namespace EarTrumpet.Quieti;

/// <summary>마우스 휠·볼륨 단축키 한 번에 움직이는 볼륨 단계(설정 1~10%, 기본 2%).</summary>
public static class VolumeStep
{
    public const int Default = 2;
    public const int Min = 1;
    public const int Max = 10;

    public static int Clamp(int percent) => Math.Clamp(percent, Min, Max);

    /// <summary>슬라이더·트레이 휠이 쓰는 값. 로그 볼륨이면 EarTrumpet처럼 10분의 1.</summary>
    public static float Delta(int percent, bool logarithmic)
    {
        var step = Clamp(percent);
        return logarithmic ? step / 10f : step;
    }
}

/// <summary>"Windows 시작 시 실행" 항목(HKCU Run 값).</summary>
public static class StartupEntry
{
    public const string ValueName = "Quieti";

    public static string CommandFor(string exePath) => $"\"{exePath}\"";

    /// <summary>Run 값이 바로 이 exe를 가리키는가(다른 빌드·옛 경로면 꺼진 것으로 본다).</summary>
    public static bool PointsTo(string? value, string exePath)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        var path = value.Trim();
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"')
        {
            path = path[1..^1];
        }
        return string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase);
    }
}
