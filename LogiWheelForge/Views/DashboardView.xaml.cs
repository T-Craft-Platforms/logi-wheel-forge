using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LogiWheelForge.Models;
using LogiWheelForge.ViewModels;
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using Point = System.Windows.Point;

namespace LogiWheelForge.Views;

public partial class DashboardView : UserControl
{
    private Point _origin;
    private Vector _delta;
    private Thumb? _dragHandle;
    private DashboardViewModel? Model => DataContext as DashboardViewModel;
    public DashboardView()
    {
        InitializeComponent();
        Unloaded += (_, _) => CancelDrag();
        DataContextChanged += (_, _) => Model?.Board.SetWidth(BoardSurface.ActualWidth);
    }
    private void Board_OnSizeChanged(object sender, SizeChangedEventArgs e) => Model?.Board.SetWidth(e.NewSize.Width);
    private void AddWidget_OnClick(object sender, RoutedEventArgs e)
    {
        if (Model is not { } model || sender is not Button button) return;
        model.Board.IsEditing = true;
        var menu = new System.Windows.Controls.ContextMenu();
        foreach (var kind in model.Board.Kinds)
        {
            var item = new MenuItem { Header = kind switch { DashboardWidgetKind.Wheel => "Detected wheel", DashboardWidgetKind.Telemetry => "Telemetry & current vehicle",
                DashboardWidgetKind.Rpm => "RPM & LED strip", DashboardWidgetKind.Mapper => "Input Mapper status", DashboardWidgetKind.Inputs => "Live wheel inputs",
                DashboardWidgetKind.Mappings => "Live mappings", _ => "Wheel resistance" }, IsEnabled = model.Board.Widgets.All(w => w.Kind != kind) };
            item.Click += (_, _) => model.Board.Add(kind);
            menu.Items.Add(item);
        }
        button.ContextMenu = menu; menu.PlacementTarget = button; menu.IsOpen = true;
    }
    private void Drag_OnStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is not Thumb { DataContext: DashboardWidgetViewModel widget } thumb) return;
        _dragHandle = thumb; _origin = new Point(widget.Left, widget.Top); _delta = new Vector();
        widget.Board.BeginDrag(widget.Kind);
    }
    private void Drag_OnDelta(object sender, DragDeltaEventArgs e)
    {
        _delta += new Vector(e.HorizontalChange, e.VerticalChange);
        Model?.Board.DragTo(_origin.X + _delta.X, _origin.Y + _delta.Y);
    }
    private void Drag_OnCompleted(object sender, DragCompletedEventArgs e)
    {
        _dragHandle = null;
        if (e.Canceled) Model?.Board.CancelDrag(); else Model?.Board.Drop();
    }
    private void CancelDrag()
    {
        Model?.Board.CancelDrag();
        var handle = _dragHandle; _dragHandle = null; handle?.CancelDrag();
    }
    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Model?.Board.IsDragging == true) { CancelDrag(); e.Handled = true; }
    }
}
