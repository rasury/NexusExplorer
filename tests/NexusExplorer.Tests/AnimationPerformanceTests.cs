using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NexusExplorer.Models;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;
using NexusExplorer.Views.Dialogs;

namespace NexusExplorer.Tests;
[Collection("NativePlaybackLogging")]
public class AnimationPerformanceTests
{
    [Fact]
    [Trait("Category", "Performance")]
    public async Task MeasureProductionMotion()
    {
        using var host = new TestHost();
        var report = new List<object>();
        await WpfTestHost.RunAsync(async () =>
        {
            foreach (var setup in new[] { (Count:1000, Effects:true) })
            {
                using var engine = new FakePlaybackEngine();
                var main = new MainViewModel(host.Categories, host.Files, host.Organization, engine);
                var window = new MainWindow(main, host.Categories, host.Files, host.Organization, host.RecycleBin, engine) { ShowInTaskbar = false };
                try
                {
                    window.Show(); await Task.Delay(800);
                    var panel = (CategoryFilePanel)window.FindName("LeftPanel");
                    main.CurrentFiles = new ObservableCollection<FileItem>(Enumerable.Range(0, setup.Count).Select(i => new FileItem { Id=i+1, FileName=$"audio-{i}.mp3", AbsolutePath=Path.Combine(host.RootDir,"missing.mp3") }));
                    panel.SetFileFilter(""); await Task.Delay(800);
                    if (!setup.Effects) foreach (var element in Descendants(window).OfType<UIElement>()) element.Effect = null;
                    var player = (PlayerPanel)window.FindName("PlayerArea");
                    var drawer = (Button)window.FindName("DrawerToggleButton");
                    var scope = (Button)window.FindName("SearchScope");
                    var list = (ListBox)panel.FindName("FileListBox");
                    async Task Sample(string name, Action action)
                    {
                        var intervals = new List<double>(); var last = double.NaN; var lastRender = TimeSpan.MinValue;
                        var layouts = 0; var resizes = 0; var watch = Stopwatch.StartNew();
                        var cpu = Process.GetCurrentProcess().TotalProcessorTime;
                        var allocated = GC.GetAllocatedBytesForCurrentThread();
                        void Frame(object? s, EventArgs e)
                        {
                            var render = ((RenderingEventArgs)e).RenderingTime;
                            if (render == lastRender) return; lastRender=render;
                            var now=watch.Elapsed.TotalMilliseconds;
                            if (!double.IsNaN(last)) intervals.Add(now-last); last=now;
                        }
                        void Layout(object? s, EventArgs e) => layouts++;
                        void Resize(object s, SizeChangedEventArgs e) => resizes++;
                        CompositionTarget.Rendering += Frame; window.LayoutUpdated += Layout; player.SizeChanged += Resize;
                        action(); await Task.Delay(850);
                        CompositionTarget.Rendering -= Frame; window.LayoutUpdated -= Layout; player.SizeChanged -= Resize;
                        var sorted=intervals.Order().ToArray();
                        report.Add(new { allocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocated, setup.Count, setup.Effects, name, tier=RenderCapability.Tier>>16, frames=sorted.Length, medianMs=sorted.Length>0?sorted[sorted.Length/2]:0, p95Ms=sorted.Length>0?sorted[(int)((sorted.Length-1)*.95)]:0, maxMs=sorted.LastOrDefault(), over25=sorted.Count(x=>x>25), layouts, resizes, cpuMs=(Process.GetCurrentProcess().TotalProcessorTime-cpu).TotalMilliseconds, realized=Enumerable.Range(0,setup.Count).Count(i=>list.ItemContainerGenerator.ContainerFromIndex(i)!=null) });
                    }
                    for(var round=0;round<2;round++)
                    {
                        await Sample("search",()=>scope.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                        await Sample("drawer-close",()=>drawer.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                        await Sample("drawer-open",()=>drawer.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                        await Sample("inspector-open",()=>player.ShowInspector(true));
                        await Sample("inspector-close",()=>player.ShowInspector(false));
                        await Sample("notice",()=>MaterialDialogService.Notice("动画测量"));
                        await Task.Delay(900);
                    }
                }
                finally { window.PrepareForVerificationExit(); window.Close(); }
            }
        });
        var folder=Path.Combine(Path.GetTempPath(),"nexus-feature-checks"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"motion-" + (Environment.GetEnvironmentVariable("NEXUS_MOTION_REPORT") ?? "latest") + ".json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        { var child=VisualTreeHelper.GetChild(root,i); yield return child; foreach(var nested in Descendants(child)) yield return nested; }
    }
}
