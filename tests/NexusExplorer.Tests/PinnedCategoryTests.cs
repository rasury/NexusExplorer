using NexusExplorer.Services;
using NexusExplorer.ViewModels;

namespace NexusExplorer.Tests;

/// <summary>底栏快捷分类(钉)功能:服务层 Pin/Unpin/GetPinned + 导航选中联动。</summary>
public class PinnedCategoryTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Pin_ThenGetPinned_ContainsCategory()
    {
        var root = await _host.Categories.CreateAsync("视频", null);
        var child = await _host.Categories.CreateAsync("科幻", root.Id);

        Assert.Empty(await _host.Categories.GetPinnedAsync());

        await _host.Categories.PinAsync(child.Id);

        var pinned = await _host.Categories.GetPinnedAsync();
        Assert.Single(pinned);
        Assert.Equal("科幻", pinned[0].Name);
    }

    [Fact]
    public async Task Unpin_RemovesFromPinned()
    {
        var root = await _host.Categories.CreateAsync("视频", null);
        await _host.Categories.PinAsync(root.Id);
        Assert.Single(await _host.Categories.GetPinnedAsync());

        await _host.Categories.UnpinAsync(root.Id);

        Assert.Empty(await _host.Categories.GetPinnedAsync());
    }

    [Fact]
    public async Task SelectPinned_UpdatesBreadcrumbToCategoryChain()
    {
        var main = new MainViewModel(_host.Categories, _host.Files, _host.Organization, new FakePlaybackEngine());
        var root = await _host.Categories.CreateAsync("视频", null);
        var child = await _host.Categories.CreateAsync("科幻", root.Id);
        var grandChild = await _host.Categories.CreateAsync("星际穿越", child.Id);

        await _host.Categories.PinAsync(grandChild.Id);
        await main.Navigation.RefreshPinnedAsync();
        Assert.Single(main.Navigation.PinnedCategories);

        // 点击快捷分类(任意层级):面包屑展开到它的链
        await main.Navigation.SelectPinnedAsync(grandChild);

        Assert.Equal(3, main.Navigation.Breadcrumb.Count);
        Assert.Equal(root.Id, main.Navigation.Breadcrumb[0].Id);
        Assert.Equal(child.Id, main.Navigation.Breadcrumb[1].Id);
        Assert.Equal(grandChild.Id, main.Navigation.Breadcrumb[2].Id);
        Assert.Equal(grandChild.Id, main.Navigation.SelectedCategory!.Id);
    }

    [Fact]
    public async Task SelectPinned_ThenConfirm_RecategorizesFile()
    {
        // 完整链路:钉住深层分类 → 点快捷按钮选中 → 绿勾归类
        var main = new MainViewModel(_host.Categories, _host.Files, _host.Organization, new FakePlaybackEngine());
        var root = await _host.Categories.CreateAsync("视频", null);
        var target = await _host.Categories.CreateAsync("科幻", root.Id);
        await _host.Categories.PinAsync(target.Id);

        var file = _host.CreateTestFile("A.mp4");
        var added = await _host.Files.AddAsync(file, root.Id);
        await main.SelectFileAsync(await _host.Files.GetByIdAsync(added.Id));

        // 点快捷分类 → 绿勾
        await main.Navigation.SelectPinnedAsync(target);
        Assert.False(main.Navigation.IsSelectedCategoryCurrent);
        await main.Navigation.ConfirmRecategorizeAsync();

        var latest = await _host.Files.GetByIdAsync(added.Id);
        Assert.Equal(target.Id, latest!.CategoryId);
        Assert.False(main.Navigation.HasCurrentFile); // 队列耗尽停止，不能从头重播
    }
}
