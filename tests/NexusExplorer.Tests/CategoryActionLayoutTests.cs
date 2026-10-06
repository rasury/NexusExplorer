using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NexusExplorer.Views;
using NexusExplorer.ViewModels;
using NexusExplorer.Views.Dialogs;
using MaterialDesignThemes.Wpf;

namespace NexusExplorer.Tests;

public class CategoryActionLayoutTests
{
    [Fact]
    public async Task ContextImportsUseClickedCategoryAndDoNotChangeBrowserOrPlayer()
    {
        using var host = new TestHost();
        var a=await host.Categories.CreateAsync("A",null); var b=await host.Categories.CreateAsync("B",null);
        var existing=await host.Files.AddAsync(host.CreateTestFile("existing.mp3"),a.Id);
        var added=host.CreateTestFile("new.txt"); var folder=Path.Combine(host.RootDir,"folder"); var nested=Path.Combine(folder,"child"); Directory.CreateDirectory(nested); File.WriteAllText(Path.Combine(nested,"nested.txt"),"test");
        await WpfTestHost.RunAsync(async()=>
        {
            var engine=new FakePlaybackEngine(); var main=new MainViewModel(host.Categories,host.Files,host.Organization,engine);
            var panel=new CategoryFilePanel(); panel.Initialize(main,host.RecycleBin);
            var window=new Window{Content=panel,Width=360,Height=800,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-5000,Top=-5000};
            try
            {
                window.Show(); await main.RefreshTreeAsync(); await main.SelectCategoryAsync(a); await main.SelectFileAsync(existing);
                panel.RecordContextMenuTarget(new TreeViewItem{Header=b});
                main.FileList.PickFiles=()=>Task.FromResult<IReadOnlyList<string>>(new[]{added});
                var imported=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); main.FileList.ShowInfo=_=>imported.TrySetResult();
                var tree=(TreeView)panel.FindName("CategoryTree");
                tree.ContextMenu.Items.OfType<MenuItem>().Single(m=>(string)m.Header=="添加文件").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await imported.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Single(await host.Files.GetByCategoryAsync(b.Id)); Assert.Single(main.CurrentFiles); Assert.Equal(a.Id,main.CurrentCategory!.Id);
                main.FileList.PickDirectory=()=>Task.FromResult<string?>(folder);
                imported=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                tree.ContextMenu.Items.OfType<MenuItem>().Single(m=>(string)m.Header=="添加文件夹").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await imported.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var children=await host.Categories.GetChildrenAsync(b.Id); Assert.Single(children);
                Assert.Single(await host.Files.GetByCategoryAsync(children[0].Id));
                Assert.Equal(a.Id,main.CurrentCategory!.Id); Assert.Same(existing,main.CurrentFile); Assert.Single(engine.Played);
                Assert.True(panel.OrganizeCommand.CanExecute(b)); Assert.False(panel.OrganizeCommand.CanExecute(null));
            }
            finally { window.Close(); }
        });
    }
    [Fact]
    public async Task HeaderPlusCreatesRootAndBlankTreeAcceptsOnlyCategoryRootDrops()
    {
        using var host=new TestHost(); var parent=await host.Categories.CreateAsync("parent",null);
        await WpfTestHost.RunAsync(async()=>
        {
            using var engine=new FakePlaybackEngine(); var main=new MainViewModel(host.Categories,host.Files,host.Organization,engine);
            var window=new MainWindow(main,host.Categories,host.Files,host.Organization,host.RecycleBin,engine)
            {ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-5000,Top=-5000};
            try
            {
                window.Show(); await main.SelectCategoryAsync(parent);
                main.Category.ShowInputDialog=(_,_)=>Task.FromResult<string?>("new-root");
                var button=(Button)window.FindName("CreateRootCategoryButton");
                Assert.Equal(PackIconKind.Plus,Assert.IsType<PackIcon>(button.Content).Kind);
                Assert.Same(main.Category.CreateRootCommand,button.Command);
                await main.Category.CreateRootCommand.ExecuteAsync(null);
                Assert.Contains(await host.Categories.GetChildrenAsync(null),c=>c.Name=="new-root");
                Assert.Empty(await host.Categories.GetChildrenAsync(parent.Id));
                var categoryData=new DataObject("NexusExplorer.CategoryId",parent.Id);
                var filesData=new DataObject(DataFormats.FileDrop,new[]{"file.txt"});
                Assert.True(CategoryFilePanel.CanDropCategoryAtRoot(categoryData,new TreeView()));
                Assert.False(CategoryFilePanel.CanDropCategoryAtRoot(categoryData,new ScrollBar()));
                Assert.False(CategoryFilePanel.CanDropCategoryAtRoot(categoryData,new TreeViewItem{Header=parent}));
                Assert.False(CategoryFilePanel.CanDropCategoryAtRoot(filesData,new TreeView()));
            }
            finally { window.PrepareForVerificationExit(); window.Close(); }
        });
    }
    [Fact]
    public async Task ContextOrganizeUsesExplicitCategoryWhileAnotherCategoryIsBrowsed()
    {
        using var host=new TestHost(); var a=await host.Categories.CreateAsync("A",null); var b=await host.Categories.CreateAsync("B",null);
        var aFile=await host.Files.AddAsync(host.CreateTestFile("a.txt"),a.Id);
        var bFile=await host.Files.AddAsync(host.CreateTestFile("b.txt"),b.Id);
        await WpfTestHost.RunAsync(async()=>
        {
            using var engine=new FakePlaybackEngine(); var main=new MainViewModel(host.Categories,host.Files,host.Organization,engine);
            var window=new MainWindow(main,host.Categories,host.Files,host.Organization,host.RecycleBin,engine)
            {ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-5000,Top=-5000};
            try
            {
                window.Show(); await main.SelectCategoryAsync(a);
                var panel=(CategoryFilePanel)window.FindName("LeftPanel"); panel.RecordContextMenuTarget(new TreeViewItem{Header=b});
                var tree=(TreeView)panel.FindName("CategoryTree");
                tree.ContextMenu.Items.OfType<MenuItem>().Single(m=>(string)m.Header=="整理").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                var execution=((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)panel.OrganizeCommand).ExecutionTask!;
                var dialog=(DialogHost)window.FindName("RootDialog"); await BoundedDialogTests.Until(()=>dialog.IsOpen);
                ((DialogSurface)dialog.DialogContent!).Complete(DialogAnswer.Ok); await execution.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(a.Id,main.CurrentCategory!.Id);
                Assert.Equal(aFile.AbsolutePath,(await host.Files.GetByIdAsync(aFile.Id))!.AbsolutePath);
                Assert.StartsWith(b.PhysicalPath,(await host.Files.GetByIdAsync(bFile.Id))!.AbsolutePath);
            }
            finally { MaterialDialogService.CancelAll(); window.PrepareForVerificationExit(); window.Close(); }
        });
    }
}
