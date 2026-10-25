using System.IO;
using NexusExplorer.Services;

namespace NexusExplorer.Tests;

public class CategoryServiceTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Create_CreatesPhysicalDirectory()
    {
        var category = await _host.Categories.CreateAsync("视频", null);

        Assert.NotNull(category);
        Assert.Equal("视频", category.Name);
        Assert.True(Directory.Exists(category.PhysicalPath));
        Assert.StartsWith(_host.StorageRoot, category.PhysicalPath);
    }

    [Fact]
    public async Task Create_DuplicateNameUnderSameParent_Throws()
    {
        await _host.Categories.CreateAsync("电影", null);

        var ex = await Assert.ThrowsAsync<OperationException>(
            () => _host.Categories.CreateAsync("电影", null));
        Assert.Equal("分类名称已存在", ex.Message);
    }

    [Fact]
    public async Task Create_SameNameUnderDifferentParent_Allowed()
    {
        var parent1 = await _host.Categories.CreateAsync("视频", null);
        var parent2 = await _host.Categories.CreateAsync("音乐", null);

        var child1 = await _host.Categories.CreateAsync("科幻", parent1.Id);
        var child2 = await _host.Categories.CreateAsync("科幻", parent2.Id);

        Assert.NotEqual(child1.Id, child2.Id);
        Assert.True(Directory.Exists(child1.PhysicalPath));
        Assert.True(Directory.Exists(child2.PhysicalPath));
    }

    [Fact]
    public async Task Create_ExceedsMaxDepth_Throws()
    {
        // 建满 10 层
        var current = await _host.Categories.CreateAsync("L1", null);
        for (var i = 2; i <= CategoryService.MaxDepth; i++)
            current = await _host.Categories.CreateAsync($"L{i}", current.Id);

        // 第 11 层应该失败
        var ex = await Assert.ThrowsAsync<OperationException>(
            () => _host.Categories.CreateAsync("L11", current.Id));
        Assert.Contains("10 层", ex.Message);
    }

    [Fact]
    public async Task Create_InvalidCharacters_Throws()
    {
        await Assert.ThrowsAsync<OperationException>(() => _host.Categories.CreateAsync("a/b", null));
        await Assert.ThrowsAsync<OperationException>(() => _host.Categories.CreateAsync("a:b", null));
        await Assert.ThrowsAsync<OperationException>(() => _host.Categories.CreateAsync(" ", null));
        await Assert.ThrowsAsync<OperationException>(() => _host.Categories.CreateAsync("..", null));
    }

    [Fact]
    public async Task Rename_UpdatesPhysicalPathOfSubtree()
    {
        var root = await _host.Categories.CreateAsync("视频", null);
        var child = await _host.Categories.CreateAsync("电影", root.Id);
        var grandChild = await _host.Categories.CreateAsync("科幻", child.Id);

        await _host.Categories.RenameAsync(root.Id, "影视");

        var renamedRoot = await _host.Categories.GetByIdAsync(root.Id);
        var renamedChild = await _host.Categories.GetByIdAsync(child.Id);
        var renamedGrand = await _host.Categories.GetByIdAsync(grandChild.Id);

        Assert.Equal("影视", renamedRoot!.Name);
        Assert.EndsWith("影视", renamedRoot.PhysicalPath);
        Assert.Contains("影视", renamedChild!.PhysicalPath);
        Assert.Contains("影视", renamedGrand!.PhysicalPath);
        Assert.True(Directory.Exists(renamedGrand.PhysicalPath));
        Assert.False(Directory.Exists(root.PhysicalPath));
    }

    [Fact]
    public async Task Rename_DuplicateSibling_Throws()
    {
        var root = await _host.Categories.CreateAsync("视频", null);
        var child1 = await _host.Categories.CreateAsync("电影", root.Id);
        var child2 = await _host.Categories.CreateAsync("电视剧", root.Id);

        var ex = await Assert.ThrowsAsync<OperationException>(
            () => _host.Categories.RenameAsync(child2.Id, "电影"));
        Assert.Equal("分类名称已存在", ex.Message);
    }

    [Fact]
    public async Task Move_SubtreePreserved_PhysicalDirectoryMoved()
    {
        // 视频 → 电影 → 科幻,另一个根分类 电视剧
        var video = await _host.Categories.CreateAsync("视频", null);
        var movie = await _host.Categories.CreateAsync("电影", video.Id);
        var scifi = await _host.Categories.CreateAsync("科幻", movie.Id);
        var tv = await _host.Categories.CreateAsync("电视剧", null);

        var oldScifiPath = scifi.PhysicalPath;
        Assert.True(Directory.Exists(oldScifiPath));

        // 把 电影 移到 电视剧 下
        await _host.Categories.MoveAsync(movie.Id, tv.Id);

        var moved = await _host.Categories.GetByIdAsync(movie.Id);
        var movedChild = await _host.Categories.GetByIdAsync(scifi.Id);

        Assert.Equal(tv.Id, moved!.ParentId);
        Assert.True(Directory.Exists(moved.PhysicalPath));
        Assert.False(Directory.Exists(oldScifiPath));

        // 子分类父子关系保持
        Assert.Equal(movie.Id, movedChild!.ParentId);
        Assert.StartsWith(moved.PhysicalPath, movedChild.PhysicalPath);
    }

    [Fact]
    public async Task Move_ToOwnDescendant_Throws()
    {
        var root = await _host.Categories.CreateAsync("视频", null);
        var child = await _host.Categories.CreateAsync("电影", root.Id);
        var grandChild = await _host.Categories.CreateAsync("科幻", child.Id);

        await Assert.ThrowsAsync<OperationException>(
            () => _host.Categories.MoveAsync(root.Id, grandChild.Id));
        await Assert.ThrowsAsync<OperationException>(
            () => _host.Categories.MoveAsync(root.Id, root.Id));
    }

    [Fact]
    public async Task Move_WouldExceedDepth_Throws()
    {
        // A 链 8 层,B 链 4 层:把 B1 整链挂到 A8 下会超限(8+4=12)
        var a = await _host.Categories.CreateAsync("A1", null);
        for (var i = 2; i <= 8; i++)
            a = await _host.Categories.CreateAsync($"A{i}", a.Id);

        var bTop = await _host.Categories.CreateAsync("B1", null);
        var b = bTop;
        for (var i = 2; i <= 4; i++)
            b = await _host.Categories.CreateAsync($"B{i}", b.Id);

        await Assert.ThrowsAsync<OperationException>(() => _host.Categories.MoveAsync(bTop.Id, a.Id));
    }

    [Fact]
    public async Task Move_DuplicateNameInTarget_Throws()
    {
        var root1 = await _host.Categories.CreateAsync("视频", null);
        var child = await _host.Categories.CreateAsync("电影", root1.Id);

        var root2 = await _host.Categories.CreateAsync("音乐", null);
        var existing = await _host.Categories.CreateAsync("电影", root2.Id);

        var ex = await Assert.ThrowsAsync<OperationException>(
            () => _host.Categories.MoveAsync(child.Id, root2.Id));
        Assert.Contains("同名", ex.Message);
    }

    [Fact]
    public async Task Delete_RemovesSubtree_FilesAndDirectories()
    {
        var root = await _host.Categories.CreateAsync("视频", null);
        var child = await _host.Categories.CreateAsync("电影", root.Id);

        var filePath = _host.CreateTestFile("A.mp4");
        await _host.Files.AddAsync(filePath, child.Id);

        await _host.Categories.DeleteAsync(root.Id, _host.RecycleBin);

        // 回收站收到:文件 + 物理目录
        Assert.Contains(filePath, _host.RecycleBin.RecycledFiles);
        Assert.Contains(root.PhysicalPath, _host.RecycleBin.RecycledDirectories);

        // 数据库记录清空
        Assert.Empty(await _host.Categories.GetTreeAsync());
        Assert.Empty(await _host.Files.GetByCategoryAsync(child.Id));
        Assert.False(Directory.Exists(root.PhysicalPath));
    }

    [Fact]
    public async Task Delete_RecycleBinFails_AbortsAndKeepsRecords()
    {
        var root = await _host.Categories.CreateAsync("视频", null);
        var filePath = _host.CreateTestFile("A.mp4");
        await _host.Files.AddAsync(filePath, root.Id);

        _host.RecycleBin.FailAll = true;

        await Assert.ThrowsAsync<OperationException>(
            () => _host.Categories.DeleteAsync(root.Id, _host.RecycleBin));

        // 数据库记录保留
        var categories = await _host.Categories.GetTreeAsync();
        Assert.Single(categories);
        var files = await _host.Files.GetByCategoryAsync(root.Id);
        Assert.Single(files);
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task SortOrder_SiblingsOrderedBySortOrder()
    {
        var root = await _host.Categories.CreateAsync("视频", null);
        var c1 = await _host.Categories.CreateAsync("B分类", root.Id);
        var c2 = await _host.Categories.CreateAsync("A分类", root.Id);
        var c3 = await _host.Categories.CreateAsync("C分类", root.Id);

        var children = await _host.Categories.GetChildrenAsync(root.Id);
        // 创建顺序即默认顺序(B, A, C),不按字母
        Assert.Equal(new[] { c1.Id, c2.Id, c3.Id }, children.Select(c => c.Id).ToArray());

        // 上移 A分类
        await _host.Categories.MoveWithinSiblingsAsync(c2.Id, -1);
        children = await _host.Categories.GetChildrenAsync(root.Id);
        Assert.Equal(new[] { c2.Id, c1.Id, c3.Id }, children.Select(c => c.Id).ToArray());

        // 任意位置重排
        await _host.Categories.ReorderAsync(c3.Id, 0);
        children = await _host.Categories.GetChildrenAsync(root.Id);
        Assert.Equal(new[] { c3.Id, c2.Id, c1.Id }, children.Select(c => c.Id).ToArray());
    }

    [Fact]
    public async Task GetCategoryPath_ReturnsFullPathFromRoot()
    {
        var video = await _host.Categories.CreateAsync("视频", null);
        var movie = await _host.Categories.CreateAsync("电影", video.Id);
        var scifi = await _host.Categories.CreateAsync("科幻", movie.Id);

        var path = await _host.Categories.GetCategoryPathAsync(scifi.Id);
        Assert.Equal("视频 / 电影 / 科幻", path);
    }

    [Fact]
    public async Task AnyLevel_CanHoldFiles()
    {
        var video = await _host.Categories.CreateAsync("视频", null);
        var movie = await _host.Categories.CreateAsync("电影", video.Id);

        var f1 = _host.CreateTestFile("A.mp4");
        var f2 = _host.CreateTestFile("B.mp4");

        await _host.Files.AddAsync(f1, video.Id);
        await _host.Files.AddAsync(f2, movie.Id);

        Assert.Single(await _host.Files.GetByCategoryAsync(video.Id));
        Assert.Single(await _host.Files.GetByCategoryAsync(movie.Id));
    }
}
