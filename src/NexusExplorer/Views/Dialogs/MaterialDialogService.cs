using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Serilog;

namespace NexusExplorer.Views.Dialogs;

internal static class MaterialDialogService
{
    private static readonly SemaphoreSlim Queue = new(1, 1);
    private static WeakReference<DialogHost>? _root;
    private static DialogSession? _session;
    private static bool _closing;
    internal static System.Windows.Controls.Grid? ThemeSurface { get; private set; }
    public static event Action<string>? NoticeRequested;
    public static event Action<Window, bool>? InputWindowStateChanged;
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
        Window? owner = null;
        bool ownerWasEnabled = false;
        var app = System.Windows.Application.Current;
        var previousMain = app.MainWindow; var previousMode = app.ShutdownMode;
        try
        {
            if (_closing) return null;
            DialogHost? host = null;
            if (_root is not null && _root.TryGetTarget(out var root) && root.IsLoaded) host = root;
            var scene = host is null ? null : Window.GetWindow(host)?.FindName("WallpaperScene") as FrameworkElement;
            var glass = new PrismCard { Content = view };
            var slide = new TranslateTransform(0, MotionProfiles.DialogOffset);
            glass.RenderTransform = slide;
            glass.Opacity = .15;
            bool exiting = false, allowClose = false;
            void Enter(object sender, RoutedEventArgs args)
            {
                if (exiting) return;
                // Cache the live visual only while its position/opacity moves; content
                // changes still invalidate WPF's cache. No screenshots or frozen wallpaper.
                glass.CacheMode = new BitmapCache { SnapsToDevicePixels = true };
                PrismMotion.Animate(slide, TranslateTransform.YProperty, 0, MotionProfiles.DialogEnter,
                    () => { if (!exiting) glass.CacheMode = null; });
                PrismMotion.Animate(glass, UIElement.OpacityProperty, 1, MotionProfiles.DialogFade, minimum: 0, maximum: 1);
            }
            glass.Loaded += Enter;
            glass.PrepareDialogBackdrop(scene);
            if (host is null || view.RequiresInputWindow)
            {
                // Text input uses a real owned HWND: Popup's IME context can lose
                // the caret anchor after refocusing. Keep DialogHost inside it.
                owner = host is not null ? Window.GetWindow(host) : null;
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                host = new DialogHost { Identifier = Guid.NewGuid().ToString("N"), CloseOnClickAway = false, DialogContentUniformCornerRadius = 24 };
                host.SetResourceReference(FrameworkElement.StyleProperty, "MaterialDesignEmbeddedDialogHost");
                shell = new Window { Title = "NexusExplorer", Width = 720, Height = 520, Content = host,
                    Owner = owner, WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                    ShowInTaskbar = false, UseLayoutRounding = true,
                    Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/NexusExplorer;component/Assets/AppIcon.ico")) };
                if (view.RequiresInputWindow) { shell.Width = view.Width + 96; shell.Height = view.Width <= 480 ? 400 : 560; }
                shell.SetResourceReference(FrameworkElement.StyleProperty, "MaterialDesignWindow");
                shell.SetResourceReference(Window.BackgroundProperty, "BrushPrimaryBg");
                shell.SetResourceReference(Window.FontFamilyProperty, "FontFamilyMain");
                if (view.RequiresInputWindow)
                {
                    shell.WindowStyle = WindowStyle.None;
                    shell.ResizeMode = ResizeMode.NoResize;
                    shell.AllowsTransparency = true;
                    shell.Background = System.Windows.Media.Brushes.Transparent;
                    host.Background = System.Windows.Media.Brushes.Transparent;
                    host.OverlayBackground = System.Windows.Media.Brushes.Transparent;
                    // Dp24's 49px blur and 12.5px offset must fit inside the
                    // transparent HWND; otherwise its edges clip the rounded shadow.
                    host.DialogMargin = new Thickness(64);
                    host.DialogBackground = System.Windows.Media.Brushes.Transparent;
                    shell.Width = view.Width + view.Margin.Left + view.Margin.Right + host.DialogMargin.Left + host.DialogMargin.Right;
                }
                var area = SystemParameters.WorkArea;
                shell.MaxWidth = Math.Max(1, area.Width - 32); shell.MaxHeight = Math.Max(1, area.Height - 32);
                shell.Width = Math.Min(shell.Width, shell.MaxWidth); shell.Height = Math.Min(shell.Height, shell.MaxHeight);
                if (view.RequiresInputWindow)
                {
                    view.Constrain(shell.Width, shell.MaxHeight, host.DialogMargin);
                    view.Measure(new Size(Math.Min(view.Width, view.MaxWidth) + view.Margin.Left + view.Margin.Right, double.PositiveInfinity));
                    shell.Height = Math.Min(shell.MaxHeight, view.DesiredSize.Height + host.DialogMargin.Top + host.DialogMargin.Bottom);
                }
                shell.Closing += (_, args) =>
                {
                    if (_session is not { IsEnded: false } session) return;
                    if (!_closing && !allowClose) args.Cancel = true;
                    session.Close(null);
                };
                if (owner is not null)
                {
                    ownerWasEnabled = owner.IsEnabled; owner.IsEnabled = false;
                    InputWindowStateChanged?.Invoke(owner, true);
                }
                var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                host.Loaded += (_, _) => loaded.TrySetResult(); shell.Show(); await loaded.Task;
            }
            void Bounds(object? sender, SizeChangedEventArgs e) => view.Constrain(host.ActualWidth, host.ActualHeight, host.DialogMargin);
            view.Constrain(host.ActualWidth, host.ActualHeight, host.DialogMargin); host.SizeChanged += Bounds;
            try
            {
                host.DialogBackground = System.Windows.Media.Brushes.Transparent;
                // DialogHost still owns modality/focus; PrismMotion alone owns card motion.
                host.ApplyTemplate();
                if (VisualTreeHelper.GetChildrenCount(host) > 0 && VisualTreeHelper.GetChild(host, 0) is FrameworkElement templateRoot)
                    foreach (var group in VisualStateManager.GetVisualStateGroups(templateRoot).OfType<VisualStateGroup>())
                        if (group.Name == "PopupStates") group.Transitions.Clear();
                var result = await DialogHost.Show(glass, host.Identifier!, new DialogOpenedEventHandler((_, e) =>
                {
                    // Prism owns the rounded clipping; the host must not crop its shadow.
                    host.ApplyTemplate();
                    if (host.Template.FindName("PART_PopupContentElement", host) is Card card)
                    {
                        card.ClipContent = false;
                        card.Effect = new System.Windows.Media.Effects.DropShadowEffect
                        { Color = Color.FromRgb(30, 55, 70), BlurRadius = 28, ShadowDepth = 8, Opacity = .28 };
                    }
                    _session = e.Session;
                    ThemeSurface = glass.Template.FindName("PART_Clip", glass) as System.Windows.Controls.Grid;
                    view.Complete = result => { if (!e.Session.IsEnded) e.Session.Close(result); };

                }), new DialogClosingEventHandler((_, e) =>
                {
                    if (allowClose || _closing || !glass.IsLoaded) return;
                    e.Cancel();
                    if (exiting) return;
                    exiting = true; view.IsHitTestVisible = false;
                    glass.CacheMode = new BitmapCache { SnapsToDevicePixels = true };
                    var parameter = e.Parameter;
                    PrismMotion.Animate(slide, TranslateTransform.YProperty, MotionProfiles.DialogOffset, MotionProfiles.DialogExit);
                    PrismMotion.Animate(glass, UIElement.OpacityProperty, 0, MotionProfiles.DialogExit, () =>
                    {
                        allowClose = true;
                        if (!e.Session.IsEnded) e.Session.Close(parameter);
                    }, minimum: 0, maximum: 1);
                }));
                // Show completes at logical close, before the template's exit transition.
                // Keep content/desired size intact until the popup has actually disappeared.
                await WaitForVisualCloseAsync(host);
                return result;
            }
            finally
            {
                glass.Loaded -= Enter; PrismMotion.StopAll(glass); PrismMotion.StopAll(slide);
                glass.CacheMode = null;
                host.SizeChanged -= Bounds; host.DialogContent = null; _session = null; ThemeSurface = null;
            }
        }
        finally
        {
            if (shell is not null)
            {
                if (ReferenceEquals(app.MainWindow, shell)) app.MainWindow = previousMain;
                shell.Close(); app.ShutdownMode = previousMode;
                if (owner is not null)
                {
                    owner.IsEnabled = ownerWasEnabled;
                    InputWindowStateChanged?.Invoke(owner, false);
                    if (owner.IsVisible) owner.Activate();
                }
            }
            Queue.Release();
        }
    }

    private static async Task WaitForVisualCloseAsync(DialogHost host)
    {
        if (!host.IsLoaded || host.Dispatcher.HasShutdownStarted) return;
        var popup = host.Template.FindName("PART_Popup", host) as Popup;
        var root = VisualTreeHelper.GetChildrenCount(host) > 0 ? VisualTreeHelper.GetChild(host, 0) as FrameworkElement : null;
        var states = root is null ? null : VisualStateManager.GetVisualStateGroups(root).OfType<VisualStateGroup>()
            .FirstOrDefault(group => group.Name == "PopupStates");
        // Embedded hosts have no Popup HWND; their Closed visual state is the boundary.
        bool Finished() => !host.IsLoaded || (popup is not null ? !popup.IsOpen : states is null || states.CurrentState?.Name == "Closed");
        if (Finished()) return;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Closed(object? sender, EventArgs e) => done.TrySetResult();
        void Unloaded(object sender, RoutedEventArgs e) => done.TrySetResult();
        void StateChanged(object? sender, VisualStateChangedEventArgs e) { if (Finished()) done.TrySetResult(); }
        if (popup is not null) popup.Closed += Closed;
        else if (states is not null) states.CurrentStateChanged += StateChanged;
        host.Unloaded += Unloaded;
        host.Dispatcher.ShutdownStarted += Closed;
        try { if (!Finished()) await done.Task; }
        finally
        {
            if (popup is not null) popup.Closed -= Closed;
            else if (states is not null) states.CurrentStateChanged -= StateChanged;
            host.Unloaded -= Unloaded;
            host.Dispatcher.ShutdownStarted -= Closed;
        }
    }

    public static async void NotifyError(string text, string title = "错误")
    {
        try { await MessageDialog.ShowAsync(text, title, severity: DialogSeverity.Warning); }
        catch (Exception ex) { Log.Error(ex, "无法显示 Material 提示"); }
    }
}
