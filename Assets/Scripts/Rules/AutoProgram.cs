using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CoreSim.Rules
{
    /// <summary>オートアタッカーのプログラムの条件 (1 行に複数あれば AND)。</summary>
    public enum AutoCondition : byte
    {
        Always = 0,
        EnemyWithin = 1,     // いちばん近い敵が N m 以内
        NoEnemyWithin = 2,   // N m 以内に敵がいない
        HpBelow = 3,         // 自機 HP が N % 未満
        AmmoBelow = 4,       // 残弾が N 枚未満
        TimeAfter = 5,       // ラウンド開始から N 秒以上
        TimeBefore = 6,      // ラウンド開始から N 秒未満
        BaseOpen = 7,        // 相手の陣地を攻撃できる (3 分経過、未撃破)
        AllyWithin = 8,      // いちばん近い味方が N m 以内
    }

    /// <summary>オートアタッカーの行動。</summary>
    public enum AutoAction : byte
    {
        Engage = 0,       // 相手に近づいて撃つ (間合い N m を保つ)
        AttackBase = 1,   // 相手の陣地を攻撃 (できない時間は Engage)
        GoTo = 2,         // 地点へ移動 (撃たない。着いたら止まる。撃つなら上の行に「敵が近い」の条件で交戦や待機を書く)
        Hold = 3,         // その場で待機し、射程内の相手を撃つ
        Retreat = 4,      // 自同盟のスタートゾーンへ戻る
    }

    /// <summary>GoTo の行き先 (フィールドの名前付きの点)。</summary>
    public enum AutoPoint : byte
    {
        Center = 0,      // 中央
        OwnFront = 1,    // 自陣地の前
        EnemyFront = 2,  // 相手陣地の前
        Start = 3,       // 自同盟のスタートゾーン
        LeftLane = 4,    // 自分から見て左の通路 (相手のブロックゾーン側、中央より向こう)
        RightLane = 5,   // 右の通路 (自分のブロックゾーン側)
        EnemyStart = 6,  // 相手同盟のスタートゾーンの前
    }

    /// <summary>狙う相手の選び方。</summary>
    public enum AutoTarget : byte
    {
        Nearest = 0,
        Weakest = 1,     // HP が最も少ない
        Head = 2,        // ヘッドアタッカー優先
        Builder = 3,     // ビルダー優先
        Attacker = 4,    // ビルダー以外を優先
    }

    public struct AutoClause
    {
        public AutoCondition Kind;
        public float Value;
    }

    /// <summary>プログラムの 1 行: 条件 (AND) が成り立てば行動。上の行ほど優先。</summary>
    public sealed class AutoRule
    {
        public readonly List<AutoClause> Conditions = new List<AutoClause>();
        public AutoAction Action = AutoAction.Engage;
        public float Param = 5f;             // Engage: 間合い [m]。GoTo: 地点 (AutoPoint の番号)
        public AutoTarget Target = AutoTarget.Nearest;

        public AutoPoint Point => (AutoPoint)Math.Max(0, Math.Min((int)Param, (int)AutoPoint.EnemyStart));
        public const int PointCount = (int)AutoPoint.EnemyStart + 1;

        public AutoRule Clone()
        {
            var r = new AutoRule { Action = Action, Param = Param, Target = Target };
            r.Conditions.AddRange(Conditions);
            return r;
        }
    }

    /// <summary>オートアタッカーが毎回の判断で見る値。ゲーム側が埋める。</summary>
    public struct AutoSensors
    {
        public float EnemyDistance;   // いちばん近い敵まで [m]。いなければ float.MaxValue
        public float AllyDistance;    // いちばん近い味方まで [m]。いなければ float.MaxValue
        public float HpPercent;       // 0..100
        public int Ammo;
        public float Time;            // ラウンド開始からの秒
        public bool BaseOpen;         // 相手の陣地を攻撃できる
    }

    /// <summary>
    /// オートアタッカーのプログラム (12.7: 人は始動と停止だけ)。カルネージハートや Gladiabots と同じく
    /// 「条件 → 行動」の行を上から順に評価し、最初に成り立った行の行動をとる。どの行も成り立たなければ
    /// 最後の既定 (Engage)。テキスト 1 行 = 1 ルール (<see cref="Format"/> / <see cref="Parse"/>)。
    /// </summary>
    public sealed class AutoProgram
    {
        public const int MaxRules = 12;
        public const int MaxConditions = 3;
        public const int MaxTextLength = 2000;

        public readonly List<AutoRule> Rules = new List<AutoRule>();

        /// <summary>既定のプログラム: 弾切れなら下がり、HP が減ったら距離を取り、陣地が攻撃できれば攻め、ふだんは最寄りの敵。</summary>
        public static AutoProgram Default()
        {
            return Parse(string.Join("\n", new[]
            {
                "ammo<1 > retreat",
                "hp<30 > engage=8 @nearest",
                "base & enemy>6 > base",
                "enemy<12 > engage=5 @nearest",
                "always > goto=center",
            }));
        }

        /// <summary>上から順に評価して、最初に成り立つ行 (無ければ null)。</summary>
        public AutoRule Select(in AutoSensors s)
        {
            foreach (AutoRule r in Rules)
            {
                if (Matches(r, s)) return r;
            }
            return null;
        }

        public int IndexOf(AutoRule r) => Rules.IndexOf(r);

        public static bool Matches(AutoRule r, in AutoSensors s)
        {
            foreach (AutoClause c in r.Conditions)
            {
                switch (c.Kind)
                {
                    case AutoCondition.Always: break;
                    case AutoCondition.EnemyWithin: if (!(s.EnemyDistance <= c.Value)) return false; break;
                    case AutoCondition.NoEnemyWithin: if (s.EnemyDistance <= c.Value) return false; break;
                    case AutoCondition.HpBelow: if (!(s.HpPercent < c.Value)) return false; break;
                    case AutoCondition.AmmoBelow: if (!(s.Ammo < c.Value)) return false; break;
                    case AutoCondition.TimeAfter: if (!(s.Time >= c.Value)) return false; break;
                    case AutoCondition.TimeBefore: if (!(s.Time < c.Value)) return false; break;
                    case AutoCondition.BaseOpen: if (!s.BaseOpen) return false; break;
                    case AutoCondition.AllyWithin: if (!(s.AllyDistance <= c.Value)) return false; break;
                }
            }
            return true;
        }

        // ------------------------------------------------------------------ テキスト形式
        // 1 行 = 1 ルール:  条件[&条件...] > 行動[=値] [@狙い]
        //   条件: always | enemy<N | enemy>N | hp<N | ammo<N | time>N | time<N | base | ally<N
        //   行動: engage[=間合い] | base | goto=center|ownfront|enemyfront|start|left|right|enemystart | hold | retreat
        //   狙い: @nearest | @weakest | @head | @builder | @attacker

        private static readonly (AutoCondition kind, string name, bool hasValue, char op)[] s_Conditions =
        {
            (AutoCondition.Always, "always", false, ' '),
            (AutoCondition.EnemyWithin, "enemy", true, '<'),
            (AutoCondition.NoEnemyWithin, "enemy", true, '>'),
            (AutoCondition.HpBelow, "hp", true, '<'),
            (AutoCondition.AmmoBelow, "ammo", true, '<'),
            (AutoCondition.TimeAfter, "time", true, '>'),
            (AutoCondition.TimeBefore, "time", true, '<'),
            (AutoCondition.BaseOpen, "base", false, ' '),
            (AutoCondition.AllyWithin, "ally", true, '<'),
        };
        private static readonly string[] s_Actions = { "engage", "base", "goto", "hold", "retreat" };
        private static readonly string[] s_Points = { "center", "ownfront", "enemyfront", "start", "left", "right", "enemystart" };
        private static readonly string[] s_Targets = { "nearest", "weakest", "head", "builder", "attacker" };

        public static string Format(AutoProgram p)
        {
            var sb = new StringBuilder();
            foreach (AutoRule r in p.Rules)
            {
                sb.Append(FormatRule(r)).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        public static string FormatRule(AutoRule r)
        {
            var sb = new StringBuilder();
            if (r.Conditions.Count == 0) sb.Append("always");
            for (int i = 0; i < r.Conditions.Count; i++)
            {
                if (i > 0) sb.Append(" & ");
                AutoClause c = r.Conditions[i];
                foreach (var d in s_Conditions)
                {
                    if (d.kind != c.Kind) continue;
                    sb.Append(d.name);
                    if (d.hasValue) sb.Append(d.op).Append(c.Value.ToString("0.##", CultureInfo.InvariantCulture));
                    break;
                }
            }
            sb.Append(" > ").Append(s_Actions[(int)r.Action]);
            if (r.Action == AutoAction.Engage) sb.Append('=').Append(r.Param.ToString("0.##", CultureInfo.InvariantCulture));
            else if (r.Action == AutoAction.GoTo) sb.Append('=').Append(s_Points[(int)r.Point]);
            if (r.Action == AutoAction.Engage || r.Action == AutoAction.Hold)
            {
                sb.Append(" @").Append(s_Targets[(int)r.Target]);
            }
            return sb.ToString();
        }

        /// <summary>読めない行は飛ばす (ネット越しの文字列でも例外を出さない)。</summary>
        public static AutoProgram Parse(string text)
        {
            var p = new AutoProgram();
            if (string.IsNullOrEmpty(text)) return p;
            if (text.Length > MaxTextLength) text = text.Substring(0, MaxTextLength);
            foreach (string raw in text.Split('\n'))
            {
                if (p.Rules.Count >= MaxRules) break;
                AutoRule r = ParseRule(raw);
                if (r != null) p.Rules.Add(r);
            }
            return p;
        }

        public static AutoRule ParseRule(string line)
        {
            if (line == null) return null;
            string s = line.Trim();
            int hash = s.IndexOf('#');
            if (hash >= 0) s = s.Substring(0, hash).Trim();
            if (s.Length == 0) return null;
            int arrow = s.IndexOf('>');
            // 条件側にも '>' (enemy>N, time>N) があるので、" > " (前後に空白) を区切りとする
            arrow = s.IndexOf(" > ", StringComparison.Ordinal);
            if (arrow < 0) return null;
            string condPart = s.Substring(0, arrow).Trim();
            string actPart = s.Substring(arrow + 3).Trim();
            var r = new AutoRule();
            foreach (string c in condPart.Split('&'))
            {
                string t = c.Trim().ToLowerInvariant();
                if (t.Length == 0) continue;
                if (!ParseClause(t, out AutoClause clause)) return null;
                if (clause.Kind == AutoCondition.Always) continue;
                if (r.Conditions.Count >= MaxConditions) break;
                r.Conditions.Add(clause);
            }
            string[] parts = actPart.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;
            string action = parts[0].ToLowerInvariant();
            string value = null;
            int eq = action.IndexOf('=');
            if (eq >= 0) { value = action.Substring(eq + 1); action = action.Substring(0, eq); }
            int ai = Array.IndexOf(s_Actions, action);
            if (ai < 0) return null;
            r.Action = (AutoAction)ai;
            if (r.Action == AutoAction.Engage)
            {
                r.Param = 5f;
                if (value != null && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float range)) r.Param = Clamp(range, 1f, 15f);
            }
            else if (r.Action == AutoAction.GoTo)
            {
                int pi = value != null ? Array.IndexOf(s_Points, value) : -1;
                r.Param = pi < 0 ? 0f : pi;
            }
            else r.Param = 0f;
            for (int i = 1; i < parts.Length; i++)
            {
                string t = parts[i].ToLowerInvariant();
                if (t.StartsWith("@"))
                {
                    int ti = Array.IndexOf(s_Targets, t.Substring(1));
                    if (ti >= 0) r.Target = (AutoTarget)ti;
                }
            }
            return r;
        }

        private static bool ParseClause(string t, out AutoClause clause)
        {
            clause = default;
            foreach (var d in s_Conditions)
            {
                if (!d.hasValue)
                {
                    if (t == d.name) { clause.Kind = d.kind; return true; }
                    continue;
                }
                if (t.StartsWith(d.name + d.op))
                {
                    string v = t.Substring(d.name.Length + 1);
                    if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) return false;
                    clause.Kind = d.kind;
                    clause.Value = Clamp(value, 0f, 1000f);
                    return true;
                }
            }
            return false;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        // ------------------------------------------------------------------ 表示用の名前

        public static string ConditionName(AutoCondition c) => c switch
        {
            AutoCondition.EnemyWithin => "敵が N m 以内",
            AutoCondition.NoEnemyWithin => "N m 以内に敵なし",
            AutoCondition.HpBelow => "HP が N % 未満",
            AutoCondition.AmmoBelow => "残弾が N 枚未満",
            AutoCondition.TimeAfter => "開始から N 秒以上",
            AutoCondition.TimeBefore => "開始から N 秒未満",
            AutoCondition.BaseOpen => "相手陣地を攻撃できる",
            AutoCondition.AllyWithin => "味方が N m 以内",
            _ => "いつでも",
        };

        public static string ActionName(AutoAction a) => a switch
        {
            AutoAction.AttackBase => "相手陣地を攻撃",
            AutoAction.GoTo => "地点へ移動",
            AutoAction.Hold => "その場で待機して撃つ",
            AutoAction.Retreat => "スタートゾーンへ戻る",
            _ => "敵に近づいて撃つ (間合い N m)",
        };

        public static string PointName(AutoPoint p) => p switch
        {
            AutoPoint.OwnFront => "自陣地の前",
            AutoPoint.EnemyFront => "相手陣地の前",
            AutoPoint.Start => "スタートゾーン",
            AutoPoint.LeftLane => "左の通路 (相手ブロックゾーン側)",
            AutoPoint.RightLane => "右の通路 (自ブロックゾーン側)",
            AutoPoint.EnemyStart => "相手のスタートゾーンの前",
            _ => "中央",
        };

        public static string TargetName(AutoTarget t) => t switch
        {
            AutoTarget.Weakest => "HP が少ない敵",
            AutoTarget.Head => "ヘッドアタッカー優先",
            AutoTarget.Builder => "ビルダー優先",
            AutoTarget.Attacker => "ビルダー以外を優先",
            _ => "いちばん近い敵",
        };

        public static bool HasValue(AutoCondition c) => c != AutoCondition.Always && c != AutoCondition.BaseOpen;
    }
}
