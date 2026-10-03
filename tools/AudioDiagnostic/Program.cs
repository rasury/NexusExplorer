using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using NexusExplorer.Services;
using Serilog;

namespace NexusExplorer.AudioDiagnostic;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDirectory);
        Log.Logger = new LoggerConfiguration().MinimumLevel.Debug()
            .WriteTo.File(Path.Combine(logDirectory, $"audio-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log"))
            .CreateLogger();
        try
        {
            var nativePath = FindNativeDirectory();
            Core.Initialize(nativePath);
            var hardware = ReadHardwareSetting(nativePath);
            Log.Information("独立诊断；VLC目录 {Path}；硬件配置 {Hardware}；引擎源码SHA256 {Hash}",
                nativePath, hardware, ReadEngineHash());
            if (args.Contains("--verify", StringComparer.Ordinal))
            {
                application.Dispatcher.BeginInvoke(async () =>
                {
                    try { await VerifyAsync(nativePath, hardware); application.Shutdown(0); }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "自检失败");
                        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "verification.json"),
                            JsonSerializer.Serialize(new { Success = false, Error = ex.ToString() }));
                        application.Shutdown(1);
                    }
                });
            }
            else
            {
                var window = new DiagnosticWindow(nativePath, hardware, logDirectory);
                application.MainWindow = window;
                window.Show();
            }
            return application.Run();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "诊断启动失败");
            if (!args.Contains("--verify", StringComparer.Ordinal))
                MessageBox.Show(ex.Message, "音频诊断启动失败");
            return 1;
        }
        finally { Log.CloseAndFlush(); }
    }

    private static string FindNativeDirectory()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "artifacts", "NexusExplorer-2.0.4-preview-win-x64", "libvlc", "win-x64");
                if (File.Exists(Path.Combine(candidate, "libvlc.dll"))) return candidate;
            }
        throw new FileNotFoundException("未找到固定版本目录中的 VLC 库。请在项目目录下运行此诊断程序。");
    }

    private static bool ReadHardwareSetting(string nativePath)
    {
        var settings = Path.GetFullPath(Path.Combine(nativePath, "..", "..", "appsettings.json"));
        if (!File.Exists(settings)) return true;
        using var document = JsonDocument.Parse(File.ReadAllText(settings));
        return !document.RootElement.TryGetProperty("Playback", out var playback)
            || !playback.TryGetProperty("HardwareDecoding", out var hardware) || hardware.GetBoolean();
    }

    private static string ReadEngineHash()
    {
        var assembly = typeof(Program).Assembly;
        using var stream = assembly.GetManifestResourceStream("ProductionEngine.cs")!;
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    // A muted native-output smoke check. It verifies wiring and release, not audible quality.
    private static async Task VerifyAsync(string nativePath, bool hardware)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "verification-tone.wav");
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            const int rate = 44100, bytes = rate * 2 * 2 * 4;
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)2); writer.Write(rate); writer.Write(rate * 4);
            writer.Write((short)4); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
            for (var i = 0; i < rate * 4; i++)
                for (var c = 0; c < 2; c++) writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 4000));
        }
        using var engine = new MediaPlayerService(Dispatcher.CurrentDispatcher) { HardwareDecoding = hardware };
        await engine.InitializeAsync();
        await engine.SetVolumeAsync(0);
        foreach (var audio in new[] { true, false })
        {
            await engine.StopAndReleaseAsync();
            await engine.PlayAsync(path, audio);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            var decoded = false;
            while (DateTime.UtcNow < deadline)
            {
                using var media = engine.NativePlayer!.Media;
                decoded = engine.Snapshot.IsPlaying && engine.Snapshot.Position.TotalMilliseconds > 100
                    && media is not null && media.Statistics.DecodedAudio > 0;
                if (decoded) break;
                await Task.Delay(50);
            }
            if (!decoded) throw new InvalidOperationException($"{(audio ? "Music" : "Video")} 模式未实际解码音频。");
            engine.LogAudioDiagnostics();
            await engine.TogglePauseAsync();
            var pauseDeadline = DateTime.UtcNow.AddSeconds(3);
            while (!engine.Snapshot.IsPaused && DateTime.UtcNow < pauseDeadline) await Task.Delay(25);
            if (!engine.Snapshot.IsPaused) throw new InvalidOperationException("暂停未生效。");
            var pausedAt = engine.Snapshot.Position;
            await engine.TogglePauseAsync();
            var resumeDeadline = DateTime.UtcNow.AddSeconds(3);
            while ((!engine.Snapshot.IsPlaying || engine.Snapshot.Position <= pausedAt) && DateTime.UtcNow < resumeDeadline)
                await Task.Delay(25);
            if (!engine.Snapshot.IsPlaying || engine.Snapshot.Position <= pausedAt)
                throw new InvalidOperationException("恢复播放后进度未继续推进。");
            await engine.StopAndReleaseAsync();
            using var exclusive = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "verification.json"), JsonSerializer.Serialize(new
        {
            Success = true, Modes = new[] { "Music", "Video" }, NativePath = nativePath,
            VlcVersion = engine.VlcVersion, EngineSha256 = ReadEngineHash(), AudibleQualityVerified = false
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal sealed class DiagnosticWindow : Window
{
    private readonly MediaPlayerService _engine;
    private readonly TextBox _path = new() { IsReadOnly = true, MinWidth = 200 };
    private readonly ComboBox _mode = new() { SelectedIndex = 0, Margin = new Thickness(0, 10, 0, 10) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _closing;

    public DiagnosticWindow(string nativePath, bool hardware, string logDirectory)
    {
        Title = "NexusExplorer 独立音频诊断"; Width = 700; Height = 340; MinWidth = 550; MinHeight = 300;
        _engine = new MediaPlayerService(Dispatcher) { HardwareDecoding = hardware };
        _engine.PlaybackError += (_, message) => _status.Text = message;
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "选择同一音频，分别用 A、B 模式从头播放并比较。", TextWrapping = TextWrapping.Wrap });
        var fileRow = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var browse = Button("选择音频…", (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "音频|*.mp3;*.wav;*.flac;*.aac;*.m4a;*.ogg;*.wma;*.opus;*.ape|所有文件|*.*" };
            if (dialog.ShowDialog(this) == true) _path.Text = dialog.FileName;
        });
        DockPanel.SetDock(browse, Dock.Right); fileRow.Children.Add(browse); fileRow.Children.Add(_path); panel.Children.Add(fileRow);
        _mode.Items.Add("A：软件原样（Music 角色）");
        _mode.Items.Add("B：同一引擎，仅改为 Video 角色"); panel.Children.Add(_mode);
        _mode.SelectedIndex = 0;
        var actions = new WrapPanel();
        actions.Children.Add(Button("从头播放", async (_, _) => await RunAsync(async () =>
        {
            if (!File.Exists(_path.Text)) throw new FileNotFoundException("请先选择存在的音频文件。");
            var path = _path.Text; var audio = _mode.SelectedIndex == 0;
            Log.Information("测试模式 {Mode}；文件 {Path}", audio ? "A/Music" : "B/Video", path);
            await _engine.StopAndReleaseAsync();
            await _engine.PlayAsync(path, audio);
            await Task.Delay(500); if (!_closing) _engine.LogAudioDiagnostics();
        })));
        actions.Children.Add(Button("暂停 / 继续", async (_, _) => await RunAsync(_engine.TogglePauseAsync)));
        actions.Children.Add(Button("停止", async (_, _) => await RunAsync(_engine.StopAndReleaseAsync)));
        panel.Children.Add(actions);
        var volume = new Slider { Minimum = 0, Maximum = 100, Value = 100, TickFrequency = 10, IsSnapToTickEnabled = true, Width = 280 };
        var volumeText = new TextBlock { Text = "音量：100", Margin = new Thickness(0, 8, 0, 0) };
        volume.ValueChanged += async (_, _) => { volumeText.Text = $"音量：{volume.Value:0}"; await RunAsync(() => _engine.SetVolumeAsync((int)volume.Value)); };
        panel.Children.Add(volumeText); panel.Children.Add(volume); panel.Children.Add(_status);
        panel.Children.Add(new TextBlock { Text = $"日志：{logDirectory}", TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 8, 0, 0) });
        Content = panel;
        var repository = Path.GetFullPath(Path.Combine(nativePath, "..", "..", "..", ".."));
        var sample = Path.Combine(repository, "oldtest", "Storage", "audio", "login.mp3");
        if (File.Exists(sample)) _path.Text = sample;
        _timer.Tick += (_, _) =>
        {
            var state = _engine.Snapshot;
            if (state.IsPlaying || state.IsPaused)
                _status.Text = $"{(state.IsPaused ? "已暂停" : "播放中")}  {state.Position:mm\\:ss} / {state.Duration:mm\\:ss}";
        };
        Loaded += async (_, _) => { await RunAsync(_engine.InitializeAsync); _timer.Start(); };
        Closing += async (_, args) =>
        {
            if (_closing) return;
            args.Cancel = true; _closing = true; IsEnabled = false; _timer.Stop();
            try { await _engine.StopAndReleaseAsync(); }
            catch (Exception ex) { Log.Error(ex, "关闭时停止失败"); }
            finally { _engine.Dispose(); System.Windows.Application.Current.Shutdown(); }
        };
    }
    private static Button Button(string text, RoutedEventHandler action)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 6) };
        button.Click += action; return button;
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (_closing) return;
        try { await action(); }
        catch (Exception ex) { Log.Error(ex, "诊断操作失败"); if (!_closing) _status.Text = ex.Message; }
    }
}
