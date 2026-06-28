using System.Collections.Generic;
using UnityEngine;

namespace NativeRelay
{
    /// <summary>
    /// 唯一一个依赖 UnityEngine 的核心类：驱动 <see cref="Bridge"/> 的「每帧主线程派发」的<b>薄壳</b>。
    /// 真正的逻辑（排干队列、按 seed 派发、超时清理）都在纯 C# 的 <see cref="RelayPump"/> 里；
    /// 本 MonoBehaviour 只做两件事：① 提供主线程时钟（<see cref="Now"/>）；② 每帧 <c>Update</c> 调一次每个已注册 Bridge 的 Pump。
    /// </summary>
    /// <remarks>
    /// 这样切分（壳/核分离）是有意为之：核心可脱离 Unity 用断言验证、零 GC 可独立度量，
    /// 也让未来 JNI/P-Invoke 边界保持不碰 Unity API。
    /// 单例自动创建为一个隐藏的 DontDestroyOnLoad GameObject，业务无需手动挂载。
    /// </remarks>
    [AddComponentMenu("")] // 不在 AddComponent 菜单里露出（仅内部自动创建）
    public sealed class MainThreadDispatcher : MonoBehaviour
    {
        private static MainThreadDispatcher _instance;
        private static bool _appQuitting;

        private readonly List<Bridge> _bridges = new List<Bridge>(8);

        /// <summary>全局单例（首次访问时自动创建隐藏对象）。应用退出后返回 null。</summary>
        public static MainThreadDispatcher Instance
        {
            get
            {
                if (_instance == null && !_appQuitting)
                {
                    var go = new GameObject("[NativeRelay.MainThreadDispatcher]");
                    go.hideFlags = HideFlags.HideAndDontSave;
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<MainThreadDispatcher>();
                }
                return _instance;
            }
        }

        /// <summary>主线程时钟（秒）：请求时间戳与超时判定共用此源。</summary>
        public double Now
        {
            get { return Time.realtimeSinceStartupAsDouble; }
        }

        /// <summary>
        /// 便捷工厂：创建一个由本 dispatcher 每帧驱动、时钟取自 <see cref="Now"/> 的 <see cref="Bridge"/>。
        /// </summary>
        public Bridge CreateBridge(INativeChannel channel, double timeoutSeconds = 10.0, int capacity = 64)
        {
            var bridge = new Bridge(channel, () => Now, timeoutSeconds, capacity);
            Register(bridge);
            return bridge;
        }

        /// <summary>登记一个 Bridge，使其每帧被 Pump（重复登记无副作用）。</summary>
        public void Register(Bridge bridge)
        {
            if (bridge != null && !_bridges.Contains(bridge))
            {
                _bridges.Add(bridge);
            }
        }

        /// <summary>注销一个 Bridge，停止对它的每帧 Pump。</summary>
        public void Unregister(Bridge bridge)
        {
            _bridges.Remove(bridge);
        }

        private void Update()
        {
            // for-index 遍历（不产生枚举器分配）；顺手清理已 Dispose 的 Bridge。
            for (int i = _bridges.Count - 1; i >= 0; i--)
            {
                var b = _bridges[i];
                if (b == null || b.IsDisposed)
                {
                    _bridges.RemoveAt(i);
                    continue;
                }
                b.Pump();
            }
        }

        private void OnApplicationQuit()
        {
            _appQuitting = true;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
