using NexusExplorer.Infrastructure.Playback.Mpv;
using System.IO;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using NexusExplorer.ApplicationLayer;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public class AacDurationTests
{
    private static byte[] Frame(int size, int rate = 3, int blocks = 1, bool crc = false)
    {
        var frame = new byte[size];
        frame[0] = 255; frame[1] = crc ? (byte)240 : (byte)241;
        frame[2] = (byte)(64 | rate << 2); frame[3] = (byte)(128 | size >> 11);
        frame[4] = (byte)(size >> 3); frame[5] = (byte)((size & 7) << 5 | 31);
        frame[6] = (byte)(252 | blocks - 1); return frame;
    }

    [Fact]
    public void VariableFrameSizesCrcBlocksAndSampleRatesUseSampleDuration()
    {
        var data = Frame(15).Concat(Frame(500, blocks: 3, crc: true)).Concat(Frame(50, rate: 4)).ToArray();
        using var stream = new MemoryStream(data);
        var result = AdtsDurationReader.TryRead(stream, default);
        Assert.NotNull(result); Assert.Equal(3, result.Frames);
        Assert.InRange(Math.Abs(result.Duration.TotalSeconds - (4096d / 48000 + 1024d / 44100)), 0, .000001);
    }

    [Fact]
    public void Id3PrefixAndFooterDoNotContributeToAudioTime()
    {
        var id3 = new byte[] { 73, 68, 51, 4, 0, 0, 0, 0, 0, 5 }.Concat(new byte[5]);
        var tail = new byte[128]; tail[0] = 84; tail[1] = 65; tail[2] = 71;
        using var stream = new MemoryStream(id3.Concat(Frame(40)).Concat(tail).ToArray());
        Assert.InRange(AdtsDurationReader.TryRead(stream, default)!.Duration.TotalSeconds, .021333, .021334);
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("reserved-rate")]
    [InlineData("wrong-sync")]
    [InlineData("bad-length")]
    [InlineData("adif")]
    public void UnsupportedOrIncompleteInputDoesNotInventExactDuration(string invalid)
    {
        var data = Frame(30);
        switch (invalid)
        {
            case "truncated": data = data[..^1]; break;
            case "reserved-rate": data[2] = 124; break;
            case "wrong-sync": data[0] = 0; break;
            case "bad-length": data[3] &= 252; data[4] = 0; data[5] &= 31; break;
            case "adif": data[0] = 65; data[1] = 68; data[2] = 73; data[3] = 70; break;
        }
        using var stream = new MemoryStream(data);
        Assert.Null(AdtsDurationReader.TryRead(stream, default));
    }

    [Fact]
    public void CancellationInterruptsFrameScan()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new CancellingStream(Frame(30).Concat(Frame(40)).ToArray(), cancellation);
        Assert.Throws<OperationCanceledException>(() => AdtsDurationReader.TryRead(stream, cancellation.Token));
    }
    private sealed class CancellingStream(byte[] data, CancellationTokenSource cancellation) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count)
        { var read = base.Read(buffer, offset, count); cancellation.Cancel(); return read; }
    }

    [Fact]
    public async Task RealAacKeepsExactDurationUsesCorrectModulesAndEndsAfterSeek()
    {
        using var host = new TestHost();
        var fixture = Path.Combine(AppContext.BaseDirectory, "Assets", "Aac", "silent-6s.aac");
        Assert.True(File.Exists(fixture), "AAC playback fixture is required.");
        var path = Path.Combine(host.RootDir, "silent.aac"); File.Copy(fixture, path);
        // ffprobe counted 283 AAC-LC packets at 48 kHz; raw ADTS includes encoder padding.
        var expected = TimeSpan.FromSeconds(283 * 1024d / 48000);
        var logs = new Logs(); var previous = Log.Logger;
        using var logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(logs).CreateLogger(); Log.Logger = logger;
        try
        {
            await WpfTestHost.RunAsync(async () =>
            {
                using var engine = new MpvPlaybackEngine(); await engine.InitializeAsync();
                var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                engine.MediaEnded += () => ended.TrySetResult();
                await engine.PlayAsync(path, true);
                await Until(() => engine.Snapshot.IsPlaying && !engine.Snapshot.IsDurationPending && engine.Snapshot.Position > TimeSpan.Zero);
                Assert.False(engine.Snapshot.IsDurationEstimated);
                for (var i = 0; i < 4; i++)
                { Assert.InRange(Math.Abs(engine.Snapshot.Duration.TotalMilliseconds - expected.TotalMilliseconds), 0, 1); await Task.Delay(100); }
                var track = engine.SelectedAudioTrack;
                await engine.SetAudioTrackAsync(-1, engine.CurrentToken); Assert.Equal(-1, engine.SelectedAudioTrack);
                await engine.SetAudioTrackAsync(track, engine.CurrentToken); Assert.Equal(track, engine.SelectedAudioTrack);
                Assert.Equal("no", await engine.ReadNativeForVerificationAsync("mute"));
                await engine.SeekAsync(.5f); await Until(() => engine.Snapshot.Position.TotalSeconds >= 3 && engine.Snapshot.Position.TotalSeconds < 4.5);
                await engine.TogglePauseAsync(); await Until(() => engine.Snapshot.IsPaused);
                Assert.InRange(Math.Abs(engine.Snapshot.Duration.TotalMilliseconds - expected.TotalMilliseconds), 0, 1);
                await engine.TogglePauseAsync(); await engine.SeekAsync(.9f);
                await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.InRange(engine.Snapshot.Position.TotalSeconds, expected.TotalSeconds - .3, expected.TotalSeconds + .3);
                Assert.Contains(logs.Messages, m => m.Contains("wasapi"));
                var wave = SyntheticMedia.WriteWave(Path.Combine(host.RootDir, "next.wav"), silent: true);
                await engine.PlayAsync(wave, true); Assert.False(engine.Snapshot.IsDurationPending);
                using (var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                await engine.StopAndReleaseAsync();
                Assert.Equal(TimeSpan.Zero, engine.Snapshot.Duration);
                using var released = File.Open(wave, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            });
        }
        finally { Log.Logger = previous; }
    }

    [Theory]
    [InlineData(PlayMode.Sequential, true)]
    [InlineData(PlayMode.RepeatOne, false)]
    [InlineData(PlayMode.Shuffle, true)]
    public async Task RealPlayerUiDistinguishesPendingEstimateAndCompletedPlayback(PlayMode mode, bool completes)
    {
        using var host = new TestHost(); var category = await host.Categories.CreateAsync("A", null);
        var file = await host.Files.AddAsync(host.CreateTestFile("sample.aac"), category.Id);
        await WpfTestHost.RunAsync(async () =>
        {
            using var engine = new FakePlaybackEngine();
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, engine);
            var panel = new PlayerPanel(); panel.Initialize(main);
            try
            {
                await main.SelectCategoryAsync(category); await main.SelectFileAsync(file);
                var duration = (TextBlock)panel.FindName("DurationText"); var progress = (Slider)panel.FindName("ProgressSlider");
                engine.SetSnapshot(new(true, TimeSpan.Zero, TimeSpan.Zero, IsDurationPending: true)); panel.RefreshPlaybackUi();
                Assert.Equal("读取时长中…", duration.Text); Assert.False(progress.IsEnabled);
                engine.SetSnapshot(new(true, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), IsDurationEstimated: true)); panel.RefreshPlaybackUi();
                Assert.Equal("约 0:10", duration.Text);
                engine.SetSnapshot(new(true, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10))); panel.RefreshPlaybackUi();
                main.Player.PlayMode = mode; engine.End();
                await Until(() => main.Player.PlaybackCompleted || engine.Played.Count == 2);
                panel.RefreshPlaybackUi(); Assert.Equal(completes, main.Player.PlaybackCompleted);
                if (completes)
                {
                    Assert.Equal("0:10 · 已播放完", duration.Text); Assert.Equal(1, progress.Value); Assert.False(progress.IsEnabled);
                    Assert.Equal("重播", ((Button)panel.FindName("PlayPauseButton")).ToolTip);
                    await main.Player.TogglePlayPauseAsync(); Assert.False(main.Player.PlaybackCompleted);
                    panel.RefreshPlaybackUi(); Assert.Equal(0, progress.Value);
                }
                else Assert.DoesNotContain("已播放完", duration.Text);
            }
            finally { panel.Detach(); }
        });
    }

    private static async Task Until(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!ready()) { if (DateTime.UtcNow > deadline) throw new TimeoutException(); await Task.Delay(30); }
    }
    private sealed class Logs : ILogEventSink
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public void Emit(LogEvent entry) => Messages.Enqueue(entry.Properties.TryGetValue("Message", out var value)
            && value is ScalarValue { Value: string message } ? message : entry.RenderMessage());
    }
}
