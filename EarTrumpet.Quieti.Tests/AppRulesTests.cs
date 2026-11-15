using EarTrumpet.Quieti;

namespace EarTrumpet.Quieti.Tests;

public class AppRulesTests
{
    private static Proc P(uint pid, string exe) => new(pid, exe);
    private static FgWindow W(string exe, string cls, bool own) => new(exe, cls, own);

    [Fact]
    public void app_key_takes_file_name_lowercased()
    {
        Assert.Equal("chrome.exe", AppRules.AppKey(@"C:\X\Chrome.EXE"));
        Assert.Equal("explorer.exe", AppRules.AppKey(@"\Device\HarddiskVolume3\Windows\explorer.exe"));
        Assert.Equal("game.exe", AppRules.AppKey("Game.exe"));
        Assert.Equal("", AppRules.AppKey(""));
    }

    [Fact]
    public void app_key_ignores_install_folder()
    {
        Assert.Equal(
            AppRules.AppKey(@"C:\Users\a\AppData\Local\Discord\app-1.0.9268\Discord.exe"),
            AppRules.AppKey(@"C:\Users\a\AppData\Local\Discord\app-1.0.9300\Discord.exe"));
    }

    [Fact]
    public void key_for_store_app_is_app_id()
    {
        const string appId = "Microsoft.ZuneMusic_8wekyb3d8bbwe!App";
        Assert.Equal(appId, AppRules.KeyForApp(false, "x.exe", appId));
        Assert.Equal("chrome.exe", AppRules.KeyForApp(true, @"C:\X\Chrome.EXE", "ignored"));
    }

    [Fact]
    public void ancestors_stop_before_explorer()
    {
        var chain = new[] { P(3, "a.exe"), P(2, "b.exe"), P(1, "explorer.exe"), P(0, "x.exe") };
        Assert.Equal(new uint[] { 2 }, AppRules.Ancestors(chain));
        Assert.Empty(AppRules.Ancestors(new[] { P(3, "a.exe") }));
    }

    [Fact]
    public void neutral_windows()
    {
        Assert.True(AppRules.IsNeutral(W("chrome.exe", "Chrome_WidgetWin_1", true)));
        Assert.True(AppRules.IsNeutral(W("explorer.exe", "Shell_TrayWnd", false)));
        Assert.True(AppRules.IsNeutral(W("explorer.exe", "Progman", false)));
        Assert.False(AppRules.IsNeutral(W("explorer.exe", "CabinetWClass", false)));
        Assert.True(AppRules.IsNeutral(W("searchhost.exe", "Windows.UI.Core.CoreWindow", false)));
        Assert.True(AppRules.IsNeutral(W("systemsettings.exe", "ApplicationFrameWindow", false)));
        Assert.True(AppRules.IsNeutral(W("sndvol.exe", "#32770", false)));
        Assert.False(AppRules.IsNeutral(W("chrome.exe", "Chrome_WidgetWin_1", false)));
        Assert.False(AppRules.IsNeutral(W("", "", false)));
    }

    [Fact]
    public void own_window_is_neutral()
    {
        Assert.True(AppRules.IsNeutral(new FgWindow("eartrumpet.exe", "HwndWrapper[EarTrumpet.exe;;1]", true)));
    }

    [Fact]
    public void identifier_gives_app()
    {
        const string id = @"{0.0.0.00000000}.{bcef8395-38a0-42fb-aa07-a6f8ecd3519a}|\Device\HarddiskVolume3\Windows\System32\WindowsPowerShell\v1.0\powershell.exe%b{00000000-0000-0000-0000-000000000000}";
        Assert.Equal("powershell.exe", AppRules.AppOfIdentifier(id));
        Assert.Equal("powershell.exe", AppRules.AppOfIdentifier(id + "|1%b172060"));
        Assert.Equal("", AppRules.AppOfIdentifier("garbage"));
    }

    [Fact]
    public void volume_change_keeps_quieti_mute()
    {
        Assert.True(AppRules.ShouldKeepQuietiMuteOnVolumeChange(true, false));
        Assert.True(AppRules.ShouldKeepQuietiMuteOnVolumeChange(true, true));
        Assert.False(AppRules.ShouldKeepQuietiMuteOnVolumeChange(false, false));
        Assert.False(AppRules.ShouldKeepQuietiMuteOnVolumeChange(false, true));
    }
}
