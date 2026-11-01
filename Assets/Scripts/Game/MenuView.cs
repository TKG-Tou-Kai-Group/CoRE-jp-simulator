using UnityEngine;
using UnityEngine.UI;

namespace CoreSim
{
    /// <summary>タイトル画面。CoRE の説明と、遊び方の選択。細かい設定は <see cref="SettingsView"/> に分けてある。</summary>
    public sealed class MenuView : MonoBehaviour
    {
        private GameSettings m_Settings;
        private Button m_RulesButton, m_ModeButton;
        private Text m_Summary;

        public const string AboutText =
            "CoRE (Championship of Robotics Engineers) は、学生や社会人が自作ロボットで競う\n" +
            "ロボット競技会です。このシミュレータは CoRE-1 の 2027 フィールド (24m × 15m) での撃ち合いを体験できます。\n\n" +
            "かんたんルール: 装甲板にディスクが当たるたびに HP が 10 減り (初期 200)、時間切れで HP が多い側の勝ち。\n" +
            "  自動復活が無い代わりに HP を多くした特別ルールで、1 vs 1 や 2 vs 2 に向いています。\n\n" +
            "大会ルール (CoRE-1 ルールブック V27.1.3、アタッカーのみ): 赤同盟 vs 青同盟、1 ラウンド 7 分、VP 500 で終了。\n" +
            "  撃破 +10VP、陣地撃破 +100VP (開始 3 分後から有効)、ヘッドアタッカー (HP 300) の減った HP が VP に。\n" +
            "  RP でスキル (ブースト・バリア・ヒーリング・リジェネ・ピットイン・サプライ)。残弾が尽きたらピットインで補給。\n\n" +
            "視点は大会と同じく機体に付いたカメラ (一人称) だけです。俯瞰は左下の地図で確認します。\n\n" +
            "操作 (キーボード)  移動: 矢印 / W S    旋回: A D    発射: Space    ターボ: Shift    地図: V    スキル: 1〜9    メニュー: Esc\n" +
            "操作 (ゲームパッド)  移動: 左スティック    旋回: 右スティック    発射: RB / RT    ターボ: LB    地図: Y    スキル: 十字← → と X\n" +
            "操作 (タブレット)  画面左右の仮想スティックとボタン\n" +
            "ネット対戦: 同じ LAN か、閉域網で 1 ポート (既定 UDP 7777) が通れば遊べます";

        public static MenuView Create(Transform parent, GameSettings settings, System.Action onStart, System.Action onNetwork,
                                      System.Action onSettings, System.Action onQuit)
        {
            RectTransform root = UiKit.Panel(parent, "Menu", new Color(0.05f, 0.07f, 0.1f, 0.55f),
                                             Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var menu = root.gameObject.AddComponent<MenuView>();
            menu.m_Settings = settings;

            RectTransform title = UiKit.Rect(root, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(1400f, 160f));
            UiKit.Label(title, "Main", "CoRE Simulator", 110, Color.white);
            RectTransform sub = UiKit.Rect(root, "Sub", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -210f), new Vector2(1400f, 50f));
            UiKit.Label(sub, "Text", "Championship of Robotics Engineers  CoRE-1 2027 フィールド", 32, new Color(0.8f, 0.85f, 0.95f));

            // 左: 説明 (縦スクロール)
            RectTransform about = UiKit.ScrollView(root, "About", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(560f, -50f), new Vector2(1000f, 700f),
                                                   new Color(0f, 0f, 0f, 0.55f), new RectOffset(28, 28, 20, 20), 0f, controlChildSize: true);
            Text aboutText = UiKit.Label(about, "Text", AboutText, 24, Color.white, TextAnchor.UpperLeft, false);
            aboutText.horizontalOverflow = HorizontalWrapMode.Wrap;
            aboutText.verticalOverflow = VerticalWrapMode.Truncate;
            aboutText.lineSpacing = 1.1f;

            // 右: 選ぶのは「ルール」と「遊び方」だけ。細かい数値は設定画面へ。
            RectTransform options = UiKit.Rect(root, "Options", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-370f, -50f), new Vector2(620f, 700f));
            UiKit.Image(options, "bg", new Color(0f, 0f, 0f, 0.55f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var layout = options.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(30, 30, 24, 24);
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            Vector2 size = new Vector2(560f, 68f);
            menu.m_RulesButton = UiKit.Button(options, "", size, () => { settings.Rules = settings.Rules == Ruleset.Casual ? Ruleset.Tournament : Ruleset.Casual; menu.Refresh(); },
                                              new Color(0.3f, 0.25f, 0.45f, 0.95f), 28);
            menu.m_ModeButton = UiKit.Button(options, "", size, () => { settings.Mode = (GameMode)(((int)settings.Mode + 1) % 4); menu.Refresh(); }, fontSize: 28);
            RectTransform summaryRow = UiKit.Rect(options, "Summary", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(560f, 150f));
            menu.m_Summary = UiKit.Label(summaryRow, "Text", "", 22, new Color(0.85f, 0.9f, 1f), TextAnchor.UpperLeft, false);
            menu.m_Summary.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.Button(options, "試合開始", new Vector2(560f, 88f), () => { settings.Save(); onStart?.Invoke(); },
                         new Color(0.85f, 0.35f, 0.2f, 0.95f), 40);
            UiKit.Button(options, "ネット対戦 (LAN / 閉域網)", size, () => { settings.Save(); onNetwork?.Invoke(); },
                         new Color(0.2f, 0.5f, 0.85f, 0.95f), 30);
            UiKit.Button(options, "設定 (ルールの詳細・操作・音)", size, () => { settings.Save(); onSettings?.Invoke(); },
                         new Color(0.25f, 0.35f, 0.4f, 0.95f), 28);
            if (!Application.isMobilePlatform)
            {
                UiKit.Button(options, "終了", new Vector2(560f, 50f), () => onQuit?.Invoke(), new Color(0.25f, 0.25f, 0.3f, 0.9f), 24);
            }

            RectTransform foot = UiKit.Rect(root, "Foot", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(1600f, 40f));
            UiKit.Label(foot, "Text", "Unity_ROS2_Robot_Simulator / CoRE-jp-Unity-ROS2-packages をもとにした単体ゲーム版  v0.3.1", 20, new Color(0.7f, 0.7f, 0.75f));

            menu.Refresh();
            return menu;
        }

        private void Refresh()
        {
            bool tournament = m_Settings.Rules == Ruleset.Tournament;
            UiKit.SetButtonLabel(m_RulesButton, tournament ? "ルール: 大会ルール (CoRE-1 V27.1.3)" : "ルール: かんたん (HP 200 の撃ち合い)");
            m_ModeButton.gameObject.SetActive(!tournament);
            UiKit.SetButtonLabel(m_ModeButton, m_Settings.Mode switch
            {
                GameMode.SoloVsCpu => "遊び方: ひとりで対戦 (個人戦 vs CPU)",
                GameMode.TeamVsCpu => "遊び方: チーム戦 (2 vs 2、味方は CPU)",
                GameMode.LocalVersus => "遊び方: ふたりで対戦 (画面分割、パッド 2 台)",
                _ => "遊び方: 練習 (時間無制限)",
            });
            m_Summary.text = tournament
                ? $"赤 {m_Settings.RobotsPerSide} 台 vs 青 {m_Settings.RobotsPerSide} 台、{BestOfText(m_Settings.BestOf)}\n" +
                  $"自分: {(m_Settings.PlayerIsRed ? "赤" : "青")}同盟の{(m_Settings.PlayerIsHead ? "ヘッドアタッカー" : "アタッカー")}、弾倉 {m_Settings.Magazine} 枚\n" +
                  $"機体の速さ: {(m_Settings.Speed == SpeedPreset.Game ? "ゲーム" : "リアル")}"
                : $"{(m_Settings.Mode == GameMode.SoloVsCpu ? $"CPU {m_Settings.CpuCount} 台、" : "")}" +
                  $"{(m_Settings.Mode == GameMode.Practice ? "時間無制限" : $"試合時間 {m_Settings.MatchSeconds} 秒")}\n" +
                  $"機体の速さ: {(m_Settings.Speed == SpeedPreset.Game ? "ゲーム" : "リアル")}";
        }

        public static string BestOfText(int bestOf) => bestOf switch { 3 => "準決勝形式 (2 本先取)", 5 => "決勝形式 (3 本先取)", _ => "予選形式 (1 ラウンド)" };
    }
}
