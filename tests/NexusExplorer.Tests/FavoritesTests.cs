using System.IO;
using Microsoft.EntityFrameworkCore;
using NexusExplorer.Data;
using NexusExplorer.Models;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public class FavoritesTests
{
    [Fact]
    public async Task FavoriteSharesIdentityAndFollowsRenameMoveWithoutInvalidatingOrganization()
    {
        using var host = new TestHost();
        var a = await host.Categories.CreateAsync("A", null);
        var b = await host.Categories.CreateAsync("B", null);
        var file = await host.Files.AddAsync(host.CreateTestFile("sample.txt"), a.Id);
        await using (var db = host.DbFactory.CreateDbContext())
            await db.Categories.Where(c => c.Id == a.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsOrganized, true));
        await host.Files.SetFavoriteAsync(file.Id, true);
        await host.Files.SetFavoriteAsync(file.Id, true);
        Assert.True((await host.Categories.GetByIdAsync(a.Id))!.IsOrganized);
        Assert.Equal(file.Id, Assert.Single(await host.Files.GetByCategoryAsync(Category.FavoritesId)).Id);
        Assert.Equal(file.Id, Assert.Single(await host.Files.GetByCategoryAsync(a.Id)).Id);
        Assert.Single(await host.Files.SearchAsync("sample"));
        await host.Files.RenameAsync(file.Id, "renamed.txt");
        await host.Files.RecategorizeAsync(file.Id, b.Id);
        await host.Organization.OrganizeAsync(b.Id);
        await host.Categories.RenameAsync(b.Id, "Moved");
        var favorite = Assert.Single(await host.Files.GetByCategoryAsync(Category.FavoritesId));
        Assert.Equal(file.Id, favorite.Id); Assert.Equal("renamed.txt", favorite.FileName);
        Assert.True(File.Exists(favorite.AbsolutePath)); Assert.Equal(b.Id, favorite.CategoryId);
    }

    [Fact]
    public async Task RemovingOriginalFileOrCategoryPreservesFavoritesUntilUnstarred()
    {
        using var host = new TestHost();
        var category = await host.Categories.CreateAsync("A", null);
        var child = await host.Categories.CreateAsync("Child", category.Id);
        var one = await host.Files.AddAsync(host.CreateTestFile("one.txt"), category.Id);
        var two = await host.Files.AddAsync(host.CreateTestFile("two.txt"), child.Id);
        var normal = await host.Files.AddAsync(host.CreateTestFile("normal.txt"), child.Id);
        await host.Files.SetFavoriteAsync(one.Id, true); await host.Files.SetFavoriteAsync(two.Id, true);
        await host.Files.RemoveAsync(one.Id);
        Assert.Equal(Category.FavoritesId, (await host.Files.GetByIdAsync(one.Id))!.CategoryId);
        var removed = await host.Categories.RemoveAsync(category.Id);
        Assert.Contains(normal.Id, removed); Assert.DoesNotContain(two.Id, removed);
        Assert.Equal(2, (await host.Files.GetByCategoryAsync(Category.FavoritesId)).Count);
        await host.Files.SetFavoriteAsync(one.Id, false);
        Assert.Null(await host.Files.GetByIdAsync(one.Id)); Assert.True(File.Exists(one.AbsolutePath));
        var destination = await host.Categories.CreateAsync("Destination", null);
        await host.Files.RecategorizeAsync(two.Id, destination.Id);
        await host.Files.SetFavoriteAsync(two.Id, false);
        Assert.False((await host.Files.GetByIdAsync(two.Id))!.IsFavorite);
        Assert.True(File.Exists(two.AbsolutePath));
    }

    [Fact]
    public async Task PhysicalDeletionRemovesFavoriteAndFixedCategoryRejectsMutations()
    {
        using var host = new TestHost();
        var category = await host.Categories.CreateAsync("A", null);
        var file = await host.Files.AddAsync(host.CreateTestFile("one.txt"), category.Id);
        await host.Files.SetFavoriteAsync(file.Id, true);
        host.RecycleBin.FailAll = true;
        await Assert.ThrowsAnyAsync<Exception>(() => host.Files.DeleteAsync(file.Id, host.RecycleBin));
        Assert.Single(await host.Files.GetByCategoryAsync(Category.FavoritesId));
        host.RecycleBin.FailAll = false;
        await host.Files.DeleteAsync(file.Id, host.RecycleBin);
        Assert.Empty(await host.Files.GetByCategoryAsync(Category.FavoritesId));
        Assert.False(File.Exists(file.AbsolutePath));
        await Assert.ThrowsAsync<OperationException>(() => host.Categories.RemoveAsync(Category.FavoritesId));
        await Assert.ThrowsAsync<OperationException>(() => host.Categories.DeleteAsync(Category.FavoritesId, host.RecycleBin));
        await Assert.ThrowsAsync<OperationException>(() => host.Categories.RenameAsync(Category.FavoritesId, "Other"));
        await Assert.ThrowsAsync<OperationException>(() => host.Categories.MoveAsync(category.Id, Category.FavoritesId));
        await Assert.ThrowsAsync<OperationException>(() => host.Categories.CreateAsync("Child", Category.FavoritesId));
        await Assert.ThrowsAsync<OperationException>(() => host.Organization.OrganizeAsync(Category.FavoritesId));
    }

    [Fact]
    public async Task VersionThreeUpgradePreservesFilesAndBacksUpDatabase()
    {
        using var host = new TestHost();
        var category = await host.Categories.CreateAsync("Legacy", null);
        var file = await host.Files.AddAsync(host.CreateTestFile("legacy.txt"), category.Id);
        var path = Path.Combine(host.RootDir, "test.db");
        await using (var db = host.DbFactory.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM Categories WHERE Id=-1; DROP INDEX IX_Files_IsFavorite_FileName_Id; ALTER TABLE Files DROP COLUMN IsFavorite; PRAGMA user_version=3;");
            await DatabaseInitializer.InitializeAsync(db, path);
        }
        var restored = await host.Files.GetByIdAsync(file.Id);
        Assert.Equal(file.AbsolutePath, restored!.AbsolutePath); Assert.False(restored.IsFavorite);
        Assert.Single(Directory.GetFiles(host.RootDir, "*.before-v4-*.bak"));
        await host.Files.SetFavoriteAsync(file.Id, true);
        Assert.Single(await new FileService(host.DbFactory).GetByCategoryAsync(Category.FavoritesId));
    }

    [Fact]
    public async Task RemovingPlayingFavoritesOwnerUpdatesNavigationWithoutRestartingPlayback()
    {
        using var host = new TestHost();
        var category = await host.Categories.CreateAsync("Owner", null);
        var file = await host.Files.AddAsync(host.CreateTestFile("playing.mp3"), category.Id);
        await host.Files.SetFavoriteAsync(file.Id, true);
        await WpfTestHost.RunAsync(async () =>
        {
            using var engine = new FakePlaybackEngine();
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, engine);
            await main.SelectCategoryAsync(category); await main.SelectFileAsync(main.CurrentFiles.Single());
            await main.Category.RemoveAsync(category);
            Assert.Equal(Category.FavoritesId, main.CurrentFile!.CategoryId);
            Assert.Equal("收藏", main.Navigation.CurrentPathText);
            Assert.True(engine.Snapshot.IsPlaying); Assert.Equal(file.AbsolutePath, engine.Path);
            Assert.Single(await host.Files.GetByCategoryAsync(Category.FavoritesId));
        });
    }

    [Fact]
    public async Task FavoriteRowIsFirstAndStarCommandDoesNotSelectOrPlayFile()
    {
        using var host = new TestHost();
        var category = await host.Categories.CreateAsync("A", null);
        var file = await host.Files.AddAsync(host.CreateTestFile("one.txt"), category.Id);
        await WpfTestHost.RunAsync(async () =>
        {
            using var engine = new FakePlaybackEngine();
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, engine);
            var panel = new CategoryFilePanel(); panel.Initialize(main, host.RecycleBin);
            var window = new Window { Content = panel, Width = 500, Height = 650, ShowInTaskbar = false };
            try
            {
                window.Show(); await main.RefreshTreeAsync(); await main.SelectCategoryAsync(category); window.UpdateLayout();
                Assert.True(main.Category.RootCategories[0].IsFavorites);
                var list = (ListBox)panel.FindName("FileListBox");
                var row = Find<FileRow>(list)!; Assert.NotNull(row);
                var star = (Button)row.FindName("FavoriteButton");
                Assert.True(CategoryFilePanel.IsControlChrome(star)); Assert.Null(CategoryFilePanel.HitFile(star));
                Assert.NotNull(star.Command); Assert.Same(list.Items[0], star.CommandParameter);
                await main.FileList.ToggleFavoriteCommand.ExecuteAsync(star.CommandParameter);
                Assert.True(((FileItem)list.Items[0]).IsFavorite);
                Assert.Equal(Visibility.Visible, star.Visibility);
                Assert.Equal(Color.FromRgb(227, 175, 39), ((SolidColorBrush)star.Foreground).Color);
                Assert.Null(list.SelectedItem); Assert.Null(main.CurrentFile);
                await main.SelectCategoryAsync(Category.Favorites());
                await main.FileList.DeleteManyAsync(main.CurrentFiles.ToArray(), true);
                Assert.Empty(main.CurrentFiles);
                Assert.NotNull(await host.Files.GetByIdAsync(file.Id)); Assert.True(File.Exists(file.AbsolutePath));
            }
            finally { window.Close(); }
        });
    }
    private static T? Find<T>(DependencyObject node) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        { var child = VisualTreeHelper.GetChild(node, i); if (child is T value) return value; if (Find<T>(child) is { } nested) return nested; }
        return null;
    }
}
