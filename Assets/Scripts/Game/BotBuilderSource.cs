using System.Collections.Generic;
using CoreSim.Rules;
using UnityEngine;
using UnityEngine.AI;

namespace CoreSim
{
    /// <summary>
    /// CPU のビルダー。手順は「ブロックを 2 個ビルドサイトに積む → 置き場 A と共通のコンテナをスポットへ →
    /// ビルドサイト 3・4 を獲得して置き場 B・C のコンテナも運ぶ」。
    /// 運搬は (1) 物の正面に立つ → (2) ハンドの高さを合わせてつかむ → (3) 置き場所の正面へ → (4) 高さを合わせて置く、
    /// の繰り返し。移動は NavMesh の経路、正面の位置は構造物に当たらない向きをレイで探す。
    /// </summary>
    public sealed class BotBuilderSource : ICommandSource, IBotStatus
    {
        public RobotAgent Self;
        public System.Func<IReadOnlyList<RobotAgent>> Robots;
        public TournamentController Tournament;

        private enum Phase { Plan, GoToPickup, Pickup, GoToPlace, Place, Wait }

        private Phase m_Phase = Phase.Plan;
        private FieldProp m_Target;
        private PropSlot m_Dest;
        private Vector3 m_Stand;
        private Quaternion m_Face;
        private float m_PhaseSince;
        private float m_NextThinkTime;
        private RobotCommand m_Current;
        private readonly NavMeshPath m_Path = new NavMeshPath();
        private Vector3 m_PathGoal;
        private float m_PathTime = -10f;
        private int m_PathIndex;
        private float m_StuckSince = -1f;
        private float m_EscapeUntil = -1f;
        private float m_EscapeTurn = 1f;
        private bool m_GrabPulse;
        private readonly HashSet<FieldProp> m_Failed = new HashSet<FieldProp>();

        private PropRegistry Registry => Self != null && Self.Lift != null ? Self.Lift.Registry : null;

        /// <summary>F9 のデバッグ表示: 段階、目標、速度。</summary>
        public string Status
        {
            get
            {
                float speed = Self != null && Self.Drive != null ? Self.Drive.Speed : 0f;
                string target = m_Target != null ? m_Target.Label : "-";
                string dest = m_Dest != null ? m_Dest.Label : "-";
                string held = Self != null && Self.Lift != null && Self.Lift.Held != null ? Self.Lift.Held.Label : "なし";
                string path = m_Path.corners != null && m_Path.corners.Length >= 2 ? $"経路 {m_Path.corners.Length} 角" : "経路 なし";
                string escape = Time.time < m_EscapeUntil ? "  [脱出中]" : "";
                return $"{m_Phase}  目標 {target} → {dest}  持ち物 {held}  {path}  {speed:F2} m/s{escape}";
            }
        }
        private string MySide => Self.Team == Team.Red ? "red" : "blue";
        private Side MyRuleSide => Self.Team == Team.Red ? Side.Red : Side.Blue;

        public RobotCommand Read()
        {
            if (Self == null || Self.Lift == null || Registry == null) return RobotCommand.None;
            if (Time.time < m_NextThinkTime) return m_Current;
            m_NextThinkTime = Time.time + 0.1f;
            m_Current = Unstick(Think());
            return m_Current;
        }

        private RobotCommand Think()
        {
            if (Self.Frozen || Self.IsKnockedOut) { m_Phase = Phase.Plan; return RobotCommand.None; }
            BuilderLift lift = Self.Lift;
            float now = Time.time;

            // 持っているはずの物が無い (落とした / ラウンドが変わった) ときは計画し直す
            if ((m_Phase == Phase.GoToPlace || m_Phase == Phase.Place) && lift.Held == null) m_Phase = Phase.Plan;
            if ((m_Phase == Phase.GoToPickup || m_Phase == Phase.Pickup) && (m_Target == null || m_Target.HeldBy != null)) m_Phase = Phase.Plan;

            switch (m_Phase)
            {
                case Phase.Plan:
                    if (lift.Held != null)
                    {
                        // 何かを持ったまま計画に戻った: 置き場所を決める
                        m_Dest = ChooseDestination(lift.Held);
                        if (m_Dest == null) { return Pulse(RobotCommand.None, grab: true); }   // 置き場が無ければ落とす
                        SetStand(m_Dest.NextBottom, m_Dest.Rotation, 0.75f);
                        Enter(Phase.GoToPlace);
                        break;
                    }
                    if (!Plan()) { Enter(Phase.Wait); break; }
                    Enter(Phase.GoToPickup);
                    break;

                case Phase.Wait:
                    // 何もできないときは自同盟のスポットの近くで待つ (邪魔にならない所)。5 秒ごとに計画し直す
                    if (now - m_PhaseSince > 5f) Enter(Phase.Plan);
                    return Steer(Self.transform, Self.transform.position, 10f);

                case Phase.GoToPickup:
                {
                    RobotCommand cmd = Approach(out bool arrived);
                    if (arrived) Enter(Phase.Pickup);
                    if (now - m_PhaseSince > 40f) { m_Failed.Add(m_Target); Enter(Phase.Plan); }
                    return cmd;
                }
                case Phase.Pickup:
                {
                    // 高さを合わせ、ハンドの候補が目標の物になったらつかむ
                    float want = Mathf.Clamp(m_Target.transform.position.y, BuilderLift.MinHeight, BuilderLift.MaxHeight);
                    RobotCommand cmd = Nudge(m_Target.transform.position);
                    cmd.Lift = LiftToward(want);
                    if (Mathf.Abs(lift.Height - want) < 0.03f && lift.Candidate == m_Target)
                    {
                        cmd = Pulse(cmd, grab: true);
                        if (lift.Held == m_Target)
                        {
                            m_Dest = ChooseDestination(m_Target);
                            if (m_Dest == null) { Enter(Phase.Plan); break; }
                            SetStand(m_Dest.NextBottom, m_Dest.Rotation, 0.75f);
                            Enter(Phase.GoToPlace);
                        }
                    }
                    if (now - m_PhaseSince > 12f) { m_Failed.Add(m_Target); Enter(Phase.Plan); }
                    return cmd;
                }
                case Phase.GoToPlace:
                {
                    // 持ち上げて運ぶ (床の物に引っ掛からない高さ)
                    RobotCommand cmd = Approach(out bool arrived);
                    cmd.Lift = LiftToward(Mathf.Max(0.35f, Mathf.Min(BuilderLift.MaxHeight, m_Dest.NextBottom.y + lift.Held.Size.y / 2f + 0.05f)));
                    if (arrived) Enter(Phase.Place);
                    if (now - m_PhaseSince > 40f) Enter(Phase.Plan);
                    return cmd;
                }
                case Phase.Place:
                {
                    if (m_Dest.Full) { Enter(Phase.Plan); break; }
                    float want = Mathf.Clamp(m_Dest.NextBottom.y + lift.Held.Size.y / 2f + 0.02f, BuilderLift.MinHeight, BuilderLift.MaxHeight);
                    RobotCommand cmd = Nudge(m_Dest.NextBottom + Vector3.up * (lift.Held.Size.y / 2f));
                    cmd.Lift = LiftToward(want);
                    if (Mathf.Abs(lift.Height - want) < 0.03f && lift.PlaceTarget == m_Dest)
                    {
                        cmd = Pulse(cmd, grab: true);
                        if (lift.Held == null) Enter(Phase.Plan);
                    }
                    if (now - m_PhaseSince > 12f)
                    {
                        // 置けないなら一度下がってやり直す
                        SetStand(m_Dest.NextBottom, m_Dest.Rotation, 0.75f);
                        Enter(Phase.GoToPlace);
                    }
                    return cmd;
                }
            }
            return RobotCommand.None;
        }

        private void Enter(Phase phase)
        {
            m_Phase = phase;
            m_PhaseSince = Time.time;
            m_GrabPulse = false;
        }

        /// <summary>つかむ / 放すは立ち上がりで効くので、1 回だけ true を出す。</summary>
        private RobotCommand Pulse(RobotCommand cmd, bool grab)
        {
            cmd.Grab = grab && !m_GrabPulse;
            m_GrabPulse = grab;
            return cmd;
        }

        private float LiftToward(float want)
        {
            float d = want - Self.Lift.Height;
            if (Mathf.Abs(d) < 0.015f) return 0f;
            return Mathf.Clamp(d / 0.1f, -1f, 1f);
        }

        // ------------------------------------------------------------------ 計画

        /// <summary>次に運ぶ物を決める。戻り値 false = 今は何もない。</summary>
        private bool Plan()
        {
            m_Target = null;
            m_Dest = null;
            if (Tournament == null || Tournament.Round == null) return PlanCasual();
            TournamentAlliance me = Tournament.Round.Of(MyRuleSide);
            // 1. ビルドサイト 1 (自分の防御)、3、4 (置き場 B・C の開放)、2 (陣地の防御) の順に 2 個ずつ
            foreach (int site in new[] { 1, 3, 4, 2 })
            {
                if (me.SiteAcquired(site)) continue;
                PropSlot slot = Registry.Slot(PlaceKind.Site, MySide, site);
                if (slot == null || slot.Full) continue;
                FieldProp block = NearestFreeBlock(slot.Position);
                if (block == null) break;
                // コンテナを先に運んだ方が点になるので、A と共通が残っていればそちらを優先 (サイト 1 だけは先)
                if (site != 1 && NearestContainer(me, slot.Position, allowBC: false) != null) break;
                m_Target = block;
                m_Dest = slot;
                SetStand(block.transform.position, block.transform.rotation, 0.7f);
                return true;
            }
            // 2. コンテナを空いているスポットへ
            PropSlot spot = NextSpot();
            if (spot != null)
            {
                FieldProp c = NearestContainer(me, spot.Position, allowBC: true);
                if (c != null)
                {
                    m_Target = c;
                    m_Dest = spot;
                    SetStand(c.transform.position, c.transform.rotation, 0.7f);
                    return true;
                }
            }
            // 3. 残ったブロックは防御ポートへ
            foreach (PropSlot port in Registry.SlotsOf(PlaceKind.Port, MySide))
            {
                if (port.Full) continue;
                FieldProp block = NearestFreeBlock(port.Position);
                if (block == null) break;
                m_Target = block;
                m_Dest = port;
                SetStand(block.transform.position, block.transform.rotation, 0.7f);
                return true;
            }
            return false;
        }

        /// <summary>かんたんルール: ブロックをビルドサイトに積むだけ (得点は無い)。</summary>
        private bool PlanCasual()
        {
            foreach (PropSlot slot in Registry.SlotsOf(PlaceKind.Site, MySide))
            {
                if (slot.Full) continue;
                FieldProp block = NearestFreeBlock(slot.Position);
                if (block == null) return false;
                m_Target = block;
                m_Dest = slot;
                SetStand(block.transform.position, block.transform.rotation, 0.7f);
                return true;
            }
            return false;
        }

        private PropSlot ChooseDestination(FieldProp held)
        {
            if (held.Kind == PropKind.Container)
            {
                return NextSpot();
            }
            if (Tournament != null && Tournament.Round != null)
            {
                TournamentAlliance me = Tournament.Round.Of(MyRuleSide);
                foreach (int site in new[] { 1, 3, 4, 2 })
                {
                    PropSlot slot = Registry.Slot(PlaceKind.Site, MySide, site);
                    if (slot != null && !slot.Full && !me.SiteAcquired(site)) return slot;
                }
            }
            foreach (PropSlot slot in Registry.SlotsOf(PlaceKind.Site, MySide)) if (!slot.Full) return slot;
            foreach (PropSlot port in Registry.SlotsOf(PlaceKind.Port, MySide)) if (!port.Full) return port;
            return null;
        }

        /// <summary>次に埋めるスポット: 下段の 1 列目から順 (4 個並びの得点が早く入る)。</summary>
        private PropSlot NextSpot()
        {
            for (int i = 1; i <= TournamentConfig.SpotCount; i++)
            {
                PropSlot s = Registry.Slot(PlaceKind.Spot, MySide, i);
                if (s != null && !s.Full) return s;
            }
            return null;
        }

        private FieldProp NearestFreeBlock(Vector3 from)
        {
            FieldProp best = null;
            float bestDist = float.MaxValue;
            foreach (FieldProp p in Registry.Props)
            {
                if (p == null || p.Kind != PropKind.Block || p.HeldBy != null || m_Failed.Contains(p)) continue;
                if (p.Slot != null) continue;                                   // 置いた物は取らない (谷部・ポートも)
                if (p.AtHome && p.HomeSide != MySide) continue;                 // 相手のブロックゾーンまでは行かない
                float d = (p.transform.position - from).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = p; }
            }
            return best;
        }

        private FieldProp NearestContainer(TournamentAlliance me, Vector3 from, bool allowBC)
        {
            FieldProp best = null;
            float bestDist = float.MaxValue;
            foreach (FieldProp p in Registry.Props)
            {
                if (p == null || p.Kind != PropKind.Container || p.HeldBy != null || p.Slot != null || m_Failed.Contains(p)) continue;
                if (p.ColorSide.HasValue && p.ColorSide.Value != MyRuleSide) continue;
                if (p.Origin == ContainerOrigin.HolderB && (!allowBC || !me.SiteAcquired(3))) continue;
                if (p.Origin == ContainerOrigin.HolderC && (!allowBC || !me.SiteAcquired(4))) continue;
                float d = (p.transform.position - from).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = p; }
            }
            return best;
        }

        // ------------------------------------------------------------------ 位置取り

        /// <summary>
        /// 目標 (物または置き場所) の正面に立つ位置を決める。目標の 4 方向のうち、構造物に当たらず
        /// NavMesh の上にある向きを選び、その向きに distance だけ離れた点を立ち位置、目標を向く回転を目標の向きにする。
        /// </summary>
        private void SetStand(Vector3 target, Quaternion rotation, float distance)
        {
            Vector3 best = Vector3.zero;
            float bestScore = float.MinValue;
            Vector3 flatTarget = new Vector3(target.x, 0f, target.z);
            Vector3 me = Self.transform.position;
            for (int i = 0; i < 8; i++)
            {
                Vector3 dir = rotation * Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                dir.y = 0f;
                dir.Normalize();
                Vector3 stand = flatTarget + dir * distance;
                // 立ち位置が NavMesh の上にあり、目標との間に構造物が無いこと
                if (!NavMesh.SamplePosition(stand, out NavMeshHit hit, 0.4f, NavMesh.AllAreas)) continue;
                int mask = ~((1 << Tuning.LayerDisc) | (1 << Tuning.LayerMinimapOnly) | (1 << Tuning.LayerRobot) | (1 << Tuning.LayerProp));
                if (Physics.Raycast(new Vector3(target.x, Mathf.Max(target.y, 0.1f) + 0.1f, target.z), dir, distance - 0.1f, mask)) continue;
                float score = -(hit.position - me).magnitude - (i % 2 == 1 ? 0.5f : 0f);   // 近い方、できれば正面 4 方向
                if (score > bestScore) { bestScore = score; best = new Vector3(hit.position.x, 0f, hit.position.z); }
            }
            if (bestScore == float.MinValue)
            {
                Vector3 dir = (me - flatTarget);
                dir.y = 0f;
                best = flatTarget + (dir.sqrMagnitude > 1e-3f ? dir.normalized : Vector3.forward) * distance;
            }
            m_Stand = best;
            Vector3 look = flatTarget - best;
            look.y = 0f;
            m_Face = look.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(look, Vector3.up) : rotation;
        }

        /// <summary>立ち位置まで経路で向かい、着いたら目標を向く。</summary>
        private RobotCommand Approach(out bool arrived)
        {
            Transform t = Self.transform;
            Vector3 to = m_Stand - t.position;
            to.y = 0f;
            arrived = false;
            if (to.magnitude > 0.35f)
            {
                return Steer(t, m_Stand, 0.3f);
            }
            float bearing = Vector3.SignedAngle(t.forward, m_Face * Vector3.forward, Vector3.up);
            var cmd = new RobotCommand { Turn = Mathf.Clamp(bearing / 25f, -1f, 1f) };
            cmd.Move = new Vector2(Vector3.Dot(to, t.right), Vector3.Dot(to, t.forward)) * 2f;
            cmd.Move = Vector2.ClampMagnitude(cmd.Move, 0.4f);
            arrived = Mathf.Abs(bearing) < 6f && to.magnitude < 0.2f;
            return cmd;
        }

        /// <summary>目標点がハンドの真下 / 真上に来るように、少しずつ前後左右に動く (向きは保つ)。</summary>
        private RobotCommand Nudge(Vector3 target)
        {
            Transform t = Self.transform;
            Vector3 hand = Self.Lift.Hand.position;
            Vector3 d = target - hand;
            d.y = 0f;
            var cmd = new RobotCommand();
            if (d.magnitude > 0.04f)
            {
                cmd.Move = Vector2.ClampMagnitude(new Vector2(Vector3.Dot(d, t.right), Vector3.Dot(d, t.forward)) * 3f, 0.35f);
            }
            float bearing = Vector3.SignedAngle(t.forward, m_Face * Vector3.forward, Vector3.up);
            cmd.Turn = Mathf.Clamp(bearing / 25f, -1f, 1f);
            return cmd;
        }

        private RobotCommand Steer(Transform t, Vector3 goal, float stopDistance)
        {
            var cmd = new RobotCommand();
            Vector3 to = goal - t.position;
            to.y = 0f;
            if (to.magnitude < stopDistance) return cmd;
            Vector3 waypoint = NextWaypoint(t.position, goal);
            to = waypoint - t.position;
            to.y = 0f;
            float bearing = Vector3.SignedAngle(t.forward, to.normalized, Vector3.up);
            cmd.Turn = Mathf.Clamp(bearing / 30f, -1f, 1f);
            cmd.Move = new Vector2(0f, Mathf.Abs(bearing) < 60f ? 1f : 0.2f);
            cmd.Turbo = to.magnitude > 6f && Self.Lift.Held == null;
            if (Blocked(t, t.forward, 1.2f))
            {
                bool left = Blocked(t, Quaternion.Euler(0f, -35f, 0f) * t.forward, 1.2f);
                bool right = Blocked(t, Quaternion.Euler(0f, 35f, 0f) * t.forward, 1.2f);
                cmd.Move = new Vector2(left && !right ? 1f : (right && !left ? -1f : 1f), 0.1f);
            }
            return cmd;
        }

        private Vector3 NextWaypoint(Vector3 from, Vector3 goal)
        {
            bool stale = Time.time - m_PathTime > 1f || (goal - m_PathGoal).sqrMagnitude > 0.25f;
            if (stale)
            {
                m_PathTime = Time.time;
                m_PathGoal = goal;
                m_PathIndex = 1;
                int mask = NavMesh.AllAreas & ~(1 << NavigationBuilder.SupplyArea);
                bool ok = NavMesh.SamplePosition(from, out NavMeshHit a, 1.5f, NavMesh.AllAreas) &&
                          NavMesh.SamplePosition(goal, out NavMeshHit b, 1.0f, mask) &&
                          NavMesh.CalculatePath(a.position, b.position, mask, m_Path) &&
                          m_Path.status != NavMeshPathStatus.PathInvalid;
                if (!ok) m_Path.ClearCorners();
            }
            Vector3[] corners = m_Path.corners;
            if (corners == null || corners.Length < 2) return goal;
            while (m_PathIndex < corners.Length - 1 && Vector3.ProjectOnPlane(corners[m_PathIndex] - from, Vector3.up).magnitude < 0.5f)
            {
                m_PathIndex++;
            }
            return corners[Mathf.Clamp(m_PathIndex, 0, corners.Length - 1)];
        }

        private static bool Blocked(Transform t, Vector3 dir, float range)
        {
            int mask = ~((1 << Tuning.LayerDisc) | (1 << Tuning.LayerMinimapOnly) | (1 << Tuning.LayerRobot));
            if (!Physics.Raycast(t.position + Vector3.up * 0.15f, dir, out RaycastHit hit, range, mask)) return false;
            if (hit.collider.GetComponentInParent<RobotAgent>() == t.GetComponent<RobotAgent>()) return false;   // 自分の荷物
            return hit.normal.y < 0.45f;
        }

        private RobotCommand Unstick(RobotCommand cmd)
        {
            float now = Time.time;
            if (now < m_EscapeUntil)
            {
                return new RobotCommand { Move = new Vector2(0f, -0.8f), Turn = m_EscapeTurn, Lift = cmd.Lift };
            }
            bool wantsToMove = cmd.Move.sqrMagnitude > 0.3f;
            float speed = Self.Drive != null ? Self.Drive.Speed : 1f;
            if (wantsToMove && speed < 0.08f && !Self.Frozen && (m_Phase == Phase.GoToPickup || m_Phase == Phase.GoToPlace))
            {
                if (m_StuckSince < 0f) m_StuckSince = now;
                else if (now - m_StuckSince > 2f)
                {
                    m_StuckSince = -1f;
                    m_EscapeUntil = now + Random.Range(0.8f, 1.5f);
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
