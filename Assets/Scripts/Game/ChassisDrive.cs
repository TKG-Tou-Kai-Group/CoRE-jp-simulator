using UnityEngine;

namespace CoreSim
{
    /// <summary>
    /// 車体速度の指令 (車体基準の vx, vy, ωz) を力とトルクで追従させる駆動。
    /// Unity_ROS2_Robot_Simulator の BodyTwistDrive を Rigidbody 向けに移植したもの。
    /// </summary>
    /// <remarks>
    /// 速度を代入せず力で追従させるので、他機に押されるし押し返せる。各 FixedUpdate で
    /// 「指令速度 − 現在速度」を 1 ステップで詰めるのに要る加速度を求め、上限でクランプする。
    /// ForceMode.Acceleration を使うので質量・慣性の換算は物理エンジンに任せられる
    /// (元の実装は ArticulationBody で連結全体の質量と慣性を自前で集計していた)。
    /// 鉛直・ロール・ピッチは制御しない。
    /// </remarks>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ChassisDrive : MonoBehaviour
    {
        public float maxLinearVelocity = Tuning.RealTurboLinear;
        public float maxAngularVelocity = Tuning.RealTurboAngular;
        public float maxLinearAcceleration = Tuning.RealLinearAccel;
        public float maxAngularAcceleration = Tuning.RealAngularAccel;
        public float commandTimeout = Tuning.CommandTimeout;

        private Rigidbody m_Body;
        private float m_Vx, m_Vy, m_Wz;
        private float m_LastCommandTime = float.NegativeInfinity;
        private Vector3 m_GroundNormal = Vector3.up;
        private bool m_Grounded;
        private float m_TiltedSince = -1f;

        /// <summary>転倒から起き上がっている最中 (ゲームとして成立させるための救済。実機にはない)。</summary>
        public bool Righting { get; private set; }
        public const float TiltLimit = 0.55f;      // transform.up.y がこれより小さい = 約 57 度以上傾いた
        public const float RightingDelay = 1.0f;   // 傾いたままこの秒数たったら起き上がる
        public const float RightingGain = 3.0f;    // 角加速度 [rad/s^2 / rad]

        /// <summary>接地面の法線 (接地していなければ上向き)。</summary>
        public Vector3 GroundNormal => m_GroundNormal;
        public bool Grounded => m_Grounded;

        /// <summary>現在のワールド水平速度 [m/s]。</summary>
        public float Speed => Vector3.ProjectOnPlane(m_Body.linearVelocity, Vector3.up).magnitude;

        private void Awake()
        {
            m_Body = GetComponent<Rigidbody>();
        }

        /// <summary>ROS の Twist と同じ向き: vx 前, vy 左, wz 上から見て反時計回り。</summary>
        public void SetCommand(float vx, float vy, float wz)
        {
            m_Vx = vx;
            m_Vy = vy;
            m_Wz = wz;
            m_LastCommandTime = Time.fixedTime;
        }

        private void FixedUpdate()
        {
            // 接地面の法線。接地球は摩擦 0 なので、斜面では重力の斜面成分で滑り落ちる。実機は
            // 車輪の摩擦で斜面を保持できるので、その成分を打ち消す力を足して斜面を平地と同じに扱う。
            // (勾配 15 度で約 2.5 m/s^2 あり、加速度上限 0.4〜1.6 m/s^2 ではこれが無いと登れない)
            int mask = ~((1 << Tuning.LayerDisc) | (1 << Tuning.LayerMinimapOnly) | (1 << Tuning.LayerRobot));
            m_Grounded = Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 0.5f, mask);
            m_GroundNormal = m_Grounded ? hit.normal : Vector3.up;
            // 登れる斜面 (約 37 度まで) だけ。もっと急な面 (段差の角や構造物の斜めの面) に乗り上げたときに
            // 面の上向きへ押すと、構造物へ押し付けられて動けなくなる
            if (m_Grounded && m_GroundNormal.y > 0.8f && m_GroundNormal.y < 0.999f)
            {
                Vector3 g = Physics.gravity;
                Vector3 tangential = g - Vector3.Dot(g, m_GroundNormal) * m_GroundNormal;
                m_Body.AddForce(-tangential, ForceMode.Acceleration);
            }

            // 転倒からの復帰: 大きく傾いたまま 1 秒たったら、ゆっくり起き上がる向きのトルクをかける
            float upY = transform.up.y;
            if (upY < TiltLimit)
            {
                if (m_TiltedSince < 0f) m_TiltedSince = Time.fixedTime;
                if (Time.fixedTime - m_TiltedSince > RightingDelay) Righting = true;
            }
            else if (upY > 0.97f)
            {
                m_TiltedSince = -1f;
                Righting = false;
            }
            if (Righting)
            {
                Vector3 axis = Vector3.Cross(transform.up, Vector3.up);
                float angle = Vector3.Angle(transform.up, Vector3.up) * Mathf.Deg2Rad;
                Vector3 torque = axis.normalized * (angle * RightingGain) - Vector3.ProjectOnPlane(m_Body.angularVelocity, Vector3.up) * 1.5f;
                m_Body.AddTorque(Vector3.ClampMagnitude(torque, 4f), ForceMode.Acceleration);
                m_Body.AddForce(Vector3.up * 4f, ForceMode.Acceleration);   // 床に押し付けられたまま回れるように少し浮かせる
                return;   // 起き上がるまでは走らない
            }

            float vx = 0f, vy = 0f, wz = 0f;
            if (Time.fixedTime - m_LastCommandTime <= commandTimeout)
            {
                vx = m_Vx;
                vy = m_Vy;
                wz = m_Wz;
            }

            // 並進: ROS 車体座標 → Unity 車体座標 (-y, 0, x) → ワールド。
            Vector3 targetLocal = new Vector3(-vy, 0f, vx);
            Vector3 targetWorld = transform.TransformDirection(targetLocal);
            targetWorld = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(targetWorld, Vector3.up), maxLinearVelocity);
            Vector3 current = Vector3.ProjectOnPlane(m_Body.linearVelocity, Vector3.up);
            Vector3 accel = Vector3.ClampMagnitude((targetWorld - current) / Time.fixedDeltaTime, maxLinearAcceleration);
            m_Body.AddForce(accel, ForceMode.Acceleration);

            // ヨー: ROS の +z 反時計回りは Unity では -y 回り。
            float targetYaw = -Mathf.Clamp(wz, -maxAngularVelocity, maxAngularVelocity);
            float currentYaw = m_Body.angularVelocity.y;
            float angAccel = Mathf.Clamp((targetYaw - currentYaw) / Time.fixedDeltaTime,
                                         -maxAngularAcceleration, maxAngularAcceleration);
            m_Body.AddTorque(new Vector3(0f, angAccel, 0f), ForceMode.Acceleration);
        }
    }
}
