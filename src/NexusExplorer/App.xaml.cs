using NexusExplorer.Views.Dialogs;
using NexusExplorer.ApplicationLayer;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexusExplorer.Data;
using NexusExplorer.Infrastructure;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;
using Serilog;
using Serilog.Events;

namespace NexusExplorer;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        MaterialDialogService.BeginSession();

        try
        {

        var config = AppConfig.LoadOrDefault(AppPaths.SettingsPath);
        UiThemeService.Start(config.Appearance?.ThemeMode ?? UiThemeMode.System);

        config.EnsureDirectories();

        var minimumLevel = Enum.TryParse<LogEventLevel>(config.Logging.MinimumLevel, true, out var level)
            ? level
            : LogEventLevel.Information;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .WriteTo.File(
                Path.Combine(config.Logging.ResolvedDirectory, "nexus-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("===== NexusExplorer 启动 =====");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _services = ConfigureServices(config);

        // 分类物理根目录跟随配置
        _services.GetRequiredService<CategoryService>().StorageRoot = config.Storage.ResolvedRoot;

        // 版本化升级与持久操作恢复。无法识别的数据保持原样并停止启动。
        using (var scope = _services.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            using var db = factory.CreateDbContext();
            await NexusExplorer.Data.DatabaseInitializer.InitializeAsync(db, config.Database.ResolvedPath);
            var recoveryProblems = await _services.GetRequiredService<FileOperationExecutor>().RecoverAsync();
            if (recoveryProblems.Count > 0)
                await MessageDialog.ShowAsync("以下操作需要手动检查，已保留原件：\n" + string.Join("\n", recoveryProblems), "操作恢复");
            Log.Information("数据库初始化完成: {Path}", config.Database.ResolvedPath);
        }

        var mainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        if (e.Args.Contains("--verify-startup", StringComparer.Ordinal))
        {
            Log.Information("自检：构建界面与状态");
            await _services.GetRequiredService<MainViewModel>().RefreshTreeAsync();
            mainWindow.Measure(new Size(1280, 800)); mainWindow.Arrange(new Rect(0, 0, 1280, 800)); mainWindow.UpdateLayout();
            await _services.GetRequiredService<MediaPlayerService>().InitializeAsync();
            Log.Information("自检：VLC 原生库已加载");
            File.WriteAllText(Path.Combine(AppPaths.AppRoot, "startup-verification.json"),
                System.Text.Json.JsonSerializer.Serialize(new { Success = true, SchemaVersion = DatabaseInitializer.SchemaVersion, Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), NativeVlc = _services.GetRequiredService<MediaPlayerService>().VlcVersion }));
            mainWindow.PrepareForVerificationExit();
            await _services.GetRequiredService<MediaPlayerService>().StopAndReleaseAsync();
            Log.Information("自检：资源已释放，即将退出");
            Shutdown(0); return;
        }
        mainWindow.Show();

        Log.Information("主窗口已显示");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "启动失败，原数据保留");
            if (e.Args.Contains("--verify-startup", StringComparer.Ordinal))
                File.WriteAllText(Path.Combine(AppPaths.AppRoot, "startup-verification.json"), System.Text.Json.JsonSerializer.Serialize(new { Success = false, Error = ex.ToString() }));
            else await MessageDialog.ShowAsync("启动失败，原数据保留：\n" + ex.Message, "NexusExplorer", DialogButtons.Ok, DialogSeverity.Error);
            Shutdown(1);
        }
    }


    private static ServiceProvider ConfigureServices(AppConfig config)
    {
        var services = new ServiceCollection();

        services.AddSingleton(config);

        services.AddSingleton<IDbContextFactory<AppDbContext>>(_ =>
            new Infrastructure.DbContextFactoryStub(config.Database.ResolvedPath));

        services.AddSingleton<IRecycleBinService, RecycleBinService>();
        services.AddSingleton<FileOperationExecutor>();
        services.AddSingleton<CategoryService>();
        services.AddSingleton<FileService>();
        services.AddSingleton<OrganizationService>();
        services.AddSingleton(new MediaPlayerService(System.Windows.Threading.Dispatcher.FromThread(System.Threading.Thread.CurrentThread)) { HardwareDecoding = config.Playback.HardwareDecoding });
        services.AddSingleton<IPlaybackEngine>(p => p.GetRequiredService<MediaPlayerService>());

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    private async void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Log.Error(e.Exception, "UI 线程未处理异常");
        await MessageDialog.ShowAsync(
            $"发生未处理的错误:\n{e.Exception.Message}",
            "NexusExplorer",
            DialogButtons.Ok,
            DialogSeverity.Error);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Log.Fatal(ex, "致命未处理异常,程序即将退出");
        else
            Log.Fatal("致命未处理异常: {Object}", e.ExceptionObject);
        Log.CloseAndFlush();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "后台任务未观察异常");
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        UiThemeService.Stop();
        _services?.Dispose();
        Log.Information("===== NexusExplorer 退出 =====");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
