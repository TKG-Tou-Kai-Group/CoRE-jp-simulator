using CoreSim.Rules;
using System.Collections.Generic;
using UnityEngine;

namespace CoreSim
{
    /// <summary>
    /// sample_robot_description の URDF と同じ寸法で機体をプリミティブから組み立てる。
    /// 座標は ROS (x 前, y 左, z 上) で書き、<see cref="Tuning.RosToUnity"/> で変換する。
    /// </summary>
    public static class RobotFactory
    {
        /// <param name="kind">アタッカー (射出機構) かビルダー (昇降ハンド)。走行部と装甲板は共通。</param>
        public static RobotAgent Create(int index, Pose startPose, GameSettings settings, RobotKind kind = RobotKind.Attacker)
        {
            Team team = MatchState.TeamOf(index);
            bool builder = kind == RobotKind.Builder;
            Color teamColor = team == Team.Blue ? Tuning.BlueTeam : Tuning.RedTeam;

            var root = new GameObject($"Robot_{index + 1}");
            root.layer = Tuning.LayerRobot;
            root.transform.SetPositionAndRotation(startPose.position, startPose.rotation);

            var body = root.AddComponent<Rigidbody>();
            body.mass = builder ? Tuning.BuilderMass : Tuning.RobotMass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            body.angularDamping = 0.5f;
            body.linearDamping = 0f;
            body.maxAngularVelocity = 10f;
            // 構造物にめり込んだときの押し出し速度の上限。既定 (3 m/s) だと「吹き飛ぶ」ように見える
            body.maxDepenetrationVelocity = 1.5f;

            // ---- 車体の当たり判定: バンパーの高さ (0.05〜0.15) の一枚の箱 ----
            // 薄い板 4 枚 (厚さ 1 cm) だとメッシュの面をすり抜けて中に入り込み、押し出されて飛ぶことがあった。
            // 中身の詰まった箱なら面の内側に入れない。ディスクが当たる装甲板はこれより上にある
            var hull = root.AddComponent<BoxCollider>();
            hull.center = new Vector3(0f, 0.10f, 0f);
            hull.size = new Vector3(0.9f, 0.10f, 0.9f);
            // 重心は低く固定する (バッテリーと駆動が底にある実機に合わせる)。コライダから自動で求めると、
            // ビルダーはマストのぶん重心が上がって、棚にハンドを突っ込んだときに転倒しやすい
            body.centerOfMass = new Vector3(0f, 0.08f, 0f);

            Material white = MaterialLibrary.Color(new Color(0.92f, 0.92f, 0.92f), 0.4f);
            Material gray = MaterialLibrary.Color(new Color(0.35f, 0.35f, 0.38f), 0.3f);
            Material dark = MaterialLibrary.Color(new Color(0.15f, 0.15f, 0.17f), 0.3f);

            // ---- base_frame_link: 0.6 x 0.6 x 0.02 の板、base_link の +0.06 ----
            Box(root, "base_frame", Tuning.RosToUnity(0f, 0f, 0.06f), Quaternion.identity,
                new Vector3(0.6f, 0.02f, 0.6f), white, collider: false);

            // ---- 接地脚 4 個: 摩擦 0 の球 (半径 0.05) + 車輪の見た目 ----
            float[] sx = { 0.35f, -0.35f, -0.35f, 0.35f };
            float[] sy = { 0.35f, 0.35f, -0.35f, -0.35f };
            for (int i = 0; i < 4; i++)
            {
                Vector3 pos = Tuning.RosToUnity(sx[i], sy[i], 0.05f);
                var skid = new GameObject($"skid_{i + 1}");
                skid.transform.SetParent(root.transform, false);
                skid.transform.localPosition = pos;
                var sphere = skid.AddComponent<SphereCollider>();
                sphere.radius = 0.05f;
                sphere.sharedMaterial = MaterialLibrary.Frictionless;
                // オムニホイールの見た目: 車軸が機体中心から放射方向を向く円筒。
                Vector3 radial = new Vector3(pos.x, 0f, pos.z).normalized;
                Visuals.Create("wheel", PrimitiveType.Cylinder, skid.transform, Vector3.zero,
                               Quaternion.FromToRotation(Vector3.up, radial), new Vector3(0.1f, 0.02f, 0.1f), dark);
            }

            // ---- バンパー 4 枚: 0.9 x 0.01 x 0.10、base_link の z 0.10 (見た目。当たり判定は上の箱) ----
            Box(root, "bumper_back", Tuning.RosToUnity(-0.45f, 0f, 0.10f), Quaternion.identity, new Vector3(0.9f, 0.10f, 0.01f), white, false);
            Box(root, "bumper_front", Tuning.RosToUnity(0.45f, 0f, 0.10f), Quaternion.identity, new Vector3(0.9f, 0.10f, 0.01f), white, false);
            Box(root, "bumper_left", Tuning.RosToUnity(0f, 0.45f, 0.10f), Quaternion.identity, new Vector3(0.01f, 0.10f, 0.9f), white, false);
            Box(root, "bumper_right", Tuning.RosToUnity(0f, -0.45f, 0.10f), Quaternion.identity, new Vector3(0.01f, 0.10f, 0.9f), white, false);

            Transform muzzle = null;
            var rollers = new List<Transform>();
            BuilderLift lift = null;
            Transform cameraMount;
            if (!builder)
            {
                // ---- シュータ: base_link の +0.3512、仰角 3.4 度 (shooter.urdf.xacro) ----
                var shooterRoot = new GameObject("shooter");
                shooterRoot.transform.SetParent(root.transform, false);
                shooterRoot.transform.localPosition = Tuning.RosToUnity(0f, 0f, Tuning.ShooterMountHeight);
                shooterRoot.transform.localRotation = Quaternion.Euler(-Tuning.MuzzleElevationDeg, 0f, 0f);
                Material shooterMat = MaterialLibrary.Color(new Color(0.55f, 0.6f, 0.68f), 0.5f, 0.3f);
                Box(shooterRoot, "shooter_base", Tuning.RosToUnity(0.05f, 0f, 0f), Quaternion.identity, new Vector3(0.18f, 0.08f, 0.3f), shooterMat, true);
                Box(shooterRoot, "shooter_barrel", Tuning.RosToUnity(0.04f, 0f, 0.085f), Quaternion.identity, new Vector3(0.18f, 0.04f, 0.32f), shooterMat, false);
                Box(shooterRoot, "shooter_wall_l", Tuning.RosToUnity(0.0f, 0.1015f, 0.04f), Quaternion.identity, new Vector3(0.02f, 0.06f, 0.22f), shooterMat, false);
                Box(shooterRoot, "shooter_wall_r", Tuning.RosToUnity(0.0f, -0.1015f, 0.04f), Quaternion.identity, new Vector3(0.02f, 0.06f, 0.22f), shooterMat, false);
                float rollerY = Tuning.DiscRadius + 0.04f - 0.002f;
                foreach (float side in new[] { rollerY, -rollerY })
                {
                    GameObject roller = Visuals.Create("roller", PrimitiveType.Cylinder, shooterRoot.transform,
                        Tuning.RosToUnity(0.15f, side, 0.055f), Quaternion.identity, new Vector3(0.08f, 0.015f, 0.08f), dark);
                    rollers.Add(roller.transform);
                }
                // 装填用のポール 4 本 (弾倉の名残だが機体のシルエットになる)
                foreach (var (px, py) in new[] { (0.07778f, 0.07778f), (-0.07778f, 0.07778f), (-0.07778f, -0.07778f), (0.07778f, -0.07778f) })
                {
                    Visuals.Create("pole", PrimitiveType.Cylinder, shooterRoot.transform,
                                   Tuning.RosToUnity(px, py, 0.315f), Quaternion.identity, new Vector3(0.02f, 0.25f, 0.02f), gray);
                }
                var muzzleGo = new GameObject("muzzle");
                muzzleGo.transform.SetParent(shooterRoot.transform, false);
                muzzleGo.transform.localPosition = Tuning.RosToUnity(0.21f, 0f, 0.055f);
                muzzle = muzzleGo.transform;

                // カメラ取り付け点 (camera_link: 0.45, 0, 0.28)
                var cam = new GameObject("camera_mount");
                cam.transform.SetParent(root.transform, false);
                cam.transform.localPosition = Tuning.RosToUnity(0.45f, 0f, 0.28f);
                cameraMount = cam.transform;
            }
            else
            {
                // ---- ビルダー: 前方のマスト 2 本 + 昇降キャリッジ (フォーク + ハンド) ----
                // 開始時 800x800x1000 以内、開始後 1200 mm 角まで (14.1)。ハンドは機体中心の 0.62 m 前 (ブロックを
                // 持っても全長 1.22 m)。マストの高さ 1.1 m。
                Material mastMat = MaterialLibrary.Color(new Color(0.3f, 0.32f, 0.36f), 0.5f, 0.6f);
                Material handMat = MaterialLibrary.Color(new Color(0.9f, 0.6f, 0.15f), 0.4f, 0.2f);
                foreach (float side in new[] { 0.14f, -0.14f })
                {
                    Box(root, "mast", Tuning.RosToUnity(0.33f, side, 0.58f), Quaternion.identity, new Vector3(0.04f, 1.04f, 0.05f), mastMat, false);
                }
                Box(root, "mast_top", Tuning.RosToUnity(0.33f, 0f, 1.11f), Quaternion.identity, new Vector3(0.36f, 0.04f, 0.05f), mastMat, false);
                // マストの当たり判定は 2 本まとめて 1 つの箱 (細い柱 2 本だと棚板の縁に引っ掛かって抜けなくなる)
                var mastHull = root.AddComponent<BoxCollider>();
                mastHull.center = Tuning.RosToUnity(0.33f, 0f, 0.58f);
                mastHull.size = new Vector3(0.36f, 1.04f, 0.05f);
                var carriage = new GameObject("carriage");
                carriage.transform.SetParent(root.transform, false);
                carriage.transform.localPosition = Tuning.RosToUnity(0.36f, 0f, 0.0f);   // y はリフトが決める
                // キャリッジ本体 (マストの前面を滑る板) とフォーク 2 本、ハンドの爪 2 枚
                Visuals.Create("carriage_plate", PrimitiveType.Cube, carriage.transform, Tuning.RosToUnity(0.0f, 0f, 0f), Quaternion.identity,
                               new Vector3(0.4f, 0.16f, 0.03f), mastMat);
                foreach (float side in new[] { 0.10f, -0.10f })
                {
                    Visuals.Create("fork", PrimitiveType.Cube, carriage.transform, Tuning.RosToUnity(0.15f, side, -0.095f), Quaternion.identity,
                                   new Vector3(0.03f, 0.02f, 0.3f), handMat);
                }
                foreach (float side in new[] { 0.19f, -0.19f })
                {
                    Visuals.Create("finger", PrimitiveType.Cube, carriage.transform, Tuning.RosToUnity(0.26f, side, 0f), Quaternion.identity,
                                   new Vector3(0.02f, 0.14f, 0.22f), handMat);
                }
                var hand = new GameObject("hand");
                hand.transform.SetParent(carriage.transform, false);
                hand.transform.localPosition = Tuning.RosToUnity(Tuning.BuilderHandForward - 0.36f, 0f, 0f);
                // 操縦カメラはキャリッジに載せる (ハンドと荷物が常に見える)。少し下を向ける
                var cam = new GameObject("camera_mount");
                cam.transform.SetParent(carriage.transform, false);
                cam.transform.localPosition = Tuning.RosToUnity(-0.06f, 0f, 0.26f);
                cam.transform.localRotation = Quaternion.Euler(22f, 0f, 0f);
                cameraMount = cam.transform;
                lift = root.AddComponent<BuilderLift>();
                lift.Setup(null, carriage.transform, hand.transform, BuilderLift.MinHeight);   // 機体の原点は床の高さ
            }

            // ---- 装甲板 4 枚: (±0.35, ±0.35, 0.45)、外向き 45 度。板は 0.15 x 0.15 ----
            var plates = new ArmorPlate[4];
            float[] ax = { 0.35f, -0.35f, -0.35f, 0.35f };
            float[] ay = { 0.35f, 0.35f, -0.35f, -0.35f };
            float[] ayaw = { Mathf.PI / 4f, Mathf.PI * 3f / 4f, -Mathf.PI * 3f / 4f, -Mathf.PI / 4f };
            var agent = root.AddComponent<RobotAgent>();
            for (int i = 0; i < 4; i++)
            {
                var holder = new GameObject($"armor_{i + 1}");
                holder.transform.SetParent(root.transform, false);
                holder.transform.localPosition = Tuning.RosToUnity(ax[i], ay[i], 0.45f);
                holder.transform.localRotation = Quaternion.Euler(0f, Tuning.RosYawToUnityDeg(ayaw[i]), 0f);
                var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plate.name = "plate";
                plate.layer = Tuning.LayerRobot;
                plate.transform.SetParent(holder.transform, false);
                plate.transform.localScale = new Vector3(0.15f, 0.15f, 0.03f);   // 厚みは当たり判定のため 3 cm
                var armor = plate.AddComponent<ArmorPlate>();
                armor.Setup(agent, i, teamColor);
                plates[i] = armor;
                // 板を支える柱
                Visuals.Create("post", PrimitiveType.Cylinder, holder.transform,
                               new Vector3(0f, -0.19f, 0f), Quaternion.identity, new Vector3(0.02f, 0.19f, 0.02f), gray);
            }

            // ---- 番号札 (上から見ても分かるように) ----
            GameObject sign = Visuals.Create("sign", PrimitiveType.Cube, root.transform,
                Tuning.RosToUnity(-0.2f, 0f, 0.62f), Quaternion.identity, new Vector3(0.22f, 0.22f, 0.02f),
                MaterialLibrary.Color(teamColor, 0.4f));
            // TextMesh は -z 側から読める向きに描かれる。後ろ (自分の追従カメラ側) と
            // 前 (相手側) の両面に置く。札のスケールを打ち消して文字を実寸で指定する。
            foreach (float side in new[] { -1f, 1f })
            {
                var label = new GameObject(side < 0f ? "label_back" : "label_front");
                label.transform.SetParent(sign.transform, false);
                label.transform.localPosition = new Vector3(0f, 0f, 0.6f * side);
                label.transform.localRotation = Quaternion.Euler(0f, side < 0f ? 0f : 180f, 0f);
                label.transform.localScale = new Vector3(1f / 0.22f, 1f / 0.22f, 1f / 0.02f);
                var text = label.AddComponent<TextMesh>();
                text.font = UiKit.Font;
                text.text = (index + 1).ToString();
                text.fontSize = 64;
                text.characterSize = 0.025f;
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.color = Color.white;
                label.GetComponent<MeshRenderer>().sharedMaterial = UiKit.Font.material;
            }

            // ---- ミニマップ用マーカー (MinimapOnly レイヤ、3 m 上空に浮かぶ矢印) ----
            GameObject marker = Visuals.Create("minimap_marker", PrimitiveType.Cube, root.transform,
                new Vector3(0f, 3f, 0.1f), Quaternion.identity, new Vector3(0.7f, 0.05f, 1.1f),
                MaterialLibrary.Unlit(teamColor), Tuning.LayerMinimapOnly);
            Visuals.Create("nose", PrimitiveType.Cube, marker.transform,
                new Vector3(0f, 0f, 0.6f), Quaternion.identity, builder ? new Vector3(0.9f, 1f, 0.25f) : new Vector3(0.5f, 1f, 0.3f),
                MaterialLibrary.Unlit(builder ? new Color(1f, 0.8f, 0.3f) : Color.white), Tuning.LayerMinimapOnly);

            // ---- コンポーネントの配線 ----
            var drive = root.AddComponent<ChassisDrive>();
            drive.maxLinearVelocity = Tuning.RealTurboLinear * settings.LinearScale;
            drive.maxAngularVelocity = Tuning.RealTurboAngular * settings.LinearScale;
            drive.maxLinearAcceleration = Tuning.RealLinearAccel * settings.AccelScale;
            drive.maxAngularAcceleration = Tuning.RealAngularAccel * settings.AccelScale;

            Shooter shooter = null;
            if (!builder)
            {
                shooter = root.AddComponent<Shooter>();
                shooter.Interval = settings.FireInterval;
                shooter.Muzzle = muzzle;
                shooter.Rollers = rollers.ToArray();
            }

            agent.Index = index;
            agent.Team = team;
            agent.Kind = kind;
            agent.Drive = drive;
            agent.Shooter = shooter;
            agent.Lift = lift;
            if (lift != null) lift.Owner = agent;
            agent.CameraMount = cameraMount;
            agent.Marker = marker.transform;
            agent.NormalLinear = Tuning.RealNormalLinear * settings.LinearScale;
            agent.NormalAngular = Tuning.RealNormalAngular * settings.LinearScale;
            agent.TurboLinear = Tuning.RealTurboLinear * settings.LinearScale;
            agent.TurboAngular = Tuning.RealTurboAngular * settings.LinearScale;
            if (shooter != null) shooter.Setup(agent);
            agent.Setup(startPose, plates);

            // 重心を低めに置いて、バンパー同士の押し合いで転ばないようにする。
            body.centerOfMass = new Vector3(0f, 0.12f, 0f);
            return agent;
        }

        private static GameObject Box(GameObject parent, string name, Vector3 localPos, Quaternion localRot,
                                      Vector3 size, Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = Tuning.LayerRobot;
            if (!collider)
            {
                Object.Destroy(go.GetComponent<Collider>());
            }
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }
    }
}
