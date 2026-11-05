using System.Collections.Generic;
using CoreSim.Net;
using CoreSim.Rules;
using Side = CoreSim.Rules.Side;
using SkillKind = CoreSim.Rules.SkillKind;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoreSim
{
    /// <summary>
    /// 操縦画面の重ね描き。ROS 2 版の core_jp_camera_publisher が映像に焼き込んでいた
    /// レティクル・カウントダウン・全機の HP・俯瞰画像をそのまま uGUI にしたもの。
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        private static readonly System.Text.StringBuilder s_Sb = new System.Text.StringBuilder();

        /// <summary>CPU 全機の判断の中身を 1 行ずつ。</summary>
        private static string BotDebugText(GameDirector director)
        {
            s_Sb.Clear();
            s_Sb.AppendLine("[F9] CPU の状態");
            foreach (RobotAgent r in director.Robots)
            {
                if (r == null || !(r.Source is IBotStatus bot)) continue;
                Vector3 p = r.transform.position;
                s_Sb.AppendLine($"{r.DisplayName} {r.KindName} ({p.x:F1}, {p.z:F1}) {(r.IsKnockedOut ? "KO " : "")}{bot.Status}");
            }
            return s_Sb.ToString();
        }

        private Text m_Timer;
        private Text m_Phase;
        private Text m_Message;
        private Text m_OwnStatus;
        private Text m_BuilderHint;   // かんたんルールのビルダーの案内 (大会ルールでは TournamentHud が出す)
        private Text m_BotDebug;      // F9: CPU の判断の中身
        private Text m_Objective;     // 今の目標 (1 行)
        private Text m_HintLine;      // 初回だけのヒント (目標の帯の下)
        private Text m_ResultDetail;  // 結果画面の内訳と「次に試すこと」
        private float m_HintUntil;
        private bool m_WasKo, m_WasEmpty, m_BaseOpenShown;
        private GameSettings m_Settings;
        private static int s_GuideToggleFrame = -1;
        private float m_GuideNoticeUntil;
        private static bool s_SelectHeld, s_GuideKeyHeld;
        /// <summary>F9 で切り替える CPU のデバッグ表示 (全 HUD 共通)。</summary>
        public static bool ShowBotDebug;
        private static int s_ToggleFrame = -1;
        private RectTransform m_Reticle;
        private RectTransform m_CooldownFill;
        private RectTransform m_HpListBlue;   // 左上: 青 (偶数番)
        private RectTransform m_HpListRed;    // 右上: 赤 (奇数番)。大会の表示に合わせて左右に分ける
        private RectTransform m_Minimap;
        private readonly List<(RobotAgent agent, Text label, RectTransform fill, Text value)> m_HpRows =
            new List<(RobotAgent, Text, RectTransform, Text)>();
        private RobotAgent m_Self;
        private RectTransform m_Top;
        private RectTransform m_Own;
        public TournamentHud Tournament { get; private set; }

        public static HudView Create(Transform parent, Rect viewport, int playerIndex, Texture minimap, GameSettings settings)
        {
            RectTransform root = UiKit.Panel(parent, $"HUD_{playerIndex + 1}", new Color(0, 0, 0, 0),
                                             new Vector2(viewport.xMin, viewport.yMin), new Vector2(viewport.xMax, viewport.yMax),
                                             Vector2.zero, Vector2.zero);
            var hud = root.gameObject.AddComponent<HudView>();
            hud.m_Settings = settings;

            // 上中央: 残り時間
            RectTransform top = UiKit.Rect(root, "Top", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(520f, 140f));
            hud.m_Top = top;
            UiKit.Image(top, "bg", new Color(0f, 0f, 0f, 0.45f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            hud.m_Timer = UiKit.Label(top, "Timer", "120", 72, Color.white);
            hud.m_Timer.rectTransform.offsetMin = new Vector2(0f, 0f);
            hud.m_Timer.rectTransform.offsetMax = new Vector2(0f, -10f);
            hud.m_Phase = UiKit.Label(top, "Phase", "", 24, new Color(0.85f, 0.85f, 0.85f), TextAnchor.LowerCenter);
            hud.m_Phase.rectTransform.offsetMin = new Vector2(0f, 6f);

            // 中央: メッセージ (カウントダウン / 結果)
            RectTransform mid = UiKit.Rect(root, "Message", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(1200f, 200f));
            hud.m_Message = UiKit.Label(mid, "Text", "", 96, Color.white);

            // 下中央: ビルダーの案内 (かんたんルール)
            RectTransform hint = UiKit.Rect(root, "BuilderHint", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 200f), new Vector2(1200f, 50f));
            hud.m_BuilderHint = UiKit.Label(hint, "Text", "", 30, new Color(1f, 0.85f, 0.4f));

            // 上中央の下: 今の目標 (1 行。状況で変わる)
            RectTransform obj = UiKit.Rect(root, "Objective", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -192f), new Vector2(1400f, 40f));
            UiKit.Round(UiKit.Image(obj, "bg", new Color(0f, 0f, 0f, 0.4f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero));
            hud.m_Objective = UiKit.Label(obj, "Text", "", 18, new Color(1f, 0.92f, 0.6f));
            RectTransform hintRow = UiKit.Rect(root, "HintLine", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -232f), new Vector2(1400f, 34f));
            hud.m_HintLine = UiKit.Label(hintRow, "Text", "", 16, new Color(0.8f, 0.95f, 1f));
            hud.m_HintLine.horizontalOverflow = HorizontalWrapMode.Wrap;
            RectTransform detail = UiKit.Rect(root, "ResultDetail", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(1500f, 120f));
            hud.m_ResultDetail = UiKit.Label(detail, "Text", "", 20, new Color(0.9f, 0.95f, 1f));
            hud.m_ResultDetail.horizontalOverflow = HorizontalWrapMode.Wrap;

            // 左中段: CPU のデバッグ表示 (F9)
            RectTransform dbg = UiKit.Rect(root, "BotDebug", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(900f, 400f));
            dbg.pivot = new Vector2(0f, 0.5f);
            hud.m_BotDebug = UiKit.Label(dbg, "Text", "", 18, new Color(0.8f, 1f, 0.8f), TextAnchor.MiddleLeft);

            // レティクル
            hud.m_Reticle = UiKit.Rect(root, "Reticle", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60f, 60f));
            Color reticleColor = new Color(1f, 1f, 1f, 0.8f);
            UiKit.Image(hud.m_Reticle, "h", reticleColor, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -1f), new Vector2(0f, 1f));
            UiKit.Image(hud.m_Reticle, "v", reticleColor, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(-1f, 0f), new Vector2(1f, 0f));
            UiKit.Image(hud.m_Reticle, "dot", reticleColor, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-3f, -3f), new Vector2(3f, 3f));

            // 左上に青、右上に赤の機体一覧
            hud.m_HpListBlue = UiKit.Rect(root, "HpListBlue", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(200f, -40f), new Vector2(380f, 60f));
            hud.m_HpListBlue.pivot = new Vector2(0.5f, 1f);
            hud.m_HpListRed = UiKit.Rect(root, "HpListRed", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-200f, -40f), new Vector2(380f, 60f));
            hud.m_HpListRed.pivot = new Vector2(0.5f, 1f);

            // 左下: ミニマップ
            RectTransform map = UiKit.Rect(root, "Minimap", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(140f, 140f), new Vector2(236f, 236f));
            hud.m_Minimap = map;
            UiKit.Image(map, "frame", new Color(0f, 0f, 0f, 0.6f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var raw = new GameObject("Image", typeof(RectTransform)).AddComponent<RawImage>();
            raw.transform.SetParent(map, false);
            raw.rectTransform.anchorMin = Vector2.zero;
            raw.rectTransform.anchorMax = Vector2.one;
            raw.rectTransform.offsetMin = new Vector2(8f, 8f);
            raw.rectTransform.offsetMax = new Vector2(-8f, -8f);
            raw.texture = minimap;
            raw.raycastTarget = false;

            // 右下: 自機の状態と射撃クールダウン
            RectTransform own = UiKit.Rect(root, "Own", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-240f, 70f), new Vector2(440f, 100f));
            hud.m_Own = own;
            UiKit.Image(own, "bg", new Color(0f, 0f, 0f, 0.45f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            hud.m_OwnStatus = UiKit.Label(own, "Text", "", 26, Color.white, TextAnchor.UpperCenter);
            hud.m_OwnStatus.rectTransform.offsetMax = new Vector2(0f, -8f);
            RectTransform cd = UiKit.Rect(own, "Cooldown", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(400f, 10f));
            UiKit.Image(cd, "bg", new Color(1f, 1f, 1f, 0.2f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            hud.m_CooldownFill = UiKit.Image(cd, "fill", new Color(1f, 0.85f, 0.3f, 0.9f), Vector2.zero, new Vector2(1f, 1f), Vector2.zero, Vector2.zero).rectTransform;

            if (settings.ShowTouchControls && playerIndex == 0)
            {
                TouchControls.Create(root);
            }
            return hud;
        }

        /// <summary>俯瞰 (ミニマップ) の表示切替。ROS 2 版の V キー「俯瞰画像切替」に相当する。</summary>
        public void SetMinimapVisible(bool visible) => m_Minimap.gameObject.SetActive(visible);
        public bool MinimapVisible => m_Minimap.gameObject.activeSelf;

        /// <summary>大会ルールの表示を足す。簡易ルールの残り時間パネルは隠す。</summary>
        public void AttachTournament(Side side, System.Action<SkillKind> onSkill)
        {
            m_Top.gameObject.SetActive(false);
            m_Own.gameObject.SetActive(false);   // 残弾などは大会ルールのパネルが出す
            Tournament = TournamentHud.Create((RectTransform)transform, side, onSkill);
        }

        public void Bind(RobotAgent self, IReadOnlyList<RobotAgent> all)
        {
            m_Self = self;
            foreach (var row in m_HpRows)
            {
                if (row.label != null) Destroy(row.label.transform.parent.gameObject);
            }
            m_HpRows.Clear();
            float yBlue = 0f, yRed = 0f;
            foreach (RobotAgent agent in all)
            {
                bool red = agent.Team == Team.Red;
                RectTransform list = red ? m_HpListRed : m_HpListBlue;
                float y = red ? yRed : yBlue;
                if (red) yRed -= 44f; else yBlue -= 44f;
                RectTransform row = UiKit.Rect(list, agent.name, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, y - 20f), new Vector2(0f, 40f));
                UiKit.Image(row, "bg", new Color(0f, 0f, 0f, 0.45f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                UiKit.Image(row, "team", agent.TeamColor, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(4f, 4f), new Vector2(12f, -4f));
                string shown = RowName(agent);
                Text label = UiKit.Label(row, "name", shown, 22, agent == self ? new Color(1f, 0.95f, 0.5f) : Color.white, TextAnchor.MiddleLeft);
                label.rectTransform.offsetMin = new Vector2(20f, 0f);
                RectTransform bar = UiKit.Rect(row, "bar", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 7f), new Vector2(-24f, 8f));
                bar.anchoredPosition = new Vector2(6f, 7f);
                UiKit.Image(bar, "bg", new Color(1f, 1f, 1f, 0.15f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                RectTransform fill = UiKit.Image(bar, "fill", agent.TeamColor, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).rectTransform;
                Text value = UiKit.Label(row, "hp", "200", 22, Color.white, TextAnchor.MiddleRight);
                value.rectTransform.offsetMax = new Vector2(-8f, 0f);
                m_HpRows.Add((agent, label, fill, value));
            }
        }

        /// <summary>初めて起きた出来事にだけ出すヒント (撃破された、弾切れ、陣地攻撃の解禁)。</summary>
        private void UpdateHints(GameDirector director, bool guide)
        {
            if (m_HintLine == null) return;
            if (m_Self != null && director.Match.Phase == MatchPhase.Playing)
            {
                bool ko = m_Self.IsKnockedOut;
                if (ko && !m_WasKo) Hint(m_Self.IsHeadAttacker ? "撃破されました。ヘッドアタッカーは復活しません (ラウンドは続きます)" : "撃破されました。30 秒後に開始位置で復活します (開始 3 分以内なら)");
                m_WasKo = ko;
                if (director.Tournament != null && director.Tournament.View.Robots.TryGetValue(m_Self.Index, out RobotExtra me))
                {
                    bool empty = me.Magazine == 0 && !me.IsBuilder && !me.IsAuto;
                    if (empty && !m_WasEmpty) Hint("残弾が 0 になりました。1 キーでピットインを発動し、自同盟の補給エリアへ入ると装填できます (20 秒停止)");
                    m_WasEmpty = empty;
                    bool baseOpen = director.Tournament.View.BaseAttackActive(director.Tournament.Config);
                    if (baseOpen && !m_BaseOpenShown && !me.IsBuilder) Hint("開始 3 分がたち、相手の陣地を攻撃できます。陣地の低い位置の板を撃つと HP が減り、撃破で +100VP");
                    if (baseOpen) m_BaseOpenShown = true;
                }
            }
            m_HintLine.text = guide && Time.time < m_HintUntil ? m_HintLine.text : "";
        }

        private void Hint(string text)
        {
            m_HintLine.text = "ヒント: " + text;
            m_HintUntil = Time.time + 6f;
        }

        /// <summary>今の目標。役割と状況から 1 行で。初めての人が「何をすれば得点か」を見失わないため。</summary>
        private string ObjectiveText(GameDirector director)
        {
            if (m_Self == null) return "";
            if (m_Self.IsKnockedOut) return "撃破されました。復活を待つ";
            if (m_Self.Frozen) return "補給中 (完全停止)";
            TournamentController tc = director.Tournament;
            if (tc == null)
            {
                return m_Self.IsBuilder ? "目標: コンテナやブロックを運ぶ (かんたんルールでは得点なし)"
                     : m_Self.IsAuto ? "目標: プログラムが自動で戦う。見守る"
                     : "目標: 相手ロボットの装甲板 (4 枚) にディスクを当てる。横や後ろが当てやすい";
            }
            TournamentView v = tc.View;
            RobotExtra me = v.Robots.TryGetValue(m_Self.Index, out RobotExtra x) ? x : default;
            if (m_Self.IsBuilder)
            {
                AllianceState mine = v.Of(TournamentController.SideOf(m_Self.Team));
                if ((mine.Sites & 1) == 0) return "目標: コンテナを自同盟のスポットへ (+10VP)。ブロックをビルドサイト 1 に 2 個積むと自分の防御が 2 倍";
                return "目標: コンテナをスポットへ (1 段に 4 個で +50VP)。サイト 3・4 を取ると置き場 B・C も使える";
            }
            if (m_Self.IsAuto) return me.AutoStarted ? "目標: プログラムが自動で戦っている。スキル (1〜9) で支援" : "まもなく自動で始動";
            if (me.Magazine == 0) return me.PitInGranted ? "残弾なし: 自同盟の補給エリアへ入る (20 秒停止で装填)" : "残弾なし: 1 キーでピットインを発動して補給エリアへ";
            bool baseOpen = v.BaseAttackActive(tc.Config);
            Side enemy = TournamentRound.Opposite(TournamentController.SideOf(m_Self.Team));
            if (baseOpen && !v.Of(enemy).BaseDestroyed) return "目標: 相手ロボットを撃つ (+10VP)。相手陣地の低い板も撃てる (撃破で +100VP)";
            float until = tc.Config.BaseImmuneSeconds - v.Time;
            return until > 0f ? $"目標: 相手ロボットの装甲板を撃つ (+10VP)。{TournamentController.FormatTime(until)} 後から相手陣地も撃てる"
                              : "目標: 相手ロボットの装甲板を撃つ (+10VP)";
        }

        private static string RowName(RobotAgent agent) => (agent.IsHeadAttacker ? "★ " : agent.IsBuilder ? "[B] " : agent.IsAuto ? "[A] " : "") + agent.DisplayName;

        public void Refresh(GameDirector director)
        {
            MatchState match = director.Match;
            if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame && s_ToggleFrame != Time.frameCount)
            {
                s_ToggleFrame = Time.frameCount;
                ShowBotDebug = !ShowBotDebug;
            }
            if (m_BotDebug != null)
            {
                m_BotDebug.text = ShowBotDebug ? BotDebugText(director) : "";
            }
            BotDebugLines.Refresh(director.Robots, ShowBotDebug);
            // ガイドの ON / OFF (H / パッドの Select)。経験者は消して遊べる
            bool selectNow = Gamepad.current != null && InputBindings.Current.PadHeld(InputAct.Guide, Gamepad.current);
            bool keyNow = InputBindings.Current.KeyHeld(InputAct.Guide, Keyboard.current);
            bool toggle = (keyNow && !s_GuideKeyHeld) || (selectNow && !s_SelectHeld);
            s_GuideKeyHeld = keyNow;
            s_SelectHeld = selectNow;
            if (toggle && m_Settings != null && s_GuideToggleFrame != Time.frameCount)
            {
                s_GuideToggleFrame = Time.frameCount;
                m_Settings.ShowGuide = !m_Settings.ShowGuide;
                m_Settings.Save();
                m_GuideNoticeUntil = Time.time + 2f;
            }
            bool guide = m_Settings == null || m_Settings.ShowGuide;
            UpdateHints(director, guide);
            if (m_ResultDetail != null)
            {
                m_ResultDetail.text = match.Phase == MatchPhase.Result && guide ? director.ResultDetails(m_Self) : "";
            }
            if (m_Objective != null)
            {
                m_Objective.transform.parent.gameObject.SetActive(guide || Time.time < m_GuideNoticeUntil);
                m_Objective.text = Time.time < m_GuideNoticeUntil ? (guide ? "ガイド ON (H / Select で OFF)" : "ガイド OFF (H / Select で ON)")
                                 : match.Phase == MatchPhase.Playing ? ObjectiveText(director) : "";
            }
            if (Tournament != null && director.Tournament != null)
            {
                var src = m_Self != null && director.Tournament != null ? director.PlayerInputFor(m_Self) : null;
                Tournament.Refresh(director.Tournament.View, m_Self, src != null ? src.SelectedSkill : -1, m_Self != null && director.CanCommand(m_Self));
                if (match.Phase == MatchPhase.Countdown)
                {
                    m_Message.text = match.RemainingCeil <= 3 ? match.RemainingCeil.ToString() : $"ラウンド {director.Tournament.View.RoundNumber}  READY";
                }
                else if (match.Phase == MatchPhase.Playing)
                {
                    m_Message.text = director.GoFlashUntil > Time.time ? "GO!" : "";
                }
                else
                {
                    m_Message.text = director.ResultText(m_Self);
                }
            }
            else
            switch (match.Phase)
            {
                case MatchPhase.Countdown:
                    m_Timer.text = match.RemainingCeil.ToString();
                    m_Phase.text = "試合開始まで";
                    m_Message.text = match.RemainingCeil <= 3 ? match.RemainingCeil.ToString() : "READY";
                    break;
                case MatchPhase.Playing:
                    m_Timer.text = match.Unlimited ? "∞" : match.RemainingCeil.ToString();
                    m_Phase.text = match.Unlimited ? "練習中" : "残り時間";
                    m_Message.text = director.GoFlashUntil > Time.time ? "GO!" : "";
                    break;
                case MatchPhase.Result:
                case MatchPhase.Idle:
                    m_Timer.text = "0";
                    m_Phase.text = "試合終了";
                    m_Message.text = director.ResultText(m_Self);
                    break;
            }

            foreach (var row in m_HpRows)
            {
                float ratio = row.agent.Health.Ratio;
                row.fill.anchorMax = new Vector2(ratio, 1f);
                row.value.text = row.agent.Health.Hp.ToString();
                string name = RowName(row.agent);
                row.label.text = row.agent.IsKnockedOut ? name + " (撃破)" : name;
            }

            if (m_Self != null)
            {
                bool alive = !m_Self.IsKnockedOut;
                m_Reticle.gameObject.SetActive(alive && match.Phase == MatchPhase.Playing);
                string turbo = m_Self.IsTurbo ? "  TURBO" : "";
                if (!m_Self.Remote && m_Self.Drive != null && m_Self.Drive.Righting) turbo += "  転倒 → 復帰中";
                float speed = m_Self.Remote ? 0f : (m_Self.Drive != null ? m_Self.Drive.Speed : 0f);
                m_OwnStatus.text = alive
                    ? (m_Self.Remote ? $"HP {m_Self.Health.Hp}{turbo}" : $"HP {m_Self.Health.Hp}   {speed:F1} m/s{turbo}")
                    : "KO";
                if (m_BuilderHint != null)
                {
                    m_BuilderHint.text = alive && m_Self.IsBuilder && m_Self.Lift != null && Tournament == null && match.Phase == MatchPhase.Playing
                        ? m_Self.Lift.Hint("Space / R1") : "";
                }
                float cd = !m_Self.Remote && m_Self.Shooter != null && m_Self.Shooter.Interval > 0f
                    ? 1f - m_Self.Shooter.Cooldown / m_Self.Shooter.Interval : 1f;
                m_CooldownFill.anchorMax = new Vector2(Mathf.Clamp01(cd), 1f);
            }
        }
    }
}
