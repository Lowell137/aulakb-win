using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AulaKb.App.Pages;

public sealed partial class DevicePage : Page
{
    public DevicePage()
    {
        InitializeComponent();
        TitleText.Text = Loc.S("device.title");
        ProductText.Text = Loc.S("device.searching");
        RescanText.Text = Loc.S("device.rescan");
        DetailsExpander.Header = Loc.S("device.details");
        VidPidLabel.Text = Loc.S("device.vidpid");
        IdentityLabel.Text = Loc.S("device.identity");
        PathLabel.Text = Loc.S("device.hidpath");
        Loaded += async (_, _) => await RefreshAsync(reconnect: false);
    }

    async Task RefreshAsync(bool reconnect)
    {
        Status.Busy(Loc.S("device.scan"));
        if (reconnect)
            KeyboardService.Disconnect();
        try
        {
            var (identity, info, firmware) = await KeyboardService.RunAsync(kb => (kb.QueryIdentity(), kb.Info, kb.FirmwareVersion));
            ProductText.Text = identity.Name;
            SubText.Text = Loc.F("device.wired_firmware", firmware);
            VidPidText.Text = $"{info.VendorId:X4}:{info.ProductId:X4}";
            RawText.Text = identity.RawHex;
            PathText.Text = info.Path;
            DetailsExpander.Visibility = Visibility.Visible;
            Status.Ok(Loc.S("device.connected"));
        }
        catch (Exception ex)
        {
            ProductText.Text = Loc.S("device.notfound");
            SubText.Text = Loc.S("device.notfound_hint");
            DetailsExpander.Visibility = Visibility.Collapsed;
            Status.Error(ex.Message);
        }
    }

    async void Rescan_Click(object sender, RoutedEventArgs e) => await RefreshAsync(reconnect: true);
}
