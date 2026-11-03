using System.Collections.Generic;
using CoreSim.Rules;
using UnityEngine;

namespace CoreSim
{
    /// <summary>
    /// オートアタッカー (14.1 / 12.7): 人は始動と停止だけで、動きはプログラム (<see cref="AutoProgram"/>) が決める。
    /// 0.2 秒ごとにセンサー値 (敵・味方までの距離、HP %、残弾、時間、陣地攻撃の可否) を埋めて行を選び、
    /// 移動・照準・発射は <see cref="BotCommandSource"/> に指示として渡す。
    /// </summary>
    public sealed class AutoAttackerSource : ICommandSource, IBotStatus
    {
        public RobotAgent Self;
        public System.Func<IReadOnlyList<RobotAgent>> Robots;
        public TournamentController Tournament;
        public AutoProgram Program = AutoProgram.Default();
        /// <summary>人の操縦手 (始動・停止・スキルの入力用。機体の操縦には使わない)。</summary>
        public PlayerCommandSource Operator;

        private BotCommandSource m_Bot;
        private float m_NextThink;
        private float m_NextLogTime;
        private AutoRule m_Rule;
        private int m_RuleIndex = -1;
        private AutoSensors m_Sensors;
        private RobotCommand m_Current;

        private BotCommandSource Bot
        {
            get
            {
                if (m_Bot == null)
                {
                    m_Bot = new BotCommandSource { Self = Self, Robots = Robots, Tournament = Tournament, Skill = 0.7f, IsAutoProgram = true };
                }
                return m_Bot;
            }
        }

        public bool Active => Self != null && (Tournament == null || Self.AutoStarted);

        public string Status
        {
            get
            {
                if (!Active) return "始動待ち";
                if (Self.IsKnockedOut) return "撃破中 (復活待ち)";
                string rule = m_Rule != null ? $"行 {m_RuleIndex + 1}: {AutoProgram.FormatRule(m_Rule)}" : "既定 (交戦)";
                return $"{rule}  [敵 {Fmt(m_Sensors.EnemyDistance)} 味方 {Fmt(m_Sensors.AllyDistance)} HP {m_Sensors.HpPercent:F0}% 弾 {m_Sensors.Ammo} 時間 {m_Sensors.Time:F0}s]  {Bot.Status}";
            }
        }

        private static string Fmt(float d) => d >= 1000f ? "-" : $"{d:F1}m";

        public void DebugPath(List<Vector3> points)
        {
            if (m_Bot != null) m_Bot.DebugPath(points);
            else points.Clear();
        }

        public RobotCommand Read()
        {
            if (Self == null) return RobotCommand.None;
            if (!Active)
            {
                m_Current = RobotCommand.None;
                BotLog.Maybe(Self, this, ref m_NextLogTime);
                return m_Current;
            }
            if (Self.IsKnockedOut)
            {
                // 撃破中は判断しない (復活したら続きから)
                m_Current = RobotCommand.None;
                BotLog.Maybe(Self, this, ref m_NextLogTime);
                return m_Current;
            }
            if (Time.time >= m_NextThink)
            {
                m_NextThink = Time.time + 0.2f;
                Decide();
            }
            m_Current = Bot.Read();
            BotLog.Maybe(Self, this, ref m_NextLogTime);
            return m_Current;
        }

        private void Decide()
        {
            m_Sensors = Sense();
            m_Rule = Program != null ? Program.Select(m_Sensors) : null;
            m_RuleIndex = m_Rule != null && Program != null ? Program.IndexOf(m_Rule) : -1;
            BotCommandSource bot = Bot;
            if (m_Rule == null)
            {
                bot.Directive = BotCommandSource.BotDirective.Engage;
                bot.DirectiveRange = 5f;
                bot.TargetPolicy = AutoTarget.Nearest;
                return;
            }
            bot.TargetPolicy = m_Rule.Target;
            switch (m_Rule.Action)
            {
                case AutoAction.AttackBase:
                    bot.Directive = BotCommandSource.BotDirective.AttackBase;
                    break;
                case AutoAction.GoTo:
                    bot.Directive = BotCommandSource.BotDirective.GoTo;
                    bot.DirectivePoint = PointFor(m_Rule.Point);
                    break;
                case AutoAction.Hold:
                    bot.Directive = BotCommandSource.BotDirective.Hold;
                    break;
                case AutoAction.Retreat:
                    bot.Directive = BotCommandSource.BotDirective.Retreat;
                    break;
                default:
                    bot.Directive = BotCommandSource.BotDirective.Engage;
                    bot.DirectiveRange = Mathf.Clamp(m_Rule.Param, 1f, 15f);
                    break;
            }
        }

        private AutoSensors Sense()
        {
            var s = new AutoSensors { EnemyDistance = float.MaxValue, AllyDistance = float.MaxValue, HpPercent = 100f, Ammo = 999, Time = 0f, BaseOpen = false };
            IReadOnlyList<RobotAgent> robots = Robots != null ? Robots() : null;
            Vector3 p = Self.transform.position;
            bool teams = Tournament != null || (GameDirector.Instance != null && GameDirector.Instance.IsTeamMatch);
            if (robots != null)
            {
                foreach (RobotAgent r in robots)
                {
                    if (r == null || r == Self || r.IsKnockedOut) continue;
                    float d = Vector3.Distance(r.transform.position, p);
                    bool ally = teams && r.Team == Self.Team;
                    if (ally) s.AllyDistance = Mathf.Min(s.AllyDistance, d);
                    else s.EnemyDistance = Mathf.Min(s.EnemyDistance, d);
                }
            }
            if (Self.Health != null && Self.Health.MaxHp > 0) s.HpPercent = 100f * Self.Health.Hp / Self.Health.MaxHp;
            if (Tournament != null)
            {
                TournamentView view = Tournament.View;
                s.Time = view.Time;
                if (view.Robots.TryGetValue(Self.Index, out RobotExtra me)) s.Ammo = me.Magazine;
                Side enemy = TournamentRound.Opposite(TournamentController.SideOf(Self.Team));
                s.BaseOpen = view.BaseAttackActive(Tournament.Config) && !view.Of(enemy).BaseDestroyed;
            }
            else if (GameDirector.Instance != null && GameDirector.Instance.Match != null)
            {
                MatchState m = GameDirector.Instance.Match;
                s.Time = Mathf.Max(0f, m.MatchSeconds - m.Remaining);   // かんたんルール: 経過時間
            }
            return s;
        }

        /// <summary>名前付きの地点 (自同盟の側から見た位置)。</summary>
        private Vector3 PointFor(AutoPoint point)
        {
            Field2027Data field = ArenaBuilder.Field2027;
            Side mine = TournamentController.SideOf(Self.Team);
            string side = TournamentController.SideName(mine);
            string enemy = TournamentController.SideName(TournamentRound.Opposite(mine));
            float sign = mine == Side.Red ? -1f : 1f;   // 赤は x < 0 側
            if (m_Rule != null && m_Rule.Custom && point == AutoPoint.Custom)
            {
                // 地図で選んだ点は「自同盟が赤側」の座標。青同盟なら点対称 (フィールドは点対称)
                return mine == Side.Red ? new Vector3(m_Rule.X, 0f, m_Rule.Z) : new Vector3(-m_Rule.X, 0f, -m_Rule.Z);
            }
            switch (point)
            {
                case AutoPoint.OwnFront: return BaseFront(field, side);
                case AutoPoint.EnemyFront: return BaseFront(field, enemy);
                case AutoPoint.Start: return field != null ? field.Spawn(side, 0).position : Self.StartPosition;
                case AutoPoint.EnemyStart:
                {
                    // 相手のスタートゾーンの手前 (中に入ると相手の補給エリアに近く、スタートゾーンの出口で待ち構える形)
                    Vector3 sp = field != null ? field.Spawn(enemy, 0).position : -Self.StartPosition;
                    Vector3 toCenter = -sp; toCenter.y = 0f;
                    return sp + (toCenter.sqrMagnitude > 1e-3f ? toCenter.normalized : Vector3.zero) * 3f;
                }
                case AutoPoint.LeftLane: return new Vector3(sign * 3f, 0f, -sign * 3f);    // 赤: 陣地側 (z > 0)
                case AutoPoint.RightLane: return new Vector3(sign * 3f, 0f, sign * 3f);    // 赤: ブロックゾーン側 (z < 0)
                default: return Vector3.zero;
            }
        }

        private static Vector3 BaseFront(Field2027Data field, string side)
        {
            if (field == null) return Vector3.zero;
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (BasePanel p in field.PanelsOf(side)) { sum += p.transform.position; n++; }
            if (n == 0) return Vector3.zero;
            Vector3 c = sum / n;
            c.y = 0f;
            Vector3 toCenter = -c;
            toCenter.y = 0f;
            return c + (toCenter.sqrMagnitude > 1e-3f ? toCenter.normalized : Vector3.zero) * 2.5f;
        }
    }
}
