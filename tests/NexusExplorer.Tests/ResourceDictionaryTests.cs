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
            var icon = Application.GetResourceStream(new Uri("pack://application:,,,/NexusExplorer;component/Assets/AppIcon.ico"));
            Assert.NotNull(icon);
            using var stream = icon.Stream;
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream,
                System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            Assert.Contains(decoder.Frames, f => f.PixelWidth == 16 && f.PixelHeight == 16);
            Assert.Contains(decoder.Frames, f => f.PixelWidth == 256 && f.PixelHeight == 256);
        });
    }
}
