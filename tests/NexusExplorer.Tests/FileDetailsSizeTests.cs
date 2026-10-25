using System.IO;
using System.Windows.Controls;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public class FileDetailsSizeTests
{
    [Fact]
    public async Task SizeHandlesEmptyMissingAndRapidFileChanges()
    {
        using var host = new TestHost();
        var empty = host.CreateTestFile("empty.txt", "");
        var sized = host.CreateTestFile("sized.bin", new string('x', 1536));
        await WpfTestHost.RunAsync(async () =>
        {
            var panel = new PlayerPanel();
            var label = (TextBlock)panel.FindName("DetailSize");
            await panel.UpdateFileSizeAsync(sized);
            Assert.Contains("KB", label.Text); Assert.Contains("1", label.Text);
            Assert.Equal($"{1536:N0} 字节", label.ToolTip);
            await panel.UpdateFileSizeAsync(empty); Assert.Equal("0 B", label.Text);
            await panel.UpdateFileSizeAsync(Path.Combine(host.RootDir, "missing.txt")); Assert.Equal("文件已失效", label.Text);
            var pending = panel.UpdateFileSizeAsync(sized);
            await panel.UpdateFileSizeAsync(null); await pending;
            Assert.Equal("—", label.Text); Assert.Null(label.ToolTip);
        });
    }
}
