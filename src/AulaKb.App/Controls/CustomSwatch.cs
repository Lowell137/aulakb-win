using AulaKb.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AulaKb.App.Controls;

/// <summary>
/// Colour row feedback shared by the Lighting and Music pages: the active preset gets an
/// accent ring; a colour that is not a preset shows on the "+" (custom) button instead, with
/// the ring, so a picked custom colour is visibly selected.
/// </summary>
static class CustomSwatch
{
    /// <summary>
    /// Saved custom colours: shown as swatches before the "+" button (right click removes one),
    /// and an "Add to colours" button under the colour picker. Stored in settings.json, so the
    /// Lighting and Music pages share them.
    /// </summary>
    public static void AttachSaved(Panel swatches, Button custom, ColorPicker picker, Action<Rgb> pick)
    {
        foreach (var hex in AppSettings.Load().CustomColours)
            if (TryParse(hex, out var c))
                Insert(swatches, custom, c, pick);

        var add = new Button { Content = Loc.S("colour.save"), HorizontalAlignment = HorizontalAlignment.Stretch };
        add.Click += (_, _) =>
        {
            var c = picker.Color.ToRgb();
            var settings = AppSettings.Load();
            string hex = $"{c.R:X2}{c.G:X2}{c.B:X2}";
            if (!settings.CustomColours.Contains(hex))
            {
                settings.CustomColours.Add(hex);
                settings.Save();
                Insert(swatches, custom, c, pick);
            }
            pick(c);
            custom.Flyout?.Hide();
        };
        if (custom.Flyout is Flyout flyout)
        {
            flyout.Content = null;
            flyout.Content = new StackPanel { Spacing = 8, Children = { picker, add } };
        }
    }

    static void Insert(Panel swatches, Button custom, Rgb c, Action<Rgb> pick)
    {
        var swatch = new Button
        {
            Width = custom.Width,
            Height = custom.Height,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(custom.Width / 2),
            Background = new SolidColorBrush(c.ToColor()),
            Tag = c,
        };
        ToolTipService.SetToolTip(swatch, $"#{c.R:X2}{c.G:X2}{c.B:X2}");
        swatch.Click += (_, _) => pick(c);
        var remove = new MenuFlyoutItem { Text = Loc.S("colour.remove"), Icon = new FontIcon { Glyph = "" } };
        remove.Click += (_, _) =>
        {
            var settings = AppSettings.Load();
            settings.CustomColours.Remove($"{c.R:X2}{c.G:X2}{c.B:X2}");
            settings.Save();
            swatches.Children.Remove(swatch);
        };
        swatch.ContextFlyout = new MenuFlyout { Items = { remove } };
        swatches.Children.Insert(swatches.Children.IndexOf(custom), swatch);
    }

    static bool TryParse(string hex, out Rgb c)
    {
        c = Rgb.Black;
        if (hex.Length != 6 || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint v))
            return false;
        c = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static void Highlight(Panel swatches, Button custom, Rgb? active)
    {
        var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        bool matched = false;
        foreach (Button swatch in swatches.Children.OfType<Button>().Where(b => b.Tag is Rgb))
        {
            bool on = active is Rgb a && (Rgb)swatch.Tag == a;
            matched |= on;
            swatch.BorderBrush = on ? accent : null;
            swatch.BorderThickness = new Thickness(on ? 3 : 0);
        }

        bool customOn = active is Rgb && !matched;
        if (customOn)
        {
            var c = active!.Value;
            custom.Background = new SolidColorBrush(c.ToColor());
            double luma = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            custom.Foreground = new SolidColorBrush(luma > 140 ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White);
            custom.BorderBrush = accent;
            custom.BorderThickness = new Thickness(3);
        }
        else
        {
            custom.ClearValue(Control.BackgroundProperty);
            custom.ClearValue(Control.ForegroundProperty);
            custom.ClearValue(Control.BorderBrushProperty);
            custom.ClearValue(Control.BorderThicknessProperty);
        }
    }
}
