using EarTrumpet.UI.Helpers;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;

namespace EarTrumpet.UI.ViewModels;

public interface IAppItemViewModel : IAppIconSource, INotifyPropertyChanged
{
    string Id { get; }
    bool IsMuted { get; set; }
    float Volume { get; set; }
    Color Background { get; }
    ObservableCollection<IAppItemViewModel> ChildApps { get; }
    string DisplayName { get; }
    string ExeName { get; }
    string AppId { get; }
    string PackageInstallPath { get; }
    char IconText { get; }
    bool IsExpanded { get; }
    bool IsMovable { get; }
    float PeakValue1 { get; }
    float PeakValue2 { get; }
    string PersistedOutputDevice { get; }
    uint ProcessId { get; }
    bool DoesGroupWith(IAppItemViewModel app);
    void MoveToDevice(string id, bool hide);
    void UpdatePeakValueForeground();
    void UpdatePeakValueBackground();
    IDeviceViewModel Parent { get; }

    // Quieti: mute while in the background (left click), full mute (icon right click).
    bool IsBackgroundMuteArmed { get; }
    bool IsMutedByQuieti { get; }
    ICommand ToggleBackgroundMute { get; }
    ICommand ToggleFullMute { get; }
    string MuteButtonToolTip { get; }
}