using UnityEngine;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.UI;

namespace CoreSim
{
    /// <summary>
    /// タブレット用の画面上パッド。Input System の OnScreenStick / OnScreenButton が
    /// 仮想 Gamepad を作るので、<see cref="PlayerCommandSource"/> はゲームパッドと同じ経路で読める。
    /// </summary>
    public static class TouchControls
    {
        public static RectTransform Create(Transform parent)
        {
            RectTransform root = UiKit.Panel(parent, "TouchControls", new Color(0, 0, 0, 0),
                                             Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            Stick(root, "MoveStick", new Vector2(0f, 0f), new Vector2(220f, 220f), "<Gamepad>/leftStick");
            Stick(root, "TurnStick", new Vector2(1f, 0f), new Vector2(-220f, 220f), "<Gamepad>/rightStick");

            Button(root, "Fire", "発射", new Vector2(1f, 0f), new Vector2(-470f, 160f), 170f,
                   new Color(0.9f, 0.3f, 0.25f, 0.75f), "<Gamepad>/rightShoulder");
            Button(root, "Turbo", "ターボ", new Vector2(1f, 0f), new Vector2(-470f, 360f), 120f,
                   new Color(0.25f, 0.6f, 0.9f, 0.7f), "<Gamepad>/leftShoulder");
            Button(root, "Map", "マップ", new Vector2(0f, 0f), new Vector2(430f, 180f), 110f,
                   new Color(0.4f, 0.4f, 0.45f, 0.7f), "<Gamepad>/buttonNorth");
            return root;
        }

        private static void Stick(RectTransform parent, string name, Vector2 anchor, Vector2 pos, string controlPath)
        {
            RectTransform bg = UiKit.Rect(parent, name, anchor, anchor, pos, new Vector2(260f, 260f));
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.color = new Color(1f, 1f, 1f, 0.12f);
            bgImg.raycastTarget = false;   // 当たりはノブ側 (dynamic origin の透明な円) が持つ
            RectTransform knob = UiKit.Rect(bg, "Knob", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110f, 110f));
            var knobImg = knob.gameObject.AddComponent<Image>();
            knobImg.color = new Color(1f, 1f, 1f, 0.5f);
            var stick = knob.gameObject.AddComponent<OnScreenStick>();
            stick.controlPath = controlPath;
            stick.movementRange = 80f;
            // 台座のどこに触れてもそこを原点にして倒せるようにする。OnScreenStick が
            // dynamicOriginRange の透明な円を自分で作るので、台座側の raycast は切っておく。
            stick.behaviour = OnScreenStick.Behaviour.ExactPositionWithDynamicOrigin;
            stick.dynamicOriginRange = 130f;
        }

        private static void Button(RectTransform parent, string name, string label, Vector2 anchor, Vector2 pos,
                                   float size, Color color, string controlPath)
        {
            RectTransform rt = UiKit.Rect(parent, name, anchor, anchor, pos, new Vector2(size, size));
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            var btn = rt.gameObject.AddComponent<OnScreenButton>();
            btn.controlPath = controlPath;
            UiKit.Label(rt, "Label", label, (int)(size * 0.22f), Color.white, TextAnchor.MiddleCenter, false);
        }
    }
}
