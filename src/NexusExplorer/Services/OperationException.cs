namespace NexusExplorer.Services;

/// <summary>用户可见的业务错误(消息直接展示给用户)。</summary>
public class OperationException : Exception
{
    public OperationException(string message) : base(message)
    {
    }
}
