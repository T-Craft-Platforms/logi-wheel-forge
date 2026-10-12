using LogiWheelForge.Models;

namespace LogiWheelForge.Services;

/// <summary>Cell geometry independent of WPF. Only collisions move widgets; gaps remain intentional.</summary>
public static class DashboardLayoutEngine
{
    public const double Gap = 16, RowHeight = 180;
    public static int Columns(double width) => width >= 880 ? 4 : width >= 440 ? 2 : 1;
    public static WidgetPlacement[] Defaults(int columns) => Arrange([
        new(DashboardWidgetKind.Wheel, DashboardWidgetSize.Small, 0, 0),
        new(DashboardWidgetKind.Telemetry, DashboardWidgetSize.Small, 1, 0),
        new(DashboardWidgetKind.Rpm, DashboardWidgetSize.Medium, 2, 0),
        new(DashboardWidgetKind.Mapper, DashboardWidgetSize.Medium, 0, 1),
        new(DashboardWidgetKind.Inputs, DashboardWidgetSize.Medium, 2, 1),
        new(DashboardWidgetKind.Mappings, DashboardWidgetSize.Large, 0, 2)
    ], columns);

    public static WidgetPlacement[] Arrange(IEnumerable<WidgetPlacement> source, int columns, WidgetPlacement? moved = null)
    {
        if (columns is not (1 or 2 or 4)) throw new ArgumentOutOfRangeException(nameof(columns));
        var placements = new List<WidgetPlacement>();
        if (moved is not null) placements.Add(Clamp(moved, columns));
        foreach (var item in source.Where(p => p.Kind != moved?.Kind).OrderBy(p => p.Row).ThenBy(p => p.Column).ThenBy(p => p.Kind))
        {
            var candidate = Clamp(item, columns);
            while (placements.Where(p => Intersects(candidate, p, columns)).ToArray() is { Length: > 0 } collisions)
                candidate = candidate with { Row = collisions.Max(p => p.Row + p.Height) };
            placements.Add(candidate);
        }
        return placements.OrderBy(p => p.Row).ThenBy(p => p.Column).ThenBy(p => p.Kind).ToArray();
    }
    private static WidgetPlacement Clamp(WidgetPlacement p, int columns) => p with
    { Column = Math.Clamp(p.Column, 0, columns - p.Width(columns)), Row = Math.Clamp(p.Row, 0, 10000) };
    public static bool Intersects(WidgetPlacement a, WidgetPlacement b, int columns) =>
        a.Column < b.Column + b.Width(columns) && a.Column + a.Width(columns) > b.Column &&
        a.Row < b.Row + b.Height && a.Row + a.Height > b.Row;

    public static void Validate(DashboardLayout layout)
    {
        if (layout.Version != DashboardLayout.CurrentVersion) throw new ArgumentException("Unsupported dashboard layout version.");
        foreach (var (columns, widgets) in layout.Breakpoints)
        {
            if (columns is not (1 or 2 or 4) || widgets is null || widgets.Length > 7 ||
                widgets.Select(w => w.Kind).Distinct().Count() != widgets.Length)
                throw new ArgumentException("Invalid dashboard breakpoint or widget membership.");
            foreach (var widget in widgets)
                if (!Enum.IsDefined(widget.Kind) || !Enum.IsDefined(widget.Size) || widget.Row is < 0 or > 10000 ||
                    widget.Column < 0 || widget.Column + widget.Width(columns) > columns ||
                    widgets.Any(other => other.Kind != widget.Kind && Intersects(widget, other, columns)))
                    throw new ArgumentException("Invalid or overlapping dashboard placement.");
        }
    }
}
