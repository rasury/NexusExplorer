using System.Diagnostics;
using NexusExplorer.ApplicationLayer;
using Serilog;

namespace NexusExplorer.Infrastructure.Playback.Mpv;

internal sealed class MpvDiagnostics
{
    private readonly object _gate = new();
    private readonly Queue<object> _history = new();
    private long _lastSample, _lastDump;
    internal void Sample(PlaybackToken token, PlaybackSnapshot snapshot, string? hwdec, string? ao, string? vo)
    {
        var now = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            if (_lastSample != 0 && Stopwatch.GetElapsedTime(_lastSample, now) < TimeSpan.FromSeconds(1)) return;
            _lastSample = now;
            _history.Enqueue(new { At = DateTimeOffset.Now, Token = token, State = snapshot, HardwareDecoder = hwdec, AudioOutput = ao, VideoOutput = vo });
            while (_history.Count > 32) _history.Dequeue();
        }
    }
    internal void Dump(string reason, bool throttle = false)
    {
        object[] history;
        var now = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            if (throttle && _lastDump != 0 && Stopwatch.GetElapsedTime(_lastDump, now) < TimeSpan.FromSeconds(30)) return;
            _lastDump = now; history = _history.ToArray();
        }
        Log.Information("mpv 诊断标记 {Reason};最近状态 {@History}", reason, history);
    }
}
