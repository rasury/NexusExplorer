using System.Windows;
using System.Windows.Controls;
using NexusExplorer.Services;

namespace NexusExplorer.Views.Dialogs;

/// <summary>整理结果汇总对话框。</summary>
public static class OrganizeResultDialog
{
    public static Task ShowAsync(string categoryName, List<OrganizeFileResult> results)
        => MaterialDialogService.ShowAsync(Create(categoryName, results));

    internal static DialogSurface Create(string categoryName, List<OrganizeFileResult> results)
    {
        var moved = results.Count(r => r.Outcome is OrganizeOutcome.Moved);
        var renamed = results.Count(r => r.Outcome is OrganizeOutcome.Renamed);
        var skipped = results.Count(r => r.Outcome is OrganizeOutcome.Skipped);
        var already = results.Count(r => r.Outcome is OrganizeOutcome.AlreadyOrganized);
        var missing = results.Count(r => r.Outcome is OrganizeOutcome.Missing);
        var failed = results.Count(r => r.Outcome is OrganizeOutcome.Failed);

        var summary = $"分类「{categoryName}」整理完成:\n" +
                      $"移动 {moved + renamed} 个(重命名保留{renamed} 个)\n" +
                      $"跳过 {skipped} 个,已有 {already} 个\n" +
                      $"失效 {missing} 个,失败 {failed} 个";

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = summary,
            Style = (Style)System.Windows.Application.Current.Resources["TextPrimary"],
            TextWrapping = TextWrapping.Wrap
        });

        // 失败/失效明细
        var problems = results
            .Where(r => r.Outcome is OrganizeOutcome.Failed or OrganizeOutcome.Missing)
            .Take(8)
            .ToList();
        if (problems.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "\n未处理的文件:",
                Style = (Style)System.Windows.Application.Current.Resources["TextSecondary"],
                Margin = new Thickness(0, 10, 0, 4)
            });
            foreach (var problem in problems)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"• {problem.FileName} — {problem.Error ?? "源文件不存在"}",
                    Style = (Style)System.Windows.Application.Current.Resources["TextCaption"],
                    TextWrapping = TextWrapping.Wrap
                });
            }
            if (problems.Count < results.Count(r => r.Outcome is OrganizeOutcome.Failed or OrganizeOutcome.Missing))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "…",
                    Style = (Style)System.Windows.Application.Current.Resources["TextCaption"]
                });
            }
        }

        DialogSurface dialog = null!;
        var ok = new Button { Content = "确定", MinWidth = 80, IsDefault = true };
        ok.SetResourceReference(FrameworkElement.StyleProperty, "ButtonPrimary");
        ok.Click += (_, _) => dialog.Complete(null);
        dialog = new DialogSurface("整理结果", panel, ok);
        return dialog;
    }
}
