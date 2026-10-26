using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public class ImmersiveControlsTests
{
    [Fact]
    public void IdleThresholdIsOneSecond()
    {
        Assert.False(PlayerPanel.ImmersiveTimeoutElapsed(0, Stopwatch.Frequency * 999 / 1000));
        Assert.True(PlayerPanel.ImmersiveTimeoutElapsed(0, Stopwatch.Frequency));
    }

    [Fact]
    public async Task FullscreenReusesAndRestoresControlsOnRepeatedSwitches()
    {
        using var host = new TestHost();
        await WpfTestHost.RunAsync(() =>
        {
            using var engine = new FakePlaybackEngine();
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, engine);
            var panel = new PlayerPanel();
            panel.Initialize(main);
            var layout = (Grid)panel.FindName("PlayerLayout");
            var bar = (PrismCard)panel.FindName("ControlsBar");
            var margin = bar.Margin;
            var popup = (Popup)typeof(PlayerPanel).GetField("_fullscreenControls", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
            try
            {
                for (var i = 0; i < 3; i++)
                {
                    panel.SetFocusPreview(true);
                    Assert.DoesNotContain(bar, layout.Children.Cast<UIElement>());
                    var viewport = Assert.IsType<Grid>(popup.Child);
                    var motion = Assert.IsType<Grid>(Assert.Single(viewport.Children.Cast<UIElement>()));
                    Assert.Contains(bar, motion.Children.Cast<UIElement>());
                    Assert.True(viewport.ClipToBounds);
                    panel.SetFocusPreview(false);
                    Assert.Null(popup.Child);
                    Assert.False(popup.IsOpen);
                    Assert.Contains(bar, layout.Children.Cast<UIElement>());
                    Assert.Equal(margin, bar.Margin);
                    Assert.True(double.IsNaN(bar.Width));
                    Assert.Equal(1, bar.Opacity);
                    Assert.True(bar.IsHitTestVisible);
                }
            }
            finally { panel.Detach(); }
        });
    }
}
