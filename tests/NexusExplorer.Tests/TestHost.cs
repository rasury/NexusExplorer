using System.IO;
using Microsoft.EntityFrameworkCore;
using NexusExplorer.Data;
using NexusExplorer.Services;

namespace NexusExplorer.Tests;

/// <summary>
/// 测试基座:每个测试使用独立的临时目录(SQLite + Storage),
/// 测试结束清理,互不影响。
/// </summary>
public sealed class TestHost : IDisposable
{
    public string RootDir { get; }
    public string StorageRoot { get; }
    public IDbContextFactory<AppDbContext> DbFactory { get; }
    public CategoryService Categories { get; }
    public FileService Files { get; }
    public OrganizationService Organization { get; }
    public FakeRecycleBin RecycleBin { get; } = new();

    public TestHost()
    {
        RootDir = Path.Combine(Path.GetTempPath(), "nexus-tests-" + Guid.NewGuid().ToString("N"));
        StorageRoot = Path.Combine(RootDir, "Storage");
        Directory.CreateDirectory(StorageRoot);

        var dbPath = Path.Combine(RootDir, "test.db");
        DbFactory = new NexusExplorer.Infrastructure.DbContextFactoryStub(dbPath);

        using (var db = DbFactory.CreateDbContext())
            DatabaseInitializer.InitializeAsync(db, dbPath).GetAwaiter().GetResult();

        Categories = new CategoryService(DbFactory) { StorageRoot = StorageRoot };
        Files = new FileService(DbFactory);
        Organization = new OrganizationService(DbFactory);
    }

    /// <summary>在临时目录创建一个测试文件。</summary>
    public string CreateTestFile(string name, string content = "test")
    {
        var path = Path.Combine(RootDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootDir))
                Directory.Delete(RootDir, recursive: true);
        }
        catch
        {
            // Windows 文件锁导致的偶发失败不影响测试结果
        }
    }
}

/// <summary>回收站假实现:记录调用,可选择失败。</summary>
public class FakeRecycleBin : IRecycleBinService
{
    public List<string> RecycledFiles { get; } = new();
    public List<string> RecycledDirectories { get; } = new();

    /// <summary>设为 true 模拟回收站失败。</summary>
    public bool FailAll { get; set; }

    public bool SendFileToRecycleBin(string path)
    {
        if (FailAll) return false;
        RecycledFiles.Add(path);
        File.Delete(path);
        return true;
    }

    public bool SendDirectoryToRecycleBin(string path)
    {
        if (FailAll) return false;
        RecycledDirectories.Add(path);
        Directory.Delete(path, recursive: true);
        return true;
    }
}
