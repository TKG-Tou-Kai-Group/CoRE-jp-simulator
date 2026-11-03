using UnityEngine;
using UnityEngine.UI;

namespace CoreSim
{
    /// <summary>設定画面。共通 / かんたんルール / 大会ルール の 3 つの束にして縦に並べる (スクロール可)。</summary>
    public sealed class SettingsView : MonoBehaviour
    {
        private GameSettings m_Settings;
        private readonly System.Collections.Generic.List<(Button button, System.Func<string> label)> m_Rows =
            new System.Collections.Generic.List<(Button, System.Func<string>)>();

        public static SettingsView Create(Transform parent, GameSettings settings, System.Action onBack, System.Action onEditProgram = null)
        {
            RectTransform root = UiKit.Panel(parent, "Settings", new Color(0.05f, 0.07f, 0.1f, 0.7f),
                                             Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var view = root.gameObject.AddComponent<SettingsView>();
            view.m_Settings = settings;

            RectTransform title = UiKit.Rect(root, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(1400f, 90f));
            UiKit.Label(title, "Text", "設定", 64, Color.white);

            RectTransform content = UiKit.ScrollView(root, "Body", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(1500f, 760f),
                                                     new Color(0f, 0f, 0f, 0.55f), new RectOffset(30, 30, 20, 20), 10f, controlChildSize: false);
            Vector2 size = new Vector2(1420f, 64f);
            GameSettings s = settings;

            view.Section(content, "共通");
            view.Row(content, size, () => s.Speed == SpeedPreset.Game ? "機体の速さ: ゲーム (実機の 2 倍、加速 4 倍)" : "機体の速さ: リアル (実機相当 0.5 m/s、ターボ 1.0 m/s)",
                     () => s.Speed = s.Speed == SpeedPreset.Game ? SpeedPreset.Real : SpeedPreset.Game);
            view.Row(content, size, () => s.Sound ? "効果音: ON" : "効果音: OFF", () => s.Sound = !s.Sound);
            view.Row(content, size, () => s.ShowTouchControls ? "タッチ操作パッド: 表示 (タブレット向け)" : "タッチ操作パッド: 非表示", () => s.ShowTouchControls = !s.ShowTouchControls);

            view.Section(content, "かんたんルール (HP 200、自動復活なし)");
            view.Row(content, size, () => $"ひとりで対戦のときの CPU の数: {s.CpuCount} 台", () => s.CpuCount = s.CpuCount % 3 + 1);
            view.Row(content, size, () => $"試合時間: {s.MatchSeconds} 秒", () => s.MatchSeconds = s.MatchSeconds switch { 60 => 120, 120 => 180, _ => 60 });

            view.Section(content, "大会ルール (CoRE-1 ルールブック V27.1.3、アタッカー・ビルダー・オートアタッカー)");
            view.Row(content, size, () => $"各同盟の機体: {s.RobotsPerSide} 台 (うちビルダー {s.EffectiveBuilders} 台。自分以外は CPU)", () => s.RobotsPerSide = s.RobotsPerSide % 4 + 1);
            view.Row(content, size, () => $"各同盟のビルダー: {s.EffectiveBuilders} 台 (アタッカーは 1 台以上残す)", () => s.BuildersPerSide = (s.BuildersPerSide + 1) % 3);
            view.Row(content, size, () => $"各同盟のオートアタッカー: {s.AutosPerSide} 台 (上の台数とは別枠。1 台まで。HP 200、ミニディスク 200 枚、完全自律)", () => s.AutosPerSide = (s.AutosPerSide + 1) % 2);
            view.Row(content, size, () => "オートアタッカーのプログラムを編集 ▶ (自分の機体と、自分がホストのときの CPU 以外)", () => onEditProgram?.Invoke());
            view.Row(content, size, () => $"試合: {MenuView.BestOfText(s.BestOf)}", () => s.BestOf = s.BestOf switch { 1 => 3, 3 => 5, _ => 1 });
            view.Row(content, size, () => MenuView.RoleText(s.Role), () => s.Role = (PlayerRole)(((int)s.Role + 1) % 4));
            view.Row(content, size, () => s.PlayerIsRed ? "自分の同盟: 赤 (奇数番)" : "自分の同盟: 青 (偶数番)", () => s.PlayerIsRed = !s.PlayerIsRed);
            view.Row(content, size, () => $"弾倉: {s.Magazine} 枚 (同盟のディスク 400 枚から装填。ルールに無い値)",
                     () => s.Magazine = s.Magazine switch { 30 => 50, 50 => 80, 80 => 120, _ => 30 });
            view.Row(content, size, () => GameSettings.FireText(s.FireInterval), () => s.FireInterval = GameSettings.NextFireInterval(s.FireInterval));
            view.Text(content, "変えられない値 (ルールブックの規定): ラウンド 7 分、VP 500 で終了、命中 -10HP、陣地 HP 200 (開始 3 分間は無効)、\n" +
                               "撃破 +10VP、陣地撃破 +100VP +100RP、RP 初期 100 (5 分で +100)、スキルの消費 RP と効果時間、射出 15 m/s、\n" +
                               "ピットイン 20 秒停止、反則 -50VP");

            view.Section(content, "ネット対戦");
            view.Text(content, $"名前 {s.PlayerName} / ポート {s.NetPort} / {(s.NetUseTcp ? "TCP" : "UDP")} (ネット対戦の画面で変えられます)");

            RectTransform foot = UiKit.Rect(root, "Foot", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(400f, 70f));
            UiKit.Button(foot, "戻る", new Vector2(400f, 70f), () => onBack?.Invoke(), new Color(0.85f, 0.35f, 0.2f, 0.95f), 32);

            view.RefreshAll();
            return view;
        }

        private void Section(RectTransform parent, string title)
        {
            RectTransform row = UiKit.Rect(parent, "Section", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(1420f, 52f));
            UiKit.Label(row, "Text", title, 30, new Color(1f, 0.9f, 0.6f), TextAnchor.LowerLeft);
        }

        private void Text(RectTransform parent, string text)
        {
            int lines = text.Split('\n').Length;
            RectTransform row = UiKit.Rect(parent, "Note", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(1420f, 34f * lines + 8f));
            Text t = UiKit.Label(row, "Text", text, 22, new Color(0.8f, 0.85f, 0.9f), TextAnchor.UpperLeft, false);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private void Row(RectTransform parent, Vector2 size, System.Func<string> label, System.Action onClick)
        {
            Button b = null;
            b = UiKit.Button(parent, "", size, () => { onClick(); RefreshAll(); }, fontSize: 26);
            m_Rows.Add((b, label));
        }

        private void RefreshAll()
        {
            foreach (var (button, label) in m_Rows)
            {
                UiKit.SetButtonLabel(button, label());
            }
        }
    }
}
