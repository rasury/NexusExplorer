namespace NexusExplorer.Services;
internal static class MutationGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static async Task<IDisposable> AcquireAsync(Microsoft.EntityFrameworkCore.IDbContextFactory<Data.AppDbContext>? factory = null)
    {
        await Gate.WaitAsync();
        try
        {
            if (factory is not null)
            {
                await using var db = await factory.CreateDbContextAsync();
                if (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(db.FileOperations.Where(o => o.State != "Completed" && o.State != "Failed")))
                    throw new OperationException("存在未完成的文件操作，请重新启动并完成恢复后再修改数据。");
            }
            return new Lease();
        }
        catch { Gate.Release(); throw; }
    }
    private sealed class Lease : IDisposable { public void Dispose() => Gate.Release(); }
}
