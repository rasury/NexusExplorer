using System.IO;

namespace NexusExplorer.Services;

internal sealed record AdtsDuration(TimeSpan Duration, long Frames, int SampleRate);

/// <summary>Count ADTS sample blocks; variable packet sizes do not determine audio duration.</summary>
internal static class AdtsDurationReader
{
    private static readonly int[] Rates = { 96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350 };

    public static AdtsDuration? TryRead(string path, CancellationToken token)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        return TryRead(stream, token);
    }

    internal static AdtsDuration? TryRead(Stream stream, CancellationToken token)
    {
        var length = stream.Length;
        Span<byte> header = stackalloc byte[10];
        var discard = new byte[8192];
        var samples = new long[Rates.Length];
        long frames = 0;
        var firstRate = 0;
        while (stream.Position < length)
        {
            token.ThrowIfCancellationRequested();
            var remaining = length - stream.Position;
            if (remaining < 7) return null;
            stream.ReadExactly(header[..7]);
            if (header[..3].SequenceEqual("ID3"u8))
            {
                if (remaining < 10) return null;
                stream.ReadExactly(header[7..]);
                if (header[3] is < 2 or > 4 || header[6..10].ContainsAnyExceptInRange((byte)0, (byte)127)) return null;
                var size = ((long)header[6] << 21) | ((long)header[7] << 14) | ((long)header[8] << 7) | header[9];
                if (header[3] == 4 && (header[5] & 16) != 0) size += 10; // ID3v2.4 footer
                if (size > length - stream.Position) return null;
                stream.Seek(size, SeekOrigin.Current); continue;
            }
            if (remaining == 128 && frames > 0 && header[..3].SequenceEqual("TAG"u8))
            { stream.Seek(121, SeekOrigin.Current); break; }
            if (header[0] != 255 || (header[1] & 246) != 240) return null; // sync word and zero layer
            var rate = (header[2] >> 2) & 15;
            if (rate >= Rates.Length) return null;
            var frameLength = ((header[3] & 3) << 11) | (header[4] << 3) | (header[5] >> 5);
            var headerLength = (header[1] & 1) == 0 ? 9 : 7;
            if (frameLength < headerLength || frameLength > remaining) return null;
            var payload = frameLength - 7;
            while (payload > 0)
            {
                token.ThrowIfCancellationRequested();
                var read = stream.Read(discard, 0, Math.Min(payload, discard.Length));
                if (read == 0) return null;
                payload -= read;
            }
            samples[rate] = checked(samples[rate] + 1024 * ((header[6] & 3) + 1));
            firstRate = firstRate == 0 ? Rates[rate] : firstRate;
            frames++;
        }
        if (frames == 0) return null;
        decimal seconds = 0;
        for (var i = 0; i < Rates.Length; i++) seconds += (decimal)samples[i] / Rates[i];
        var ticks = decimal.Round(seconds * TimeSpan.TicksPerSecond);
        if (ticks > TimeSpan.MaxValue.Ticks) return null;
        return new AdtsDuration(TimeSpan.FromTicks((long)ticks), frames, firstRate);
    }
}
