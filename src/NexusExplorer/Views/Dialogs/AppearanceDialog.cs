using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NexusExplorer.Infrastructure;
namespace NexusExplorer.Views.Dialogs;
internal static class AppearanceDialog
{
    internal static async Task<AppearanceConfig?> ShowAsync(AppearanceConfig current)
    {
        var body = new StackPanel();
        var themes = new[] { new DialogChoice<UiThemeMode>("跟随 Windows", UiThemeMode.System), new("浅色", UiThemeMode.Light), new("深色", UiThemeMode.Dark) };
        var theme = new ComboBox { ItemsSource = themes, DisplayMemberPath = "Name", SelectedItem = themes.First(x => x.Value == UiThemeService.Mode), Margin = new Thickness(0, 8, 0, 20) };
        body.Children.Add(new TextBlock { Text = "界面主题" }); body.Children.Add(theme);
        var path = current.BackgroundImagePath;
        var label = new TextBlock { Text = path ?? "默认背景", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) };
        body.Children.Add(new TextBlock { Text = "背景图片" }); body.Children.Add(label);
        var row = new WrapPanel();
        var choose = new Button { Content = "选择图片", Margin = new Thickness(0, 0, 8, 0) }; choose.SetResourceReference(FrameworkElement.StyleProperty, "ButtonText");
        choose.Click += (_, _) => { var picker = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp", CheckFileExists = true }; if (picker.ShowDialog() == true) { path = picker.FileName; label.Text = path; } };
        var reset = new Button { Content = "恢复默认背景" }; reset.SetResourceReference(FrameworkElement.StyleProperty, "ButtonText"); reset.Click += (_, _) => { path = null; label.Text = "默认背景"; };
        row.Children.Add(choose); row.Children.Add(reset); body.Children.Add(row);
        var value = new TextBlock { Margin = new Thickness(0, 20, 0, 8) };
        var opacity = new Slider { Minimum = 0, Maximum = 100, Value = Math.Clamp(current.BackgroundOpacity, 0, 1) * 100, TickFrequency = 1, IsSnapToTickEnabled = true };
        void UpdateLabel() => value.Text = $"背景透明度：{100 - opacity.Value:0}%";
        opacity.ValueChanged += (_, _) => UpdateLabel(); UpdateLabel(); body.Children.Add(value); body.Children.Add(opacity);
        // The slider increases transparency from left to right.
        opacity.IsDirectionReversed = true;
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DialogSurface view = null!;
        footer.Children.Add(DialogSurface.Action("取消", "ButtonText", null, () => view));
        var apply = new Button { Content = "应用", IsDefault = true }; apply.SetResourceReference(FrameworkElement.StyleProperty, "ButtonPrimary");
        apply.Click += (_, _) => view.Complete(new AppearanceConfig { ThemeMode = ((DialogChoice<UiThemeMode>)theme.SelectedItem).Value, BackgroundImagePath = path, BackgroundOpacity = opacity.Value / 100 });
        footer.Children.Add(apply); view = new DialogSurface("外观设置", body, footer, 480);
        return await MaterialDialogService.ShowAsync(view) as AppearanceConfig;
    }
}
