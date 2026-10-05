using System.Windows;
using System.Windows.Threading;

namespace NexusExplorer.Tests;

// All resource-aware tests share one real WPF dispatcher and one Application.
internal static class WpfTestHost
{
    private static readonly Task<Dispatcher> Ready = Start();
    private static Task<Dispatcher> Start()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new MaterialDesignThemes.Wpf.BundledTheme
                { BaseTheme = MaterialDesignThemes.Wpf.BaseTheme.Light, PrimaryColor = MaterialDesignColors.PrimaryColor.DeepPurple, SecondaryColor = MaterialDesignColors.SecondaryColor.Teal });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml") });
                foreach (var name in new[] { "Theme", "Controls", "Converters" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    { Source = new Uri($"pack://application:,,,/NexusExplorer;component/Resources/{name}.xaml") });
                ready.SetResult(app.Dispatcher); Dispatcher.Run();
            }
            catch (Exception ex) { ready.TrySetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return ready.Task;
    }
    public static async Task RunAsync(Action action)
    {
        var dispatcher = await Ready;
        await dispatcher.InvokeAsync(action);
    }
    public static async Task RunAsync(Func<Task> action)
    {
        var dispatcher = await Ready;
        await dispatcher.InvokeAsync(action).Task.Unwrap();
    }
}

