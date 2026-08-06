using System;

namespace Likeon.NativeRelay
{
    /// <summary>
    /// 能力过滤通道（组合包装）：按 command 决定是否真正下发到内层通道；
    /// 未实现的命令立即以<b>项目自定义</b>的 (code, data) 应答。
    /// 用途：业务调用点因此可以全平台统一写法（永远 bridge.Request），
    /// 「哪条命令在哪个平台有原生实现」这张能力矩阵收在项目自己的通道配置里——
    /// 框架不解释 code/data 语义，未实现时的应答码也由项目自定。
    /// </summary>
    /// <remarks>
    /// 典型用法（项目侧）：
    /// <code>
    /// INativeChannel channel = NativeChannelFactory.CreateForCurrentPlatform();
    /// INativeChannel gated = new FilteringChannel(channel, MyCommands.IsImplementedHere, fallbackCode: 0);
    /// var bridge = MainThreadDispatcher.Instance.CreateBridge(gated, timeoutSeconds: 5.0);
    /// </code>
    /// 平台没有任何已实现命令时，内层通道从不被调用（例如 iOS 无原生库时，IosChannel 的
    /// P/Invoke 构造函数不会被执行到 → 无符号引用）。与继承通道的分工：想复用基类行为的选继承，
    /// 想按命令表过滤的选本类；两者可叠加。
    /// </remarks>
    public sealed class FilteringChannel : INativeChannel
    {
        /// <summary>内层通道回来的结果（原样透传）或未实现命令的兜底应答。</summary>
        public event Action<long, int, string> OnResult;

        private readonly INativeChannel _inner;
        private readonly Func<int, bool> _isImplemented;
        private readonly int _fallbackCode;
        private readonly string _fallbackData;
        private volatile bool _disposed;

        /// <param name="inner">实际干活的内层通道（仅已实现命令会送达）。</param>
        /// <param name="isImplemented">能力矩阵：command → 本平台是否已实现。</param>
        /// <param name="fallbackCode">未实现命令的应答码（项目自定义，默认 0）。</param>
        /// <param name="fallbackData">未实现命令的应答数据（项目自定义，默认 null）。</param>
        public FilteringChannel(INativeChannel inner, Func<int, bool> isImplemented,
            int fallbackCode = 0, string fallbackData = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _isImplemented = isImplemented ?? throw new ArgumentNullException(nameof(isImplemented));
            _fallbackCode = fallbackCode;
            _fallbackData = fallbackData;
            _inner.OnResult += HandleInnerResult;
        }

        /// <inheritdoc />
        public void Send(long seed, int command, string payload)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FilteringChannel));
            if (_isImplemented(command))
            {
                _inner.Send(seed, command, payload);
                return;
            }
            OnResult?.Invoke(seed, _fallbackCode, _fallbackData);
        }

        private void HandleInnerResult(long seed, int code, string data)
        {
            OnResult?.Invoke(seed, code, data);
        }

        /// <summary>关闭通道：一并关闭内层。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _inner.OnResult -= HandleInnerResult;
            OnResult = null;
            _inner.Dispose();
        }
    }
}
