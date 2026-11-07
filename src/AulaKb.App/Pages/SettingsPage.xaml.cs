using System.Diagnostics;
using AulaKb.App.Controls;
using AulaKb.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AulaKb.App.Pages;

public sealed partial class SettingsPage : Page
{
    static readonly string BackupRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AulaKb", "Yedekler");

    readonly DispatcherQueueTimer _applyTimer;
    bool _ready;
    bool _langLoading;

    public SettingsPage()
    {
        InitializeComponent();
        // Ranges in code: setting Minimum (>0) during XAML parse threw
        // XamlParseException "Failed to assign to property RangeBase.Minimum" intermittently.
        DebounceSlider.Minimum = 1;
        DebounceSlider.Maximum = 5;
        DebounceSlider.StepFrequency = 1;
        TapSlider.Minimum = 1;
        TapSlider.Maximum = 127;
        TapSlider.StepFrequency = 1;
        TitleText.Text = Loc.S("settings.title");
        SectionView.Text = Loc.S("settings.section_view");
        SectionKeyboard.Text = Loc.S("settings.section_keyboard");
        SectionBackup.Text = Loc.S("settings.section_backup");
        SectionMaintenance.Text = Loc.S("settings.section_maintenance");
        SectionAbout.Text = Loc.S("settings.section_about");
        LanguageRow.Title = Loc.S("settings.language_title");
        LanguageRow.Description = Loc.S("settings.language_desc");
        LayoutRow.Title = Loc.S("settings.layout_title");
        LayoutRow.Description = Loc.S("settings.layout_desc");
        DebounceRow.Title = Loc.S("settings.debounce");
        DebounceRow.Description = Loc.S("settings.debounce_desc");
        TapRow.Title = Loc.S("settings.tap");
        TapRow.Description = Loc.S("settings.tap_desc");
        BackupRow.Title = Loc.S("settings.backup_title");
        BackupRow.Description = Loc.S("settings.backup_desc");
        OpenFolderButton.Content = Loc.S("settings.backup_open");
        BackupButton.Content = Loc.S("settings.backup_btn");
        RestoreRow.Title = Loc.S("settings.restore_title");
        RestoreRow.Description = Loc.S("settings.restore_desc");
        RestoreButton.Content = Loc.S("settings.restore_btn");
        FactoryRow.Title = Loc.S("settings.factory_title");
        FactoryRow.Description = Loc.S("settings.factory_desc");
        FactoryButton.Content = Loc.S("settings.factory_btn");
        AboutRow.Title = Loc.S("settings.about_title");
        AboutRow.Description = Loc.S("settings.about_desc");

        var current = VisualLayout.Current;
        foreach (var (kind, name) in VisualLayout.All)
        {
            var item = new ComboBoxItem { Content = name, Tag = kind };
            LayoutBox.Items.Add(item);
            if (kind == current)
                LayoutBox.SelectedItem = item;
        }

        _applyTimer = DispatcherQueue.CreateTimer();
        _applyTimer.Interval = TimeSpan.FromMilliseconds(600);
        _applyTimer.IsRepeating = false;
        _applyTimer.Tick += async (_, _) => await ApplyAsync();

        Loaded += async (_, _) => await LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (_applyTimer.IsRunning)
        {
            _applyTimer.Stop();
            _ = ApplyAsync();
        }
        base.OnNavigatedFrom(e);
    }

    async Task LoadAsync()
    {
        _langLoading = true;
        var saved = AppSettings.Load().Language;
        LanguageBox.SelectedIndex = saved == Loc.Turkish ? 1 : saved == Loc.English ? 2 : 0;
        BackupBox.PlaceholderText = Loc.S("settings.restore_ph");
        _langLoading = false;
        RefreshBackups();
        _ready = false;
        try
        {
            Status.Busy(Loc.S("common.reading"));
            var (profile, identity, firmware) = await KeyboardService.RunAsync(kb => (kb.ReadProfile(), kb.QueryIdentity(), kb.FirmwareVersion));
            if (!profile.IsValid)
                throw new InvalidDataException(Loc.S("error.profile_signature"));
            DebounceSlider.Value = profile.Debounce + 1;
            DebounceText.Text = (profile.Debounce + 1).ToString();
            TapToggle.IsOn = profile.TapSensitivity > 0;
            TapSlider.Value = profile.TapSensitivity > 0 ? profile.TapSensitivity : 0x7F;
            TapSlider.IsEnabled = TapToggle.IsOn;
            // name and firmware version are data, not UI text
            AboutRow.Title = $"{identity.Name} · firmware {firmware}";
            Status.Ok(Loc.S("common.synced"));
            _ready = true;
        }
        catch (Exception ex)
        {
            Status.Error(ex.Message);
        }
    }

    void LayoutBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LayoutBox.SelectedItem is ComboBoxItem { Tag: LayoutKind kind } && kind != VisualLayout.Current)
        {
            VisualLayout.Current = kind;
            Status.Ok(Loc.S("settings.layout_changed"));
        }
    }

    void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_langLoading || LanguageBox.SelectedIndex < 0)
            return;
        string? language = LanguageBox.SelectedIndex == 1 ? Loc.Turkish : LanguageBox.SelectedIndex == 2 ? Loc.English : null;
        var settings = AppSettings.Load();
        if (settings.Language == language)
            return;
        settings.Language = language;
        settings.Save();
        // "" resets the override: the next launch follows the Windows preferred language again.
        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language ?? "";
        _ = ConfirmRestartAsync();
    }

    async Task ConfirmRestartAsync()
    {
        if (XamlRoot is null)
            return;
        var dialog = new ContentDialog
        {
            Title = Loc.S("settings.restart_prompt_title"),
            Content = Loc.S("settings.restart_prompt"),
            PrimaryButtonText = Loc.S("settings.restart_now"),
            CloseButtonText = Loc.S("settings.restart_later"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            App.Restart();
    }

    void KeyboardSetting_Changed(object sender, object e)
    {
        if (DebounceText is null || TapSlider is null)
            return;
        DebounceText.Text = ((int)DebounceSlider.Value).ToString();
        TapSlider.IsEnabled = TapToggle.IsOn;
        if (!_ready)
            return;
        Status.Busy(Loc.S("common.applying"));
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    async Task ApplyAsync()
    {
        byte debounce = (byte)(DebounceSlider.Value - 1);
        byte tap = TapToggle.IsOn ? (byte)TapSlider.Value : (byte)0;
        try
        {
            // Read-modify-write so lighting changes made elsewhere are kept.
            var profile = await KeyboardService.RunAsync(kb =>
            {
                var p = kb.ReadProfile();
                p.Debounce = debounce;
                p.TapSensitivity = tap;
                kb.WriteProfile(p);
                return kb.ReadProfile();
            });
            bool ok = profile.Debounce == debounce && profile.TapSensitivity == tap;
            if (ok)
                Status.Ok(Loc.S("common.applied"));
            else
                Status.Pending(Loc.S("common.mismatch"));
        }
        catch (Exception ex)
        {
            Status.Error(ex.Message);
        }
    }

    async void Backup_Click(object sender, RoutedEventArgs e)
    {
        string dir = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        try
        {
            Status.Busy(Loc.S("settings.backup_doing"));
            await KeyboardService.RunAsync(kb => KeyboardBackup.Save(kb, dir));
            RefreshBackups();
            Status.Ok(Loc.F("settings.backup_done", Path.GetFileName(dir)));
        }
        catch (Exception ex)
        {
            Status.Error(ex.Message);
        }
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(BackupRoot);
        Process.Start(new ProcessStartInfo("explorer.exe", BackupRoot) { UseShellExecute = true });
    }

    async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (BackupBox.SelectedItem is not string name)
        {
            Status.Pending(Loc.S("settings.pick_backup"));
            return;
        }
        if (!await ConfirmAsync(Loc.S("settings.restore_confirm_title"), Loc.F("settings.restore_confirm", name)))
            return;
        try
        {
            Status.Busy(Loc.S("settings.restore_doing"));
            await KeyboardService.RunAsync(kb => KeyboardBackup.Restore(kb, Path.Combine(BackupRoot, name)));
            await LoadAsync();
            Status.Ok(Loc.F("settings.restored", name));
        }
        catch (Exception ex)
        {
            Status.Error(ex.Message);
        }
    }

    async void Factory_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync(Loc.S("settings.factory_confirm_title"), Loc.S("settings.factory_confirm")))
            return;
        try
        {
            Status.Busy(Loc.S("settings.resetting"));
            await KeyboardService.RunAsync(kb => kb.RestoreFactoryDefaults());
            await LoadAsync();
            Status.Ok(Loc.S("settings.factory_done"));
        }
        catch (Exception ex)
        {
            Status.Error(ex.Message);
        }
    }

    void RefreshBackups()
    {
        BackupBox.Items.Clear();
        if (!Directory.Exists(BackupRoot))
            return;
        foreach (var dir in Directory.GetDirectories(BackupRoot).OrderByDescending(d => d))
            BackupBox.Items.Add(Path.GetFileName(dir));
    }

    async Task<bool> ConfirmAsync(string title, string text)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = text,
            PrimaryButtonText = Loc.S("common.continue"),
            CloseButtonText = Loc.S("common.cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
