using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Likeon.NativeRelay;
using UnityEngine;

namespace Likeon.NativeRelay.Samples
{
    /// <summary>
    /// 「应用示例」：用 NativeRelay 把一个<b>模拟的语音识别（ASR）</b>结果从子线程安全送回主线程，驱动一段假对话。
    /// 点 <b>Speak</b> = 模拟说了一句话 → 框架发一个 <c>Recognize</c> 请求 → <see cref="MockChannel"/> 在子线程随机延迟后
    /// 回传一段"识别出的文本"（这里是预置短语，<b>纯模拟、不接任何真实 ASR 引擎</b>）→ NativeRelay 切回主线程，
    /// 业务拿到文本驱动 NPC 回应。展示的是<b>跨层异步通信</b>，不是任何具体业务逻辑。
    /// <para>用法：挂到空场景里的空 GameObject，按 Play，点 Speak。无需真机/麦克风/key/联网。</para>
    /// </summary>
    /// <remarks>
    /// 接真实 ASR 时：把 <see cref="MockChannel"/> 换成你自己的 <see cref="INativeChannel"/> 实现
    /// （Android 录音+识别 / iOS 等），在其 <c>OnResult</c> 里回传真实识别文本即可，业务侧代码不用改。
    /// UI 用 IMGUI（<see cref="OnGUI"/>）保持零额外依赖。
    /// </remarks>
    public sealed class AsrDemo : MonoBehaviour
    {
        // 使用者自定义命令（业务侧，不在插件核心里）；调用时强转 int。
        private enum AsrDemoCommand { StartRecord = 1, StopRecord = 2, Recognize = 3 }

        // 预置的"识别结果"短语池（纯模拟）。真实场景这里是 ASR 引擎的输出。
        private static readonly string[] FakePhrases =
        {
            "你好", "今天天气怎么样", "向前走", "打开背包", "保存进度", "再见",
        };

        private Bridge _bridge;
        private int _mainThreadId;
        private readonly List<string> _dialogue = new List<string>();
        private GUIStyle _lineStyle;
        private bool _waiting;

        private void Start()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;

            // 通道：用 MockChannel，但让它的结果是"识别文本"（自定义 resultFactory），模拟语音→文本回流。
            var channel = new MockChannel(
                minDelayMs: 200, maxDelayMs: 900,
                resultFactory: (seed, command, payload) =>
                {
                    // 注意：resultFactory 在 MockChannel 的【子线程】上执行（模拟原生侧产出结果），
                    // 这里【绝不能】用 UnityEngine.Random 等只能主线程调的 API，否则会抛异常→结果发不出→超时。
                    // 改用 seed 确定性选短语：线程安全、零分配、点 Speak 会循环切换短语。
                    string phrase = FakePhrases[(int)(seed % FakePhrases.Length)];
                    return Encoding.UTF8.GetBytes(phrase); // 模拟"识别出的文本"字节
                });
            _bridge = MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds: 5.0);

            AppendLine("（点 Speak 模拟说一句话，识别结果会在随后某帧回到主线程，驱动 NPC 回应）");
        }

        private void Speak()
        {
            _waiting = true;
            AppendLine("🎤 （识别中…）");

            // 业务侧：发一个 Recognize 请求，结果在 onResult（主线程）里拿到识别文本。
            _bridge.Request(
                command: (int)AsrDemoCommand.Recognize,
                payload: null,
                onResult: result =>
                {
                    _waiting = false;
                    bool onMain = Thread.CurrentThread.ManagedThreadId == _mainThreadId;
                    string text = Encoding.UTF8.GetString(result);
                    AppendLine($"🗣 你（识别，主线程={onMain}）：{text}");
                    AppendLine($"🤖 NPC：{Reply(text)}");
                },
                onError: err =>
                {
                    _waiting = false;
                    AppendLine($"⚠ 识别失败：{err.Kind}");
                });
        }

        // 极简的"假剧情"：按识别文本给个通用回应（仅为演示业务侧拿到文本后能驱动逻辑）。
        private static string Reply(string recognized)
        {
            if (recognized.Contains("你好")) return "你好，旅行者。";
            if (recognized.Contains("天气")) return "今天是个赶路的好天气。";
            if (recognized.Contains("走")) return "好的，我们继续前进。";
            if (recognized.Contains("背包")) return "（打开了背包界面）";
            if (recognized.Contains("保存")) return "进度已保存。";
            if (recognized.Contains("再见")) return "一路平安。";
            return "（我没太听清，可以再说一次吗？）";
        }

        private void AppendLine(string line)
        {
            _dialogue.Add(line);
            if (_dialogue.Count > 16) _dialogue.RemoveAt(0);
        }

        private void OnDestroy()
        {
            _bridge?.Dispose();
        }

        private void OnGUI()
        {
            if (_lineStyle == null)
            {
                _lineStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            }

            GUILayout.BeginArea(new Rect(20, 20, Mathf.Min(Screen.width - 40, 760), Screen.height - 40));
            GUILayout.Label("<b>Likeon.NativeRelay · AsrDemo（模拟语音→文本）</b>",
                new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true });
            GUILayout.Space(6);

            GUI.enabled = !_waiting;
            if (GUILayout.Button("🎤 Speak（模拟说一句话）", GUILayout.Height(46), GUILayout.Width(260)))
            {
                Speak();
            }
            GUI.enabled = true;

            GUILayout.Space(10);
            for (int i = 0; i < _dialogue.Count; i++)
            {
                GUILayout.Label(_dialogue[i], _lineStyle);
            }
            GUILayout.EndArea();
        }
    }
}
