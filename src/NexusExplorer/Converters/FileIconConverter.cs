using System.Globalization;
using System.Windows.Data;

namespace NexusExplorer.Converters;

/// <summary>文件扩展名 → 图标字符。</summary>
public class FileIconConverter : IValueConverter
{
    private static readonly HashSet<string> Video = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts", ".3gp" };

    private static readonly HashSet<string> Audio = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".flac", ".wav", ".aac", ".ogg", ".wma", ".m4a", ".ape", ".opus" };

    private static readonly HashSet<string> Image = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".apng", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".ico", ".svg" };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var ext = value as string ?? string.Empty;
        if (Video.Contains(ext)) return "🎬";
        if (Audio.Contains(ext)) return "🎵";
        if (Image.Contains(ext)) return "🖼️";
        return "📄";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
