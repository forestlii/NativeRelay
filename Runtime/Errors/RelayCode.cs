namespace Likeon.NativeRelay
{
    /// <summary>
    /// 框架<b>仅有</b>的几个「自产」结果码。NativeRelay 不定义、不解释业务错误——成功/失败/具体错误码
    /// 全由<b>原生层产出、业务层解释</b>，框架只透传。框架唯一会自己产出码的场景，是它无法从原生层拿到结果时
    /// （原生迟迟不回 = <see cref="Timeout"/>；桥被关闭 = <see cref="Disposed"/>）。
    /// </summary>
    /// <remarks>
    /// 取值刻意放在 <see cref="int"/> 极端处（<c>int.MinValue</c> 区），业务码请避开这两个值即可，几乎不会冲突。
    /// 业务侧收到 <c>code</c> 后自行判断：是自己的码（1/0/10086…），还是这两个保留码。
    /// </remarks>
    public static class RelayCode
    {
        /// <summary>原生层超过 timeout 仍未回结果，框架清理该请求并以此码通知业务。</summary>
        public const int Timeout = int.MinValue;

        /// <summary>桥已 Dispose / 关闭，未完成的请求被取消，框架以此码通知业务收尾。</summary>
        public const int Disposed = int.MinValue + 1;
    }
}
