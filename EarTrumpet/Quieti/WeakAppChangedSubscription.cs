using System;

namespace EarTrumpet.Quieti;

/// <summary>
///     Subscribes to QuietiService.AppChanged without keeping the target alive (the service is a
///     singleton). The callback must not capture the target. It never throws: AppChanged can be raised
///     from inside a native event callback.
/// </summary>
internal sealed class WeakAppChangedSubscription<T> : IDisposable where T : class
{
    private readonly WeakReference<T> _target;
    private readonly Action<T, string> _onChanged;
    private int _disposed;

    public WeakAppChangedSubscription(T target, Action<T, string> onChanged)
    {
        _target = new WeakReference<T>(target);
        _onChanged = onChanged;
        QuietiService.Current.AppChanged += OnAppChanged;
    }

    private void OnAppChanged(string appKey)
    {
        if (!_target.TryGetTarget(out var target))
        {
            Dispose();
            return;
        }
        try
        {
            _onChanged(target, appKey);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Quieti AppChanged handler failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            QuietiService.Current.AppChanged -= OnAppChanged;
        }
    }
}
