using System.IO;
using System.Xml.Linq;

namespace NexusExplorer.Tests;

/// <summary>
/// 进度条 Slider 模板验证:轨道两侧 RepeatButton 不得携带
/// Slider.DecreaseLarge/IncreaseLarge 命令——否则拦截
/// IsMoveToPointEnabled 的点击定位(进度条点击跳转失效的根因)。
/// 直接解析模板 XAML,不依赖 WPF 运行时样式应用。
/// </summary>
public class SliderBehaviorTests
{
    [Fact]
    public void SliderTemplate_TrackRepeatButtonsHaveNoCommands()
    {
        var xamlPath = Path.Combine(
            FindRepoRoot(), "src", "NexusExplorer", "Resources", "Controls.xaml");
        Assert.True(File.Exists(xamlPath), $"找不到 {xamlPath}");

        var doc = XDocument.Load(xamlPath);
        XNamespace xn = "http://schemas.microsoft.com/winfx/2006/xaml";

        var sliderStyles = doc.Descendants()
            .Where(e => e.Name.LocalName == "Style"
                && (string?)e.Attribute("TargetType") == "Slider")
            .ToList();
        Assert.NotEmpty(sliderStyles);

        foreach (var style in sliderStyles)
        {
            var repeatButtons = style.Descendants()
                .Where(e => e.Name.LocalName == "RepeatButton")
                .ToList();

            foreach (var button in repeatButtons)
            {
                var command = button.Attribute("Command")?.Value;
                Assert.True(string.IsNullOrEmpty(command),
                    $"Slider 模板的 RepeatButton 不应绑定命令(会拦截点击定位): {command}");
            }
        }
    }

    [Fact]
    public void ProgressSlider_InPlayerPanel_UsesMoveToPoint()
    {
        var xamlPath = Path.Combine(
            FindRepoRoot(), "src", "NexusExplorer", "Views", "PlayerPanel.xaml");
        Assert.True(File.Exists(xamlPath));

        var doc = XDocument.Load(xamlPath);
        XNamespace xn = "http://schemas.microsoft.com/winfx/2006/xaml";
        var progressSlider = doc.Descendants()
            .FirstOrDefault(e => (string?)e.Attribute(xn + "Name") == "ProgressSlider"
                || (string?)e.Attribute("Name") == "ProgressSlider");

        Assert.NotNull(progressSlider);
        Assert.Equal("True", (string?)progressSlider!.Attribute("IsMoveToPointEnabled"));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "NexusExplorer")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("找不到仓库根目录");
    }
}
