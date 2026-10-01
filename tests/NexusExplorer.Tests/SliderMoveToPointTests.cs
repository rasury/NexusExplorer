using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace NexusExplorer.Tests;

/// <summary>
/// 验证 WPF Slider 的 MoveToPoint 机制:点击轨道时 Slider 类处理会把
/// PreviewMouseLeftButtonDown 标记为已处理 → 普通实例处理器(XAML 挂载)
/// 不会触发;必须 AddHandler(handledEventsToo: true) 才能收到。
/// 这是"进度条点击跳转失效"的根因,测试需真实布局(Track 必须存在)。
/// </summary>
public class SliderMoveToPointTests
{
    [Fact]
    public void MoveToPoint_MarksPreviewDownHandled_PlainHandlerSkipped()
    {
        bool plainFired = false;
        bool handledTooFired = false;
        bool endedHandled = false;
        bool trackFound = false;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                var slider = new Slider
                {
                    Minimum = 0, Maximum = 1, Value = 0.3,
                    IsMoveToPointEnabled = true,
                    Width = 200, Height = 20
                };

                var window = new Window
                {
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    AllowsTransparency = true,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -5000, Top = -5000,
                    Width = 240, Height = 60,
                    Content = slider,
                    Visibility = Visibility.Hidden
                };
                window.Show();
                slider.UpdateLayout();
                Pump();

                var track = FindVisualChild<Track>(slider);
                trackFound = track is not null;

                slider.PreviewMouseLeftButtonDown += (_, _) => plainFired = true;
                slider.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
                    new MouseButtonEventHandler((_, _) => handledTooFired = true), true);
                slider.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
                    new MouseButtonEventHandler((_, e) => endedHandled = e.Handled), true);

                slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent
                });
                Pump();
                window.Close();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
        Assert.True(trackFound, "Track 必须存在(布局后模板生效)");
        Assert.False(plainFired,
            $"普通订阅不应触发(Slider MoveToPoint 类处理标记了事件已处理); endedHandled={endedHandled}");
        Assert.True(handledTooFired, "handledEventsToo 注册必须触发");
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background,
            (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var result = FindVisualChild<T>(child);
            if (result is not null) return result;
        }
        return null;
    }
}
