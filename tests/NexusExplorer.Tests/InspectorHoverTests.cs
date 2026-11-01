using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

public class InspectorHoverTests
{
    [Fact]
    public async Task LeaveStartsDeadlineImmediatelyReentryAndPinCancelIt()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var panel = new PlayerPanel();
            var slot = (FrameworkElement)panel.FindName("InspectorSlot");
            var pin = (ToggleButton)panel.FindName("InspectorPin");
            object? Field(string name) => typeof(PlayerPanel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel);
            void MouseEvent(RoutedEvent routed) => slot.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = routed });
            var timer = (DispatcherTimer)Field("_inspectorTimer")!;
            try
            {
                MouseEvent(Mouse.MouseEnterEvent);
                Assert.Equal(true, Field("_inspectorPendingOpen"));
                MouseEvent(Mouse.MouseLeaveEvent);
                Assert.False(timer.IsEnabled); // Leaving before opening cancels the pending open.
                panel.ShowInspector(true);
                MouseEvent(Mouse.MouseLeaveEvent);
                Assert.True(timer.IsEnabled);
                Assert.Equal(false, Field("_inspectorPendingOpen"));
                Assert.Equal(TimeSpan.FromMilliseconds(150), timer.Interval);
                MouseEvent(Mouse.MouseLeaveEvent);
                Assert.True(timer.IsEnabled);
                Assert.Equal(false, Field("_inspectorPendingOpen"));
                MouseEvent(Mouse.MouseEnterEvent);
                Assert.False(timer.IsEnabled);
                Assert.Null(Field("_inspectorPendingOpen"));
                MouseEvent(Mouse.MouseLeaveEvent);
                pin.IsChecked = true;
                Assert.False(timer.IsEnabled);
                Assert.Null(Field("_inspectorPendingOpen"));
                MouseEvent(Mouse.MouseLeaveEvent);
                Assert.False(timer.IsEnabled);
            }
            finally { timer.Stop(); PrismMotion.StopAll(panel); }
        });
    }
}
