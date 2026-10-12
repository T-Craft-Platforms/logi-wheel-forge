using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace LogiWheelForge.Controls;

public partial class MappingsEditor : UserControl
{
    public MappingsEditor() => InitializeComponent();
    private void OnEditorSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wide = e.NewSize.Width >= 860;
        ListColumn.Width = wide ? new GridLength(280) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(MappingDetails, wide ? 1 : 0); Grid.SetRow(MappingDetails, wide ? 0 : 1);
        MappingDetails.Margin = wide ? new Thickness(0) : new Thickness(0, 16, 0, 0);
        MappingList.Margin = wide ? new Thickness(0, 0, 16, 0) : new Thickness(0);
    }
}
