using System.Buffers;
using System.Threading.Tasks.Sources;

namespace PureRpc;

/// <summary>
/// 可复用的请求完成源，替代 TaskCompletionSource 减少每调用分配。
/// 实现 IValueTaskSource 使 ValueTask 可直接绑定到此对象，消除 Task 对象分配。
/// </summary>
internal sealed class PendingRequest : IValueTaskSource<ReadOnlySequence<byte>>
{
    private ManualResetValueTaskSourceCore<ReadOnlySequence<byte>> _source;

    /// <summary>当前版本号，由 ManualResetValueTaskSourceCore 管理</summary>
    public short Version => _source.Version;

    /// <summary>获取当前 ValueTask，必须在 Reset() 之后调用</summary>
    public ValueTask<ReadOnlySequence<byte>> AsValueTask() => new(this, _source.Version);

    /// <summary>重置状态以便复用，版本号自动递增</summary>
    public void Reset()
    {
        _source.Reset();
    }

    public void SetResult(ReadOnlySequence<byte> result)
    {
        _source.SetResult(result);
    }

    public void SetException(Exception ex)
    {
        _source.SetException(ex);
    }

    public void SetCanceled(CancellationToken token)
    {
        _source.SetException(new OperationCanceledException(token));
    }

    // IValueTaskSource<ReadOnlySequence<byte>>
    public ReadOnlySequence<byte> GetResult(short token) => _source.GetResult(token);
    public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _source.OnCompleted(continuation, state, token, flags);
}
