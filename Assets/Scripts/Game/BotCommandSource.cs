using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace CoreSim
{
    /// <summary>
    /// CPU 操縦。いちばん近い敵へ向き直り、間合いを取りつつ正面に捉えたら撃つ。
    /// 人と同じ <see cref="RobotCommand"/> を出すので、速度や射撃間隔の条件は人と同じ。
    /// </summary>
    public sealed class BotCommandSource : ICommandSource, IBotStatus
    {
        public RobotAgent Self;
        public System.Func<IReadOnlyList<RobotAgent>> Robots;
        /// <summary>撃たない (練習モードの的)。</summary>
        public bool Passive;
        /// <summary>0..1。高いほど狙いが正確で反応が速い。</summary>
        public float Skill = 0.6f;

        public float PreferredRange = 5f;
        public float FireRange = 9f;
        public float FireConeDeg = 5f;
        /// <summary>
        /// ビルダーはこの距離より近くに来たときだけ狙う (一度狙ったら <see cref="BuilderKeepRange"/> まで追う)。
        /// 遠くのビルダーを追ってブロックゾーンやスポットの周りをうろつくと、人がビルダーで作業できない。
        /// 相手にアタッカーがいなければ距離に関係なく狙う。
        /// </summary>
        public float BuilderEngageRange = 4f;
        public float BuilderKeepRange = 7f;

        /// <summary>オートアタッカーのプログラムからの指示。Free は CPU 本来の判断。</summary>
        public enum BotDirective { Free, Engage, AttackBase, GoTo, Hold, Retreat }
        public BotDirective Directive = BotDirective.Free;
        public Vector3 DirectivePoint;
        public float DirectiveRange = 5f;
        public Rules.AutoTarget TargetPolicy = Rules.AutoTarget.Nearest;
        /// <summary>オートアタッカー: ピットインを申請せず、弾切れでもプログラム通りに動く。ログは AutoAttackerSource が出す。</summary>
        public bool IsAutoProgram;
        /// <summary>大会ルールのとき。陣地攻撃と補給の判断に使う。</summary>
        public TournamentController Tournament;

        private float m_NextThinkTime;
        private RobotCommand m_Current;
        private RobotAgent m_Target;
        private string m_Mode = "";
        private bool m_PathOk;
        private float m_NextLogTime;
        private float m_NextPitInRequest;

        public void DebugPath(List<Vector3> points)
        {
            points.Clear();
            if (Self == null) return;
            points.Add(Self.transform.position);
            Vector3[] corners = m_Path.corners;
            if (corners != null)
            {
                for (int i = Mathf.Max(1, m_PathIndex); i < corners.Length; i++) points.Add(corners[i]);
            }
            if (m_Target != null) points.Add(m_Target.transform.position);
        }

        /// <summary>F9 のデバッグ表示: 今の判断、経路、速度。</summary>
        public string Status
        {
            get
            {
                float speed = Self != null && Self.Drive != null ? Self.Drive.Speed : 0f;
                string path = m_Path.corners != null && m_Path.corners.Length >= 2
                    ? $"経路 {m_Path.corners.Length} 角" + (m_Path.status == NavMeshPathStatus.PathPartial ? " (途中まで)" : "")
                    : (m_PathOk ? "経路 なし" : "経路 失敗");
                string escape = Time.time < m_EscapeUntil ? "  [脱出中]" : "";
                return $"{m_Mode}  {path}  {speed:F2} m/s  指令 ({m_Current.Move.x:F1}, {m_Current.Move.y:F1}, 旋回 {m_Current.Turn:F1}){escape}";
            }
        }
        private float m_StrafeSign = 1f;
        private float m_NextStrafeFlip;
        private float m_AimOffsetDeg;
        private float m_NextAimChange;
        private float m_StuckSince = -1f;
        private float m_EscapeUntil = -1f;
        private float m_EscapeTurn = 1f;
        private readonly NavMeshPath m_Path = new NavMeshPath();
        private Vector3 m_PathGoal;
        private float m_PathTime = -10f;
        private int m_PathIndex;

        public RobotCommand Read()
        {
            if (Self == null)
            {
                return RobotCommand.None;
            }
            // 人の反応速度に寄せて 10 Hz 前後で考える。
            if (Time.time < m_NextThinkTime)
            {
                return m_Current;
            }
            m_NextThinkTime = Time.time + Mathf.Lerp(0.2f, 0.08f, Skill);
            m_Current = Think();
            m_Current = Unstick(m_Current);
            m_Current = GuardZones(m_Current);
            if (!IsAutoProgram) BotLog.Maybe(Self, this, ref m_NextLogTime);
            return m_Current;
        }

        private RobotCommand Think()
        {
            var cmd = new RobotCommand();
            Transform t = Self.transform;
            if (Self.Frozen)
            {
                return cmd;   // ピットイン中は完全停止
            }

            if (Time.time > m_NextStrafeFlip)
            {
                m_StrafeSign = Random.value < 0.5f ? -1f : 1f;
                m_NextStrafeFlip = Time.time + Random.Range(1.5f, 4f);
            }
            if (Time.time > m_NextAimChange)
            {
                float spread = Mathf.Lerp(6f, 1f, Skill);
                m_AimOffsetDeg = Random.Range(-spread, spread);
                m_NextAimChange = Time.time + Random.Range(0.5f, 1.5f);
            }

            // 壁・構造物の回避: 前方 3 本のレイ。
            bool blockedAhead = Blocked(t, t.forward, 2.0f);
            bool blockedLeft = Blocked(t, Quaternion.Euler(0f, -35f, 0f) * t.forward, 1.6f);
            bool blockedRight = Blocked(t, Quaternion.Euler(0f, 35f, 0f) * t.forward, 1.6f);
            RobotAgent target = Nearest(t.position);

            // 大会ルール: 弾が無ければ自同盟の補給エリアへ。3 分を過ぎたら相手の陣地も狙う。
            if (Tournament != null && !Tournament.Remote && Tournament.Round != null && Tournament.Field != null)
            {
                Rules.TournamentRobot me = Tournament.Round.Find(Self.Index);
                if (me != null && me.PitInGranted)
                {
                    // この機体にピットインが発動されていれば補給エリアへ (弾が残っていても行く)
                    m_Mode = "ピットイン: 補給エリアへ";
                    return Steer(t, Tournament.Field.SupplyPoint(TournamentController.SideName(me.Side)), 0.4f, blockedAhead, blockedLeft, blockedRight);
                }
                if (me != null && me.Magazine == 0 && !IsAutoProgram)
                {
                    // 弾切れになったら自分でピットインを申請する (味方が人の同盟でも同じ。同盟の RP を 20 使う)。
                    // 許可が出るまでは、許可がないのに入ると反則なので、自同盟のスタートゾーンの内側で待つ
                    if (Time.time >= m_NextPitInRequest)
                    {
                        m_NextPitInRequest = Time.time + 2f;
                        if (Tournament.Round.Of(me.Side).Discs > 0 && Tournament.Round.CanActivate(me.Side, Rules.SkillKind.PitIn, out _, me.Slot))
                        {
                            Tournament.ActivateSkill(me.Side, Rules.SkillKind.PitIn, out _, me.Slot);
                        }
                    }
                    m_Mode = "弾切れ: ピットインを申請して待機";
                    return Steer(t, Tournament.Field.Spawn(TournamentController.SideName(me.Side), 0).position, 1.0f, blockedAhead, blockedLeft, blockedRight);
                }
                bool wantBase = Directive == BotDirective.AttackBase ||
                                (Directive == BotDirective.Free && (target == null || Random.value < 0.35f));
                if (me != null && wantBase && Tournament.View.BaseAttackActive(Tournament.Round.Config))
                {
                    string enemySide = TournamentController.SideName(Rules.TournamentRound.Opposite(me.Side));
                    if (!Tournament.Round.OpponentOf(me.Side).BaseDestroyed)
                    {
                        BasePanel panel = NearestPanel(enemySide, t.position);
                        if (panel != null)
                        {
                            m_Mode = "相手の陣地を攻撃";
                            Vector3 aim = panel.transform.position;
                            aim.y = 0f;
                            Vector3 to = aim - t.position;
                            to.y = 0f;
                            float dist = to.magnitude;
                            float bearing = SignedBearing(t, to);
                            if (dist > 6f)
                            {
                                return Steer(t, aim, 6f, blockedAhead, blockedLeft, blockedRight);
                            }
                            cmd.Turn = Mathf.Clamp(bearing / 30f, -1f, 1f);
                            cmd.Move = new Vector2(m_StrafeSign * 0.3f, 0f);
                            cmd.Fire = !Passive && Mathf.Abs(bearing) < FireConeDeg && dist < 7f;
                            return cmd;
                        }
                    }
                }
            }
            // プログラムの指示: 移動 / 退避 (撃たない。着いたら止まる) / 待機 (その場で射程内の相手を撃つ)
            if (Directive == BotDirective.Retreat || Directive == BotDirective.GoTo)
            {
                Vector3 goal = Directive == BotDirective.Retreat ? RetreatPoint() : DirectivePoint;
                Vector3 flat = goal - t.position;
                flat.y = 0f;
                if (flat.magnitude > 1.2f)
                {
                    m_Mode = (Directive == BotDirective.Retreat ? "退避" : "移動") + $" ({goal.x:F1},{goal.z:F1}) へ {flat.magnitude:F1} m";
                    return Steer(t, goal, 1.0f, blockedAhead, blockedLeft, blockedRight);
                }
                m_Mode = "到着して停止";
                return cmd;
            }
            if (Directive == BotDirective.Hold)
            {
                m_Mode = "待機";
                if (target != null)
                {
                    Vector3 toT = target.transform.position - t.position;
                    toT.y = 0f;
                    float distT = toT.magnitude;
                    float bearingT = SignedBearing(t, toT) + m_AimOffsetDeg;
                    cmd.Turn = Mathf.Clamp(bearingT / 30f, -1f, 1f);
                    bool clearT = !Physics.Raycast(t.position + Vector3.up * 0.45f, toT.normalized, distT - 0.8f,
                                                   ~((1 << Tuning.LayerDisc) | (1 << Tuning.LayerMinimapOnly) | (1 << Tuning.LayerRobot)));
                    cmd.Fire = !Passive && Mathf.Abs(bearingT - m_AimOffsetDeg) < FireConeDeg && distT < FireRange && clearT;
                    m_Mode = $"待機して {target.DisplayName} を狙う {distT:F1} m" + (clearT ? "" : " (遮られている)");
                }
                return cmd;
            }

            if (target == null)
            {
                // 狙う相手がいなければフィールド中央へ (経路で)。着いたら中央を向いて少し横に動きながら待つ
                Vector3 toCenter = -t.position;
                toCenter.y = 0f;
                m_Mode = "相手なし: 中央へ";
                if (toCenter.magnitude > 3f)
                {
                    return Steer(t, Vector3.zero, 3f, blockedAhead, blockedLeft, blockedRight);
                }
                m_Mode = "相手なし: 中央で待機";
                cmd.Turn = Mathf.Clamp(SignedBearing(t, toCenter) / 45f, -1f, 1f);
                cmd.Move = new Vector2(m_StrafeSign * 0.3f, 0f);
            }
            else
            {
                Vector3 to = target.transform.position - t.position;
                to.y = 0f;
                float dist = to.magnitude;
                float range = Directive == BotDirective.Engage ? DirectiveRange : PreferredRange;
                int losMask = ~((1 << Tuning.LayerDisc) | (1 << Tuning.LayerMinimapOnly) | (1 << Tuning.LayerRobot));
                Vector3 dir = to.normalized;
                // 射線 (装甲板の高さ) と、走って行ける線 (バンパーの高さとバンカーの高さ)。射線はバンカーや
                // ブロックの上を越えて通るので、それだけで直進すると構造物に突っ掛かってその場でうろつく
                bool clearLine = !Physics.Raycast(t.position + Vector3.up * 0.45f, dir, dist - 0.8f, losMask);
                bool clearDrive = clearLine &&
                                  !Physics.Raycast(t.position + Vector3.up * 0.15f, dir, dist - 0.8f, losMask) &&
                                  !Physics.Raycast(t.position + Vector3.up * 0.32f, dir, dist - 0.8f, losMask);
                if (dist > 9f || (!clearDrive && dist > 2.5f))
                {
                    // 遠い・真っ直ぐ行けないときは NavMesh の経路で近づく (スロープの先の谷などへ落ちない)
                    m_Mode = $"{target.DisplayName} へ接近 {dist:F1} m" + (clearDrive ? "" : " (直進不可)") + (blockedAhead ? " 前方に障害物" : "");
                    return Steer(t, target.transform.position, clearDrive ? range : 1.5f, blockedAhead, blockedLeft, blockedRight);
                }
                float bearing = SignedBearing(t, to) + m_AimOffsetDeg;
                cmd.Turn = Mathf.Clamp(bearing / 30f, -1f, 1f);
                m_Mode = $"{target.DisplayName} と交戦 {dist:F1} m" + (clearLine ? "" : " (遮られている)") + (blockedAhead ? " 前方に障害物" : "");

                float forward = 0f;
                if (dist > range + 1.5f) forward = 1f;
                else if (dist < range - 1.5f) forward = -0.6f;
                float strafe = Mathf.Abs(bearing) < 40f ? m_StrafeSign * 0.7f : 0f;
                cmd.Move = new Vector2(strafe, forward);
                cmd.Turbo = dist > range + 4f;

                bool aligned = Mathf.Abs(bearing - m_AimOffsetDeg) < FireConeDeg && dist < FireRange;
                cmd.Fire = !Passive && aligned && clearLine && Random.value < Mathf.Lerp(0.5f, 1f, Skill);
            }

            if (blockedAhead)
            {
                cmd.Move = new Vector2(blockedLeft && !blockedRight ? 1f : (blockedRight && !blockedLeft ? -1f : m_StrafeSign), -0.3f);
            }
            else if (blockedLeft && cmd.Move.x < 0f)
            {
                cmd.Move.x = 0.5f;
            }
            else if (blockedRight && cmd.Move.x > 0f)
            {
                cmd.Move.x = -0.5f;
            }
            return cmd;
        }

        /// <summary>
        /// 目標点へ向かう (到達判定は stopDistance)。NavMesh の経路があればその角を順に辿り、
        /// 無ければ直進する。壁に当たりそうなら横へ避ける。
        /// </summary>
        private RobotCommand Steer(Transform t, Vector3 goal, float stopDistance, bool blockedAhead, bool blockedLeft, bool blockedRight)
        {
            var cmd = new RobotCommand();
            Vector3 to = goal - t.position;
            to.y = 0f;
            if (to.magnitude < stopDistance) return cmd;
            Vector3 waypoint = NextWaypoint(t.position, goal);
            to = waypoint - t.position;
            to.y = 0f;
            float bearing = SignedBearing(t, to);
            cmd.Turn = Mathf.Clamp(bearing / 30f, -1f, 1f);
            cmd.Move = new Vector2(0f, Mathf.Abs(bearing) < 60f ? 1f : 0.2f);
            cmd.Turbo = to.magnitude > 6f;
            if (blockedAhead)
            {
                cmd.Move = new Vector2(blockedLeft && !blockedRight ? 1f : (blockedRight && !blockedLeft ? -1f : m_StrafeSign), 0.1f);
            }
            return cmd;
        }

        /// <summary>
        /// 経路上の次の角。目標が 1 m 以上動いたか 1 秒たったら経路を引き直す。
        /// 経路が取れない (NavMesh の外など) ときは目標そのものを返す。
        /// </summary>
        private Vector3 NextWaypoint(Vector3 from, Vector3 goal)
        {
            bool stale = Time.time - m_PathTime > 1f || (goal - m_PathGoal).sqrMagnitude > 1f;
            if (stale)
            {
                m_PathTime = Time.time;
                m_PathGoal = goal;
                m_PathIndex = 1;
                // 補給エリア (NavMesh の Supply 領域) は、ピットインを許可された機体だけが通れる
                bool granted = Tournament != null && Tournament.IsPitInGranted(Self.Index);
                int mask = granted ? NavMesh.AllAreas : NavMesh.AllAreas & ~(1 << NavigationBuilder.SupplyArea);
                bool ok = NavMesh.SamplePosition(from, out NavMeshHit a, 1.5f, NavMesh.AllAreas) &&
                          NavMesh.SamplePosition(goal, out NavMeshHit b, 2.0f, mask) &&
                          NavMesh.CalculatePath(a.position, b.position, mask, m_Path) &&
                          m_Path.status != NavMeshPathStatus.PathInvalid;
                m_PathOk = ok;
                if (!ok) m_Path.ClearCorners();
            }
            Vector3[] corners = m_Path.corners;
            if (corners == null || corners.Length < 2)
            {
                return goal;
            }
            // 近すぎる角は飛ばす
            while (m_PathIndex < corners.Length - 1 && Vector3.ProjectOnPlane(corners[m_PathIndex] - from, Vector3.up).magnitude < 0.6f)
            {
                m_PathIndex++;
            }
            return corners[Mathf.Clamp(m_PathIndex, 0, corners.Length - 1)];
        }

        private BasePanel NearestPanel(string side, Vector3 from)
        {
            BasePanel best = null;
            float bestDist = float.MaxValue;
            foreach (BasePanel p in Tournament.Field.PanelsOf(side))
            {
                if (p.High) continue;   // 低い位置の板 (4 枚) を狙う
                float d = (p.transform.position - from).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = p; }
            }
            return best;
        }

        /// <summary>
        /// 狙う相手。いちばん近い敵のアタッカー / ヘッド。ビルダーは近く (<see cref="BuilderEngageRange"/>) に来たときだけ、
        /// または敵にアタッカーがいないときだけ狙う。
        /// </summary>
        /// <summary>自同盟のスタートゾーン (大会ルール) か開始位置。</summary>
        private Vector3 RetreatPoint()
        {
            if (Tournament != null && Tournament.Field != null && Tournament.Round != null)
            {
                Rules.TournamentRobot me = Tournament.Round.Find(Self.Index);
                if (me != null) return Tournament.Field.Spawn(TournamentController.SideName(me.Side), 0).position;
            }
            return Self.StartPosition;
        }

        private bool IsEnemy(RobotAgent r)
        {
            if (r == null || r == Self || r.IsKnockedOut) return false;
            if (GameDirector.Instance != null && (GameDirector.Instance.IsTeamMatch || Tournament != null) && r.Team == Self.Team) return false;
            return true;
        }

        /// <summary>プログラムの狙いの選び方 (最寄り以外)。優先する種別がいなければ最寄り。</summary>
        private RobotAgent ByPolicy(Vector3 from, IReadOnlyList<RobotAgent> robots)
        {
            RobotAgent best = null;
            float bestScore = float.MaxValue;
            foreach (RobotAgent r in robots)
            {
                if (!IsEnemy(r)) continue;
                float d = (r.transform.position - from).magnitude;
                float score = d;
                switch (TargetPolicy)
                {
                    case Rules.AutoTarget.Weakest: score = (r.Health != null ? r.Health.Hp : 999) * 100f + d; break;
                    case Rules.AutoTarget.Head: if (!r.IsHeadAttacker) score += 1000f; break;
                    case Rules.AutoTarget.Builder: if (!r.IsBuilder) score += 1000f; break;
                    case Rules.AutoTarget.Attacker: if (r.IsBuilder) score += 1000f; break;
                }
                if (score < bestScore) { bestScore = score; best = r; }
            }
            m_Target = best;
            return best;
        }

        private RobotAgent Nearest(Vector3 from)
        {
            IReadOnlyList<RobotAgent> robots = Robots != null ? Robots() : null;
            if (robots == null)
            {
                m_Target = null;
                return null;
            }
            if (TargetPolicy != Rules.AutoTarget.Nearest) return ByPolicy(from, robots);
            RobotAgent best = null;
            float bestDist = float.MaxValue;
            RobotAgent nearBuilder = null;
            float nearBuilderDist = float.MaxValue;
            bool anyFighter = false;
            foreach (RobotAgent r in robots)
            {
                if (r == null || r == Self || r.IsKnockedOut)
                {
                    continue;
                }
                if (GameDirector.Instance != null && (GameDirector.Instance.IsTeamMatch || Tournament != null) && r.Team == Self.Team)
                {
                    continue;
                }
                float d = (r.transform.position - from).sqrMagnitude;
                if (r.IsBuilder)
                {
                    if (d < nearBuilderDist) { nearBuilderDist = d; nearBuilder = r; }
                    continue;
                }
                anyFighter = true;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = r;
                }
            }
            if (nearBuilder != null)
            {
                float keep = m_Target == nearBuilder ? BuilderKeepRange : BuilderEngageRange;
                bool engage = !anyFighter || nearBuilderDist < keep * keep;
                if (engage && nearBuilderDist < bestDist)
                {
                    best = nearBuilder;
                }
            }
            m_Target = best;
            return best;
        }

        /// <summary>目標方向の方位角 [deg]。右が正。</summary>
        private static float SignedBearing(Transform t, Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-6f)
            {
                return 0f;
            }
            return Vector3.SignedAngle(t.forward, dir.normalized, Vector3.up);
        }

        /// <summary>
        /// 前方に壁や段差があるか。登れる斜面 (法線が十分上を向く面) は障害物にしない。
        /// レイは床から 0.15 m の高さなので、スロープの面にも当たるが、それを壁扱いすると
        /// CPU は斜面の手前で横に逃げ続けて登らない。
        /// </summary>
        private static bool Blocked(Transform t, Vector3 dir, float range)
        {
            int mask = ~((1 << Tuning.LayerDisc) | (1 << Tuning.LayerMinimapOnly) | (1 << Tuning.LayerRobot));
            if (!Physics.Raycast(t.position + Vector3.up * 0.15f, dir, out RaycastHit hit, range, mask))
            {
                return false;
            }
            return hit.normal.y < 0.45f;   // 約 63 度より急な面 (壁・段差) だけを障害物とする
        }

        /// <summary>
        /// 最後の砦: 戦闘中の後退・横移動・脱出は目標点を持たないので、進む先が禁止領域
        /// (許可のない自同盟の補給エリア、相手の補給エリア、進入禁止ゾーン) なら、その領域から
        /// 離れる向きの指令に置き換える。スタートゾーンのそばで撃ち合うとフェンスの切れ目から
        /// 補給エリアへ後退して反則になっていた。
        /// </summary>
        private RobotCommand GuardZones(RobotCommand cmd)
        {
            if (Tournament == null || Tournament.Field == null || Tournament.Round == null) return cmd;
            Transform t = Self.transform;
            Vector3 dir = t.right * cmd.Move.x + t.forward * cmd.Move.y;
            dir.y = 0f;
            Rules.TournamentRobot me = Tournament.Round.Find(Self.Index);
            bool granted = me != null && me.PitInGranted;
            string own = me != null ? TournamentController.SideName(me.Side) : "";
            string enemy = me != null ? TournamentController.SideName(Rules.TournamentRound.Opposite(me.Side)) : "";

            // 現在地と、1.5 m 先 (指令方向) の 2 点を見る
            Vector3 ahead = t.position + (dir.sqrMagnitude > 1e-4f ? dir.normalized * 1.5f : Vector3.zero);
            Vector3 center;
            bool forbidden = Forbidden(ahead, own, enemy, granted, out center) || Forbidden(t.position, own, enemy, granted, out center);
            if (!forbidden) return cmd;

            Vector3 away = t.position - center;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = -t.forward;
            away.Normalize();
            return new RobotCommand
            {
                Move = new Vector2(Vector3.Dot(away, t.right), Vector3.Dot(away, t.forward)) * 0.8f,
                Turn = cmd.Turn,
                Fire = cmd.Fire,
                Turbo = false,
            };
        }

        private bool Forbidden(Vector3 p, string own, string enemy, bool granted, out Vector3 center)
        {
            Field2027Data f = Tournament.Field;
            center = Vector3.zero;
            if (!granted && f.InSupplyZone(own, p)) { center = f.SupplyPoint(own); return true; }
            if (f.InSupplyZone(enemy, p)) { center = f.SupplyPoint(enemy); return true; }
            if (f.InNoEntryZone(p))
            {
                foreach (PolyZoneDef z in f.Manifest.noEntryZones)
                {
                    if (Field2027Data.InsidePolygon(z.xz, p.x, p.z)) { center = new Vector3(z.cx, 0f, z.cz); break; }
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// 前へ進もうとしているのに速度が出ない状態が続いたら、少し下がって向きを変える。
        /// 段差の角や構造物の隙間に引っ掛かったまま押し続けて動けなくなるのを防ぐ。
        /// </summary>
        private RobotCommand Unstick(RobotCommand cmd)
        {
            float now = Time.time;
            if (now < m_EscapeUntil)
            {
                return new RobotCommand { Move = new Vector2(0f, -0.8f), Turn = m_EscapeTurn, Turbo = false };
            }
            bool wantsToMove = cmd.Move.sqrMagnitude > 0.1f;
            float speed = Self.Drive != null ? Self.Drive.Speed : 1f;
            if (wantsToMove && speed < 0.08f && !Self.Frozen)
            {
                if (m_StuckSince < 0f) m_StuckSince = now;
                else if (now - m_StuckSince > 1.5f)
                {
                    m_StuckSince = -1f;
                    m_EscapeUntil = now + Random.Range(1.0f, 1.8f);
                    m_EscapeTurn = Random.value < 0.5f ? -1f : 1f;
                }
            }
            else
            {
                m_StuckSince = -1f;
            }
            return cmd;
        }
    }
}
