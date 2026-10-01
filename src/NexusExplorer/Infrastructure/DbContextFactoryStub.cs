using Microsoft.EntityFrameworkCore;
using NexusExplorer.Data;

namespace NexusExplorer.Infrastructure;

/// <summary>基于固定 SQLite 文件路径的 DbContextFactory。</summary>
public class DbContextFactoryStub : IDbContextFactory<AppDbContext>
{
    private readonly string _databasePath;

    public DbContextFactoryStub(string databasePath)
    {
        _databasePath = databasePath;
    }

    public AppDbContext CreateDbContext() => new(_databasePath);
}
