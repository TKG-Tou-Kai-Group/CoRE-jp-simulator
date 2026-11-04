using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoreSim
{
    /// <summary>試合前の 1 枚: あなたの機体、目標、操作。初めての機体なら 6 秒、2 回目からは 2.5 秒で消える。何か押せば閉じる。</summary>
    public sealed class BriefingView : MonoBehaviour
    {
        private float m_CloseAt;
        private bool m_Armed;

        public static BriefingView Show(Transform parent, GameSettings settings, RobotAgent self, bool tournament, int matchSeconds)
        {
            if (settings != null && !settings.ShowGuide) return null;   // ガイド OFF (経験者)
            string role = self == null ? "アタッカー" : self.KindName;
            string key = "brief_" + (self == null ? "a" : self.Kind.ToString()) + (tournament ? "_t" : "_c");
            bool first = PlayerPrefs.GetInt(key, 0) == 0;
            PlayerPrefs.SetInt(key, 1);
            string side = self == null ? "" : self.Team == Rules.Team.Red ? "赤同盟の" : "青同盟の";
            string goal, keys;
            if (self != null && self.IsBuilder)
            {
                goal = "コンテナを自同盟のスポットへ運ぶと +10VP。ブロックをビルドサイトに 2 個積むと同盟が強くなる。\n相手の置き場やスポットに触ると反則 (-50VP)。画面下の案内を見れば避けられる";
                keys = "移動: 矢印 / 左スティック   旋回: A D / 右スティック   ハンド: R F / 十字キー上下   つかむ / 放す: Space / RB";
            }
            else if (self != null && self.IsAuto)
            {
                goal = "あなたのプログラムが自動で戦う。2 秒後に始動。見守りながらスキル (1〜9) を使える";
                keys = "視点: 自機のカメラ   地図: V   スキル: 1〜9 / 十字← → と X";
            }
            else if (tournament)
            {
                goal = (self != null && self.IsHeadAttacker ? "HP 300 で自動回復するが、撃破されると復活しない。無理をしない。\n" : "") +
                       "相手ロボットの装甲板にディスクを当てると HP が 10 減り、撃破で +10VP。3 分後からは相手の陣地の低い板も撃てる (+100VP)。\n弾が尽きたら 1 キーでピットインを発動して自同盟の補給エリアへ";
                keys = "移動: 矢印 / 左スティック   旋回: A D / 右スティック   発射: Space / RB   ターボ: Shift / LB   地図: V / Y";
            }
            else
            {
                goal = $"相手ロボットの装甲板 (4 枚) にディスクを当てると HP が 10 減る (初期 200)。{matchSeconds} 秒で HP が多い方の勝ち。\n正面より横や後ろの板が当てやすい";
                keys = "移動: 矢印 / 左スティック   旋回: A D / 右スティック   発射: Space / RB   ターボ: Shift / LB   地図: V / Y";
            }

            RectTransform root = UiKit.Panel(parent, "Briefing", new Color(0f, 0f, 0f, 0.55f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                             new Vector2(-760f, -200f), new Vector2(760f, 200f));
            var view = root.gameObject.AddComponent<BriefingView>();
            view.m_CloseAt = Time.time + (first ? 6f : 2.5f);
            UiKit.Label(UiKit.Rect(root, "Role", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(1480f, 70f)), "Text",
                        $"あなたは {side}{role}", 44, new Color(1f, 0.85f, 0.4f));
            Text g = UiKit.Label(UiKit.Rect(root, "Goal", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(1440f, 150f)), "Text", goal, 26, Color.white);
            g.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.Label(UiKit.Rect(root, "Keys", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(1480f, 40f)), "Text", keys, 20, new Color(0.85f, 0.9f, 1f));
            UiKit.Label(UiKit.Rect(root, "Skip", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(1480f, 30f)), "Text",
                        "何かキーを押すか画面に触れると閉じます   (ガイドは H / パッドの Select でいつでも ON / OFF)", 18, new Color(0.7f, 0.7f, 0.75f));
            return view;
        }

        private void Update()
        {
            // 開いた直後の押しっぱなしで閉じないように 1 フレーム待つ
            if (!m_Armed) { m_Armed = true; return; }
            bool any = Time.time >= m_CloseAt;
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) any = true;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) any = true;
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) any = true;
            if (Gamepad.current != null && (Gamepad.current.buttonSouth.wasPressedThisFrame || Gamepad.current.startButton.wasPressedThisFrame ||
                                            Gamepad.current.rightShoulder.wasPressedThisFrame)) any = true;
            if (any) Destroy(gameObject);
        }
    }
}
