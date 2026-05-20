namespace PureRpc.Transport.Tcp;

/// <summary>
/// 统一的 RPC 消息类型定义。
/// 涵盖了请求、响应及控制信令，使 Transport 层能以统一的逻辑处理所有帧。
/// </summary>
internal enum RpcMessageType : byte
{
    Request = 1,
    Response = 2,
    Error = 3,
    Cancel = 8
}