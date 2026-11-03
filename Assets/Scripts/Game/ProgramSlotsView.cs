using CoreSim.Rules;
using UnityEngine;
using UnityEngine.UI;

namespace CoreSim
{
    /// <summary>
    /// オートアタッカーのプログラムの保存と読み込み。名前付きのスロット (8 つ) に今のプログラムを保存し、
    /// 読み込むと編集中のプログラムに置き換わる。CPU に持たせるプログラムも設定でここのスロットから選ぶ。
    /// </summary>
    public sealed class ProgramSlotsView : MonoBehaviour
    {
        private GameSettings m_Settings;
        private RectTransform m_Content;
        private System.Action m_OnLoaded;

        public static ProgramSlotsView Create(Transform parent, GameSettings settings, System.Action onLoaded, System.Action onBack)
        {
            RectTransform root = UiKit.Panel(parent, "ProgramSlots", new Color(0.04f, 0.05f, 0.08f, 0.96f),
                                             Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var view = root.gameObject.AddComponent<ProgramSlotsView>();
            view.m_Settings = settings;
            view.m_OnLoaded = onLoaded;

            RectTransform title = UiKit.Rect(root, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(1500f, 60f));
            UiKit.Label(title, "Text", "プログラムの保存と読み込み", 44, Color.white);
            RectTransform help = UiKit.Rect(root, "Help", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(1500f, 40f));
            UiKit.Label(help, "Text", "名前を付けて「ここに保存」。「読み込む」で編集中のプログラムに置き換えます。CPU に持たせるプログラムは設定画面でスロットから選べます。", 20, new Color(0.85f, 0.9f, 1f));

            view.m_Content = UiKit.ScrollView(root, "Body", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(1500f, 640f),
                                              new Color(0f, 0f, 0f, 0.55f), new RectOffset(20, 20, 16, 16), 8f, controlChildSize: false);
            RectTransform foot = UiKit.Rect(root, "Foot", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(600f, 70f));
            UiKit.Button(foot, "戻る", new Vector2(240f, 60f), () => onBack?.Invoke(), new Color(0.25f, 0.35f, 0.5f, 0.95f), 26);
            view.Rebuild();
            return view;
        }

        private void Rebuild()
        {
            for (int i = m_Content.childCount - 1; i >= 0; i--) Destroy(m_Content.GetChild(i).gameObject);
            for (int i = 0; i < m_Settings.ProgramSlots.Count; i++) BuildRow(i);
        }

        private void BuildRow(int index)
        {
            GameSettings.ProgramSlot slot = m_Settings.ProgramSlots[index];
            RectTransform row = UiKit.Rect(m_Content, $"Slot{index}", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(1440f, 62f));
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
            UiKit.Label(UiKit.Rect(row, "Num", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(40f, 54f)), "Text", $"{index + 1}.", 24, Color.white);
            InputField name = UiKit.InputField(row, "Name", new Vector2(340f, 54f), slot.Name ?? "", $"プログラム {index + 1}", fontSize: 24);
            name.onEndEdit.AddListener(v => { slot.Name = v.Trim(); m_Settings.Save(); });
            bool has = !string.IsNullOrWhiteSpace(slot.Text);
            int lines = has ? AutoProgram.Parse(slot.Text).Rules.Count : 0;
            UiKit.Label(UiKit.Rect(row, "Info", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(170f, 54f)), "Text", has ? $"{lines} 行" : "(空)", 22, new Color(0.8f, 0.8f, 0.85f));
            Button load = UiKit.Button(row, "読み込む", new Vector2(170f, 54f), () =>
            {
                if (string.IsNullOrWhiteSpace(slot.Text)) return;
                m_Settings.AutoProgramText = slot.Text;
                m_Settings.Save();
                m_OnLoaded?.Invoke();
            }, new Color(0.2f, 0.4f, 0.55f, 0.95f), 22);
            load.interactable = has;
            UiKit.Button(row, "ここに保存", new Vector2(190f, 54f), () =>
            {
                slot.Text = m_Settings.AutoProgramText ?? "";
                if (string.IsNullOrWhiteSpace(slot.Name)) slot.Name = string.IsNullOrWhiteSpace(name.text) ? $"プログラム {index + 1}" : name.text.Trim();
                m_Settings.Save();
                Rebuild();
            }, new Color(0.2f, 0.45f, 0.3f, 0.95f), 22);
            Button del = UiKit.Button(row, "削除", new Vector2(110f, 54f), () => { slot.Name = ""; slot.Text = ""; m_Settings.Save(); Rebuild(); }, new Color(0.45f, 0.2f, 0.2f, 0.95f), 22);
            del.interactable = has || !string.IsNullOrEmpty(slot.Name);
        }
    }
}
