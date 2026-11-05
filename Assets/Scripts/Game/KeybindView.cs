using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace CoreSim
{
    /// <summary>
    /// 操作の割り当ての画面。操作ごとにキー 2 つ・パッドのボタン 2 つ。枠を押してからキーかボタンを押すと入れ替わる。
    /// 移動と旋回のスティック、スキルの番号キー、ストライダーの Q W O P は固定。
    /// </summary>
    public sealed class KeybindView : MonoBehaviour
    {
        private GameSettings m_Settings;
        private InputBindings m_Bindings;
        private RectTransform m_Content;
        private Text m_Status;
        private InputAct m_CaptureAct;
        private int m_CaptureSlot = -1;
        private bool m_CapturePad;
        private float m_CaptureSince;

        public static KeybindView Create(Transform parent, GameSettings settings, System.Action onBack)
        {
            RectTransform root = UiKit.Panel(parent, "Keybinds", new Color(0.04f, 0.05f, 0.08f, 0.96f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var view = root.gameObject.AddComponent<KeybindView>();
            view.m_Settings = settings;
            view.m_Bindings = InputBindings.Parse(settings.BindingsText);

            RectTransform title = UiKit.Rect(root, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(1500f, 60f));
            UiKit.Label(title, "Text", "操作の割り当て", 40, Color.white);
            RectTransform help = UiKit.Rect(root, "Help", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(1500f, 50f));
            Text h = UiKit.Label(help, "Text", "枠を押してから、割り当てたいキーかボタンを押します (Esc で取り消し)。スティック (移動・旋回)、スキルの番号キー 1〜9、ストライダーの Q W O P は固定です。", 16, new Color(0.85f, 0.9f, 1f));
            h.horizontalOverflow = HorizontalWrapMode.Wrap;

            view.m_Content = UiKit.ScrollView(root, "Body", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(1500f, 640f),
                                              new Color(0f, 0f, 0f, 0.55f), new RectOffset(20, 20, 16, 16), 6f, controlChildSize: false);
            RectTransform foot = UiKit.Rect(root, "Foot", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(1500f, 70f));
            var fl = foot.gameObject.AddComponent<HorizontalLayoutGroup>();
            fl.spacing = 20f;
            fl.childControlWidth = false;
            fl.childControlHeight = false;
            fl.childForceExpandWidth = false;
            fl.childAlignment = TextAnchor.MiddleCenter;
            view.m_Status = UiKit.Label(UiKit.Rect(foot, "Status", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(640f, 60f)), "Text", "", 18, new Color(1f, 0.9f, 0.5f));
            UiKit.Button(foot, "既定に戻す", new Vector2(240f, 60f), () => { view.m_Bindings = InputBindings.Defaults(); view.Save(); view.Rebuild(); }, new Color(0.35f, 0.3f, 0.2f, 0.95f), 24);
            UiKit.Button(foot, "戻る", new Vector2(240f, 60f), () => { view.Save(); onBack?.Invoke(); }, new Color(0.25f, 0.35f, 0.5f, 0.95f), 24);
            view.Rebuild();
            return view;
        }

        private void Save()
        {
            m_Settings.BindingsText = m_Bindings.Serialize();
            m_Settings.Save();
            InputBindings.Current = InputBindings.Parse(m_Settings.BindingsText);
        }

        private void Rebuild()
        {
            for (int i = m_Content.childCount - 1; i >= 0; i--) Destroy(m_Content.GetChild(i).gameObject);
            Header();
            foreach (InputAct a in InputBindings.All) Row(a);
        }

        private void Header()
        {
            RectTransform row = UiKit.Rect(m_Content, "Header", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(1440f, 36f));
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            Row(layout);
            UiKit.Label(UiKit.Rect(row, "A", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(360f, 34f)), "Text", "操作", 18, new Color(0.8f, 0.85f, 0.9f), TextAnchor.MiddleLeft);
            UiKit.Label(UiKit.Rect(row, "K", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(500f, 34f)), "Text", "キーボード", 18, new Color(0.8f, 0.85f, 0.9f));
            UiKit.Label(UiKit.Rect(row, "P", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(500f, 34f)), "Text", "ゲームパッド", 18, new Color(0.8f, 0.85f, 0.9f));
        }

        private static void Row(HorizontalLayoutGroup layout)
        {
            layout.spacing = 10f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
        }

        private void Row(InputAct a)
        {
            RectTransform row = UiKit.Rect(m_Content, "Row_" + a, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(1440f, 54f));
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            Row(layout);
            UiKit.Label(UiKit.Rect(row, "Name", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(360f, 50f)), "Text", InputBindings.ActName(a), 19, Color.white, TextAnchor.MiddleLeft);
            for (int slot = 0; slot < InputBindings.Slots; slot++)
            {
                int s = slot;
                bool capturing = m_CaptureSlot == s && m_CaptureAct == a && !m_CapturePad;
                UiKit.Button(row, capturing ? "キーを押す…" : InputBindings.KeyName(m_Bindings.GetKey(a, s)), new Vector2(245f, 48f),
                             () => BeginCapture(a, s, false), capturing ? new Color(0.8f, 0.5f, 0.2f, 0.95f) : new Color(0.2f, 0.25f, 0.35f, 0.95f), 18);
            }
            for (int slot = 0; slot < InputBindings.Slots; slot++)
            {
                int s = slot;
                bool capturing = m_CaptureSlot == s && m_CaptureAct == a && m_CapturePad;
                if (!InputBindings.PadAssignable(a))
                {
                    UiKit.Label(UiKit.Rect(row, "Fixed", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(245f, 48f)), "Text", s == 0 ? "スティック (固定)" : "", 16, new Color(0.7f, 0.7f, 0.75f));
                    continue;
                }
                UiKit.Button(row, capturing ? "ボタンを押す…" : InputBindings.ButtonName(m_Bindings.GetButton(a, s)), new Vector2(245f, 48f),
                             () => BeginCapture(a, s, true), capturing ? new Color(0.8f, 0.5f, 0.2f, 0.95f) : new Color(0.2f, 0.3f, 0.25f, 0.95f), 18);
            }
        }

        private void BeginCapture(InputAct a, int slot, bool pad)
        {
            m_CaptureAct = a;
            m_CaptureSlot = slot;
            m_CapturePad = pad;
            m_CaptureSince = Time.unscaledTime;
            m_Status.text = pad ? $"{InputBindings.ActName(a)}: ゲームパッドのボタンを押してください (Esc で取り消し)" : $"{InputBindings.ActName(a)}: キーを押してください (Esc で取り消し。Delete で空に)";
            Rebuild();
        }

        private void Update()
        {
            if (m_CaptureSlot < 0) return;
            if (Time.unscaledTime - m_CaptureSince < 0.25f) return;   // 枠を押したクリック / ボタンを拾わない
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { EndCapture("取り消しました"); return; }
            if (!m_CapturePad)
            {
                if (kb == null) return;
                if (kb.deleteKey.wasPressedThisFrame) { m_Bindings.SetKey(m_CaptureAct, m_CaptureSlot, Key.None); EndCapture("空にしました"); return; }
                foreach (KeyControl k in kb.allKeys)
                {
                    if (!k.wasPressedThisFrame || k.keyCode == Key.None || k.keyCode == Key.Escape) continue;
                    m_Bindings.SetKey(m_CaptureAct, m_CaptureSlot, k.keyCode);
                    EndCapture($"{InputBindings.ActName(m_CaptureAct)} = {InputBindings.KeyName(k.keyCode)}");
                    return;
                }
            }
            else
            {
                foreach (Gamepad pad in Gamepad.all)
                {
                    PadButton b = InputBindings.AnyPressed(pad);
                    if (b == PadButton.None) continue;
                    m_Bindings.SetButton(m_CaptureAct, m_CaptureSlot, b);
                    EndCapture($"{InputBindings.ActName(m_CaptureAct)} = {InputBindings.ButtonName(b)}");
                    return;
                }
            }
        }

        private void EndCapture(string status)
        {
            m_CaptureSlot = -1;
            m_Status.text = status;
            Save();
            Rebuild();
        }
    }
}
