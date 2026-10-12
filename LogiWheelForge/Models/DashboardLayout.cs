namespace LogiWheelForge.Models;

public enum DashboardWidgetKind { Wheel, Telemetry, Rpm, Mapper, Inputs, Mappings, Resistance }
public enum DashboardWidgetSize { Small, Medium, Large }
public sealed record WidgetPlacement(DashboardWidgetKind Kind, DashboardWidgetSize Size, int Column, int Row)
{
    public int Width(int columns) => Math.Min(columns, Size == DashboardWidgetSize.Small ? 1 : 2);
    public int Height => Size == DashboardWidgetSize.Large ? 2 : 1;
}
public sealed record DashboardLayout
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public Dictionary<int, WidgetPlacement[]> Breakpoints { get; init; } = [];
}
