namespace LogiWheelForge.Core.Tests;

internal sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
    private readonly List<FakeTimer> _timers = [];
    public override DateTimeOffset GetUtcNow() => _now;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state);
        _timers.Add(timer); timer.Change(dueTime, period); return timer;
    }
    public void Advance(double milliseconds)
    {
        var target = _now.AddMilliseconds(milliseconds);
        while (_timers.Where(t => t.Due <= target).OrderBy(t => t.Due).FirstOrDefault() is { } timer)
        {
            _now = timer.Due!.Value;
            timer.Due = timer.Period == Timeout.InfiniteTimeSpan ? null : _now + timer.Period;
            timer.Callback(timer.State);
        }
        _now = target;
    }
    private sealed class FakeTimer(FakeClock clock, TimerCallback callback, object? state) : ITimer
    {
        public TimerCallback Callback { get; } = callback;
        public object? State { get; } = state;
        public DateTimeOffset? Due { get; set; }
        public TimeSpan Period { get; private set; }
        public bool Change(TimeSpan dueTime, TimeSpan period)
        { Period = period; Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime; return true; }
        public void Dispose() { Due = null; clock._timers.Remove(this); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
