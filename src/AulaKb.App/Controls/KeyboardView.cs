using AulaKb.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace AulaKb.App.Controls;

/// <summary>
/// The F87 Pro's key layout drawn as Fluent controls: theme-aware keycaps with a hairline
/// stroke and rounded corners, no product styling. Layout (Turkish Q ISO, English ISO,
/// English ANSI) comes from <see cref="VisualLayout.Current"/>. The LED colour of a key tints
/// its face and stroke, and can be set every frame.
/// </summary>
public sealed class KeyboardView : UserControl
{
    sealed class Cap
    {
        public required CapKind Kind;
        public readonly SolidColorBrush Face = new();
        public readonly SolidColorBrush Stroke = new();
        public readonly SolidColorBrush Text = new();
        public readonly List<FrameworkElement> Shapes = [];
        public Rgb Light = Rgb.Black;
        public bool Hover;
    }

    readonly Dictionary<int, Cap> _caps = [];
    readonly SolidColorBrush _barFill = new();
    Rgb _barLight = Rgb.Black;
    KeyInfo? _selected;
    bool _dark = true;

    public KeyboardView()
    {
        Layout = VisualLayout.Current;
        var keys = VisualLayout.Build(Layout);
        Keys = keys.Select(k => k.Key).ToList();
        Content = Build(keys);
        ActualThemeChanged += (_, _) => Repaint();
        Loaded += (_, _) => Repaint();
    }

    public LayoutKind Layout { get; }

    /// <summary>Keys of the active layout (ISO adds the "&lt;" and "," keys and drops "\").</summary>
    public IReadOnlyList<KeyInfo> Keys { get; }

    public event Action<KeyInfo>? KeyClicked;

    UIElement Build(IReadOnlyList<VisualKey> keys)
    {
        const double pad = 4;
        const double inset = 1.2; // gap between caps
        double minX = keys.SelectMany(k => k.Rects).Min(r => r.X);
        double minY = keys.SelectMany(k => k.Rects).Min(r => r.Y);
        double maxX = keys.SelectMany(k => k.Rects).Max(r => r.X + r.W);
        double maxY = keys.SelectMany(k => k.Rects).Max(r => r.Y + r.H);
        var canvas = new Canvas { Width = maxX - minX + 2 * pad, Height = maxY - minY + 2 * pad };
        double ox = pad - minX, oy = pad - minY;

        foreach (var vk in keys)
        {
            var cap = new Cap { Kind = vk.Cap };
            var key = vk.Key;
            FrameworkElement shape;
            KeyRect labelRect;

            if (vk.Rects.Count == 1)
            {
                var r = vk.Rects[0];
                shape = new Border
                {
                    Width = r.W - 2 * inset,
                    Height = r.H - 2 * inset,
                    CornerRadius = new CornerRadius(3),
                    Background = cap.Face,
                    BorderBrush = cap.Stroke,
                    BorderThickness = new Thickness(0.6),
                };
                Canvas.SetLeft(shape, r.X + ox + inset);
                Canvas.SetTop(shape, r.Y + oy + inset);
                labelRect = r;
            }
            else
            {
                // ISO Enter: wide top part and a narrow part reaching down (inverted L), as one outline.
                var top = vk.Rects[0];
                var low = vk.Rects[1];
                double l = top.X + inset, t = top.Y + inset, rgt = top.X + top.W - inset;
                double tb = top.Y + top.H - inset, ll = low.X + inset, lb = low.Y + low.H - inset;
                var figure = new PathFigure { StartPoint = new Point(l + ox, t + oy), IsClosed = true };
                foreach (var p in new[] { (rgt, t), (rgt, lb), (ll, lb), (ll, tb), (l, tb) })
                    figure.Segments.Add(new LineSegment { Point = new Point(p.Item1 + ox, p.Item2 + oy) });
                var geometry = new PathGeometry();
                geometry.Figures.Add(figure);
                shape = new Microsoft.UI.Xaml.Shapes.Path
                {
                    Data = geometry,
                    Fill = cap.Face,
                    Stroke = cap.Stroke,
                    StrokeThickness = 0.6,
                    StrokeLineJoin = PenLineJoin.Round,
                };
                labelRect = low with { Y = top.Y, H = low.Y + low.H - top.Y };
            }

            ToolTipService.SetToolTip(shape, key.Label);
            shape.Tapped += (_, _) => KeyClicked?.Invoke(key);
            shape.PointerEntered += (_, _) => { cap.Hover = true; Paint(cap); };
            shape.PointerExited += (_, _) => { cap.Hover = false; Paint(cap); };
            canvas.Children.Add(shape);
            cap.Shapes.Add(shape);

            var legend = Legend(vk.Label, cap.Text);
            legend.Width = labelRect.W - 2 * inset;
            legend.Height = labelRect.H - 2 * inset;
            legend.IsHitTestVisible = false;
            Canvas.SetLeft(legend, labelRect.X + ox + inset);
            Canvas.SetTop(legend, labelRect.Y + oy + inset);
            canvas.Children.Add(legend);

            _caps[key.Id] = cap;
        }

        AddLightBar(canvas, keys, ox, oy);
        return new Viewbox { Child = canvas, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
    }

    static Grid Legend(string label, Brush brush)
    {
        bool single = !label.Contains('\n') && label.Length <= 2;
        var text = new TextBlock
        {
            Text = label,
            FontSize = single ? 8.5 : label.Contains('\n') ? 6.4 : 6.2,
            FontWeight = single ? Microsoft.UI.Text.FontWeights.Normal : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = brush,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 7.4,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return new Grid { Children = { text } };
    }

    void AddLightBar(Canvas canvas, IReadOnlyList<VisualKey> keys, double ox, double oy)
    {
        // The F87 Pro's light strip between the navigation block and the arrow keys.
        var del = keys.FirstOrDefault(k => k.Key.Led == 86)?.Key;
        var pgdn = keys.FirstOrDefault(k => k.Key.Led == 98)?.Key;
        var up = keys.FirstOrDefault(k => k.Key.Led == 94)?.Key;
        if (del is null || pgdn is null || up is null)
            return;
        var bar = new Border
        {
            Width = pgdn.X + pgdn.W - del.X - 4,
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = _barFill,
        };
        Canvas.SetLeft(bar, del.X + ox + 2);
        Canvas.SetTop(bar, (del.Y + del.H + up.Y) / 2 - 2 + oy);
        canvas.Children.Add(bar);
    }

    public KeyInfo? Selected
    {
        get => _selected;
        set
        {
            var old = _selected;
            _selected = value;
            if (old is not null && _caps.TryGetValue(old.Id, out var o))
                Paint(o);
            if (value is not null && _caps.TryGetValue(value.Id, out var n))
                Paint(n);
        }
    }

    /// <summary>Sets the LED colour of a key (black = unlit). Cheap enough to call every frame.</summary>
    public void SetKeyLight(KeyInfo key, Rgb light)
    {
        if (!_caps.TryGetValue(key.Id, out var cap) || cap.Light == light)
            return;
        cap.Light = light;
        Paint(cap);
    }

    public void SetBarLight(Rgb light)
    {
        _barLight = light;
        PaintBar();
    }

    /// <summary>Highlights a key with a colour (null = unlit).</summary>
    public void SetKeyColor(KeyInfo key, Color? color) =>
        SetKeyLight(key, color is Color c ? new Rgb(c.R, c.G, c.B) : Rgb.Black);

    public void SetToolTip(KeyInfo key, string text)
    {
        if (_caps.TryGetValue(key.Id, out var cap))
            foreach (var s in cap.Shapes)
                ToolTipService.SetToolTip(s, text);
    }

    // ---- painting (theme aware) ----

    void Repaint()
    {
        _dark = ActualTheme != ElementTheme.Light;
        foreach (var cap in _caps.Values)
            Paint(cap);
        PaintBar();
    }

    (Color Face, Color Stroke, Color Text) Base(CapKind kind) => (_dark, kind) switch
    {
        // Close to Fluent control fills on Mica: regular keys like a button, modifiers one step darker.
        (true, CapKind.Light) => (C(0x34, 0x34, 0x37), C(0x45, 0x45, 0x49), C(0xF2, 0xF2, 0xF2)),
        (true, _) => (C(0x27, 0x27, 0x2A), C(0x3B, 0x3B, 0x3F), C(0xC8, 0xC8, 0xCC)),
        (false, CapKind.Light) => (C(0xFD, 0xFD, 0xFD), C(0xD9, 0xD9, 0xDC), C(0x1B, 0x1B, 0x1B)),
        (false, _) => (C(0xEC, 0xEC, 0xEE), C(0xD2, 0xD2, 0xD6), C(0x4A, 0x4A, 0x4F)),
    };

    void Paint(Cap cap)
    {
        var (face, stroke, text) = Base(cap.Kind);
        var l = cap.Light;
        double k = Math.Max(l.R, Math.Max(l.G, l.B)) / 255.0;
        var led = C(l.R, l.G, l.B);
        if (k > 0.02)
        {
            // Light fills the cap like an accent fill; stronger light, stronger colour.
            face = Mix(face, Normalize(l), _dark ? 0.70 * k : 0.55 * k);
            stroke = Mix(stroke, led, 0.9);
            double luma = 0.299 * face.R + 0.587 * face.G + 0.114 * face.B;
            text = luma > 150 ? C(0x10, 0x10, 0x10) : C(0xFA, 0xFA, 0xFA);
        }
        if (cap.Hover)
            face = Mix(face, _dark ? C(255, 255, 255) : C(0, 0, 0), 0.08);
        cap.Face.Color = face;
        cap.Text.Color = text;
        bool selected = _selected is not null && _caps.TryGetValue(_selected.Id, out var s) && ReferenceEquals(s, cap);
        cap.Stroke.Color = selected ? AccentColor() : stroke;
        foreach (var shape in cap.Shapes)
        {
            double thickness = selected ? 1.6 : 0.6;
            if (shape is Border b)
                b.BorderThickness = new Thickness(thickness);
            else if (shape is Microsoft.UI.Xaml.Shapes.Path p)
                p.StrokeThickness = thickness;
        }
    }

    void PaintBar()
    {
        var idle = _dark ? C(0x3A, 0x3A, 0x3E) : C(0xD6, 0xD6, 0xDA);
        _barFill.Color = _barLight == Rgb.Black ? idle : Mix(idle, Normalize(_barLight), 0.9);
    }

    static Color AccentColor() =>
        Application.Current.Resources.TryGetValue("SystemAccentColorLight2", out var v) && v is Color c ? c : C(0x60, 0xCD, 0xFF);

    /// <summary>Full-saturation version of an LED colour, so dim colours still read as their hue.</summary>
    static Color Normalize(Rgb l)
    {
        double m = Math.Max(1.0, Math.Max((double)l.R, Math.Max(l.G, (double)l.B)));
        return C((byte)(l.R * 255 / m), (byte)(l.G * 255 / m), (byte)(l.B * 255 / m));
    }

    static Color Mix(Color a, Color b, double t) => C(
        (byte)Math.Clamp(a.R + (b.R - a.R) * t, 0, 255),
        (byte)Math.Clamp(a.G + (b.G - a.G) * t, 0, 255),
        (byte)Math.Clamp(a.B + (b.B - a.B) * t, 0, 255));

    static Color C(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);

    static Color C(int r, int g, int b) => Color.FromArgb(255, (byte)r, (byte)g, (byte)b);
}

static class ColorExt
{
    public static Color ToColor(this Rgb c) => Color.FromArgb(255, c.R, c.G, c.B);

    public static Rgb ToRgb(this Color c) => new(c.R, c.G, c.B);
}
