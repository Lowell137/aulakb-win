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
    readonly KeyboardView _board = new() { IsHitTestVisible = false };
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _preview;
    volatile byte[]? _lastFrame;
    Rgb _colour = new(0, 255, 0);

    /// <summary>Shows on screen what was last sent to the keyboard.</summary>
    void PaintPreview()
    {
        var frame = _lastFrame;
        foreach (var key in _board.Keys)
        {
            int led = key.Led;
            var rgb = frame is not null && led * 3 + 2 < frame.Length
                ? new Rgb(frame[led * 3], frame[led * 3 + 1], frame[led * 3 + 2])
                : Rgb.Black;
            _board.SetKeyLight(key, rgb);
        }
    }

    /// <summary>Selftest: runs every mode on synthetic audio for a while and shows the result.</summary>
    internal async Task SelfTestAsync(Func<string, Task> snapshot)
    {
        var rnd = new Random(7);
        // Optional real level sequence (one entry per vendor frame, ~47 ms): {"l":[17 floats],"b":0|1}.
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AulaKb");
        List<(float[] Levels, int Beat)>? sequence = null;
        string seqPath = Path.Combine(dir, "levels.json");
        if (File.Exists(seqPath))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(seqPath));
            sequence = doc.RootElement.EnumerateArray()
                .Select(e => (e.GetProperty("l").EnumerateArray().Select(v => v.GetSingle()).ToArray(), e.GetProperty("b").GetInt32()))
                .ToList();
        }
        // row of each LED (0 = F row .. 5 = Ctrl row), for the per-row statistics
        var keys = _board.Keys.Where(k => k.Led < AulaKeyboard.LedCount).ToList();
        int minY = keys.Min(k => k.Y);
        var rowOf = keys.ToDictionary(k => k.Led, k => Math.Clamp((int)Math.Round((k.Y - minY) / 32.0 - (k.Y > minY ? 0.25 : 0)), 0, 5));
        int[] perRow = Enumerable.Range(0, 6).Select(r => rowOf.Values.Count(v => v == r)).ToArray();
        var report = new System.Text.StringBuilder("mode: fraction of each row lit, rows 0 (F) .. 5 (Ctrl), then Space\n");

        foreach (var style in Enum.GetValues<MusicStyle>())
        {
            StyleBox.SelectedIndex = (int)style;
            var lit = new double[6];
            int spaceLit = 0, frames = Math.Min(sequence?.Count ?? 60, 200);
            for (int f = 0; f < frames; f++)
            {
                var (levels, beat) = sequence is not null
                    ? sequence[f]
                    : (Enumerable.Range(0, 17).Select(b => (float)Math.Clamp(0.85 - b * 0.035 + rnd.NextDouble() * 0.25 * Math.Sin(f * 0.3 + b), 0, 1)).ToArray(), f % 8 == 0 ? 1 : 0);
                _visualizer.Inject(levels, beat);
                var frame = _visualizer.BuildFrame();
                _lastFrame = frame;
                foreach (var (led, row) in rowOf)
                    if (frame[led * 3] > 0 || frame[led * 3 + 1] > 0 || frame[led * 3 + 2] > 0)
                    {
                        lit[row]++;
                        if (led == 35)
                            spaceLit++;
                    }
                await Task.Delay(sequence is null ? 30 : 47);
            }
            report.AppendLine($"{(int)style + 1} {string.Join(" ", Enumerable.Range(0, 6).Select(r => (lit[r] / frames / perRow[r]).ToString("0.00")))}  space={(double)spaceLit / frames:0.00}");
            PaintPreview();
            await Task.Delay(100);
            await snapshot($"music-{(int)style + 1}-{style}");
        }
        await File.WriteAllTextAsync(Path.Combine(dir, "selftest", "music-rows.txt"), report.ToString());
    }

    byte[] NextFrame()
    {
        var frame = _visualizer.BuildFrame();
        _lastFrame = frame;
        return frame;
    }

    public MusicPage()
    {
        InitializeComponent();
        TitleText.Text = Loc.S("music.title");
        OnRow.Title = Loc.S("music.row_title");
        OnRow.Description = Loc.S("music.row_desc");
        OnToggle.OnContent = Loc.S("music.on");
        OnToggle.OffContent = Loc.S("music.off");
        ViewRow.Title = Loc.S("music.style");
        for (int i = 1; i <= Enum.GetValues<MusicStyle>().Length; i++)
            StyleBox.Items.Add(Loc.S($"music.mode{i}"));
        StyleBox.SelectedIndex = 0;
        BoardHost.Child = _board;
        _preview = DispatcherQueue.CreateTimer();
        _preview.Interval = TimeSpan.FromMilliseconds(50);
        _preview.Tick += (_, _) => PaintPreview();
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
            Swatches.Children.Insert(Swatches.Children.Count - 1, swatch); // before the "+" button
        }
        ToolTipService.SetToolTip(CustomButton, Loc.S("lighting.custom"));
        KeyboardService.StreamFailed += OnStreamFailed;
        Apply();
        Status.Ok(Loc.S("music.off"));
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        KeyboardService.StreamFailed -= OnStreamFailed;
        _preview.Stop();
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
            _preview.Stop();
            _lastFrame = null;
            PaintPreview();
            Status.Ok(Loc.S("music.off_status2"));
            return;
        }
        try
        {
            _visualizer.Start();
            KeyboardService.StartStream(NextFrame);
            _preview.Start();
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
        if (StyleBox is null || GainSlider is null || SmoothSlider is null || RainbowButton is null || Swatches is null || CustomButton is null)
            return;
        _visualizer.Style = (MusicStyle)Math.Max(0, StyleBox.SelectedIndex);
        _visualizer.Gain = (float)GainSlider.Value;
        _visualizer.Smoothness = (int)SmoothSlider.Value;
        _visualizer.Colourful = RainbowButton.IsChecked == true;
        _visualizer.Colour = _colour;
        CustomSwatch.Highlight(Swatches, CustomButton, _visualizer.Colourful ? null : _colour);
    }

    void CustomPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        _colour = args.NewColor.ToRgb();
        RainbowButton.IsChecked = false;
        Apply();
    }

    void OnStreamFailed(Exception ex) => DispatcherQueue.TryEnqueue(() =>
    {
        _preview.Stop();
        OnToggle.IsOn = false;
        Status.Error(ex.Message);
        KeyboardService.Disconnect();
    });
}
