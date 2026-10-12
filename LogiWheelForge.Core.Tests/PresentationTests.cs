using LogiWheelForge.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogiWheelForge.Core.Tests;

[TestClass]
public sealed class PresentationTests
{
    [TestMethod]
    public void FakeClock_VerifiesFocusUnfocusedHiddenAndImmediateRestoration()
    {
        var clock = new FakeClock(); var refreshes = 0; var backend = 0;
        var coordinator = new PresentationUpdateCoordinator(clock);
        using var view = coordinator.Register(() => refreshes++);
        coordinator.Tick(); Assert.AreEqual(1, refreshes);
        clock.Advance(33); coordinator.Tick(); Assert.AreEqual(1, refreshes);
        clock.Advance(1); coordinator.Tick(); Assert.AreEqual(2, refreshes);
        coordinator.SetWindowState(true, false, true); Assert.AreEqual(3, refreshes);
        clock.Advance(499); coordinator.Tick(); Assert.AreEqual(3, refreshes);
        clock.Advance(1); coordinator.Tick(); Assert.AreEqual(4, refreshes);
        coordinator.SetWindowState(false, false, true);
        for (var i = 0; i < 100; i++) { backend++; clock.Advance(100); coordinator.Tick(); }
        Assert.AreEqual(4, refreshes); Assert.AreEqual(100, backend);
        coordinator.SetWindowState(true, false, true); Assert.AreEqual(5, refreshes);
        coordinator.SetWindowState(true, true, true); Assert.AreEqual(6, refreshes);
        Assert.AreEqual(TimeSpan.FromSeconds(1d / 30), coordinator.Interval);
    }
    [TestMethod]
    public void PreferenceOff_KeepsNormalVisibleRateAndMountedViewsOnly()
    {
        var clock = new FakeClock(); var mounted = false; var count = 0;
        var coordinator = new PresentationUpdateCoordinator(clock);
        using var view = coordinator.Register(() => count++, () => mounted);
        coordinator.SetWindowState(true, false, false);
        Assert.AreEqual(TimeSpan.FromSeconds(1d / 30), coordinator.Interval);
        Assert.AreEqual(0, count);
        mounted = true; coordinator.RefreshImmediately(); Assert.AreEqual(1, count);
        view.Dispose(); clock.Advance(40); coordinator.Tick(); Assert.AreEqual(1, count);
    }
    [TestMethod]
    public void Producers_KeepOnlyLatestSnapshotWhileHidden()
    {
        var latest = new LatestSnapshot<string>(); var displayed = string.Empty;
        var coordinator = new PresentationUpdateCoordinator(new FakeClock());
        using var view = coordinator.Register(() => displayed = latest.Value!);
        coordinator.SetWindowState(false, false, true);
        for (var i = 0; i < 100000; i++) latest.Set(i.ToString());
        Assert.AreEqual(string.Empty, displayed);
        coordinator.SetWindowState(true, true, true);
        Assert.AreEqual("99999", displayed);
    }
}
