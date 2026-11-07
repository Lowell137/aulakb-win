using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace AulaKb.App.Controls;

/// <summary>
/// A row like the Windows Settings app: icon, title and description, and a control on the right.
/// When the row gets narrow the control moves under the text.
/// </summary>
[ContentProperty(Name = nameof(Action))]
public sealed class SettingsRow : UserControl
{
    const double NarrowWidth = 620;

    readonly Grid _grid = new() { ColumnSpacing = 16, RowSpacing = 10 };
    readonly FontIcon _icon = new() { FontSize = 18, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _title = new();
    readonly TextBlock _description = new();
    readonly ContentPresenter _action = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
    bool _narrow;

    public SettingsRow()
    {
        _title.Style = (Style)Application.Current.Resources["RowTitleStyle"];
        _description.Style = (Style)Application.Current.Resources["RowDescriptionStyle"];
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _title, _description } };
        Grid.SetColumn(text, 1);
        _grid.Children.Add(_icon);
        _grid.Children.Add(text);
        _grid.Children.Add(_action);
        Place(narrow: false);

        Content = new Border { Style = (Style)Application.Current.Resources["RowStyle"], Child = _grid };
        SizeChanged += (_, e) =>
        {
            bool narrow = e.NewSize.Width < NarrowWidth;
            if (narrow != _narrow)
                Place(narrow);
        };
    }

    public string Glyph
    {
        get => _icon.Glyph;
        set => _icon.Glyph = value;
    }

    public string Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    public string Description
    {
        get => _description.Text;
        set
        {
            _description.Text = value;
            _description.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public object? Action
    {
        get => _action.Content;
        set => _action.Content = value;
    }

    void Place(bool narrow)
    {
        _narrow = narrow;
        Grid.SetRow(_action, narrow ? 1 : 0);
        Grid.SetColumn(_action, narrow ? 1 : 2);
        Grid.SetColumnSpan(_action, narrow ? 2 : 1);
        _action.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }
}
