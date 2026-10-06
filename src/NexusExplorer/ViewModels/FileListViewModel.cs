using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusExplorer.Models;
using NexusExplorer.Services;

namespace NexusExplorer.ViewModels;

/// <summary>左侧下半部分:当前分类的文件列表。</summary>
public partial class FileListViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly CategoryService _categoryService;
    private readonly FileService _fileService;

    [ObservableProperty]
    private string _headerText = "当前分类";

    public ObservableCollection<FileItem> Files => _main.CurrentFiles;

    public IRecycleBinService RecycleBin { get; set; } = null!;

    /// <summary>选择文件(播放)回调。</summary>
    public Func<FileItem, Task>? OnFileActivated { get; set; }

    /// <summary>添加文件对话框(多选)。由 View 注入。</summary>
    public Func<Task<IReadOnlyList<string>>>? PickFiles { get; set; }

    /// <summary>添加文件夹对话框。由 View 注入。</summary>
    public Func<Task<string?>>? PickDirectory { get; set; }

    /// <summary>重新定位文件对话框。返回新路径或 null。</summary>
    public Func<string, Task<string?>>? PickRelocateFile { get; set; }
    public Func<string, Task<string?>>? ShowRenameFileDialog { get; set; }

    public Action<string>? ShowError { get; set; }
    public Func<string, Task>? ShowErrorAsync { get; set; }
    private async Task ReportErrorAsync(string message)
    { if (ShowErrorAsync is not null) await ShowErrorAsync(message); else ShowError?.Invoke(message); }
    public Func<string, Task<bool>>? ShowConfirmDialog { get; set; }
    public Action<string>? ShowInfo { get; set; }
    public Action<string>? ShowImportStatus { get; set; }

    public FileListViewModel(MainViewModel main, CategoryService categoryService, FileService fileService)
    {
        _main = main;
        _categoryService = categoryService;
        _fileService = fileService;
    }

    public void UpdateHeader()
    {
        HeaderText = _main.CurrentCategory is null
            ? "当前分类"
            : $"当前分类:{_main.CurrentCategory.Name}";
    }

    [RelayCommand]
    public async Task ActivateFileAsync(FileItem? file)
    {
        if (file is null) return;
        if (!file.ExistsOnDisk)
        {
            await ReportErrorAsync($"文件已失效(可能被移动或删除):\n{file.AbsolutePath}\n\n请右键选择「重新定位」。");
            return;
        }
        await _main.SelectFileAsync(file);
    }

    [RelayCommand]
    public Task AddFilesAsync() => AddFilesToCategoryAsync(_main.CurrentCategory);
    public async Task AddFilesToCategoryAsync(Category? target)
    {
        if (target is null)
        {
            await ReportErrorAsync("请先选择一个分类。");
            return;
        }

        if (PickFiles is null) return;
        var paths = await PickFiles();
        if (paths.Count == 0) return;

        var result = await _fileService.AddRangeAsync(paths, target.Id);
        await _main.RefreshOrganizationStatesAsync();
        if (_main.CurrentCategory?.Id == target.Id) await _main.RefreshFilesAsync();
        await ReportBatchResultAsync(result);
    }

    [RelayCommand]
    public Task AddFolderAsync() => AddFolderToCategoryAsync(_main.CurrentCategory);
    public async Task AddFolderToCategoryAsync(Category? target)
    {
        if (target is null)
        {
            await ReportErrorAsync("请先选择一个分类。");
            return;
        }

        if (PickDirectory is null) return;
        var directory = await PickDirectory();
        if (string.IsNullOrEmpty(directory)) return;

        try
        {
            var result = await _fileService.ImportDirectoryAsync(directory, target.Id, _categoryService);
            await _main.RefreshTreeAsync();
            await _main.RefreshFilesAsync();
            await ReportBatchResultAsync(result);
        }
        catch (OperationException ex)
        {
            await ReportErrorAsync(ex.Message);
        }
    }

    /// <summary>Windows Explorer 拖入:文件或文件夹。</summary>
    public async Task ImportDroppedPathsAsync(string[] paths)
    {
        if (_main.CurrentCategory is null) { await ReportErrorAsync("请先双击打开分类，再拖入文件。"); return; }
        await ImportIntoAsync(paths, _main.CurrentCategory);
    }

    public async Task ImportIntoAsync(string[] paths, Category target, string? importId = null)
    {
        importId ??= Guid.NewGuid().ToString("N")[..8];
        var timing = Stopwatch.StartNew();
        Serilog.Log.Information("分类导入登记开始;导入 {ImportId};目标 {CategoryId};当前浏览 {BrowsedId};数量 {Count}", importId, target.Id, _main.CurrentCategory?.Id, paths.Length);
        var coordinator = new NexusExplorer.ApplicationLayer.ImportCoordinator(_fileService, _categoryService);
        var result = await coordinator.ImportAsync(paths, target.Id);
        Serilog.Log.Information("分类导入登记结束;导入 {ImportId};成功 {Added};失败 {Failed};耗时 {ElapsedMs:F1} ms", importId, result.Added.Count, result.Failed.Count, timing.Elapsed.TotalMilliseconds);
        timing.Restart();
        await _main.RefreshTreeAsync();
        Serilog.Log.Information("分类导入刷新结束;导入 {ImportId};目标 {CategoryId};当前浏览 {BrowsedId};耗时 {ElapsedMs:F1} ms", importId, target.Id, _main.CurrentCategory?.Id, timing.Elapsed.TotalMilliseconds);
        await ReportBatchResultAsync(result, nonBlocking: true);
    }

    private async Task ReportBatchResultAsync(BatchAddResult result, bool nonBlocking = false)
    {
        if (result.Failed.Count == 0)
        {
            (nonBlocking ? ShowImportStatus : ShowInfo)?.Invoke($"已添加 {result.Added.Count} 个文件。");
        }
        else
        {
            var errors = string.Join("\n", result.Failed.Take(5).Select(f => $"• {f.FileName}: {f.Error}"));
            if (result.Failed.Count > 5)
                errors += $"\n… 以及另外 {result.Failed.Count - 5} 个失败";
            if (nonBlocking) ShowImportStatus?.Invoke($"成功添加 {result.Added.Count} 个,失败 {result.Failed.Count} 个:\n{errors}");
            else await ReportErrorAsync($"成功添加 {result.Added.Count} 个,失败 {result.Failed.Count} 个:\n{errors}");
        }
    }

    [RelayCommand]
    public Task RemoveFileAsync(FileItem? file) => DeleteManyAsync(file is null ? Array.Empty<FileItem>() : new[] { file }, true);
    [RelayCommand]
    public Task DeleteFileAsync(FileItem? file) => DeleteManyAsync(file is null ? Array.Empty<FileItem>() : new[] { file }, false);

    [RelayCommand]
    public async Task RenameFileAsync(FileItem? file)
    {
        if (file is null || ShowRenameFileDialog is null) return;
        var name = await ShowRenameFileDialog(file.FileName);
        if (name is null || string.Equals(name, file.FileName, StringComparison.Ordinal)) return;
        try
        {
            var renamed = await _fileService.RenameAsync(file.Id, name);
            await _main.RefreshOrganizationStatesAsync();
            await _main.RefreshFilesAsync();
            ShowInfo?.Invoke($"已重命名为「{renamed.FileName}」。");
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "文件重命名失败;文件 {FileId}", file.Id);
            await _main.RefreshFilesAsync();
            await ReportErrorAsync(ex is OperationException ? ex.Message : $"重命名失败：{ex.Message}");
        }
    }

    [RelayCommand]
    public async Task RelocateFileAsync(FileItem? file)
    {
        if (file is null) return;
        if (PickRelocateFile is null) return;

        var newPath = await PickRelocateFile(file.FileName);
        if (string.IsNullOrEmpty(newPath)) return;

        try
        {
            await _fileService.RelocateAsync(file.Id, newPath);
            await _main.RefreshOrganizationStatesAsync();
            await _main.RefreshFilesAsync();
            ShowInfo?.Invoke($"已重新定位「{file.FileName}」。");
        }
        catch (OperationException ex)
        {
            await ReportErrorAsync(ex.Message);
        }
    }

    public async Task RecategorizeManyAsync(IReadOnlyList<FileItem> files, Category target)
    {
        var succeeded = new List<int>(); var errors = new List<string>();
        foreach (var f in files)
        {
            if (f.CategoryId == target.Id) continue;
            try { await _fileService.RecategorizeAsync(f.Id, target.Id); succeeded.Add(f.Id); }
            catch (Exception ex) { errors.Add($"{f.FileName}: {ex.Message}"); Serilog.Log.Error(ex, "批量归类失败"); }
        }
        await _main.OnFilesRecategorizedAsync(succeeded);
        if (errors.Count > 0) await ReportErrorAsync($"成功 {succeeded.Count}，失败 {errors.Count}\n" + string.Join("\n", errors));
    }
    public Task RecategorizeAsync(FileItem file, Category target) => RecategorizeManyAsync(new[] { file }, target);

    public async Task DeleteManyAsync(IReadOnlyList<FileItem> files, bool removeOnly)
    {
        if (files.Count == 0) return;
        if (ShowConfirmDialog is not null && !await ShowConfirmDialog(removeOnly
            ? $"从分类移除 {files.Count} 个文件？"
            : $"删除 {files.Count} 个文件？")) return;
        var failures = new List<string>(); var succeeded = 0;
        foreach (var file in files)
        {
            try
            {
                if (_main.CurrentFile?.Id == file.Id) await _main.SelectFileAsync(null);
                if (removeOnly) await _fileService.RemoveAsync(file.Id);
                else await _fileService.DeleteAsync(file.Id, RecycleBin);
                succeeded++;
            }
            catch (Exception ex) { failures.Add($"{file.FileName}: {ex.Message}"); Serilog.Log.Error(ex, "批量文件操作失败"); }
        }
        await _main.RefreshOrganizationStatesAsync();
        await _main.RefreshFilesAsync();
        if (failures.Count > 0) await ReportErrorAsync($"成功 {succeeded}，失败 {failures.Count}\n" + string.Join("\n", failures));
    }
}
