namespace PureRpc.Abstractions;

internal static class RpcProtocolConstants
{
    public const int MaxServiceNameLength = 256;
    public const int MaxMethodNameLength = 256;
    public const int MaxHeaderCount = 64;
    public const int MaxHeaderKeyLength = 256;
    public const int MaxHeaderValueLength = 4096;
    public const int MaxFrameSize = 64 * 1024 * 1024;
    public const int DefaultObjectPoolMaxRetained = 1024;
    public const int DefaultRequestThrottleLimit = 512;
}

public enum RpcMessageType : byte
{
    Request = 1,
    Response = 2,
    Error = 3,
    Cancel = 8
}
