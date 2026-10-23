using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NexusExplorer.Views;
using NexusExplorer.ViewModels;
using NexusExplorer.Views.Dialogs;

namespace NexusExplorer.Tests;
[Collection("NativePlaybackLogging")]
public class MotionTests
{
    [Fact]
    public void RetargetedSpringPreservesVelocityAndSettlesExactly()
    {
        var original = new MotionSegment(320, 0, 0, MotionProfiles.Panel);
        var moving = original.Sample(.12);
        var reversed = new MotionSegment(moving.Value, 320, moving.Velocity, MotionProfiles.Panel);
        Assert.Equal(moving.Value, reversed.Sample(0).Value, 8);
        Assert.Equal(moving.Velocity, reversed.Sample(0).Velocity, 8);
        Assert.Equal(new MotionSample(320, 0), reversed.Sample(MotionProfiles.Panel.Seconds));
        Assert.Contains(Enumerable.Range(0,700), t=>original.Sample(t/1000d).Value < 0);
        var fade = new MotionSegment(.5, 1, -20, MotionProfiles.Fade, 0, 1);
        Assert.All(Enumerable.Range(0,700), t=>Assert.InRange(fade.Sample(t/1000d).Value,0,1));
    }
    [Fact]
    public async Task ReplacementKeepsPositionCancelsOldCompletionAndReleasesClocks()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var transform = new TranslateTransform(); var border = new Border { Width=100, Height=100, RenderTransform=transform };
            var window = new Window { Content=border, Width=200, Height=200, ShowInTaskbar=false };
            try
            {
                window.Show(); var oldCompleted=0; var completed=0;
                PrismMotion.Animate(transform, TranslateTransform.XProperty,100, MotionProfiles.Panel,()=>oldCompleted++);
                await Task.Delay(130);
                var before=transform.X;
                PrismMotion.Animate(transform, TranslateTransform.XProperty,0, MotionProfiles.Panel,()=>completed++);
                Assert.InRange(Math.Abs(transform.X-before),0,.01);
                await Task.Delay(850);
                Assert.Equal(0,transform.X); Assert.Equal(0,oldCompleted); Assert.Equal(1,completed);
                Assert.False(transform.HasAnimatedProperties);
                PrismMotion.Animate(transform, TranslateTransform.XProperty,80, MotionProfiles.Panel);
                await Task.Delay(80); PrismMotion.Stop(transform,TranslateTransform.XProperty,finish:true);
                Assert.Equal(80,transform.X); Assert.False(transform.HasAnimatedProperties);
                var scale=new ScaleTransform(2,2); border.RenderTransform=scale;
                PressFeedback.SetIsEnabled(border,true);
                border.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent});
                var group=Assert.IsType<TransformGroup>(border.RenderTransform); Assert.Same(scale,group.Children[0]);
            }
            finally { window.Close(); }
        });
    }
    [Fact]
    public async Task RapidDrawerInspectorAndNoticeReversalKeepLatestState()
    {
        using var host=new TestHost();
        await WpfTestHost.RunAsync(async () =>
        {
            using var engine=new FakePlaybackEngine();
            var main=new MainViewModel(host.Categories,host.Files,host.Organization,engine);
            var window=new MainWindow(main,host.Categories,host.Files,host.Organization,host.RecycleBin,engine){ShowInTaskbar=false};
            try
            {
                window.Show(); await Task.Delay(800);
                var toggle=(Button)window.FindName("DrawerToggleButton");
                var library=(FrameworkElement)window.FindName("LibraryCard");
                var slot=(FrameworkElement)window.FindName("LibrarySlot");
                for(var i=0;i<5;i++){toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(85);}
                await Task.Delay(800);
                Assert.Equal(0,slot.Width); Assert.Equal(0,library.Opacity); Assert.False(library.IsHitTestVisible);
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(800);
                Assert.Equal(320,slot.Width); Assert.Equal(1,library.Opacity); Assert.True(library.IsHitTestVisible);
                var player=(PlayerPanel)window.FindName("PlayerArea");
                var body=(FrameworkElement)player.FindName("InspectorBody");
                player.ShowInspector(true); await Task.Delay(100); player.ShowInspector(false);
                Assert.Equal(Visibility.Visible,body.Visibility);
                await Task.Delay(100); player.ShowInspector(true); await Task.Delay(800);
                Assert.Equal(260,((FrameworkElement)player.FindName("InspectorSlot")).Width);
                Assert.Equal(Visibility.Visible,body.Visibility); Assert.True(body.IsHitTestVisible);
                MaterialDialogService.Notice("first"); await Task.Delay(1450);
                var notice=(FrameworkElement)window.FindName("NoticeCard");
                var shift=(TranslateTransform)notice.RenderTransform; var before=shift.Y;
                MaterialDialogService.Notice("latest");
                Assert.InRange(Math.Abs(before-shift.Y),0,.01);
                await Task.Delay(900);
                Assert.Equal("latest",((TextBlock)window.FindName("NoticeText")).Text);
                Assert.Equal(Visibility.Visible,notice.Visibility);
                await Task.Delay(900); Assert.Equal(Visibility.Collapsed,notice.Visibility);
            }
            finally{window.PrepareForVerificationExit();window.Close();}
        });
    }

}
