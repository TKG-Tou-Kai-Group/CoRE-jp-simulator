using CoreSim.Rules;
using UnityEngine;

namespace CoreSim
{
    /// <summary>CPU 側の総大将。1 秒ごとに状況を見て RP でスキルを発動する。</summary>
    public sealed class BotCommander
    {
        public Side Side;
        public TournamentController Tournament;
        public float NextThinkTime;
        private int m_LastBaseHp = -1;
        private float m_BaseHitAt = -100f;

        public void Think(float now, System.Collections.Generic.IReadOnlyList<RobotAgent> agents)
        {
            if (Tournament == null || Tournament.Remote || Tournament.Round == null || !Tournament.Round.Running) return;
            if (now < NextThinkTime) return;
            NextThinkTime = now + 1f;

            TournamentRound round = Tournament.Round;
            TournamentAlliance me = round.Of(Side);
            float t = round.Time;
            if (m_LastBaseHp >= 0 && me.BaseHp < m_LastBaseHp) m_BaseHitAt = t;
            m_LastBaseHp = me.BaseHp;

            // 0. オートアタッカーはラウンド開始 2 秒後に始動する (12.7)
            if (t > 2f)
            {
                foreach (TournamentRobot r in me.Robots)
                {
                    if (r.IsAuto && !r.AutoStarted && !r.AutoStopped) round.StartAuto(r.Slot, out _);
                }
            }
            // 1. ピットインは弾切れの CPU が自分で申請する (BotCommandSource)。総大将は出さない
            // 2. 同盟のディスクが尽きそうならサプライ
            if (me.Discs < 40 && round.CanActivate(Side, SkillKind.Supply, out _))
            {
                round.ActivateSkill(Side, SkillKind.Supply, out _);
            }
            // 3. 3 分を過ぎて撃破されたままの機体がいればリジェネ
            if (t > round.Config.AutoReviveDeadline && round.CanActivate(Side, SkillKind.Regene, out _))
            {
                round.ActivateSkill(Side, SkillKind.Regene, out _);
                return;
            }
            // 4. 陣地が攻撃され続けている・ヘッドが削られているならバリア
            TournamentRobot head = me.Head;
            bool baseUnderFire = t - m_BaseHitAt < 8f && !me.BaseDestroyed;
            bool headLow = head != null && !head.Destroyed && head.Hp < head.MaxHp / 2;
            if ((baseUnderFire || headLow) && round.CanActivate(Side, SkillKind.BarrierLv1, out _))
            {
                round.ActivateSkill(Side, SkillKind.BarrierLv1, out _);
                return;
            }
            // 5. 味方の HP が減っていればヒーリング
            int hurt = 0, alive = 0;
            foreach (TournamentRobot r in me.Robots)
            {
                if (r.IsHead || r.Destroyed) continue;
                alive++;
                if (r.Hp <= r.MaxHp / 2) hurt++;
            }
            if (alive > 0 && hurt >= Mathf.Max(1, alive / 2) && round.CanActivate(Side, SkillKind.HealingLv1, out _))
            {
                round.ActivateSkill(Side, SkillKind.HealingLv1, out _);
                return;
            }
            // 6. 敵が近くにいて余裕があればブースト
            bool enemyNear = false;
            foreach (RobotAgent a in agents)
            {
                if (TournamentController.SideOf(a.Team) != Side || a.IsKnockedOut) continue;
                foreach (RobotAgent b in agents)
                {
                    if (TournamentController.SideOf(b.Team) == Side || b.IsKnockedOut) continue;
                    if ((a.transform.position - b.transform.position).sqrMagnitude < 64f) { enemyNear = true; break; }
                }
                if (enemyNear) break;
            }
            if (enemyNear && me.Rp >= 150 && t > 30f && round.CanActivate(Side, SkillKind.BoostLv1, out _))
            {
                round.ActivateSkill(Side, SkillKind.BoostLv1, out _);
            }
        }
    }
}
