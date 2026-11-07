using AulaKb.App.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace AulaKb.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop(); // XAML'da this Release'de parse edilmüyor

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1240, 860));

        NavDevice.Content = Loc.S("nav.device");
        NavLighting.Content = Loc.S("nav.lighting");
        NavKeys.Content = Loc.S("nav.keys");
        NavMacros.Content = Loc.S("nav.macros");
        NavMusic.Content = Loc.S("nav.music");

        Nav.SelectedItem = Nav.MenuItems[0];
        Closed += (_, _) => KeyboardService.Shutdown();

        if (Environment.GetCommandLineArgs().Contains("--selftest"))
            RunSelfTest();
    }

    /// <summary>Opens every page in turn (with --selftest) so XAML or start-up errors show up in crash.log.</summary>
    void RunSelfTest()
    {
        var pages = Nav.MenuItems.Cast<object>().Append(Nav.SettingsItem).ToList();
        int index = 0;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(2500);
        timer.Tick += (_, _) =>
        {
            if (index < pages.Count)
            {
                Nav.SelectedItem = pages[index++];
                return;
            }
            timer.Stop();
            string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AulaKb", "selftest.log");
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            string sample;
            try
            {
                sample = Loc.S("nav.keys");
            }
            catch (Exception ex)
            {
                sample = $"Loc error: {ex.Message}";
            }
            var dbgSettings = Controls.AppSettings.Load();
            File.WriteAllText(log, $"selftest ok: {pages.Count} pages · Loc[nav.keys]='{sample}' · lang={(Loc.IsEnglish ? "en" : "tr")} · settings.Language='{dbgSettings.Language ?? "null"}' · override='{Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride}' at {DateTime.Now:HH:mm:ss}\n");
            Close();
        };
        timer.Start();
    }

    void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Type page = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : (args.SelectedItemContainer?.Tag as string) switch
            {
                "lighting" => typeof(LightingPage),
                "keys" => typeof(KeysPage),
                "macro" => typeof(MacrosPage),
                "music" => typeof(MusicPage),
                _ => typeof(DevicePage),
            };
        ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
    }
}
