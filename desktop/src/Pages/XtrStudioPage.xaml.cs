using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DLSS_Swapper.Pages;

public sealed partial class XtrStudioPage : Page
{
    public static string PageTag { get; } = "PageTag_XtrStudio";

    public XtrStudioPageModel ViewModel { get; private set; }

    public XtrStudioPage()
    {
        this.InitializeComponent();
        ViewModel = new XtrStudioPageModel(this);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel?.RefreshLibraryStats();
    }

    private void Hotkey_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel?.OnSettingChanged();
    }
}
