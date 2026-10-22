using NexusExplorer.Views.Dialogs;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;
using NexusExplorer.Infrastructure;
using NexusExplorer.ApplicationLayer;
using MaterialDesignThemes.Wpf;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NexusExplorer.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _main;

    public MainWindow(MainViewModel main, CategoryService categoryService,
        FileService fileService, OrganizationService organizationService,
        IRecycleBinService recycleBin, IPlaybackEngine mediaPlayer)
    {
        InitializeComponent();

        _main = main;
        CreateRootCategoryButton.Command = main.Category.CreateRootCommand;

        // 注入面板依赖
        LeftPanel.Initialize(_main, recycleBin);
        PlayerArea.Initialize(_main);
        NavBar.Initialize(_main);
        Activated += (_, _) => { if (_focusPreview) Topmost = true; };
        Deactivated += (_, _) => { if (_focusPreview) Topmost = false; };
        PlayerArea.FocusPreviewRequested += ToggleFocusPreview;
        PlayerArea.ExitFocusPreviewRequested += () => { if (_focusPreview) ToggleFocusPreview(); };
        LeftPanel.PresentationSizeChanged += ResizeLibrary;
        Workspace.SizeChanged += (_, _) => ResizeLibrary();
        Loaded += async (_, _) => { try { await ApplyWallpaperAsync(AppConfig.LoadOrDefault(AppPaths.SettingsPath).Appearance); } catch (Exception ex) { Serilog.Log.Warning(ex, "自定义背景不可用，使用默认背景"); } };
        StateChanged += (_, _) => { Dispatcher.BeginInvoke(() => WindowAppearance.UpdateMaximizedInset(this, WindowSurface)); MaximizeIcon.Kind = WindowState == WindowState.Maximized ? PackIconKind.WindowRestore : PackIconKind.WindowMaximize; WindowLayout.Margin = new Thickness(_focusPreview ? 0 : 6); };
        SizeChanged += (_, _) => WindowAppearance.UpdateMaximizedInset(this, WindowSurface);
        UiThemeService.Changed += OnThemeChanged;
        SourceInitialized += (_, _) => { WindowAppearance.Apply(this); WindowAppearance.UpdateMaximizedInset(this, WindowSurface); };
        Loaded += (_, _) => { WindowAppearance.Apply(this); WindowAppearance.UpdateMaximizedInset(this, WindowSurface); };
        MaterialDialogService.NoticeRequested += OnNotice;
        MaterialDialogService.InputWindowStateChanged += OnInputWindowStateChanged;
        Closed += (_, _) => { UiThemeService.Changed -= OnThemeChanged; MaterialDialogService.NoticeRequested -= OnNotice; MaterialDialogService.InputWindowStateChanged -= OnInputWindowStateChanged; _noticeTimer.Stop(); PlayerArea.FocusPreviewRequested -= ToggleFocusPreview; };
        RootDialog.Identifier = "Nexus." + Guid.NewGuid().ToString("N");
        RootDialog.Loaded += (_, _) => MaterialDialogService.Register(RootDialog);
        RootDialog.Unloaded += (_, _) => MaterialDialogService.Unregister(RootDialog);
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.F11) { ToggleFocusPreview(); e.Handled = true; }
            if (e.Key == System.Windows.Input.Key.Escape && _focusPreview) { ToggleFocusPreview(); e.Handled = true; }
            if (e.Key == System.Windows.Input.Key.F6 && _main.Player.Engine is IPlaybackDiagnostics diagnostics)
            { e.Handled = true; await diagnostics.RecordDiagnosticsAsync(); }
        };

        Loaded += async (_, _) =>
        {
            try { await _main.RefreshTreeAsync(); await LeftPanel.RestoreBrowseAsync(); }
            catch (Exception ex) { Serilog.Log.Error(ex, "加载界面失败"); await MessageDialog.ShowAsync(ex.Message, "加载失败"); }
        };
        Closing += async (_, e) =>
        {
            if (_closingFinished) return;
            e.Cancel = true;
            if (_closingStarted) return;
            _closingStarted = true; IsEnabled = false;
            MaterialDialogService.CancelAll();
            try
            {
                await LeftPanel.PrepareForCloseAsync(); LeftPanel.SaveUiState();
                if (mediaPlayer is IPlaybackLifetime lifetime) await lifetime.ShutdownAsync();
                else await mediaPlayer.StopAndReleaseAsync();
                PlayerArea.Detach();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "退出时停止播放失败"); _closingStarted = false; IsEnabled = true;
                await MessageDialog.ShowAsync("播放器尚未释放资源，请保留日志并稍后重试关闭。", "关闭失败"); return;
            }
            _closingFinished = true; Close();
        };
    }
    private bool _closingStarted;
    private bool _closingFinished;
    private void OnThemeChanged() => WindowAppearance.Apply(this);
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private int _noticeVersion;
    private void OnNotice(string message)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => OnNotice(message)); return; }
        var version = ++_noticeVersion;
        _noticeTimer.Stop(); NoticeText.Text = message; NoticeCard.Visibility = Visibility.Visible;
        NoticeCard.UpdateLayout();
        var distance = Math.Max(100, NoticeCard.ActualHeight + 30);
        var animation = new DoubleAnimationUsingKeyFrames();
        for (int i = 0; i <= 74; i++)
        {
            var t = i / 74d * .82; const double damping = .58, frequency = 18;
            var damped = frequency * Math.Sqrt(1 - damping * damping);
            var y = -distance * Math.Exp(-damping * frequency * t) * (Math.Cos(damped * t) + damping * frequency / damped * Math.Sin(damped * t));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(i == 74 ? 0 : y, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
        }
        animation.Completed += (_, _) => { if (version == _noticeVersion) _noticeTimer.Start(); };
        _noticeTimer.Tick -= HideNotice; _noticeTimer.Tick += HideNotice;
        NoticeTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, animation);
    }
    private void HideNotice(object? sender, EventArgs e)
    {
        _noticeTimer.Stop(); var version = _noticeVersion;
        var exit = new DoubleAnimation(0, -Math.Max(100, NoticeCard.ActualHeight + 30), TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        exit.Completed += (_, _) => { if (version == _noticeVersion) NoticeCard.Visibility = Visibility.Collapsed; };
        NoticeTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, exit);
    }
    private bool _libraryOpen = true;
    private double _libraryHeight;
    private void ResizeLibrary()
    {
        var available = Math.Max(360, Workspace.ActualHeight - 64);
        LibrarySlot.MaxHeight = available;
        var height = Math.Clamp(LeftPanel.PreferredHeight + 72, 360, available);
        if (Math.Abs(height - _libraryHeight) < 1) return;
        _libraryHeight = height;
        LibraryCard.BeginAnimation(HeightProperty, PrismMotion.Spring(LibraryCard.ActualHeight > 0 ? LibraryCard.ActualHeight : height, height, true));
    }
    private void OnToggleDrawer(object sender, RoutedEventArgs e)
    {
        _libraryOpen = !_libraryOpen;
        LibraryCard.IsHitTestVisible = _libraryOpen;
        LibrarySlot.BeginAnimation(WidthProperty, PrismMotion.Spring(LibrarySlot.ActualWidth, _libraryOpen ? 320 : 0, true));
        LibraryScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, PrismMotion.Spring(LibraryScale.ScaleX, _libraryOpen ? 1 : .78));
        LibraryScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, PrismMotion.Spring(LibraryScale.ScaleY, _libraryOpen ? 1 : .78));
        LibraryTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, PrismMotion.Spring(LibraryTranslate.X, _libraryOpen ? 0 : 160));
        LibraryCard.BeginAnimation(OpacityProperty, new DoubleAnimation(_libraryOpen ? 1 : 0, TimeSpan.FromMilliseconds(350)));
    }
    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void OnMaximize(object sender, RoutedEventArgs e) { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();
    private void OnSearchChanged(object sender, TextChangedEventArgs e) => LeftPanel?.SetFileFilter(FileSearch.Text, _globalSearch);
    private bool _globalSearch;
    private void OnSearchScopeClick(object sender, RoutedEventArgs e)
    {
        _globalSearch = !_globalSearch;
        SearchScope.ApplyTemplate();
        if (SearchScope.Template.FindName("ScopeTranslate", SearchScope) is System.Windows.Media.TranslateTransform slide)
            slide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, PrismMotion.Spring(slide.X, _globalSearch ? -104 : 0));
        HintAssist.SetHint(FileSearch, _globalSearch ? "搜索全部分类文件…" : "搜索当前分类文件…");
        LeftPanel.SetFileFilter(FileSearch.Text, _globalSearch);
    }
    private bool _focusPreview;
    private WindowState _beforeFocusState;
    private Visibility _beforeFocusLibrary;
    private Rect _beforeFocusBounds;
    private void ToggleFocusPreview()
    {
        _focusPreview = !_focusPreview;
        var center = (Grid)PlayerArea.Parent;
        if (_focusPreview)
        {
            _beforeFocusState = WindowState; _beforeFocusLibrary = LibraryCard.Visibility;
            _beforeFocusBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            WindowSurface.Margin = new Thickness(0); Topmost = true;
            WindowAppearance.FillMonitor(this);
        }
        else
        {
            Topmost = false; WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize;
            Left = _beforeFocusBounds.Left; Top = _beforeFocusBounds.Top; Width = _beforeFocusBounds.Width; Height = _beforeFocusBounds.Height;
            WindowState = _beforeFocusState;
            WindowAppearance.UpdateMaximizedInset(this, WindowSurface);
        }
        WindowLayout.Margin = new Thickness(_focusPreview ? 0 : 6);
        Workspace.Margin = _focusPreview ? new Thickness(0) : new Thickness(20, 8, -6, 16);
        WallpaperScene.Visibility = _focusPreview ? Visibility.Hidden : Visibility.Visible;
        TitleBar.Visibility = SearchCard.Visibility = _focusPreview ? Visibility.Collapsed : Visibility.Visible;
        System.Windows.Shell.WindowChrome.GetWindowChrome(this).CaptionHeight = _focusPreview ? 0 : 64;
        WindowLayout.RowDefinitions[0].Height = new GridLength(_focusPreview ? 0 : 64);
        LibraryCard.Visibility = _focusPreview ? Visibility.Collapsed : _beforeFocusLibrary;
        LibrarySlot.Visibility = _focusPreview ? Visibility.Collapsed : Visibility.Visible;
        center.RowDefinitions[0].Height = new GridLength(_focusPreview ? 0 : 64);
        ((FrameworkElement)NavBar.Parent).Visibility = _focusPreview ? Visibility.Collapsed : Visibility.Visible;
        PlayerArea.SetFocusPreview(_focusPreview);
        WindowAppearance.SetFullscreenFrame(this, _focusPreview);
    }
    private void OnDialogOpened(object sender, DialogOpenedEventArgs e) => PlayerArea.SetDialogCovered(true);
    private void OnDialogClosed(object sender, DialogClosedEventArgs e) => PlayerArea.SetDialogCovered(false);
    private void OnInputWindowStateChanged(Window owner, bool covered)
    { if (ReferenceEquals(owner, this)) PlayerArea.SetDialogCovered(covered); }
    private async void OnAppearanceClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = AppConfig.LoadOrDefault(AppPaths.SettingsPath);
            var before = config.Appearance;
            var result = await AppearanceDialog.ShowAsync(before, async draft =>
            {
                UiThemeService.Apply(draft.ThemeMode);
                await ApplyWallpaperAsync(draft);
            }, ApplyWallpaperOpacity);
            if (result is null) { UiThemeService.Apply(before.ThemeMode); await ApplyWallpaperAsync(before); return; }
            await ApplyWallpaperAsync(result);
            config.Appearance = result; config.Save(AppPaths.SettingsPath);
            UiThemeService.Apply(result.ThemeMode);
        }
        catch (Exception ex) { await MessageDialog.ShowAsync(ex.Message, "外观设置", severity: DialogSeverity.Error); }
    }
    private int _wallpaperVersion;
    private bool _wallpaperLoaded;
    private string? _wallpaperPath;
    internal async Task ApplyWallpaperAsync(AppearanceConfig appearance)
    {
        var version = ++_wallpaperVersion;
        var path = appearance.BackgroundImagePath;
        if (_wallpaperLoaded && path == _wallpaperPath) { ApplyWallpaperOpacity(appearance.BackgroundOpacity); return; }
        var image = await Task.Run(() =>
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 2560;
            bitmap.UriSource = string.IsNullOrWhiteSpace(path)
                ? new Uri("pack://application:,,,/NexusExplorer;component/Assets/PrismBackground.png") : new Uri(System.IO.Path.GetFullPath(path));
            bitmap.EndInit(); bitmap.Freeze(); return bitmap;
        });
        if (version != _wallpaperVersion) return;
        _wallpaperLoaded = true; _wallpaperPath = path; WallpaperImage.Source = image; ApplyWallpaperOpacity(appearance.BackgroundOpacity);
    }
    internal void ApplyWallpaperOpacity(double opacity)
    {
        opacity = double.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : 1;
        WallpaperImage.Opacity = opacity;
        WallpaperShade.Opacity = opacity;
        MaterialDialogService.RefreshBackdrop();
    }
    internal void PrepareForVerificationExit()
    {
        // Explicit Application.Shutdown cannot await an asynchronous Closing handler.
        _closingFinished = true; PlayerArea.Detach();
    }
}
