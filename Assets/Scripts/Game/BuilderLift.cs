using UnityEngine;

namespace CoreSim
{
    /// <summary>
    /// ビルダーの昇降ハンド (小型のトップリフター)。マストに沿って上下するキャリッジの先に
    /// ハンドがあり、ブロックとコンテナを 1 つ持てる。つかむと物体はハンドに固定され (物理なし)、
    /// 放すと置き場所が近ければそこに揃えて置き、無ければその場に落とす。
    /// </summary>
    /// <remarks>
    /// 関節や把持力の物理は使わない。実機の「つかむ・持ち上げる・置く」の手順と見た目を再現しつつ、
    /// 物理の不安定さ (すり抜け・はじき飛び) を避けるため。
    /// </remarks>
    public sealed class BuilderLift : MonoBehaviour
    {
        public const float MinHeight = 0.10f;     // ハンド中心の床からの高さ。ブロック (高さ 0.2) を床から持てる
        public const float MaxHeight = 1.00f;     // スポット上段 (0.85) にコンテナ (0.15) を置ける
        public const float LiftSpeed = 0.45f;     // m/s
        public const float GrabRange = 0.22f;     // ハンド中心から物体中心まで (水平)。爪の間にほぼ収まっている必要がある
        public const float GrabHeightTolerance = 0.10f;   // ハンド中心と物体中心の高さの差
        public const float PlaceRange = 0.15f;    // 持っている物体の底面から置き場所まで (水平)。半分は重なっている必要がある
        public const float PlaceHeightTolerance = 0.12f;   // 底面と置き場所の高さの差。スポットの段の間隔 (0.275) より小さく

        public RobotAgent Owner;
        public Transform Carriage;   // マストに沿って動く部分 (カメラも載る)
        public Transform Hand;       // 持った物体の中心
        public PropRegistry Registry;

        public float Height { get; private set; } = MinHeight;
        public FieldProp Held { get; private set; }
        /// <summary>今つかめる物体 (無ければ null) と、つかむと反則になる理由。</summary>
        public FieldProp Candidate { get; private set; }
        public string CandidateWarning { get; private set; }
        /// <summary>放すと置ける場所 (無ければ null = その場に落とす) と、置くと反則になる理由。</summary>
        public PropSlot PlaceTarget { get; private set; }
        public string PlaceWarning { get; private set; }

        private bool m_GrabHeld;
        private float m_CarriageBase;
        private readonly Collider[] m_Hits = new Collider[16];

        public void Setup(RobotAgent owner, Transform carriage, Transform hand, float carriageBaseY)
        {
            Owner = owner;
            Carriage = carriage;
            Hand = hand;
            m_CarriageBase = carriageBaseY;
            ApplyHeight();
        }

        /// <summary>クライアント側の写し: 高さと持ち物はスナップショットから。</summary>
        public void ApplyRemote(float height, FieldProp held)
        {
            Height = Mathf.Clamp(height, MinHeight, MaxHeight);
            ApplyHeight();
            if (held != Held)
            {
                if (Held != null && Held.HeldBy == Owner) Held.Drop(Vector3.zero);
                Held = held;
                if (Held != null) Held.Hold(Hand, Owner);
            }
            UpdateCandidates();
        }

        private void ApplyHeight()
        {
            if (Carriage != null)
            {
                Vector3 p = Carriage.localPosition;
                p.y = m_CarriageBase + (Height - MinHeight);
                Carriage.localPosition = p;
            }
        }

        /// <summary>毎物理フレーム、操縦指令から昇降とつかむ / 放すを処理する (ホスト / ローカル)。</summary>
        public void Tick(RobotCommand cmd, bool canAct)
        {
            if (Held != null && Held.HeldBy != Owner) Held = null;   // ラウンド開始などで物が開始位置に戻された
            if (canAct && Mathf.Abs(cmd.Lift) > 0.05f)
            {
                Height = Mathf.Clamp(Height + cmd.Lift * LiftSpeed * Time.fixedDeltaTime, MinHeight, MaxHeight);
                ApplyHeight();
            }
            UpdateCandidates();
            bool grab = cmd.Grab && canAct;
            if (grab && !m_GrabHeld)
            {
                if (Held == null) TryGrab();
                else Release();
            }
            m_GrabHeld = grab;
            if (!canAct && Held != null && Owner != null && Owner.IsKnockedOut)
            {
                Release(forceDrop: true);   // 撃破されたら落とす (14.4 の牽引解除と同じ考え方)
            }
        }

        /// <summary>持ち物を手放す (開始位置へ戻すときなど)。</summary>
        public void Clear()
        {
            Held = null;
            m_GrabHeld = false;
        }

        private void UpdateCandidates()
        {
            Candidate = null;
            CandidateWarning = null;
            PlaceTarget = null;
            PlaceWarning = null;
            if (Hand == null || Registry == null) return;
            var tc = GameDirector.Instance != null ? GameDirector.Instance.Tournament : null;
            if (Held == null)
            {
                // 爪の間に入っている物だけ: 水平に GrabRange 以内、高さの差が GrabHeightTolerance 以内
                float radius = Mathf.Sqrt(GrabRange * GrabRange + GrabHeightTolerance * GrabHeightTolerance);
                int n = Physics.OverlapSphereNonAlloc(Hand.position, radius, m_Hits, 1 << FieldProp.Layer, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    var prop = m_Hits[i].GetComponent<FieldProp>();
                    if (prop == null || prop.HeldBy != null || (prop.Remote && prop.RemoteHeld)) continue;
                    Vector3 d = prop.transform.position - Hand.position;
                    if (Mathf.Abs(d.y) > GrabHeightTolerance) continue;
                    d.y = 0f;
                    float flat = d.sqrMagnitude;
                    if (flat > GrabRange * GrabRange || flat >= best) continue;
                    best = flat;
                    Candidate = prop;
                }
                if (Candidate != null && tc != null && Owner != null) CandidateWarning = tc.PickupWarning(Owner, Candidate);
            }
            else
            {
                PlaceTarget = Registry.NearestOpenSlot(Held.Kind, Held.Bottom, PlaceRange, PlaceHeightTolerance);
                if (PlaceTarget != null && tc != null && Owner != null) PlaceWarning = tc.PlaceWarning(Owner, Held, PlaceTarget);
            }
        }

        private void TryGrab()
        {
            if (Candidate == null || Owner == null) return;
            FieldProp prop = Candidate;
            var tc = GameDirector.Instance != null ? GameDirector.Instance.Tournament : null;
            tc?.OnPickUp(Owner, prop);   // 反則の判定 (持つ前の状態で)
            prop.Hold(Hand, Owner);
            Held = prop;
            GameDirector.Instance?.Audio.PlayClick();
        }

        private void Release(bool forceDrop = false)
        {
            if (Held == null) return;
            FieldProp prop = Held;
            Held = null;
            var tc = GameDirector.Instance != null ? GameDirector.Instance.Tournament : null;
            if (PlaceTarget != null && !forceDrop)
            {
                PropSlot slot = PlaceTarget;
                prop.PlaceIn(slot);
                tc?.OnPlaced(Owner, prop, slot);
                GameDirector.Instance?.Audio.PlayClick();
            }
            else
            {
                Vector3 v = Owner != null && Owner.GetComponent<Rigidbody>() != null ? Owner.GetComponent<Rigidbody>().linearVelocity : Vector3.zero;
                prop.Drop(v);
            }
            UpdateCandidates();
        }

        /// <summary>HUD 用の案内文。</summary>
        public string Hint(string grabKey)
        {
            if (Held == null)
            {
                if (Candidate == null) return $"ハンド {Height:F2} m   近くの物を {grabKey} でつかむ";
                string warn = CandidateWarning != null ? $"  ※反則: {CandidateWarning}" : "";
                return $"{Candidate.Label} を {grabKey} でつかむ{warn}";
            }
            if (PlaceTarget == null) return $"{Held.Label} を持っている   {grabKey} でその場に落とす";
            string pw = PlaceWarning != null ? $"  ※反則: {PlaceWarning}" : "";
            return $"{Held.Label} を {grabKey} で {PlaceTarget.Label} に置く{pw}";
        }
    }
}
