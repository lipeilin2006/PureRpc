using System;

namespace PureRpc.Transport.Kcp
{
    /// <summary>
    /// KCP 简单日志记录器 / KCP simple logger.
    /// 默认使用 Console.Error.WriteLine，可替换为 Unity Debug.Log 等 / 
    /// Uses Console.Error.WriteLine by default; can be replaced with Unity Debug.Log, etc.
    /// </summary>
    public static class Log
    {
        /// <summary>
        /// 信息级别日志委托 / Info level log delegate.
        /// </summary>
        public static Action<string> Info    = Console.Error.WriteLine;

        /// <summary>
        /// 警告级别日志委托 / Warning level log delegate.
        /// </summary>
        public static Action<string> Warning = Console.Error.WriteLine;

        /// <summary>
        /// 错误级别日志委托 / Error level log delegate.
        /// </summary>
        public static Action<string> Error   = Console.Error.WriteLine;
    }
}
