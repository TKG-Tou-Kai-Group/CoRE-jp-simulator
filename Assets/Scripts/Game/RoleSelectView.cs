using UnityEngine;
using UnityEngine.UI;

namespace CoreSim
{
    /// <summary>
    /// 大会の試合に出る前の「あなたの機体」。アタッカー / ビルダー / オートアタッカーのカードから選ぶ。
    /// ヘッドアタッカーはアタッカーの中のチェック、同盟の色もここで選ぶ。選んだ値は設定に保存する。
    /// </summary>
    public sealed class RoleSelectView : MonoBehaviour
    {
        private GameSettings m_Settings;
        private readonly Image[] m_CardBg = new Image[3];
        private Button m_HeadButton, m_SideButton, m_StartButton;
        private Text m_Summary;

        private static readonly (PlayerRole role, string name, string what, string score, string keys, string pic, Color color)[] s_Cards =
        {
            (PlayerRole.Attacker, "アタッカー", "ディスクを撃って相手ロボットと陣地を攻撃する", "撃破 +10VP、陣地撃破 +100VP (3 分後から)", "移動・旋回・発射。弾が尽きたらピットイン", "Tutorial/role_attacker", new Color(0.55f, 0.2f, 0.2f, 0.9f)),
            (PlayerRole.Builder, "ビルダー", "コンテナとブロックを運ぶ (撃てない、HP 80)", "コンテナをスポットへ +10VP、ビルドサイト獲得で防御が強くなる", "移動・旋回・ハンド上下・つかむ / 放す", "Tutorial/role_builder", new Color(0.2f, 0.45f, 0.3f, 0.9f)),
            (PlayerRole.AutoAttacker, "オートアタッカー", "自分で書いたプログラムで自動で戦う (HP 200)", "撃破 +10VP、陣地撃破 +100VP。人は見ているだけ", "試合前にプログラムを作る (設定 → 自分のプログラム)", "Tutorial/role_auto", new Color(0.2f, 0.35f, 0.5f, 0.9f)),
        };

        public static RoleSelectView Create(Transform parent, GameSettings settings, System.Action onStart, System.Action onSettings, System.Action onBack)
        {
            RectTransform root = UiKit.Panel(parent, "RoleSelect", new Color(0.03f, 0.05f, 0.08f, 0.96f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var view = root.gameObject.AddComponent<RoleSelectView>();
            view.m_Settings = settings;

            RectTransform title = UiKit.Rect(root, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(1500f, 90f));
            UiKit.Label(title, "Text", "あなたの機体を選ぶ", 56, Color.white);
            RectTransform sub = UiKit.Rect(root, "Sub", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(1500f, 40f));
            UiKit.Label(sub, "Text", "大会ルールの試合に、この機体で出ます。同盟のほかの機体は CPU です", 24, new Color(0.85f, 0.9f, 1f));

            RectTransform cards = UiKit.Rect(root, "Cards", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(1500f, 520f));
            var layout = cards.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 30f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childAlignment = TextAnchor.MiddleCenter;
            Button first = null;
            for (int i = 0; i < s_Cards.Length; i++)
            {
                int index = i;
                var c = s_Cards[i];
                RectTransform card = UiKit.Rect(cards, "Card_" + c.name, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(470f, 520f));
                view.m_CardBg[i] = UiKit.Image(card, "bg", new Color(0.12f, 0.14f, 0.18f, 0.95f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                RectTransform pic = UiKit.Rect(card, "Pic", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(440f, 220f));
                OnboardingView.Picture(pic, c.pic, c.color, $"{c.name}の絵 ({c.pic.Substring(9)}.png)");
                UiKit.Label(UiKit.Rect(card, "Name", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -258f), new Vector2(440f, 44f)), "Text", c.name, 34, Color.white);
                Text desc = UiKit.Label(UiKit.Rect(card, "Desc", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -360f), new Vector2(430f, 150f)), "Text",
                    $"{c.what}\n得点: {c.score}\n操作: {c.keys}", 19, new Color(0.9f, 0.92f, 0.95f), TextAnchor.UpperLeft, false);
                desc.horizontalOverflow = HorizontalWrapMode.Wrap;
                Button pick = UiKit.Button(card, "これにする", new Vector2(300f, 60f), () => view.Pick(index), new Color(0.3f, 0.4f, 0.55f, 0.95f), 26);
                var prt = (RectTransform)pick.transform;
                prt.anchorMin = new Vector2(0.5f, 0f);
                prt.anchorMax = new Vector2(0.5f, 0f);
                prt.anchoredPosition = new Vector2(0f, 50f);
                if (i == 0) first = pick;
            }

            RectTransform opts = UiKit.Rect(root, "Opts", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 200f), new Vector2(1500f, 64f));
            var ol = opts.gameObject.AddComponent<HorizontalLayoutGroup>();
            ol.spacing = 20f;
            ol.childControlWidth = false;
            ol.childControlHeight = false;
            ol.childForceExpandWidth = false;
            ol.childAlignment = TextAnchor.MiddleCenter;
            view.m_HeadButton = UiKit.Button(opts, "", new Vector2(600f, 56f), () => { settings.Role = settings.Role == PlayerRole.Head ? PlayerRole.Attacker : PlayerRole.Head; view.Refresh(); }, new Color(0.3f, 0.3f, 0.4f, 0.95f), 22);
            view.m_SideButton = UiKit.Button(opts, "", new Vector2(360f, 56f), () => { settings.PlayerIsRed = !settings.PlayerIsRed; view.Refresh(); }, new Color(0.3f, 0.3f, 0.4f, 0.95f), 22);
            UiKit.Button(opts, "くわしい設定", new Vector2(260f, 56f), () => onSettings?.Invoke(), new Color(0.25f, 0.35f, 0.4f, 0.95f), 22);

            RectTransform foot = UiKit.Rect(root, "Foot", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 100f), new Vector2(1500f, 90f));
            var fl = foot.gameObject.AddComponent<HorizontalLayoutGroup>();
            fl.spacing = 30f;
            fl.childControlWidth = false;
            fl.childControlHeight = false;
            fl.childForceExpandWidth = false;
            fl.childAlignment = TextAnchor.MiddleCenter;
            UiKit.Button(foot, "戻る", new Vector2(240f, 80f), () => onBack?.Invoke(), new Color(0.25f, 0.25f, 0.3f, 0.95f), 28);
            view.m_Summary = UiKit.Label(UiKit.Rect(foot, "Summary", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(640f, 80f)), "Text", "", 22, new Color(0.85f, 0.9f, 1f));
            view.m_StartButton = UiKit.Button(foot, "試合開始", new Vector2(360f, 80f), () => { settings.Save(); onStart?.Invoke(); }, new Color(0.85f, 0.35f, 0.2f, 0.95f), 36);
            view.Refresh();
            if (first != null) UiKit.Select(first);
            return view;
        }

        private void Pick(int index)
        {
            m_Settings.Role = s_Cards[index].role;   // アタッカーのカードはヘッドの指定を外す (下のボタンで付ける)
            Refresh();
            UiKit.Select(m_StartButton);
        }

        private void Refresh()
        {
            for (int i = 0; i < s_Cards.Length; i++)
            {
                bool on = m_Settings.Role == s_Cards[i].role || (m_Settings.Role == PlayerRole.Head && s_Cards[i].role == PlayerRole.Attacker);
                m_CardBg[i].color = on ? new Color(0.35f, 0.3f, 0.15f, 0.98f) : new Color(0.12f, 0.14f, 0.18f, 0.95f);
            }
            bool attacker = m_Settings.Role == PlayerRole.Attacker || m_Settings.Role == PlayerRole.Head;
            m_HeadButton.interactable = attacker;
            UiKit.SetButtonLabel(m_HeadButton, attacker
                ? (m_Settings.Role == PlayerRole.Head ? "ヘッドアタッカーで出る: はい (HP 300 で回復するが復活なし)" : "ヘッドアタッカーで出る: いいえ (HP 60、30 秒で復活)")
                : "ヘッドアタッカーはアタッカーだけ");
            UiKit.SetButtonLabel(m_SideButton, m_Settings.PlayerIsRed ? "同盟: 赤 (右から攻める)" : "同盟: 青 (左から攻める)");
            m_Summary.text = $"{(m_Settings.PlayerIsRed ? "赤" : "青")}同盟の{MenuView.RoleName(m_Settings.Role)}として、{m_Settings.RobotsPerSide} 台 (ビルダー {m_Settings.EffectiveBuilders} 台" +
                             $"{(m_Settings.AutosPerSide > 0 || m_Settings.Role == PlayerRole.AutoAttacker ? "、オート 1 台" : "")}) vs {m_Settings.RobotsPerSide} 台";
        }
    }
}
