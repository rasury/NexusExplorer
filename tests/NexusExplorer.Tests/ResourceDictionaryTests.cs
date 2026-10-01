using System.Collections;
using System.Windows;

namespace NexusExplorer.Tests;

/// <summary>
/// 强制实例化应用资源字典中的全部条目。
/// ResourceDictionary 的值是延迟创建的:从未被 XAML StaticResource 引用过的样式
/// 只在运行时第一次访问时才实例化——若某个 Setter 无法解析会在那一刻抛
/// XamlParseException(设置属性 System.Windows.Setter.Property 时引发了异常)。
/// </summary>
public class ResourceDictionaryTests
{
    [Fact]
    public void AllResources_Instantiate_WithoutErrors()
    {
        var failures = new List<string>();

        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application();
                foreach (var source in new[]
                {
                    "pack://application:,,,/NexusExplorer;component/Resources/Theme.xaml",
                    "pack://application:,,,/NexusExplorer;component/Resources/Controls.xaml",
                    "pack://application:,,,/NexusExplorer;component/Resources/Converters.xaml"
                })
                {
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(source) });
                }

                foreach (var dictionary in app.Resources.MergedDictionaries.Cast<ResourceDictionary>().ToList())
                {
                    foreach (var key in dictionary.Keys.Cast<object>().ToList())
                    {
                        try
                        {
                            _ = dictionary[key];
                        }
                        catch (Exception ex)
                        {
                            failures.Add($"[{key}] {ex}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failures.Add($"(setup) {ex}");
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
    }
}
