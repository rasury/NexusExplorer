using System.IO;
using NexusExplorer.Services;

namespace NexusExplorer.Tests;

public class FileServiceTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Add_DoesNotMovePhysicalFile()
    {
        var category = await _host.Categories.CreateAsync("视频", null);
        var sourcePath = _host.CreateTestFile("A.mp4");

        var file = await _host.Files.AddAsync(sourcePath, category.Id);

        // 物理文件保持原位
        Assert.True(File.Exists(sourcePath));
        Assert.Equal(Path.GetFullPath(sourcePath), file.AbsolutePath);
        // 未复制到分类目录
        Assert.Empty(Directory.GetFiles(category.PhysicalPath));
    }

    [Fact]
    public async Task Add_SamePathTwice_UpdatesCategoryNotDuplicates()
    {
        var cat1 = await _host.Categories.CreateAsync("分类1", null);
        var cat2 = await _host.Categories.CreateAsync("分类2", null);
        var sourcePath = _host.CreateTestFile("A.mp4");

        await _host.Files.AddAsync(sourcePath, cat1.Id);
        var file = await _host.Files.AddAsync(sourcePath, cat2.Id);

        // 单分类约束:同一路径只有一条记录,归属最新分类
        var inCat1 = await _host.Files.GetByCategoryAsync(cat1.Id);
        var inCat2 = await _host.Files.GetByCategoryAsync(cat2.Id);
        Assert.Empty(inCat1);
        Assert.Single(inCat2);
        Assert.Equal(cat2.Id, file.CategoryId);
    }

    [Fact]
    public async Task Add_NonExistentFile_Throws()
    {
        var category = await _host.Categories.CreateAsync("视频", null);
        var ghost = Path.Combine(_host.RootDir, "ghost.mp4");

        await Assert.ThrowsAsync<OperationException>(() => _host.Files.AddAsync(ghost, category.Id));
    }

    [Fact]
    public async Task Recategorize_ChangesCategoryIdOnly()
    {
        var cat1 = await _host.Categories.CreateAsync("科幻", null);
        var cat2 = await _host.Categories.CreateAsync("动作", null);
        var sourcePath = _host.CreateTestFile("A.mp4");
        var file = await _host.Files.AddAsync(sourcePath, cat1.Id);

        await _host.Files.RecategorizeAsync(file.Id, cat2.Id);

        var updated = await _host.Files.GetByIdAsync(file.Id);
        Assert.Equal(cat2.Id, updated!.CategoryId);
        // 物理位置不变
        Assert.Equal(Path.GetFullPath(sourcePath), updated.AbsolutePath);
        Assert.True(File.Exists(sourcePath));
    }

    [Fact]
    public async Task Recategorize_PhysicalFileNotMoved()
    {
        var cat1 = await _host.Categories.CreateAsync("科幻", null);
        var cat2 = await _host.Categories.CreateAsync("动作", null);
        var sourcePath = _host.CreateTestFile("A.mp4");
        var file = await _host.Files.AddAsync(sourcePath, cat1.Id);

        await _host.Files.RecategorizeAsync(file.Id, cat2.Id);

        // 分类目录里没有文件
        Assert.Empty(Directory.GetFiles(cat1.PhysicalPath));
        Assert.Empty(Directory.GetFiles(cat2.PhysicalPath));
        Assert.True(File.Exists(sourcePath));
    }

    [Fact]
    public async Task MissingFile_DetectedAsInvalid()
    {
        var category = await _host.Categories.CreateAsync("视频", null);
        var sourcePath = _host.CreateTestFile("A.mp4");
        var file = await _host.Files.AddAsync(sourcePath, category.Id);

        // 外部删除文件
        File.Delete(sourcePath);

        var missing = await _host.Files.GetMissingFilesAsync(category.Id);
        Assert.Single(missing);
        Assert.Equal(file.Id, missing[0].Id);
    }

    [Fact]
    public async Task Relocate_UpdatesPathToNewLocation()
    {
        var category = await _host.Categories.CreateAsync("视频", null);
        var sourcePath = _host.CreateTestFile("A.mp4");
        var file = await _host.Files.AddAsync(sourcePath, category.Id);

        // 模拟外部改名
        var newPath = Path.Combine(_host.RootDir, "A-renamed.mp4");
        File.Move(sourcePath, newPath);

        await _host.Files.RelocateAsync(file.Id, newPath);

        var updated = await _host.Files.GetByIdAsync(file.Id);
        Assert.Equal(Path.GetFullPath(newPath), updated!.AbsolutePathPathForTest());
        Assert.Equal("A-renamed.mp4", updated.FileName);
    }

    [Fact]
    public async Task ImportDirectory_RecursivelyAddsAllFiles()
    {
        var category = await _host.Categories.CreateAsync("视频", null);

        var dir = Path.Combine(_host.RootDir, "media");
        var sub = Path.Combine(dir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(dir, "a.mp4"), "a");
        File.WriteAllText(Path.Combine(dir, "b.mp3"), "b");
        File.WriteAllText(Path.Combine(sub, "c.jpg"), "c");
        // 子目录的目录本身也有一层
        var subsub = Path.Combine(sub, "deep");
        Directory.CreateDirectory(subsub);
        File.WriteAllText(Path.Combine(subsub, "d.mkv"), "d");

        var result = await _host.Files.ImportDirectoryAsync(dir, category.Id);

        Assert.Equal(4, result.Added.Count);
        Assert.Empty(result.Failed);
        var files = await _host.Files.GetByCategoryAsync(category.Id);
        Assert.Equal(4, files.Count);
        // 不移动文件
        Assert.True(File.Exists(Path.Combine(subsub, "d.mkv")));
    }

    [Fact]
    public async Task Remove_DeletesRecordOnly_FileUntouched()
    {
        var category = await _host.Categories.CreateAsync("视频", null);
        var sourcePath = _host.CreateTestFile("A.mp4");
        var file = await _host.Files.AddAsync(sourcePath, category.Id);

        await _host.Files.RemoveAsync(file.Id);

        // 数据库记录删除
        Assert.Null(await _host.Files.GetByIdAsync(file.Id));
        // 源文件原位保留
        Assert.True(File.Exists(sourcePath));
        // 不进回收站
        Assert.Empty(_host.RecycleBin.RecycledFiles);
    }

    [Fact]
    public async Task Delete_SendsToRecycleBin_AndRemovesRecord()
    {
        var category = await _host.Categories.CreateAsync("视频", null);
        var sourcePath = _host.CreateTestFile("A.mp4");
        var file = await _host.Files.AddAsync(sourcePath, category.Id);

        await _host.Files.DeleteAsync(file.Id, _host.RecycleBin);

        Assert.Contains(sourcePath, _host.RecycleBin.RecycledFiles);
        Assert.False(File.Exists(sourcePath));
        Assert.Null(await _host.Files.GetByIdAsync(file.Id));
    }

    [Fact]
    public async Task Delete_RecycleBinFails_KeepsRecord()
    {
        var category = await _host.Categories.CreateAsync("视频", null);
        var sourcePath = _host.CreateTestFile("A.mp4");
        var file = await _host.Files.AddAsync(sourcePath, category.Id);

        _host.RecycleBin.FailAll = true;

        await Assert.ThrowsAsync<OperationException>(
            () => _host.Files.DeleteAsync(file.Id, _host.RecycleBin));

        Assert.NotNull(await _host.Files.GetByIdAsync(file.Id));
        Assert.True(File.Exists(sourcePath));
    }
}

file static class TestExtensions
{
    public static string AbsolutePathPathForTest(this NexusExplorer.Models.FileItem file) => file.AbsolutePath;
}
