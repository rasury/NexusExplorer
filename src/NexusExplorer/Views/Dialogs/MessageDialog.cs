using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
namespace NexusExplorer.Views.Dialogs;
internal enum DialogButtons { Ok, OkCancel, YesNo, YesNoCancel }
internal enum DialogAnswer { Ok, Cancel, Yes, No }
internal enum DialogSeverity { Information, Question, Warning, Error }
internal static class MessageDialog
{
    public static async Task<DialogAnswer> ShowAsync(string message, string title, DialogButtons buttons = DialogButtons.Ok, DialogSeverity severity = DialogSeverity.Information)
        => await MaterialDialogService.ShowAsync(Create(message, title, buttons, severity)) is DialogAnswer result ? result : DialogAnswer.Cancel;
    public static async Task<bool> ConfirmAsync(string message, string title = "确认")
        => await ShowAsync(message, title, DialogButtons.YesNo, DialogSeverity.Question) == DialogAnswer.Yes;
    internal static DialogSurface Create(string message, string title, DialogButtons buttons, DialogSeverity severity)
    {
        var body = new StackPanel();
        var icon = new PackIcon { Kind = severity == DialogSeverity.Question ? PackIconKind.HelpCircleOutline : severity is DialogSeverity.Warning or DialogSeverity.Error ? PackIconKind.AlertCircleOutline : PackIconKind.InformationOutline, Width = 32, Height = 32, Margin = new Thickness(0, 0, 0, 16) };
        icon.SetResourceReference(Control.ForegroundProperty, severity is DialogSeverity.Warning or DialogSeverity.Error ? "BrushDanger" : "BrushAccentBlue");
        body.Children.Add(icon);
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }; text.SetResourceReference(FrameworkElement.StyleProperty, "TextPrimary"); body.Children.Add(text);
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DialogSurface view = null!;
        void Add(string label, DialogAnswer result, bool primary)
        { var button = DialogSurface.Action(label, primary ? "ButtonPrimary" : "ButtonText", result, () => view); button.IsDefault = primary; footer.Children.Add(button); }
        if (buttons is DialogButtons.YesNo or DialogButtons.YesNoCancel) { Add("是", DialogAnswer.Yes, true); Add("否", DialogAnswer.No, false); }
        else Add("确定", DialogAnswer.Ok, true);
        if (buttons is DialogButtons.OkCancel or DialogButtons.YesNoCancel) Add("取消", DialogAnswer.Cancel, false);
        view = new DialogSurface(title, body, footer); return view;
    }
}
