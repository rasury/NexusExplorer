using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NexusExplorer.Services;

namespace NexusExplorer;

/// <summary>
/// 服务定位器:仅供 XAML 生成的视图事件代码使用(避免在所有视图中传递 ServiceProvider)。
/// 主流程依赖注入优先。
/// </summary>
public static class AppServices
{
    private static ServiceProvider? _provider;

    public static OrganizationService? Organization => _provider?.GetService<OrganizationService>();

    public static FileService? FileService => _provider?.GetService<FileService>();

    public static CategoryService? Categories => _provider?.GetService<CategoryService>();

    public static void Initialize(ServiceProvider provider) => _provider = provider;
}
