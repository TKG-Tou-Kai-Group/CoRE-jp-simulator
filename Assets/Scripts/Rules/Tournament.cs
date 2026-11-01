using System;
using System.Collections.Generic;

namespace CoreSim.Rules
{
    /// <summary>赤同盟と青同盟。</summary>
    public enum Side : byte
    {
        Red = 0,
        Blue = 1,
    }

    /// <summary>総大将が RP で発動するスキル (ルールブック 8.1 表 8-1)。</summary>
    public enum SkillKind : byte
    {
        PitIn = 0,
        Supply = 1,
        HealingLv1 = 2,
        HealingLv2 = 3,
        BoostLv1 = 4,
        BoostLv2 = 5,
        BarrierLv1 = 6,
        BarrierLv2 = 7,
        Regene = 8,
    }

    public struct SkillSpec
    {
        public SkillKind Kind;
        public int Cost;
        public float Duration;   // 0 = 効果時間なし
        public string Name;
    }

    /// <summary>
    /// CoRE-1 ルールブック V27.1.3 (2027 シーズン) の数値。アタッカーだけをモデル化しているので、
    /// ビルダー・オートアタッカー・ストライダー・コンテナ・ブロックに関わる値は持たない。
    /// </summary>
    public sealed class TournamentConfig
    {
        public float RoundSeconds = 420f;         // 10.5 / 5.1 ラウンドは最長 7 分
        public int VpToWin = 500;                 // 5.1 VP 500 でラウンド終了
        public int AttackerHp = 60;               // 6.3 表 6-2
        public int HeadAttackerHp = 300;          // 6.3 / 6.7
        public float HeadRegenPerSecond = 5f;     // 6.7 1 秒ごとに 5
        public int HitDamage = 10;                // 6.4 / 6.5
        public int BaseHp = 200;                  // 6.8
        public int BaseLowHpDamage = 10;          // 6.8 表 6-5
        public int BaseLowRpDamage = 5;
        public int BaseHighHpDamage = 20;
        public int BaseHighRpDamage = 10;
        public float BaseImmuneSeconds = 180f;    // 6.8 開始から 3 分は効かない
        public float AutoReviveSeconds = 30f;     // 6.6
        public float AutoReviveDeadline = 180f;   // 6.6 30 秒後が 3 分以内なら復活
        public float ReviveInvincibleSeconds = 5f;// 6.6
        public int RpInitial = 100;               // 5.4 式 5-1 (ボーナス無し)
        public float RpBonusAt = 300f;            // 5.4 5 分で +100
        public int RpBonus = 100;
        public int DiscsPerAlliance = 400;        // 6.2 予選・準決勝 (決勝は 500)
        public int MagazineCapacity = 50;         // ルールには無い (機体設計次第)。ゲームの仮定 (メニューで変更可)
        public float PitInDwellSeconds = 20f;     // 8.2 入ってから 20 秒は出られない
        public float SkillLockSeconds = 5f;       // 8.1 効果が終わってから 5 秒
        public int KillVp = 10;                   // 5.3
        public int BaseDestroyVp = 100;           // 5.3
        public int BaseDestroyRp = 100;           // 5.4
        public int HeadVpMax = 300;               // 6.7
        public int FoulVp = 50;                   // 9.1
        public float BunkerDamageFactor = 0.5f;   // 7.6 / 6.5
        public float DiscSpeedLimit = 15f;        // 14.3 射出速度 15 m/s まで

        /// <summary>表示などが参照する既定値。</summary>
        public static readonly TournamentConfig Default = new TournamentConfig();

        public static readonly SkillSpec[] Skills =
        {
            new SkillSpec { Kind = SkillKind.PitIn, Cost = 20, Duration = 0f, Name = "ピットイン" },
            new SkillSpec { Kind = SkillKind.Supply, Cost = 100, Duration = 0f, Name = "サプライ" },
            new SkillSpec { Kind = SkillKind.HealingLv1, Cost = 50, Duration = 0f, Name = "ヒーリング Lv.1" },
            new SkillSpec { Kind = SkillKind.HealingLv2, Cost = 100, Duration = 0f, Name = "ヒーリング Lv.2" },
            new SkillSpec { Kind = SkillKind.BoostLv1, Cost = 50, Duration = 20f, Name = "ブースト Lv.1" },
            new SkillSpec { Kind = SkillKind.BoostLv2, Cost = 90, Duration = 40f, Name = "ブースト Lv.2" },
            new SkillSpec { Kind = SkillKind.BarrierLv1, Cost = 80, Duration = 20f, Name = "バリア Lv.1" },
            new SkillSpec { Kind = SkillKind.BarrierLv2, Cost = 150, Duration = 15f, Name = "バリア Lv.2" },
            new SkillSpec { Kind = SkillKind.Regene, Cost = 80, Duration = 0f, Name = "リジェネ" },
        };

        public static SkillSpec Spec(SkillKind kind) => Skills[(int)kind];
    }

    public sealed class TournamentRobot
    {
        public int Slot;
        public Side Side;
        public bool IsHead;
        public int MaxHp;
        public int Hp;
        public bool Destroyed;
        public float DestroyedAt = -1f;
        /// <summary>自動復活の予定時刻。-1 は復活しない。</summary>
        public float ReviveAt = -1f;
        public float InvincibleUntil = -1f;
        /// <summary>ピットイン中 (完全停止) の終了時刻。</summary>
        public float FrozenUntil = -1f;
        public int Magazine;
        public int Shots;
        public int HitsTaken;
        public int DamageDealt;
        public int Kills;
        /// <summary>接地面がバンカーの上面にある (ゲーム側が毎フレーム更新)。</summary>
        public bool OnBunker;
        /// <summary>このヘッドアタッカーから相手同盟へ渡った VP の累計 (最大 300)。</summary>
        public int HeadVpAwarded;
        /// <summary>総大将がこの機体にピットインを発動した (8.2: 1 回の発動で 1 台が 1 回入れる)。</summary>
        public bool PitInGranted;

        public bool IsInvincible(float now) => now < InvincibleUntil;
        public bool IsFrozen(float now) => now < FrozenUntil;
        public bool CanAct(float now) => !Destroyed && !IsFrozen(now);
    }

    public sealed class TournamentAlliance
    {
        public Side Side;
        public int Vp;
        public int Rp;
        public int BaseHp;
        public bool BaseDestroyed;
        /// <summary>同盟で共用するディスクの残り (機体に装填済みのものを除く)。</summary>
        public int Discs;
        public float BoostUntil = -1f;
        public float BarrierUntil = -1f;
        public int BarrierLevel;
        public float SkillLockUntil = -1f;
        public int DamageDealt;
        public int Fouls;
        public readonly List<TournamentRobot> Robots = new List<TournamentRobot>();

        public bool BoostActive(float now) => now < BoostUntil;
        public bool BarrierActive(float now) => now < BarrierUntil;
        public bool SkillLocked(float now) => now < SkillLockUntil;
        /// <summary>ピットインとサプライ以外のスキルを今発動できるか。</summary>
        public bool TimedSkillAvailable(float now) => !BoostActive(now) && !BarrierActive(now) && !SkillLocked(now);

        /// <summary>ピットインを発動済みでまだ補給エリアに入っていない機体の数 (表示用)。</summary>
        public int PitInCredits
        {
            get
            {
                int n = 0;
                foreach (TournamentRobot r in Robots) if (r.PitInGranted) n++;
                return n;
            }
        }

        public TournamentRobot Head
        {
            get
            {
                foreach (TournamentRobot r in Robots) if (r.IsHead) return r;
                return null;
            }
        }
    }

    public enum TournamentEventKind : byte
    {
        Hit,
        Destroyed,
        Revived,
        BaseHit,
        BaseDestroyed,
        Skill,
        SkillFailed,
        Foul,
        PitIn,
        RpBonus,
        RoundEnd,
    }

    public struct TournamentEvent
    {
        public TournamentEventKind Kind;
        public Side Side;      // 行為者の同盟
        public int Slot;       // 関係する機体 (-1 = なし)
        public int Value;
        public string Text;
    }

    /// <summary>
    /// 1 ラウンドの進行と得点 (ルールブック第 5・6・8・9 章のうちアタッカーに関わる部分)。
    /// 物理と時間の進み方はゲーム側が持ち、ここは判定と数値だけを扱う。
    /// </summary>
    public sealed class TournamentRound
    {
        public readonly TournamentConfig Config;
        public float Time { get; private set; }
        public bool Running { get; private set; }
        public bool Finished { get; private set; }
        public Side? Winner { get; private set; }
        public string EndReason { get; private set; } = "";
        public readonly TournamentAlliance Red = new TournamentAlliance { Side = Side.Red };
        public readonly TournamentAlliance Blue = new TournamentAlliance { Side = Side.Blue };
        public readonly List<TournamentEvent> Events = new List<TournamentEvent>();

        private readonly List<TournamentRobot> m_Robots = new List<TournamentRobot>();
        private readonly float[] m_RegenAcc = new float[2];
        /// <summary>効果時間のあるスキルの終了時刻 (同盟ごと)。-1 は無し。終了後 5 秒のロックに使う。</summary>
        private readonly float[] m_EffectEnd = { -1f, -1f };
        private bool m_RpBonusGiven;

        public IReadOnlyList<TournamentRobot> Robots => m_Robots;

        public TournamentRound(TournamentConfig config = null)
        {
            Config = config ?? new TournamentConfig();
        }

        public TournamentAlliance Of(Side side) => side == Side.Red ? Red : Blue;
        public TournamentAlliance OpponentOf(Side side) => side == Side.Red ? Blue : Red;
        public static Side Opposite(Side side) => side == Side.Red ? Side.Blue : Side.Red;

        public TournamentRobot Find(int slot)
        {
            foreach (TournamentRobot r in m_Robots) if (r.Slot == slot) return r;
            return null;
        }

        public TournamentRobot AddRobot(int slot, Side side, bool isHead)
        {
            if (Running) throw new InvalidOperationException("ラウンド中は機体を足せない");
            var r = new TournamentRobot
            {
                Slot = slot,
                Side = side,
                IsHead = isHead,
                MaxHp = isHead ? Config.HeadAttackerHp : Config.AttackerHp,
            };
            r.Hp = r.MaxHp;
            m_Robots.Add(r);
            Of(side).Robots.Add(r);
            return r;
        }

        /// <summary>ラウンド開始。VP 0、RP 初期値、陣地 HP、ディスクの付与と装填。</summary>
        public void Begin()
        {
            Time = 0f;
            Running = true;
            Finished = false;
            Winner = null;
            EndReason = "";
            m_RpBonusGiven = false;
            foreach (TournamentAlliance a in new[] { Red, Blue })
            {
                a.Vp = 0;
                a.Rp = Config.RpInitial;
                a.BaseHp = Config.BaseHp;
                a.BaseDestroyed = false;
                a.Discs = Config.DiscsPerAlliance;
                a.BoostUntil = a.BarrierUntil = a.SkillLockUntil = -1f;
                a.BarrierLevel = 0;
                a.DamageDealt = 0;
                a.Fouls = 0;
                foreach (TournamentRobot r in a.Robots)
                {
                    r.Hp = r.MaxHp;
                    r.Destroyed = false;
                    r.DestroyedAt = r.ReviveAt = r.InvincibleUntil = r.FrozenUntil = -1f;
                    r.Shots = r.HitsTaken = r.DamageDealt = r.Kills = 0;
                    r.OnBunker = false;
                    r.HeadVpAwarded = 0;
                    r.PitInGranted = false;
                    int take = Math.Min(Config.MagazineCapacity, a.Discs);
                    r.Magazine = take;
                    a.Discs -= take;
                }
            }
            m_RegenAcc[0] = m_RegenAcc[1] = 0f;
            m_EffectEnd[0] = m_EffectEnd[1] = -1f;
        }

        public void Tick(float dt)
        {
            if (!Running || Finished) return;
            Time += dt;

            foreach (TournamentAlliance a in new[] { Red, Blue })
            {
                int i = (int)a.Side;
                // ヘッドアタッカーの自動回復 (6.7)。自同盟の陣地が撃破されると止まる。
                TournamentRobot head = a.Head;
                if (head != null && !head.Destroyed && !a.BaseDestroyed && head.Hp < head.MaxHp)
                {
                    m_RegenAcc[i] += dt * Config.HeadRegenPerSecond;
                    int whole = (int)m_RegenAcc[i];
                    if (whole > 0)
                    {
                        m_RegenAcc[i] -= whole;
                        head.Hp = Math.Min(head.MaxHp, head.Hp + whole);
                    }
                }
                // 自動復活 (6.6)
                foreach (TournamentRobot r in a.Robots)
                {
                    if (r.Destroyed && r.ReviveAt >= 0f && Time >= r.ReviveAt)
                    {
                        Revive(r, "自動復活");
                    }
                }
                // 効果時間のあるスキルが終わったら、終了時刻から 5 秒のロック (8.1)
                if (m_EffectEnd[i] >= 0f && Time >= m_EffectEnd[i])
                {
                    a.SkillLockUntil = m_EffectEnd[i] + Config.SkillLockSeconds;
                    a.BarrierLevel = 0;
                    m_EffectEnd[i] = -1f;
                }
            }

            if (!m_RpBonusGiven && Time >= Config.RpBonusAt)
            {
                m_RpBonusGiven = true;
                Red.Rp += Config.RpBonus;
                Blue.Rp += Config.RpBonus;
                Emit(TournamentEventKind.RpBonus, Side.Red, -1, Config.RpBonus, "5 分経過: 両同盟に RP +100");
            }

            if (Red.Vp >= Config.VpToWin || Blue.Vp >= Config.VpToWin)
            {
                Finish("VP が 500 に達した");
            }
            else if (Time >= Config.RoundSeconds)
            {
                Finish("7 分が経過した");
            }
        }

        /// <summary>ラウンドを終え、勝敗を決める (5.2)。同点は与ダメージ合計、それでも同じなら引き分け。</summary>
        public void Finish(string reason)
        {
            if (Finished) return;
            Finished = true;
            Running = false;
            EndReason = reason;
            if (Red.Vp != Blue.Vp) Winner = Red.Vp > Blue.Vp ? Side.Red : Side.Blue;
            else if (Red.DamageDealt != Blue.DamageDealt) Winner = Red.DamageDealt > Blue.DamageDealt ? Side.Red : Side.Blue;
            else Winner = null;
            Emit(TournamentEventKind.RoundEnd, Winner ?? Side.Red, -1, 0,
                 Winner.HasValue ? $"{(Winner == Side.Red ? "赤" : "青")}同盟の勝ち ({reason})" : $"引き分け ({reason})");
        }

        // ------------------------------------------------------------------ 攻撃

        /// <summary>1 回の命中で減る HP (6.5)。10 に倍率を掛け、小数第 1 位を四捨五入。</summary>
        public int DamageFor(TournamentRobot attackerRobot, TournamentRobot target)
        {
            if (target.IsInvincible(Time)) return 0;
            double value = Config.HitDamage;
            if (attackerRobot != null && Of(attackerRobot.Side).BoostActive(Time)) value *= 2.0;
            TournamentAlliance ta = Of(target.Side);
            if (!target.IsHead && ta.BarrierActive(Time))
            {
                value *= ta.BarrierLevel >= 2 ? 0.0 : 0.5;
            }
            if (target.OnBunker) value *= Config.BunkerDamageFactor;
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        /// <summary>ディスクが機体のダメージパネルに当たった。味方のディスクでも減る (6.4)。戻り値は減った HP。</summary>
        public int Hit(int attackerSlot, int targetSlot)
        {
            if (!Running || Finished) return 0;
            TournamentRobot target = Find(targetSlot);
            if (target == null || target.Destroyed) return 0;
            TournamentRobot attacker = Find(attackerSlot);
            int damage = Math.Min(target.Hp, DamageFor(attacker, target));
            target.HitsTaken++;
            if (damage <= 0) return 0;
            target.Hp -= damage;
            if (attacker != null)
            {
                attacker.DamageDealt += damage;
                if (attacker.Side != target.Side) Of(attacker.Side).DamageDealt += damage;
            }
            TournamentAlliance enemy = OpponentOf(target.Side);
            // ヘッドアタッカーの減った HP は、自同盟の陣地が撃破された後なら即 VP になる (6.7 (1))
            if (target.IsHead && Of(target.Side).BaseDestroyed)
            {
                AwardHeadVp(target, damage);
            }
            Emit(TournamentEventKind.Hit, attacker != null ? attacker.Side : target.Side, targetSlot, damage, null);
            if (target.Hp <= 0)
            {
                Destroy(target, attacker);
            }
            return damage;
        }

        private void AwardHeadVp(TournamentRobot head, int amount)
        {
            int room = Config.HeadVpMax - head.HeadVpAwarded;
            int give = Math.Max(0, Math.Min(room, amount));
            if (give <= 0) return;
            head.HeadVpAwarded += give;
            OpponentOf(head.Side).Vp += give;
        }

        private void Destroy(TournamentRobot robot, TournamentRobot by)
        {
            robot.Hp = 0;
            robot.Destroyed = true;
            robot.DestroyedAt = Time;
            robot.FrozenUntil = -1f;
            TournamentAlliance enemy = OpponentOf(robot.Side);
            if (by != null) by.Kills++;
            if (robot.IsHead)
            {
                // 復活しない。陣地がまだなら、その時点で 300VP (6.7 (2))
                robot.ReviveAt = -1f;
                if (!Of(robot.Side).BaseDestroyed)
                {
                    AwardHeadVp(robot, Config.HeadVpMax);
                }
                Emit(TournamentEventKind.Destroyed, enemy.Side, robot.Slot, 0, "ヘッドアタッカー撃破");
            }
            else
            {
                enemy.Vp += Config.KillVp;
                float at = Time + Config.AutoReviveSeconds;
                robot.ReviveAt = at <= Config.AutoReviveDeadline ? at : -1f;
                Emit(TournamentEventKind.Destroyed, enemy.Side, robot.Slot, Config.KillVp,
                     robot.ReviveAt >= 0f ? "撃破 (+10VP)。30 秒後に復活" : "撃破 (+10VP)。復活なし");
            }
        }

        private void Revive(TournamentRobot robot, string how)
        {
            robot.Destroyed = false;
            robot.Hp = robot.MaxHp;
            robot.ReviveAt = -1f;
            robot.InvincibleUntil = Time + Config.ReviveInvincibleSeconds;
            Emit(TournamentEventKind.Revived, robot.Side, robot.Slot, 0, how);
        }

        /// <summary>陣地のダメージパネルへの命中 (6.8)。戻り値は陣地の減った HP。</summary>
        public int HitBase(int attackerSlot, Side targetSide, bool highPanel)
        {
            if (!Running || Finished) return 0;
            if (Time < Config.BaseImmuneSeconds) return 0;
            TournamentAlliance owner = Of(targetSide);
            if (owner.BaseDestroyed) return 0;
            TournamentRobot attacker = Find(attackerSlot);
            TournamentAlliance attackers = attacker != null ? Of(attacker.Side) : OpponentOf(targetSide);

            double hp = highPanel ? Config.BaseHighHpDamage : Config.BaseLowHpDamage;
            if (attackers.BoostActive(Time)) hp *= 2.0;
            if (owner.BarrierActive(Time)) hp *= owner.BarrierLevel >= 2 ? 0.0 : 0.5;
            int hpLoss = Math.Min(owner.BaseHp, (int)Math.Round(hp, MidpointRounding.AwayFromZero));
            int rpLoss = owner.BarrierActive(Time) ? 0 : (highPanel ? Config.BaseHighRpDamage : Config.BaseLowRpDamage);
            owner.BaseHp -= hpLoss;
            owner.Rp = Math.Max(0, owner.Rp - rpLoss);
            if (attacker != null && attacker.Side != targetSide)
            {
                attacker.DamageDealt += hpLoss;
                attackers.DamageDealt += hpLoss;
            }
            Emit(TournamentEventKind.BaseHit, attackers.Side, attackerSlot, hpLoss, highPanel ? "陣地 (高) に命中" : "陣地 (低) に命中");
            if (owner.BaseHp <= 0)
            {
                owner.BaseDestroyed = true;
                TournamentAlliance enemy = OpponentOf(targetSide);
                enemy.Vp += Config.BaseDestroyVp;
                enemy.Rp += Config.BaseDestroyRp;
                TournamentRobot head = owner.Head;
                if (head != null && !head.Destroyed)
                {
                    AwardHeadVp(head, head.MaxHp - head.Hp);   // 減っている分をすぐに (6.7 (1))
                }
                Emit(TournamentEventKind.BaseDestroyed, enemy.Side, -1, Config.BaseDestroyVp,
                     $"{(targetSide == Side.Red ? "赤" : "青")}の陣地を撃破 (+100VP +100RP)");
            }
            return hpLoss;
        }

        /// <summary>射出できるなら 1 枚消費する。撃破中・ピットイン中・残弾 0 なら撃てない。</summary>
        public bool TryFire(int slot)
        {
            if (!Running || Finished) return false;
            TournamentRobot r = Find(slot);
            if (r == null || !r.CanAct(Time) || r.Magazine <= 0) return false;
            r.Magazine--;
            r.Shots++;
            return true;
        }

        // ------------------------------------------------------------------ スキル・補給・反則

        /// <param name="targetSlot">ピットインのとき、補給を受ける機体。</param>
        public bool CanActivate(Side side, SkillKind kind, out string reason, int targetSlot = -1)
        {
            TournamentAlliance a = Of(side);
            SkillSpec spec = TournamentConfig.Spec(kind);
            reason = "";
            if (!Running || Finished) { reason = "ラウンド外"; return false; }
            if (a.Rp < spec.Cost) { reason = $"RP が足りない ({a.Rp}/{spec.Cost})"; return false; }
            bool exempt = kind == SkillKind.PitIn || kind == SkillKind.Supply;
            if (!exempt && !a.TimedSkillAvailable(Time)) { reason = "別のスキルの効果中か、終了後 5 秒以内"; return false; }
            if (kind == SkillKind.Regene && PickRegeneTarget(a) == null) { reason = "復活できる機体がいない"; return false; }
            if (kind == SkillKind.PitIn)
            {
                TournamentRobot t = Find(targetSlot);
                if (t == null || t.Side != side) { reason = "対象の機体がいない"; return false; }
                if (t.Destroyed) { reason = "撃破中の機体はピットインできない"; return false; }
                if (t.PitInGranted) { reason = "すでに発動済み"; return false; }
            }
            return true;
        }

        /// <param name="targetSlot">ピットインのとき、補給を受ける機体 (1 回の発動で 1 台が 1 回入れる)。</param>
        public bool ActivateSkill(Side side, SkillKind kind, out string reason, int targetSlot = -1)
        {
            if (!CanActivate(side, kind, out reason, targetSlot))
            {
                Emit(TournamentEventKind.SkillFailed, side, -1, 0, $"{TournamentConfig.Spec(kind).Name}: {reason}");
                return false;
            }
            TournamentAlliance a = Of(side);
            SkillSpec spec = TournamentConfig.Spec(kind);
            a.Rp -= spec.Cost;
            switch (kind)
            {
                case SkillKind.PitIn:
                    Find(targetSlot).PitInGranted = true;
                    break;
                case SkillKind.Supply:
                    a.Discs += 100;
                    break;
                case SkillKind.HealingLv1:
                case SkillKind.HealingLv2:
                    foreach (TournamentRobot r in a.Robots)
                    {
                        if (r.IsHead || r.Destroyed) continue;
                        r.Hp = kind == SkillKind.HealingLv2 ? r.MaxHp : Math.Min(r.MaxHp, r.Hp + 30);
                    }
                    a.SkillLockUntil = Time + Config.SkillLockSeconds;
                    break;
                case SkillKind.BoostLv1:
                case SkillKind.BoostLv2:
                    a.BoostUntil = Time + spec.Duration;
                    m_EffectEnd[(int)side] = a.BoostUntil;
                    break;
                case SkillKind.BarrierLv1:
                case SkillKind.BarrierLv2:
                    a.BarrierUntil = Time + spec.Duration;
                    a.BarrierLevel = kind == SkillKind.BarrierLv2 ? 2 : 1;
                    m_EffectEnd[(int)side] = a.BarrierUntil;
                    break;
                case SkillKind.Regene:
                    Revive(PickRegeneTarget(a), "リジェネ");
                    a.SkillLockUntil = Time + Config.SkillLockSeconds;
                    break;
            }
            Emit(TournamentEventKind.Skill, side, kind == SkillKind.PitIn ? targetSlot : -1, spec.Cost, $"{spec.Name} 発動 (-{spec.Cost}RP)");
            return true;
        }

        private static TournamentRobot PickRegeneTarget(TournamentAlliance a)
        {
            TournamentRobot best = null;
            foreach (TournamentRobot r in a.Robots)
            {
                if (r.IsHead || !r.Destroyed) continue;
                if (best == null || r.DestroyedAt < best.DestroyedAt) best = r;
            }
            return best;
        }

        /// <summary>
        /// 機体が自同盟の手動補給エリアに入った (8.2)。ピットインが発動されていれば 20 秒の
        /// 完全停止と引き換えに装填し、されていなければ反則 (9.1)。入った瞬間に 1 回だけ呼ぶ。
        /// </summary>
        public bool EnterSupplyArea(int slot)
        {
            if (!Running || Finished) return false;
            TournamentRobot r = Find(slot);
            if (r == null || r.Destroyed) return false;
            TournamentAlliance a = Of(r.Side);
            if (!r.PitInGranted)
            {
                Foul(r.Side, r.Slot, "ピットインなしで手動補給エリアに入った");
                return false;
            }
            r.PitInGranted = false;
            r.FrozenUntil = Time + Config.PitInDwellSeconds;
            int take = Math.Min(Config.MagazineCapacity - r.Magazine, a.Discs);
            r.Magazine += take;
            a.Discs -= take;
            Emit(TournamentEventKind.PitIn, r.Side, r.Slot, take, $"ピットイン: ディスク {take} 枚を装填 (20 秒停止)");
            return true;
        }

        /// <summary>相手同盟の手動補給エリアに入った (9.1)。入った瞬間に 1 回だけ呼ぶ。</summary>
        public void EnterEnemySupplyArea(int slot)
        {
            TournamentRobot r = Find(slot);
            if (r == null || !Running || r.Destroyed) return;
            Foul(r.Side, slot, "相手同盟の手動補給エリアに入った");
        }

        /// <summary>進入禁止ゾーンに接地した (9.1)。入った瞬間に 1 回だけ呼ぶ。</summary>
        public void EnterNoEntryZone(int slot)
        {
            TournamentRobot r = Find(slot);
            if (r == null || !Running) return;
            Foul(r.Side, slot, "進入禁止ゾーンに入った");
        }

        public void Foul(Side side, int slot, string why)
        {
            TournamentAlliance a = Of(side);
            a.Vp -= Config.FoulVp;   // 0 より小さくなることがある (9.1)
            a.Fouls++;
            Emit(TournamentEventKind.Foul, side, slot, -Config.FoulVp, $"反則 (-50VP): {why}");
        }

        private void Emit(TournamentEventKind kind, Side side, int slot, int value, string text)
        {
            Events.Add(new TournamentEvent { Kind = kind, Side = side, Slot = slot, Value = value, Text = text });
        }
    }

    /// <summary>試合 = 複数ラウンドの先取 (10.5)。予選 BO1、準決勝 BO3、決勝 BO5。</summary>
    public sealed class TournamentSeries
    {
        public int WinsNeeded;
        public int RedWins;
        public int BlueWins;
        public int RoundNumber;   // 1 始まり

        public TournamentSeries(int bestOf)
        {
            WinsNeeded = bestOf / 2 + 1;
        }

        public bool Decided => RedWins >= WinsNeeded || BlueWins >= WinsNeeded;
        public Side? Champion => RedWins >= WinsNeeded ? Side.Red : BlueWins >= WinsNeeded ? (Side?)Side.Blue : null;

        public void Record(Side? winner)
        {
            RoundNumber++;
            if (winner == Side.Red) RedWins++;
            else if (winner == Side.Blue) BlueWins++;
        }
    }
}
