using System.Buffers;
using System.Threading.Tasks.Sources;

namespace PureRpc;

/// <summary>
/// 待处理请求状态管理器 / Pending request state manager.
/// 基于 <see cref="ManualResetValueTaskSourceCore{T}"/> 实现的高性能 ValueTask 来源 / 
/// High-performance ValueTask source based on <see cref="ManualResetValueTaskSourceCore{T}"/>.
/// 支持对象池化复用 / Supports pooled reuse.
/// </summary>
internal sealed class PendingRequest : IValueTaskSource<ReadOnlySequence<byte>>
{
    /// <summary>
    /// 底层 ValueTask 源核心 / The underlying ValueTask source core.
    /// </summary>
    private ManualResetValueTaskSourceCore<ReadOnlySequence<byte>> _source;

    /// <summary>
    /// 获取当前版本号，用于防止过期的 ValueTask 被重复消费 / 
    /// Gets the current version, used to prevent stale ValueTask from being consumed twice.
    /// 每次 Reset 后版本号递增 / Version increments after each Reset.
    /// </summary>
    public short Version => _source.Version;

    /// <summary>
    /// 创建当前请求的 ValueTask，供调用方 await / 
    /// Creates a ValueTask for this request, for the caller to await.
    /// </summary>
    /// <returns>绑定当前版本号的 ValueTask / A ValueTask bound to the current version.</returns>
    public ValueTask<ReadOnlySequence<byte>> AsValueTask() => new(this, _source.Version);

    /// <summary>
    /// 设置成功结果，完成 ValueTask / Sets the successful result, completing the ValueTask.
    /// </summary>
    /// <param name="result">响应数据 / The response data.</param>
    public void SetResult(ReadOnlySequence<byte> result)
    {
        _source.SetResult(result);
    }

    /// <summary>
    /// 设置异常结果，使 ValueTask 抛出异常 / Sets the exception result, causing the ValueTask to throw.
    /// </summary>
    /// <param name="ex">要抛出的异常 / The exception to throw.</param>
    public void SetException(Exception ex)
    {
        _source.SetException(ex);
    }

    /// <summary>
    /// 设置取消结果，使 ValueTask 抛出 <see cref="OperationCanceledException"/> / 
    /// Sets the canceled result, causing the ValueTask to throw <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <param name="token">触发取消的令牌 / The token that triggered cancellation.</param>
    public void SetCanceled(CancellationToken token)
    {
        _source.SetException(new OperationCanceledException(token));
    }

    /// <summary>
    /// 重置内部状态以便复用（版本号递增） / 
    /// Resets internal state for reuse (version increments).
    /// </summary>
    public void Reset()
    {
        _source.Reset();
    }

    /// <summary>
    /// 获取操作结果 / Gets the result of the operation.
    /// </summary>
    /// <param name="token">版本令牌 / Version token.</param>
    /// <returns>响应数据 / The response data.</returns>
    public ReadOnlySequence<byte> GetResult(short token) => _source.GetResult(token);

    /// <summary>
    /// 获取当前操作状态 / Gets the current operation status.
    /// </summary>
    /// <param name="token">版本令牌 / Version token.</param>
    /// <returns>ValueTask 的当前状态 / The current status of the ValueTask.</returns>
    public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);

    /// <summary>
    /// 注册完成回调 / Registers a completion callback.
    /// 如果底层 OnCompleted 抛出 InvalidOperationException（已完成），回退到线程池调度 / 
    /// Falls back to thread pool scheduling if underlying OnCompleted throws InvalidOperationException (already completed).
    /// </summary>
    /// <param name="continuation">完成后的延续回调 / The continuation callback after completion.</param>
    /// <param name="state">传递给回调的状态对象 / State object passed to the callback.</param>
    /// <param name="token">版本令牌 / Version token.</param>
    /// <param name="flags">完成标志 / Completion flags.</param>
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        try
        {
            _source.OnCompleted(continuation, state, token, flags);
        }
        catch (InvalidOperationException)
        {
            ThreadPool.UnsafeQueueUserWorkItem(continuation, state, preferLocal: false);
        }
    }
}
