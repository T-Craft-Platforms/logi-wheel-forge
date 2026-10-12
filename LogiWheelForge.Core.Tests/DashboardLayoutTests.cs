using LogiWheelForge.Models;
using LogiWheelForge.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogiWheelForge.Core.Tests;

[TestClass]
public sealed class DashboardLayoutTests
{
    [DataTestMethod]
    [DataRow(439d, 1)][DataRow(440d, 2)][DataRow(879d, 2)][DataRow(880d, 4)]
    public void Columns_UseAvailableBoardWidth(double width, int columns) => Assert.AreEqual(columns, DashboardLayoutEngine.Columns(width));

    [DataTestMethod]
    [DataRow(1)][DataRow(2)][DataRow(4)]
    public void Defaults_AreSixUniqueNonOverlappingWidgets(int columns)
    {
        var widgets = DashboardLayoutEngine.Defaults(columns);
        Assert.AreEqual(6, widgets.Length);
        Assert.IsFalse(widgets.Any(w => w.Kind == DashboardWidgetKind.Resistance));
        DashboardLayoutEngine.Validate(new() { Breakpoints = new() { [columns] = widgets } });
        Assert.AreEqual(180d, DashboardLayoutEngine.RowHeight);
        Assert.AreEqual(16d, DashboardLayoutEngine.Gap);
    }

    [TestMethod]
    public void Collision_PushesIntersectingWidgetsDownAndPreservesGaps()
    {
        WidgetPlacement[] source = [new(DashboardWidgetKind.Wheel, DashboardWidgetSize.Small, 0, 0),
            new(DashboardWidgetKind.Telemetry, DashboardWidgetSize.Small, 0, 1),
            new(DashboardWidgetKind.Rpm, DashboardWidgetSize.Small, 0, 2),
            new(DashboardWidgetKind.Mapper, DashboardWidgetSize.Small, 1, 8)];
        var moved = source[0] with { Row = 1 };
        var result = DashboardLayoutEngine.Arrange(source, 2, moved);
        Assert.AreEqual(1, result.Single(w => w.Kind == DashboardWidgetKind.Wheel).Row);
        Assert.AreEqual(2, result.Single(w => w.Kind == DashboardWidgetKind.Telemetry).Row);
        Assert.AreEqual(3, result.Single(w => w.Kind == DashboardWidgetKind.Rpm).Row);
        Assert.AreEqual(8, result.Single(w => w.Kind == DashboardWidgetKind.Mapper).Row);
        CollectionAssert.AreEqual(result, DashboardLayoutEngine.Arrange(source.Reverse(), 2, moved));
        Assert.AreEqual(0, source[0].Row, "Preview geometry must not mutate the persisted source.");
    }

    [TestMethod]
    public void Presets_ClampWidthAndRetainLargeHeight()
    {
        var item = new WidgetPlacement(DashboardWidgetKind.Mappings, DashboardWidgetSize.Large, 3, 4);
        var result = DashboardLayoutEngine.Arrange([item], 1).Single();
        Assert.AreEqual(0, result.Column); Assert.AreEqual(1, result.Width(1)); Assert.AreEqual(2, result.Height);
        Assert.AreEqual(2, item.Width(4)); Assert.AreEqual(4, result.Row);
    }

    [TestMethod]
    public async Task Store_RoundTripsIndependentBreakpointMembershipAndGaps()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DashboardLayoutStore(Path.Combine(directory, "dashboard-layout.json"));
            var layout = new DashboardLayout { Breakpoints = new() { [4] = DashboardLayoutEngine.Defaults(4),
                [2] = [new(DashboardWidgetKind.Resistance, DashboardWidgetSize.Small, 1, 7)], [1] = [] } };
            await store.SaveAsync(layout);
            var loaded = await store.LoadAsync();
            Assert.AreEqual(DashboardLayout.CurrentVersion, loaded.Version);
            CollectionAssert.AreEqual(layout.Breakpoints[4], loaded.Breakpoints[4]);
            CollectionAssert.AreEqual(layout.Breakpoints[2], loaded.Breakpoints[2]);
            Assert.AreEqual(0, loaded.Breakpoints[1].Length);
            Assert.AreEqual(1, Directory.GetFiles(directory).Length, "Successful atomic saves must clean temporary files.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task InvalidSave_LeavesLastValidLayoutUntouched()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "dashboard-layout.json"); var store = new DashboardLayoutStore(path);
            await store.SaveAsync(new() { Breakpoints = new() { [4] = DashboardLayoutEngine.Defaults(4) } });
            var before = await File.ReadAllTextAsync(path);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => store.SaveAsync(new() { Version = 99 }));
            Assert.AreEqual(before, await File.ReadAllTextAsync(path));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
