using System.Threading;

namespace Likeon.NativeRelay
{
    /// <summary>
    /// 请求序号（seed）生成器：每次请求产出一个唯一、单调递增的 <see cref="long"/>。
    /// seed 是「请求 ↔ 结果」一一对应的关键：它跟随请求透传到原生层，原生层把结果带着同一个 seed 回来，
    /// 框架层据此在 pending 表里查到对应回调。
    /// </summary>
    /// <remarks>
    /// 为什么是 long + Interlocked：
    /// - <see cref="Interlocked.Increment(ref long)"/> 是无锁、线程安全的原子自增，零分配（零 GC），
    ///   多个业务线程并发取号也不会撞号——这是「一一对应」的底座。
    /// - long 而非 int：即使每秒上万请求也几十年不回绕，避免 seed 复用导致的错配。
    /// - 首个 seed 从 1 开始（字段初值 0，自增后为 1），<b>0 保留为「无效 / 无 seed」哨兵</b>，
    ///   便于上层用 0 表示「尚未分配」而不与真实 seed 冲突。
    /// </remarks>
    public sealed class SeedGenerator
    {
        /// <summary>0 表示「无效 / 无 seed」，可供上层做哨兵判断。</summary>
        public const long InvalidSeed = 0L;

        private long _last; // 初值 0；首次 Increment 后 = 1

        /// <summary>原子产出下一个唯一 seed（线程安全、零分配）。</summary>
        public long Next()
        {
            return Interlocked.Increment(ref _last);
        }
    }
}
