using EarTrumpet.Quieti;

namespace EarTrumpet.Quieti.Tests;

public class PreferencesTests
{
    [Theory]
    [InlineData(2, 2)]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(11, 10)]
    public void volume_step_is_kept_between_1_and_10(int stored, int expected)
    {
        Assert.Equal(expected, VolumeStep.Clamp(stored));
    }

    [Fact]
    public void linear_delta_is_the_step_in_percent()
    {
        Assert.Equal(2f, VolumeStep.Delta(2, logarithmic: false));
        Assert.Equal(7f, VolumeStep.Delta(7, logarithmic: false));
    }

    [Fact]
    public void logarithmic_delta_is_a_tenth_of_the_step_like_upstream()
    {
        // EarTrumpet moved 2 (linear) or 0.2 (logarithmic) per wheel notch.
        Assert.Equal(0.2f, VolumeStep.Delta(2, logarithmic: true), 5);
        Assert.Equal(1f, VolumeStep.Delta(10, logarithmic: true), 5);
    }

    [Fact]
    public void delta_uses_the_clamped_step()
    {
        Assert.Equal(10f, VolumeStep.Delta(50, logarithmic: false));
        Assert.Equal(1f, VolumeStep.Delta(0, logarithmic: false));
    }

    [Fact]
    public void startup_command_quotes_the_exe_path()
    {
        Assert.Equal("\"C:\\Apps\\Quieti\\Quieti.exe\"", StartupEntry.CommandFor(@"C:\Apps\Quieti\Quieti.exe"));
    }

    [Theory]
    [InlineData("\"C:\\Apps\\Quieti\\Quieti.exe\"")]
    [InlineData("C:\\Apps\\Quieti\\Quieti.exe")]
    [InlineData("\"c:\\apps\\quieti\\QUIETI.EXE\"")]
    [InlineData("  \"C:\\Apps\\Quieti\\Quieti.exe\"  ")]
    public void startup_entry_points_to_this_exe(string value)
    {
        Assert.True(StartupEntry.PointsTo(value, @"C:\Apps\Quieti\Quieti.exe"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\"D:\\Old\\Quieti.exe\"")]
    [InlineData("\"C:\\Apps\\Quieti\\Quieti.exe\" --other")]
    public void startup_entry_for_another_exe_or_missing_is_off(string? value)
    {
        Assert.False(StartupEntry.PointsTo(value, @"C:\Apps\Quieti\Quieti.exe"));
    }
}
