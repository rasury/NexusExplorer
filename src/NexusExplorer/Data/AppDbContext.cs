using Microsoft.EntityFrameworkCore;
using NexusExplorer.Models;

namespace NexusExplorer.Data;

public sealed class AppDbContext : DbContext
{
    private readonly string _path;
    private readonly bool _pooling;
    public AppDbContext(string path, bool pooling = true) { _path = path; _pooling = pooling; }
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<FileItem> Files => Set<FileItem>();
    public DbSet<DirectoryLocation> DirectoryLocations => Set<DirectoryLocation>();
    public DbSet<FileOperation> FileOperations => Set<FileOperation>();
    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = _path, ForeignKeys = true, Pooling = _pooling }.ToString());
        // SQLite's built-in NOCASE is ASCII-only; Windows paths require Unicode case matching.
        connection.CreateCollation("NOCASE", (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left, right));
        options.UseSqlite(connection, contextOwnsConnection: true);
    }
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Category>().Ignore(c => c.Depth);
        model.Entity<Category>().HasOne(c => c.Parent).WithMany(c => c.Children).HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Category>().HasOne(c => c.Location).WithMany().HasForeignKey(c => c.DirectoryLocationId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Category>().HasIndex(c => new { c.ParentId, c.Name });
        model.Entity<FileItem>().Ignore(f => f.ExistsOnDisk).Ignore(f => f.Extension);
        model.Entity<FileItem>().HasOne(f => f.Category).WithMany(c => c.Files).HasForeignKey(f => f.CategoryId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<FileItem>().HasOne(f => f.Location).WithMany().HasForeignKey(f => f.DirectoryLocationId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<FileItem>().HasIndex(f => f.AbsolutePath);
        model.Entity<FileItem>().Property(f => f.AbsolutePath).UseCollation("NOCASE");
        model.Entity<DirectoryLocation>().HasOne(d => d.Parent).WithMany().HasForeignKey(d => d.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}
