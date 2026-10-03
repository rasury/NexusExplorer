using NexusExplorer.ApplicationLayer;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NexusExplorer.Models;
using NexusExplorer.Services;
using Serilog;
using SixLabors.ImageSharp;
using Image = SixLabors.ImageSharp.Image;

namespace NexusExplorer.ViewModels;
public enum MediaKind { None, Video, Audio, Image, Unsupported }
public partial class PlayerViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    public IPlaybackEngine Engine { get; }
    private CancellationTokenSource? _opening;
    private long _version;
    private readonly Random _random = new();
    private readonly SemaphoreSlim _ended = new(1, 1);
    private GifAnimation? _gif;
    [ObservableProperty] private MediaKind _kind;
    [ObservableProperty] private string? _mediaTitle;
    [ObservableProperty] private string? _mediaPath;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private TimeSpan _position;
    [ObservableProperty] private TimeSpan _duration;
    [ObservableProperty] private int _volume = 100;
    [ObservableProperty] private PlayMode _playMode = PlayMode.Sequential;
    [ObservableProperty] private ImageSource? _imageSource;
    [ObservableProperty] private double _imageScale; // 0 means fit; effective scale is computed by the view.
    public Action<string>? ShowError { get; set; }
    public event Action? StateChanged;
    public PlayerViewModel(MainViewModel main, FileService files, IPlaybackEngine engine)
    {
        _main = main; Engine = engine;
        engine.PlaybackError += OnPlaybackError; engine.MediaEnded += OnMediaEnded;
    }
    public static MediaKind GetMediaKind(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts", ".3gp" }.Contains(ext)) return MediaKind.Video;
        if (new[] { ".mp3", ".flac", ".wav", ".aac", ".ogg", ".wma", ".m4a", ".ape", ".opus" }.Contains(ext)) return MediaKind.Audio;
        if (new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".ico" }.Contains(ext)) return MediaKind.Image;
        return MediaKind.Unsupported;
    }
    public async Task PlayFileAsync(FileItem? file)
    {
        var version = Interlocked.Increment(ref _version);
        _opening?.Cancel(); _opening?.Dispose(); _opening = new CancellationTokenSource(); var token = _opening.Token;
        ClearGifAnimation();
        await Engine.StopAndReleaseAsync();
        if (version != Interlocked.Read(ref _version)) return;
        ImageSource = null; ImageScale = 0; Position = TimeSpan.Zero; Duration = TimeSpan.Zero; IsPlaying = false;
        Kind = file is null ? MediaKind.None : GetMediaKind(file.FileName); MediaTitle = file?.FileName; MediaPath = file?.AbsolutePath;
        StateChanged?.Invoke();
        if (file is null) return;
        try
        {
            if (Kind == MediaKind.Image)
            {
                if (Path.GetExtension(file.FileName).Equals(".gif", StringComparison.OrdinalIgnoreCase))
                {
                    var decoded = await Task.Run(() => GifAnimation.DecodeAsync(file.AbsolutePath, token), token);
                    if (version != Interlocked.Read(ref _version) || token.IsCancellationRequested)
                    { decoded.Dispose(); return; }
                    try { _gif = new GifAnimation(decoded, Dispatcher.CurrentDispatcher); }
                    catch { decoded.Dispose(); throw; }
                    ImageSource = _gif.Bitmap;
                    _gif.Start();
                }
                else
                {
                    var image = await Task.Run(() => { var bitmap = LoadBitmap(file.AbsolutePath); if (bitmap is not null && !bitmap.IsFrozen) bitmap.Freeze(); return bitmap; }, token);
                    if (version == Interlocked.Read(ref _version)) ImageSource = image;
                }
            }
            else if (Kind is MediaKind.Video or MediaKind.Audio)
            {
                await Engine.PlayAsync(file.AbsolutePath, Kind == MediaKind.Audio, token);
                if (version == Interlocked.Read(ref _version)) IsPlaying = true;
            }
            else ShowError?.Invoke("暂不支持预览该文件类型。");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (version == Interlocked.Read(ref _version)) OnPlaybackError(file.AbsolutePath, ex.Message); }
        if (version == Interlocked.Read(ref _version)) StateChanged?.Invoke();
    }
    private void ClearGifAnimation() { _gif?.Dispose(); _gif = null; }
    internal void ReleaseImagePreview()
    {
        Interlocked.Increment(ref _version); _opening?.Cancel();
        ClearGifAnimation(); ImageSource = null;
    }
    private static BitmapSource? LoadBitmap(string path)
    {
        BitmapSource? DecodeWithImageSharp()
        {
            using var image = Image.Load(path);
            using var memoryStream = new MemoryStream();
            image.SaveAsPng(memoryStream);
            memoryStream.Position = 0;
            var decoder = new PngBitmapDecoder(memoryStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return decoder.Frames[0];
        }

        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".webp" or ".tiff" or ".tif")
                return DecodeWithImageSharp();

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            return bitmap;
        }
        catch
        {
            return DecodeWithImageSharp();
        }
    }


    private void OnPlaybackError(string path, string message)
    { Log.Error("播放失败 {Path}: {Message}", path, message); ShowError?.Invoke(message); IsPlaying = false; StateChanged?.Invoke(); }
    private async void OnMediaEnded()
    {
        if (!await _ended.WaitAsync(0)) return;
        try
        {
            if (_main.CurrentFile is null) return;
            if (PlayMode == PlayMode.RepeatOne) await PlayFileAsync(_main.CurrentFile);
            else if (PlayMode == PlayMode.Shuffle)
            {
                var choices = _main.Session.Queue.Where(id => id != _main.CurrentFile.Id).ToList();
                if (choices.Count > 0)
                {
                    var file = await _main.Files.GetByIdAsync(choices[_random.Next(choices.Count)]);
                    if (file is not null && file.ExistsOnDisk)
                    { await _main.PlayQueuedIdAsync(file.Id); }
                }
            }
            else if (!await _main.PlayAdjacentAsync(1))
            {
                if (PlayMode == PlayMode.RepeatAll) await _main.ReplayQueueAsync();
                else await StopPlaybackAsync();
            }
        }
        catch (Exception ex) { OnPlaybackError(MediaPath ?? "", ex.Message); }
        finally { _ended.Release(); }
    }
    public async Task TogglePlayPauseAsync()
    {
        if (Kind is not (MediaKind.Video or MediaKind.Audio)) return;
        if (Engine.Snapshot.IsPlaying || Engine.Snapshot.IsPaused) await Engine.TogglePauseAsync();
        else if (_main.CurrentFile is not null) await Engine.PlayAsync(_main.CurrentFile.AbsolutePath, Kind == MediaKind.Audio);
    }
    public async Task StopPlaybackAsync() { ReleaseImagePreview(); await Engine.StopAndReleaseAsync(); IsPlaying = false; Position = TimeSpan.Zero; StateChanged?.Invoke(); }
    public Task NextAsync() => AdjacentAsync(1);
    public Task PreviousAsync() => AdjacentAsync(-1);
    private async Task AdjacentAsync(int offset)
    {
        if (await _main.PlayAdjacentAsync(offset)) return;
        if (PlayMode == PlayMode.RepeatAll) await _main.ReplayQueueAsync(offset < 0);
    }
    public Task SeekAsync(float fraction) => Engine.SeekAsync(fraction);
    public Task SetVolumeAsync(int value) { Volume = Math.Clamp(value, 0, 100); return Engine.SetVolumeAsync(Volume); }
    public void CyclePlayMode() { PlayMode = (PlayMode)(((int)PlayMode + 1) % 4); StateChanged?.Invoke(); }
    public void ResetZoom() { ImageScale = 0; StateChanged?.Invoke(); }
}
