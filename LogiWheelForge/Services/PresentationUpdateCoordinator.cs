namespace LogiWheelForge.Services;

/// <summary>Presentation only. Producers replace cached snapshots and never enqueue UI work.</summary>
public sealed class PresentationUpdateCoordinator(TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly List<Registration> _views = [];
    private DateTimeOffset _next;
    private bool _visible = true, _focused = true, _reduce = true;
    public TimeSpan? Interval => !_visible ? null : !_focused && _reduce
        ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromSeconds(1d / 30);

    public IDisposable Register(Action refresh, Func<bool>? mounted = null)
    {
        var registration = new Registration(this, refresh, mounted ?? (() => true));
        _views.Add(registration);
        return registration;
    }

    public void SetWindowState(bool visible, bool focused, bool reduceWhenUnfocused)
    {
        var changed = _visible != visible || _focused != focused || _reduce != reduceWhenUnfocused;
        _visible = visible; _focused = focused; _reduce = reduceWhenUnfocused;
        if (changed) RefreshImmediately();
    }

    public void RefreshImmediately() { _next = DateTimeOffset.MinValue; Tick(); }
    public void Tick()
    {
        if (Interval is not { } interval || _clock.GetUtcNow() < _next) return;
        _next = _clock.GetUtcNow() + interval;
        foreach (var view in _views.ToArray()) if (view.Mounted()) view.Refresh();
    }

    private sealed class Registration(PresentationUpdateCoordinator owner, Action refresh, Func<bool> mounted) : IDisposable
    {
        public Action Refresh { get; } = refresh;
        public Func<bool> Mounted { get; } = mounted;
        public void Dispose() => owner._views.Remove(this);
    }
}

internal sealed class LatestSnapshot<T> where T : class
{
    private T? _value;
    public T? Value => Volatile.Read(ref _value);
    public void Set(T value) => Volatile.Write(ref _value, value);
}
