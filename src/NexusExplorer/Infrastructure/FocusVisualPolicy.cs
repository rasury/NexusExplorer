using System.Windows;
using System.Windows.Controls;

namespace NexusExplorer.Infrastructure;

internal static class FocusVisualPolicy
{
    private static bool _initialized;
    internal static void Initialize()
    {
        if (_initialized) return;
        // Run before creating controls. Coercion also covers explicit focus styles
        // from third-party templates and later light/dark theme replacements.
        FrameworkElement.FocusVisualStyleProperty.OverrideMetadata(typeof(Control),
            new FrameworkPropertyMetadata(null, null, (_, _) => null));
        _initialized = true;
    }
}
