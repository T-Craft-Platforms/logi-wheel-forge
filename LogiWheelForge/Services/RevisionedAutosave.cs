namespace LogiWheelForge.Services;

/// <summary>Debounces edits and serializes writes. A completed old write cannot acknowledge newer edits.</summary>
public sealed class RevisionedAutosave<T>(Func<T> capture, Action<T> validate, Func<T, Task> write,
    TimeProvider? clock = null) : IDisposable
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _delay;
    private long _revision, _savedRevision;
    private bool _disposed;
    public bool IsDirty => _revision != _savedRevision;
    public string Status { get; private set; } = "Saved";
    public string? Error { get; private set; }
    public event EventHandler? Changed;

    public void Edit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _revision++;
        _delay?.Cancel();
        var delay = _delay = new CancellationTokenSource();
        Status = "Saving"; Error = null; Changed?.Invoke(this, EventArgs.Empty);
        _ = SaveAfterDelayAsync(delay);
    }

    private async Task SaveAfterDelayAsync(CancellationTokenSource delay)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400), _clock, delay.Token);
            if (!delay.IsCancellationRequested) await SaveAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* SaveAsync exposes the error and keeps the draft dirty. */ }
        finally
        {
            if (ReferenceEquals(_delay, delay)) _delay = null;
            delay.Dispose();
        }
    }

    public async Task FlushAsync()
    {
        _delay?.Cancel();
        do { await SaveAsync(); } while (IsDirty);
    }

    private async Task SaveAsync()
    {
        await _gate.WaitAsync();
        var revision = _revision;
        try
        {
            if (!IsDirty) return;
            var snapshot = capture();
            validate(snapshot);
            await write(snapshot);
            _savedRevision = revision;
            if (_revision == revision) { Status = "Saved"; Error = null; }
        }
        catch (Exception ex)
        {
            if (_revision == revision) { Status = "Needs attention"; Error = ex.Message; }
            throw;
        }
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }

    public void Dispose()
    {
        _disposed = true;
        _delay?.Cancel();
        // A running write owns the gate until completion; do not dispose it under that write.
    }
}
