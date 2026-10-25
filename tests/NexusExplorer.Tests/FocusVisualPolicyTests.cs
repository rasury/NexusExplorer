using System.Windows;
using System.Windows.Controls;
using NexusExplorer.Infrastructure;

namespace NexusExplorer.Tests;
[Collection("NativePlaybackLogging")]
public class FocusVisualPolicyTests
{
    [Fact]
    public async Task ExplicitAndThemedFocusStylesStayHiddenWithoutDisablingControls()
    {
        await WpfTestHost.RunAsync(() =>
        {
            foreach (var mode in new[] { UiThemeMode.Light, UiThemeMode.Dark })
            {
                UiThemeService.Apply(mode);
                foreach (var control in new Control[] { new Button(), new ListBoxItem(), new TreeViewItem(), new TextBox(), new ComboBox(), new CheckBox() })
                {
                    control.FocusVisualStyle = new Style(typeof(Control));
                    Assert.Null(control.FocusVisualStyle);
                    Assert.True(control.Focusable);
                    Assert.True(control.IsEnabled);
                }
                var button = new Button { Style = (Style)Application.Current.FindResource("ClassicIconButton") };
                Assert.Same(Application.Current.FindResource("MaterialDesignIconButton"), button.Style.BasedOn);
                Assert.Null(button.FocusVisualStyle);
            }
        });
    }
}
