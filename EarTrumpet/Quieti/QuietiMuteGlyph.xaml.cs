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

    public QuietiMuteGlyph()
    {
        InitializeComponent();
    }
}
