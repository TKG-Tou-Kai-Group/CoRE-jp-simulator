using UnityEngine;

namespace CoreSim
{
    /// <summary>
    /// 物理・試合の定数。値の出どころは CoRE-jp-Unity-ROS2-packages (ROS 2 版) で、
    /// 実機相当の挙動を「リアル」プリセット、遊びやすく速めたものを「ゲーム」プリセットにしている。
    /// </summary>
    public static class Tuning
    {
        // ---- 走行 (sample_robot.simulation.xacro の body_twist_drive と teleop の scale) ----
        // 通常時 0.5 m/s / 0.75 rad/s、Turbo で 1.0 m/s / 1.5 rad/s。加速度 0.4 m/s^2 / 0.8 rad/s^2。
        public const float RealNormalLinear = 0.5f;
        public const float RealNormalAngular = 0.75f;
        public const float RealTurboLinear = 1.0f;
        public const float RealTurboAngular = 1.5f;
        public const float RealLinearAccel = 0.4f;
        public const float RealAngularAccel = 0.8f;

        // ゲームプリセット。27.6 m のフィールドを 0.5 m/s で渡ると 55 秒かかり、
        // 初めて触る人には間延びするので速度 2 倍・加速度 4 倍にしてある。
        public const float GameSpeedScale = 2.0f;
        public const float GameAccelScale = 4.0f;

        public const float CommandTimeout = 0.5f;
        public const float RobotMass = 24f;          // base 20 kg + シュータ等

        // ---- 射出 (shooter.urdf.xacro / 実測の弾道特性) ----
        // 初速 [m/s]。CoRE-1 ルールブック 14.3 の上限 15 m/s に全ルールで統一する。
        // ROS 2 版の 20 m/s は射出輪がシミュレータ上で安定して出せる速度として決めた値で、ルールの値ではない。
        public const float DiscSpeed = 15f;
        public const float MuzzleElevationDeg = 3.4f;
        public const float ShooterMountHeight = 0.3512f;   // shooter_mount_z
        public const float FireInterval = 0.6f;      // 装填 1 ストロークぶん
        public const float DispersionDeg = 2.0f;     // 方位のばらつき (±)。実機相当は ±6 度
        public const float DiscRadius = 0.09f;
        public const float DiscThickness = 0.02f;
        public const float DiscMass = 0.015f;
        public const float DiscLifetime = 6f;

        // ---- HP (hp_manager / sample_robot_N_spawn_for_core.launch.py) ----
        public const int InitialHp = 200;
        public const int DamagePerHit = 10;

        // ---- 試合 (game_manager / bring_up_core_stage.launch.py) ----
        public const int DefaultMatchSeconds = 120;
        public const int PreCountdownSeconds = 10;
        public const int ResultSeconds = 10;

        // ---- フィールド (core_stage.world)。ROS (x 前, y 左, z 上) → Unity (-y, z, x) ----
        // 壁は ROS x = ±10 (長さ 28 m)、y = ±14 (長さ 20 m)、高さ 5 m。
        public const float WallHalfZ = 10f;   // Unity z (= ROS x)
        public const float WallHalfX = 14f;   // Unity x (= -ROS y)
        public const float WallHeight = 5f;

        public static Vector3 RosToUnity(float x, float y, float z) => new Vector3(-y, z, x);

        /// <summary>ROS のヨー [rad] (z 軸まわり反時計) を Unity の Y 回転 [deg] に。</summary>
        public static float RosYawToUnityDeg(float yawRad) => -yawRad * Mathf.Rad2Deg;

        /// <summary>
        /// 8 台ぶんの開始位置 (sample_robot_N_spawn_for_core.launch.py と同じ座標)。
        /// 奇数番 (Blue) が ROS y 負側、偶数番 (Red) が正側で、互いに向き合う。
        /// </summary>
        public static readonly Pose[] StartPoses =
        {
            StartPose(-4.5f, -9.75f, 1.57f),   // 1 Blue
            StartPose(4.5f, 9.75f, -1.57f),    // 2 Red
            StartPose(-5.5f, -9.75f, 1.57f),   // 3 Blue
            StartPose(5.5f, 9.75f, -1.57f),    // 4 Red
            StartPose(-4.5f, -11.25f, 1.57f),  // 5 Blue
            StartPose(4.5f, 11.25f, -1.57f),   // 6 Red
            StartPose(-5.5f, -11.25f, 1.57f),  // 7 Blue
            StartPose(5.5f, 11.25f, -1.57f),   // 8 Red
        };

        private static Pose StartPose(float rosX, float rosY, float rosYaw) =>
            new Pose(RosToUnity(rosX, rosY, 0.15f), Quaternion.Euler(0f, RosYawToUnityDeg(rosYaw), 0f));

        public static readonly Color BlueTeam = new Color(0.2f, 0.5f, 1f);
        public static readonly Color RedTeam = new Color(1f, 0.25f, 0.25f);

        public const int LayerDisc = 6;
        public const int LayerMinimapOnly = 7;
        public const int LayerRobot = 8;
        public const int LayerProp = 9;              // ブロック・コンテナ
        public const int LayerBridge = 10;           // 谷部の橋 (機体だけが乗る。ブロック・コンテナは溝に落ちる)

        // ---- ビルダー (ルールブック 14.1: 開始後 1200 mm 角まで) ----
        public const float BuilderMass = 26f;
        public const float BuilderHandForward = 0.62f;   // 機体中心からハンド中心まで (前方)
    }
}
