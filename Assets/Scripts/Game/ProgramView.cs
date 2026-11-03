using System.Collections.Generic;
using CoreSim.Rules;
using UnityEngine;
using UnityEngine.UI;

namespace CoreSim
{
    /// <summary>
    /// オートアタッカーのプログラムの編集画面。行 = 「条件 (最大 3 つ、AND) → 行動 [値] [狙い]」で、上の行ほど優先。
    /// ドロップダウンは使わず、ボタンを押すたびに候補を巡回する (タブレットでも作れる)。
    /// 変更のたびにテキスト形式 (<see cref="AutoProgram.Format"/>) で設定へ保存する。
    /// </summary>
    public sealed class ProgramView : MonoBehaviour
    {
        private GameSettings m_Settings;
        private AutoProgram m_Program;
        private RectTransform m_Content;
        private Text m_Preview;

        private static readonly float[] s_Enemy = { 3f, 5f, 8f, 12f, 15f };
        private static readonly float[] s_Hp = { 20f, 30f, 50f, 70f };
        private static readonly float[] s_Ammo = { 1f, 10f, 30f, 50f, 100f };
        private static readonly float[] s_Time = { 30f, 60f, 120f, 180f, 240f, 300f };
        private static readonly float[] s_Ally = { 2f, 3f, 5f, 8f };
        private static readonly float[] s_Range = { 3f, 5f, 8f, 12f };

        public static ProgramView Create(Transform parent, GameSettings settings, System.Action onBack)
        {
            RectTransform root = UiKit.Panel(parent, "Program", new Color(0.05f, 0.07f, 0.1f, 0.92f),
                                             Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var view = root.gameObject.AddComponent<ProgramView>();
            view.m_Settings = settings;
            view.m_Program = string.IsNullOrWhiteSpace(settings.AutoProgramText) ? AutoProgram.Default() : AutoProgram.Parse(settings.AutoProgramText);
            if (view.m_Program.Rules.Count == 0) view.m_Program = AutoProgram.Default();

            RectTransform title = UiKit.Rect(root, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(1500f, 70f));
            UiKit.Label(title, "Text", "オートアタッカーのプログラム", 48, Color.white);
            RectTransform help = UiKit.Rect(root, "Help", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(1500f, 60f));
            UiKit.Label(help, "Text", "上の行から順に条件を調べ、最初に成り立った行の行動をとります (どれも成り立たなければ最寄りの敵と交戦)。\n" +
                                      "ボタンを押すと候補が切り替わります。試合中に人がすることは無く、ラウンド開始 2 秒後に自動で始動します。", 22, new Color(0.85f, 0.9f, 1f));

            view.m_Content = UiKit.ScrollView(root, "Body", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(1560f, 620f),
                                              new Color(0f, 0f, 0f, 0.55f), new RectOffset(20, 20, 16, 16), 8f, controlChildSize: false);

            RectTransform foot = UiKit.Rect(root, "Foot", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(1560f, 70f));
            var layout = foot.gameObject.AddComponent<HorizontalLayoutGroup>();
            Row(layout, 12f);
            UiKit.Button(foot, "行を追加", new Vector2(240f, 60f), () => { if (view.m_Program.Rules.Count < AutoProgram.MaxRules) { view.m_Program.Rules.Add(AutoProgram.ParseRule("always > engage=5 @nearest")); view.Changed(); } }, new Color(0.2f, 0.45f, 0.3f, 0.95f), 26);
            UiKit.Button(foot, "既定に戻す", new Vector2(240f, 60f), () => { view.m_Program = AutoProgram.Default(); view.Changed(); }, new Color(0.35f, 0.3f, 0.2f, 0.95f), 26);
            UiKit.Button(foot, "戻る", new Vector2(240f, 60f), () => { view.Save(); onBack?.Invoke(); }, new Color(0.25f, 0.35f, 0.5f, 0.95f), 26);
            RectTransform prev = UiKit.Rect(root, "Preview", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(1560f, 40f));
            view.m_Preview = UiKit.Label(prev, "Text", "", 18, new Color(0.7f, 0.7f, 0.75f));
            view.Rebuild();
            return view;
        }

        private static void Row(HorizontalLayoutGroup layout, float spacing)
        {
            layout.spacing = spacing;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
        }

        private void Changed()
        {
            Save();
            Rebuild();
        }

        private void Save()
        {
            m_Settings.AutoProgramText = AutoProgram.Format(m_Program);
            m_Settings.Save();
        }

        private void Rebuild()
        {
            for (int i = m_Content.childCount - 1; i >= 0; i--) Destroy(m_Content.GetChild(i).gameObject);
            for (int i = 0; i < m_Program.Rules.Count; i++) BuildRow(i);
            m_Preview.text = AutoProgram.Format(m_Program).Replace("\n", "   |   ");
        }

        private void BuildRow(int index)
        {
            AutoRule rule = m_Program.Rules[index];
            RectTransform row = UiKit.Rect(m_Content, $"Rule{index}", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(1500f, 58f));
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            Row(layout, 6f);
            Text num = UiKit.Label(UiKit.Rect(row, "Num", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(40f, 50f)), "Text", $"{index + 1}.", 24, Color.white);
            // 条件
            if (rule.Conditions.Count == 0)
            {
                UiKit.Button(row, "いつでも", new Vector2(150f, 50f), () => { rule.Conditions.Add(new AutoClause { Kind = AutoCondition.EnemyWithin, Value = 8f }); Changed(); }, new Color(0.25f, 0.3f, 0.4f, 0.95f), 20);
            }
            for (int c = 0; c < rule.Conditions.Count; c++)
            {
                int ci = c;
                AutoClause clause = rule.Conditions[c];
                if (c > 0) UiKit.Label(UiKit.Rect(row, "And", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(40f, 50f)), "Text", "かつ", 18, new Color(0.8f, 0.8f, 0.8f));
                UiKit.Button(row, AutoProgram.ConditionName(clause.Kind), new Vector2(230f, 50f), () =>
                {
                    AutoClause cl = rule.Conditions[ci];
                    cl.Kind = (AutoCondition)(((int)cl.Kind % 8) + 1);   // Always は飛ばす (1..8 を巡回)
                    cl.Value = Presets(cl.Kind)[0];
                    rule.Conditions[ci] = cl;
                    Changed();
                }, new Color(0.25f, 0.3f, 0.4f, 0.95f), 18);
                if (AutoProgram.HasValue(clause.Kind))
                {
                    UiKit.Button(row, Unit(clause.Kind, clause.Value), new Vector2(90f, 50f), () =>
                    {
                        AutoClause cl = rule.Conditions[ci];
                        cl.Value = Next(Presets(cl.Kind), cl.Value);
                        rule.Conditions[ci] = cl;
                        Changed();
                    }, new Color(0.3f, 0.35f, 0.3f, 0.95f), 20);
                }
            }
            if (rule.Conditions.Count > 0 && rule.Conditions.Count < AutoProgram.MaxConditions)
            {
                UiKit.Button(row, "+条件", new Vector2(70f, 50f), () => { rule.Conditions.Add(new AutoClause { Kind = AutoCondition.HpBelow, Value = 30f }); Changed(); }, new Color(0.2f, 0.3f, 0.25f, 0.95f), 18);
            }
            if (rule.Conditions.Count > 0)
            {
                UiKit.Button(row, "−条件", new Vector2(70f, 50f), () => { rule.Conditions.RemoveAt(rule.Conditions.Count - 1); Changed(); }, new Color(0.3f, 0.25f, 0.2f, 0.95f), 18);
            }
            UiKit.Label(UiKit.Rect(row, "Arrow", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(40f, 50f)), "Text", "→", 26, Color.white);
            // 行動
            UiKit.Button(row, AutoProgram.ActionName(rule.Action), new Vector2(250f, 50f), () =>
            {
                rule.Action = (AutoAction)(((int)rule.Action + 1) % 5);
                rule.Param = rule.Action == AutoAction.Engage ? 5f : 0f;
                Changed();
            }, new Color(0.4f, 0.3f, 0.2f, 0.95f), 18);
            if (rule.Action == AutoAction.Engage)
            {
                UiKit.Button(row, $"{rule.Param:0.#} m", new Vector2(80f, 50f), () => { rule.Param = Next(s_Range, rule.Param); Changed(); }, new Color(0.3f, 0.35f, 0.3f, 0.95f), 20);
            }
            else if (rule.Action == AutoAction.GoTo)
            {
                string pointLabel = rule.Custom ? $"地図 ({rule.X:0.0}, {rule.Z:0.0})" : AutoProgram.PointName(rule.Point);
                UiKit.Button(row, pointLabel, new Vector2(200f, 50f), () =>
                {
                    // 名前付きの地点を巡回する (地図で選んだ点からは「中央」へ戻る)
                    rule.Param = rule.Custom ? 0f : ((int)rule.Point + 1) % 7;
                    rule.Custom = false;
                    Changed();
                }, new Color(0.3f, 0.35f, 0.3f, 0.95f), 18);
                UiKit.Button(row, "地図", new Vector2(70f, 50f), () => OpenMap(rule), new Color(0.2f, 0.35f, 0.45f, 0.95f), 18);
            }
            if (rule.Action == AutoAction.Engage || rule.Action == AutoAction.Hold || rule.Action == AutoAction.GoTo)
            {
                UiKit.Button(row, AutoProgram.TargetName(rule.Target), new Vector2(190f, 50f), () => { rule.Target = (AutoTarget)(((int)rule.Target + 1) % 5); Changed(); }, new Color(0.3f, 0.25f, 0.4f, 0.95f), 18);
            }
            // 並べ替え・削除
            UiKit.Button(row, "↑", new Vector2(44f, 50f), () => { if (index > 0) { Swap(index, index - 1); Changed(); } }, null, 22);
            UiKit.Button(row, "↓", new Vector2(44f, 50f), () => { if (index < m_Program.Rules.Count - 1) { Swap(index, index + 1); Changed(); } }, null, 22);
            UiKit.Button(row, "×", new Vector2(44f, 50f), () => { m_Program.Rules.RemoveAt(index); Changed(); }, new Color(0.5f, 0.2f, 0.2f, 0.95f), 22);
        }

        private MapPickerView m_Map;

        private void OpenMap(AutoRule rule)
        {
            if (m_Map != null) return;
            m_Map = MapPickerView.Create(transform.parent, rule.Custom, rule.X, rule.Z, (x, z) =>
            {
                rule.Custom = true;
                rule.X = x;
                rule.Z = z;
                CloseMap();
                Changed();
            }, CloseMap);
        }

        private void CloseMap()
        {
            if (m_Map != null) Destroy(m_Map.gameObject);
            m_Map = null;
        }

        private void OnDestroy() => CloseMap();

        private void Swap(int a, int b)
        {
            AutoRule t = m_Program.Rules[a];
            m_Program.Rules[a] = m_Program.Rules[b];
            m_Program.Rules[b] = t;
        }

        private static float[] Presets(AutoCondition kind) => kind switch
        {
            AutoCondition.EnemyWithin => s_Enemy,
            AutoCondition.NoEnemyWithin => s_Enemy,
            AutoCondition.HpBelow => s_Hp,
            AutoCondition.AmmoBelow => s_Ammo,
            AutoCondition.TimeAfter => s_Time,
            AutoCondition.TimeBefore => s_Time,
            AutoCondition.AllyWithin => s_Ally,
            _ => s_Enemy,
        };

        private static string Unit(AutoCondition kind, float v) => kind switch
        {
            AutoCondition.HpBelow => $"{v:0.#} %",
            AutoCondition.AmmoBelow => $"{v:0.#} 枚",
            AutoCondition.TimeAfter => $"{v:0.#} 秒",
            AutoCondition.TimeBefore => $"{v:0.#} 秒",
            _ => $"{v:0.#} m",
        };

        private static float Next(float[] presets, float current)
        {
            int best = 0;
            for (int i = 0; i < presets.Length; i++) if (Mathf.Abs(presets[i] - current) < Mathf.Abs(presets[best] - current)) best = i;
            return presets[(best + 1) % presets.Length];
        }
    }
}
