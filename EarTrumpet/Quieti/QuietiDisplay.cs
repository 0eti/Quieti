using System.ComponentModel;

namespace EarTrumpet.Quieti;

/// <summary>Display options the app rows bind to as a static source, so a settings change shows at once
/// without every row subscribing to AppSettings.</summary>
public sealed class QuietiDisplay : INotifyPropertyChanged
{
    public static QuietiDisplay Current { get; } = new();

    public event PropertyChangedEventHandler PropertyChanged;

    private bool _showBadge = true;
    public bool ShowBadge
    {
        get => _showBadge;
        set
        {
            if (_showBadge != value)
            {
                _showBadge = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowBadge)));
            }
        }
    }
}
