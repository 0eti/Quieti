using EarTrumpet.DataModel.Audio;
using EarTrumpet.DataModel.WindowsAudio.Internal;
using EarTrumpet.DataModel.WindowsAudio;
using EarTrumpet.Extensions;
using EarTrumpet.Quieti;
using EarTrumpet.UI.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization;

namespace EarTrumpet.UI.ViewModels;

public class AppItemViewModel : AudioSessionViewModel, IAppItemViewModel
{
    public class ExeNameComparer : IComparer<IAppItemViewModel>
    {
        public int Compare(IAppItemViewModel x, IAppItemViewModel y)
        {
            return string.Compare(x.ExeName, y.ExeName, StringComparison.Ordinal);
        }
    }

    public static readonly ExeNameComparer CompareByExeName = new();

    public Color Background { get; private set; }
    public char IconText => string.IsNullOrWhiteSpace(DisplayName) ? '?' : DisplayName.ToUpperInvariant().FirstOrDefault(x => char.IsLetterOrDigit(x));
    public string DisplayName => _session.DisplayName;
    public string ExeName => _session.ExeName;
    public string AppId => _session.AppId;
    public string IconPath => _session.IconPath;
    public bool IsDesktopApp => _session.IsDesktopApp;
    public bool IsExpanded { get; private set; }
    public uint ProcessId => _session.ProcessId;
    public ObservableCollection<IAppItemViewModel> ChildApps { get; private set; }

    public bool IsMovable => !_session.IsSystemSoundsSession &&
                              Environment.OSVersion.IsAtLeast(OSVersions.RS4);
    public string PersistedOutputDevice => _session.Parent.Parent is IAudioDeviceManagerWindowsAudio ?
        ((IAudioDeviceManagerWindowsAudio)_session.Parent.Parent).GetDefaultEndPoint(ProcessId) : "";

    public IDeviceViewModel Parent
    {
        get
        {
            if (_parent.TryGetTarget(out var parent))
            {
                return parent;
            }
            return null;
        }
    }

    public string PackageInstallPath => _session.PackageInstallPath;

    internal IAudioDeviceSession Session => _session;

    // Quieti
    private string QuietiKey => QuietiService.AppKeyOf(IsDesktopApp, AppId);
    public bool IsBackgroundMuteArmed => QuietiService.Current.IsArmed(QuietiKey);
    public bool IsMutedByQuieti => QuietiService.Current.StateOf(QuietiKey) == AppState.MutedByQuieti;
    public ICommand ToggleBackgroundMute { get; }
    public ICommand ToggleFullMute { get; }
    public string MuteButtonToolTip => DisplayName + "\n" + Properties.Resources.QuietiMuteButtonHint;

    // Weak, so the static QuietiService does not keep this view model alive.
    private readonly WeakAppChangedSubscription<AppItemViewModel> _quietiSubscription;

    private readonly IAudioDeviceSession _session;
    private readonly WeakReference<DeviceViewModel> _parent;

    internal AppItemViewModel(DeviceViewModel parent, IAudioDeviceSession session, bool isChild = false) : base(session)
    {
        IsExpanded = isChild;
        _session = session;
        _session.PropertyChanged += Session_PropertyChanged;
        _parent = new WeakReference<DeviceViewModel>(parent);

        ToggleBackgroundMute = new RelayCommand(() => QuietiService.Current.ToggleArmed(QuietiKey));
        ToggleFullMute = new RelayCommand(() =>
        {
            // Rows Quieti cannot key or does not know (no AppId, no session seen yet) keep upstream's plain mute toggle.
            if (!QuietiService.Current.TryToggleFullMute(QuietiKey))
            {
                IsMuted = !IsMuted;
            }
        });
        _quietiSubscription = new WeakAppChangedSubscription<AppItemViewModel>(this, static (vm, key) => vm.OnQuietiAppChanged(key));

        if (_session.Children != null)
        {
            _session.Children.CollectionChanged += Children_CollectionChanged;
            ChildApps = new ObservableCollection<IAppItemViewModel>(_session.Children.Select(t => new AppItemViewModel(parent, t, true)));
        }
    }

    ~AppItemViewModel()
    {
        _quietiSubscription?.Dispose();
        _session.PropertyChanged -= Session_PropertyChanged;
    }

    private void OnQuietiAppChanged(string key)
    {
        if (key == QuietiKey)
        {
            RaisePropertyChanged(nameof(IsBackgroundMuteArmed));
            RaisePropertyChanged(nameof(IsMutedByQuieti));
        }
    }

    private void Session_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch(e.PropertyName)
        {
            case nameof(_session.DisplayName):
                RaisePropertyChanged(nameof(DisplayName));
                RaisePropertyChanged(nameof(MuteButtonToolTip));
                break;
            case nameof(_session.IconPath):
                RaisePropertyChanged(nameof(IconPath));
                break;
        }
    }

    private void Children_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (_parent.TryGetTarget(out var parent))
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    Debug.Assert(e.NewItems.Count == 1);
                    ChildApps.Add(new AppItemViewModel(parent, (IAudioDeviceSession)e.NewItems[0], true));
                    break;

                case NotifyCollectionChangedAction.Remove:
                    Debug.Assert(e.OldItems.Count == 1);
                    ChildApps.Remove(ChildApps.First(x => x.Id == ((IAudioDeviceSession)e.OldItems[0]).Id));
                    break;
                default:
                    throw new NotImplementedException();
            }
        }
    }

    public void MoveToDevice(string id, bool hide)
    {
        ((IAudioDeviceSessionInternal)_session).MoveToDevice(id, hide);
    }

    public override void UpdatePeakValueForeground()
    {
        if (ChildApps != null)
        {
            foreach (var child in ChildApps)
            {
                child.UpdatePeakValueForeground();
            }
        }

        base.UpdatePeakValueForeground();
    }

    public void UpdatePeakValueBackground()
    {
        if (ChildApps != null)
        {
            foreach (var child in ChildApps.ToArray())
            {
                child.UpdatePeakValueBackground();
            }
        }

        ((IAudioDeviceSessionInternal)_session).UpdatePeakValueBackground();
    }

    public bool DoesGroupWith(IAppItemViewModel app) => (AppId == app.AppId);

   public override string ToString() => IsMuted ? Properties.Resources.AppOrDeviceMutedFormatAccessibleText.Replace("{Name}", DisplayName) :
        Properties.Resources.AppOrDeviceFormatAccessibleText.Replace("{Name}", DisplayName).Replace("{Volume}", Volume.ToString(CultureInfo.CurrentCulture));
}
