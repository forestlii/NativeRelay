namespace Likeon.NativeRelay
{
    /// <summary>桥的错误类别。请求未能以正常结果收尾时，业务通过 onError 回调收到。</summary>
    public enum BridgeErrorKind
    {
        /// <summary>原生层迟迟不回，pending 超过 timeout 被清理。</summary>
        Timeout = 1,

        /// <summary>通道发送/底层失败（如 Send 抛异常、原生侧报错）。</summary>
        ChannelFailure = 2,

        /// <summary>桥已 Dispose / 关闭，未完成的请求被取消。</summary>
        Disposed = 3,
    }

    /// <summary>
    /// 一次失败请求的错误信息。<b>readonly struct（值类型）</b>：作为参数传递与经 <c>Action&lt;BridgeError&gt;</c>
    /// 派发都不装箱、不产生堆分配——错误路径同样守零 GC（仅 <see cref="Message"/> 若由调用方格式化字符串才可能分配，
    /// 而错误是冷路径、非每帧热路径，可接受）。
    /// </summary>
    public readonly struct BridgeError
    {
        /// <summary>错误类别。</summary>
        public readonly BridgeErrorKind Kind;

        /// <summary>出错请求的 seed（便于业务定位是哪一次请求失败）。</summary>
        public readonly long Seed;

        /// <summary>可选的人类可读说明（可为 null）。</summary>
        public readonly string Message;

        public BridgeError(BridgeErrorKind kind, long seed, string message = null)
        {
            Kind = kind;
            Seed = seed;
            Message = message;
        }

        /// <summary>构造一个超时错误。</summary>
        public static BridgeError Timeout(long seed, string message = null)
        {
            return new BridgeError(BridgeErrorKind.Timeout, seed, message);
        }

        /// <summary>构造一个通道失败错误。</summary>
        public static BridgeError ChannelFailure(long seed, string message = null)
        {
            return new BridgeError(BridgeErrorKind.ChannelFailure, seed, message);
        }

        /// <summary>构造一个「桥已关闭」错误。</summary>
        public static BridgeError Disposed(long seed, string message = null)
        {
            return new BridgeError(BridgeErrorKind.Disposed, seed, message);
        }

        public override string ToString()
        {
            return Message == null
                ? "BridgeError(" + Kind + ", seed=" + Seed + ")"
                : "BridgeError(" + Kind + ", seed=" + Seed + ", \"" + Message + "\")";
        }
    }
}
