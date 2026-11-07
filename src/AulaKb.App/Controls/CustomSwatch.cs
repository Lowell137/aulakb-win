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
