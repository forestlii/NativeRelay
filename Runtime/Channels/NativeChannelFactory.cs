namespace Likeon.NativeRelay
{
    /// <summary>
    /// 按当前平台挑一个 <see cref="INativeChannel"/> 实现。<b>这是整套里唯一用 <c>#if</c> 平台分发的地方</b>——
    /// 把"散在各处的宏分发"收拢到一处，藏在干净的接口后面：
    /// 每个平台的原生代码各自关在自己的通道类里（互相隔离、各自可独立编译/替换），这里只负责"选哪一个"。
    /// </summary>
    /// <remarks>
    /// 为什么必须用 <c>#if</c>（而非运行时判断）：iOS 的 <c>[DllImport("__Internal")]</c>、Windows 的
    /// <c>[DllImport("x.dll")]</c>、Android 的 JNI 都得<b>编译期</b>选定，错平台的原生引用不该被编进/链接进别的平台。
    /// <para>
    /// 测试 / 编辑器友好：<see cref="UnityEditor"/> 下返回 <see cref="MockChannel"/>，无需真机即可 Play 调试；
    /// 单测可直接 <c>new MockChannel()</c> 或任意通道注入，绕开本工厂。
    /// </para>
    /// </remarks>
    public static class NativeChannelFactory
    {
        /// <summary>创建当前平台对应的通道（Editor / Windows / 其它桌面 → Mock；Android → JNI；iOS → P/Invoke）。</summary>
        public static INativeChannel CreateForCurrentPlatform()
        {
#if UNITY_EDITOR
            return new MockChannel();   // 编辑器：用 Mock，免真机就能 Play 调
#elif UNITY_ANDROID
            return new AndroidChannel(); // JNI（需配套 .aar，见 docs/native-android.md）
#elif UNITY_IOS
            return new IosChannel();     // P/Invoke __Internal + GCHandle（需配套原生库，见 docs/native-ios.md）
#else
            return new MockChannel();   // Windows / 桌面 / 其它：手游原生行为不适用，走 Mock
#endif
        }
    }
}
