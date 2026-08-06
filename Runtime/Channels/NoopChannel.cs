using System;

namespace Likeon.NativeRelay
{
    /// <summary>
    /// 无原生实现平台的通道：任何请求都立即以 <see cref="RelayCode.PlatformUnsupported"/> 应答。
    /// 业务侧无需为平台写 #if——调用是安全的，收到保留码按"平台不支持"忽略即可。
    /// 与 <see cref="MockChannel"/> 的分工：Mock 用于编辑器/单测模拟真实结果；
    /// Noop 用于真实玩家包中该平台本就不支持的场景（不造假数据、不白跑线程）。
    /// </summary>
    public sealed class NoopChannel : INativeChannel
    {
        /// <summary>原生层 → 框架层：结果 (seed, code, data) 回来。</summary>
        public event Action<long, int, string> OnResult;

        private volatile bool _disposed;

        /// <inheritdoc />
        public void Send(long seed, int command, string payload)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NoopChannel));
            OnResult?.Invoke(seed, RelayCode.PlatformUnsupported, null);
        }

        /// <summary>关闭通道。</summary>
        public void Dispose()
        {
            _disposed = true;
            OnResult = null;
        }
    }
}
