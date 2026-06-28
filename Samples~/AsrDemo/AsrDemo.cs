using System.Collections.Generic;
using System.Threading;
using Likeon.NativeRelay;
using UnityEngine;

namespace Likeon.NativeRelay.Samples
{
    /// <summary>
    /// 「应用示例」：用 NativeRelay 把一个<b>模拟的语音识别（ASR）</b>结果从子线程安全送回主线程，驱动一段假对话。
    /// 你在输入框打字（或点快捷短语）= 模拟"说了一句话" → 框架发一个 <c>Recognize</c> 请求，把这句话作为 payload 传下去 →
    /// <see cref="MockChannel"/> 在子线程随机延迟后把它当"识别出的文本"回传（<b>纯模拟、不接任何真实 ASR 引擎/麦克风</b>）→
    /// NativeRelay 切回主线程，业务拿到文本驱动 NPC 回应。展示的是<b>跨层异步通信</b>，不是任何具体业务逻辑。
    /// <para>用法：挂到空场景里的空 GameObject，按 Play，输入或选一句，点 Speak。无需真机/麦克风/key/联网。</para>
    /// </summary>
    /// <remarks>
    /// 真实 ASR 是"语音→文本"；这里没有麦克风，所以由你提供"说的文本"，Mock 把它原样当识别结果回传——
    /// 重点演示的是中继链路（子线程延迟 → 切回主线程派发），而非识别本身。
    /// 接真实 ASR 时把 <see cref="MockChannel"/> 换成你的 <see cref="INativeChannel"/>，在其 OnResult 里回传真实识别文本即可，业务侧不变。
    /// UI 用 IMGUI（<see cref="OnGUI"/>）保持零额外依赖。
    /// </remarks>
    public sealed class AsrDemo : MonoBehaviour
    {
        // 使用者自定义命令（业务侧，不在插件核心里）；调用时强转 int。
        private enum AsrDemoCommand { StartRecord = 1, StopRecord = 2, Recognize = 3 }

        // 快捷短语（一键"说这句"）。这些词能命中下面 Reply() 的关键字，得到有意义的回应。
        private static readonly string[] QuickPhrases =
        {
            "你好", "今天天气怎么样", "向前走", "打开背包", "保存进度", "再见",
        };

        private Bridge _bridge;
        private int _mainThreadId;
        private readonly List<string> _dialogue = new List<string>();
        private GUIStyle _lineStyle;
        private string _input = "你好";
        private bool _waiting;

        private void Start()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;

            // 通道：MockChannel 把请求的 payload（你"说"的文本）当"识别结果"原样回传(code=1)，模拟语音→文本回流。
            // 纯码契约下 payload/data 都是 string，不用编解码。resultFactory 在子线程执行，但只读字符串、线程安全。
            var channel = new MockChannel(
                minDelayMs: 200, maxDelayMs: 900,
                resultFactory: (seed, command, payload) =>
                    (1, string.IsNullOrEmpty(payload) ? "（没说话）" : payload));
            _bridge = MainThreadDispatcher.Instance.CreateBridge(channel, timeoutSeconds: 5.0);

            AppendLine("（在输入框打一句话，或点下面的快捷短语，再点 Speak —— 识别结果会在随后某帧回到主线程驱动 NPC）");
        }

        // text = 你"说"的话。作为 payload 走桥，Mock 在子线程延迟后当识别结果回传。
        private void Speak(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || _waiting) return;

            _waiting = true;
            AppendLine($"🎤 你说：「{text}」（识别中…）");

            _bridge.Request(
                command: (int)AsrDemoCommand.Recognize,
                payload: text,                       // 你说的话直接作为 string payload 走桥
                onResult: (code, data) =>
                {
                    _waiting = false;
                    if (code == RelayCode.Timeout) { AppendLine("⚠ 识别超时"); return; }
                    bool onMain = Thread.CurrentThread.ManagedThreadId == _mainThreadId;
                    string recognized = data;        // data 就是识别文本
                    AppendLine($"🗣 识别结果（主线程={onMain}）：{recognized}");
                    AppendLine($"🤖 NPC：{Reply(recognized)}");
                });
        }

        // 极简"假剧情"：按识别文本给个通用回应（仅演示业务侧拿到文本后能驱动逻辑）。
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
            if (_dialogue.Count > 14) _dialogue.RemoveAt(0);
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

            // 输入框 +（模拟）说话按钮
            GUILayout.Label("模拟你要说的话：", _lineStyle);
            GUILayout.BeginHorizontal();
            GUI.enabled = !_waiting;
            _input = GUILayout.TextField(_input ?? string.Empty, GUILayout.Width(560), GUILayout.Height(28));
            if (GUILayout.Button("🎤 Speak", GUILayout.Width(120), GUILayout.Height(28)))
            {
                Speak(_input);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            // 快捷短语：点一下 = 把这句填入输入框（不直接发，方便你看清"说了什么"再 Speak）
            GUILayout.Space(4);
            GUILayout.Label("快捷短语（点一下填入上面输入框）：", _lineStyle);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < QuickPhrases.Length; i++)
            {
                if (GUILayout.Button(QuickPhrases[i], GUILayout.Height(26)))
                {
                    _input = QuickPhrases[i];
                }
            }
            GUILayout.EndHorizontal();

            // 对话日志
            GUILayout.Space(10);
            for (int i = 0; i < _dialogue.Count; i++)
            {
                GUILayout.Label(_dialogue[i], _lineStyle);
            }
            GUILayout.EndArea();
        }
    }
}
