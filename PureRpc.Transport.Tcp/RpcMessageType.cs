namespace PureRpc.Transport.Tcp;

/// <summary>
/// 统一的 RPC 消息类型定义。
/// 涵盖了请求、响应及控制信令，使 Transport 层能以统一的逻辑处理所有帧。
/// </summary>
internal enum RpcMessageType : byte
{
    // --- 业务数据帧 ---

    /// <summary>
    /// 调用请求 (Invoke)。
    /// </summary>
    Request = 1,

    /// <summary>
    /// 调用响应 (Result)。
    /// </summary>
    Response = 2,

    /// <summary>
    /// 异常响应 (Exception/Error)。
    /// </summary>
    Error = 3,

    // --- 流式传输帧 (Streaming) ---

    /// <summary>
    /// 流数据片段。
    /// </summary>
    StreamItem = 4,

    /// <summary>
    /// 流结束标志。
    /// </summary>
    StreamEnd = 5,

    // --- 控制与心跳帧 ---

    /// <summary>
    /// 存活探测请求。
    /// </summary>
    Ping = 6,

    /// <summary>
    /// 存活探测响应。
    /// </summary>
    Pong = 7,

    /// <summary>
    /// 取消正在执行的任务。
    /// </summary>
    Cancel = 8
}