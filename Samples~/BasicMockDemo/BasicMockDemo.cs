using System;
using System.Collections.Generic;
using System.Threading;
using Likeon.NativeRelay;
using UnityEngine;

namespace Likeon.NativeRelay.Samples
{
    /// <summary>
    /// NativeRelay 最小示例：点按钮发若干请求，结果经 MockChannel 在<b>子线程</b>随机延迟后回来，
    /// 由桥安全切回<b>主线程</b>按 seed 一一对应派发。界面显示每个请求"第几帧发出 → 第几帧在主线程返回"，
    /// 直观看到跨线程派发与"即到即派发（乱序）"。
    /// <para>用法：把本组件挂到空场景里的一个空 GameObject 上，按 Play，点按钮即可。无需真机/key/联网。</para>
    /// </summary>
    /// <remarks>
    /// 看点：业务侧只调 <see cref="Bridge.Request(int, byte[], System.Action{byte[]}, System.Action{BridgeError})"/>，
    /// 拿到的 onResult 一定在主线程（可安全碰 Unity API）。
    /// UI 用 IMGUI（<see cref="OnGUI"/>）以保持<b>零额外依赖</b>（不需要 com.unity.ugui），任何工程都能直接跑。
    /// </remarks>
    public sealed class BasicMockDemo : MonoBehaviour
    {
        // 使用者自定义的命令枚举（属于业务侧，不在插件核心里）；调用时强转 int。
        private enum DemoCommand { Ping = 1 }

        [Tooltip("每次点击发起的请求数")]
        public int requestsPerClick = 5;

        private Bridge _bridge;
        private int _mainThreadId;
        private readonly List<string> _log = new List<string>();
        private GUIStyle _logStyle;

        private void Start()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;

            // —— NativeRelay 用法（核心三步）——
            // 1) 准备一个原生通道（这里用纯 C# Mock：子线程随机延迟后回结果，结果回显 seed）。
            var channel = new MockChannel(minDelayMs: 100, maxDelayMs: 800);
            // 2) 通过每帧驱动的 dispatcher 创建桥；它会每帧自动 Pump 把结果切回主线程派发。
            _bridge = MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds: 5.0);
            // 3) 业务发请求 → 在 onResult（主线程）里处理。见 SendBatch()。

            AppendLog("点 [Send] 发起请求；结果会在随后的某一帧于主线程返回（注意乱序）。");
        }

        private void SendBatch()
        {
            for (int i = 0; i < requestsPerClick; i++)
            {
                int sentFrame = Time.frameCount;
                // 闭包捕获本请求的发出帧；seed 由 Request 返回。
                long seed = _bridge.Request(
                    command: (int)DemoCommand.Ping,
                    payload: null,
                    onResult: result =>
                    {
                        bool onMain = Thread.CurrentThread.ManagedThreadId == _mainThreadId;
                        long echoed = BitConverter.ToInt64(result, 0);
                        AppendLog($"#{echoed}  发出帧 {sentFrame} → 返回帧 {Time.frameCount}  主线程={onMain}");
                    },
                    onError: err => AppendLog($"#{err.Seed}  失败：{err.Kind}"));

                AppendLog($"#{seed}  已发出（帧 {sentFrame}），等待子线程回结果…");
            }
        }

        private void AppendLog(string line)
        {
            _log.Add(line);
            if (_log.Count > 16) _log.RemoveAt(0); // 只保留最近若干行
        }

        private void OnDestroy()
        {
            // 关桥：未完成请求会收到 Disposed，底层通道也会被 Dispose。
            _bridge?.Dispose();
        }

        // IMGUI：每帧在主线程绘制，零额外依赖。仅供 demo 展示，不是 NativeRelay 的一部分。
        private void OnGUI()
        {
            if (_logStyle == null)
            {
                _logStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            }

            GUILayout.BeginArea(new Rect(20, 20, Mathf.Min(Screen.width - 40, 760), Screen.height - 40));
            GUILayout.Label("<b>NativeRelay · BasicMockDemo</b>", new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true });
            GUILayout.Space(6);

            if (GUILayout.Button($"Send {requestsPerClick} requests", GUILayout.Height(46), GUILayout.Width(240)))
            {
                SendBatch();
            }
            if (_bridge != null)
            {
                GUILayout.Label($"in-flight (pending): {_bridge.PendingCount}", _logStyle);
            }

            GUILayout.Space(8);
            for (int i = _log.Count - 1; i >= 0; i--) // 最新在上
            {
                GUILayout.Label(_log[i], _logStyle);
            }
            GUILayout.EndArea();
        }
    }
}
