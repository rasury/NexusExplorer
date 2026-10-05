using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NexusExplorer.Views.Dialogs;

/// <summary>A bounded DialogHost view: scroll title/body together; keep actions outside scrolling.</summary>
internal sealed class DialogSurface : Grid
{
    public Action<object?> Complete { get; set; } = _ => { };
    public DialogSurface(string title, UIElement body, FrameworkElement actions, double width = 560)
    {
        Width = width; Margin = new Thickness(24);
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var content = new StackPanel();
        var heading = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        heading.SetResourceReference(StyleProperty, "DialogTitle");
        content.Children.Add(heading); content.Children.Add(body);
        Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        actions.Margin = new Thickness(0, 24, 0, 0); SetRow(actions, 1); Children.Add(actions);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Complete(null); } };
    }
    public void Constrain(double width, double height)
    { MaxWidth = Math.Max(1, width - 96); MaxHeight = Math.Max(1, height - 96); }
    public static Button Action(string text, string style, object? result, Func<DialogSurface> view)
    {
        var button = new Button { Content = text, MinWidth = 80, Margin = new Thickness(8, 0, 0, 8) };
        button.SetResourceReference(StyleProperty, style); button.Click += (_, _) => view().Complete(result); return button;
    }
}
