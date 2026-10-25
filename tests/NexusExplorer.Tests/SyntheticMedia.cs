using System.IO;
namespace NexusExplorer.Tests;
internal static class SyntheticMedia
{
    private static void FourCc(BinaryWriter writer, string text) => writer.Write(System.Text.Encoding.ASCII.GetBytes(text));
    public static string WriteWave(string path, bool silent = false)
    {
        const int rate = 44100, channels = 2, seconds = 4;
        using var writer = new BinaryWriter(File.Create(path));
        var bytes = rate * channels * seconds * 2;
        FourCc(writer, "RIFF"); writer.Write(36 + bytes); FourCc(writer, "WAVE"); FourCc(writer, "fmt "); writer.Write(16);
        writer.Write((short)1); writer.Write((short)channels); writer.Write(rate); writer.Write(rate * channels * 2); writer.Write((short)(channels * 2)); writer.Write((short)16);
        FourCc(writer, "data"); writer.Write(bytes);
        for (var i = 0; i < rate * seconds; i++) for (var c = 0; c < channels; c++) writer.Write(silent ? (short)0 : (short)(Math.Sin(2 * Math.PI * (c == 0 ? 440 : 660) * i / rate) * 8000));
        return path;
    }
    public static string WriteAvi(string path, int width = 64, int height = 48, bool audio = false, bool silentAudio = false)
    {
        const int rate = 10, frames = 40, audioRate = 44100, audioFrameBytes = audioRate / rate * 4;
        var size = width * height * 3;
        using var w = new BinaryWriter(File.Create(path));
        FourCc(w, "RIFF"); w.Write(0); FourCc(w, "AVI ");
        long Begin(string type) { FourCc(w, "LIST"); var p = w.BaseStream.Position; w.Write(0); FourCc(w, type); return p; }
        void End(long p) { var end = w.BaseStream.Position; w.BaseStream.Position = p; w.Write((int)(end - p - 4)); w.BaseStream.Position = end; }
        var hdrl = Begin("hdrl");
        FourCc(w, "avih"); w.Write(56); w.Write(1000000 / rate); w.Write(size * rate); w.Write(0); w.Write(0);
        w.Write(frames); w.Write(0); w.Write(audio ? 2 : 1); w.Write(size); w.Write(width); w.Write(height); for (var i = 0; i < 4; i++) w.Write(0);
        var strl = Begin("strl");
        FourCc(w, "strh"); w.Write(56); FourCc(w, "vids"); FourCc(w, "DIB "); w.Write(0); w.Write((short)0); w.Write((short)0);
        w.Write(0); w.Write(1); w.Write(rate); w.Write(0); w.Write(frames); w.Write(size); w.Write(-1); w.Write(0); w.Write((short)0); w.Write((short)0); w.Write((short)width); w.Write((short)height);
        FourCc(w, "strf"); w.Write(40); w.Write(40); w.Write(width); w.Write(height); w.Write((short)1); w.Write((short)24); w.Write(0); w.Write(size); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        End(strl);
        if (audio)
        {
            var audioList = Begin("strl");
            FourCc(w, "strh"); w.Write(56); FourCc(w, "auds"); w.Write(0); w.Write(0); w.Write((short)0); w.Write((short)0);
            w.Write(0); w.Write(4); w.Write(audioRate * 4); w.Write(0); w.Write(audioRate * frames / rate); w.Write(audioFrameBytes); w.Write(-1); w.Write(4);
            for (var i = 0; i < 4; i++) w.Write((short)0);
            FourCc(w, "strf"); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(audioRate); w.Write(audioRate * 4); w.Write((short)4); w.Write((short)16);
            End(audioList);
        }
        End(hdrl); var movi = Begin("movi");
        for (var frame = 0; frame < frames; frame++)
        {
            FourCc(w, "00db"); w.Write(size);
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            { w.Write((byte)(x * 3)); w.Write((byte)(y * 4)); w.Write((byte)(64 + frame * 3)); }
            if (audio)
            {
                FourCc(w, "01wb"); w.Write(audioFrameBytes);
                for (var sample = 0; sample < audioRate / rate; sample++)
                    for (var channel = 0; channel < 2; channel++)
                        w.Write(silentAudio ? (short)0 : (short)(Math.Sin(2 * Math.PI * (channel == 0 ? 440 : 660) * (frame * audioRate / rate + sample) / audioRate) * 8000));
            }
        }
        End(movi); var length = w.BaseStream.Length; w.BaseStream.Position = 4; w.Write((int)length - 8); return path;
    }
}

// The native log assertion temporarily uses the process-wide Serilog logger.
[CollectionDefinition("NativePlaybackLogging", DisableParallelization = true)]
public sealed class NativePlaybackLoggingCollection { }
