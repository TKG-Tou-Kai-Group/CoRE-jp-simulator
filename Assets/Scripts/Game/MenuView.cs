using UnityEngine;
using UnityEngine.UI;

namespace CoreSim
{
    /// <summary>
    /// タイトル画面。「何をしてみる?」のカード 3 枚 (撃ち合う / 大会の試合に出る / 4 本脚を歩かせる) で選ぶ。
    /// ルールや遊び方の名前ではなく体験で選ばせる (docs/UX_PLAN.md)。細かい数値は「くわしい設定」へ。
    /// </summary>
    public sealed class MenuView : MonoBehaviour
    {
        public static MenuView Create(Transform parent, GameSettings settings, GameDirector director)
        {
            RectTransform root = UiKit.Panel(parent, "Menu", new Color(0.05f, 0.07f, 0.1f, 0.55f),
                                             Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var menu = root.gameObject.AddComponent<MenuView>();

            RectTransform title = UiKit.Rect(root, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(1400f, 110f));
            UiKit.Label(title, "Main", "CoRE Simulator", 72, Color.white);
            RectTransform sub = UiKit.Rect(root, "Sub", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1400f, 40f));
            UiKit.Label(sub, "Text", "ロボット競技会 CoRE のフィールドで遊ぶ   何をしてみる?", 24, new Color(0.8f, 0.85f, 0.95f));

            // カード 3 枚
            RectTransform cards = UiKit.Rect(root, "Cards", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(1420f, 560f));
            var layout = cards.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 30f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childAlignment = TextAnchor.MiddleCenter;

            // 1. 撃ち合う (かんたんルール)
            RectTransform c1 = Card(cards, "撃ち合う", "Tutorial/mode_shoot", new Color(0.5f, 0.25f, 0.2f, 0.9f),
                                    "ディスクを撃ち合うだけの短い試合。操作を覚えるのに向いている (HP 200、2 分)");
            RectTransform grid = UiKit.Rect(c1, "Grid", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 100f), new Vector2(420f, 170f));
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(205f, 50f);
            gl.spacing = new Vector2(10f, 10f);
            gl.childAlignment = TextAnchor.MiddleCenter;
            Button first = UiKit.Button(grid, "まず操作の手ほどき", Vector2.zero, () => director.StartTutorial(TutorialKind.Attacker), new Color(0.2f, 0.45f, 0.35f, 0.95f), 17);
            UiKit.Button(grid, "ひとりで (vs CPU)", Vector2.zero, () => director.StartCasual(GameMode.SoloVsCpu), new Color(0.85f, 0.35f, 0.2f, 0.95f), 17);
            UiKit.Button(grid, "チーム戦 (2 vs 2)", Vector2.zero, () => director.StartCasual(GameMode.TeamVsCpu), new Color(0.7f, 0.3f, 0.2f, 0.95f), 17);
            UiKit.Button(grid, "ふたりで (画面分割)", Vector2.zero, () => director.StartCasual(GameMode.LocalVersus), new Color(0.7f, 0.3f, 0.2f, 0.95f), 17);
            UiKit.Button(grid, "練習 (的を撃つ)", Vector2.zero, () => director.StartCasual(GameMode.Practice), new Color(0.7f, 0.3f, 0.2f, 0.95f), 17);

            // 2. 大会の試合に出る
            RectTransform c2 = Card(cards, "大会の試合に出る", "Tutorial/mode_tournament", new Color(0.2f, 0.3f, 0.5f, 0.9f),
                                    "CoRE-1 と同じルール。アタッカー・ビルダー・オートアタッカーから自分の機体を選ぶ (7 分)");
            BigButton(c2, "機体を選んで試合へ", () => director.OpenRoleSelect());

            // 3. 4 本脚を歩かせる
            RectTransform c3 = Card(cards, "4 本脚を歩かせる", "Tutorial/mode_strider", new Color(0.45f, 0.35f, 0.15f, 0.9f),
                                    "ストライダーチャレンジ。Q W O P で脚を動かし、障害物のコースを歩いて踏破するタイムアタック");
            BigButton(c3, "歩かせてみる", () => director.StartStrider());

            // 下段: そのほか
            RectTransform bottom = UiKit.Rect(root, "Bottom", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 95f), new Vector2(1500f, 64f));
            var bl = bottom.gameObject.AddComponent<HorizontalLayoutGroup>();
            bl.spacing = 16f;
            bl.childControlWidth = false;
            bl.childControlHeight = false;
            bl.childForceExpandWidth = false;
            bl.childAlignment = TextAnchor.MiddleCenter;
            UiKit.Button(bottom, "ネット対戦 (LAN / 閉域網)", new Vector2(340f, 58f), () => { settings.Save(); director.OpenNetworkMenu(); }, new Color(0.2f, 0.5f, 0.85f, 0.95f), 19);
            UiKit.Button(bottom, "あそびかた (はじめに)", new Vector2(300f, 58f), () => director.OpenIntro(), new Color(0.3f, 0.4f, 0.5f, 0.95f), 19);
            UiKit.Button(bottom, "CoRE について", new Vector2(220f, 58f), () => director.OpenAboutCore(), new Color(0.3f, 0.4f, 0.5f, 0.95f), 19);
            UiKit.Button(bottom, "くわしい設定", new Vector2(220f, 58f), () => { settings.Save(); director.OpenSettings(); }, new Color(0.25f, 0.35f, 0.4f, 0.95f), 19);
            if (!Application.isMobilePlatform)
            {
                UiKit.Button(bottom, "終了", new Vector2(140f, 58f), () => director.QuitGame(), new Color(0.25f, 0.25f, 0.3f, 0.9f), 19);
            }

            RectTransform foot = UiKit.Rect(root, "Foot", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(1600f, 40f));
            UiKit.Label(foot, "Text", "Unity_ROS2_Robot_Simulator / CoRE-jp-Unity-ROS2-packages をもとにした単体ゲーム版  v0.5.0", 15, new Color(0.7f, 0.7f, 0.75f));
            UiKit.Select(first);
            return menu;
        }

        private static RectTransform Card(RectTransform parent, string name, string pic, Color fallback, string desc)
        {
            RectTransform card = UiKit.Rect(parent, "Card_" + name, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(450f, 560f));
            UiKit.Image(card, "bg", new Color(0.1f, 0.12f, 0.16f, 0.95f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            RectTransform p = UiKit.Rect(card, "Pic", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(420f, 240f));
            OnboardingView.Picture(p, pic, fallback, $"{name}の絵 ({pic.Substring(9)}.png)");
            // 文字は 1.25 倍で描かれる (UiKit.FontScale)。名前 30 → 38 px、説明 17 → 21 px で 3 行まで
            UiKit.Label(UiKit.Rect(card, "Name", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -274f), new Vector2(430f, 46f)), "Text", name, 30, Color.white);
            Text d = UiKit.Label(UiKit.Rect(card, "Desc", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -336f), new Vector2(416f, 72f)), "Text", desc, 16, new Color(0.9f, 0.92f, 0.95f), TextAnchor.UpperCenter, false);
            d.horizontalOverflow = HorizontalWrapMode.Wrap;
            d.verticalOverflow = VerticalWrapMode.Truncate;
            return card;
        }

        private static Button BigButton(RectTransform card, string label, System.Action onClick)
        {
            Button b = UiKit.Button(card, label, new Vector2(400f, 64f), onClick, new Color(0.85f, 0.35f, 0.2f, 0.95f), 24);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 60f);
            return b;
        }

        public static string RoleName(PlayerRole role) => role switch { PlayerRole.Builder => "ビルダー", PlayerRole.Attacker => "アタッカー", PlayerRole.AutoAttacker => "オートアタッカー", _ => "ヘッドアタッカー" };
        public static string RoleText(PlayerRole role) => role switch
        {
            PlayerRole.Builder => "自分: ビルダー (HP 80、射出なし。ブロックとコンテナを運ぶ)",
            PlayerRole.Attacker => "自分: アタッカー (HP 60、撃破から 30 秒で復活)",
            PlayerRole.AutoAttacker => "自分: オートアタッカー (HP 200、プログラムで自律。人は始動・停止・スキル・観戦)",
            _ => "自分: ヘッドアタッカー (HP 300、毎秒 5 回復、復活なし)",
        };
        public static string BestOfText(int bestOf) => bestOf switch { 3 => "準決勝形式 (2 本先取)", 5 => "決勝形式 (3 本先取)", _ => "予選形式 (1 ラウンド)" };
    }
}
