using CoreSim.Rules;
using UnityEngine;

namespace CoreSim
{
    /// <summary>
    /// 1 台の機体。操縦指令の出どころ (<see cref="ICommandSource"/>) を駆動と射出へ繋ぎ、HP を持つ。
    /// ネット対戦のクライアントでは <see cref="Remote"/> になり、物理を回さずホストの状態を写す。
    /// </summary>
    public sealed class RobotAgent : MonoBehaviour
    {
        public int Index;                 // 0 始まり = ネット対戦のスロット番号。表示は Index + 1
        public string DisplayName = "";
        public Team Team;
        public bool IsPlayer;
        public bool CanFire = true;       // 練習モードの的は撃たない
        public bool Remote;               // クライアント側の写し (物理なし)
        public bool IsHeadAttacker;       // 大会ルール: ヘッドアタッカー
        public bool Frozen;               // 大会ルール: ピットイン中の完全停止
        public RobotKind Kind;            // アタッカー / ビルダー / オートアタッカー
        public bool AutoStarted;          // オートアタッカー: 始動済みで停止していない (大会ルール。ホスト / 写しとも)
        public HealthModel Health;
        public ICommandSource Source;
        public ChassisDrive Drive;
        public Shooter Shooter;           // アタッカーのみ
        public BuilderLift Lift;          // ビルダーのみ
        public Transform CameraMount;     // 一人称視点
        public Transform Marker;          // ミニマップ用

        public float NormalLinear = Tuning.RealNormalLinear;
        public float NormalAngular = Tuning.RealNormalAngular;
        public float TurboLinear = Tuning.RealTurboLinear;
        public float TurboAngular = Tuning.RealTurboAngular;

        public RobotCommand LastCommand { get; private set; }
        public int ShotCount { get; set; }
        public bool IsKnockedOut => Health != null && !Health.IsAlive;
        public bool CanAct => !IsKnockedOut && !Frozen;
        public bool IsTurbo => Remote ? m_RemoteTurbo : LastCommand.Turbo;
        public bool IsBuilder => Kind == RobotKind.Builder;
        public bool IsAuto => Kind == RobotKind.AutoAttacker;
        public string KindName => IsBuilder ? "ビルダー" : IsAuto ? "オートアタッカー" : IsHeadAttacker ? "ヘッドアタッカー" : "アタッカー";
        public Vector3 StartPosition => m_StartPose.position;
        public Color TeamColor => Team == Team.Blue ? Tuning.BlueTeam : Tuning.RedTeam;

        private Rigidbody m_Body;
        private Pose m_StartPose;
        private ArmorPlate[] m_Plates = new ArmorPlate[0];
        private bool m_RemoteTurbo;
        private bool m_RemoteKo;
        private Vector3 m_TargetPos;
        private Quaternion m_TargetRot;
        private bool m_HasTarget;

        public event System.Action<RobotAgent, int> Damaged;   // (機体, 減った HP)
        public event System.Action<RobotAgent> KnockedOut;

        public void Setup(Pose startPose, ArmorPlate[] plates)
        {
            m_Body = GetComponent<Rigidbody>();
            m_StartPose = startPose;
            m_Plates = plates;
            if (Health == null)
            {
                Health = new HealthModel(Tuning.InitialHp, Tuning.DamagePerHit);
            }
        }

        /// <summary>開始位置へ戻し、HP を満タンにする。</summary>
        public void ResetToStart()
        {
            Health.Reset();
            if (Lift != null)
            {
                if (Lift.Held != null) Lift.Held.ResetHome();
                Lift.Clear();
            }
            m_RemoteKo = false;
            if (!m_Body.isKinematic)
            {
                m_Body.linearVelocity = Vector3.zero;
                m_Body.angularVelocity = Vector3.zero;
            }
            m_Body.position = m_StartPose.position;
            m_Body.rotation = m_StartPose.rotation;
            transform.SetPositionAndRotation(m_StartPose.position, m_StartPose.rotation);
            PaintPlates(TeamColor);
        }

        /// <summary>クライアント側の写しにする。物理と駆動を止め、位置はスナップショットから与える。</summary>
        public void MakeRemote()
        {
            Remote = true;
            m_Body.isKinematic = true;
            m_Body.interpolation = RigidbodyInterpolation.None;
            if (Drive != null) Drive.enabled = false;
            if (Shooter != null) Shooter.enabled = false;
        }

        /// <summary>クライアント側: ビルダーのハンドの高さと持ち物を写す。</summary>
        public void ApplyRemoteLift(float height, FieldProp held)
        {
            if (Lift != null) Lift.ApplyRemote(height, held);
        }

        public void SetRemoteTarget(Vector3 position, Quaternion rotation)
        {
            if (!m_HasTarget)
            {
                transform.SetPositionAndRotation(position, rotation);
                m_HasTarget = true;
            }
            m_TargetPos = position;
            m_TargetRot = rotation;
        }

        public void ApplyRemoteState(int hp, int hits, bool ko, bool turbo)
        {
            Health.Set(hp, hits);
            m_RemoteTurbo = turbo;
            if (ko != m_RemoteKo)
            {
                m_RemoteKo = ko;
                PaintPlates(ko ? new Color(0.25f, 0.25f, 0.25f) : TeamColor);
            }
        }

        private void PaintPlates(Color color)
        {
            foreach (ArmorPlate p in m_Plates)
            {
                p.SetBaseColor(color);
            }
        }

        /// <summary>装甲板の色を外から決める (大会ルール: 撃破 / 無敵 / 通常)。</summary>
        public void SetPlateColor(Color color) => PaintPlates(color);

        public void FlashPlates()
        {
            foreach (ArmorPlate p in m_Plates) p.FlashOnly();
        }

        /// <summary>ヘッドアタッカーの印 (金色の輪) を付ける。</summary>
        public void MarkAsHead()
        {
            IsHeadAttacker = true;
            Visuals.Create("head_crown", PrimitiveType.Cylinder, transform, Tuning.RosToUnity(-0.2f, 0f, 0.78f), Quaternion.identity,
                           new Vector3(0.3f, 0.015f, 0.3f), MaterialLibrary.Emissive(new Color(1f, 0.8f, 0.2f)));
        }

        public void OnArmorHit(ArmorPlate plate, Disc disc, Vector3 point)
        {
            var director = GameDirector.Instance;
            if (director != null && !director.AcceptsDamage)
            {
                return;   // カウントダウン中・結果表示中は無効
            }
            if (director != null && director.Tournament != null)
            {
                director.Tournament.RegisterRobotHit(this, disc != null ? disc.Owner : null);   // 判定はルール側
                return;
            }
            int lost = Health.Hit();
            if (lost <= 0)
            {
                return;
            }
            director?.Audio.PlayHit(point);
            Damaged?.Invoke(this, lost);
            if (!Health.IsAlive)
            {
                PaintPlates(new Color(0.25f, 0.25f, 0.25f));
                KnockedOut?.Invoke(this);
            }
        }

        private void Update()
        {
            if (!Remote || !m_HasTarget)
            {
                return;
            }
            // スナップショット (30 Hz) の間を滑らかに埋める。
            float k = 1f - Mathf.Exp(-Time.deltaTime * 20f);
            transform.position = Vector3.Lerp(transform.position, m_TargetPos, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, m_TargetRot, k);
        }

        private void FixedUpdate()
        {
            if (Remote)
            {
                return;
            }
            RobotCommand cmd = Source != null ? Source.Read() : RobotCommand.None;
            var director = GameDirector.Instance;
            bool controllable = director != null && director.RobotsMayMove && CanAct &&
                                (!IsAuto || AutoStarted || director.Tournament == null);   // オートは始動まで動かない (12.7)
            if (!controllable)
            {
                cmd = RobotCommand.None;
            }
            LastCommand = cmd;

            float lin = cmd.Turbo ? TurboLinear : NormalLinear;
            float ang = cmd.Turbo ? TurboAngular : NormalAngular;
            Vector2 move = Vector2.ClampMagnitude(cmd.Move, 1f);
            // Move.y 前 → ROS vx、Move.x 右 → ROS vy は左が正なので符号反転。
            // Turn 右回り → ROS wz は反時計が正なので符号反転。
            Drive.SetCommand(move.y * lin, -move.x * lin, -Mathf.Clamp(cmd.Turn, -1f, 1f) * ang);

            if (Shooter != null && cmd.Fire && CanFire && controllable && director.AcceptsDamage)
            {
                Shooter.TryFire();
            }
            if (Lift != null)
            {
                Lift.Tick(cmd, controllable);
            }
        }
    }
}
