using NexusExplorer.Services;

namespace NexusExplorer.Tests;

/// <summary>复现用户序列:创建分类→移动→再创建/移动 出现 NullReferenceException。</summary>
public class CategoryReproTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Create_Move_CreateAgain_NoNullReference()
    {
        var test = await _host.Categories.CreateAsync("test", null);
        var test1 = await _host.Categories.CreateAsync("test1", null);

        // 移动 test1 到 test 下(用户操作)
        await _host.Categories.MoveAsync(test1.Id, test.Id);

        // 再创建(用户报错点)
        var ex = Record.ExceptionAsync(async () =>
            await _host.Categories.CreateAsync("test2", null));
        Assert.Null(await ex);

        // 再移动
        var ex2 = Record.ExceptionAsync(async () =>
            await _host.Categories.MoveAsync(test1.Id, null));
        Assert.Null(await ex2);

        // 树查询
        var ex3 = Record.ExceptionAsync(async () =>
            await _host.Categories.GetTreeAsync());
        Assert.Null(await ex3);
    }

    [Fact]
    public async Task CreateChild_AfterMove_NoNullReference()
    {
        var test = await _host.Categories.CreateAsync("test", null);
        var test1 = await _host.Categories.CreateAsync("test1", null);
        await _host.Categories.MoveAsync(test1.Id, test.Id);

        // 在移动后的分类下创建子分类
        var ex = Record.ExceptionAsync(async () =>
            await _host.Categories.CreateAsync("child", test1.Id));
        Assert.Null(await ex);
    }
}
