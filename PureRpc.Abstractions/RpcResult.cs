using System;

namespace PureRpc.Abstractions;

/// <summary>
/// 非泛型 RpcResult，用于 void 方法返回成功/失败信息。
/// </summary>
public class RpcResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }

    public static RpcResult Success() => new() { IsSuccess = true };
    public static RpcResult Failure(string error) => new() { ErrorMessage = error ?? "Unknown error" };

    public void ThrowIfFailed()
    {
        if (!IsSuccess) throw new InvalidOperationException(ErrorMessage ?? "Request failed");
    }
}

/// <summary>
/// 泛型 RpcResult，用于带返回值的方法返回成功/失败信息。
/// 服务端返回此类型时，源生成器自动处理 IsSuccess 分支：成功时序列化 Value，
/// 失败时写入错误消息并 abort。客户端代理自动将错误响应转为 RpcResult.Failure。
/// </summary>
public class RpcResult<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public string? ErrorMessage { get; init; }

    public static RpcResult<T> Success(T value) => new() { IsSuccess = true, Value = value };
    public static RpcResult<T> Failure(string error) => new() { ErrorMessage = error ?? "Unknown error" };

    public void ThrowIfFailed()
    {
        if (!IsSuccess) throw new InvalidOperationException(ErrorMessage ?? "Request failed");
    }
}
