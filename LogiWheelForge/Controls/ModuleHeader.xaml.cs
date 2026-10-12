using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace LogiWheelForge.Controls;

public partial class ModuleHeader : UserControl
{
    public static readonly DependencyProperty ModuleNameProperty = DependencyProperty.Register(nameof(ModuleName), typeof(string), typeof(ModuleHeader), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(ModuleHeader), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty RuntimeStatusProperty = DependencyProperty.Register(nameof(RuntimeStatus), typeof(string), typeof(ModuleHeader), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(ModuleHeader), new PropertyMetadata("◉"));
    public static readonly DependencyProperty SwitchNameProperty = DependencyProperty.Register(nameof(SwitchName), typeof(string), typeof(ModuleHeader), new PropertyMetadata("Module enabled"));
    public static readonly DependencyProperty ModuleEnabledProperty = DependencyProperty.Register(nameof(ModuleEnabled), typeof(bool), typeof(ModuleHeader), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public ModuleHeader() => InitializeComponent();
    public string ModuleName { get => (string)GetValue(ModuleNameProperty); set => SetValue(ModuleNameProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string RuntimeStatus { get => (string)GetValue(RuntimeStatusProperty); set => SetValue(RuntimeStatusProperty, value); }
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string SwitchName { get => (string)GetValue(SwitchNameProperty); set => SetValue(SwitchNameProperty, value); }
    public bool ModuleEnabled { get => (bool)GetValue(ModuleEnabledProperty); set => SetValue(ModuleEnabledProperty, value); }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 560;
        SwitchColumn.Width = narrow ? new GridLength(0) : GridLength.Auto;
        Grid.SetColumn(ModuleSwitch, narrow ? 0 : 1); Grid.SetRow(ModuleSwitch, narrow ? 1 : 0);
        ModuleSwitch.Margin = narrow ? new Thickness(44, 14, 0, 0) : new Thickness(16, 0, 0, 0);
    }
}
