using LogiWheelForge.Services;
using LogiWheelForge.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogiWheelForge.Core.Tests;

[TestClass]
public sealed class AutosaveTests
{
    [TestMethod]
    public async Task Debounce_WritesLatestEditAfter400Milliseconds()
    {
        var clock = new FakeClock(); var value = 1; var written = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var save = new RevisionedAutosave<int>(() => value, _ => { }, snapshot => { written.TrySetResult(snapshot); return Task.CompletedTask; }, clock);
        save.Edit(); clock.Advance(200); value = 2; save.Edit();
        clock.Advance(399); Assert.IsFalse(written.Task.IsCompleted);
        clock.Advance(1); Assert.AreEqual(2, await written.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await save.FlushAsync(); Assert.AreEqual("Saved", save.Status); Assert.IsFalse(save.IsDirty);
    }

    [TestMethod]
    public async Task OlderWrite_CannotAcknowledgeNewerEditAndWritesAreSerialized()
    {
        var clock = new FakeClock(); var value = 1; var inFlight = 0; var maxInFlight = 0;
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new List<int>();
        using var save = new RevisionedAutosave<int>(() => value, _ => { }, async snapshot =>
        {
            maxInFlight = Math.Max(maxInFlight, ++inFlight); writes.Add(snapshot);
            if (snapshot == 1) { firstStarted.SetResult(); await finishFirst.Task; }
            inFlight--;
        }, clock);
        save.Edit(); clock.Advance(400);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        value = 2; save.Edit();
        Assert.IsTrue(save.IsDirty); Assert.AreEqual("Saving", save.Status);
        var flush = save.FlushAsync(); finishFirst.SetResult(); await flush;
        CollectionAssert.AreEqual(new[] { 1, 2 }, writes); Assert.AreEqual(1, maxInFlight);
        Assert.IsFalse(save.IsDirty); Assert.AreEqual("Saved", save.Status);
    }

    [TestMethod]
    public async Task InvalidDraft_IsRetainedAndCannotReplaceRuntimeState()
    {
        var value = 4; var runtime = 4;
        using var save = new RevisionedAutosave<int>(() => value, v => { if (v < 0) throw new ArgumentException("Use a positive number"); }, v => { runtime = v; return Task.CompletedTask; }, new FakeClock());
        value = -1; save.Edit();
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => save.FlushAsync());
        Assert.AreEqual(4, runtime); Assert.AreEqual(-1, value);
        Assert.IsTrue(save.IsDirty); Assert.AreEqual("Needs attention", save.Status);
        value = 8; save.Edit(); await save.FlushAsync(); Assert.AreEqual(8, runtime); Assert.IsNull(save.Error);
    }

    [TestMethod]
    public async Task WriteFailure_StaysDirtyAndFlushCanRetry()
    {
        var fail = true; var writes = 0;
        using var save = new RevisionedAutosave<int>(() => 1, _ => { }, _ =>
        { if (fail) throw new IOException("Disk unavailable"); writes++; return Task.CompletedTask; }, new FakeClock());
        save.Edit(); await Assert.ThrowsExceptionAsync<IOException>(() => save.FlushAsync());
        Assert.IsTrue(save.IsDirty); Assert.AreEqual("Needs attention", save.Status); Assert.AreEqual("Disk unavailable", save.Error);
        fail = false; await save.FlushAsync(); Assert.AreEqual(1, writes); Assert.IsFalse(save.IsDirty);
    }

    [TestMethod]
    public async Task ShutdownFlush_DoesNotWaitForDebounceOrRewriteCleanState()
    {
        var writes = 0;
        using var save = new RevisionedAutosave<int>(() => 42, _ => { }, _ => { writes++; return Task.CompletedTask; }, new FakeClock());
        save.Edit(); await save.FlushAsync(); await save.FlushAsync(); Assert.AreEqual(1, writes);
    }

    [TestMethod]
    public void InvalidNumberText_IsRetainedUntilCorrected()
    {
        var fields = new DraftNumberFields(); var stored = 5d; var changes = 0;
        fields.Set("Step", "unfinished", v => stored = v, () => changes++);
        Assert.AreEqual(5d, stored); Assert.AreEqual("unfinished", fields.Get("Step", stored));
        Assert.AreEqual(1, changes); Assert.ThrowsException<ArgumentException>(() => fields.Validate());
        fields.Set("Step", "2", v => stored = v, () => changes++);
        fields.Validate(); Assert.AreEqual(2d, stored); Assert.AreEqual("2", fields.Get("Step", stored));
    }
}
