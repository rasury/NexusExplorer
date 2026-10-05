using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using NexusExplorer.Models;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;
using NexusExplorer.Views.Dialogs;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public class MaterialFeedbackTests
{
    [Fact]
    public async Task VolumeThumbAndIconShareTheSameVerticalCenter()
    {
        using var host=new TestHost();
        await WpfTestHost.RunAsync(()=>
        {
            var main=new MainViewModel(host.Categories,host.Files,host.Organization,new FakePlaybackEngine());
            var panel=new PlayerPanel();panel.Initialize(main);main.Player.Kind=MediaKind.Audio;main.Player.ResetZoom();
            panel.Measure(new Size(600,480));panel.Arrange(new Rect(0,0,600,480));panel.UpdateLayout();
            var slider=(Slider)panel.FindName("VolumeSlider");var icon=(FrameworkElement)panel.FindName("VolumeIcon");
            var track=(Track)slider.Template.FindName("PART_Track",slider);var thumb=track.Thumb;
            var iconCenter=icon.TranslatePoint(new Point(icon.ActualWidth/2,icon.ActualHeight/2),panel).Y;
            var thumbCenter=thumb.TranslatePoint(new Point(thumb.ActualWidth/2,thumb.ActualHeight/2),panel).Y;
            Assert.InRange(Math.Abs(iconCenter-thumbCenter),0,1);
            foreach(var value in new[]{0d,50d,100d}){slider.Value=value;panel.UpdateLayout();Assert.Equal(iconCenter,thumb.TranslatePoint(new Point(thumb.ActualWidth/2,thumb.ActualHeight/2),panel).Y);}
            panel.Detach();
        });
    }

    [Fact]
    public async Task ManyChildrenKeepNavigationToOneRowAndPickerSearchFindsHiddenCategories()
    {
        using var host=new TestHost();
        await WpfTestHost.RunAsync(()=>
        {
            var main=new MainViewModel(host.Categories,host.Files,host.Organization,new FakePlaybackEngine());
            var bar=new NavigationBar();bar.Initialize(main);
            main.Navigation.Children=new ObservableCollection<Category>(Enumerable.Range(1,80).Select(i=>new Category{Id=i,Name=$"子分类 {i:D2}"}));
            bar.Measure(new Size(960,400));bar.Arrange(new Rect(0,0,960,bar.DesiredSize.Height));bar.UpdateLayout();
            Assert.Equal(3,((ItemsControl)bar.FindName("ChildrenHost")).Items.Count);
            var scroll=(ScrollViewer)bar.FindName("NavigationScroll");Assert.True(scroll.ActualHeight<=64);
            var more=(Button)bar.FindName("MoreChildrenButton");Assert.Equal(Visibility.Visible,more.Visibility);Assert.Contains("80",(string)more.Content);
            var view=CategoryPickerDialog.Create(main.Navigation.Children.ToList());
            var body=(StackPanel)((StackPanel)((ScrollViewer)view.Children[0]).Content).Children[1];
            var search=body.Children.OfType<TextBox>().Single();var list=body.Children.OfType<ListBox>().Single();
            search.Text="78";Assert.Single(list.Items);Assert.Equal(78,((Category)list.Items[0]).Id);
            object? result=null;view.Complete=value=>result=value;list.SelectedIndex=0;
            ((WrapPanel)view.Children[1]).Children.OfType<Button>().Single(b=>(string)b.Content=="进入分类").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(78,((Category)result!).Id);
        });
    }

    [Fact]
    public async Task VideoTrackDialogHasItsOwnMouseSurfaceAndKeepsVideoVisible()
    {
        using var host=new TestHost();var category=await host.Categories.CreateAsync("video",null);
        var path=SyntheticMedia.WriteAvi(Path.Combine(host.RootDir,"click.avi"),audio:true,silentAudio:true);
        var file=await host.Files.AddAsync(path,category.Id);
        await WpfTestHost.RunAsync(async()=>
        {
            using var engine=new MediaPlayerService(){HardwareDecoding=false};
            var main=new MainViewModel(host.Categories,host.Files,host.Organization,engine);
            var window=new MainWindow(main,host.Categories,host.Files,host.Organization,host.RecycleBin,engine){ShowInTaskbar=false,ShowActivated=false};
            window.Show();await Dispatcher.Yield(DispatcherPriority.Loaded);
            try
            {
                await main.SelectFileAsync(file);await BoundedDialogTests.Until(()=>engine.Snapshot.Position.TotalMilliseconds>100);
                var player=(PlayerPanel)window.FindName("PlayerArea");var video=(FrameworkElement)player.FindName("VideoView");
                var clickSurface=(FrameworkElement)player.FindName("VideoClickSurface");var foreground=Window.GetWindow(clickSurface);
                Assert.NotNull(foreground);Assert.NotSame(window,foreground);Assert.True(foreground.IsVisible);
                var choices=engine.NativePlayer!.AudioTrackDescription.Select(t=>new DialogChoice<int>(t.Name,t.Id)).ToList();
                var result=ChoiceDialog.ShowAsync("音轨","音轨",choices,engine.SelectedAudioTrack);
                var root=(DialogHost)window.FindName("RootDialog");await BoundedDialogTests.Until(()=>root.IsOpen && root.DialogContent is DialogSurface s && s.IsLoaded);
                await Task.Delay(350);Assert.False(foreground.IsVisible);Assert.Equal(Visibility.Visible,video.Visibility);Assert.True(engine.Snapshot.IsPlaying);
                var view=(DialogSurface)root.DialogContent!;
                var box=Descendants<ComboBox>(view).Single();var apply=Descendants<Button>(view).Single(b=>b.Content is string text && text=="应用");
                var popupSource=(HwndSource)PresentationSource.FromVisual(apply);
                Assert.NotEqual(new WindowInteropHelper(window).Handle,popupSource.Handle);
                Assert.True(IsWindowVisible(popupSource.Handle));Assert.True(IsWindowEnabled(popupSource.Handle));
                var point=apply.TranslatePoint(new Point(apply.ActualWidth/2,apply.ActualHeight/2),popupSource.RootVisual as UIElement);
                var hit=((UIElement)popupSource.RootVisual).InputHitTest(point) as DependencyObject;
                Assert.Same(apply,CategoryFilePanel.FindAncestor<Button>(hit));
                box.IsDropDownOpen=true;await Dispatcher.Yield(DispatcherPriority.Render);Assert.True(box.IsDropDownOpen);
                box.SelectedItem=choices.Single(c=>c.Value==-1);box.IsDropDownOpen=false;
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var picked=await result;
                Assert.True(picked.Confirmed);Assert.Equal(-1,picked.Value);Assert.True(foreground.IsVisible);Assert.Equal(Visibility.Visible,video.Visibility);
            }
            finally{MaterialDialogService.CancelAll();await engine.StopAndReleaseAsync();window.PrepareForVerificationExit();window.Close();}
        });
    }
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool IsWindowEnabled(IntPtr hwnd);
    private static IEnumerable<T>Descendants<T>(DependencyObject root)where T:DependencyObject
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T found)yield return found;foreach(var nested in Descendants<T>(child))yield return nested;}}
}
