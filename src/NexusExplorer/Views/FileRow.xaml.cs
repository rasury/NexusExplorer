using System.IO;
using System.Windows;
using System.Windows.Controls;
using NexusExplorer.Models;
using NexusExplorer.Services;
namespace NexusExplorer.Views;
public partial class FileRow : UserControl
{
    private CancellationTokenSource? _load;
    public FileRow()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadRow();
        DataContextChanged += (_, _) => { if (IsLoaded) LoadRow(); };
        Unloaded += (_, _) => { _load?.Cancel(); Cover.Source = null; };
    }
    private async void LoadRow()
    {
        _load?.Cancel(); _load?.Dispose(); _load = new();
        var token = _load.Token;
        Cover.Source = null;
        if (DataContext is not FileItem file) return;
        Metadata.Text = file.Extension.TrimStart('.').ToUpperInvariant();
        try
        {
            // Only realized rows reach this path; recycling invalidates stale work.
            await Task.Delay(100, token);
            var size = await Task.Run(() => {
                var info = new FileInfo(file.AbsolutePath);
                if (!info.Exists) return "已失效";
                double bytes = info.Length; var units = new[] { "B", "KB", "MB", "GB", "TB" }; int unit = 0;
                while (bytes >= 1024 && unit < units.Length - 1) { bytes /= 1024; unit++; }
                return $"{bytes:0.#} {units[unit]}";
            }, token);
            token.ThrowIfCancellationRequested();
            Metadata.Text += " · " + size;
            var thumbnail = await ThumbnailService.Shared.GetAsync(file.AbsolutePath, token);
            if (!token.IsCancellationRequested) Cover.Source = thumbnail.Image;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Serilog.Log.Debug(ex, "文件行预览不可用"); }
    }
}
