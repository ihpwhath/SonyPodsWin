using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SonyPodsWin.Assets;

namespace SonyPodsWin.Views;

public sealed partial class AboutPage : Page
{
    private bool _qrLoaded;

    public AboutPage()
    {
        InitializeComponent();
        ScrollViewerWheelHelper.Attach(PageScrollViewer);
    }

    private async void OnZanshangExpanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        if (_qrLoaded)
        {
            return;
        }

        _qrLoaded = true;
        BitmapImage? image = await ZanshangAsset.LoadBitmapImageAsync();
        if (image is not null)
        {
            ZanshangQrImage.Source = image;
        }
    }
}
