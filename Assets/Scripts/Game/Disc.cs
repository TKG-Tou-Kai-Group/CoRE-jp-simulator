using System.Collections.Generic;
using UnityEngine;

namespace CoreSim
{
    /// <summary>
    /// 射出されたフライングディスク。何かに触れた時点で消える (装甲板なら被弾を記録)。
    /// </summary>
    /// <remarks>
    /// ROS 2 版は撃った弾を場に最大 24 枚残していたが、静止した剛体でもソルバの
    /// コストはゼロではない。スペック要求を下げるため、着地・場外・寿命で即座に消す。
    /// 発射者の機体とは衝突させない (射出口のすぐ前に自分の装甲板があるため)。
    /// ネット対戦ではホストだけが本物を持ち、クライアントは <see cref="GhostDisc"/> で見た目だけ写す。
    /// </remarks>
    public sealed class Disc : MonoBehaviour
    {
        /// <summary>場に存在するディスク (ホスト / ローカル)。スナップショットの元になる。</summary>
        public static readonly List<Disc> Live = new List<Disc>();
        private static ushort s_NextId = 1;

        public ushort Id { get; private set; }
        public RobotAgent Owner { get; private set; }
        public Rigidbody Body { get; private set; }

        private float m_Deadline;
        private bool m_Done;

        public static Material Material => MaterialLibrary.Color(new Color(0.95f, 0.95f, 0.9f), 0.5f);

        public static Disc Launch(RobotAgent owner, Vector3 position, Vector3 direction, float speed, Vector3 inheritedVelocity)
        {
            GameObject go = Visuals.Create("Disc", PrimitiveType.Cylinder, null, position,
                Quaternion.LookRotation(direction, Vector3.up),
                new Vector3(Tuning.DiscRadius * 2f, Tuning.DiscThickness / 2f, Tuning.DiscRadius * 2f),
                Material, Tuning.LayerDisc);

            var sphere = go.AddComponent<SphereCollider>();
            sphere.radius = 0.5f;   // ローカル半径 0.5 x スケール 0.18 = 0.09 m

            var body = go.AddComponent<Rigidbody>();
            body.mass = Tuning.DiscMass;
            body.linearDamping = 0.05f;
            body.angularDamping = 0.5f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.linearVelocity = direction.normalized * speed + inheritedVelocity;
            body.angularVelocity = go.transform.up * 60f;   // 見た目用の回転

            var disc = go.AddComponent<Disc>();
            disc.Id = s_NextId++;
            if (s_NextId == 0) s_NextId = 1;
            disc.Owner = owner;
            disc.Body = body;
            disc.m_Deadline = Time.time + Tuning.DiscLifetime;
            if (owner != null)
            {
                foreach (Collider c in owner.GetComponentsInChildren<Collider>())
                {
                    Physics.IgnoreCollision(sphere, c, true);
                }
            }
            Live.Add(disc);
            return disc;
        }

        private void Update()
        {
            if (m_Done)
            {
                return;
            }
            Vector3 p = transform.position;
            if (Time.time > m_Deadline || p.y < -1f ||
                Mathf.Abs(p.x) > Tuning.WallHalfX + 2f || Mathf.Abs(p.z) > Tuning.WallHalfZ + 2f)
            {
                Vanish();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (m_Done)
            {
                return;
            }
            var plate = collision.collider.GetComponent<ArmorPlate>();
            if (plate != null)
            {
                plate.RegisterHit(this, collision.GetContact(0).point);
            }
            else
            {
                var basePanel = collision.collider.GetComponent<BasePanel>();
                if (basePanel != null)
                {
                    GameDirector.Instance?.Tournament?.RegisterBaseHit(basePanel, Owner);
                }
            }
            Vanish();
        }

        private void Vanish()
        {
            m_Done = true;
            Live.Remove(this);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            Live.Remove(this);
        }
    }

    /// <summary>クライアント側の見た目だけのディスク。スナップショットの間は速度で外挿する。</summary>
    public sealed class GhostDisc : MonoBehaviour
    {
        public Vector3 Velocity;

        public static GhostDisc Create(Vector3 position, Vector3 velocity)
        {
            GameObject go = Visuals.Create("GhostDisc", PrimitiveType.Cylinder, null, position,
                Quaternion.LookRotation(velocity.sqrMagnitude > 1e-4f ? velocity : Vector3.forward, Vector3.up),
                new Vector3(Tuning.DiscRadius * 2f, Tuning.DiscThickness / 2f, Tuning.DiscRadius * 2f),
                Disc.Material, Tuning.LayerDisc);
            var ghost = go.AddComponent<GhostDisc>();
            ghost.Velocity = velocity;
            return ghost;
        }

        public void Apply(Vector3 position, Vector3 velocity)
        {
            transform.position = position;
            Velocity = velocity;
        }

        private void Update()
        {
            transform.position += Velocity * Time.deltaTime;
            transform.Rotate(0f, 400f * Time.deltaTime, 0f, Space.Self);
        }
    }
}
