using System.Diagnostics;
using AulaKb.App.Controls;
using AulaKb.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace AulaKb.App.Pages;

/// <summary>Macro list, recording with real timing, per-step delays. Changes are saved to the keyboard automatically.</summary>
public sealed partial class MacrosPage : Page
{
    readonly List<Macro> _macros = [];
    readonly Stopwatch _clock = new();
    readonly HashSet<byte> _held = [];
    readonly DispatcherQueueTimer _saveTimer;
    bool _loading;

    public MacrosPage()
    {
        InitializeComponent();
        TitleText.Text = Loc.S("macros.title");
        NameBox.PlaceholderText = Loc.S("macros.name_ph");
        NewText.Text = Loc.S("macros.new");
        ToolTipService.SetToolTip(NewButton, Loc.S("macros.new_tt"));
        ToolTipService.SetToolTip(DeleteButton, Loc.S("macros.delete_tt"));
        ToolTipService.SetToolTip(DeleteEventButton, Loc.S("macros.delete_step_tt"));
        DelayLabel.Text = Loc.S("macros.step_delay");
        DelayAllButton.Content = Loc.S("macros.delay_all");
        FooterText.Text = Loc.S("macros.footer");
        RecordText.Text = Loc.S("macros.record");
        InsertKeyText.Text = Loc.S("macros.insert_key");
        ToolTipService.SetToolTip(InsertKeyButton, Loc.S("macros.insert_key_tt"));
        ToolTipService.SetToolTip(MacroMenuButton, Loc.S("profile.more"));
        ExportItem.Text = Loc.S("macros.export");
        ImportItem.Text = Loc.S("macros.import");
        _saveTimer = DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(800);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += async (_, _) => await SaveAsync();
        Loaded += async (_, _) => await LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        RecordButton.IsChecked = false;
        if (_saveTimer.IsRunning)
        {
            _saveTimer.Stop();
            _ = SaveAsync();
        }
        base.OnNavigatedFrom(e);
    }

    Macro? Current => MacroList.SelectedIndex >= 0 && MacroList.SelectedIndex < _macros.Count ? _macros[MacroList.SelectedIndex] : null;

    async Task LoadAsync()
    {
        try
        {
            Status.Busy(Loc.S("common.reading"));
            var macros = await KeyboardService.RunAsync(kb => kb.ReadMacros());
            _macros.Clear();
            _macros.AddRange(macros);
            RefreshList(0);
            Status.Ok(_macros.Count == 0 ? Loc.S("macros.status_empty") : Loc.F("macros.status_count", _macros.Count));
        }
        catch (Exception ex)
        {
            Status.Error(ex.Message);
        }
    }

    void RefreshList(int select)
    {
        _loading = true;
        MacroList.Items.Clear();
        for (int i = 0; i < _macros.Count; i++)
            MacroList.Items.Add(ListLabel(i));
        MacroList.SelectedIndex = Math.Min(select, _macros.Count - 1);
        _loading = false;
        RefreshEvents();
    }

    string ListLabel(int i) => $"{i + 1}. {_macros[i].Name}";

    void RefreshEvents()
    {
        var macro = Current;
        EditorPane.Opacity = macro is null ? 0.5 : 1;
        NameBox.IsEnabled = RecordButton.IsEnabled = macro is not null;
        int keep = EventList.SelectedIndex;
        EventList.Items.Clear();
        if (macro is null)
        {
            NameBox.Text = "";
            EmptyText.Text = Loc.S("macros.empty_create");
            EmptyText.Visibility = Visibility.Visible;
            StepBar.Visibility = Visibility.Collapsed;
            return;
        }
        if (NameBox.Text != macro.Name)
            NameBox.Text = macro.Name;
        foreach (var e in macro.Events)
            EventList.Items.Add($"{(e.Down ? "↓" : "↑")}  {HidUsage.Name(e.Usage)}{(e.DelayMs > 0 ? $"   ·   {e.DelayMs} ms" : "")}");
        EmptyText.Text = Loc.S("macros.empty");
        EmptyText.Visibility = macro.Events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EventList.SelectedIndex = Math.Min(keep, EventList.Items.Count - 1);
        StepBar.Visibility = EventList.SelectedIndex >= 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void MacroList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;
        RecordButton.IsChecked = false;
        RefreshEvents();
    }

    void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (Current is not Macro macro || macro.Name == NameBox.Text)
            return;
        macro.Name = NameBox.Text;
        int index = MacroList.SelectedIndex;
        _loading = true;
        MacroList.Items[index] = ListLabel(index);
        MacroList.SelectedIndex = index;
        _loading = false;
        ScheduleSave();
    }

    void New_Click(object sender, RoutedEventArgs e)
    {
        _macros.Add(new Macro { Name = Loc.F("macros.new_name", _macros.Count + 1) });
        RefreshList(_macros.Count - 1);
        ScheduleSave();
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Current is null)
            return;
        int index = MacroList.SelectedIndex;
        _macros.RemoveAt(index);
        RefreshList(Math.Max(0, index - 1));
        ScheduleSave();
    }

    // ---- recording ----

    void Record_Checked(object sender, RoutedEventArgs e)
    {
        if (Current is null)
        {
            RecordButton.IsChecked = false;
            return;
        }
        Current.Events.Clear();
        _held.Clear();
        _clock.Reset();
        RefreshEvents();
        RecordIcon.Glyph = "";
        RecordText.Text = Loc.S("macros.stop");
        EmptyText.Text = Loc.S("macros.recording");
        Status.Busy(Loc.S("macros.status_recording"));
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += OnKeyUp;
        Focus(FocusState.Programmatic);
    }

    void Record_Unchecked(object sender, RoutedEventArgs e)
    {
        PreviewKeyDown -= OnKeyDown;
        PreviewKeyUp -= OnKeyUp;
        _clock.Stop();
        RecordIcon.Glyph = "";
        RecordText.Text = Loc.S("macros.record");
        RefreshEvents();
        if (Current is Macro macro && macro.Events.Count > 0)
            ScheduleSave();
    }

    void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        e.Handled = true;
        if (VirtualKeyMap.TryGet(e.Key, out byte usage) && _held.Add(usage))
            AddEvent(true, usage);
    }

    void OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        e.Handled = true;
        if (VirtualKeyMap.TryGet(e.Key, out byte usage) && _held.Remove(usage))
            AddEvent(false, usage);
    }

    void AddEvent(bool down, byte usage)
    {
        if (Current is not Macro macro || macro.Events.Count >= 120)
            return;
        // The delay belongs to the previous step: the time between it and this one.
        if (macro.Events.Count > 0)
            macro.Events[^1] = macro.Events[^1] with { DelayMs = (ushort)Math.Min(_clock.ElapsedMilliseconds, ushort.MaxValue) };
        macro.Events.Add(new MacroEvent(down, usage, 0));
        _clock.Restart();
        RefreshEvents();
        EventList.SelectedIndex = EventList.Items.Count - 1;
    }

    // ---- editing ----

    void EventList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool has = Current is Macro m && EventList.SelectedIndex >= 0 && EventList.SelectedIndex < m.Events.Count;
        StepBar.Visibility = has && RecordButton.IsChecked != true ? Visibility.Visible : Visibility.Collapsed;
        if (has)
        {
            _loading = true;
            DelayBox.Value = Current!.Events[EventList.SelectedIndex].DelayMs;
            _loading = false;
        }
    }

    void DelayBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || Current is not Macro macro || EventList.SelectedIndex < 0 || double.IsNaN(args.NewValue))
            return;
        int i = EventList.SelectedIndex;
        macro.Events[i] = macro.Events[i] with { DelayMs = (ushort)Math.Clamp(args.NewValue, 0, 65535) };
        RefreshEvents();
        ScheduleSave();
    }

    void DelayAll_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not Macro macro || double.IsNaN(DelayBox.Value))
            return;
        for (int i = 0; i < macro.Events.Count; i++)
            macro.Events[i] = macro.Events[i] with { DelayMs = (ushort)Math.Clamp(DelayBox.Value, 0, 65535) };
        RefreshEvents();
        ScheduleSave();
    }

    void DeleteEvent_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not Macro macro || EventList.SelectedIndex < 0)
            return;
        macro.Events.RemoveAt(EventList.SelectedIndex);
        RefreshEvents();
        ScheduleSave();
    }

    // ---- inserting a key into an existing macro ----

    void InsertKey_Checked(object sender, RoutedEventArgs e)
    {
        if (Current is null)
        {
            InsertKeyButton.IsChecked = false;
            return;
        }
        Status.Busy(Loc.S("macros.insert_waiting"));
        PreviewKeyDown += OnInsertKey;
        Focus(FocusState.Programmatic);
    }

    void InsertKey_Unchecked(object sender, RoutedEventArgs e) => PreviewKeyDown -= OnInsertKey;

    /// <summary>Adds a press and a release of the pressed key after the selected step (or at the end).</summary>
    void OnInsertKey(object sender, KeyRoutedEventArgs e)
    {
        e.Handled = true;
        if (Current is not Macro macro || !VirtualKeyMap.TryGet(e.Key, out byte usage))
            return;
        int at = EventList.SelectedIndex >= 0 ? EventList.SelectedIndex + 1 : macro.Events.Count;
        if (macro.Events.Count + 2 > 120)
        {
            InsertKeyButton.IsChecked = false;
            return;
        }
        // a short, typical press: 30 ms held, then 30 ms before whatever followed
        macro.Events.Insert(at, new MacroEvent(true, usage, 30));
        macro.Events.Insert(at + 1, new MacroEvent(false, usage, 30));
        InsertKeyButton.IsChecked = false;
        RefreshEvents();
        EventList.SelectedIndex = at + 1;
        ScheduleSave();
    }

    // ---- import / export (JSON: [{ "name": "...", "events": [{ "down": true, "usage": 4, "delay": 30 }] }]) ----

    sealed record MacroFileEvent(bool Down, byte Usage, ushort Delay);

    sealed record MacroFile(string Name, List<MacroFileEvent> Events);

    static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_macros.Count == 0)
            return;
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = Loc.S("macros.title") };
        picker.FileTypeChoices.Add("JSON", [".json"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
            return;
        try
        {
            var data = _macros.Select(m => new MacroFile(m.Name, m.Events.Select(x => new MacroFileEvent(x.Down, x.Usage, x.DelayMs)).ToList())).ToList();
            await File.WriteAllTextAsync(file.Path, System.Text.Json.JsonSerializer.Serialize(data, JsonOptions));
            Status.Ok(Loc.F("macros.exported", _macros.Count, file.Name));
        }
        catch (Exception ex)
        {
            Status.Error(ex.Message);
        }
    }

    async void Import_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;
        try
        {
            var data = System.Text.Json.JsonSerializer.Deserialize<List<MacroFile>>(await File.ReadAllTextAsync(file.Path), JsonOptions)
                ?? throw new InvalidDataException(file.Name);
            int first = _macros.Count;
            foreach (var m in data)
            {
                var macro = new Macro { Name = m.Name };
                macro.Events.AddRange(m.Events.Take(120).Select(x => new MacroEvent(x.Down, x.Usage, x.Delay)));
                _macros.Add(macro);
            }
            RefreshList(first);
            ScheduleSave(); // too long for the keyboard shows an error instead of writing
        }
        catch (Exception ex)
        {
            Status.Error(ex.Message);
        }
    }

    // ---- saving ----

    void ScheduleSave()
    {
        Status.Busy(Loc.S("macros.status_saving"));
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    async Task SaveAsync()
    {
        if (RecordButton.IsChecked == true)
            return; // saved when recording stops
        var macros = _macros.Select(m =>
        {
            var copy = new Macro { Name = m.Name };
            copy.Events.AddRange(m.Events);
            return copy;
        }).ToList();
        int size = Macro.BuildBlob(macros).Length;
        if (size > AulaKeyboard.ChunkLength)
        {
            Status.Error(Loc.F("macros.too_long", size, AulaKeyboard.ChunkLength));
            return;
        }
        try
        {
            int back = await KeyboardService.RunAsync(kb =>
            {
                kb.WriteMacros(macros);
                return kb.ReadMacros().Count;
            });
            if (back == macros.Count)
                Status.Ok(Loc.S("macros.saved"));
            else
                Status.Pending(Loc.S("macros.mismatch"));
        }
        catch (Exception ex)
        {
            Status.Error(ex.Message);
        }
    }
}
