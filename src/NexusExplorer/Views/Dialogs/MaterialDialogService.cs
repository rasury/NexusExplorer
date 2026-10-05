using System.Windows;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using Serilog;

namespace NexusExplorer.Views.Dialogs;

internal static class MaterialDialogService
{
    private static readonly SemaphoreSlim Queue = new(1, 1);
    private static WeakReference<DialogHost>? _root;
    private static DialogSession? _session;
    private static bool _closing;
    public static event Action<string>? NoticeRequested;
    public static void Notice(string message) => NoticeRequested?.Invoke(message);
    internal static void BeginSession() => _closing = false;
    public static void Register(DialogHost host) { _root = new(host); _closing = false; }
    public static void Unregister(DialogHost host)
    { if (_root is not null && _root.TryGetTarget(out var current) && ReferenceEquals(current, host)) _root = null; }
    public static void CancelAll()
    { _closing = true; if (_session is { IsEnded: false }) _session.Close(null); }

    public static async Task<object?> ShowAsync(DialogSurface view)
    {
        await Queue.WaitAsync();
        Window? shell = null;
        var app = System.Windows.Application.Current;
        var previousMain = app.MainWindow; var previousMode = app.ShutdownMode;
        try
        {
            if (_closing) return null;
            DialogHost? host = null;
            if (_root is not null && _root.TryGetTarget(out var root) && root.IsLoaded) host = root;
            if (host is null)
            {
                // Startup/recovery errors also use asynchronous DialogHost, before MainWindow exists.
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                host = new DialogHost { Identifier = Guid.NewGuid().ToString("N"), CloseOnClickAway = false, DialogContentUniformCornerRadius = 28 };
                host.SetResourceReference(FrameworkElement.StyleProperty, "MaterialDesignEmbeddedDialogHost");
                shell = new Window { Title = "NexusExplorer", Width = 720, Height = 520, Content = host,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen, Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/NexusExplorer;component/Assets/AppIcon.ico")) };
                shell.SetResourceReference(FrameworkElement.StyleProperty, "MaterialDesignWindow");
                shell.SetResourceReference(Window.BackgroundProperty, "BrushPrimaryBg");
                var area = SystemParameters.WorkArea;
                shell.MaxWidth = Math.Max(1, area.Width - 32); shell.MaxHeight = Math.Max(1, area.Height - 32);
                shell.Width = Math.Min(shell.Width, shell.MaxWidth); shell.Height = Math.Min(shell.Height, shell.MaxHeight);
                shell.Closing += (_, _) => { if (_session is { IsEnded: false }) _session.Close(null); };
                var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                host.Loaded += (_, _) => loaded.TrySetResult(); shell.Show(); await loaded.Task;
            }
            void Bounds(object? sender, SizeChangedEventArgs e) => view.Constrain(host.ActualWidth, host.ActualHeight);
            view.Constrain(host.ActualWidth, host.ActualHeight); host.SizeChanged += Bounds;
            try
            {
                return await DialogHost.Show(view, host.Identifier!, new DialogOpenedEventHandler((_, e) =>
                {
                    _session = e.Session;
                    view.Complete = result => { if (!e.Session.IsEnded) e.Session.Close(result); };
                }));
            }
            finally { host.SizeChanged -= Bounds; host.DialogContent = null; _session = null; }
        }
        finally
        {
            if (shell is not null)
            {
                if (ReferenceEquals(app.MainWindow, shell)) app.MainWindow = previousMain;
                shell.Close(); app.ShutdownMode = previousMode;
            }
            Queue.Release();
        }
    }

    public static async void NotifyError(string text, string title = "错误")
    {
        try { await MessageDialog.ShowAsync(text, title, severity: DialogSeverity.Warning); }
        catch (Exception ex) { Log.Error(ex, "无法显示 Material 提示"); }
    }
}
