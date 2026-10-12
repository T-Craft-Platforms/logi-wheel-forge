using LogiWheelForge.Models;
using LogiWheelForge.Services;
using LogiWheelForge.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogiWheelForge.Tests;

[TestClass]
public sealed class DashboardBoardTests
{
    [TestMethod]
    public async Task Board_DragCancelDropPresetsMembershipBreakpointsAndReset()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var led = new LedIndicatorService(new Receiver(), new Led(), new SettingsStore(Path.Combine(directory, "settings.json")),
            redlineLearner: new SmartRedlineLearner(Path.Combine(directory, "calibrations.json")));
        using var mapper = new InputMapperService([], new(Path.Combine(directory, "mapper.json")), new InertWheelResistance(), new InertMapperOutput());
        using var selection = new WheelSelectionService([], () => []);
        using var dashboard = new DashboardViewModel(led, mapper, selection);
        using var board = new DashboardBoardViewModel(dashboard, new(Path.Combine(directory, "dashboard-layout.json")));
        try
        {
            await board.InitializeAsync(); board.SetWidth(900); board.IsEditing = true;
            var before = board.Widgets.Select(w => w.Placement).ToArray();
            board.BeginDrag(DashboardWidgetKind.Wheel); board.DragTo(400, 600); Assert.IsTrue(board.IsDragging);
            CollectionAssert.AreEqual(before, board.Widgets.Select(w => w.Placement).ToArray());
            board.CancelDrag(); Assert.IsFalse(board.IsDragging);
            CollectionAssert.AreEqual(before, board.Widgets.Select(w => w.Placement).ToArray());
            board.BeginDrag(DashboardWidgetKind.Wheel); board.DragTo(400, 600); board.Drop();
            Assert.IsFalse(board.IsDragging); Assert.AreEqual(3, board.Widgets.Single(w => w.Kind == DashboardWidgetKind.Wheel).Placement.Row);
            board.Resize(DashboardWidgetKind.Wheel, DashboardWidgetSize.Large);
            Assert.AreEqual(376d, board.Widgets.Single(w => w.Kind == DashboardWidgetKind.Wheel).Height);
            board.Add(DashboardWidgetKind.Resistance); board.Add(DashboardWidgetKind.Resistance); Assert.AreEqual(7, board.Widgets.Count);
            var wide = board.Widgets.Select(w => w.Placement).ToArray();
            board.SetWidth(500); board.Remove(DashboardWidgetKind.Resistance); var medium = board.Widgets.Select(w => w.Placement).ToArray();
            board.SetWidth(300); foreach (var widget in board.Widgets.ToArray()) board.Remove(widget.Kind); Assert.IsTrue(board.IsEmpty);
            board.SetWidth(900); CollectionAssert.AreEquivalent(wide, board.Widgets.Select(w => w.Placement).ToArray());
            board.SetWidth(500); CollectionAssert.AreEquivalent(medium, board.Widgets.Select(w => w.Placement).ToArray());
            await board.FlushAsync();
            var saved = await new DashboardLayoutStore(Path.Combine(directory, "dashboard-layout.json")).LoadAsync();
            Assert.AreEqual(0, saved.Breakpoints[1].Length); Assert.AreEqual(6, saved.Breakpoints[2].Length); Assert.AreEqual(7, saved.Breakpoints[4].Length);
            board.Reset(); await board.FlushAsync();
            saved = await new DashboardLayoutStore(Path.Combine(directory, "dashboard-layout.json")).LoadAsync();
            foreach (var columns in new[] { 1, 2, 4 }) CollectionAssert.AreEqual(DashboardLayoutEngine.Defaults(columns), saved.Breakpoints[columns]);
        }
        finally { await led.DisposeAsync(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class Receiver : ITelemetryReceiver
    {
        public bool IsRunning => false;
        public event Action<TelemetryFrame>? FrameReceived { add { } remove { } }
        public event Action<string>? ErrorOccurred { add { } remove { } }
        public Task StartAsync(LedProfileSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Led : IWheelLedController
    {
        public bool IsConnected => false;
        public string WheelName => "No wheel detected";
        public string StatusMessage => "Disconnected";
        public WheelDefinition? CurrentDefinition => null;
        public IReadOnlyList<WheelDefinition> AvailableDefinitions => [];
        public IReadOnlyList<string> DefinitionDiagnostics => [];
        public bool Initialize(nint handle) => true;
        public void SetPreferredWheel(string? instanceId) { }
        public void Refresh() { }
        public void RefreshNow() { }
        public bool SetLevel(int groups) => true;
        public void ClearLeds() { }
        public Task TestLedsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PlayReadyAnimationAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Shutdown() { }
        public void Dispose() { }
    }
}
