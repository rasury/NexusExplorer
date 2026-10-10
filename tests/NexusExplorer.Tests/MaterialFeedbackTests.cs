using NexusExplorer.Infrastructure.Playback.Mpv;
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
using NexusExplorer.Infrastructure;
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
    public async Task RootNavigationButtonDisplaysItsLabelAndReturnsToRoot()
    {
        using var host = new TestHost();
        var category = await host.Categories.CreateAsync("父分类", null);
        await host.Categories.CreateAsync("子分类", category.Id);
        await WpfTestHost.RunAsync(async () =>
        {
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, new FakePlaybackEngine());
            var bar = new NavigationBar(); bar.Initialize(main);
            await main.Navigation.NavigateToAsync(category);
            bar.Measure(new Size(960, 400)); bar.Arrange(new Rect(0, 0, 960, bar.DesiredSize.Height)); bar.UpdateLayout();
            var button = (Button)bar.FindName("RootBackButton");
            Assert.Equal(Visibility.Visible, button.Visibility);
            Assert.Contains(Descendants<TextBlock>(button), text => text.Text.Contains("顶层") && text.ActualWidth > 0);
            Assert.Contains(Descendants<TextBlock>((ItemsControl)bar.FindName("ChildrenHost")), text => text.Text == "子分类");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await BoundedDialogTests.Until(() => main.Navigation.IsAtRoot && main.Navigation.Children.Any(c => c.Id == category.Id));
            Assert.Empty(main.Navigation.Breadcrumb);
            Assert.Null(main.Navigation.SelectedCategory);
            Assert.Equal(Visibility.Collapsed, button.Visibility);
        });
    }

    [Fact]
    public async Task CategoryListStartsAfterSingleStatusRowWithoutToolbarOrRootStrip()
    {
        using var host = new TestHost();
        await WpfTestHost.RunAsync(() =>
        {
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, new FakePlaybackEngine());
            var panel = new CategoryFilePanel(); panel.Initialize(main, host.RecycleBin);
            panel.Measure(new Size(400, 800)); panel.Arrange(new Rect(0, 0, 400, 800)); panel.UpdateLayout();
            Assert.Null(panel.FindName("RootDropZone"));
            var status = (TextBlock)panel.FindName("OperationStatus");
            var tree = (TreeView)panel.FindName("CategoryTree");
            var treeTop = tree.TranslatePoint(new Point(), panel).Y;
            Assert.Equal(TextWrapping.NoWrap, status.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, status.TextTrimming);
            Assert.InRange(treeTop, 40, 41);
            Assert.True(tree.ActualHeight > 350);
        });
    }

    [Theory]
    [InlineData(UiThemeMode.Light)]
    [InlineData(UiThemeMode.Dark)]
    public async Task HoverVolumePopupAdjustsVolumeVerticallyAndShowsAllThreeLevels(UiThemeMode theme)
    {
        using var host=new TestHost();
        await WpfTestHost.RunAsync(async()=>
        {
            UiThemeService.Apply(theme);
            var main=new MainViewModel(host.Categories,host.Files,host.Organization,new FakePlaybackEngine());
            var panel=new PlayerPanel();panel.Initialize(main);main.Player.Kind=MediaKind.Audio;main.Player.ResetZoom();
            var window=new Window{Content=panel,Width=600,Height=480,ShowActivated=false,ShowInTaskbar=false,Left=SystemParameters.WorkArea.Left+80,Top=SystemParameters.WorkArea.Top+80};
            try
            {
                window.Show();window.UpdateLayout();
                var volume=(PopupBox)panel.FindName("VolumePopup");var slider=(Slider)panel.FindName("VolumeSlider");var icon=(PackIcon)panel.FindName("VolumeIcon");
                Assert.False(volume.IsPopupOpen);Assert.Equal(PopupBoxPopupMode.MouseOver,volume.PopupMode);
                volume.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=UIElement.MouseEnterEvent});
                await BoundedDialogTests.Until(()=>slider.IsVisible && slider.ActualHeight>100);
                Assert.True(volume.IsPopupOpen);Assert.Equal(Orientation.Vertical,slider.Orientation);
                var source=(HwndSource)PresentationSource.FromVisual(slider);
                Assert.NotEqual(new WindowInteropHelper(window).Handle,source.Handle);
                var point=slider.TranslatePoint(new Point(slider.ActualWidth/2,slider.ActualHeight/2),(UIElement)source.RootVisual);
                Assert.Same(slider,CategoryFilePanel.FindAncestor<Slider>(((UIElement)source.RootVisual).InputHitTest(point) as DependencyObject));
                var track=(Track)slider.Template.FindName("PART_Track",slider);
                double? lowY=null;
                foreach(var (value,expected) in new[]{(0,PackIconKind.VolumeMute),(30,PackIconKind.VolumeLow),(31,PackIconKind.VolumeMedium),(60,PackIconKind.VolumeMedium),(61,PackIconKind.VolumeHigh),(100,PackIconKind.VolumeHigh)})
                {
                    slider.Value=value;slider.UpdateLayout();
                    Assert.Equal(value,main.Player.Volume);Assert.Equal(expected,icon.Kind);
                    Assert.Equal(value.ToString(),((TextBlock)panel.FindName("VolumeValueText")).Text);
                    var iconCenter=icon.PointToScreen(new Point(icon.ActualWidth/2,icon.ActualHeight/2)).X;
                    var thumbCenter=track.Thumb.PointToScreen(new Point(track.Thumb.ActualWidth/2,track.Thumb.ActualHeight/2)).X;
                    Assert.InRange(Math.Abs(iconCenter-thumbCenter),0,VisualTreeHelper.GetDpi(icon).DpiScaleX);
                    if(value==0)lowY=track.Thumb.TranslatePoint(new Point(),slider).Y;
                    if(value==100)Assert.True(track.Thumb.TranslatePoint(new Point(),slider).Y<lowY);
                    Assert.True(volume.IsPopupOpen);
                }
                SaveVolumePreview((FrameworkElement)source.RootVisual,volume,$"volume-aligned-{theme}");
                volume.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=UIElement.MouseLeaveEvent});
                Assert.True(volume.IsPopupOpen);
                await BoundedDialogTests.Until(()=>!volume.IsPopupOpen);
            }
            finally{panel.Detach();window.Close();UiThemeService.Apply(UiThemeMode.Light);}
        });
    }

    [Fact]
    public async Task ClickingVolumeIconTogglesZeroAndRemembersSliderVolumeWithoutDoubleCommands()
    {
        using var host = new TestHost();
        await WpfTestHost.RunAsync(async () =>
        {
            var engine = new FakePlaybackEngine();
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, engine);
            var panel = new PlayerPanel(); panel.Initialize(main); main.Player.Kind = MediaKind.Audio; main.Player.ResetZoom();
            var window = new Window { Content=panel, Width=600, Height=480, ShowActivated=false, ShowInTaskbar=false, WindowStartupLocation=WindowStartupLocation.Manual, Left=-5000, Top=-5000 };
            try
            {
                window.Show(); window.UpdateLayout();
                var popup=(PopupBox)panel.FindName("VolumePopup"); var slider=(Slider)panel.FindName("VolumeSlider"); var icon=(PackIcon)panel.FindName("VolumeIcon");
                var button=(ToggleButton)popup.Template.FindName(PopupBox.TogglePartName,popup);
                slider.Value=57; Assert.Equal(57,main.Player.Volume);
                popup.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=UIElement.MouseEnterEvent});
                await BoundedDialogTests.Until(()=>popup.IsPopupOpen && slider.IsVisible);
                button.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonUpEvent});
                Assert.Equal(0,main.Player.Volume); Assert.Equal(0,slider.Value); Assert.Equal(PackIconKind.VolumeMute,icon.Kind);
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.Equal(57,main.Player.Volume); Assert.Equal(57,slider.Value); Assert.Equal(PackIconKind.VolumeMedium,icon.Kind);
                slider.Value=0; button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Assert.Equal(57,main.Player.Volume);
                slider.Value=23; button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.Equal(23,main.Player.Volume); Assert.Equal(PackIconKind.VolumeLow,icon.Kind);
                Assert.Equal(new[]{57,0,57,0,57,23,0,23},engine.Volumes);
                Assert.Equal(PopupBoxPopupMode.MouseOver,popup.PopupMode);
            }
            finally { panel.Detach(); window.Close(); }
        });
    }

    [Fact]
    public async Task VolumeThumbReleaseDoesNotLeavePopupCapturingTheRestOfTheWindow()
    {
        using var host=new TestHost();
        await WpfTestHost.RunAsync(async()=>
        {
            var main=new MainViewModel(host.Categories,host.Files,host.Organization,new FakePlaybackEngine());
            var panel=new PlayerPanel(); panel.Initialize(main); main.Player.Kind=MediaKind.Audio; main.Player.ResetZoom();
            var window=new Window{Content=panel,Width=600,Height=480,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=SystemParameters.WorkArea.Left+80,Top=SystemParameters.WorkArea.Top+80};
            try
            {
                window.Show(); window.UpdateLayout();
                var popup=(PopupBox)panel.FindName("VolumePopup"); var slider=(Slider)panel.FindName("VolumeSlider");
                popup.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=UIElement.MouseEnterEvent});
                await BoundedDialogTests.Until(()=>popup.IsPopupOpen && slider.IsVisible);
                var thumb=((Track)slider.Template.FindName("PART_Track",slider)).Thumb;
                Assert.True(thumb.CaptureMouse()); Assert.Same(thumb,System.Windows.Input.Mouse.Captured);
                slider.Value=43;
                thumb.ReleaseMouseCapture(); await Dispatcher.Yield(DispatcherPriority.Input);
                Assert.NotSame(popup,System.Windows.Input.Mouse.Captured);
                popup.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=UIElement.MouseLeaveEvent});
                Assert.True(popup.IsPopupOpen);
                await BoundedDialogTests.Until(()=>!popup.IsPopupOpen);
                Assert.Null(System.Windows.Input.Mouse.Captured); Assert.Equal(43,main.Player.Volume);
                // Cleanup must not steal a capture legitimately owned by an unrelated control.
                var other=(Button)panel.FindName("PlayPauseButton"); Assert.True(other.CaptureMouse());
                popup.IsPopupOpen=true; popup.IsPopupOpen=false;
                Assert.Same(other,System.Windows.Input.Mouse.Captured); other.ReleaseMouseCapture();
            }
            finally { System.Windows.Input.Mouse.Capture(null); panel.Detach(); window.Close(); }
        });
    }

    [Fact]
    public async Task VolumeHoverCorridorAllowsDiagonalApproachReentryAndThumbDrag()
    {
        using var host=new TestHost();
        await WpfTestHost.RunAsync(async()=>
        {
            var main=new MainViewModel(host.Categories,host.Files,host.Organization,new FakePlaybackEngine());
            var panel=new PlayerPanel(); panel.Initialize(main); main.Player.Kind=MediaKind.Audio; main.Player.ResetZoom();
            var window=new Window{Content=panel,Width=600,Height=480,ShowActivated=false,ShowInTaskbar=false,Left=SystemParameters.WorkArea.Left+80,Top=SystemParameters.WorkArea.Top+80};
            try
            {
                window.Show(); window.UpdateLayout();
                var popup=(VolumeHoverPopupBox)panel.FindName("VolumePopup"); var slider=(Slider)panel.FindName("VolumeSlider");
                popup.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=UIElement.MouseEnterEvent});
                await BoundedDialogTests.Until(()=>slider.IsVisible && slider.ActualHeight>100);
                var bounds=popup.GetHoverBounds();
                Assert.True(bounds.Width>=popup.ActualWidth+24);
                Assert.True(bounds.Top<-100, $"Hover bounds: {bounds}; slider: {slider.PointToScreen(new Point())}; toggle: {popup.PointToScreen(new Point())}");
                Assert.True(bounds.Bottom>=popup.ActualHeight);
                // Both sides, including the gap immediately above the icon,
                // remain open without taking capture from the rest of the app.
                foreach(var x in new[]{bounds.Left+1,bounds.Right-1})
                {
                    popup.ProcessHover(new Point(x,-1),false,1000);
                    popup.ProcessHover(new Point(x,bounds.Top+1),false,2000);
                    Assert.True(popup.IsPopupOpen);
                }
                var outside=new Point(bounds.Right+30,bounds.Bottom+30);
                popup.ProcessHover(outside,false,3000);
                popup.ProcessHover(outside,false,3249); Assert.True(popup.IsPopupOpen);
                popup.ProcessHover(new Point(20,20),false,3250); // Reentry cancels pending closure.
                popup.ProcessHover(outside,false,4000);
                popup.ProcessHover(outside,true,5000); Assert.True(popup.IsPopupOpen); // Captured thumb can drag outside.
                popup.ProcessHover(outside,false,6000);
                popup.ProcessHover(outside,false,6250); Assert.False(popup.IsPopupOpen);
                Assert.Null(System.Windows.Input.Mouse.Captured);
                popup.IsPopupOpen=true; panel.Detach(); Assert.False(popup.IsPopupOpen);
                await Task.Delay(350); Assert.False(popup.IsPopupOpen);
            }
            finally { panel.Detach(); window.Close(); }
        });
    }

    [Theory]
    [InlineData(UiThemeMode.Light)]
    [InlineData(UiThemeMode.Dark)]
    public async Task HardwareDecodeToggleClearlyIdentifiesOnAndOff(UiThemeMode theme)
    {
        using var host=new TestHost();
        await WpfTestHost.RunAsync(()=>
        {
            UiThemeService.Apply(theme);
            var main=new MainViewModel(host.Categories,host.Files,host.Organization,new FakePlaybackEngine());
            var panel=new PlayerPanel();panel.Initialize(main);main.Player.Kind=MediaKind.Video;main.Player.ResetZoom();
            try
            {
                var toggle=(ToggleButton)panel.FindName("HardwareCheck");var badge=(Border)panel.FindName("HardwareEnabledBadge");
                foreach(var enabled in new[]{false,true,false})
                {
                    toggle.IsChecked=enabled;
                    panel.Measure(new Size(552,480));panel.Arrange(new Rect(0,0,552,480));panel.UpdateLayout();
                    Assert.Contains(enabled?"已开启":"已关闭",(string)toggle.ToolTip);
                    Assert.Equal(toggle.ToolTip,System.Windows.Automation.AutomationProperties.GetName(toggle));
                    Assert.Equal(enabled?Visibility.Visible:Visibility.Collapsed,badge.Visibility);
                    Assert.Equal(enabled?((SolidColorBrush)panel.FindResource("BrushAccentBlueLight")).Color:Colors.Transparent,((SolidColorBrush)toggle.Background).Color);
                    Assert.Equal(((SolidColorBrush)panel.FindResource(enabled?"BrushOnPrimaryContainer":"BrushSecondaryText")).Color,((SolidColorBrush)toggle.Foreground).Color);
                    SavePreview((FrameworkElement)panel.FindName("ControlsBar"),$"hardware-{theme}-{enabled}");
                }
            }
            finally{panel.Detach();UiThemeService.Apply(UiThemeMode.Light);}
        });
    }

    [Theory]
    [InlineData(MediaKind.Video,552)]
    [InlineData(MediaKind.Audio,552)]
    [InlineData(MediaKind.Video,800)]
    [InlineData(MediaKind.Image,552)]
    public async Task PlayerUsesOneRowAndIconOnlyButtons(MediaKind kind,int width)
    {
        using var host=new TestHost();
        await WpfTestHost.RunAsync(()=>
        {
            var main=new MainViewModel(host.Categories,host.Files,host.Organization,new FakePlaybackEngine());
            var panel=new PlayerPanel();panel.Initialize(main);main.Player.Kind=kind;main.Player.ResetZoom();
            try
            {
                panel.Measure(new Size(width,480));panel.Arrange(new Rect(0,0,width,480));panel.UpdateLayout();
                var card=(FrameworkElement)panel.FindName("ControlsBar");Assert.InRange(card.ActualHeight,40,72);
                foreach(var button in Descendants<Button>(card).Where(b=>b.ActualWidth>0).ToList())
                {Assert.IsType<PackIcon>(button.Content);Assert.NotNull(button.ToolTip);button.ApplyTemplate();Assert.True(Descendants<Ripple>(button).Any(),$"缺少水波纹：{button.ToolTip}");}
                if(kind!=MediaKind.Image)
                {
                    var buttons=(FrameworkElement)panel.FindName("MediaButtonsRow");var progress=(FrameworkElement)panel.FindName("ProgressRow");var settings=(FrameworkElement)panel.FindName("MediaSettingsRow");
                    double Center(FrameworkElement e)=>e.TranslatePoint(new Point(0,e.ActualHeight/2),panel).Y;
                    Assert.InRange(Math.Abs(Center(buttons)-Center(progress)),0,1);Assert.Equal(Center(buttons),Center(settings));
                    Assert.True(((Slider)panel.FindName("ProgressSlider")).ActualWidth>=80);
                    Assert.True(settings.TranslatePoint(new Point(settings.ActualWidth,0),panel).X<=width);
                    Assert.Equal(kind==MediaKind.Video?Visibility.Visible:Visibility.Collapsed,((FrameworkElement)panel.FindName("HardwareCheck")).Visibility);
                }
                SavePreview(card,$"{kind}-{width}");
            }
            finally{panel.Detach();}
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
            using var engine=new MpvPlaybackEngine(){HardwareDecoding=false};
            var main=new MainViewModel(host.Categories,host.Files,host.Organization,engine);
            var window=new MainWindow(main,host.Categories,host.Files,host.Organization,host.RecycleBin,engine){ShowInTaskbar=false,ShowActivated=false};
            window.Show();await Dispatcher.Yield(DispatcherPriority.Loaded);
            try
            {
                await main.SelectFileAsync(file);await BoundedDialogTests.Until(()=>engine.Snapshot.Position.TotalMilliseconds>100);
                var player=(PlayerPanel)window.FindName("PlayerArea");var video=(FrameworkElement)player.FindName("VideoView");
                var videoHost=(MpvVideoHost)video;
                Assert.NotEqual(IntPtr.Zero,videoHost.Handle);
                var volume=(PopupBox)player.FindName("VolumePopup");volume.IsPopupOpen=true;
                var slider=(Slider)player.FindName("VolumeSlider");await BoundedDialogTests.Until(()=>slider.IsVisible && slider.ActualHeight>100);
                var volumeSource=(HwndSource)PresentationSource.FromVisual(slider);
                var volumePoint=slider.TranslatePoint(new Point(slider.ActualWidth/2,slider.ActualHeight/2),(UIElement)volumeSource.RootVisual);
                Assert.Same(slider,CategoryFilePanel.FindAncestor<Slider>(((UIElement)volumeSource.RootVisual).InputHitTest(volumePoint) as DependencyObject));
                slider.Value=50;Assert.Equal(50,main.Player.Volume);Assert.True(engine.Snapshot.IsPlaying);
                var choices=engine.AudioTracks.Select(t=>new DialogChoice<long>(t.Name,t.Id)).Prepend(new DialogChoice<long>("禁用音轨",-1)).ToList();
                var result=ChoiceDialog.ShowAsync("音轨","音轨",choices,engine.SelectedAudioTrack);
                var root=(DialogHost)window.FindName("RootDialog");await BoundedDialogTests.Until(()=>root.IsOpen && root.DialogContent is DialogSurface s && s.IsLoaded);
                await Task.Delay(350);Assert.False(volume.IsPopupOpen);Assert.Equal(Visibility.Visible,video.Visibility);Assert.True(IsWindowVisible(videoHost.Handle));Assert.True(engine.Snapshot.IsPlaying);
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
                Assert.True(picked.Confirmed);Assert.Equal(-1,picked.Value);Assert.Equal(Visibility.Visible,video.Visibility);
            }
            finally{MaterialDialogService.CancelAll();await engine.StopAndReleaseAsync();window.PrepareForVerificationExit();window.Close();}
        });
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VideoRenameDialogKeepsPreviewPlayingUntilConfirmation(bool confirm)
    {
        using var host=new TestHost();var category=await host.Categories.CreateAsync("video",null);
        var path=SyntheticMedia.WriteAvi(Path.Combine(host.RootDir,"pending.avi"),audio:true,silentAudio:true);
        var file=await host.Files.AddAsync(path,category.Id);
        await WpfTestHost.RunAsync(async()=>
        {
            using var engine=new MpvPlaybackEngine(){HardwareDecoding=false};
            var main=new MainViewModel(host.Categories,host.Files,host.Organization,engine);
            var window=new MainWindow(main,host.Categories,host.Files,host.Organization,host.RecycleBin,engine){ShowInTaskbar=false,ShowActivated=false};
            window.Show();await Dispatcher.Yield(DispatcherPriority.Loaded);
            try
            {
                await main.SelectCategoryAsync(category);await main.SelectFileAsync(file);
                await BoundedDialogTests.Until(()=>engine.Snapshot.Position.TotalMilliseconds>100 && engine.AudioOutput=="wasapi");
                var video=(MpvVideoHost)((PlayerPanel)window.FindName("PlayerArea")).FindName("VideoView");
                var token=engine.CurrentToken;var position=engine.Snapshot.Position;
                var rename=main.FileList.RenameFileAsync(file);
                DialogHost? root=null;
                await BoundedDialogTests.Until(()=>(root=System.Windows.Application.Current.Windows.OfType<Window>().Where(w=>ReferenceEquals(w.Owner,window)).Select(w=>w.Content).OfType<DialogHost>().FirstOrDefault()) is {IsOpen:true,DialogContent:DialogSurface {IsLoaded:true}});
                var view=(DialogSurface)root!.DialogContent!;var box=Descendants<TextBox>(view).Single();
                await BoundedDialogTests.Until(()=>box.SelectedText=="pending");
                await Task.Delay(350);
                Assert.Equal(Visibility.Visible,video.Visibility);Assert.True(IsWindowVisible(video.Handle));
                Assert.True(engine.Snapshot.IsPlaying);Assert.True(engine.Snapshot.Position>position);
                Assert.Equal(token,engine.CurrentToken);Assert.Equal(path,(await host.Files.GetByIdAsync(file.Id))!.AbsolutePath);
                Assert.Equal("gpu-next",engine.VideoOutput);
                var button=Descendants<Button>(view).Single(b=>b.Content is string label && label==(confirm?"确定":"取消"));
                var source=(HwndSource)PresentationSource.FromVisual(button);
                Assert.NotEqual(new WindowInteropHelper(window).Handle,source.Handle);
                var point=button.TranslatePoint(new Point(button.ActualWidth/2,button.ActualHeight/2),(UIElement)source.RootVisual);
                Assert.Same(button,CategoryFilePanel.FindAncestor<Button>(((UIElement)source.RootVisual).InputHitTest(point) as DependencyObject));
                if(confirm)box.Text="renamed.avi";
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await rename;
                if(confirm)
                {
                    Assert.False(engine.Snapshot.IsPlaying);Assert.Null(main.CurrentFile);
                    Assert.False(File.Exists(path));Assert.True(File.Exists(Path.Combine(host.RootDir,"renamed.avi")));
                    Assert.Equal("renamed.avi",(await host.Files.GetByIdAsync(file.Id))!.FileName);
                }
                else
                {
                    Assert.True(engine.Snapshot.IsPlaying);Assert.Equal(token,engine.CurrentToken);
                    Assert.Equal(Visibility.Visible,video.Visibility);Assert.True(IsWindowVisible(video.Handle));
                    Assert.True(File.Exists(path));Assert.Equal("pending.avi",(await host.Files.GetByIdAsync(file.Id))!.FileName);
                }
            }
            finally{MaterialDialogService.CancelAll();await engine.StopAndReleaseAsync();window.PrepareForVerificationExit();window.Close();}
        });
    }

    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool IsWindowVisible(IntPtr hwnd);
    private static void SavePreview(FrameworkElement element,string name)
    {
        var directory=Path.GetFullPath("../../../../../artifacts/player-controls-preview",AppContext.BaseDirectory);Directory.CreateDirectory(directory);
        var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())drawing.DrawRectangle(new VisualBrush(element),null,new Rect(0,0,element.ActualWidth,element.ActualHeight));
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output=File.Create(Path.Combine(directory,name+".png"));encoder.Save(output);
    }
    private static void SaveVolumePreview(FrameworkElement popup,FrameworkElement toggle,string name)
    {
        var start=popup.PointToScreen(new Point());var end=toggle.PointToScreen(new Point());
        var transform=PresentationSource.FromVisual(toggle).CompositionTarget.TransformFromDevice;
        var offset=transform.Transform(end-start);var height=Math.Max(popup.ActualHeight,offset.Y+toggle.ActualHeight);
        var combined=new DrawingVisual();using(var drawing=combined.RenderOpen())
        {
            drawing.DrawRectangle(new VisualBrush(popup),null,new Rect(0,0,popup.ActualWidth,popup.ActualHeight));
            drawing.DrawRectangle(new VisualBrush(toggle),null,new Rect(offset.X,offset.Y,toggle.ActualWidth,toggle.ActualHeight));
        }
        var image=new System.Windows.Controls.Image{Source=Render(combined,(int)popup.ActualWidth,(int)Math.Ceiling(height))};
        image.Measure(new Size(popup.ActualWidth,height));image.Arrange(new Rect(0,0,popup.ActualWidth,height));SavePreview(image,name);
    }
    private static System.Windows.Media.Imaging.RenderTargetBitmap Render(Visual visual,int width,int height)
    {var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);return bitmap;}
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool IsWindowEnabled(IntPtr hwnd);
    private static IEnumerable<T>Descendants<T>(DependencyObject root)where T:DependencyObject
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T found)yield return found;foreach(var nested in Descendants<T>(child))yield return nested;}}
}
