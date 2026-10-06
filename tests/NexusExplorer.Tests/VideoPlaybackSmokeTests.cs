using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using NexusExplorer.Infrastructure.Playback.Mpv;
using NexusExplorer.Views;
using NexusExplorer.ViewModels;
using NexusExplorer.Services;
using System.Text;
using System.Text.Json;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public class VideoPlaybackSmokeTests
{
    [Fact]
    public void PinnedNativeApiAndWindowsLayoutsAreCorrect()
    {
        Assert.Equal(0x20005u, MpvNative.ApiVersion());
        Assert.Equal(24, Marshal.SizeOf<MpvNative.Event>());
        Assert.Equal(24, Marshal.SizeOf<MpvNative.Property>());
        Assert.Equal(16, Marshal.SizeOf<MpvNative.Node>());
        Assert.Equal(32, Marshal.SizeOf<MpvNative.EndFile>());
        Assert.Equal(32, Marshal.SizeOf<MpvNative.LogMessage>());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealVideoUsesPinnedCoreWasapiAndActualDecoderAndReleases(bool hardware)
    {
        using var host = new TestHost();
        var fixture = Path.Combine(AppContext.BaseDirectory, "Assets", "seek-h264-aac.mp4");
        Assert.True(File.Exists(fixture), "Required H264/AAC fixture is missing.");
        var path = Path.Combine(host.RootDir, "中文 # 视频 (1).mp4"); File.Copy(fixture, path);
        await WpfTestHost.RunAsync(async () =>
        {
            using var engine = new MpvPlaybackEngine() { HardwareDecoding = hardware };
            using var view = new MpvVideoHost();
            var window = new Window { Content = view, Width = 640, Height = 400, ShowActivated = false, ShowInTaskbar = false, Left = -5000, Top = -5000, WindowStartupLocation = WindowStartupLocation.Manual };
            window.Show(); await Dispatcher.Yield(DispatcherPriority.Loaded); engine.AttachSurface(view.Handle);
            try
            {
                await engine.PlayAsync(path, false);
                await Until(() => engine.Snapshot.Position.TotalMilliseconds > 100 && engine.AudioOutput is not null && engine.ActiveHardwareDecoder is not null);
                Assert.Equal("mpv v0.41.0-1100-gc15296420", engine.EngineVersion);
                Assert.Equal("wasapi", engine.AudioOutput);
                Assert.Equal("gpu-next", engine.VideoOutput);
                if (hardware) Assert.NotEqual("no", engine.ActiveHardwareDecoder); else Assert.Equal("no", engine.ActiveHardwareDecoder);
                Assert.NotEmpty(engine.AudioTracks);
                var evidenceDirectory = Path.GetFullPath("../../../../../artifacts/mpv-verification", AppContext.BaseDirectory);
                Directory.CreateDirectory(evidenceDirectory);
                File.WriteAllText(Path.Combine(evidenceDirectory, hardware ? "native-hardware.json" : "native-software.json"), JsonSerializer.Serialize(new
                { engine.EngineVersion, engine.ActiveHardwareDecoder, engine.AudioOutput, engine.VideoOutput, Tracks = engine.AudioTracks, Scope = "Silent synthetic fixture / real WPF HWND / native module confirmation, not listening quality" }, new JsonSerializerOptions { WriteIndented = true }));
                if (!hardware)
                {
                    var clicked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    engine.VideoClicked += () => clicked.TrySetResult();
                    var child = IntPtr.Zero;
                    EnumChildWindows(view.Handle, (hwnd, _) =>
                    { var name = new StringBuilder(128); GetClassName(hwnd, name, name.Capacity); if (name.ToString() == "mpv") child = hwnd; return true; }, IntPtr.Zero);
                    Assert.NotEqual(IntPtr.Zero, child);
                    SendMessage(child, 0x0201, (IntPtr)1, (IntPtr)((60 << 16) | 80));
                    SendMessage(child, 0x0202, IntPtr.Zero, (IntPtr)((60 << 16) | 80));
                    await clicked.Task.WaitAsync(TimeSpan.FromSeconds(3));
                }
                await engine.SetVolumeAsync(50);
                Assert.Equal("50.000000", await engine.ReadNativeForVerificationAsync("volume"));
                var track = engine.SelectedAudioTrack; Assert.True(track >= 0);
                var token = engine.CurrentToken;
                await engine.SetAudioTrackAsync(-1, token);
                Assert.Equal("no", await engine.ReadNativeForVerificationAsync("aid"));
                Assert.Equal("no", await engine.ReadNativeForVerificationAsync("mute"));
                await engine.SetAudioTrackAsync(track, token);
                Assert.Equal(track.ToString(), await engine.ReadNativeForVerificationAsync("aid"));
                await engine.SeekAsync(.5f);
                await Until(() => engine.Snapshot.Position.TotalSeconds >= engine.Snapshot.Duration.TotalSeconds * .45);
                await engine.TogglePauseAsync(); await Until(() => engine.Snapshot.IsPaused);
                await engine.SeekAsync(.2f); Assert.Equal("yes", await engine.ReadNativeForVerificationAsync("pause"));
                await engine.TogglePauseAsync(); await Until(() => engine.Snapshot.IsPlaying);
                await engine.StopAndReleaseAsync();
                using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Assert.Null(engine.CurrentPath); Assert.Empty(engine.AudioTracks);
            }
            finally { await engine.ShutdownAsync(); window.Close(); }
        });
    }
    [Fact]
    public async Task AudioRapidReplaceDoesNotReviveOldMediaAndExplicitStopDoesNotEndQueue()
    {
        using var host = new TestHost();
        var first = SyntheticMedia.WriteWave(Path.Combine(host.RootDir, "第一.wav"), silent: true);
        var second = SyntheticMedia.WriteWave(Path.Combine(host.RootDir, "第二.wav"), silent: true);
        await WpfTestHost.RunAsync(async () =>
        {
            using var engine = new MpvPlaybackEngine(); var ended = 0; engine.MediaEnded += () => ended++;
            try
            {
                var opening = engine.PlayAsync(first, true);
                var replacing = engine.PlayAsync(second, true);
                try { await opening; } catch (OperationCanceledException) { }
                await replacing;
                await Until(() => engine.Snapshot.Position.TotalMilliseconds > 100);
                Assert.Equal(second, engine.CurrentPath);
                var old = engine.CurrentToken;
                await engine.StopAndReleaseAsync();
                Assert.Equal(0, ended);
                using var exclusive1 = File.Open(first, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                using var exclusive2 = File.Open(second, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Assert.NotEqual(old, engine.CurrentToken);
            }
            finally { await engine.ShutdownAsync(); }
        });
    }
    internal static async Task Until(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (!predicate()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Native playback state not reached."); await Task.Delay(30); }
    }
    [Fact]
    public async Task SamePathReopenRejectsStaleTrackAndOrganizeDeleteWaitForNativeRelease()
    {
        using var host = new TestHost();
        var category = await host.Categories.CreateAsync("A", null);
        var path = SyntheticMedia.WriteWave(Path.Combine(host.RootDir, "organize.wav"), silent: true);
        var file = await host.Files.AddAsync(path, category.Id);
        await WpfTestHost.RunAsync(async () =>
        {
            using var engine = new MpvPlaybackEngine();
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, engine);
            try
            {
                await main.SelectFileAsync(file);
                var stale = engine.CurrentToken;
                await main.Player.PlayFileAsync(file);
                await Assert.ThrowsAsync<OperationException>(() => engine.SetAudioTrackAsync(-1, stale));
                var organized = await host.Organization.OrganizeAsync(category.Id);
                Assert.Equal(OrganizeOutcome.Moved, Assert.Single(organized).Outcome);
                var relocated = await host.Files.GetByIdAsync(file.Id);
                Assert.NotNull(relocated);
                var moved = relocated.AbsolutePath;
                Assert.Null(engine.CurrentPath); Assert.True(File.Exists(moved)); Assert.False(File.Exists(path));
                await main.SelectFileAsync(relocated);
                await host.Files.DeleteAsync(file.Id, host.RecycleBin);
                Assert.False(File.Exists(moved)); Assert.Null(engine.CurrentPath);
            }
            finally { await engine.ShutdownAsync(); }
        });
    }
    private delegate bool ChildProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, ChildProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
}
