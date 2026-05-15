using System;

namespace PureRpc
{
    /// <summary>
    /// 表示 PureRpc 框架在请求处理、传输或协议解析过程中发生的异常。
    /// </summary>
    public class RpcException : Exception
    {
        /// <summary>
        /// 获取与此异常关联的请求唯一标识符（如果有）。
        /// </summary>
        public ulong? RequestId { get; }

        /// <summary>
        /// 获取服务端的原始错误载荷（可选）。
        /// </summary>
        public byte[]? ErrorData { get; }

        public RpcException(string message) : base(message) { }

        public RpcException(string message, Exception innerException)
            : base(message, innerException) { }

        public RpcException(string message, ulong requestId) : base(message)
        {
            RequestId = requestId;
        }

        public RpcException(string message, ulong requestId, byte[] errorData) : base(message)
        {
            RequestId = requestId;
            ErrorData = errorData;
        }
    }
}