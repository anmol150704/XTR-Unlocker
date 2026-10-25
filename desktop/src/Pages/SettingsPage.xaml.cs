using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace DLSS_Swapper.Pages;

/// <summary>
/// Page for application settings.
/// </summary>
public sealed partial class SettingsPage : Page
{
    public static string PageTag { get; } = "PageTag_Settings";

    public SettingsPageModel ViewModel { get; private set; }

    public SettingsPage()
    {
        this.InitializeComponent();
        ViewModel = new SettingsPageModel(this);
        DataContext = ViewModel;
    }

    private void SettingsLayoutGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 760;
        SettingsLayoutGrid.ColumnDefinitions[1].MinWidth = compact ? 0 : 280;
        SettingsLayoutGrid.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(SettingsRightColumn, compact ? 0 : 1);
        Grid.SetRow(SettingsRightColumn, compact ? 1 : 0);
    }
}
