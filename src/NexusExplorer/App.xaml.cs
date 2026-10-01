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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var config = AppConfig.LoadOrDefault(AppPaths.SettingsPath);

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
        AppServices.Initialize(_services);

        // 分类物理根目录跟随配置
        _services.GetRequiredService<CategoryService>().StorageRoot = config.Storage.ResolvedRoot;

        // 数据库初始化 + 轻量迁移(EnsureCreated 不改已有表结构)
        using (var scope = _services.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            using var db = factory.CreateDbContext();
            db.Database.EnsureCreated();
            MigrateDatabase(db, config.Database.ResolvedPath);
            Log.Information("数据库初始化完成: {Path}", config.Database.ResolvedPath);
        }

        var mainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();

        Log.Information("主窗口已显示");
    }

    /// <summary>SQLite 轻量迁移:给已存在的表补缺失的列(EnsureCreated 不会改表)。</summary>
    private static void MigrateDatabase(AppDbContext db, string dbPath)
    {
        try
        {
            var connection = db.Database.GetDbConnection();
            connection.Open();

            using var check = connection.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Categories') WHERE name='IsPinned'";
            var exists = Convert.ToInt64(check.ExecuteScalar()!) > 0;

            if (!exists)
            {
                using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE Categories ADD COLUMN IsPinned INTEGER NOT NULL DEFAULT 0";
                alter.ExecuteNonQuery();
                Log.Information("数据库迁移: Categories.IsPinned 列已添加");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "数据库迁移检查失败(继续启动)");
        }
    }

    private static ServiceProvider ConfigureServices(AppConfig config)
    {
        var services = new ServiceCollection();

        services.AddSingleton(config);

        services.AddSingleton<IDbContextFactory<AppDbContext>>(_ =>
            new Infrastructure.DbContextFactoryStub(config.Database.ResolvedPath));

        services.AddSingleton<IRecycleBinService, RecycleBinService>();
        services.AddSingleton<CategoryService>();
        services.AddSingleton<FileService>();
        services.AddSingleton<OrganizationService>();
        services.AddSingleton(new MediaPlayerService(System.Windows.Threading.Dispatcher.FromThread(System.Threading.Thread.CurrentThread)));

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "UI 线程未处理异常");
        MessageBox.Show(
            $"发生未处理的错误:\n{e.Exception.Message}",
            "NexusExplorer",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
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
        _services?.GetRequiredService<MediaPlayerService>().Dispose();
        _services?.Dispose();
        Log.Information("===== NexusExplorer 退出 =====");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
