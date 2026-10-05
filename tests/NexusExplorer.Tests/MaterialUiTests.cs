using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using NexusExplorer.Infrastructure;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;
using NexusExplorer.Views.Dialogs;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public class MaterialUiTests
{
    [Fact]
    public async Task StartupAlertUsesAsyncDialogHostAndPreservesApplicationLifetime()
    {
        await WpfTestHost.RunAsync(async()=>
        {
            var app=System.Windows.Application.Current;var previous=app.MainWindow;var mode=app.ShutdownMode;
            app.MainWindow=null!;app.ShutdownMode=ShutdownMode.OnLastWindowClose;MaterialDialogService.BeginSession();
            try
            {
                var task=MessageDialog.ShowAsync("数据恢复报告","启动提示");
                await BoundedDialogTests.Until(()=>app.Windows.OfType<Window>().Any(w=>w.Content is DialogHost h && h.IsOpen));
                var host=(DialogHost)app.Windows.OfType<Window>().Single(w=>w.Content is DialogHost).Content;
                ((DialogSurface)host.DialogContent!).Complete(DialogAnswer.Ok);
                Assert.Equal(DialogAnswer.Ok,await task);Assert.Null(app.MainWindow);
                Assert.Equal(ShutdownMode.OnLastWindowClose,app.ShutdownMode);Assert.False(app.Dispatcher.HasShutdownStarted);
            }
            finally{app.MainWindow=previous;app.ShutdownMode=mode;}
        });
    }

    [Theory]
    [InlineData(UiThemeMode.Light,1280,880)]
    [InlineData(UiThemeMode.Dark,1280,880)]
    [InlineData(UiThemeMode.Light,960,720)]
    [InlineData(UiThemeMode.Dark,960,720)]
    public async Task CompleteMaterialWindowFitsViewportAndRenders(UiThemeMode mode,int width,int height)
    {
        using var host=new TestHost();
        var audio=await host.Categories.CreateAsync("音频",null);
        await host.Categories.CreateAsync("ASMR 收藏",audio.Id);
        var images=await host.Categories.CreateAsync("图片",null);
        var videos=await host.Categories.CreateAsync("视频",null);
        foreach(var category in new[]{audio,images,videos})await host.Categories.PinAsync(category.Id);
        for(var i=0;i<8;i++)await host.Files.AddAsync(host.CreateTestFile($"音乐 {i+1}.aac"),audio.Id);
        await WpfTestHost.RunAsync(async()=>
        {
            UiThemeService.Apply(mode);
            using var native=new MediaPlayerService();using var fake=new FakePlaybackEngine();
            var main=new MainViewModel(host.Categories,host.Files,host.Organization,fake);
            var window=new MainWindow(main,host.Categories,host.Files,host.Organization,host.RecycleBin,native)
                {Width=width,Height=height,ShowActivated=false,ShowInTaskbar=false};
            try
            {
                window.Show();await main.RefreshTreeAsync();await main.SelectCategoryAsync(audio);
                await main.SelectFileAsync((await host.Files.GetByCategoryAsync(audio.Id))[0]);main.Player.MediaTitle="BigRicePiano - Limerence";
                fake.SetSnapshot(new(true,TimeSpan.FromSeconds(98),TimeSpan.FromSeconds(569)));
                main.Player.ResetZoom();await Dispatcher.Yield(DispatcherPriority.Loaded);window.UpdateLayout();
                var drawer=(DrawerHost)window.FindName("NavigationDrawer");Assert.Equal(DrawerHostOpenMode.Standard,drawer.OpenMode);Assert.True(drawer.IsLeftDrawerOpen);
                var player=(PlayerPanel)window.FindName("PlayerArea");
                foreach(var name in new[]{"ControlsBar","ProgressRow","MediaButtonsRow","MediaSettingsRow"})
                {
                    var element=(FrameworkElement)player.FindName(name);
                    var point=element.TranslatePoint(new Point(),window);
                    Assert.True(point.X+element.ActualWidth<=window.ActualWidth+.5);Assert.True(point.Y+element.ActualHeight<=window.ActualHeight+.5);
                }
                var buttons=Descendants<Button>(window).ToList();Assert.Contains(buttons,b=>Descendants<Ripple>(b).Any());
                var directory=Path.GetFullPath("../../../../../artifacts/material-preview",AppContext.BaseDirectory);Directory.CreateDirectory(directory);
                var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output=File.Create(Path.Combine(directory,$"{mode}-{width}.png"));encoder.Save(output);
            }
            finally{window.PrepareForVerificationExit();window.Close();UiThemeService.Apply(UiThemeMode.Light);}
        });
    }

    [Fact]
    public async Task AsyncDialogHostQueuesResultsAndClosingCancelsPendingConflict()
    {
        using var host=new TestHost();
        await WpfTestHost.RunAsync(async()=>
        {
            using var native=new MediaPlayerService();var main=new MainViewModel(host.Categories,host.Files,host.Organization,new FakePlaybackEngine());
            var window=new MainWindow(main,host.Categories,host.Files,host.Organization,host.RecycleBin,native){ShowActivated=false,ShowInTaskbar=false};
            window.Show();await Dispatcher.Yield(DispatcherPriority.Loaded);
            try
            {
                var root=(DialogHost)window.FindName("RootDialog");
                var first=MessageDialog.ShowAsync("first","确认",DialogButtons.YesNo);var second=MessageDialog.ShowAsync("second","信息");
                await BoundedDialogTests.Until(()=>root.IsOpen);Assert.False(first.IsCompleted);Assert.False(second.IsCompleted);
                var serviced=false;_ = window.Dispatcher.BeginInvoke(new Action(()=>serviced=true));await BoundedDialogTests.Until(()=>serviced);
                ((DialogSurface)root.DialogContent!).Complete(DialogAnswer.Yes);Assert.Equal(DialogAnswer.Yes,await first);
                await BoundedDialogTests.Until(()=>root.IsOpen);((DialogSurface)root.DialogContent!).Complete(DialogAnswer.Ok);Assert.Equal(DialogAnswer.Ok,await second);
                var conflict=ConflictDialog.ShowDecisionAsync("same.aac","target");await BoundedDialogTests.Until(()=>root.IsOpen);
                window.Close();Assert.Equal(ConflictResolution.Ask,(await conflict).Resolution);
                await BoundedDialogTests.Until(()=>!window.IsVisible);
            }
            finally{if(window.IsVisible){window.PrepareForVerificationExit();window.Close();}}
        });
    }

    [Fact]
    public async Task MaterialDialogCoversNativeVideoWithoutStoppingPlayback()
    {
        using var host=new TestHost();var category=await host.Categories.CreateAsync("视频",null);
        var path=SyntheticMedia.WriteAvi(Path.Combine(host.RootDir,"dialog.avi"),audio:true,silentAudio:true);
        var file=await host.Files.AddAsync(path,category.Id);
        await WpfTestHost.RunAsync(async()=>
        {
            using var engine=new MediaPlayerService(){HardwareDecoding=false};
            var main=new MainViewModel(host.Categories,host.Files,host.Organization,engine);
            var window=new MainWindow(main,host.Categories,host.Files,host.Organization,host.RecycleBin,engine){ShowActivated=false,ShowInTaskbar=false};
            window.Show();await Dispatcher.Yield(DispatcherPriority.Loaded);
            try
            {
                await main.SelectCategoryAsync(category);await main.SelectFileAsync(file);await BoundedDialogTests.Until(()=>engine.Snapshot.Position.TotalMilliseconds>100);
                var player=(PlayerPanel)window.FindName("PlayerArea");var video=(FrameworkElement)player.FindName("VideoView");
                var prompt=MessageDialog.ShowAsync("视频播放期间的提示","提示");var root=(DialogHost)window.FindName("RootDialog");await BoundedDialogTests.Until(()=>root.IsOpen);
                Assert.Equal(Visibility.Collapsed,video.Visibility);Assert.True(engine.Snapshot.IsPlaying);
                ((DialogSurface)root.DialogContent!).Complete(DialogAnswer.Ok);await prompt;
                Assert.Equal(Visibility.Visible,video.Visibility);Assert.True(engine.Snapshot.IsPlaying);
                await engine.StopAndReleaseAsync();using var exclusive=File.Open(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
            }
            finally{await engine.StopAndReleaseAsync();window.PrepareForVerificationExit();window.Close();}
        });
    }

    [Fact]
    public async Task ThemeFollowsSystemAndUpdatesLiveBrushesWithReadableContrast()
    {
        await WpfTestHost.RunAsync(()=>
        {
            try
            {
                foreach(var mode in new[]{UiThemeMode.Light,UiThemeMode.Dark})
                {
                    UiThemeService.Apply(mode);Assert.Equal(mode==UiThemeMode.Dark,UiThemeService.IsDark);
                    var resources=System.Windows.Application.Current.Resources;
                    var foreground=((SolidColorBrush)resources["BrushPrimaryText"]).Color;var background=((SolidColorBrush)resources["BrushPrimaryBg"]).Color;
                    Assert.True(Contrast(foreground,background)>7);
                }
                UiThemeService.Apply(UiThemeMode.System);Assert.Equal(Theme.GetSystemTheme()==BaseTheme.Dark,UiThemeService.IsDark);
            }
            finally{UiThemeService.Apply(UiThemeMode.Light);}
        });
    }
    private static double Contrast(Color a,Color b)
    {
        double L(Color c){double F(byte v){var x=v/255d;return x<=.04045?x/12.92:Math.Pow((x+.055)/1.055,2.4);}return .2126*F(c.R)+.7152*F(c.G)+.0722*F(c.B);}
        var x=L(a);var y=L(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root)where T:DependencyObject
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T found)yield return found;foreach(var nested in Descendants<T>(child))yield return nested;}}
}
