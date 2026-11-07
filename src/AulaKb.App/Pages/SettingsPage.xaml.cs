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
        ((ComboBoxItem)LanguageBox.Items[0]).Content = Loc.S("settings.language_system");
        ((ComboBoxItem)LanguageBox.Items[1]).Content = Loc.S("settings.language_tr");
        ((ComboBoxItem)LanguageBox.Items[2]).Content = Loc.S("settings.language_en");
        LayoutRow.Title = Loc.S("settings.layout_title");
        LayoutRow.Description = Loc.S("settings.layout_desc");
        DebounceRow.Title = Loc.S("settings.debounce");
        DebounceRow.Description = Loc.S("settings.debounce_desc");
        TapRow.Title = Loc.S("settings.tap");
        TapRow.Description = Loc.S("settings.tap_desc");
        RestoreRow.Title = Loc.S("profile.title");
        RestoreRow.Description = Loc.S("profile.desc");
        RestoreButton.Content = Loc.S("profile.apply");
        ToolTipService.SetToolTip(ProfileMenuButton, Loc.S("profile.more"));
        ProfileNewItem.Text = Loc.S("profile.new");
        ProfileOverwriteItem.Text = Loc.S("profile.overwrite");
        ProfileRenameItem.Text = Loc.S("profile.rename");
        ProfileCopyItem.Text = Loc.S("profile.copy");
        ProfileDeleteItem.Text = Loc.S("profile.delete");
        ProfileExportItem.Text = Loc.S("profile.export");
        ProfileImportItem.Text = Loc.S("profile.import");
        ProfileFolderItem.Text = Loc.S("settings.backup_open");
        FactoryRow.Title = Loc.S("settings.factory_title");
        FactoryRow.Description = Loc.S("settings.factory_desc");
        FactoryButton.Content = Loc.S("settings.factory_btn");
        AboutRow.Title = Loc.S("settings.about_title");
        AboutRow.Description = Loc.S("settings.about_desc");
        SectionApp.Text = Loc.S("settings.section_app");
        TrayRow.Title = Loc.S("settings.tray_title");
        TrayRow.Description = Loc.S("settings.tray_desc");
        StartupRow.Title = Loc.S("settings.startup_title");
        StartupRow.Description = Loc.S("settings.startup_desc");
        _appLoading = true;
        TrayToggle.IsOn = AppSettings.Load().CloseToTray;
        StartupToggle.IsOn = Startup.IsEnabled;
        _appLoading = false;

        // First entry follows Windows; Tag null. The rest pin a drawing.
        var auto = new ComboBoxItem { Content = Loc.F("settings.layout_auto", VisualLayout.Name(VisualLayout.FromWindows())), Tag = null };
        LayoutBox.Items.Add(auto);
        bool isAuto = VisualLayout.IsAuto;
        if (isAuto)
            LayoutBox.SelectedItem = auto;
        var current = VisualLayout.Current;
        foreach (var kind in VisualLayout.All)
        {
            var item = new ComboBoxItem { Content = VisualLayout.Name(kind), Tag = kind };
            LayoutBox.Items.Add(item);
            if (!isAuto && kind == current)
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
        if (LayoutBox.SelectedItem is not ComboBoxItem item)
            return;
        var kind = item.Tag as LayoutKind?;
        if (kind is null == VisualLayout.IsAuto && (kind is null || kind == VisualLayout.Current))
            return;
        VisualLayout.Set(kind);
        Status.Ok(Loc.S("settings.layout_changed"));
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

    bool _appLoading;

    void TrayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_appLoading)
            return;
        var s = AppSettings.Load();
        s.CloseToTray = TrayToggle.IsOn;
        s.Save();
    }

    void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_appLoading)
            return;
        try
        {
            Startup.Set(StartupToggle.IsOn);
        }
        catch (Exception ex)
        {
            Status.Error(ex.Message);
        }
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

    // ---- profiles: one folder per profile under Belgeler\AulaKb\Yedekler (old backups included) ----

    string? SelectedProfile => BackupBox.SelectedItem as string;

    async Task SaveProfileAsync(string name)
    {
        string dir = Path.Combine(BackupRoot, name);
        Status.Busy(Loc.S("settings.backup_doing"));
        // write to a temporary folder first so a failed read never leaves half a profile
        string temp = dir + ".tmp";
        if (Directory.Exists(temp))
            Directory.Delete(temp, true);
        await KeyboardService.RunAsync(kb => KeyboardBackup.Save(kb, temp));
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
        Directory.Move(temp, dir);
        RefreshBackups(name);
        Status.Ok(Loc.F("settings.backup_done", name));
    }

    async void ProfileNew_Click(object sender, RoutedEventArgs e)
    {
        string? name = await AskNameAsync(Loc.S("profile.new"), Loc.F("profile.default_name", DateTime.Now.ToString("yyyy-MM-dd HH.mm")));
        if (name is null)
            return;
        try
        {
            await SaveProfileAsync(name);
        }
        catch (Exception ex)
        {
            Status.Error(ex.Message);
        }
    }

    async void ProfileOverwrite_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not string name)
        {
            Status.Pending(Loc.S("settings.pick_backup"));
            return;
        }
        if (!await ConfirmAsync(Loc.S("profile.overwrite"), Loc.F("profile.overwrite_confirm", name)))
            return;
        try
        {
            await SaveProfileAsync(name);
        }
        catch (Exception ex)
        {
            Status.Error(ex.Message);
        }
    }

    async void ProfileRename_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not string name)
        {
            Status.Pending(Loc.S("settings.pick_backup"));
            return;
        }
        string? newName = await AskNameAsync(Loc.S("profile.rename"), name);
        if (newName is null || newName == name)
            return;
        try
        {
            Directory.Move(Path.Combine(BackupRoot, name), Path.Combine(BackupRoot, newName));
            RefreshBackups(newName);
        }
        catch (Exception ex)
        {
            Status.Error(ex.Message);
        }
    }

    void ProfileCopy_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not string name)
        {
            Status.Pending(Loc.S("settings.pick_backup"));
            return;
        }
        try
        {
            string copy = FreeName(Loc.F("profile.copy_name", name));
            CopyFolder(Path.Combine(BackupRoot, name), Path.Combine(BackupRoot, copy));
            RefreshBackups(copy);
        }
        catch (Exception ex)
        {
            Status.Error(ex.Message);
        }
    }

    async void ProfileDelete_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not string name)
        {
            Status.Pending(Loc.S("settings.pick_backup"));
            return;
        }
        if (!await ConfirmAsync(Loc.S("profile.delete"), Loc.F("profile.delete_confirm", name)))
            return;
        try
        {
            // to the Recycle Bin, not gone for good
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(Path.Combine(BackupRoot, name),
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            RefreshBackups();
        }
        catch (Exception ex)
        {
            Status.Error(ex.Message);
        }
    }

    async void ProfileExport_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not string name)
        {
            Status.Pending(Loc.S("settings.pick_backup"));
            return;
        }
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = name };
        picker.FileTypeChoices.Add(Loc.S("profile.file_type"), [".aulakb"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
            return;
        try
        {
            if (File.Exists(file.Path))
                File.Delete(file.Path);
            System.IO.Compression.ZipFile.CreateFromDirectory(Path.Combine(BackupRoot, name), file.Path);
            Status.Ok(Loc.F("profile.exported", file.Name));
        }
        catch (Exception ex)
        {
            Status.Error(ex.Message);
        }
    }

    async void ProfileImport_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".aulakb");
        picker.FileTypeFilter.Add(".zip");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;
        try
        {
            string name = FreeName(Path.GetFileNameWithoutExtension(file.Name));
            string dir = Path.Combine(BackupRoot, name);
            System.IO.Compression.ZipFile.ExtractToDirectory(file.Path, dir);
            if (!File.Exists(Path.Combine(dir, "profile.bin")))
            {
                Directory.Delete(dir, true);
                throw new InvalidDataException(Loc.S("profile.import_invalid"));
            }
            RefreshBackups(name);
            Status.Ok(Loc.F("profile.imported", name));
        }
        catch (Exception ex)
        {
            Status.Error(ex.Message);
        }
    }

    static string FreeName(string name)
    {
        string candidate = name;
        for (int i = 2; Directory.Exists(Path.Combine(BackupRoot, candidate)); i++)
            candidate = $"{name} ({i})";
        return candidate;
    }

    static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
    }

    /// <summary>Asks for a profile name; null when cancelled. Rejects characters a folder cannot have.</summary>
    async Task<string?> AskNameAsync(string title, string suggestion)
    {
        var box = new TextBox { Text = suggestion, MaxLength = 60 };
        box.SelectAll();
        var dialog = new ContentDialog
        {
            Title = title,
            Content = box,
            PrimaryButtonText = Loc.S("common.continue"),
            CloseButtonText = Loc.S("common.cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        box.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = IsValidName(box.Text.Trim());
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;
        string name = box.Text.Trim();
        return IsValidName(name) ? name : null;
    }

    static bool IsValidName(string name) =>
        name.Length > 0 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.EndsWith('.') && !name.EndsWith(".tmp");

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

    void RefreshBackups(string? select = null)
    {
        string? keep = select ?? SelectedProfile;
        BackupBox.Items.Clear();
        if (!Directory.Exists(BackupRoot))
            return;
        foreach (var dir in Directory.GetDirectories(BackupRoot)
                     .Where(d => !d.EndsWith(".tmp"))
                     .OrderByDescending(Directory.GetLastWriteTime))
            BackupBox.Items.Add(Path.GetFileName(dir));
        if (keep is not null && BackupBox.Items.Contains(keep))
            BackupBox.SelectedItem = keep;
        else if (BackupBox.Items.Count > 0)
            BackupBox.SelectedIndex = 0;
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
