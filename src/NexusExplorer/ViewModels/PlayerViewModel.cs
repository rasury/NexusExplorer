using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using NexusExplorer.Models;
using NexusExplorer.Services;
using LibVLCSharp.Shared;
using Serilog;
using SixLabors.ImageSharp;
using Image = SixLabors.ImageSharp.Image;

namespace NexusExplorer.ViewModels;

/// <summary>当前文件的媒体类型。</summary>
public enum MediaKind
{
    None,
    Video,
    Audio,
    Image,
    Unsupported
}

/// <summary>右侧播放面板 ViewModel。物理渲染在 View,VLC 状态在此同步。</summary>
public partial class PlayerViewModel : ObservableObject
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts", ".3gp"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".aac", ".ogg", ".wma", ".m4a", ".ape", ".opus"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".ico", ".svg"
    };

    private readonly MainViewModel _main;
    private readonly FileService _fileService;
    private readonly MediaPlayerService _mediaPlayer;

    [ObservableProperty]
    private MediaKind _kind = MediaKind.None;

    [ObservableProperty]
    private string? _mediaTitle;

    [ObservableProperty]
    private string? _mediaPath;

    // ---------- 播放器状态 ----------
    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private TimeSpan _position;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private int _volume = 100; // 默认 100=VLC 原样输出(软件音量<100 有量化损耗)

    /// <summary>用户是否调整过音量(未调整时绝不写 VLC,保持 0 损耗直出)。</summary>
    private bool _userAdjustedVolume;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRepeatOne), nameof(IsRepeatAll), nameof(IsShuffle), nameof(IsSequential))]
    private PlayMode _playMode = PlayMode.Sequential;

    public bool IsRepeatOne => PlayMode == PlayMode.RepeatOne;
    public bool IsRepeatAll => PlayMode == PlayMode.RepeatAll;
    public bool IsShuffle => PlayMode == PlayMode.Shuffle;
    public bool IsSequential => PlayMode == PlayMode.Sequential;

    // ---------- 图片状态 ----------
    [ObservableProperty]
    private ImageSource? _imageSource;

    [ObservableProperty]
    private double _imageScale = 1.0;

    public event Action? StateChanged;

    public Action<string>? ShowError { get; set; }

    public PlayerViewModel(MainViewModel main, FileService fileService, MediaPlayerService mediaPlayer)
    {
        _main = main;
        _fileService = fileService;
        _mediaPlayer = mediaPlayer;

        _mediaPlayer.PlaybackError += OnPlaybackError;
        _mediaPlayer.MediaEnded += OnMediaEnded;
    }

    /// <summary>当前媒体播完(VLC 线程):按播放模式前进,统一经 Main 以同步左侧选中。</summary>
    private void OnMediaEnded()
    {
        _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            switch (PlayMode)
            {
                case PlayMode.RepeatOne:
                    _mediaPlayer.Replay();
                    break;

                case PlayMode.Shuffle:
                    await PlayRandomAsync();
                    break;

                default:
                    if (!await _main.PlayAdjacentAsync(1)
                        && PlayMode == PlayMode.RepeatAll
                        && _main.CurrentFiles.Count > 0)
                    {
                        await _main.SelectFileAsync(_main.CurrentFiles[0]);
                    }
                    break;
            }
        });
    }

    private async Task PlayRandomAsync()
    {
        var files = _main.CurrentFiles.Where(f => f.ExistsOnDisk).ToList();
        if (files.Count == 0) return;

        var candidates = files.Where(f => f.Id != _main.CurrentFile?.Id).ToList();
        var pick = candidates.Count == 0 ? files[0] : candidates[_random.Next(candidates.Count)];
        await _main.SelectFileAsync(pick);
    }

    private readonly Random _random = new();

    public MediaPlayerService MediaPlayer => _mediaPlayer;

    /// <summary>根据扩展名判断媒体类型。</summary>
    public static MediaKind GetMediaKind(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (VideoExtensions.Contains(ext)) return MediaKind.Video;
        if (AudioExtensions.Contains(ext)) return MediaKind.Audio;
        if (ImageExtensions.Contains(ext)) return MediaKind.Image;
        return MediaKind.Unsupported;
    }

    /// <summary>打开文件:图片直接解码,视频/音频交给 VLC,其他类型提示。</summary>
    public async Task PlayFileAsync(FileItem? file)
    {
        // 停掉旧媒体
        _mediaPlayer.Stop();
        ImageSource = null;
        ImageScale = 1.0;
        Position = TimeSpan.Zero;
        Duration = TimeSpan.Zero;
        IsPlaying = false;

        if (file is null)
        {
            Kind = MediaKind.None;
            MediaTitle = null;
            MediaPath = null;
            StateChanged?.Invoke();
            return;
        }

        MediaTitle = file.FileName;
        MediaPath = file.AbsolutePath;
        Kind = GetMediaKind(file.FileName);

        switch (Kind)
        {
            case MediaKind.Image:
                await LoadImageAsync(file);
                break;

            case MediaKind.Video:
            case MediaKind.Audio:
                StartVlcPlayback();
                break;

            case MediaKind.Unsupported:
                ShowError?.Invoke($"暂不支持预览该文件类型:\n{file.FileName}");
                break;
        }

        StateChanged?.Invoke();
    }

    private async Task LoadImageAsync(FileItem file)
    {
        try
        {
            // 解码+Freeze 都在后台线程内完成——WPF Freezable 的属性读取
            // (含 IsFrozen)同样受线程亲和保护,任何访问都不得跨线程;
            // 冻结后的位图才能安全交给 UI 线程
            var source = await Task.Run(() =>
            {
                var bitmap = LoadBitmap(file.AbsolutePath);
                if (bitmap is not null && !bitmap.IsFrozen)
                    bitmap.Freeze();
                return bitmap;
            });
            ImageSource = source;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "图片加载失败: {Path}", file.AbsolutePath);
            ShowError?.Invoke($"图片加载失败: {file.FileName}");
            Kind = MediaKind.Unsupported;
        }
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

    private void StartVlcPlayback()
    {
        // 播放列表 = 当前分类的媒体文件(按列表顺序)
        var playlist = _main.CurrentFiles
            .Where(f => GetMediaKind(f.FileName) is MediaKind.Video or MediaKind.Audio && f.ExistsOnDisk)
            .Select(f => f.AbsolutePath)
            .ToList();

        var index = playlist.FindIndex(p => string.Equals(p, MediaPath, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            playlist.Insert(0, MediaPath!);
            index = 0;
        }

        _mediaPlayer.Mode = PlayMode;
        IsPlaying = true;
        // VLC 冷启动初始化可能未完成(Lazy 触发会阻塞),放后台线程执行
        _ = Task.Run(() =>
        {
            _mediaPlayer.Play(playlist, index);
            // 用户没动过音量 → 保持 VLC 默认 100(零损耗);
            // VLC 的音量是软件衰减,主动写 <100 会造成量化损失
            if (_userAdjustedVolume && _mediaPlayer.IsVlcReady)
                _mediaPlayer.Player.Volume = Volume;
        });
    }

    // ---------- 播放控制 ----------

    public void TogglePlayPause()
    {
        if (Kind is not (MediaKind.Video or MediaKind.Audio)) return;
        if (!_mediaPlayer.IsVlcReady) return;

        // VLC 的 Pause 对已停止/播完的播放器无效,此时需要重新 Play
        var state = _mediaPlayer.Player.State;
        if (state is VLCState.Playing or VLCState.Paused)
            _mediaPlayer.PlayPause();
        else
            _mediaPlayer.Replay();
        // 播放状态由 UI 轮询 VLC 同步
    }

    public void StopPlayback()
    {
        _mediaPlayer.Stop();
        IsPlaying = false;
        Position = TimeSpan.Zero;
        StateChanged?.Invoke();
    }

    public void Next() => _ = PlayAdjacentAsync(1);

    public void Previous() => _ = PlayAdjacentAsync(-1);

    /// <summary>前进/后退统一走 Main(同步左侧选中);列表循环模式下到尾回绕。</summary>
    private async Task PlayAdjacentAsync(int offset)
    {
        if (await _main.PlayAdjacentAsync(offset))
            return;

        if (_main.CurrentFiles.Count == 0) return;

        if (offset > 0 && PlayMode is PlayMode.RepeatAll or PlayMode.RepeatOne)
            await _main.SelectFileAsync(_main.CurrentFiles[0]);
        else if (offset < 0 && PlayMode is PlayMode.RepeatAll or PlayMode.RepeatOne)
            await _main.SelectFileAsync(_main.CurrentFiles[^1]);
    }

    public void Seek(float fraction)
    {
        if (Kind is not (MediaKind.Video or MediaKind.Audio)) return;
        if (!_mediaPlayer.IsVlcReady) return;

        var lengthMs = _mediaPlayer.Player.Length;
        if (lengthMs <= 0) return;

        fraction = Math.Clamp(fraction, 0f, 1f);
        _mediaPlayer.Player.SeekTo(TimeSpan.FromMilliseconds(lengthMs * fraction));
    }

    public void SetVolume(int volume)
    {
        Volume = Math.Clamp(volume, 0, 100);
        _userAdjustedVolume = true;
        if (_mediaPlayer.IsVlcReady)
            _mediaPlayer.Player.Volume = Volume;
    }

    public void CyclePlayMode()
    {
        PlayMode = PlayMode switch
        {
            PlayMode.Sequential => PlayMode.RepeatAll,
            PlayMode.RepeatAll => PlayMode.RepeatOne,
            PlayMode.RepeatOne => PlayMode.Shuffle,
            _ => PlayMode.Sequential
        };
        _mediaPlayer.Mode = PlayMode;
        StateChanged?.Invoke();
    }

    // ---------- VLC 事件(已由 View 调度回 UI 线程) ----------

    public void OnPlaybackError(string path, string message)
    {
        ShowError?.Invoke(message);
        IsPlaying = false;
        StateChanged?.Invoke();
    }

    /// <summary>图片缩放(滚轮)。</summary>
    public void Zoom(double delta)
    {
        ImageScale = Math.Clamp(ImageScale * delta, 0.1, 10.0);
        StateChanged?.Invoke();
    }

    public void ResetZoom()
    {
        ImageScale = 1.0;
        StateChanged?.Invoke();
    }
}
