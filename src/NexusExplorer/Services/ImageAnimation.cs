using System.IO;
using System.Text;
using System.Buffers.Binary;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace NexusExplorer.Services;

/// <summary>Composited image animation frames, rendered into one reusable WPF bitmap.</summary>
internal sealed class ImageAnimation : IDisposable
{
    private const long MaxDecodedBytes = 256L * 1024 * 1024;
    private const long MaxDecodedPngBytes = 384L * 1024 * 1024;
    private readonly DecodedAnimation _image;
    private readonly DispatcherTimer _timer;
    private readonly long _repeatCount;
    private long _completedLoops;
    private bool _disposed;
    public WriteableBitmap Bitmap { get; }
    internal int FrameIndex { get; private set; }
    internal int FrameCount => _image.FrameCount;
    internal bool IsRunning => _timer.IsEnabled;

    internal sealed class DecodedAnimation(int width, int height, long repeatCount, byte[][] frames, TimeSpan[] delays) : IDisposable
    {
        private Image<Bgra32>? _png;
        private byte[]? _scratch;
        private int _first;
        public int Width { get; } = width;
        public int Height { get; } = height;
        public long RepeatCount { get; } = repeatCount;
        public byte[][] Frames { get; private set; } = frames;
        public int FrameCount => _png is null ? Frames.Length : _png.Frames.Count - _first;
        public TimeSpan[] Delays { get; } = delays;
        public DecodedAnimation(Image<Bgra32> png, int first, long repeats, TimeSpan[] delays)
            : this(png.Width, png.Height, repeats, [], delays)
        { _png = png; _first = first; }
        public byte[] Pixels(int index)
        {
            if (_png is null) return Frames[index];
            _scratch ??= new byte[checked(Width * Height * 4)];
            _png.Frames[index + _first].CopyPixelDataTo(_scratch);
            return _scratch;
        }
        public void Dispose() { _png?.Dispose(); _png = null; _scratch = null; Frames = []; }
    }

    public static async Task<DecodedAnimation> DecodeGifAsync(string path, CancellationToken token)
    {
        // Close the input before returning: animation must not keep moved/deleted files open.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var info = await Image.IdentifyAsync(stream, token);
        var frames = Math.Max(1, info.FrameMetadataCollection.Count);
        // Bound both cached raw WIC frames and composited canvases, plus scratch/display buffers.
        var decodedBytes = checked((long)info.Width * info.Height * 4 * (frames * 2L + 4));
        if (decodedBytes > MaxDecodedBytes)
            throw new OperationException("GIF 解码后需要过多内存，请缩小尺寸或减少帧数后预览。");
        stream.Position = 0;
        token.ThrowIfCancellationRequested();
        var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count != frames) throw new OperationException("GIF 帧信息不完整，无法播放。");
        var metadata = info.Metadata.GetGifMetadata();
        var canvas = new byte[checked(info.Width * info.Height * 4)];
        var background = new byte[4];
        if (!info.FrameMetadataCollection[0].GetGifMetadata().HasTransparency &&
            metadata.GlobalColorTable is { } palette && metadata.BackgroundColorIndex < palette.Length)
        {
            var color = palette.Span[metadata.BackgroundColorIndex].ToPixel<SixLabors.ImageSharp.PixelFormats.Rgb24>();
            background = [color.B, color.G, color.R, 255];
            FillRectangle(canvas, info.Width, 0, 0, info.Width, info.Height, background);
        }
        var composed = new byte[frames][];
        var delays = new TimeSpan[frames];
        for (var index = 0; index < frames; index++)
        {
            token.ThrowIfCancellationRequested();
            var raw = decoder.Frames[index];
            var frameMetadata = (BitmapMetadata)raw.Metadata;
            var left = Query(frameMetadata, "/imgdesc/Left");
            var top = Query(frameMetadata, "/imgdesc/Top");
            var width = raw.PixelWidth; var height = raw.PixelHeight;
            if (left < 0 || top < 0 || left + width > info.Width || top + height > info.Height)
                throw new OperationException("GIF 局部帧超出画布范围，无法播放。");
            var control = info.FrameMetadataCollection[index].GetGifMetadata();
            // Save the actual canvas BEFORE this frame, AFTER disposal of the preceding frame.
            // ImageSharp 3.1.11's full-frame composition can restore stale pixels for disposal 2 -> 3.
            var previous = control.DisposalMethod == GifDisposalMethod.RestoreToPrevious ? (byte[])canvas.Clone() : null;
            var converted = new FormatConvertedBitmap(raw, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[checked(width * height * 4)];
            converted.CopyPixels(pixels, width * 4, 0);
            for (var y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < width; x++)
                {
                    var source = (y * width + x) * 4;
                    // GIF palette transparency is binary; transparent pixels leave the canvas intact.
                    if (pixels[source + 3] != 0)
                        pixels.AsSpan(source, 4).CopyTo(canvas.AsSpan(((top + y) * info.Width + left + x) * 4, 4));
                }
            }
            composed[index] = (byte[])canvas.Clone();
            delays[index] = TimeSpan.FromMilliseconds(control.FrameDelay <= 0 ? 100 : control.FrameDelay * 10L);
            if (previous is not null) canvas = previous;
            else if (control.DisposalMethod == GifDisposalMethod.RestoreToBackground)
                FillRectangle(canvas, info.Width, left, top, width, height, control.HasTransparency ? new byte[4] : background);
        }
        token.ThrowIfCancellationRequested();
        return new DecodedAnimation(info.Width, info.Height, ReadTotalIterations((BitmapMetadata)decoder.Metadata), composed, delays);
    }

    public static async Task<DecodedAnimation?> TryDecodePngAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        // acTL must precede IDAT. Static PNGs only read their header here, not their pixel data twice.
        var header = await ReadPngAnimationHeaderAsync(stream, token);
        if (header is null) return null;
        var (width, height, count) = header.Value;
        var bytesPerPixelSet = 4 * (count + 4L); // decoder frames, possible poster, scratch and display
        if (width <= 0 || height <= 0 || (long)width * height > MaxDecodedPngBytes / bytesPerPixelSet)
            throw new OperationException("APNG 解码后需要过多内存，请缩小尺寸或减少帧数后预览。");
        stream.Position = 0;
        var image = await Image.LoadAsync<Bgra32>(new DecoderOptions { MaxFrames = count + 1 }, stream, token);
        try
        {
            var metadata = image.Metadata.GetPngMetadata();
            // A separate default image is a poster only: it must not appear in the animation or its canvas.
            var first = metadata.AnimateRootFrame ? 0 : 1;
            var frameCount = image.Frames.Count - first;
            if (frameCount != count || frameCount <= 0) throw new OperationException("APNG 帧信息不完整，无法播放。");
            var delays = new TimeSpan[frameCount];
            for (var index = 0; index < frameCount; index++)
            {
                token.ThrowIfCancellationRequested();
                var delay = image.Frames[first + index].Metadata.GetPngMetadata().FrameDelay.ToDouble() * 1000;
                delays[index] = TimeSpan.FromMilliseconds(delay <= 0 || !double.IsFinite(delay) ? 100 : Math.Max(1, delay));
            }
            return new DecodedAnimation(image, first, metadata.RepeatCount, delays);
        }
        catch { image.Dispose(); throw; }
    }

    private static async Task<(int Width, int Height, uint Frames)?> ReadPngAnimationHeaderAsync(Stream stream, CancellationToken token)
    {
        var buffer = new byte[13];
        await stream.ReadExactlyAsync(buffer.AsMemory(0, 8), token);
        if (!buffer.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return null;
        var width = 0; var height = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            await stream.ReadExactlyAsync(buffer.AsMemory(0, 8), token);
            var length = BinaryPrimitives.ReadUInt32BigEndian(buffer);
            var type = Encoding.ASCII.GetString(buffer, 4, 4);
            if (length + 4L > stream.Length - stream.Position) throw new OperationException("PNG 文件不完整。");
            if (type is "IDAT" or "IEND") return null;
            if (type == "IHDR" && length == 13)
            {
                await stream.ReadExactlyAsync(buffer, token);
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(buffer));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(4)));
                stream.Position += 4;
            }
            else if (type == "acTL" && length == 8)
            {
                await stream.ReadExactlyAsync(buffer.AsMemory(0, 8), token);
                var frames = BinaryPrimitives.ReadUInt32BigEndian(buffer);
                if (frames == 0) throw new OperationException("APNG 未包含动画帧。");
                return (width, height, frames);
            }
            else stream.Position += length + 4L;
        }
        return null;
    }

    private static int ReadTotalIterations(BitmapMetadata metadata)
    {
        for (var index = 0; ; index++)
        {
            var query = index == 0 ? "/appext" : $"/[{index}]appext";
            if (!metadata.ContainsQuery(query) || metadata.GetQuery(query) is not BitmapMetadata extension) return 1;
            if (extension.GetQuery("/Application") is not byte[] application) continue;
            var name = Encoding.ASCII.GetString(application);
            if (name is not ("NETSCAPE2.0" or "ANIMEXTS1.0")) continue;
            if (extension.GetQuery("/Data") is byte[] data && data.Length >= 4 && data[0] == 3 && data[1] == 1)
            {
                var repeats = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
                // The GIF extension counts repeats AFTER the first play; absent extension means one play.
                return repeats == 0 ? 0 : repeats + 1;
            }
        }
    }

    private static int Query(BitmapMetadata metadata, string name) => metadata.ContainsQuery(name) ? Convert.ToInt32(metadata.GetQuery(name)) : 0;
    private static void FillRectangle(byte[] canvas, int canvasWidth, int left, int top, int width, int height, byte[] color)
    {
        for (var y = top; y < top + height; y++)
            for (var x = left; x < left + width; x++)
                color.CopyTo(canvas, (y * canvasWidth + x) * 4);
    }

    // Ownership of image transfers to this instance after successful construction.
    public ImageAnimation(DecodedAnimation image, Dispatcher dispatcher)
    {
        dispatcher.VerifyAccess();
        _image = image;
        _repeatCount = image.RepeatCount;
        Bitmap = new WriteableBitmap(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null);
        _timer = new DispatcherTimer(DispatcherPriority.Render, dispatcher);
        _timer.Tick += OnTick;
        RenderFrame();
    }

    internal TimeSpan FrameDelay(int index)
    {
        return _image.Delays[index];
    }
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (FrameCount < 2) return;
        _timer.Interval = FrameDelay(FrameIndex);
        _timer.Start();
    }
    private void OnTick(object? sender, EventArgs args)
    {
        if (_disposed) return;
        if (FrameIndex == FrameCount - 1)
        {
            _completedLoops++;
            // 0 denotes infinite repeat; otherwise count complete iterations including the first play.
            if (_repeatCount != 0 && _completedLoops >= _repeatCount) { _timer.Stop(); return; }
            FrameIndex = 0;
        }
        else FrameIndex++;
        RenderFrame();
        _timer.Interval = FrameDelay(FrameIndex);
    }
    private void RenderFrame()
    {
        Bitmap.WritePixels(new Int32Rect(0, 0, _image.Width, _image.Height), _image.Pixels(FrameIndex), _image.Width * 4, 0);
    }
    public void Dispose()
    {
        if (_disposed) return;
        Bitmap.Dispatcher.VerifyAccess();
        _disposed = true;
        _timer.Stop(); _timer.Tick -= OnTick;
        _image.Dispose();
    }
}
