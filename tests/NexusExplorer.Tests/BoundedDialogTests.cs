using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using NexusExplorer.Services;
using NexusExplorer.Views.Dialogs;

namespace NexusExplorer.Tests;
public class BoundedDialogTests
{
    [Fact]
    public async Task OrganizationSummaryHasNoLeadingSpaceOnStatusLines()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var view = OrganizeResultDialog.Create("test", new List<OrganizeFileResult>());
            var body = (StackPanel)((StackPanel)((ScrollViewer)view.Children[0]).Content).Children[1];
            var lines = ((TextBlock)body.Children[0]).Text.Split('\n');
            Assert.StartsWith("失效", lines[3]); Assert.All(lines, line => Assert.Equal(line.TrimStart(), line));
        });
    }
    [Theory]
    [InlineData("result")][InlineData("conflict")][InlineData("location")][InlineData("message")]
    public async Task RealLongDialogScrollsAndKeepsEveryActionWithinSmallWorkArea(string kind)
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var longText = string.Join('\n', Enumerable.Repeat(new string('长', 120), 100));
            var view = kind switch
            {
                "result" => OrganizeResultDialog.Create("test", Enumerable.Range(0,8).Select(i=>new OrganizeFileResult {FileName=longText,Outcome=OrganizeOutcome.Failed}).ToList()),
                "conflict" => ConflictDialog.Create(longText,longText),
                "location" => LocationPreviewDialog.Create(new[]{longText}),
                _ => MessageDialog.Create(longText,"确认",DialogButtons.YesNoCancel,DialogSeverity.Question)
            };
            var host = new DialogHost { Identifier=Guid.NewGuid().ToString(), DialogContentUniformCornerRadius=28 };
            host.SetResourceReference(FrameworkElement.StyleProperty,"MaterialDesignEmbeddedDialogHost");
            var window = new Window { Content=host,Width=400,Height=480,ShowActivated=false,ShowInTaskbar=false };
            try
            {
                window.Show(); await Dispatcher.Yield(DispatcherPriority.Loaded); MaterialDialogService.Register(host);
                var result = MaterialDialogService.ShowAsync(view);
                await Until(()=>host.IsOpen && view.IsLoaded); window.UpdateLayout();
                Assert.True(view.ActualWidth+48<=host.ActualWidth+.1); Assert.True(view.ActualHeight+48<=host.ActualHeight+.1);
                var scroll=(ScrollViewer)view.Children[0]; Assert.True(scroll.ScrollableHeight>0); Assert.True(scroll.ViewportHeight>0);
                var footer=(FrameworkElement)view.Children[1]; var point=footer.TranslatePoint(new Point(),view);
                Assert.True(point.Y+footer.ActualHeight<=view.ActualHeight+.1);
                var buttons=footer is Panel panel ? panel.Children.OfType<Button>().ToList() : new List<Button>{(Button)footer};
                foreach(var button in buttons)
                {
                    var at=button.TranslatePoint(new Point(),view);
                    Assert.True(at.X+button.ActualWidth<=view.ActualWidth+.1); Assert.True(at.Y+button.ActualHeight<=view.ActualHeight+.1);
                }
                scroll.ScrollToEnd();window.UpdateLayout(); Assert.Equal(point.Y,footer.TranslatePoint(new Point(),view).Y);
                view.Complete(null); await result;
            }
            finally { MaterialDialogService.Unregister(host); window.Close(); }
        });
    }
    [Fact]
    public async Task ConflictDecisionAndConfirmationButtonsKeepTheirBusinessResults()
    {
        await WpfTestHost.RunAsync(()=>
        {
            var conflict=ConflictDialog.Create("file","target");object? result=null;conflict.Complete=value=>result=value;
            var body=(StackPanel)((StackPanel)((ScrollViewer)conflict.Children[0]).Content).Children[1];
            body.Children.OfType<CheckBox>().Single().IsChecked=true;
            ((WrapPanel)conflict.Children[1]).Children.OfType<Button>().Single(b=>(string)b.Content=="跳过").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new ConflictDecision(ConflictResolution.Skip,true),result);
            var confirm=MessageDialog.Create("删除确认","确认",DialogButtons.YesNo,DialogSeverity.Question); confirm.Complete=value=>result=value;
            ((WrapPanel)confirm.Children[1]).Children.OfType<Button>().Single(b=>(string)b.Content=="否").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(DialogAnswer.No,result);
        });
    }
    internal static async Task Until(Func<bool> ready)
    { var end=DateTime.UtcNow.AddSeconds(5);while(!ready()){if(DateTime.UtcNow>end)throw new TimeoutException();await Task.Delay(20);} }
}
