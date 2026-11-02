using System.Collections.Generic;
using CoreSim.Net;
using CoreSim.Rules;
using UnityEngine;

namespace CoreSim
{
    /// <summary>HUD とスナップショットが読む、大会ルールの表示用の状態。ホストはルールから、クライアントは受信から埋める。</summary>
    public sealed class TournamentView
    {
        public float Time;
        public int RoundNumber = 1;
        public bool Finished;
        public byte Winner = 255;      // 0 赤 / 1 青 / 2 引き分け / 255 未定
        public AllianceState Red;
        public AllianceState Blue;
        public readonly Dictionary<int, RobotExtra> Robots = new Dictionary<int, RobotExtra>();
        public readonly List<string> Log = new List<string>();

        public AllianceState Of(Side side) => side == Side.Red ? Red : Blue;
        public bool BaseAttackActive(TournamentConfig c) => Time >= c.BaseImmuneSeconds;
    }

    public struct RobotExtra
    {
        public bool IsHead;
        public bool IsBuilder;
        public int Hp;
        public int MaxHp;
        public int Magazine;
        public float ReviveIn;
        public float InvincibleFor;
        public float FrozenFor;
        public bool Destroyed;
        public bool PitInGranted;
    }

    /// <summary>
    /// 大会ルール (CoRE-1 V27.1.3) の進行をシーンに繋ぐ。ルールの判定は <see cref="TournamentRound"/>
    /// (純 C#) が持ち、ここは機体の位置からゾーンの出入りを拾い、HP・停止・無敵を機体へ写す。
    /// </summary>
    public sealed class TournamentController
    {
        public TournamentRound Round { get; private set; }
        public TournamentSeries Series { get; private set; }
        public Field2027Data Field;
        public readonly TournamentView View = new TournamentView();
        public readonly TournamentConfig Config = new TournamentConfig();
        public bool Remote;            // クライアントの写し (ルールは回さない)
        public System.Action<TournamentEvent> OnEvent;

        private readonly Dictionary<int, RobotAgent> m_Agents = new Dictionary<int, RobotAgent>();
        private readonly Dictionary<int, bool> m_InSupply = new Dictionary<int, bool>();
        private readonly Dictionary<int, bool> m_InNoEntry = new Dictionary<int, bool>();
        private readonly Dictionary<int, int> m_PlateState = new Dictionary<int, int>();

        public static Side SideOf(Team team) => team == Team.Red ? Side.Red : Side.Blue;
        public static string SideName(Side side) => side == Side.Red ? "red" : "blue";
        public static Team TeamOf(Side side) => side == Side.Red ? Team.Red : Team.Blue;

        public void SetupLocal(IReadOnlyList<RobotAgent> agents, int bestOf)
        {
            Round = new TournamentRound(Config);
            Series = new TournamentSeries(bestOf);
            m_Agents.Clear();
            foreach (RobotAgent a in agents)
            {
                m_Agents[a.Index] = a;
                TournamentRobot r = Round.AddRobot(a.Index, SideOf(a.Team), a.IsHeadAttacker, a.Kind);
                // HP の上限が機体ごとに違う (アタッカー 60 / ヘッド 300) ので、HUD 用の HealthModel を作り直す
                a.Health = new HealthModel(r.MaxHp, Config.HitDamage);
                m_InSupply[a.Index] = false;
                m_InNoEntry[a.Index] = false;
                m_PlateState[a.Index] = -1;
            }
        }

        public void SetupRemote(int bestOf)
        {
            Remote = true;
            Series = new TournamentSeries(bestOf);
        }

        public void RegisterAgent(RobotAgent agent)
        {
            m_Agents[agent.Index] = agent;
            m_PlateState[agent.Index] = -1;
        }

        public void BeginRound()
        {
            Round.Begin();
            View.Log.Clear();
            Field?.Props.ResetAll();
            foreach (RobotAgent a in m_Agents.Values)
            {
                a.ResetToStart();
                a.Frozen = false;
            }
            foreach (int k in new List<int>(m_InSupply.Keys)) m_InSupply[k] = false;
            foreach (int k in new List<int>(m_InNoEntry.Keys)) m_InNoEntry[k] = false;
            SyncAgents();
            FillView();
        }

        public RobotAgent Agent(int slot) => m_Agents.TryGetValue(slot, out RobotAgent a) ? a : null;

        // ------------------------------------------------------------------ 毎フレーム (ホスト)

        public void Tick(float dt)
        {
            if (Remote || Round == null) return;
            Round.Tick(dt);
            UpdateZones();
            SyncAgents();
            DrainEvents();
            FillView();
        }

        private void UpdateZones()
        {
            if (Field == null) return;
            foreach (TournamentRobot r in Round.Robots)
            {
                if (!m_Agents.TryGetValue(r.Slot, out RobotAgent a)) continue;
                Vector3 p = a.transform.position;
                r.OnBunker = Field.OnBunkerTop(p);

                bool inSupply = Field.InSupplyZone(SideName(r.Side), p);
                bool inEnemySupply = Field.InSupplyZone(SideName(TournamentRound.Opposite(r.Side)), p);
                if (inSupply && !m_InSupply[r.Slot] && !r.Destroyed)
                {
                    Round.EnterSupplyArea(r.Slot);
                }
                else if (inEnemySupply && !m_InSupply[r.Slot] && !r.Destroyed)
                {
                    Round.EnterEnemySupplyArea(r.Slot);
                }
                m_InSupply[r.Slot] = inSupply || inEnemySupply;

                bool inNoEntry = Field.InNoEntryZone(p);
                if (inNoEntry && !m_InNoEntry[r.Slot] && !r.Destroyed)
                {
                    Round.EnterNoEntryZone(r.Slot);
                }
                m_InNoEntry[r.Slot] = inNoEntry;
            }
        }

        private void SyncAgents()
        {
            float t = Round.Time;
            foreach (TournamentRobot r in Round.Robots)
            {
                if (!m_Agents.TryGetValue(r.Slot, out RobotAgent a)) continue;
                a.Health.Set(r.Hp, r.HitsTaken);
                a.Frozen = r.IsFrozen(t);
                ApplyPlateState(a, r.Destroyed, r.IsInvincible(t));
            }
        }

        private void ApplyPlateState(RobotAgent a, bool destroyed, bool invincible)
        {
            int state = destroyed ? 1 : invincible ? 2 : 0;
            if (m_PlateState.TryGetValue(a.Index, out int prev) && prev == state) return;
            m_PlateState[a.Index] = state;
            a.SetPlateColor(state == 1 ? new Color(0.25f, 0.25f, 0.25f) : state == 2 ? new Color(1f, 0.85f, 0.3f) : a.TeamColor);
        }

        private void DrainEvents()
        {
            foreach (TournamentEvent e in Round.Events)
            {
                if (!string.IsNullOrEmpty(e.Text))
                {
                    string who = e.Slot >= 0 && m_Agents.TryGetValue(e.Slot, out RobotAgent a) ? a.DisplayName + ": " : (e.Side == Side.Red ? "赤: " : "青: ");
                    View.Log.Add($"{FormatTime(Round.Time)}  {who}{e.Text}");
                    if (View.Log.Count > 4) View.Log.RemoveAt(0);
                }
                OnEvent?.Invoke(e);
            }
            Round.Events.Clear();
        }

        public static string FormatTime(float t)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(t));
            return $"{s / 60}:{s % 60:00}";
        }

        private void FillView()
        {
            View.Time = Round.Time;
            View.Finished = Round.Finished;
            View.Winner = Round.Finished ? (Round.Winner == Side.Red ? (byte)0 : Round.Winner == Side.Blue ? (byte)1 : (byte)2) : (byte)255;
            View.RoundNumber = Series.RoundNumber + 1;
            View.Red = Pack(Round.Red, Series.RedWins);
            View.Blue = Pack(Round.Blue, Series.BlueWins);
            foreach (TournamentRobot r in Round.Robots)
            {
                View.Robots[r.Slot] = new RobotExtra
                {
                    IsHead = r.IsHead,
                    IsBuilder = r.IsBuilder,
                    Hp = r.Hp,
                    MaxHp = r.MaxHp,
                    Magazine = r.Magazine,
                    ReviveIn = r.Destroyed && r.ReviveAt >= 0f ? r.ReviveAt - Round.Time : -1f,
                    InvincibleFor = Mathf.Max(0f, r.InvincibleUntil - Round.Time),
                    FrozenFor = Mathf.Max(0f, r.FrozenUntil - Round.Time),
                    Destroyed = r.Destroyed,
                    PitInGranted = r.PitInGranted,
                };
            }
        }

        private AllianceState Pack(TournamentAlliance a, int wins)
        {
            float t = Round.Time;
            return new AllianceState
            {
                Vp = a.Vp,
                Rp = (ushort)Mathf.Clamp(a.Rp, 0, ushort.MaxValue),
                BaseHp = (ushort)Mathf.Max(0, a.BaseHp),
                BaseDestroyed = a.BaseDestroyed,
                Discs = (ushort)Mathf.Max(0, a.Discs),
                BoostFor = Mathf.Max(0f, a.BoostUntil - t),
                BarrierLevel = (byte)(a.BarrierActive(t) ? a.BarrierLevel : 0),
                BarrierFor = Mathf.Max(0f, a.BarrierUntil - t),
                SkillLockFor = Mathf.Max(0f, a.SkillLockUntil - t),
                PitInCredits = (byte)Mathf.Clamp(a.PitInCredits, 0, 255),
                Wins = (byte)wins,
                Sites = (byte)a.SitesMask,
                Spots = (uint)a.SpotsMask,
                Containers = (byte)Mathf.Clamp(a.Containers, 0, 255),
            };
        }

        // ------------------------------------------------------------------ 運搬・建設 (ビルダー)

        /// <summary>つかむと反則になる理由 (HUD の警告)。ホストはルールから、クライアントは受信した状態から判定する。</summary>
        public string PickupWarning(RobotAgent robot, FieldProp prop)
        {
            if (robot == null || prop == null) return null;
            Side side = SideOf(robot.Team);
            if (prop.Kind == PropKind.Block)
            {
                if (!robot.IsBuilder) return "ビルダー以外のロボットがブロックを保持した";
                if (prop.Slot != null && prop.Slot.Kind == PlaceKind.Site) return "ビルドサイトに置かれたブロックを取り出した";
                return null;
            }
            bool onSpot = prop.Slot != null && prop.Slot.Kind == PlaceKind.Spot;
            if (!Remote && Round != null)
            {
                return Round.ContainerPickupViolation(side, prop.Origin, prop.ColorSide, onSpot, onSpot ? prop.Slot.RuleSide : (Side?)null);
            }
            if (onSpot) return "スポットに置かれたコンテナを取り出した";
            if (prop.ColorSide.HasValue && prop.ColorSide.Value != side) return "相手同盟の RP コンテナ置き場からコンテナを動かした";
            AllianceState a = View.Of(side);
            if (prop.Origin == ContainerOrigin.HolderB && (a.Sites & (1 << 2)) == 0) return "ビルドサイト 3 を獲得する前に置き場 B のコンテナを回収した";
            if (prop.Origin == ContainerOrigin.HolderC && (a.Sites & (1 << 3)) == 0) return "ビルドサイト 4 を獲得する前に置き場 C のコンテナを回収した";
            return null;
        }

        /// <summary>置くと反則になる理由 (相手のスポットなど)。</summary>
        public string PlaceWarning(RobotAgent robot, FieldProp prop, PropSlot slot)
        {
            if (robot == null || prop == null || slot == null) return null;
            Side side = SideOf(robot.Team);
            if (slot.Kind == PlaceKind.Spot && slot.RuleSide != side) return "相手同盟のスポットにコンテナを置いた";
            if (slot.Kind == PlaceKind.Site && slot.RuleSide != side) return "相手のビルドサイト (効果なし)";
            return null;
        }

        /// <summary>持ち上げた瞬間 (ホスト)。反則の判定と、スポットのコンテナ数の更新。</summary>
        public void OnPickUp(RobotAgent robot, FieldProp prop)
        {
            if (Remote || Round == null || robot == null || prop == null) return;
            if (prop.Kind == PropKind.Block)
            {
                if (!robot.IsBuilder) Round.NonBuilderHeldBlock(robot.Index);
                else if (prop.Slot != null && prop.Slot.Kind == PlaceKind.Site) Round.RemoveBlockFromSite(robot.Index);
                return;
            }
            bool onSpot = prop.Slot != null && prop.Slot.Kind == PlaceKind.Spot;
            Round.PickUpContainer(robot.Index, prop.Origin, prop.ColorSide, onSpot, onSpot ? prop.Slot.RuleSide : (Side?)null);
        }

        /// <summary>置いた瞬間 (ホスト)。スポットの得点、ビルドサイトの獲得。</summary>
        public void OnPlaced(RobotAgent robot, FieldProp prop, PropSlot slot)
        {
            if (Remote || Round == null || robot == null || prop == null || slot == null) return;
            if (slot.Kind == PlaceKind.Spot && prop.Kind == PropKind.Container)
            {
                Round.PlaceContainerOnSpot(robot.Index, slot.RuleSide, slot.Index);
            }
            else if (slot.Kind == PlaceKind.Site && prop.Kind == PropKind.Block)
            {
                Round.PlaceBlockOnSite(robot.Index, slot.RuleSide, slot.Index, slot.Count);
            }
        }

        // ------------------------------------------------------------------ 判定の入口 (ホスト)

        public void RegisterRobotHit(RobotAgent target, RobotAgent attacker)
        {
            if (Remote || Round == null) return;
            int dmg = Round.Hit(attacker != null ? attacker.Index : -1, target.Index);
            if (dmg > 0)
            {
                target.FlashPlates();
                GameDirector.Instance?.Audio.PlayHit(target.transform.position);
            }
        }

        public void RegisterBaseHit(BasePanel panel, RobotAgent attacker)
        {
            if (Remote || Round == null) return;
            Side side = panel.SideName == "red" ? Side.Red : Side.Blue;
            int dmg = Round.HitBase(attacker != null ? attacker.Index : -1, side, panel.High);
            if (dmg > 0)
            {
                panel.Flash();
                GameDirector.Instance?.Audio.PlayHit(panel.transform.position);
            }
        }

        public bool TryConsumeShot(RobotAgent agent) => !Remote && Round != null && Round.TryFire(agent.Index);

        /// <param name="targetSlot">ピットインを受ける機体 (発動した操縦者の機体)。</param>
        public bool ActivateSkill(Side side, SkillKind kind, out string reason, int targetSlot = -1)
        {
            reason = "";
            return !Remote && Round != null && Round.ActivateSkill(side, kind, out reason, targetSlot);
        }

        /// <summary>この機体がピットインを許可されているか (CPU が補給エリアへ向かう条件)。</summary>
        public bool IsPitInGranted(int slot)
        {
            TournamentRobot r = Round?.Find(slot);
            return r != null && r.PitInGranted;
        }

        public string ResultText(Side? mySide)
        {
            string round = View.Winner == 2 ? "引き分け" : View.Winner == 0 ? "赤同盟の勝ち" : View.Winner == 1 ? "青同盟の勝ち" : "";
            if (mySide.HasValue && View.Winner != 255 && View.Winner != 2)
            {
                round = (View.Winner == (byte)mySide.Value ? "勝利!  " : "敗北…  ") + round;
            }
            return $"{round}\n赤 {View.Red.Vp} VP  -  青 {View.Blue.Vp} VP";
        }

        // ------------------------------------------------------------------ クライアントの写し

        public void ApplyRemote(SnapshotMessage s)
        {
            View.Time = s.RoundTime;
            View.RoundNumber = s.RoundNumber;
            View.Winner = s.RoundWinner;
            View.Finished = s.RoundWinner != 255;
            View.Red = s.Red;
            View.Blue = s.Blue;
            foreach (RobotState rs in s.Robots)
            {
                View.Robots[rs.Slot] = new RobotExtra
                {
                    IsHead = rs.IsHead,
                    IsBuilder = rs.IsBuilder,
                    Hp = rs.Hp,
                    MaxHp = rs.MaxHp,
                    Magazine = rs.Magazine,
                    ReviveIn = rs.ReviveIn,
                    InvincibleFor = rs.InvincibleFor,
                    FrozenFor = rs.FrozenFor,
                    Destroyed = rs.Ko,
                    PitInGranted = rs.PitInGranted,
                };
                if (m_Agents.TryGetValue(rs.Slot, out RobotAgent a))
                {
                    if (rs.MaxHp > 0 && a.Health.MaxHp != rs.MaxHp)
                    {
                        a.Health = new HealthModel(rs.MaxHp, Config.HitDamage);
                    }
                    a.Health.Set(rs.Hp, rs.Hits);
                    a.Frozen = rs.FrozenFor > 0f;
                    a.IsHeadAttacker = rs.IsHead;
                    ApplyPlateState(a, rs.Ko, rs.InvincibleFor > 0f);
                }
            }
        }
    }
}
