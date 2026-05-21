using System;

namespace PureRpc
{
    /// <summary>
    /// 表示 PureRpc 框架在请求处理、传输或协议解析过程中发生的异常 / 
    /// Represents an exception that occurs during PureRpc request processing, transport, or protocol parsing.
    /// </summary>
    public class RpcException : Exception
    {
        /// <summary>
        /// 获取与此异常关联的请求唯一标识符（如果有） / 
        /// Gets the unique request identifier associated with this exception (if any).
        /// </summary>
        public ulong? RequestId { get; }

        /// <summary>
        /// 获取服务端的原始错误载荷（可选） / 
        /// Gets the raw error payload from the server (optional).
        /// </summary>
        public byte[]? ErrorData { get; }

        /// <summary>
        /// 使用错误消息初始化 / Initializes with an error message.
        /// </summary>
        /// <param name="message">错误消息 / The error message.</param>
        public RpcException(string message) : base(message) { }

        /// <summary>
        /// 使用错误消息和内部异常初始化 / Initializes with an error message and inner exception.
        /// </summary>
        /// <param name="message">错误消息 / The error message.</param>
        /// <param name="innerException">内部异常 / The inner exception.</param>
        public RpcException(string message, Exception innerException)
            : base(message, innerException) { }

        /// <summary>
        /// 使用错误消息和请求 ID 初始化 / Initializes with an error message and request ID.
        /// </summary>
        /// <param name="message">错误消息 / The error message.</param>
        /// <param name="requestId">请求唯一标识符 / The unique request identifier.</param>
        public RpcException(string message, ulong requestId) : base(message)
        {
            RequestId = requestId;
        }

        /// <summary>
        /// 使用错误消息、请求 ID 和错误数据初始化 / Initializes with an error message, request ID, and error data.
        /// </summary>
        /// <param name="message">错误消息 / The error message.</param>
        /// <param name="requestId">请求唯一标识符 / The unique request identifier.</param>
        /// <param name="errorData">服务端原始错误载荷 / The raw error payload from the server.</param>
        public RpcException(string message, ulong requestId, byte[] errorData) : base(message)
        {
            RequestId = requestId;
            ErrorData = errorData;
        }
    }
}
