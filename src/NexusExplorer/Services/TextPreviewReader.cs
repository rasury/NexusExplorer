using System.IO;
using System.Text;

namespace NexusExplorer.Services;

public enum TextEncodingChoice { Auto, Utf8, Utf16LE, Utf16BE, GB18030 }
internal sealed record TextPreview(string Text, string EncodingName, bool Truncated, long TotalBytes);

internal static class TextPreviewReader
{
    internal const int InitialBytes = 2 * 1024 * 1024;
    internal const int MaximumBytes = 16 * 1024 * 1024;
    static TextPreviewReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    internal static async Task<TextPreview> ReadAsync(string path, TextEncodingChoice choice, int limit, CancellationToken token)
    {
        if (limit <= 0 || limit > MaximumBytes) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var total = stream.Length;
        var bytes = new byte[(int)Math.Min(total, limit + 4L)];
        var read = 0;
        while (read < bytes.Length)
        {
            var count = await stream.ReadAsync(bytes.AsMemory(read), token);
            if (count == 0) break;
            read += count;
        }
        token.ThrowIfCancellationRequested();
        var truncated = read > limit;
        var length = Math.Min(read, limit);
        var bomLength = 0;
        Encoding? bom = null;
        if (read >= 4 && bytes[0] == 0xff && bytes[1] == 0xfe && bytes[2] == 0 && bytes[3] == 0) { bom = new UTF32Encoding(false, true, true); bomLength = 4; }
        else if (read >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xfe && bytes[3] == 0xff) { bom = new UTF32Encoding(true, true, true); bomLength = 4; }
        else if (read >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf) { bom = new UTF8Encoding(false, true); bomLength = 3; }
        else if (read >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) { bom = new UnicodeEncoding(false, false, true); bomLength = 2; }
        else if (read >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff) { bom = new UnicodeEncoding(true, false, true); bomLength = 2; }
        var encoding = choice switch
        {
            TextEncodingChoice.Utf8 => new UTF8Encoding(false, true),
            TextEncodingChoice.Utf16LE => new UnicodeEncoding(false, false, true),
            TextEncodingChoice.Utf16BE => new UnicodeEncoding(true, false, true),
            TextEncodingChoice.GB18030 => Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
            _ => bom ?? new UTF8Encoding(false, true)
        };
        var start = bom is not null && bom.CodePage == encoding.CodePage ? bomLength : 0;
        string Decode(Encoding selected)
        {
            var decoder = selected.GetDecoder();
            var chars = new char[selected.GetMaxCharCount(length - start)];
            var count = decoder.GetChars(bytes, start, length - start, chars, 0, flush: !truncated);
            return new string(chars, 0, count);
        }
        string text;
        try { text = Decode(encoding); }
        catch (DecoderFallbackException) when (choice == TextEncodingChoice.Auto && bom is null)
        {
            encoding = Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            text = Decode(encoding);
        }
        token.ThrowIfCancellationRequested();
        return new(text, encoding.WebName, truncated, total);
    }
}
