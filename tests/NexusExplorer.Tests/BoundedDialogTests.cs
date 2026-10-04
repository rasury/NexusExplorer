using System.Windows;
using System.Windows.Controls;
using NexusExplorer.Services;
using NexusExplorer.Views.Dialogs;

namespace NexusExplorer.Tests;

public class BoundedDialogTests
{
    [Fact]
    public async Task StartupMessagePreservesMainWindowAndShutdownMode()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var app = System.Windows.Application.Current;
            var previousMain = app.MainWindow; var previousMode = app.ShutdownMode;
            app.MainWindow = null!; app.ShutdownMode = ShutdownMode.OnLastWindowClose;
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = app.Windows.OfType<Window>().Single(w => w.Title == "测试启动消息");
                var footer = (WrapPanel)((Grid)window.Content).Children[1];
                footer.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            try
            {
                Assert.Equal(MessageBoxResult.OK, MessageDialog.Show("恢复报告", "测试启动消息"));
                Assert.Null(app.MainWindow); Assert.Equal(ShutdownMode.OnLastWindowClose, app.ShutdownMode);
                Assert.False(app.Dispatcher.HasShutdownStarted);
            }
            finally { app.MainWindow = previousMain; app.ShutdownMode = previousMode; }
        });
    }

    [Fact]
    public async Task OrganizationSummaryHasNoLeadingSpaceOnStatusLines()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var window = OrganizeResultDialog.Create("test", new List<OrganizeFileResult>());
            var layout = (Grid)window.Content;
            var body = (StackPanel)((ScrollViewer)layout.Children[0]).Content;
            var lines = ((TextBlock)body.Children[0]).Text.Split('\n');
            Assert.StartsWith("失效", lines[3]);
            Assert.All(lines, line => Assert.Equal(line.TrimStart(), line));
        });
    }

    [Theory]
    [InlineData("result")]
    [InlineData("conflict")]
    [InlineData("location")]
    [InlineData("message")]
    public async Task RealLongDialogScrollsAndKeepsEveryActionWithinSmallWorkArea(string kind)
    {
        await WpfTestHost.RunAsync(() =>
        {
            var longText = string.Join('\n', Enumerable.Repeat(new string('长', 120), 100));
            var window = kind switch
            {
                "result" => OrganizeResultDialog.Create("test", Enumerable.Range(0, 8).Select(i => new OrganizeFileResult { FileName = longText, Outcome = OrganizeOutcome.Failed, Error = "失败" }).ToList()),
                "conflict" => ConflictDialog.Create(longText, longText, _ => { }),
                "location" => LocationPreviewDialog.Create(new[] { longText }),
                _ => MessageDialog.Create(longText, "确认", MessageBoxButton.YesNoCancel, MessageBoxImage.Question)
            };
            window.ShowActivated = false;
            window.SourceInitialized += (_, _) => DialogChrome.LimitToWorkArea(window, new Rect(0, 0, 392, 362));
            try
            {
                window.Show(); window.UpdateLayout();
                Assert.InRange(window.ActualWidth, 1, 360.1); Assert.InRange(window.ActualHeight, 1, 330.1);
                var layout = (Grid)window.Content;
                var scroll = (ScrollViewer)layout.Children[0];
                Assert.True(scroll.ScrollableHeight > 0); Assert.True(scroll.ViewportHeight > 0);
                var footer = (FrameworkElement)layout.Children[1];
                var point = footer.TranslatePoint(new Point(), layout);
                Assert.True(point.Y + footer.ActualHeight <= layout.ActualHeight + .1);
                var buttons = footer is Panel panel ? panel.Children.OfType<Button>().ToList() : new List<Button> { (Button)footer };
                Assert.NotEmpty(buttons);
                foreach (var button in buttons)
                {
                    var topLeft = button.TranslatePoint(new Point(), layout);
                    Assert.InRange(topLeft.X, 0, layout.ActualWidth);
                    Assert.True(topLeft.X + button.ActualWidth <= layout.ActualWidth + .1);
                    Assert.True(topLeft.Y + button.ActualHeight <= layout.ActualHeight + .1);
                }
                var before = point.Y; scroll.ScrollToEnd(); window.UpdateLayout();
                Assert.Equal(before, footer.TranslatePoint(new Point(), layout).Y);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task ConflictDecisionAndConfirmationButtonsKeepTheirBusinessResults()
    {
        await WpfTestHost.RunAsync(() =>
        {
            ConflictDecision? selected = null;
            var conflict = ConflictDialog.Create("file", "target", value => selected = value);
            var layout = (Grid)conflict.Content;
            var body = (StackPanel)((ScrollViewer)layout.Children[0]).Content;
            body.Children.OfType<CheckBox>().Single().IsChecked = true;
            var buttons = (WrapPanel)layout.Children[1];
            buttons.Children.OfType<Button>().Single(b => (string)b.Content == "跳过").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new ConflictDecision(ConflictResolution.Skip, true), selected);

            var confirm = MessageDialog.Create("删除确认", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            var footer = (WrapPanel)((Grid)confirm.Content).Children[1];
            footer.Children.OfType<Button>().Single(b => (string)b.Content == "否").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(MessageBoxResult.No, confirm.Tag);
        });
    }
}
