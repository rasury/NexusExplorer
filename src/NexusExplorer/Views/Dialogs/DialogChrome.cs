using System.Windows;

namespace NexusExplorer.Views.Dialogs;

/// <summary>
/// 对话框统一外观。窗口行为属性(边框样式、不可调大小、随内容自适应、居中所有者、
/// 不出现在任务栏)在代码中设置——放在 ResourceDictionary 样式里会在样式首次
/// 延迟实例化时抛 XamlParseException(Setter.Property 无法解析)。
/// </summary>
internal static class DialogChrome
{
    public static void Apply(Window window)
    {
        window.WindowStyle = WindowStyle.ToolWindow;
        window.ResizeMode = ResizeMode.NoResize;
        window.SizeToContent = SizeToContent.WidthAndHeight;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.ShowInTaskbar = false;
    }
}
