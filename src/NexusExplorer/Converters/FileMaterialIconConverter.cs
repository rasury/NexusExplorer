using System.Globalization;
using System.Windows.Data;
using MaterialDesignThemes.Wpf;
using NexusExplorer.ViewModels;

namespace NexusExplorer.Converters;
public sealed class FileMaterialIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => PlayerViewModel.GetMediaKind("file" + value) switch
        { MediaKind.Audio => PackIconKind.MusicNote, MediaKind.Video => PackIconKind.Filmstrip, MediaKind.Image => PackIconKind.ImageOutline, _ => PackIconKind.FileDocumentOutline };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class AvailableWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Math.Max(32, (value is double width ? width : 320) - 8);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
