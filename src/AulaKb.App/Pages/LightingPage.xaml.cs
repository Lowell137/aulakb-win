using System.Diagnostics;
using AulaKb.App.Controls;
using AulaKb.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace AulaKb.App.Pages;

/// <summary>
/// Effect list on the left, the selected effect playing on the drawn keyboard, and a single row
/// of controls. Every change is written to the keyboard automatically after a short pause.
/// </summary>
public sealed partial class LightingPage : Page
{
    static readonly Rgb[] Presets =
    [
        new(255, 0, 0), new(255, 128, 0), new(255, 255, 0), new(0, 255, 0),
        new(0, 0, 255), new(0, 255, 255), new(255, 0, 255), new(255, 255, 255),
    ];

    readonly EffectPreview _preview;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly DispatcherQueueTimer _frameTimer;
    readonly DispatcherQueueTimer _applyTimer;

    LightProfile? _profile;
    Palette? _palette;
    PerKeyColors? _perKey;
    bool _perKeyDirty;
    bool _loading;
    Rgb _paint = new(255, 0, 0);

    public LightingPage()
    {
        InitializeComponent();
        TitleText.Text = Loc.S("lighting.title");
        BrightnessSlider.Header = Loc.S("lighting.brightness");
        SpeedSlider.Header = Loc.S("lighting.speed");
        ToolTipService.SetToolTip(RainbowButton, Loc.S("lighting.colourful"));
        ToolTipService.SetToolTip(CustomButton, Loc.S("lighting.custom"));
        PaintAllButton.Content = Loc.S("lighting.paint_all");
        PaintClearButton.Content = Loc.S("lighting.paint_clear");
        PaintDefaultButton.Content = Loc.S("lighting.paint_default");
        _preview = new EffectPreview(Board.Keys);
        foreach (var effect in Effects.All)
        {
            string name = effect.HiddenByVendor ? Loc.F("lighting.experimental", effect.Name) : effect.Name;
            EffectList.Items.Add(new ListViewItem { Content = name, Tag = effect });
            EffectCombo.Items.Add(new ComboBoxItem { Content = name, Tag = effect });
        }
        foreach (var preset in Presets)
        {
            var swatch = new Button
            {
                Width = 34,
                Height = 34,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(17),
                Background = new SolidColorBrush(preset.ToColor()),
                Tag = preset,
            };
            ToolTipService.SetToolTip(swatch, preset.ToString());
            swatch.Click += (_, _) => PickColour(preset);
            Swatches.Children.Insert(Swatches.Children.Count - 1, swatch); // before the "+" button
        }
        Board.KeyClicked += PaintKey;
        Board.KeyDragged += DragPaintKey;
        CustomSwatch.AttachSaved(Swatches, CustomButton, CustomPicker, PickColour);

        _frameTimer = DispatcherQueue.CreateTimer();
        _frameTimer.Interval = TimeSpan.FromMilliseconds(40);
        _frameTimer.Tick += (_, _) => DrawFrame();

        _applyTimer = DispatcherQueue.CreateTimer();
        _applyTimer.Interval = TimeSpan.FromMilliseconds(500);
        _applyTimer.IsRepeating = false;
        _applyTimer.Tick += async (_, _) => await ApplyAsync();

        Loaded += async (_, _) =>
        {
            _frameTimer.Start();
            await LoadAsync();
        };
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _frameTimer.Stop();
        if (_applyTimer.IsRunning)
        {
            _applyTimer.Stop();
            _ = ApplyAsync();
        }
        base.OnNavigatedFrom(e);
    }

    Effect? Selected => (EffectList.SelectedItem as ListViewItem)?.Tag as Effect;

    async Task LoadAsync()
    {
        try
        {
            SetStatus("", Loc.S("common.reading"), false);
            (_profile, _palette, _perKey) = await KeyboardService.RunAsync(kb => (kb.ReadProfile(), kb.ReadPalette(), kb.ReadPerKeyColors()));
            if (!_profile.IsValid)
                throw new InvalidDataException(Loc.S("error.profile_signature"));
            _loading = true;
            EffectList.SelectedItem = EffectList.Items.Cast<ListViewItem>().First(i => ((Effect)i.Tag).Id == _profile.Effect);
            _loading = false;
            ShowControls();
            SetStatus("", Loc.S("common.synced"), false);
        }
        catch (Exception ex)
        {
            _profile = null;
            SetStatus("", ex.Message, true);
        }
    }

    void DrawFrame()
    {
        if (_profile is null || _palette is null)
            return;
        byte id = _profile.Effect;
        byte slot = id is Effects.Off or Effects.SelfDefine ? (byte)0 : _profile.GetColourSlot(id);
        bool colourful = slot == LightProfile.ColourfulSlot;
        var colour = colourful || id is Effects.Off or Effects.SelfDefine ? Rgb.Black : _palette.Get(id, slot);
        int brightness = id is Effects.Off or Effects.SelfDefine ? 4 : _profile.GetBrightness(id);
        int speed = id is Effects.Off or Effects.SelfDefine ? 0 : _profile.GetSpeed(id);
        var lights = _preview.Render(id, _clock.Elapsed.TotalSeconds, brightness, speed, colourful, colour, _perKey);
        int r = 0, g = 0, b = 0;
        for (int i = 0; i < lights.Length; i++)
        {
            Board.SetKeyLight(_preview.Keys[i], lights[i]);
            r += lights[i].R;
            g += lights[i].G;
            b += lights[i].B;
        }
        int n = Math.Max(1, lights.Length);
        Board.SetBarLight(new Rgb((byte)Math.Min(255, 2 * r / n), (byte)Math.Min(255, 2 * g / n), (byte)Math.Min(255, 2 * b / n)));
    }

    void EffectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Narrow windows show a drop-down instead of the list; both stay in step.
        if (EffectCombo.SelectedIndex >= 0 && EffectList.SelectedIndex != EffectCombo.SelectedIndex)
            EffectList.SelectedIndex = EffectCombo.SelectedIndex;
    }

    void EffectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EffectCombo.SelectedIndex != EffectList.SelectedIndex)
            EffectCombo.SelectedIndex = EffectList.SelectedIndex;
        if (_loading || _profile is null || Selected is not Effect effect)
            return;
        _profile.Effect = effect.Id;
        ShowControls();
        ScheduleApply();
    }

    void ShowControls()
    {
        if (_profile is null || _palette is null || Selected is not Effect effect)
            return;
        _loading = true;
        bool off = effect.Id == Effects.Off;
        bool selfDefine = effect.Id == Effects.SelfDefine;
        // Like the vendor tool: no brightness/speed for Off and Self-define.
        SliderRow.Visibility = off || selfDefine ? Visibility.Collapsed : Visibility.Visible;
        ColourRow.Visibility = off ? Visibility.Collapsed : Visibility.Visible;
        PaintRow.Visibility = selfDefine ? Visibility.Visible : Visibility.Collapsed;
        RainbowButton.Visibility = selfDefine ? Visibility.Collapsed : Visibility.Visible;
        ColourHint.Text = selfDefine ? Loc.S("lighting.colour_hint") : Loc.S("lighting.colour");
        if (!off && !selfDefine)
        {
            BrightnessSlider.Value = _profile.GetBrightness(effect.Id);
            SpeedSlider.Value = _profile.GetSpeed(effect.Id);
            RainbowButton.IsChecked = _profile.GetColourSlot(effect.Id) == LightProfile.ColourfulSlot;
        }
        HighlightSwatch();
        _loading = false;
    }

    void Param_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading || _profile is null || Selected is not Effect effect || effect.Id is Effects.Off or Effects.SelfDefine)
            return;
        _profile.SetBrightness(effect.Id, (byte)BrightnessSlider.Value);
        _profile.SetSpeed(effect.Id, (byte)SpeedSlider.Value);
        ScheduleApply();
    }

    void Rainbow_Click(object sender, RoutedEventArgs e)
    {
        if (_profile is null || Selected is not Effect effect)
            return;
        _profile.SetColourSlot(effect.Id, RainbowButton.IsChecked == true ? (byte)LightProfile.ColourfulSlot : (byte)0);
        HighlightSwatch();
        ScheduleApply();
    }

    void CustomPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (!_loading)
            PickColour(args.NewColor.ToRgb());
    }

    void PickColour(Rgb colour)
    {
        if (_profile is null || _palette is null || Selected is not Effect effect)
            return;
        _paint = colour;
        if (effect.Id != Effects.SelfDefine)
        {
            // Same as the vendor tool: the colour goes to palette slot 0 and the effect uses slot 0.
            _palette.Set(effect.Id, 0, colour);
            _profile.SetColourSlot(effect.Id, 0);
            RainbowButton.IsChecked = false;
            ScheduleApply();
        }
        HighlightSwatch();
    }

    void HighlightSwatch()
    {
        Rgb? active = null;
        if (_profile is not null && _palette is not null && Selected is Effect effect)
        {
            if (effect.Id == Effects.SelfDefine)
                active = _paint;
            else if (effect.Id != Effects.Off && _profile.GetColourSlot(effect.Id) != LightProfile.ColourfulSlot)
                active = _palette.Get(effect.Id, _profile.GetColourSlot(effect.Id));
        }
        CustomSwatch.Highlight(Swatches, CustomButton, active);
    }

    // ---- self-define painting ----

    void PaintKey(KeyInfo key)
    {
        if (_perKey is null || _profile?.Effect != Effects.SelfDefine)
            return;
        _perKey[key.Led] = _perKey[key.Led] == _paint ? Rgb.Black : _paint;
        _perKeyDirty = true;
        ScheduleApply();
    }

    /// <summary>Drag painting sets the colour on every key passed (no toggling).</summary>
    void DragPaintKey(KeyInfo key)
    {
        if (_perKey is null || _profile?.Effect != Effects.SelfDefine || _perKey[key.Led] == _paint)
            return;
        _perKey[key.Led] = _paint;
        _perKeyDirty = true;
        ScheduleApply();
    }

    void PaintAll_Click(object sender, RoutedEventArgs e) => PaintEvery(_ => _paint);

    void PaintClear_Click(object sender, RoutedEventArgs e) => PaintEvery(_ => Rgb.Black);

    void PaintDefault_Click(object sender, RoutedEventArgs e)
    {
        var defaults = Defaults.PerKeyColors();
        PaintEvery(key => defaults[key.Led]);
    }

    void PaintEvery(Func<KeyInfo, Rgb> colour)
    {
        if (_perKey is null)
            return;
        foreach (var key in Board.Keys)
            _perKey[key.Led] = colour(key);
        _perKeyDirty = true;
        ScheduleApply();
    }

    // ---- writing ----

    void ScheduleApply()
    {
        SetStatus("", Loc.S("common.applying"), false);
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    async Task ApplyAsync()
    {
        if (_profile is null || _palette is null || _perKey is null)
            return;
        var profile = new LightProfile(_profile.ToArray());
        var palette = new Palette(_palette.ToArray());
        var perKey = _perKeyDirty ? new PerKeyColors(_perKey.ToArray()) : null;
        _perKeyDirty = false;
        try
        {
            bool ok = await KeyboardService.RunAsync(kb =>
            {
                kb.WriteProfile(profile);
                kb.WritePalette(palette);
                if (perKey is not null)
                    kb.WritePerKeyColors(perKey);
                return kb.ReadProfile().ToArray().AsSpan().SequenceEqual(profile.ToArray());
            });
            SetStatus(ok ? "" : "", ok ? Loc.S("common.applied") : Loc.S("common.mismatch"), !ok);
        }
        catch (Exception ex)
        {
            SetStatus("", ex.Message, true);
        }
    }

    void SetStatus(string glyph, string text, bool error)
    {
        if (error)
            Status.Error(text);
        else if (glyph == "")
            Status.Ok(text);
        else if (glyph == "")
            Status.Pending(text);
        else
            Status.Busy(text);
    }
}
