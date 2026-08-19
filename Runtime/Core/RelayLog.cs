using System;

namespace Likeon.NativeRelay
{
    /// <summary>
    /// 框架诊断日志出口（<b>默认关</b>）：真机排查「请求卡在哪一跳」用——
    /// 沿 C# Request → 通道 Send → 原生回调 → Pump 派发 的链路逐跳打点，断在哪跳一目了然。
    /// 核心层不依赖 UnityEngine，日志经 <see cref="Sink"/> 外送：Unity 下由
    /// <see cref="MainThreadDispatcher"/> 建桥时自动接 <c>Debug.Log</c>（真机 logcat 的 Unity tag 可见）；
    /// 纯 C# 环境可接 <c>Console.WriteLine</c> 或测试收集器。
    /// </summary>
    /// <remarks>
    /// 零 GC 承诺不破：所有打点以 <c>if (RelayLog.Enabled)</c> 包裹，字符串拼插只在开启时发生，
    /// 关闭时稳态路径零分配、零开销。打点只记 seed / command / 耗时等路由信息，
    /// 业务 payload 内容不入日志（可能含敏感参数）。
    /// </remarks>
    public static class RelayLog
    {
        /// <summary>总开关（默认 false）。开启后各链路 hop 打点到 <see cref="Sink"/>。</summary>
        public static bool Enabled;

        /// <summary>日志去向；为 null 时即便 <see cref="Enabled"/> 开启也安全静默（无处可写）。</summary>
        public static Action<string> Sink;

        /// <summary>
        /// 写一条诊断（自动加 <c>[NativeRelay]</c> 前缀）。调用方应先判 <see cref="Enabled"/> 再拼字符串；
        /// 本方法内部仍复查开关与 Sink，漏判直接调用也安全。
        /// </summary>
        public static void Info(string message)
        {
            if (!Enabled) return;
            Action<string> sink = Sink;
            if (sink != null)
            {
                sink("[NativeRelay] " + message);
            }
        }
    }
}
