using EarTrumpet.UI.ViewModels;

namespace EarTrumpet.Quieti;

/// <summary>The "Quieti" page in Settings: volume step, moon badge, start with Windows.</summary>
public class QuietiSettingsPageViewModel : SettingsPageViewModel
{
    private readonly AppSettings _settings;

    public QuietiSettingsPageViewModel(AppSettings settings) : base(null)
    {
        _settings = settings;
        Title = "Quieti";
        Glyph = ""; // SettingsWindow draws Quieti's moon for this page (matched by Title)
    }

    public int VolumeStepPercent
    {
        get => _settings.VolumeStepPercent;
        set
        {
            _settings.VolumeStepPercent = value;
            RaisePropertyChanged(nameof(VolumeStepPercent));
        }
    }

    public bool ShowBadge
    {
        get => _settings.ShowQuietiBadge;
        set
        {
            _settings.ShowQuietiBadge = value;
            RaisePropertyChanged(nameof(ShowBadge));
        }
    }

    public bool StartWithWindows
    {
        get => StartupRegistration.IsEnabled;
        set
        {
            StartupRegistration.IsEnabled = value;
            RaisePropertyChanged(nameof(StartWithWindows));
        }
    }
}
