using EarTrumpet.DataModel.AppInformation;
using EarTrumpet.DataModel.Audio;
using EarTrumpet.DataModel.Storage;
using EarTrumpet.DataModel.WindowsAudio.Internal;
using EarTrumpet.Interop.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;

namespace EarTrumpet.Quieti;

/// <summary>
///     Connects the MuteEngine to EarTrumpet: playback sessions in, foreground window in, mute commands
///     out, armed apps and Quieti mutes persisted. Everything runs on the UI thread.
/// </summary>
public sealed class QuietiService
{
    private const string ArmedKey = "Quieti.Armed";
    private const string RecoveredKey = "Quieti.Recovered";

    public static QuietiService Current { get; } = new();

    /// <summary>Raised with an app key whose IsArmed or StateOf may have changed (to refresh its rows).</summary>
    public event Action<string> AppChanged;

    private sealed class Tracked(AudioDeviceSession session, SessionKey key, string app)
    {
        public AudioDeviceSession Session { get; } = session;
        public SessionKey Key { get; } = key;
        public string App { get; } = app;
    }

    private readonly EchoFilter _filter = new();
    /// <summary>Events waiting to be handled, each with the app it touches (null: none).</summary>
    private readonly Queue<(EngineEvent Event, string App)> _pending = new();
    /// <summary>Real sessions by AudioDeviceSession.Id.</summary>
    private readonly Dictionary<string, Tracked> _sessions = new(StringComparer.Ordinal);
    /// <summary>Group collections under the playback devices that we listen to.</summary>
    private readonly HashSet<INotifyCollectionChanged> _watched = [];
    private readonly uint _ownPid = (uint)Environment.ProcessId;

    /// <summary>
    ///     Windows does not send a foreground event for every switch (seen when going back and forth between
    ///     Chrome and an Electron window), so the foreground handle is also checked on a timer. The timer runs only
    ///     while an armed app has a session, and a check that finds the same window does nothing.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private DispatcherTimer _poll;
    /// <summary>The foreground handle OnForeground last read.</summary>
    private nint _lastForeground;

    private MuteEngine _engine;
    private IAudioDeviceManager _playback;
    private ISettingsBag _settings;
    private HashSet<string> _savedArmed = new(StringComparer.Ordinal);
    private HashSet<string> _savedOurs = new(StringComparer.Ordinal);
    private bool _draining;
    private bool _stopped;
    /// <summary>
    ///     Ids of the sessions the engine counts as Quieti-muted, refreshed after every engine event. Replaced as a
    ///     whole, so IsOurs can be read from any thread (volume can be set from an add-on's background trigger).
    /// </summary>
    private volatile HashSet<string> _ours = new(StringComparer.Ordinal);

    private QuietiService()
    {
    }

    /// <summary>
    ///     The app key of a session or app row. Desktop apps: the exe file name with its extension, taken from
    ///     AppId (the full image path for desktop apps; ExeName has the extension stripped). Store apps: AppId.
    /// </summary>
    public static string AppKeyOf(bool isDesktopApp, string appId) => AppRules.AppKeyOf(isDesktopApp, appId);

    public void Start(IAudioDeviceManager playback, ISettingsBag settings)
    {
        if (_engine != null)
        {
            return;
        }
        _playback = playback;
        _settings = settings;
        var armed = Load(ArmedKey);
        var recovered = Load(RecoveredKey);
        _savedArmed = new HashSet<string>(armed, StringComparer.Ordinal);
        _savedOurs = new HashSet<string>(recovered, StringComparer.Ordinal);
        _engine = new MuteEngine(armed, recovered);
        Trace.WriteLine($"Quieti Start armed={armed.Length} recovered={recovered.Length}");

        // The engine treats every armed app as background until it knows the foreground:
        // feed the current foreground before any session (Rust ruling).
        _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = PollInterval };
        _poll.Tick += OnPoll;
        ForegroundWatcher.Install(OnForeground);
        OnForeground();

        _playback.Devices.CollectionChanged += OnModelChanged;
        Resync();
    }

    public bool IsArmed(string appKey) => _engine?.IsArmed(appKey) ?? false;

    public AppState StateOf(string appKey) => _engine?.StateOf(appKey) ?? AppState.NotRunning;

    /// <summary>
    ///     The session's mute is Quieti's (the engine's answer: recovered and sibling sessions included, full mutes
    ///     and unarmed apps excluded). Asked by the mute copy sites and the volume guards. Safe on any thread.
    /// </summary>
    public bool IsOurs(string sessionId) => sessionId != null && _ours.Contains(sessionId);

    public void ToggleArmed(string appKey)
    {
        if (_engine == null || _stopped || string.IsNullOrEmpty(appKey))
        {
            return;
        }
        Feed(new EngineEvent.SetArmed(appKey, !_engine.IsArmed(appKey)), appKey);
    }

    /// <summary>
    ///     Full mute on/off for the app. Returns false when Quieti does not handle it (not started or stopped, no key,
    ///     or no session of the app known to the engine): the caller then toggles IsMuted like upstream EarTrumpet.
    /// </summary>
    public bool TryToggleFullMute(string appKey)
    {
        if (_engine == null || _stopped || string.IsNullOrEmpty(appKey) || _engine.StateOf(appKey) == AppState.NotRunning)
        {
            return false;
        }
        Feed(new EngineEvent.SetFullMute(appKey, !_engine.IsFullMuted(appKey)), appKey);
        return true;
    }

    /// <summary>
    ///     On logoff/shutdown (SessionEnding): undo Quieti mutes (synchronously) and save, but keep running. If the
    ///     logoff is cancelled, the next foreground change re-applies the rules (and SaveIfChanged records the re-mutes).
    /// </summary>
    public void ReleaseAll()
    {
        if (_engine == null || _stopped)
        {
            return;
        }
        Trace.WriteLine("Quieti ReleaseAll");
        Feed(new EngineEvent.Shutdown(), null);
        Save(ArmedKey, _engine.Armed);
        Save(RecoveredKey, _engine.Ours());
    }

    /// <summary>On exit: undo Quieti mutes (synchronously), then save. Runs once.</summary>
    public void Shutdown()
    {
        if (_engine == null || _stopped)
        {
            return;
        }
        Trace.WriteLine("Quieti Shutdown");
        ForegroundWatcher.Uninstall();
        _poll?.Stop();
        Feed(new EngineEvent.Shutdown(), null);
        _stopped = true;

        _playback.Devices.CollectionChanged -= OnModelChanged;
        foreach (var collection in _watched)
        {
            collection.CollectionChanged -= OnModelChanged;
        }
        _watched.Clear();
        foreach (var tracked in _sessions.Values)
        {
            tracked.Session.MuteNotified -= OnMuteNotified;
            tracked.Session.PropertyChanged -= OnSessionPropertyChanged;
        }
        Save(ArmedKey, _engine.Armed);
        Save(RecoveredKey, _engine.Ours());
    }

    // ---- Events into the engine ----

    /// <summary>
    ///     Handles ev and the events its commands cause, in order. Re-entrant calls (from a command,
    ///     a handler or a nested message pump) only queue, so nothing recurses.
    /// </summary>
    private void Feed(EngineEvent ev, string app)
    {
        _pending.Enqueue((ev, app));
        if (_draining)
        {
            return;
        }
        _draining = true;
        try
        {
            while (_pending.Count > 0)
            {
                var (next, nextApp) = _pending.Dequeue();
                var touched = new HashSet<string>(StringComparer.Ordinal);
                if (!string.IsNullOrEmpty(nextApp))
                {
                    touched.Add(nextApp);
                }
                var cmds = _engine.Handle(next);
                RefreshOurs();
                foreach (var cmd in cmds)
                {
                    if (Execute(cmd) is { } cmdApp && cmdApp.Length > 0)
                    {
                        touched.Add(cmdApp);
                    }
                }
                SaveIfChanged();
                foreach (var changed in touched)
                {
                    AppChanged?.Invoke(changed);
                }
            }
            UpdatePolling();
        }
        finally
        {
            _draining = false;
        }
    }

    /// <summary>Runs the foreground timer only while an armed app has a session (otherwise nothing could change).</summary>
    private void UpdatePolling()
    {
        if (_poll == null)
        {
            return;
        }
        var needed = !_stopped && _sessions.Values.Any(t => _engine.IsArmed(t.App));
        if (needed && !_poll.IsEnabled)
        {
            // _lastForeground is what OnForeground last read, so a switch made before the start is caught too.
            _poll.Start();
        }
        else if (!needed && _poll.IsEnabled)
        {
            _poll.Stop();
        }
    }

    private void OnPoll(object sender, EventArgs e)
    {
        if (ForegroundWatcher.CurrentHandle() != _lastForeground)
        {
            Trace.WriteLine("Quieti foreground changed without an event");
            OnForeground();
        }
    }

    private void RefreshOurs()
    {
        var ours = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tracked in _sessions.Values)
        {
            if (_engine.IsOurs(tracked.Key))
            {
                ours.Add(tracked.Key.Value);
            }
        }
        _ours = ours;
    }

    /// <summary>Runs one engine command. Returns the session's app, or null when the session is gone.</summary>
    private string Execute(SetMuteCommand cmd)
    {
        if (!_sessions.TryGetValue(cmd.Key.Value, out var tracked))
        {
            return null;
        }
        var session = tracked.Session;
        var before = session.IsMuted;
        try
        {
            session.SetMute(cmd.Mute, cmd.Context == MuteContext.Quieti ? QuietiContext.Mute : QuietiContext.UserMute);
            _filter.Issued(cmd.Key, cmd.Mute, before);
            Trace.WriteLine($"Quieti SetMute {tracked.App} {cmd.Key.Value} mute={cmd.Mute} {cmd.Context}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Quieti SetMute failed {tracked.App} {cmd.Key.Value} mute={cmd.Mute} {cmd.Context}: {ex.Message}");
            _pending.Enqueue((new EngineEvent.MuteFailed(cmd.Key), tracked.App));
        }
        return tracked.App;
    }

    private void OnForeground()
    {
        if (_engine == null || _stopped)
        {
            return;
        }
        _lastForeground = ForegroundWatcher.CurrentHandle();
        (uint Pid, FgWindow Window)? read;
        try
        {
            read = ForegroundWatcher.Read(_ownPid);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Quieti foreground read failed: {ex.Message}");
            return;
        }
        if (read is not { } fg)
        {
            Trace.WriteLine("Quieti foreground ignored: no foreground window");
            return;
        }
        if (AppRules.IsNeutral(fg.Window))
        {
            Trace.WriteLine($"Quieti foreground ignored (neutral): {fg.Window.Exe} {fg.Window.Class} own={fg.Window.Own}");
            return;
        }
        var app = ForegroundAppKey(fg.Pid, fg.Window.Exe);
        Trace.WriteLine($"Quieti foreground: {app} pid={fg.Pid}");
        Feed(new EngineEvent.Foreground(new Fg(fg.Pid, app)), null);
    }

    /// <summary>Store apps are keyed by AppId like their sessions (AppKeyOf); other apps by exe name.</summary>
    private static string ForegroundAppKey(uint pid, string exe)
    {
        if (Kernel32Helper.IsPackagedProcess(pid))
        {
            try
            {
                var info = AppInformationFactory.CreateForProcess(pid);
                var key = AppKeyOf(info.IsDesktopApp, info.AppId);
                if (key.Length > 0)
                {
                    return key;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Quieti foreground app info failed pid={pid}: {ex.Message}");
            }
        }
        return exe;
    }

    private void OnMuteNotified(AudioDeviceSession session, bool muted, Guid context)
    {
        if (_stopped || !_sessions.TryGetValue(session.Id, out var tracked) || tracked.Session != session)
        {
            return;
        }
        var kind = context == QuietiContext.Mute ? NoticeKind.Quieti
            : context == QuietiContext.UserMute ? NoticeKind.UserMute
            : NoticeKind.External;
        if (_filter.Accept(tracked.Key, muted, kind) is { } ev)
        {
            Feed(ev, tracked.App);
        }
    }

    // ---- Session discovery ----

    private void OnModelChanged(object sender, NotifyCollectionChangedEventArgs e) => Resync();

    private void OnSessionPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IAudioDeviceSession.State) && sender is AudioDeviceSession { State: SessionState.Expired })
        {
            Resync();
        }
    }

    /// <summary>
    ///     Walks playback devices → Groups → group Children (app groups, then grouping-param groups) down to the
    ///     real AudioDeviceSession objects, listens to every collection on the way, and turns the difference
    ///     with the tracked set into SessionGone (first) and SessionAdded events. Expired sessions count as gone.
    /// </summary>
    private void Resync()
    {
        if (_engine == null || _stopped)
        {
            return;
        }
        var collections = new List<INotifyCollectionChanged>();
        var live = new List<AudioDeviceSession>();
        var liveById = new Dictionary<string, AudioDeviceSession>(StringComparer.Ordinal);

        void Walk(IAudioDeviceSession node)
        {
            switch (node)
            {
                case AudioDeviceSession real:
                    if (real.State != SessionState.Expired && liveById.TryAdd(real.Id, real))
                    {
                        live.Add(real);
                    }
                    break;
                case AudioDeviceSessionGroup group:
                    collections.Add(group.Children);
                    foreach (var child in group.Children.ToArray())
                    {
                        Walk(child);
                    }
                    break;
            }
        }

        foreach (var device in _playback.Devices.ToArray())
        {
            collections.Add(device.Groups);
            foreach (var group in device.Groups.ToArray())
            {
                Walk(group);
            }
        }

        var keep = collections.ToHashSet();
        foreach (var old in _watched.Where(c => !keep.Contains(c)).ToList())
        {
            old.CollectionChanged -= OnModelChanged;
            _watched.Remove(old);
        }
        foreach (var collection in collections)
        {
            if (_watched.Add(collection))
            {
                collection.CollectionChanged += OnModelChanged;
            }
        }

        var gone = _sessions.Values
            .Where(t => !liveById.TryGetValue(t.Key.Value, out var s) || s != t.Session)
            .ToList();
        foreach (var tracked in gone)
        {
            Untrack(tracked);
        }
        foreach (var session in live)
        {
            if (!_sessions.ContainsKey(session.Id))
            {
                Track(session);
            }
        }
    }

    private void Track(AudioDeviceSession session)
    {
        var key = new SessionKey(session.Id);
        var identifier = session.Identifier ?? "";
        var app = AppKeyOf(session.IsDesktopApp, session.AppId);
        if (app.Length == 0)
        {
            app = AppRules.AppOfIdentifier(identifier);
        }
        var tracked = new Tracked(session, key, app);
        _sessions[session.Id] = tracked;
        session.MuteNotified += OnMuteNotified;
        session.PropertyChanged += OnSessionPropertyChanged;

        var ancestors = AppRules.Ancestors(ProcessTree.Chain(session.ProcessId));
        var info = new SessionInfo(key, session.ProcessId, app, session.DisplayName ?? app, ancestors, identifier, session.IsMuted);
        Feed(new EngineEvent.SessionAdded(info), app);
    }

    private void Untrack(Tracked tracked)
    {
        // A nested Resync (from an AppChanged handler) may already have replaced or removed it.
        if (!_sessions.TryGetValue(tracked.Key.Value, out var current) || current != tracked)
        {
            return;
        }
        _sessions.Remove(tracked.Key.Value);
        tracked.Session.MuteNotified -= OnMuteNotified;
        tracked.Session.PropertyChanged -= OnSessionPropertyChanged;
        _filter.Forget(tracked.Key);
        Feed(new EngineEvent.SessionGone(tracked.Key), tracked.App);
    }

    // ---- Persistence ----

    private string[] Load(string key)
    {
        try
        {
            return _settings.Get(key, Array.Empty<string>()) ?? [];
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Quieti could not read {key}: {ex.Message}");
            return [];
        }
    }

    private void SaveIfChanged()
    {
        var armed = _engine.Armed;
        if (!_savedArmed.SetEquals(armed))
        {
            _savedArmed = new HashSet<string>(armed, StringComparer.Ordinal);
            Save(ArmedKey, _savedArmed);
        }
        var ours = _engine.Ours();
        if (!_savedOurs.SetEquals(ours))
        {
            _savedOurs = new HashSet<string>(ours, StringComparer.Ordinal);
            Save(RecoveredKey, _savedOurs);
        }
    }

    private void Save(string key, IEnumerable<string> values)
    {
        try
        {
            _settings.Set(key, values.OrderBy(v => v, StringComparer.Ordinal).ToArray());
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Quieti could not save {key}: {ex.Message}");
        }
    }
}
