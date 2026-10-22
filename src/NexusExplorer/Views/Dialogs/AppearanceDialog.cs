using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NexusExplorer.Infrastructure;
namespace NexusExplorer.Views.Dialogs;
internal static class AppearanceDialog
{
    internal static async Task<AppearanceConfig?> ShowAsync(AppearanceConfig current, Func<AppearanceConfig, Task> preview, Action<double> previewOpacity)
    {
        var body = new StackPanel { Margin = new Thickness(8) };
        var themes = new[] { new DialogChoice<UiThemeMode>("跟随 Windows", UiThemeMode.System), new("浅色", UiThemeMode.Light), new("深色", UiThemeMode.Dark) };
        var theme = new ComboBox { ItemsSource = themes, DisplayMemberPath = "Name", SelectedItem = themes.First(x => x.Value == UiThemeService.Mode), Margin = new Thickness(0, 8, 0, 20) };
        body.Children.Add(new TextBlock { Text = "界面主题" }); body.Children.Add(theme);
        var path = current.BackgroundImagePath;
        double selectedOpacity = Math.Clamp(current.BackgroundOpacity, 0, 1);
        TextBlock label = null!;
        bool previewing = false;
        async Task Preview()
        {
            if (previewing) return;
            previewing = true;
            try { await preview(new AppearanceConfig { ThemeMode = ((DialogChoice<UiThemeMode>)theme.SelectedItem).Value, BackgroundImagePath = path, BackgroundOpacity = selectedOpacity }); }
            catch (Exception ex) { label.Text = "图片无法读取：" + ex.Message; }
            finally { previewing = false; }
        }
        label = new TextBlock { Text = path ?? "默认背景", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) };
        body.Children.Add(new TextBlock { Text = "背景图片" }); body.Children.Add(label);
        var row = new WrapPanel();
        var choose = new Button { Content = "选择图片", Margin = new Thickness(0, 0, 8, 0) }; choose.SetResourceReference(FrameworkElement.StyleProperty, "ButtonText");
        choose.Click += async (_, _) => { var picker = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true }; if (picker.ShowDialog() == true) { path = picker.FileName; label.Text = path; await Preview(); } };
        var reset = new Button { Content = "恢复默认背景" }; reset.SetResourceReference(FrameworkElement.StyleProperty, "ButtonText"); reset.Click += async (_, _) => { path = null; label.Text = "默认背景"; await Preview(); };
        row.Children.Add(choose); row.Children.Add(reset); body.Children.Add(row);
        var value = new TextBlock { Margin = new Thickness(0, 20, 0, 8) };
        var opacity = new Slider { Margin = new Thickness(12, 8, 12, 12), Minimum = 0, Maximum = 100, Value = Math.Clamp(current.BackgroundOpacity, 0, 1) * 100, TickFrequency = 1, IsSnapToTickEnabled = true };
        void UpdateLabel() => value.Text = $"背景透明度：{100 - opacity.Value:0}%";
        opacity.ValueChanged += (_, _) => { selectedOpacity = opacity.Value / 100; UpdateLabel(); previewOpacity(selectedOpacity); }; UpdateLabel(); body.Children.Add(value); body.Children.Add(opacity);
        // The slider increases transparency from left to right.
        opacity.IsDirectionReversed = true;
        theme.SelectionChanged += async (_, _) => await Preview();
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DialogSurface view = null!;
        footer.Children.Add(DialogSurface.Action("取消", "ButtonText", null, () => view));
        var apply = new Button { Content = "应用", IsDefault = true }; apply.SetResourceReference(FrameworkElement.StyleProperty, "ButtonPrimary");
        apply.Click += (_, _) => view.Complete(new AppearanceConfig { ThemeMode = ((DialogChoice<UiThemeMode>)theme.SelectedItem).Value, BackgroundImagePath = path, BackgroundOpacity = opacity.Value / 100 });
        footer.Children.Add(apply); view = new DialogSurface("外观设置", body, footer, 480);
        return await MaterialDialogService.ShowAsync(view) as AppearanceConfig;
    }
}
