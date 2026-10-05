using System.Windows;
using System.Windows.Controls;
using System.Globalization;
using MaterialDesignThemes.Wpf;
using NexusExplorer.Converters;
using NexusExplorer.Models;
using NexusExplorer.Services;

namespace NexusExplorer.Views;

public partial class FileThumbnailPreview : UserControl
{
    public FileThumbnailPreview() => InitializeComponent();
    internal void ShowLoading(FileItem file)
    {
        FileTitle.Text = file.FileName;
        FallbackIcon.Kind = (PackIconKind)new FileMaterialIconConverter().Convert(file.Extension, typeof(PackIconKind), null!, CultureInfo.InvariantCulture);
        PreviewImage.Source = null; PreviewImage.Visibility = Visibility.Collapsed;
        FallbackIcon.Visibility = Visibility.Visible; PreviewStatus.Visibility = Visibility.Visible; PreviewStatus.Text = "加载预览…";
    }
    internal void ShowResult(ThumbnailResult result)
    {
        PreviewImage.Source = result.Image;
        PreviewImage.Visibility = result.Image is null ? Visibility.Collapsed : Visibility.Visible;
        FallbackIcon.Visibility = result.Image is null ? Visibility.Visible : Visibility.Collapsed;
        PreviewStatus.Visibility = result.Image is null ? Visibility.Visible : Visibility.Collapsed;
        PreviewStatus.Text = result.Status == ThumbnailStatus.Missing ? "文件已失效" : "无可用缩略图";
    }
    internal void Clear() => PreviewImage.Source = null;
}
