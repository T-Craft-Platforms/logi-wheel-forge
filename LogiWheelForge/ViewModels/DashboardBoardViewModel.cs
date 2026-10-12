using System.Collections.ObjectModel;
using System.Windows.Input;
using LogiWheelForge.Commands;
using LogiWheelForge.Models;
using LogiWheelForge.Services;

namespace LogiWheelForge.ViewModels;

public sealed class DashboardWidgetViewModel : ObservableObject
{
    private WidgetPlacement _placement;
    private double _cellWidth;
    private int _columns;
    private DashboardWidgetSize _size;
    public DashboardWidgetViewModel(WidgetPlacement placement, DashboardBoardViewModel board, DashboardViewModel dashboard)
    {
        _placement = placement; _size = placement.Size; Board = board; Dashboard = dashboard;
        RemoveCommand = new RelayCommand(() => board.Remove(Kind));
        MoveLeftCommand = new RelayCommand(() => board.Move(Kind, -1, 0));
        MoveRightCommand = new RelayCommand(() => board.Move(Kind, 1, 0));
        MoveUpCommand = new RelayCommand(() => board.Move(Kind, 0, -1));
        MoveDownCommand = new RelayCommand(() => board.Move(Kind, 0, 1));
    }
    public DashboardBoardViewModel Board { get; }
    public DashboardViewModel Dashboard { get; }
    public DashboardWidgetKind Kind => _placement.Kind;
    public WidgetPlacement Placement => _placement;
    public string Title => Kind switch { DashboardWidgetKind.Wheel => "Detected wheel", DashboardWidgetKind.Telemetry => "Telemetry & vehicle",
        DashboardWidgetKind.Rpm => "RPM & lights", DashboardWidgetKind.Mapper => "Input Mapper", DashboardWidgetKind.Inputs => "Wheel inputs",
        DashboardWidgetKind.Mappings => "Live mappings", _ => "Wheel resistance" };
    public DashboardWidgetSize Size { get => _size; set { if (SetField(ref _size, value)) Board.Resize(Kind, value); } }
    public IReadOnlyList<DashboardWidgetSize> Sizes { get; } = Enum.GetValues<DashboardWidgetSize>();
    public double Left => _placement.Column * (_cellWidth + DashboardLayoutEngine.Gap);
    public double Top => _placement.Row * (DashboardLayoutEngine.RowHeight + DashboardLayoutEngine.Gap);
    public double Width => _placement.Width(_columns) * (_cellWidth + DashboardLayoutEngine.Gap) - DashboardLayoutEngine.Gap;
    public double Height => _placement.Height * (DashboardLayoutEngine.RowHeight + DashboardLayoutEngine.Gap) - DashboardLayoutEngine.Gap;
    public ICommand RemoveCommand { get; }
    public ICommand MoveLeftCommand { get; }
    public ICommand MoveRightCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    internal void Update(WidgetPlacement placement, double cellWidth, int columns)
    {
        _placement = placement; _cellWidth = cellWidth; _columns = columns; _size = placement.Size;
        OnPropertyChanged(string.Empty);
    }
}

public sealed class DashboardBoardViewModel : ObservableObject, IDisposable
{
    private readonly DashboardViewModel _dashboard;
    private readonly DashboardLayoutStore _store;
    private readonly RevisionedAutosave<DashboardLayout> _save;
    private DashboardLayout _layout = new();
    private bool _editing, _initialized;
    private int _columns = 4;
    private double _width = 880;
    private WidgetPlacement? _drag;
    private double _pointerLeft, _pointerTop;
    public DashboardBoardViewModel(DashboardViewModel dashboard, DashboardLayoutStore? store = null)
    {
        _dashboard = dashboard; _store = store ?? new();
        _save = new(() => _layout with { Breakpoints = _layout.Breakpoints.ToDictionary(p => p.Key, p => p.Value.ToArray()) },
            DashboardLayoutEngine.Validate, _store.SaveAsync);
        _save.Changed += (_, _) => OnPropertyChanged(nameof(SaveError));
        EditCommand = new RelayCommand(() => IsEditing = !IsEditing);
        ResetCommand = new RelayCommand(Reset);
        Rebuild();
    }
    public ObservableCollection<DashboardWidgetViewModel> Widgets { get; } = [];
    public IReadOnlyList<DashboardWidgetKind> Kinds { get; } = Enum.GetValues<DashboardWidgetKind>();
    public bool IsEditing { get => _editing; set { if (SetField(ref _editing, value)) { CancelDrag(); OnPropertyChanged(nameof(EditText)); } } }
    public string EditText => IsEditing ? "Done" : "Edit dashboard";
    public bool IsEmpty => Widgets.Count == 0;
    public string? SaveError => _save.Error;
    public int Columns => _columns;
    public double BoardHeight => Widgets.Count == 0 ? 280 : Widgets.Max(w => w.Top + w.Height);
    public double PreviewLeft => (_drag?.Column ?? 0) * (CellWidth + DashboardLayoutEngine.Gap);
    public double PreviewTop => (_drag?.Row ?? 0) * (DashboardLayoutEngine.RowHeight + DashboardLayoutEngine.Gap);
    public double PreviewWidth => (_drag?.Width(_columns) ?? 1) * (CellWidth + DashboardLayoutEngine.Gap) - DashboardLayoutEngine.Gap;
    public double PreviewHeight => (_drag?.Height ?? 1) * (DashboardLayoutEngine.RowHeight + DashboardLayoutEngine.Gap) - DashboardLayoutEngine.Gap;
    public double PointerLeft => _pointerLeft;
    public double PointerTop => _pointerTop;
    public string DragTitle => Widgets.FirstOrDefault(w => w.Kind == _drag?.Kind)?.Title ?? string.Empty;
    public string DragSummary => _drag?.Kind switch
    {
        DashboardWidgetKind.Wheel => _dashboard.WheelName,
        DashboardWidgetKind.Telemetry => _dashboard.CurrentVehicle,
        DashboardWidgetKind.Rpm => _dashboard.RpmDisplay + " RPM",
        DashboardWidgetKind.Mapper => _dashboard.MapperStatus,
        DashboardWidgetKind.Resistance => _dashboard.ResistanceStatus,
        DashboardWidgetKind.Inputs => "Steering · pedals · buttons",
        _ => "Control → trigger → action"
    };
    public bool IsDragging => _drag is not null;
    public ICommand EditCommand { get; }
    public ICommand ResetCommand { get; }
    private double CellWidth => Math.Max(0, (_width - (_columns - 1) * DashboardLayoutEngine.Gap) / _columns);
    private WidgetPlacement[] Current
    {
        get
        {
            if (!_layout.Breakpoints.TryGetValue(_columns, out var widgets))
            {
                var source = _layout.Breakpoints.OrderBy(p => Math.Abs(p.Key - _columns)).Select(p => p.Value).FirstOrDefault();
                _layout.Breakpoints[_columns] = widgets = source is null ? DashboardLayoutEngine.Defaults(_columns) : DashboardLayoutEngine.Arrange(source, _columns);
            }
            return widgets;
        }
    }
    public async Task InitializeAsync()
    {
        try { _layout = await _store.LoadAsync(); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { _layout = new(); LoadError = $"Layout could not be loaded: {ex.Message}"; OnPropertyChanged(nameof(LoadError)); }
        _initialized = true; Rebuild();
        if (LoadError is null) _save.Edit();
    }
    public string? LoadError { get; private set; }
    public void SetWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0) return;
        var columns = DashboardLayoutEngine.Columns(width);
        _width = width;
        if (columns != _columns) { CancelDrag(); _columns = columns; var firstVisit = !_layout.Breakpoints.ContainsKey(columns); Rebuild(); if (_initialized && firstVisit) _save.Edit(); }
        else UpdateGeometry();
    }
    public void Add(DashboardWidgetKind kind)
    {
        if (Current.Any(p => p.Kind == kind)) return;
        var added = new WidgetPlacement(kind, DashboardWidgetSize.Small, 0, Current.Length == 0 ? 0 : Current.Max(p => p.Row + p.Height));
        Commit(Current.Append(added).ToArray());
    }
    public void Remove(DashboardWidgetKind kind) => Commit(Current.Where(p => p.Kind != kind).ToArray());
    public void Resize(DashboardWidgetKind kind, DashboardWidgetSize size)
    {
        var widget = Current.First(p => p.Kind == kind) with { Size = size };
        Commit(DashboardLayoutEngine.Arrange(Current, _columns, widget));
    }
    public void Move(DashboardWidgetKind kind, int dx, int dy)
    {
        var widget = Current.First(p => p.Kind == kind);
        Commit(DashboardLayoutEngine.Arrange(Current, _columns, widget with { Column = widget.Column + dx, Row = widget.Row + dy }));
    }
    public void BeginDrag(DashboardWidgetKind kind) { if (!IsEditing) return; _drag = Current.First(p => p.Kind == kind); _pointerLeft = PreviewLeft; _pointerTop = PreviewTop; NotifyPreview(); }
    public void DragTo(double x, double y)
    {
        if (_drag is null) return;
        _pointerLeft = Math.Clamp(x, 0, Math.Max(0, _width - PreviewWidth)); _pointerTop = Math.Max(0, y);
        _drag = _drag with { Column = Math.Clamp((int)Math.Round(x / (CellWidth + 16)), 0, _columns - _drag.Width(_columns)),
            Row = Math.Clamp((int)Math.Round(y / 196), 0, 10000) };
        NotifyPreview();
    }
    public void Drop()
    {
        if (_drag is null) return;
        var moved = _drag; CancelDrag(); Commit(DashboardLayoutEngine.Arrange(Current, _columns, moved));
    }
    public void CancelDrag() { _drag = null; NotifyPreview(); }
    private void NotifyPreview()
    {
        foreach (var property in new[] { nameof(IsDragging), nameof(PointerLeft), nameof(PointerTop), nameof(DragTitle), nameof(DragSummary), nameof(PreviewLeft), nameof(PreviewTop), nameof(PreviewWidth), nameof(PreviewHeight) }) OnPropertyChanged(property);
    }
    public void Reset()
    {
        CancelDrag(); _layout = new() { Breakpoints = new[] { 1, 2, 4 }.ToDictionary(c => c, DashboardLayoutEngine.Defaults) };
        Rebuild(); if (_initialized) _save.Edit();
    }
    private void Commit(WidgetPlacement[] widgets)
    {
        CancelDrag(); _layout.Breakpoints[_columns] = widgets; Rebuild(); if (_initialized) _save.Edit();
    }
    private void Rebuild()
    {
        var placements = Current;
        foreach (var obsolete in Widgets.Where(w => placements.All(p => p.Kind != w.Kind)).ToArray()) Widgets.Remove(obsolete);
        foreach (var placement in placements)
        {
            var widget = Widgets.FirstOrDefault(w => w.Kind == placement.Kind);
            if (widget is null) Widgets.Add(widget = new DashboardWidgetViewModel(placement, this, _dashboard));
            widget.Update(placement, CellWidth, _columns);
        }
        OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(BoardHeight));
    }
    private void UpdateGeometry()
    {
        foreach (var widget in Widgets) widget.Update(widget.Placement, CellWidth, _columns);
        OnPropertyChanged(nameof(BoardHeight));
    }
    public Task FlushAsync() => _save.FlushAsync();
    public void Dispose() => _save.Dispose();
}
