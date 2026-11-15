namespace EarTrumpet.Quieti;

/// <summary>앱 이름 규칙: exe 이름, 조상, 중립 창, 세션 식별자 → 앱.</summary>
public static class AppRules
{
    /// <summary>조상 찾기를 멈추는 셸.</summary>
    public const string Shell = "explorer.exe";

    /// <summary>앞에 와도 아무것도 바꾸지 않는 창의 exe(설계 "중립 창").</summary>
    private static readonly HashSet<string> NeutralExes = new(StringComparer.Ordinal)
    {
        "startmenuexperiencehost.exe",
        "searchhost.exe",
        "searchapp.exe",
        "shellexperiencehost.exe",
        "shellhost.exe",
        "textinputhost.exe",
        "lockapp.exe",
        "consent.exe",
        "sndvol.exe",
        "systemsettings.exe",
    };

    /// <summary>explorer.exe 창 중 진짜 앱인 파일 탐색기.</summary>
    private const string ExplorerAppClass = "CabinetWClass";

    /// <summary>경로나 이름에서 앱 이름(마지막 조각, 소문자).</summary>
    public static string AppKey(string exePathOrName)
    {
        var i = exePathOrName.LastIndexOfAny(new[] { '\\', '/' });
        return exePathOrName[(i + 1)..].ToLowerInvariant();
    }

    /// <summary>데스크톱 앱은 exe 이름, 스토어 앱은 appId가 키.</summary>
    public static string KeyForApp(bool isDesktopApp, string exeName, string appId)
        => isDesktopApp ? AppKey(exeName) : appId;

    /// <summary>부모들의 pid(셸 앞까지, 자기는 뺀다).</summary>
    public static IReadOnlyList<uint> Ancestors(IReadOnlyList<Proc> chain)
        => chain.Skip(1).TakeWhile(p => p.Exe != Shell).Select(p => p.Pid).ToList();

    /// <summary>앞에 와도 소리를 바꾸지 않는 창인가(설계 "중립 창").</summary>
    public static bool IsNeutral(FgWindow w)
    {
        if (w.Own)
        {
            return true;
        }
        if (w.Exe == Shell)
        {
            return w.Class != ExplorerAppClass;
        }
        return NeutralExes.Contains(w.Exe);
    }

    /// <summary>세션 식별자(<c>...|&lt;exe 경로&gt;%b{...}</c>)에서 앱 이름. 모양이 다르면 "".</summary>
    public static string AppOfIdentifier(string sessionIdentifier)
    {
        var bar = sessionIdentifier.IndexOf('|');
        if (bar < 0)
        {
            return "";
        }
        var rest = sessionIdentifier[(bar + 1)..];
        var b = rest.IndexOf("%b", StringComparison.Ordinal);
        if (b < 0)
        {
            return "";
        }
        return AppKey(rest[..b]);
    }

    /// <summary>true면 볼륨 설정이 음소거를 바꾸지 않는다(Quieti가 끈 세션은 볼륨 값과 무관하게 유지).</summary>
    public static bool ShouldKeepQuietiMuteOnVolumeChange(bool mutedByQuieti, bool volumeIsZero)
        => mutedByQuieti;
}
