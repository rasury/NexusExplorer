using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace NexusExplorer.Services;

/// <summary>One document, one serialized native operation; closing waits for rendering to finish.</summary>
internal sealed class PdfPreviewSession : IAsyncDisposable
{
    internal const long CacheBudget = 64L * 1024 * 1024;
    internal const int MaximumEdge = 4096;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PdfDocument? _document;
    private FileStream? _file;
    private IRandomAccessStream? _input;
    private readonly LinkedList<(int Page, int Width, int Height, BitmapSource Image, long Bytes)> _cache = new();
    private long _cacheBytes;
    private bool _closed;
    internal uint PageCount => _document?.PageCount ?? 0;
    internal long CacheBytes => Interlocked.Read(ref _cacheBytes);

    internal static async Task<PdfPreviewSession> OpenAsync(string path, string? password, CancellationToken token)
    {
        var session = new PdfPreviewSession();
        try
        {
            await Task.Run(async () =>
            {
                token.ThrowIfCancellationRequested();
                session._file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (session._file.Length >= int.MaxValue) throw new InvalidDataException("PDF 超过 2 GiB，请使用系统程序打开。");
                session._input = session._file.AsRandomAccessStream();
                // Do not abandon a native call: release the stream only after it finishes.
                session._document = await PdfDocument.LoadFromStreamAsync(session._input, password ?? "").AsTask().ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (session.PageCount == 0) throw new InvalidDataException("PDF 没有可显示的页面。");
            }, token).ConfigureAwait(false);
            return session;
        }
        catch { await session.DisposeAsync().ConfigureAwait(false); throw; }
    }

    internal async Task<Size> GetPageSizeAsync(int index, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            return await Task.Run(() =>
            {
                using var page = _document!.GetPage(checked((uint)index));
                var size = new Size(page.Size.Width, page.Size.Height);
                if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0)
                    throw new InvalidDataException("PDF 页面尺寸无效。");
                return size;
            }, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    internal async Task<BitmapSource> RenderAsync(int index, int width, int height, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            width = Math.Clamp(width, 1, MaximumEdge); height = Math.Clamp(height, 1, MaximumEdge);
            var hit = _cache.First;
            while (hit is not null && (hit.Value.Page != index || hit.Value.Width != width || hit.Value.Height != height)) hit = hit.Next;
            if (hit is not null) { _cache.Remove(hit); _cache.AddFirst(hit); return hit.Value.Image; }
            var image = await Task.Run(async () =>
            {
                token.ThrowIfCancellationRequested();
                using var page = _document!.GetPage(checked((uint)index));
                using var output = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(output, new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)width, DestinationHeight = (uint)height,
                    BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
                }).AsTask().ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                output.Seek(0);
                using var readable = output.AsStreamForRead();
                var bitmap = new BitmapImage(); bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = readable;
                bitmap.EndInit(); bitmap.Freeze(); return bitmap;
            }, token).ConfigureAwait(false);
            var bytes = (long)image.PixelWidth * image.PixelHeight * ((image.Format.BitsPerPixel + 7) / 8);
            if (bytes <= CacheBudget)
            {
                _cache.AddFirst((index, width, height, image, bytes)); Interlocked.Add(ref _cacheBytes, bytes);
                while (CacheBytes > CacheBudget || _cache.Count > 8)
                { Interlocked.Add(ref _cacheBytes, -_cache.Last!.Value.Bytes); _cache.RemoveLast(); }
            }
            return image;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_closed) return;
            _closed = true; _cache.Clear(); Interlocked.Exchange(ref _cacheBytes, 0);
            _document = null;
            _input?.Dispose(); _input = null; _file?.Dispose(); _file = null;
        }
        finally { _gate.Release(); }
    }

    internal static async Task<BitmapSource> ThumbnailAsync(string path, CancellationToken token = default)
    {
        await using var session = await OpenAsync(path, null, token).ConfigureAwait(false);
        var size = await session.GetPageSizeAsync(0, token).ConfigureAwait(false);
        var ratio = 320 / Math.Max(size.Width, size.Height);
        return await session.RenderAsync(0, Math.Max(1, (int)(size.Width * ratio)), Math.Max(1, (int)(size.Height * ratio)), token).ConfigureAwait(false);
    }
}
