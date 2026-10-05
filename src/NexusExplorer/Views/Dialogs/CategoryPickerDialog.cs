using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
using NexusExplorer.Models;

namespace NexusExplorer.Views.Dialogs;
internal static class CategoryPickerDialog
{
    public static async Task<Category?> ShowAsync(IReadOnlyList<Category> categories)
        => await MaterialDialogService.ShowAsync(Create(categories)) as Category;

    internal static DialogSurface Create(IReadOnlyList<Category> categories)
    {
        var body = new StackPanel();
        var search = new TextBox { Margin = new Thickness(0, 8, 0, 16) };
        search.SetResourceReference(FrameworkElement.StyleProperty, "TextBoxStandard");
        HintAssist.SetHint(search, "搜索子分类");
        var list = new ListBox { MaxHeight = 280, DisplayMemberPath = "Name", ItemsSource = categories };
        VirtualizingPanel.SetIsVirtualizing(list, true);
        var empty = new TextBlock { Text = "没有匹配的子分类", Visibility = Visibility.Collapsed };
        empty.SetResourceReference(FrameworkElement.StyleProperty, "TextSecondary");
        search.TextChanged += (_, _) =>
        {
            var matches = categories.Where(c => c.Name.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            list.ItemsSource = matches; empty.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        };
        body.Children.Add(search); body.Children.Add(list); body.Children.Add(empty);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DialogSurface view = null!;
        actions.Children.Add(DialogSurface.Action("取消", "ButtonText", null, () => view));
        var select = new Button { Content = "进入分类", MinWidth = 80, Margin = new Thickness(8, 0, 0, 8), IsDefault = true, IsEnabled = false };
        select.SetResourceReference(FrameworkElement.StyleProperty, "ButtonPrimary");
        list.SelectionChanged += (_, _) => select.IsEnabled = list.SelectedItem is Category;
        select.Click += (_, _) => { if (list.SelectedItem is Category category) view.Complete(category); };
        list.MouseDoubleClick += (_, e) =>
        {
            if (CategoryFilePanel.FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is Category category)
                view.Complete(category);
        };
        actions.Children.Add(select);
        view = new DialogSurface($"子分类（{categories.Count} 个）", body, actions);
        return view;
    }
}
