using UnityEngine;
using UnityEngine.UI;

namespace CoreSim
{
    /// <summary>
    /// 初めての人向けの画面。「はじめに」(CoRE とこのゲームの説明を 3 行) と「CoRE の詳細」(長い説明と公式サイトの QR)。
    /// 体験会ではタブレット + ゲームパッドなので、文は短く、ボタンは大きく。
    /// </summary>
    public static class OnboardingView
    {
        public const string CoreUrl = "https://core.scramble-robot.org/";

        /// <summary>「はじめに」。初回起動時に自動で出し、タイトルの「あそびかた」からも開ける。</summary>
        public static RectTransform CreateIntro(Transform parent, System.Action onAbout, System.Action onClose)
        {
            RectTransform root = UiKit.Panel(parent, "Intro", new Color(0.03f, 0.05f, 0.08f, 0.96f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            RectTransform title = UiKit.Rect(root, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(1500f, 90f));
            UiKit.Label(title, "Text", "はじめに", 56, Color.white);

            RectTransform pic = UiKit.Rect(root, "Pic", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-400f, 10f), new Vector2(760f, 430f));
            Picture(pic, "Tutorial/intro_field", new Color(0.2f, 0.3f, 0.45f, 0.9f), "フィールドの絵 (intro_field.png)");

            RectTransform body = UiKit.Rect(root, "Body", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(430f, 10f), new Vector2(820f, 480f));
            Text t = UiKit.Label(body, "Text",
                "CoRE は、自作のロボットで戦うロボット競技会です。\n\n" +
                "このゲームでは CoRE のフィールドで、ディスクを撃ち合うロボット、荷物を運ぶロボット、" +
                "自分で書いたプログラムで動くロボット、4 本脚で歩くロボットを動かせます。\n\n" +
                "操作は矢印 (左スティック) で移動、A / D (右スティック) で旋回、Space (RB) で発射です。\n\n" +
                "まずは好きなものを 1 つ選んで遊んでみてください。",
                22, Color.white, TextAnchor.UpperLeft, false);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.lineSpacing = 1.2f;

            RectTransform foot = UiKit.Rect(root, "Foot", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 100f), new Vector2(1500f, 100f));
            var layout = foot.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 30f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childAlignment = TextAnchor.MiddleCenter;
            UiKit.Button(foot, "CoRE について詳しく", new Vector2(460f, 90f), () => onAbout?.Invoke(), new Color(0.25f, 0.35f, 0.5f, 0.95f), 28);
            Button start = UiKit.Button(foot, "はじめる", new Vector2(460f, 90f), () => onClose?.Invoke(), new Color(0.85f, 0.35f, 0.2f, 0.95f), 34);
            UiKit.Select(start);
            return root;
        }

        /// <summary>「CoRE の詳細」。長い説明と公式サイトの QR コード。</summary>
        public static RectTransform CreateAbout(Transform parent, System.Action onClose)
        {
            RectTransform root = UiKit.Panel(parent, "AboutCore", new Color(0.03f, 0.05f, 0.08f, 0.96f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            RectTransform title = UiKit.Rect(root, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(1500f, 90f));
            UiKit.Label(title, "Text", "CoRE (Championship of Robotics Engineers) について", 40, Color.white);

            RectTransform body = UiKit.ScrollView(root, "Body", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-300f, 0f), new Vector2(1060f, 640f),
                                                  new Color(0f, 0f, 0f, 0.5f), new RectOffset(28, 28, 20, 20), 0f, controlChildSize: true);
            Text t = UiKit.Label(body, "Text",
                "CoRE は、学生や社会人のチームが自作ロボットで競うロボット競技会です。CoRE-1 は 24 m × 15 m のフィールドで、\n" +
                "複数のチームが 2 つの同盟に分かれて 1 ラウンド最長 7 分の試合をします。\n\n" +
                "ロボットの種類\n" +
                "・アタッカー: ディスクを射出して相手ロボットの装甲板と陣地を攻撃します。1 命中で HP が 10 減り、HP 0 で撃破です。\n" +
                "・ヘッドアタッカー: 出撃するアタッカーから 1 台。HP 300 で自動回復しますが、撃破されると復活しません。\n" +
                "・ビルダー: コンテナをスポットへ運んで得点し、ブロックをビルドサイトに積んで同盟の防御を強くします。\n" +
                "・オートアタッカー: 人が操縦せず、自律プログラムで動きます。このゲームでは「条件 → 行動」の行を並べて作ります。\n" +
                "・ストライダー: 4 本脚で歩き、障害物のあるストライダーエリアを踏破して得点します。\n\n" +
                "得点\n" +
                "VP (勝利ポイント) が多い同盟がラウンドを取ります。撃破 +10VP、陣地撃破 +100VP、コンテナ +10VP、ヘッドアタッカーの\n" +
                "減った HP ぶんも VP になります。RP (資源ポイント) はコンテナや踏破で増え、総大将がスキル (ブースト・バリア・\n" +
                "ヒーリング・ピットインなど) に使います。\n\n" +
                "このゲームは CoRE-1 ルールブック V27.1.3 (2027 シーズン) を元にしています。実際の大会の情報、ルールブック、\n" +
                "参加の方法は公式サイトを見てください (右の QR コード)。",
                19, Color.white, TextAnchor.UpperLeft, false);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.lineSpacing = 1.2f;

            RectTransform side = UiKit.Rect(root, "Side", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(520f, 0f), new Vector2(500f, 640f));
            UiKit.Round(UiKit.Image(side, "bg", new Color(1f, 1f, 1f, 0.95f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero));
            RectTransform qr = UiKit.Rect(side, "Qr", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -230f), new Vector2(400f, 400f));
            Picture(qr, "Tutorial/core_qr", Color.white, "QR");
            RectTransform url = UiKit.Rect(side, "Url", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 110f), new Vector2(480f, 120f));
            Text u = UiKit.Label(url, "Text", "公式サイト\n" + CoreUrl, 19, new Color(0.1f, 0.1f, 0.15f), TextAnchor.MiddleCenter, false);
            u.horizontalOverflow = HorizontalWrapMode.Wrap;

            RectTransform foot = UiKit.Rect(root, "Foot", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(600f, 80f));
            Button back = UiKit.Button(foot, "戻る", new Vector2(360f, 80f), () => onClose?.Invoke(), new Color(0.25f, 0.35f, 0.5f, 0.95f), 32);
            UiKit.Select(back);
            return root;
        }

        /// <summary>Resources の絵を出す。無ければ色の板に説明を書く (スクリーンショットを後から置ける)。</summary>
        public static void Picture(RectTransform parent, string resource, Color fallback, string caption)
        {
            var tex = Resources.Load<Texture2D>(resource);
            if (tex != null)
            {
                var go = new GameObject("Image", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                var raw = go.AddComponent<RawImage>();
                raw.texture = tex;
                raw.raycastTarget = false;
                return;
            }
            UiKit.Image(parent, "fallback", fallback, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Text c = UiKit.Label(parent, "Caption", caption, 18, new Color(1f, 1f, 1f, 0.7f));
            c.horizontalOverflow = HorizontalWrapMode.Wrap;
        }
    }
}
