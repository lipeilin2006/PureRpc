namespace PureRpc.Abstractions;

/// <summary>
/// RPC 协议常量定义 / RPC protocol constants.
/// 定义帧格式中各字段的最大长度和默认配置 / 
/// Defines maximum field lengths and default configurations for the frame format.
/// </summary>
internal static class RpcProtocolConstants
{
    /// <summary>
    /// 服务名最大字节长度 / Maximum byte length of service names.
    /// </summary>
    public const int MaxServiceNameLength = 256;

    /// <summary>
    /// 方法名最大字节长度 / Maximum byte length of method names.
    /// </summary>
    public const int MaxMethodNameLength = 256;

    /// <summary>
    /// 单个帧中最大头部数量 / Maximum number of headers per frame.
    /// </summary>
    public const int MaxHeaderCount = 64;

    /// <summary>
    /// 头部键的最大字节长度 / Maximum byte length of header keys.
    /// </summary>
    public const int MaxHeaderKeyLength = 256;

    /// <summary>
    /// 头部值的最大字节长度 / Maximum byte length of header values.
    /// </summary>
    public const int MaxHeaderValueLength = 4096;

    /// <summary>
    /// 单个帧的最大字节数（64MB） / Maximum byte size of a single frame (64MB).
    /// </summary>
    public const int MaxFrameSize = 64 * 1024 * 1024;

    /// <summary>
    /// 对象池默认最大保留数量 / Default maximum number of objects retained in the pool.
    /// </summary>
    public const int DefaultObjectPoolMaxRetained = 1024;

    /// <summary>
    /// 默认请求并发限制 / Default request throttle limit.
    /// </summary>
    public const int DefaultRequestThrottleLimit = 512;
}

/// <summary>
/// RPC 消息类型枚举 / RPC message type enum.
/// 定义协议帧中消息类型的字节值 / 
/// Defines the byte values for message types in protocol frames.
/// </summary>
public enum RpcMessageType : byte
{
    /// <summary>
    /// 请求消息 / Request message.
    /// </summary>
    Request = 1,

    /// <summary>
    /// 响应消息 / Response message (success).
    /// </summary>
    Response = 2,

    /// <summary>
    /// 错误响应消息 / Error response message.
    /// </summary>
    Error = 3,

    /// <summary>
    /// 取消请求消息 / Cancel request message.
    /// </summary>
    Cancel = 8
}
