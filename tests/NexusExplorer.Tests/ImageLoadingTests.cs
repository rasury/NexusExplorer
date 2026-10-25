using System.IO;
using System.Windows.Media.Imaging;

namespace NexusExplorer.Tests;

/// <summary>
/// 图片解码验证:LoadBitmap 同等逻辑(BMP 写入磁盘→解码→Freeze)
/// 在后台线程执行后 UI 线程可用(跨线程 Freeze 异常的回归测试)。
/// </summary>
public class ImageLoadingTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void LoadAndFreezeBitmap_OffUiThread_Works()
    {
        string? failure = null;
        int width = 0, height = 0;
        bool frozen = false;

        var thread = new Thread(() =>
        {
            try
            {
                // 生成一张真实 BMP(纯 GDI 可写,无 WPF 依赖)
                var path = Path.Combine(_host.RootDir, "test.bmp");
                var w = 64; var h = 48;
                var rowData = new byte[w * 3];
                var rows = new List<byte[]>();
                for (var y = 0; y < h; y++)
                {
                    var row = new byte[rowData.Length];
                    for (var x = 0; x < w; x++)
                    {
                        row[x * 3 + 0] = (byte)(x * 4);      // B
                        row[x * 3 + 1] = (byte)(y * 5);      // G
                        row[x * 3 + 2] = 0x80;               // R
                    }
                    rows.Add(row);
                }
                using (var fs = new FileStream(path, FileMode.Create))
                using (var writer = new BinaryWriter(fs))
                {
                    var rowStride = w * 3;
                    var pixelBytes = rowStride * h;
                    var fileSize = 54 + pixelBytes;
                    writer.Write((byte)'B'); writer.Write((byte)'M');
                    writer.Write(fileSize); writer.Write(0); writer.Write(54);
                    writer.Write(40); writer.Write(w); writer.Write(h);
                    writer.Write((short)1); writer.Write((short)24);
                    writer.Write(0); writer.Write(pixelBytes);
                    writer.Write(2835); writer.Write(2835); writer.Write(0); writer.Write(0);
                    foreach (var row in rows) writer.Write(row);
                }

                // 与 PlayerViewModel.LoadBitmap 相同路径:后台线程解码 + Freeze
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                if (!bitmap.IsFrozen)
                    bitmap.Freeze();

                frozen = bitmap.IsFrozen;
                width = bitmap.PixelWidth;
                height = bitmap.PixelHeight;
            }
            catch (Exception ex)
            {
                failure = ex.ToString();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(30000);

        Assert.Null(failure);
        Assert.True(frozen);
        Assert.Equal(64, width);
        Assert.Equal(48, height);
    }
}
