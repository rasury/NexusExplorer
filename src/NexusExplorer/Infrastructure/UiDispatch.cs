namespace NexusExplorer.Infrastructure;

internal static class UiDispatch
{
    public static async Task<T> RunAsync<T>(SynchronizationContext? context, Func<Task<T>> action)
    {
        T result = default!;
        await RunAsync(context, async () => { result = await action(); });
        return result;
    }
    public static Task RunAsync(SynchronizationContext? context, Func<Task> action)
    {
        if (context is null || ReferenceEquals(context, SynchronizationContext.Current)) return action();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(async _ =>
        {
            try { await action(); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
        }, null);
        return completion.Task;
    }
}
