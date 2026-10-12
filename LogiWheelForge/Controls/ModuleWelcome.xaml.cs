using System.Windows;
using UserControl = System.Windows.Controls.UserControl;

namespace LogiWheelForge.Controls;

public partial class ModuleWelcome : UserControl
{
    public static readonly DependencyProperty ModuleNameProperty = DependencyProperty.Register(nameof(ModuleName), typeof(string), typeof(ModuleWelcome), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(ModuleWelcome), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(ModuleWelcome), new PropertyMetadata("◉"));
    public static readonly DependencyProperty EnableNameProperty = DependencyProperty.Register(nameof(EnableName), typeof(string), typeof(ModuleWelcome), new PropertyMetadata("Enable module"));
    public static readonly DependencyProperty ModuleEnabledProperty = DependencyProperty.Register(nameof(ModuleEnabled), typeof(bool), typeof(ModuleWelcome), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public ModuleWelcome() => InitializeComponent();
    public string ModuleName { get => (string)GetValue(ModuleNameProperty); set => SetValue(ModuleNameProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string EnableName { get => (string)GetValue(EnableNameProperty); set => SetValue(EnableNameProperty, value); }
    public bool ModuleEnabled { get => (bool)GetValue(ModuleEnabledProperty); set => SetValue(ModuleEnabledProperty, value); }
    private void Enable_OnClick(object sender, RoutedEventArgs e) => SetCurrentValue(ModuleEnabledProperty, true);
}
