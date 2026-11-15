using System.Windows;
using System.Windows.Controls;

namespace EarTrumpet.Quieti;

public partial class CrescentBadge : UserControl
{
    /// <summary>Theme brush expression for the fill (EarTrumpet Theme:Brush syntax). Defaults to the system accent.</summary>
    public static readonly DependencyProperty FillThemeProperty =
        DependencyProperty.Register(nameof(FillTheme), typeof(string), typeof(CrescentBadge), new PropertyMetadata("SystemAccent"));

    public string FillTheme
    {
        get => (string)GetValue(FillThemeProperty);
        set => SetValue(FillThemeProperty, value);
    }

    public CrescentBadge()
    {
        InitializeComponent();
    }
}
