using AulaKb.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AulaKb.App.Pages;

/// <summary>
/// Layer selector, the keyboard, and a one-line editor for the clicked key. Every change is
/// written to the keyboard shortly after it is made.
/// </summary>
public sealed partial class KeysPage : Page
{
    readonly Dictionary<Layer, KeyLayer> _layers = [];
    readonly HashSet<Layer> _dirty = [];
    readonly DispatcherQueueTimer _applyTimer;
    IReadOnlyList<Macro> _macros = [];
    Layer _layer = Layer.Default;
    KeyInfo? _key;
    bool _loading;

    public KeysPage()
    {
        InitializeComponent();
        TitleText.Text = Loc.S("keys.title");
        ToolTipService.SetToolTip(MoreButton, Loc.S("keys.more"));
        ResetLayerItem.Text = Loc.S("keys.reset_layer");
        ReloadItem.Text = Loc.S("keys.reload");
        LayerDefaultItem.Text = Loc.S("keys.layer_default");
        ResetKeyText.Text = Loc.S("keys.reset_key");
        ToolTipService.SetToolTip(ResetKeyButton, Loc.S("keys.reset_key_tt"));
        Placeholder.Text = Loc.S("keys.placeholder");
        foreach (var item in TypeBox.Items.OfType<ComboBoxItem>())
            item.Content = Loc.S($"keys.type_{item.Tag}");
        foreach (var item in MouseBox.Items.OfType<ComboBoxItem>())
            item.Content = Loc.S($"keys.mouse_{item.Tag switch { "1" => "left", "2" => "right", "3" => "middle", "4" => "back", _ => "forward" }}");
        MacroBox.PlaceholderText = Loc.S("keys.macro_none");
        foreach (var item in RepeatBox.Items.OfType<ComboBoxItem>())
            item.Content = Loc.S($"keys.repeat_{item.Tag switch { "4" => "while", "2" => "until", _ => "count" }}");
        foreach (var (usage, name) in HidUsage.All.OrderBy(u => u.Key))
            KeyBox.Items.Add(new ComboBoxItem { Content = Loc.KeyName(usage), Tag = usage });
        foreach (var (usage, name) in Loc.ConsumerUsages())
            MediaBox.Items.Add(new ComboBoxItem { Content = name, Tag = usage });
        foreach (var (name, _) in Loc.Commands())
            CommandBox.Items.Add(new ComboBoxItem { Content = name });
        Board.KeyClicked += SelectKey;

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
        try
        {
            Status.Busy(Loc.S("common.reading"));
            var (layers, macros) = await KeyboardService.RunAsync(kb =>
                (Enum.GetValues<Layer>().ToDictionary(l => l, kb.ReadLayer), kb.ReadMacros()));
            if (layers.Values.Any(l => !l.IsValid))
                throw new InvalidDataException(Loc.S("error.layer_signature"));
            _layers.Clear();
            foreach (var (l, keys) in layers)
                _layers[l] = keys;
            _dirty.Clear();
            _macros = macros;
            MacroBox.Items.Clear();
            for (int i = 0; i < _macros.Count; i++)
                MacroBox.Items.Add(new ComboBoxItem { Content = $"{i + 1}. {_macros[i].Name}", Tag = (byte)i });
            Refresh();
            Status.Ok(Loc.S("common.synced"));
        }
        catch (Exception ex)
        {
            Status.Error(ex.Message);
        }
    }

    void Refresh()
    {
        if (!_layers.TryGetValue(_layer, out var keys))
            return;
        var defaults = Defaults.Layer(_layer);
        var accent = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        foreach (var key in Board.Keys)
        {
            var action = keys[key.Led];
            Board.SetKeyColor(key, action != defaults[key.Led] ? accent.Color : null);
            Board.SetToolTip(key, $"{key.Label}: {Loc.Describe(action)}");
        }
        if (_key is not null)
            SelectKey(_key);
    }

    void LayerBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        _layer = Enum.Parse<Layer>((string)sender.SelectedItem.Tag);
        Refresh();
    }

    void SelectKey(KeyInfo key)
    {
        _key = key;
        Board.Selected = key;
        if (!_layers.TryGetValue(_layer, out var keys))
            return;
        Placeholder.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        SelectedText.Text = key.Label;
        DefaultText.Text = Loc.F("keys.default_fmt", Loc.Describe(Defaults.Layer(_layer)[key.Led]));
        ShowEditor(keys[key.Led]);
    }

    void ShowEditor(KeyAction action)
    {
        _loading = true;
        string tag = action.Kind switch
        {
            KeyActionType.Mouse => "mouse",
            KeyActionType.Consumer => "media",
            KeyActionType.Macro => "macro",
            KeyActionType.Function or KeyActionType.Lighting => "command",
            _ when action == KeyAction.Disabled => "disabled",
            _ => "key",
        };
        // Shortcut commands are stored as key combinations; show them as such if they match one.
        if (tag == "key" && Commands.All.Any(c => c.Action == action) && action.B != 0)
            tag = "command";
        TypeBox.SelectedItem = TypeBox.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == tag);
        ShowPanel(tag);
        switch (tag)
        {
            case "key":
                byte usage = action.C != 0 ? action.C : action.B;
                KeyBox.SelectedItem = KeyBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (byte)i.Tag == usage);
                CtrlBox.IsChecked = action.Mods.HasFlag(Modifiers.LCtrl);
                ShiftBox.IsChecked = action.Mods.HasFlag(Modifiers.LShift);
                AltBox.IsChecked = action.Mods.HasFlag(Modifiers.LAlt);
                WinBox.IsChecked = action.Mods.HasFlag(Modifiers.LWin);
                break;
            case "mouse":
                MouseBox.SelectedIndex = Math.Clamp(action.A - 1, 0, 4);
                break;
            case "media":
                MediaBox.SelectedItem = MediaBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (ushort)i.Tag == action.ConsumerUsage);
                break;
            case "macro":
                MacroBox.SelectedIndex = action.C < MacroBox.Items.Count ? action.C : -1;
                RepeatBox.SelectedIndex = (MacroRepeat)action.A switch { MacroRepeat.WhileHeld => 1, MacroRepeat.UntilAnyKey => 2, _ => 0 };
                CountBox.Value = Math.Max(1, (int)action.B);
                break;
            case "command":
                CommandBox.SelectedIndex = Commands.All.ToList().FindIndex(c => c.Action == action);
                break;
        }
        _loading = false;
    }

    void ShowPanel(string tag)
    {
        if (CountBox is null || CommandBox is null)
            return;
        KeyPanel.Visibility = tag == "key" ? Visibility.Visible : Visibility.Collapsed;
        MouseBox.Visibility = tag == "mouse" ? Visibility.Visible : Visibility.Collapsed;
        MediaBox.Visibility = tag == "media" ? Visibility.Visible : Visibility.Collapsed;
        MacroPanel.Visibility = tag == "macro" ? Visibility.Visible : Visibility.Collapsed;
        CommandBox.Visibility = tag == "command" ? Visibility.Visible : Visibility.Collapsed;
        CountBox.Visibility = RepeatBox.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void TypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        string tag = (TypeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        ShowPanel(tag);
        if (!_loading)
            Value_Changed(sender, e);
    }

    void CountBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => Value_Changed(sender, args);

    void Value_Changed(object sender, object e)
    {
        // Raised while InitializeComponent is still creating the editor (RepeatBox SelectedIndex="0").
        if (CountBox is null || RepeatBox is null || TypeBox is null)
            return;
        CountBox.Visibility = RepeatBox.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_loading || _key is null || !_layers.TryGetValue(_layer, out var keys))
            return;
        KeyAction? action = TryBuildAction();
        if (action is not KeyAction a || keys[_key.Led] == a)
            return;
        keys[_key.Led] = a;
        _dirty.Add(_layer);
        Refresh();
        ScheduleApply();
    }

    /// <summary>The action described by the editor, or null while it is incomplete.</summary>
    KeyAction? TryBuildAction()
    {
        string tag = (TypeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        switch (tag)
        {
            case "disabled":
                return KeyAction.Disabled;
            case "key":
            {
                var mods = Modifiers.None;
                if (CtrlBox.IsChecked == true) mods |= Modifiers.LCtrl;
                if (ShiftBox.IsChecked == true) mods |= Modifiers.LShift;
                if (AltBox.IsChecked == true) mods |= Modifiers.LAlt;
                if (WinBox.IsChecked == true) mods |= Modifiers.LWin;
                byte usage = (KeyBox.SelectedItem as ComboBoxItem)?.Tag is byte u ? u : (byte)0;
                return usage == 0 && mods == Modifiers.None ? null : KeyAction.Key(usage, mods);
            }
            case "mouse":
                return MouseBox.SelectedItem is ComboBoxItem m ? KeyAction.MouseButton(byte.Parse((string)m.Tag)) : null;
            case "media":
                return MediaBox.SelectedItem is ComboBoxItem c ? KeyAction.Consumer((ushort)c.Tag) : null;
            case "macro":
                if (MacroBox.SelectedItem is not ComboBoxItem item)
                    return null;
                var repeat = (MacroRepeat)byte.Parse((string)((ComboBoxItem)RepeatBox.SelectedItem).Tag);
                return KeyAction.Macro((byte)item.Tag, repeat, (byte)Math.Clamp(double.IsNaN(CountBox.Value) ? 1 : CountBox.Value, 1, 255));
            case "command":
                return CommandBox.SelectedIndex >= 0 ? Commands.All[CommandBox.SelectedIndex].Action : null;
            default:
                return null;
        }
    }

    void ResetKey_Click(object sender, RoutedEventArgs e)
    {
        if (_key is null || !_layers.TryGetValue(_layer, out var keys))
            return;
        keys[_key.Led] = Defaults.Layer(_layer)[_key.Led];
        _dirty.Add(_layer);
        Refresh();
        ScheduleApply();
    }

    void ResetLayer_Click(object sender, RoutedEventArgs e)
    {
        _layers[_layer] = Defaults.Layer(_layer);
        _dirty.Add(_layer);
        Refresh();
        ScheduleApply();
    }

    async void Reload_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    void ScheduleApply()
    {
        Status.Busy(Loc.S("common.applying"));
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    async Task ApplyAsync()
    {
        var pending = _dirty.ToDictionary(l => l, l => new KeyLayer(_layers[l].ToArray()));
        _dirty.Clear();
        if (pending.Count == 0)
            return;
        try
        {
            bool ok = await KeyboardService.RunAsync(kb =>
            {
                bool same = true;
                foreach (var (layer, keys) in pending)
                {
                    kb.WriteLayer(layer, keys);
                    same &= kb.ReadLayer(layer).ToArray().AsSpan().SequenceEqual(keys.ToArray());
                }
                return same;
            });
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
}
