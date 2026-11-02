using System.Collections.Generic;
using CoreSim.Rules;
using UnityEngine;
using UnityEngine.AI;

namespace CoreSim
{
    public enum PropKind : byte
    {
        Block = 0,       // 4.4 300 x 300 x 200 mm、1.3 kg
        Container = 1,   // 4.3 150 mm 角、0.5 kg
    }

    public enum PropColor : byte
    {
        None = 0,
        Red = 1,
        Blue = 2,
        Yellow = 3,
    }

    /// <summary>ブロックやコンテナを置ける場所の種類。</summary>
    public enum PlaceKind : byte
    {
        Site = 0,     // 3.5 ビルドサイト (ブロックを 2 個積む)
        Valley = 1,   // 3.6 高台の谷部 (ブロック)
        Port = 2,     // 3.7 陣地の防御ポート (ブロック)
        Spot = 3,     // 3.4 スポット (コンテナ)
    }

    /// <summary>決まった置き場所 1 つ。ビルドサイトは 2 段まで積める。</summary>
    public sealed class PropSlot
    {
        public PlaceKind Kind;
        public string Side = "";        // "red" / "blue"
        public int Index;               // ビルドサイト 1..4、スポット 1..18、ポート 1..2、谷部 1..2
        public Vector3 Position;        // 1 段目の底面の中心 (ワールド)
        public Quaternion Rotation;
        public int Levels = 1;
        public bool Post;               // スポット 1・5・8・12・15・16 (四角柱あり。差し込みの判定は省略)
        public readonly List<FieldProp> Stack = new List<FieldProp>();

        public int Count => Stack.Count;
        public bool Full => Stack.Count >= Levels;
        public bool Accepts(PropKind kind) => Kind == PlaceKind.Spot ? kind == PropKind.Container : kind == PropKind.Block;
        /// <summary>次に置く段の底面の中心。</summary>
        public Vector3 NextBottom => Position + Vector3.up * (Count * FieldProp.SizeOf(Kind == PlaceKind.Spot ? PropKind.Container : PropKind.Block).y);
        public Side RuleSide => Side == "red" ? Rules.Side.Red : Rules.Side.Blue;

        public string Label => Kind switch
        {
            PlaceKind.Site => $"ビルドサイト {Index}",
            PlaceKind.Valley => "高台の谷部",
            PlaceKind.Port => $"防御ポート {Index}",
            _ => $"スポット {Index}",
        };
    }

    /// <summary>フィールド上のブロックとコンテナ、置き場所の一覧。ホストもクライアントも同じ manifest から同じ順で作る。</summary>
    public sealed class PropRegistry
    {
        public readonly List<FieldProp> Props = new List<FieldProp>();
        public readonly List<PropSlot> Slots = new List<PropSlot>();
        private readonly Dictionary<ushort, FieldProp> m_ById = new Dictionary<ushort, FieldProp>();

        public void Add(FieldProp prop)
        {
            Props.Add(prop);
            m_ById[prop.Id] = prop;
        }

        public FieldProp Find(ushort id) => m_ById.TryGetValue(id, out FieldProp p) ? p : null;

        /// <summary>すべてのブロックとコンテナを開始位置に戻す (ラウンド開始)。</summary>
        public void ResetAll()
        {
            foreach (PropSlot s in Slots) s.Stack.Clear();
            foreach (FieldProp p in Props) p.ResetHome();
        }

        public void SetRemote(bool remote)
        {
            foreach (PropSlot s in Slots) s.Stack.Clear();
            foreach (FieldProp p in Props) p.SetRemote(remote);
        }

        /// <summary>
        /// kind の物体の底面が bottom にあるとき、maxDistance 以内で空きのある置き場所のうち最も近いもの。
        /// </summary>
        public PropSlot NearestOpenSlot(PropKind kind, Vector3 bottom, float maxDistance)
        {
            PropSlot best = null;
            float bestDist = maxDistance * maxDistance;
            foreach (PropSlot s in Slots)
            {
                if (!s.Accepts(kind) || s.Full) continue;
                float d = (s.NextBottom - bottom).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = s;
                }
            }
            return best;
        }

        public IEnumerable<PropSlot> SlotsOf(PlaceKind kind, string side)
        {
            foreach (PropSlot s in Slots) if (s.Kind == kind && s.Side == side) yield return s;
        }

        public PropSlot Slot(PlaceKind kind, string side, int index)
        {
            foreach (PropSlot s in Slots) if (s.Kind == kind && s.Side == side && s.Index == index) return s;
            return null;
        }
    }

    /// <summary>
    /// ブロックまたはコンテナ。開始位置と置き場所に置かれている間は動かない (kinematic)。
    /// ビルダーが持ち上げるとハンドに付いて動き、放すと落ちる (物理)。
    /// ネット対戦のクライアントでは <see cref="Remote"/> になり、位置はスナップショットから与える。
    /// </summary>
    public sealed class FieldProp : MonoBehaviour
    {
        public const int Layer = Tuning.LayerProp;

        public ushort Id { get; private set; }
        public PropKind Kind { get; private set; }
        public PropColor Color { get; private set; }
        public ContainerOrigin Origin { get; private set; }
        public string HomeSide { get; private set; } = "";
        public Vector3 Size { get; private set; }
        public Rigidbody Body { get; private set; }
        public Collider Col { get; private set; }
        public RobotAgent HeldBy { get; private set; }
        public PropSlot Slot { get; private set; }
        public bool Remote { get; private set; }
        /// <summary>開始位置に置かれたまま (まだ誰も触っていない)。</summary>
        public bool AtHome { get; private set; }
        public bool IsFree => HeldBy == null && Slot == null && !AtHome;

        private Vector3 m_HomePos;
        private Quaternion m_HomeRot;
        private PropSlot m_HomeSlot;
        private Vector3 m_TargetPos;
        private Quaternion m_TargetRot;
        private bool m_HasTarget;
        private Vector3 m_LastSent;
        private Quaternion m_LastSentRot;
        private NavMeshObstacle m_Obstacle;

        /// <summary>コンテナの色から見た同盟 (黄色は無し)。</summary>
        public Side? ColorSide => Color == PropColor.Red ? Rules.Side.Red : Color == PropColor.Blue ? (Side?)Rules.Side.Blue : null;
        public Vector3 Bottom => transform.position - transform.up * (Size.y / 2f);
        public string Label => Kind == PropKind.Block ? "ブロック" : Color switch { PropColor.Red => "赤コンテナ", PropColor.Blue => "青コンテナ", _ => "共通コンテナ" };

        public static Vector3 SizeOf(PropKind kind) => kind == PropKind.Block ? new Vector3(0.3f, 0.2f, 0.3f) : new Vector3(0.15f, 0.15f, 0.15f);
        public static float MassOf(PropKind kind) => kind == PropKind.Block ? 1.3f : 0.5f;

        public static Material MaterialFor(PropKind kind, PropColor color)
        {
            if (kind == PropKind.Block) return MaterialLibrary.Color(new Color(0.89f, 0.77f, 0.56f), 0.15f);
            return color switch
            {
                PropColor.Red => MaterialLibrary.Color(new Color(0.85f, 0.2f, 0.25f), 0.35f),
                PropColor.Blue => MaterialLibrary.Color(new Color(0.2f, 0.4f, 0.85f), 0.35f),
                _ => MaterialLibrary.Color(new Color(0.95f, 0.8f, 0.2f), 0.35f),
            };
        }

        /// <param name="bottom">底面の中心 (ワールド)。</param>
        public static FieldProp Create(Transform parent, ushort id, PropKind kind, PropColor color, ContainerOrigin origin, string homeSide,
                                       Vector3 bottom, Quaternion rotation, PropSlot homeSlot, bool remote)
        {
            Vector3 size = SizeOf(kind);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"{kind}_{id}";
            go.layer = Layer;
            go.transform.SetParent(parent, false);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = MaterialFor(kind, color);
            var prop = go.AddComponent<FieldProp>();
            prop.Id = id;
            prop.Kind = kind;
            prop.Color = color;
            prop.Origin = origin;
            prop.HomeSide = homeSide;
            prop.Size = size;
            prop.Col = go.GetComponent<Collider>();
            prop.Body = go.AddComponent<Rigidbody>();
            prop.Body.mass = MassOf(kind);
            prop.Body.isKinematic = true;
            prop.Body.interpolation = RigidbodyInterpolation.Interpolate;
            prop.Body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            prop.Body.linearDamping = 0.3f;
            prop.Body.angularDamping = 1.5f;
            prop.m_HomePos = bottom + Vector3.up * (size.y / 2f);
            prop.m_HomeRot = rotation;
            prop.m_HomeSlot = homeSlot;
            prop.m_Root = parent;
            prop.Remote = remote;
            if (kind == PropKind.Block && !remote)
            {
                // 動かしたブロックは CPU の経路から除く (置いた場所に合わせて NavMesh を削る)
                prop.m_Obstacle = go.AddComponent<NavMeshObstacle>();
                prop.m_Obstacle.shape = NavMeshObstacleShape.Box;
                prop.m_Obstacle.size = Vector3.one;   // ローカル (スケール込みで実寸)
                prop.m_Obstacle.carving = true;
                prop.m_Obstacle.carveOnlyStationary = true;
            }
            if (kind == PropKind.Block)
            {
                // 上面の木目 (向きが分かる印)
                Visuals.Create("mark", PrimitiveType.Cube, go.transform, new Vector3(0f, 0.505f, 0f), Quaternion.identity,
                               new Vector3(0.8f, 0.01f, 0.1f), MaterialLibrary.Color(new Color(0.6f, 0.45f, 0.3f), 0.1f));
            }
            else
            {
                // 開いた面 (手前) の縁
                Visuals.Create("rim", PrimitiveType.Cube, go.transform, new Vector3(0f, 0f, -0.49f), Quaternion.identity,
                               new Vector3(0.86f, 0.86f, 0.03f), MaterialLibrary.Color(new Color(0.15f, 0.15f, 0.17f), 0.2f));
            }
            prop.ResetHome();
            return prop;
        }

        /// <summary>クライアントの写しにする / 戻す。写しでは物理と NavMesh の削りを止め、位置はスナップショットから与える。</summary>
        public void SetRemote(bool remote)
        {
            Remote = remote;
            if (m_Obstacle != null) m_Obstacle.enabled = !remote;
            ResetHome();
        }

        public void ResetHome()
        {
            Detach();
            Body.isKinematic = true;
            transform.SetPositionAndRotation(m_HomePos, m_HomeRot);
            AtHome = true;
            m_HasTarget = false;
            if (m_HomeSlot != null)
            {
                Slot = m_HomeSlot;
                m_HomeSlot.Stack.Add(this);
            }
        }

        private void Detach()
        {
            if (Slot != null)
            {
                Slot.Stack.Remove(this);
                Slot = null;
            }
            HeldBy = null;
            AtHome = false;
            if (transform.parent != m_Root) transform.SetParent(m_Root, true);
        }

        private Transform m_Root;

        /// <summary>ビルダーのハンドに付ける。</summary>
        public void Hold(Transform hand, RobotAgent by)
        {
            Detach();
            HeldBy = by;
            Body.isKinematic = true;
            transform.SetParent(hand, true);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            SetCollisionWith(by, true);
        }

        /// <summary>ハンドから放して落とす。</summary>
        public void Drop(Vector3 velocity)
        {
            RobotAgent by = HeldBy;
            Detach();
            if (!Remote)
            {
                Body.isKinematic = false;
                Body.linearVelocity = velocity;
                Body.angularVelocity = Vector3.zero;
            }
            if (by != null) StartCoroutine(RestoreCollision(by));
        }

        /// <summary>置き場所に置く (ぴったり揃えて固定)。</summary>
        public void PlaceIn(PropSlot slot)
        {
            RobotAgent by = HeldBy;
            Vector3 bottom = slot.NextBottom;
            Detach();
            Body.isKinematic = true;
            transform.SetPositionAndRotation(bottom + Vector3.up * (Size.y / 2f), slot.Rotation);
            Slot = slot;
            slot.Stack.Add(this);
            if (by != null) StartCoroutine(RestoreCollision(by));
        }

        private System.Collections.IEnumerator RestoreCollision(RobotAgent by)
        {
            yield return new WaitForSeconds(0.4f);
            if (by != null && HeldBy != by) SetCollisionWith(by, false);
        }

        private void SetCollisionWith(RobotAgent robot, bool ignore)
        {
            if (robot == null || Col == null) return;
            foreach (Collider c in robot.GetComponentsInChildren<Collider>())
            {
                if (c != null && c != Col) Physics.IgnoreCollision(Col, c, ignore);
            }
        }

        // ------------------------------------------------------------------ ネット対戦

        /// <summary>前回送ってから動いたか (スナップショットの間引き用)。</summary>
        public bool MovedSince(float threshold)
        {
            return (transform.position - m_LastSent).sqrMagnitude > threshold * threshold ||
                   Quaternion.Angle(transform.rotation, m_LastSentRot) > 1f;
        }

        public void MarkSent()
        {
            m_LastSent = transform.position;
            m_LastSentRot = transform.rotation;
        }

        /// <summary>クライアント側: スナップショットの位置を目標にする。</summary>
        public void ApplyRemote(Vector3 position, Quaternion rotation, bool held, bool inSlot)
        {
            AtHome = false;
            if (!m_HasTarget)
            {
                transform.SetPositionAndRotation(position, rotation);
                m_HasTarget = true;
            }
            m_TargetPos = position;
            m_TargetRot = rotation;
            m_RemoteHeld = held;
        }

        private bool m_RemoteHeld;
        public bool RemoteHeld => m_RemoteHeld;

        private void Update()
        {
            if (!Remote || !m_HasTarget) return;
            float k = 1f - Mathf.Exp(-Time.deltaTime * 20f);
            transform.position = Vector3.Lerp(transform.position, m_TargetPos, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, m_TargetRot, k);
        }
    }
}
