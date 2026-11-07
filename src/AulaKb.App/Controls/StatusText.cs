using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AulaKb.App.Controls;

/// <summary>Small status line for page headers: icon + text, red on errors.</summary>
public sealed class StatusText : StackPanel
{
    readonly FontIcon _icon = new() { FontSize = 14, Glyph = "" };
    readonly TextBlock _text = new() { TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420 };

    public StatusText()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 6;
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Right;
        Children.Add(_icon);
        Children.Add(_text);
        Set("", "", false);
    }

    public void Busy(string text) => Set("", text, false);

    public void Ok(string text) => Set("", text, false);

    public void Pending(string text) => Set("", text, false);

    public void Error(string text) => Set("", text, true);

    void Set(string glyph, string text, bool error)
    {
        _icon.Glyph = glyph;
        _text.Text = text;
        ToolTipService.SetToolTip(this, string.IsNullOrEmpty(text) ? null : text);
        var brush = (Brush)Application.Current.Resources[error ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush"];
        _icon.Foreground = brush;
        _text.Foreground = brush;
    }
}
