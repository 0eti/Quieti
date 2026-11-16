using EarTrumpet.Quieti;

namespace EarTrumpet.Quieti.Tests;

public class TrayIconIdTests
{
    private static readonly Guid Base = new("24566d08-2118-4267-bab6-faa7b5f5e1d6");

    [Fact]
    public void same_base_and_path_give_the_same_id()
    {
        Assert.Equal(TrayIconId.For(Base, @"D:\Q\Release\Quieti.exe"), TrayIconId.For(Base, @"D:\Q\Release\Quieti.exe"));
    }

    [Fact]
    public void path_case_does_not_matter()
    {
        Assert.Equal(TrayIconId.For(Base, @"D:\Q\Release\Quieti.exe"), TrayIconId.For(Base, @"d:\q\release\QUIETI.EXE"));
    }

    [Fact]
    public void another_exe_path_gets_another_id()
    {
        // Windows binds a tray icon GUID to the exe path that first used it.
        Assert.NotEqual(TrayIconId.For(Base, @"D:\Q\Debug\Quieti.exe"), TrayIconId.For(Base, @"D:\Q\Release\Quieti.exe"));
    }

    [Fact]
    public void another_base_gets_another_id()
    {
        Assert.NotEqual(TrayIconId.For(Base, @"D:\Q\Quieti.exe"), TrayIconId.For(Guid.NewGuid(), @"D:\Q\Quieti.exe"));
    }

    [Fact]
    public void id_is_never_empty_and_differs_from_the_base()
    {
        var id = TrayIconId.For(Base, @"D:\Q\Quieti.exe");
        Assert.NotEqual(Guid.Empty, id);
        Assert.NotEqual(Base, id);
    }
}
