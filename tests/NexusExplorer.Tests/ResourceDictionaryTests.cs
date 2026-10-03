using System.Windows;

namespace NexusExplorer.Tests;

public class ResourceDictionaryTests
{
    [Fact]
    public async Task AllResourcesInstantiateWithoutErrors()
    {
        await WpfTestHost.RunAsync(() =>
        {
            foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
                foreach (var key in dictionary.Keys.Cast<object>().ToList())
                    Assert.NotNull(dictionary[key]);
        });
    }
}
