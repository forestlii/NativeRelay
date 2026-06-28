using System;

namespace Likeon.NativeRelay
{
    /// <summary>
    /// <b>原生层抽象</b>：插件通用性的核心——原生层只是一个可替换实现。
    /// 框架层（<see cref="Bridge"/>）只通过本接口「把请求送出、收到结果」，完全不关心原生侧具体在干什么
    /// （录音 / 定位 / 蓝牙 / 第三方 SDK……都只是本接口的不同实现）。
    /// </summary>
    /// <remarks>
    /// 🔴 这是<b>对外公共契约（M1 起冻结，不轻易破坏）</b>。要点：
    /// <list type="bullet">
    /// <item><b>command 用 <see cref="int"/></b>，不用 string、不用泛型：跨线程传递 / 做 key / 跨原生边界（JNI/P-Invoke）
    /// 都零分配、干净（原生侧直接 <c>switch(int)</c> 分发）。行为枚举的具体取值由<b>使用者</b>在自己工程里定义后强转 int，
    /// 插件核心全程只认 int、不假设业务含义。</item>
    /// <item><b><see cref="OnResult"/> 可能在子线程触发</b>：实现方绝不能在该事件里碰 Unity API；
    /// 框架层会负责把它安全切回主线程。</item>
    /// </list>
    /// </remarks>
    public interface INativeChannel : IDisposable
    {
        /// <summary>
        /// 框架层 → 原生层：发起一次异步请求。
        /// </summary>
        /// <param name="seed">本次请求的唯一序号；原生层处理完必须带着<b>同一个 seed</b> 回调结果，用于一一对应。</param>
        /// <param name="command">行为标识（int）。具体取值由使用者定义，原生侧据此分发。</param>
        /// <param name="payload">请求参数字节，可为 null。</param>
        void Send(long seed, int command, byte[] payload);

        /// <summary>
        /// 原生层 → 框架层：一次请求的结果回来了。<b>注意：此事件可能在子线程触发。</b>
        /// 参数为 (seed, resultBytes)；seed 必须与对应 <see cref="Send"/> 的 seed 相同。
        /// </summary>
        event Action<long, byte[]> OnResult;
    }
}
