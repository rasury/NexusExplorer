using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public class PreviewScrollStyleTests
{
    [Fact]
    public async Task DocumentAndPdfViewersUseSharedOverlayBars() => await WpfTestHost.RunAsync(() =>
    {
        var document = new DocumentPreview();
        var editor = (ICSharpCode.AvalonEdit.TextEditor)document.FindName("SourceEditor");
        editor.Text = string.Join("\n", Enumerable.Repeat(new string('宽', 300), 100));
        var markdown = (FlowDocumentScrollViewer)document.FindName("MarkdownViewer");
        markdown.Document = new FlowDocument(new Paragraph(new Run(string.Join("\n", Enumerable.Repeat("正文", 300)))));
        var pdf = new PdfPreview();
        var continuous = new PdfContinuousView();
        var root = new StackPanel(); root.Children.Add(document); root.Children.Add(pdf); root.Children.Add(continuous);
        document.Height = pdf.Height = continuous.Height = 250;
        var window = new Window { Content = root, Width = 700, Height = 850, ShowInTaskbar = false };
        try
        {
            window.Show(); window.UpdateLayout();
            AssertOverlay(Descendants<ScrollViewer>(editor).First());
            AssertOverlay(Descendants<ScrollViewer>(markdown).First());
            AssertOverlay((ScrollViewer)pdf.FindName("PageScroll"));
            var pages = (ListBox)continuous.FindName("Pages");
            AssertOverlay(Descendants<ScrollViewer>(pages).First());
            Assert.True(VirtualizingPanel.GetIsVirtualizing(pages));
            var scroll = Descendants<ScrollViewer>(editor).First();
            scroll.ScrollToVerticalOffset(100); scroll.ScrollToHorizontalOffset(80); window.UpdateLayout();
            Assert.True(scroll.VerticalOffset > 0); Assert.True(scroll.HorizontalOffset > 0);
        }
        finally { window.Close(); }
    });
    private static void AssertOverlay(ScrollViewer scroll)
    {
        scroll.ApplyTemplate();
        var vertical = Assert.IsType<ScrollBar>(scroll.Template.FindName("PART_VerticalScrollBar", scroll));
        var horizontal = Assert.IsType<ScrollBar>(scroll.Template.FindName("PART_HorizontalScrollBar", scroll));
        Assert.Same(scroll.FindResource("PrismScrollBar"), vertical.Style);
        Assert.Same(scroll.FindResource("PrismHorizontalScrollBar"), horizontal.Style);
        Assert.Equal(0, Grid.GetColumn(vertical)); Assert.Equal(0, Grid.GetRow(horizontal));
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
