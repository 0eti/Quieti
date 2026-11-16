using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace EarTrumpet.Quieti;

/// <summary>"Start with Windows" for the unpackaged Quieti.exe: a value in the user's Run key.
/// On means the value points to this exe; Windows Settings > Startup apps can still turn it off on its side.</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string ExePath => Environment.ProcessPath ?? "";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return StartupEntry.PointsTo(key?.GetValue(StartupEntry.ValueName) as string, ExePath);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"StartupRegistration IsEnabled: {ex}");
                return false;
            }
        }
        set
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
                if (value)
                {
                    key.SetValue(StartupEntry.ValueName, StartupEntry.CommandFor(ExePath));
                }
                else
                {
                    key.DeleteValue(StartupEntry.ValueName, false);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"StartupRegistration set {value}: {ex}");
            }
        }
    }
}
