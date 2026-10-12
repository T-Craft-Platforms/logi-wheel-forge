using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LogiWheelForge.Services;
using FrameworkElement = System.Windows.FrameworkElement;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace LogiWheelForge.Controls;

/// <summary>A vector live diagram, redrawn only by a mounted presentation refresh.</summary>
public sealed class WheelInputDiagram : FrameworkElement
{
    public static readonly DependencyProperty InputsProperty = DependencyProperty.Register(nameof(Inputs),
        typeof(IReadOnlyList<WheelInputSample>), typeof(WheelInputDiagram),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<WheelInputSample>? Inputs { get => (IReadOnlyList<WheelInputSample>?)GetValue(InputsProperty); set => SetValue(InputsProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var inputs = Inputs ?? [];
        var accent = (Brush)FindResource("AccentBrush"); var text = (Brush)FindResource("TextBrush");
        var muted = (Brush)FindResource("MutedBrush"); var surface = (Brush)FindResource("ElevatedBrush");
        var steering = inputs.FirstOrDefault(i => i.Control.Equals("Steering", StringComparison.OrdinalIgnoreCase))?.Percent ?? 0;
        var radius = Math.Max(16, Math.Min(32, ActualWidth / 5));
        var center = new Point(radius + 4, radius + 4);
        dc.DrawEllipse(surface, new Pen(accent, 3), center, radius, radius);
        dc.PushTransform(new RotateTransform(steering * 1.35, center.X, center.Y));
        dc.DrawLine(new Pen(accent, 3), center, new Point(center.X, center.Y - radius + 6));
        dc.DrawLine(new Pen(accent, 3), center, new Point(center.X - radius + 6, center.Y + radius / 3));
        dc.DrawLine(new Pen(accent, 3), center, new Point(center.X + radius - 6, center.Y + radius / 3));
        dc.Pop();
        DrawText(dc, $"{steering:0.#}%", new Point(4, radius * 2 + 14), muted, 11);
        var x = radius * 2 + 24;
        var barWidth = Math.Max(12, ActualWidth - x - 8);
        var line = 0;
        foreach (var name in new[] { "Accelerator", "Brake", "Clutch" })
        {
            var percent = inputs.FirstOrDefault(i => i.Control.Equals(name, StringComparison.OrdinalIgnoreCase))?.Percent ?? 0;
            var y = line++ * 28d;
            DrawText(dc, $"{(name == "Accelerator" ? "Gas" : name)}  {percent:0}%", new Point(x, y), text, 11, barWidth);
            dc.DrawRoundedRectangle(surface, null, new Rect(x, y + 19, barWidth, 5), 2, 2);
            dc.DrawRoundedRectangle(accent, null, new Rect(x, y + 19, barWidth * Math.Clamp(percent / 100, 0, 1), 5), 2, 2);
        }
        var buttons = inputs.Where(i => i.IsButton && i.Percent > 0).Select(i => i.Control).ToArray();
        var label = buttons.Length == 0 ? "Buttons · none pressed" : string.Join(" · ", buttons);
        if (ActualHeight >= 110) DrawText(dc, label, new Point(4, 96), accent, 11, Math.Max(10, ActualWidth - 8));
        System.Windows.Automation.AutomationProperties.SetName(this,
            $"Wheel inputs. Steering {steering:0} percent. " + string.Join(". ", inputs.Where(i => !i.IsButton).Select(i => $"{i.Control} {i.Percent:0} percent")) + ". " + label);
    }
    private void DrawText(DrawingContext dc, string value, Point point, Brush brush, double size, double maxWidth = 1000)
    {
        var formatted = new FormattedText(value, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = maxWidth, Trimming = TextTrimming.CharacterEllipsis };
        dc.DrawText(formatted, point);
    }
}
