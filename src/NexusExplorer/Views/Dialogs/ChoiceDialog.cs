using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;

namespace NexusExplorer.Views.Dialogs;

internal sealed record DialogChoice<T>(string Name, T Value);
internal static class ChoiceDialog
{
    public static async Task<(bool Confirmed, T Value)> ShowAsync<T>(string title, string label, IReadOnlyList<DialogChoice<T>> choices, T current)
    {
        var box = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Name", MinHeight = 56, Margin = new Thickness(0, 8, 0, 8) };
        HintAssist.SetHint(box, label);
        box.SelectedItem = choices.FirstOrDefault(c => EqualityComparer<T>.Default.Equals(c.Value, current)) ?? choices.FirstOrDefault();
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DialogSurface view = null!;
        footer.Children.Add(DialogSurface.Action("取消", "ButtonText", null, () => view));
        var accept = new Button { Content = "应用", MinWidth = 80, Margin = new Thickness(8, 0, 0, 8), IsDefault = true };
        accept.SetResourceReference(FrameworkElement.StyleProperty, "ButtonPrimary");
        accept.Click += (_, _) => { if (box.SelectedItem is DialogChoice<T> choice) view.Complete(choice.Value); };
        footer.Children.Add(accept); view = new DialogSurface(title, box, footer, 480);
        var result = await MaterialDialogService.ShowAsync(view);
        return result is T value ? (true, value) : (false, current);
    }
}
