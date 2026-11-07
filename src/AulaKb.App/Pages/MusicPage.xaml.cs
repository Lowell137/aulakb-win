using AulaKb.App.Controls;
using AulaKb.App.Music;
using AulaKb.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace AulaKb.App.Pages;

public sealed partial class MusicPage : Page
{
    static readonly Rgb[] Presets =
    [
        new(255, 0, 0), new(255, 128, 0), new(255, 255, 0), new(0, 255, 0),
        new(0, 0, 255), new(0, 255, 255), new(255, 0, 255), new(255, 255, 255),
    ];

    readonly MusicVisualizer _visualizer = new();
    Rgb _colour = new(0, 255, 0);

    public MusicPage()
    {
        InitializeComponent();
        TitleText.Text = Loc.S("music.title");
        OnRow.Title = Loc.S("music.row_title");
        OnRow.Description = Loc.S("music.row_desc");
        OnToggle.OnContent = Loc.S("music.on");
        OnToggle.OffContent = Loc.S("music.off");
        ViewRow.Title = Loc.S("music.style");
        if (StyleBox.Items[0] is ComboBoxItem spectrum) spectrum.Content = Loc.S("music.style_spectrum");
        if (StyleBox.Items[1] is ComboBoxItem pulse) pulse.Content = Loc.S("music.style_pulse");
        if (StyleBox.Items[2] is ComboBoxItem wave) wave.Content = Loc.S("music.style_wave");
        GainRow.Title = Loc.S("music.gain");
        GainRow.Description = Loc.S("music.gain_desc");
        SmoothRow.Title = Loc.S("music.smooth");
        SmoothRow.Description = Loc.S("music.smooth_desc");
        ColourRow.Title = Loc.S("music.colour_title");
        ToolTipService.SetToolTip(RainbowButton, Loc.S("music.colourful"));
        foreach (var preset in Presets)
        {
            var swatch = new Button
            {
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(preset.ToColor()),
                Tag = preset,
            };
            swatch.Click += (_, _) =>
            {
                _colour = preset;
                RainbowButton.IsChecked = false;
                Apply();
            };
            Swatches.Children.Add(swatch);
        }
        KeyboardService.StreamFailed += OnStreamFailed;
        Apply();
        Status.Ok(Loc.S("music.off"));
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        KeyboardService.StreamFailed -= OnStreamFailed;
        OnToggle.IsOn = false;
        _visualizer.Dispose();
        base.OnNavigatedFrom(e);
    }

    void OnToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!OnToggle.IsOn)
        {
            KeyboardService.StopStream();
            _visualizer.Stop();
            Status.Ok(Loc.S("music.off_status2"));
            return;
        }
        try
        {
            _visualizer.Start();
            KeyboardService.StartStream(_visualizer.BuildFrame);
            Status.Ok(Loc.S("music.playing"));
        }
        catch (Exception ex)
        {
            OnToggle.IsOn = false;
            Status.Error(ex.Message);
        }
    }

    void Settings_Changed(object sender, object e) => Apply();

    void Apply()
    {
        if (StyleBox is null || GainSlider is null || SmoothSlider is null || RainbowButton is null || Swatches is null)
            return;
        _visualizer.Style = (MusicStyle)Math.Max(0, StyleBox.SelectedIndex);
        _visualizer.Gain = (float)GainSlider.Value;
        _visualizer.Smoothness = (int)SmoothSlider.Value;
        _visualizer.Colourful = RainbowButton.IsChecked == true;
        _visualizer.Colour = _colour;
        var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        foreach (Button swatch in Swatches.Children.OfType<Button>().Where(b => b.Tag is Rgb))
        {
            bool on = !_visualizer.Colourful && (Rgb)swatch.Tag == _colour;
            swatch.BorderBrush = on ? accent : null;
            swatch.BorderThickness = new Thickness(on ? 3 : 0);
        }
    }

    void OnStreamFailed(Exception ex) => DispatcherQueue.TryEnqueue(() =>
    {
        OnToggle.IsOn = false;
        Status.Error(ex.Message);
        KeyboardService.Disconnect();
    });
}
