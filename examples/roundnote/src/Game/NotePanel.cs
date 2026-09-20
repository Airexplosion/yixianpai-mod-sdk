#if YX_GAME
using System;
using TMPro;
using UnityEngine;
using Yx.ModSdk;
using Yx.ModSdk.Unity;

namespace YxRoundNote
{
    /// <summary>
    /// 右上角的小面板。只在游戏里存在（整个文件包在 #if YX_GAME 里，测试工程不编它）。
    /// 用 SDK 的 UiKit：底板 + 文字 + 一个按钮；按钮的回调由 UiKit 自动兜底（异常记在这个 mod 头上，不会漏进游戏）。
    /// </summary>
    public sealed class NotePanel
    {
        const float Width = 320f;
        const float Height = 170f;
        const float Pad = 10f;

        readonly UiKit _ui;
        readonly Action _onReset;
        GameObject _root;
        TextMeshProUGUI _text;

        public NotePanel(ModContext ctx, Action onReset)
        {
            _ui = new UiKit(ctx);
            _onReset = onReset;
        }

        /// <summary>开着就关，关着就开（顺便刷新文字）。</summary>
        public void Toggle(string text)
        {
            if (_root != null && _root.activeSelf) { _root.SetActive(false); return; }
            if (_root == null) Build();
            if (_root == null) return;          // 还没有 Canvas（场景没加载完）：下次再按
            _root.SetActive(true);
            Show(text);
        }

        public void Show(string text)
        {
            if (_root == null || _text == null) return;
            _text.text = text;
        }

        void Build()
        {
            var corner = new Vector2(1f, 1f);       // 锚在屏幕右上角
            // 最后一个参数 false：纯显示的悬浮窗，不挡底下游戏界面的点击。
            _root = _ui.Panel("YxRoundNote", corner, corner, new Vector2(-12f, -90f), new Vector2(Width, Height), new Color(0f, 0f, 0f, 0.7f), false);
            if (_root == null) return;
            // 挂在面板下面的东西：位置相对面板左上角，y 向下为负。
            _text = _ui.Label(_root.transform, "text", "", new Vector2(Pad, -Pad), new Vector2(Width - Pad * 2f, Height - 60f), 20f);
            _ui.TextButton(_root.transform, "reset", "清零本次", new Vector2(Pad, -(Height - 46f)), new Vector2(120f, 36f), _onReset);
        }

        /// <summary>mod 停用 / 热重载时调：宿主不会替你清界面物体。</summary>
        public void Destroy()
        {
            _ui.DestroyAll();
            _root = null;
            _text = null;
        }
    }
}
#endif
