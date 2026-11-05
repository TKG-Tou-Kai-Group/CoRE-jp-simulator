using System.Collections.Generic;
using CoreSim.Net;
using CoreSim.Rules;
using UnityEngine;
using UnityEngine.UI;

namespace CoreSim
{
    /// <summary>大会ルールの表示: VP・残り時間・RP・陣地 HP・残弾・スキル・出来事。</summary>
    public sealed class TournamentHud : MonoBehaviour
    {
        private Text m_RedVp, m_BlueVp, m_Clock, m_Round, m_Rp, m_Ammo, m_Status, m_Log, m_Center, m_BaseRed, m_BaseBlue;
        private RectTransform m_BaseRedFill, m_BaseBlueFill;
        private readonly List<Button> m_SkillButtons = new List<Button>();
        private readonly List<Text> m_SkillLabels = new List<Text>();
        private readonly List<Image> m_SkillImages = new List<Image>();

        private Side m_Side;
        private System.Action<SkillKind> m_OnSkill;

        public static TournamentHud Create(RectTransform root, Side side, System.Action<SkillKind> onSkill)
        {
            var hud = root.gameObject.AddComponent<TournamentHud>();
            hud.m_Side = side;
            hud.m_OnSkill = onSkill;

            // 上段: 青 VP | 時計 | 赤 VP (大会の表示と同じく左が青、右が赤)
            RectTransform top = UiKit.Rect(root, "TTop", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(900f, 110f));
            UiKit.Image(top, "bg", new Color(0f, 0f, 0f, 0.5f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            UiKit.Image(top, "bluebar", new Color(0.15f, 0.35f, 0.85f, 0.9f), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(10f, 0f));
            UiKit.Image(top, "redbar", new Color(0.85f, 0.15f, 0.2f, 0.9f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-10f, 0f), new Vector2(0f, 0f));
            hud.m_BlueVp = UiKit.Label(top, "BlueVp", "0", 60, new Color(0.6f, 0.75f, 1f), TextAnchor.MiddleLeft);
            hud.m_BlueVp.rectTransform.offsetMin = new Vector2(30f, 0f);
            hud.m_RedVp = UiKit.Label(top, "RedVp", "0", 60, new Color(1f, 0.55f, 0.6f), TextAnchor.MiddleRight);
            hud.m_RedVp.rectTransform.offsetMax = new Vector2(-30f, 0f);
            hud.m_Clock = UiKit.Label(top, "Clock", "0:00", 56, Color.white);
            hud.m_Clock.rectTransform.offsetMax = new Vector2(0f, -8f);
            hud.m_Round = UiKit.Label(top, "Round", "", 20, new Color(0.85f, 0.85f, 0.85f), TextAnchor.LowerCenter);
            hud.m_Round.rectTransform.offsetMin = new Vector2(0f, 4f);

            // 陣地 HP (上段の下)
            RectTransform bases = UiKit.Rect(root, "Bases", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -135f), new Vector2(900f, 34f));
            hud.m_BaseBlueFill = Bar(bases, new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, 4f), new Vector2(-6f, -4f), new Color(0.2f, 0.4f, 0.9f, 0.9f));
            hud.m_BaseRedFill = Bar(bases, new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(6f, 4f), new Vector2(0f, -4f), new Color(0.85f, 0.2f, 0.25f, 0.9f));
            hud.m_BaseBlue = UiKit.Label(bases, "BaseBlue", "", 20, Color.white, TextAnchor.MiddleLeft);
            hud.m_BaseBlue.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            hud.m_BaseBlue.rectTransform.offsetMin = new Vector2(12f, 0f);
            hud.m_BaseRed = UiKit.Label(bases, "BaseRed", "", 20, Color.white, TextAnchor.MiddleRight);
            hud.m_BaseRed.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            hud.m_BaseRed.rectTransform.offsetMax = new Vector2(-12f, 0f);

            // 右下: RP・残弾・状態。右端から 20 px 内側に収め、3 行に分ける (1 行に詰めると見切れる)。
            RectTransform own = UiKit.Rect(root, "TOwn", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-350f, 265f), new Vector2(660f, 190f));
            UiKit.Image(own, "bg", new Color(0f, 0f, 0f, 0.5f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            hud.m_Ammo = UiKit.Label(own, "Ammo", "", 28, Color.white, TextAnchor.UpperLeft);
            hud.m_Ammo.rectTransform.offsetMin = new Vector2(16f, 0f);
            hud.m_Ammo.rectTransform.offsetMax = new Vector2(-16f, -12f);
            hud.m_Rp = UiKit.Label(own, "Rp", "", 26, new Color(1f, 0.9f, 0.5f), TextAnchor.MiddleLeft);
            hud.m_Rp.rectTransform.offsetMin = new Vector2(16f, 0f);
            hud.m_Rp.rectTransform.offsetMax = new Vector2(-16f, 0f);
            hud.m_Status = UiKit.Label(own, "Status", "", 22, new Color(0.7f, 0.95f, 1f), TextAnchor.LowerLeft);
            hud.m_Status.rectTransform.offsetMin = new Vector2(16f, 12f);
            hud.m_Status.rectTransform.offsetMax = new Vector2(-16f, 0f);

            // 下中央: スキル (総大将端末)
            RectTransform skills = UiKit.Rect(root, "Skills", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(1460f, 96f));
            UiKit.Image(skills, "bg", new Color(0f, 0f, 0f, 0.45f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var layout = skills.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 8f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childAlignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < TournamentConfig.Skills.Length; i++)
            {
                SkillSpec spec = TournamentConfig.Skills[i];
                SkillKind kind = spec.Kind;
                string name = spec.Kind == SkillKind.PitIn ? "ピットイン (自機)" : spec.Name;
                Button b = UiKit.Button(skills, $"{i + 1}  {name}\n{spec.Cost} RP", new Vector2(152f, 80f), () => onSkill?.Invoke(kind),
                                        new Color(0.2f, 0.25f, 0.35f, 0.95f), 19);
                hud.m_SkillButtons.Add(b);
                hud.m_SkillLabels.Add(b.GetComponentInChildren<Text>());
                hud.m_SkillImages.Add(b.GetComponent<Image>());
            }

            RectTransform hint = UiKit.Rect(root, "SkillHint", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 122f), new Vector2(1460f, 30f));
            UiKit.Label(hint, "Text", "スキル: キー 1〜9 / パッド 十字← → で選んで X(□) で発動 / 画面のボタン", 18, new Color(0.85f, 0.85f, 0.85f));

            // 左中: 出来事
            RectTransform log = UiKit.Rect(root, "Log", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(300f, -120f), new Vector2(560f, 150f));
            hud.m_Log = UiKit.Label(log, "Text", "", 20, new Color(1f, 1f, 1f, 0.9f), TextAnchor.LowerLeft);

            // 中央下: 撃破中の案内
            RectTransform center = UiKit.Rect(root, "TCenter", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(1000f, 80f));
            hud.m_Center = UiKit.Label(center, "Text", "", 32, new Color(1f, 0.85f, 0.4f));
            return hud;
        }

        private static RectTransform Bar(RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, Color color)
        {
            RectTransform bg = UiKit.Panel(parent, "bar", new Color(1f, 1f, 1f, 0.15f), aMin, aMax, oMin, oMax);
            return UiKit.Image(bg, "fill", color, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).rectTransform;
        }

        public void Refresh(TournamentView view, RobotAgent self, int selectedSkill, bool canCommand)
        {
            TournamentConfig cfg = TournamentConfig.Default;
            m_RedVp.text = view.Red.Vp.ToString();
            m_BlueVp.text = view.Blue.Vp.ToString();
            float remain = Mathf.Max(0f, cfg.RoundSeconds - view.Time);
            m_Clock.text = TournamentController.FormatTime(remain);
            string baseState = view.Time < cfg.BaseImmuneSeconds ? $"陣地攻撃まで {TournamentController.FormatTime(cfg.BaseImmuneSeconds - view.Time)}" : "陣地攻撃 有効";
            m_Round.text = $"ラウンド {view.RoundNumber}  (青 {view.Blue.Wins} - {view.Red.Wins} 赤)   {baseState}";

            m_BaseBlueFill.anchorMax = new Vector2(Mathf.Clamp01(view.Blue.BaseHp / (float)cfg.BaseHp), 1f);
            m_BaseRedFill.anchorMin = new Vector2(1f - Mathf.Clamp01(view.Red.BaseHp / (float)cfg.BaseHp), 0f);
            m_BaseRed.text = view.Red.BaseDestroyed ? "赤陣地 撃破" : $"赤陣地 HP {view.Red.BaseHp}{Effects(view.Red)}";
            m_BaseBlue.text = view.Blue.BaseDestroyed ? "青陣地 撃破" : $"青陣地 HP {view.Blue.BaseHp}{Effects(view.Blue)}";

            AllianceState mine = view.Of(m_Side);
            m_Rp.text = $"RP {mine.Rp}   ディスク {mine.Discs} 枚   ピットイン {mine.PitInCredits} 台   サイト {SitesText(mine.Sites)}   コンテナ {mine.Containers}";
            if (self != null && view.Robots.TryGetValue(self.Index, out RobotExtra me))
            {
                bool builder = me.IsBuilder || self.IsBuilder;
                bool auto = me.IsAuto || self.IsAuto;
                if (builder)
                {
                    string held = self.Lift != null && self.Lift.Held != null ? self.Lift.Held.Label : "なし";
                    float h = self.Lift != null ? self.Lift.Height : 0f;
                    m_Ammo.text = $"ビルダー  HP {me.Hp}/{me.MaxHp}   ハンド {h:F2} m   持ち物: {held}";
                }
                else if (auto)
                {
                    string state = me.AutoStopped ? "停止" : me.AutoStarted ? "稼働中" : "始動待ち";
                    m_Ammo.text = $"オートアタッカー ({state})  HP {me.Hp}/{me.MaxHp}   ミニディスク {me.Magazine} 枚";
                }
                else
                {
                    m_Ammo.text = $"{(me.IsHead ? "ヘッド" : "アタッカー")}  HP {me.Hp}/{me.MaxHp}   残弾 {me.Magazine} 枚";
                }
                if (me.Destroyed)
                {
                    m_Center.text = me.ReviveIn >= 0f ? $"撃破されました。復活まで {Mathf.CeilToInt(me.ReviveIn)} 秒" : "撃破されました (このラウンドは復活なし)";
                }
                else if (me.FrozenFor > 0f)
                {
                    m_Center.text = $"補給中 (完全停止) あと {Mathf.CeilToInt(me.FrozenFor)} 秒";
                }
                else if (builder)
                {
                    m_Center.text = self.Lift != null ? self.Lift.Hint("Space / R1") : "";
                }
                else if (auto)
                {
                    m_Center.text = me.AutoStopped ? "停止しました (このラウンドは再始動できません)"
                        : !me.AutoStarted ? "オートアタッカーはまもなく自動で始動します"
                        : me.Magazine == 0 ? "ミニディスクなし (ラウンド中の補給はできません)" : "";
                }
                else if (me.Magazine == 0)
                {
                    m_Center.text = me.PitInGranted ? "残弾なし。自同盟の補給エリアへ (20 秒停止)" : "残弾なし。ピットイン (1) を発動してから補給エリアへ";
                }
                else
                {
                    m_Center.text = "";
                }
                string status = "";
                if (me.InvincibleFor > 0f) status += $"無敵 {me.InvincibleFor:F0}s  ";
                if (mine.BoostFor > 0f) status += $"ブースト {mine.BoostFor:F0}s  ";
                if (mine.BarrierLevel > 0) status += $"バリア Lv.{mine.BarrierLevel} {mine.BarrierFor:F0}s  ";
                if (mine.SkillLockFor > 0f) status += $"スキル待ち {mine.SkillLockFor:F0}s";
                m_Status.text = status;
            }

            bool timedFree = mine.BoostFor <= 0f && mine.BarrierLevel == 0 && mine.SkillLockFor <= 0f;
            for (int i = 0; i < m_SkillButtons.Count; i++)
            {
                SkillSpec spec = TournamentConfig.Skills[i];
                bool exempt = spec.Kind == SkillKind.PitIn || spec.Kind == SkillKind.Supply;
                bool ok = canCommand && !view.Finished && mine.Rp >= spec.Cost && (exempt || timedFree);
                m_SkillButtons[i].interactable = ok;
                m_SkillImages[i].color = i == selectedSkill ? new Color(0.9f, 0.6f, 0.2f, 0.95f)
                    : ok ? new Color(0.2f, 0.3f, 0.45f, 0.95f) : new Color(0.15f, 0.15f, 0.18f, 0.8f);
                m_SkillLabels[i].color = ok ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            }
            m_Log.text = string.Join("\n", view.Log);
        }

        private static string SitesText(byte mask)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < TournamentConfig.SiteCount; i++) sb.Append((mask & (1 << i)) != 0 ? "■" : "□");
            return sb.ToString();
        }

        private static string Effects(AllianceState a)
        {
            string s = "";
            if (a.BarrierLevel > 0) s += $"  バリア Lv.{a.BarrierLevel}";
            if (a.BoostFor > 0f) s += "  ブースト";
            return s;
        }
    }
}
