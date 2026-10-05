using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusExplorer.ViewModels;
using Serilog;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SharpImage = SixLabors.ImageSharp.Image;

namespace NexusExplorer.Services;

internal static class ThumbnailDecoder
{
    internal const int MaxEdge = 320;
    internal static BitmapSource? Decode(string path)
    {
        var kind = PlayerViewModel.GetMediaKind(path);
        if (kind == MediaKind.Image)
        {
            try { return DecodeImage(path); }
            catch (Exception ex) { Log.Debug(ex, "图片缩略图解码回退到 Windows {Path}", path); }
        }
        return kind is MediaKind.Image or MediaKind.Video or MediaKind.Audio ? DecodeShell(path) : null;
    }

    private static BitmapSource? DecodeImage(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var options = new DecoderOptions { MaxFrames = 1, TargetSize = new SixLabors.ImageSharp.Size(MaxEdge, MaxEdge) };
        var info = SharpImage.Identify(options, stream);
        if ((long)info.Width * info.Height > 80_000_000) return null;
        stream.Position = 0;
        using var image = SharpImage.Load<Rgba32>(options, stream);
        image.Mutate(c => c.AutoOrient());
        if (image.Width > MaxEdge || image.Height > MaxEdge)
            image.Mutate(c => c.Resize(new ResizeOptions { Size = new(MaxEdge, MaxEdge), Mode = SixLabors.ImageSharp.Processing.ResizeMode.Max }));
        var pixels = new byte[image.Width * image.Height * 4]; image.CopyPixelDataTo(pixels);
        // ImageSharp stores RGBA; WPF's Bgra32 expects blue and red in the opposite order.
        for (var i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, pixels, image.Width * 4);
        bitmap.Freeze(); return bitmap;
    }

    internal static BitmapSource? DecodeShell(string path)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        var hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
        if (hr < 0 || factory is null) { Log.Debug("Windows 缩略图创建失败 HRESULT {Result:X8}", hr); return null; }
        try
        {
            // Require an actual thumbnail; a generic icon is rendered by the Material preview instead.
            foreach (var flags in new uint[] { 0x08 | 0x10, 0x08 })
            {
                IntPtr handle = IntPtr.Zero;
                try
                {
                    hr = factory.GetImage(new NativeSize(MaxEdge, MaxEdge), flags, out handle);
                    Log.Debug("Windows 缩略图 HRESULT {Result:X8};缓存优先 {CacheOnly}", hr, flags == (0x08 | 0x10));
                    if (hr < 0 || handle == IntPtr.Zero) continue;
                    BitmapSource bitmap = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    var scale = Math.Min(1, (double)MaxEdge / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
                    if (scale < 1) bitmap = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
                    bitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                    bitmap.Freeze(); return bitmap;
                }
                finally { if (handle != IntPtr.Zero) DeleteObject(handle); }
            }
            return null;
        }
        finally { Marshal.FinalReleaseComObject(factory); }
    }

    [StructLayout(LayoutKind.Sequential)] private readonly struct NativeSize(int width, int height)
    { public readonly int Width = width; public readonly int Height = height; }
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    { [PreserveSig] int GetImage(NativeSize size, uint flags, out IntPtr bitmap); }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr binding, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? factory);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr handle);
}
