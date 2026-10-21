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
        PlayerArea.FocusPreviewRequested += ToggleFocusPreview;
        StateChanged += (_, _) => { MaximizeIcon.Kind = WindowState == WindowState.Maximized ? PackIconKind.WindowRestore : PackIconKind.WindowMaximize; WindowLayout.Margin = new Thickness(WindowState == WindowState.Maximized ? 8 : 6); };
        UiThemeService.Changed += OnThemeChanged;
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);
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
    private void OnToggleDrawer(object sender, RoutedEventArgs e) => LibraryCard.Visibility = LibraryCard.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void OnMaximize(object sender, RoutedEventArgs e) { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();
    private void OnSearchChanged(object sender, TextChangedEventArgs e) => LeftPanel?.SetFileFilter(FileSearch.Text);
    private bool _focusPreview;
    private WindowState _beforeFocusState;
    private Visibility _beforeFocusLibrary;
    private void ToggleFocusPreview()
    {
        _focusPreview = !_focusPreview;
        var center = (Grid)PlayerArea.Parent;
        if (_focusPreview) { _beforeFocusState = WindowState; _beforeFocusLibrary = LibraryCard.Visibility; WindowState = WindowState.Maximized; }
        else WindowState = _beforeFocusState;
        TitleBar.Visibility = SearchCard.Visibility = _focusPreview ? Visibility.Collapsed : Visibility.Visible;
        System.Windows.Shell.WindowChrome.GetWindowChrome(this).CaptionHeight = _focusPreview ? 0 : 64;
        WindowLayout.RowDefinitions[0].Height = new GridLength(_focusPreview ? 0 : 64);
        LibraryCard.Visibility = _focusPreview ? Visibility.Collapsed : _beforeFocusLibrary;
        center.RowDefinitions[0].Height = new GridLength(_focusPreview ? 0 : 64);
        ((FrameworkElement)NavBar.Parent).Visibility = _focusPreview ? Visibility.Collapsed : Visibility.Visible;
        PlayerArea.SetFocusPreview(_focusPreview);
    }
    private void OnDialogOpened(object sender, DialogOpenedEventArgs e) => PlayerArea.SetDialogCovered(true);
    private void OnDialogClosed(object sender, DialogClosedEventArgs e) => PlayerArea.SetDialogCovered(false);
    private void OnInputWindowStateChanged(Window owner, bool covered)
    { if (ReferenceEquals(owner, this)) PlayerArea.SetDialogCovered(covered); }
    private async void OnAppearanceClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var choices = new[] { new DialogChoice<UiThemeMode>("跟随 Windows", UiThemeMode.System), new("浅色", UiThemeMode.Light), new("深色", UiThemeMode.Dark) };
            var result = await ChoiceDialog.ShowAsync("外观设置", "界面主题", choices, UiThemeService.Mode);
            if (result.Confirmed) UiThemeService.Apply(result.Value, save: true);
        }
        catch (Exception ex) { await MessageDialog.ShowAsync(ex.Message, "外观设置", severity: DialogSeverity.Error); }
    }
    internal void PrepareForVerificationExit()
    {
        // Explicit Application.Shutdown cannot await an asynchronous Closing handler.
        _closingFinished = true; PlayerArea.Detach();
    }
}
