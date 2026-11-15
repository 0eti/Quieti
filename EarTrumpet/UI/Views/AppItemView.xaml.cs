using EarTrumpet.Extensions;
using EarTrumpet.UI.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EarTrumpet.UI.Views;

public partial class AppItemView : UserControl
{
    private IAppItemViewModel App => (IAppItemViewModel)DataContext;

    public AppItemView()
    {
        InitializeComponent();

        // Preview events tunnel from the row down, so the row sees the click before the button does:
        // a right-click on the icon is a full mute (Quieti), and Handled keeps it from opening the popup.
        PreviewMouseRightButtonUp += (_, e) =>
        {
            // OriginalSource can be a non-Visual (e.g. a Run in text), which IsAncestorOf rejects with an exception.
            if (e.OriginalSource == MuteButton || (e.OriginalSource is Visual source && MuteButton.IsAncestorOf(source)))
            {
                App?.ToggleFullMute.Execute(null);
                e.Handled = true;
                return;
            }
            OpenPopup();
        };
        Loaded += (_, __) =>
        {
            var container = this.FindVisualParent<ListViewItem>();
            if (container != null)
            {
                container.PreviewKeyDown += OnPreviewKeyDown;
            }
        };
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.Right:
                case Key.OemPlus:
                    App.Volume += 10;
                    e.Handled = true;
                    break;
                case Key.Left:
                case Key.OemMinus:
                    App.Volume -= 10;
                    e.Handled = true;
                    break;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.M:
            case Key.OemPeriod:
                App.ToggleFullMute.Execute(null);
                e.Handled = true;
                break;
            case Key.B:
                App.ToggleBackgroundMute.Execute(null);
                e.Handled = true;
                break;
            case Key.Right:
            case Key.OemPlus:
                App.Volume++;
                e.Handled = true;
                break;
            case Key.Left:
            case Key.OemMinus:
                App.Volume--;
                e.Handled = true;
                break;
            case Key.Space:
                OpenPopup();
                e.Handled = true;
                break;
        }
    }

    private void OpenPopup()
    {
        var viewModel = Window.GetWindow(this).DataContext as IPopupHostViewModel;
        if (viewModel != null && App != null && !App.IsExpanded)
        {
            viewModel.OpenPopup(App, this);
        }
    }
}
