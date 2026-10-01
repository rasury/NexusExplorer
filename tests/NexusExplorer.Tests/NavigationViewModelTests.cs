using NexusExplorer.Models;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;

namespace NexusExplorer.Tests;

/// <summary>导航栏新交互模型:点击=导航,绿色勾=确认归类,已属分类禁用。</summary>
public class NavigationViewModelTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private async Task<(MainViewModel Main, Category Root, Category Child)> CreateScenarioAsync()
    {
        var main = new MainViewModel(_host.Categories, _host.Files, _host.Organization, new MediaPlayerService());
        var root = await _host.Categories.CreateAsync("视频", null);
        var child = await _host.Categories.CreateAsync("科幻", root.Id);
        return (main, root, child);
    }

    [Fact]
    public async Task NavigateTo_ExpandsChildren_DoesNotRecategorize()
    {
        var (main, root, child) = await CreateScenarioAsync();
        var file = _host.CreateTestFile("A.mp4");
        var added = await _host.Files.AddAsync(file, root.Id);
        await main.SelectFileAsync(await _host.Files.GetByIdAsync(added.Id));

        // 导航到子分类(点击,不归类)
        await main.Navigation.NavigateToAsync(child);

        // 文件仍在原分类
        var latest = await _host.Files.GetByIdAsync(added.Id);
        Assert.Equal(root.Id, latest!.CategoryId);
        // 选中态已更新
        Assert.Equal(child.Id, main.Navigation.SelectedCategory!.Id);
        Assert.False(main.Navigation.IsSelectedCategoryCurrent);
    }

    [Fact]
    public async Task ConfirmRecategorize_MovesFileToSelected()
    {
        var (main, root, child) = await CreateScenarioAsync();
        var file = _host.CreateTestFile("A.mp4");
        var added = await _host.Files.AddAsync(file, root.Id);
        await main.SelectFileAsync(await _host.Files.GetByIdAsync(added.Id));

        await main.Navigation.NavigateToAsync(child);
        await main.Navigation.ConfirmRecategorizeAsync();

        var latest = await _host.Files.GetByIdAsync(added.Id);
        Assert.Equal(child.Id, latest!.CategoryId);
        // 归类后:选中分类=文件分类 → 打勾应禁用
        Assert.True(main.Navigation.IsSelectedCategoryCurrent);
    }

    [Fact]
    public async Task OnCurrentFileChanged_BreadcrumbExpandsToOwnCategory()
    {
        var (main, root, child) = await CreateScenarioAsync();
        // 文件属于子分类 child
        var file = _host.CreateTestFile("A.mp4");
        var added = await _host.Files.AddAsync(file, child.Id);

        await main.SelectFileAsync(await _host.Files.GetByIdAsync(added.Id));

        // 面包屑自动展开到 视频 › 科幻(含当前分类,可继续看其子分类)
        Assert.Equal(2, main.Navigation.Breadcrumb.Count);
        Assert.Equal(root.Id, main.Navigation.Breadcrumb[0].Id);
        Assert.Equal(child.Id, main.Navigation.Breadcrumb[1].Id);
        // 当前分类被选中且打勾禁用(已在此分类)
        Assert.True(main.Navigation.IsSelectedCategoryCurrent);
        // 同时显示子分类层(若 child 有子分类,可下钻)
        Assert.Equal(child.Id, main.Navigation.SelectedCategory!.Id);
    }

    [Fact]
    public async Task NavigateIntoOwnCategory_Allowed_AndShowsChildren()
    {
        // 问题1场景:文件在"视频",想归入"视频/科幻"。
        // 打开文件后面包屑=视频(可进入),点子分类"科幻"即选中目标。
        var (main, root, child) = await CreateScenarioAsync();
        var file = _host.CreateTestFile("A.mp4");
        var added = await _host.Files.AddAsync(file, root.Id);
        await main.SelectFileAsync(await _host.Files.GetByIdAsync(added.Id));

        // 当前文件的分类在面包屑末级,且其子分类(科幻)可见可点
        Assert.Equal(root.Id, main.Navigation.Breadcrumb[^1].Id);
        Assert.Contains(main.Navigation.Children, c => c.Id == child.Id);

        // 点子分类 → 选中科幻,可确认归类
        await main.Navigation.NavigateToAsync(child);
        await main.Navigation.ConfirmRecategorizeAsync();
        var latest = await _host.Files.GetByIdAsync(added.Id);
        Assert.Equal(child.Id, latest!.CategoryId);
    }
}
