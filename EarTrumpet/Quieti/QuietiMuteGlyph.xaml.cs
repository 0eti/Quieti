using System.Windows;
using System.Windows.Controls;

namespace EarTrumpet.Quieti;

public partial class QuietiMuteGlyph : UserControl
{
    /// <summary>Theme brush expression for the moon and the X (EarTrumpet Theme:Brush syntax).</summary>
    public static readonly DependencyProperty FillThemeProperty =
        DependencyProperty.Register(nameof(FillTheme), typeof(string), typeof(QuietiMuteGlyph), new PropertyMetadata("SystemAccent"));

    public string FillTheme
    {
        get => (string)GetValue(FillThemeProperty);
        set => SetValue(FillThemeProperty, value);
    }

    /// <summary>Moon alone, centered in the em square (no X beside it): armed but not muted.</summary>
    public static readonly DependencyProperty CenteredProperty =
        DependencyProperty.Register(nameof(Centered), typeof(bool), typeof(QuietiMuteGlyph), new PropertyMetadata(false));

    public bool Centered
    {
        get => (bool)GetValue(CenteredProperty);
        set => SetValue(CenteredProperty, value);
    }

    public QuietiMuteGlyph()
    {
        InitializeComponent();
    }
}
