using UnityEngine;

namespace CoreSim
{
    /// <summary>
    /// ストライダー (4 本脚、脚は腿と脛の 2 自由度、第 15 章)。ArticulationBody で胴体 → 股関節 (ピッチ) → 膝 (ピッチ)
    /// の連結を作る。脚の寸法はオープンソースの 4 足歩行ロボット MEVIUS (東大 JSK) を目安にし、リンク長は 15.3 の
    /// 200〜400 mm に収める。脚の番号: 0 前左、1 前右、2 後左、3 後右。トロットの対角ペア A = 0 + 3、B = 1 + 2。
    /// </summary>
    public sealed class Strider : MonoBehaviour
    {
        public const float Thigh = 0.30f;      // 股関節から膝 [m]。15.3 の上限は 400 mm。0.22 だとハードル (30 cm) で後脚がつかえた
        public const float Calf = 0.30f;       // 膝から脚先 [m]。直立の高さは約 0.54 m、脚を畳めば脚先を 0.45 m 上げられる
        public const float HipX = 0.11f;       // 胴体の中心から後脚の股関節まで (左右)。前脚と左右で 12 cm ずらして、振ったときに前後の脚が当たらないようにする
        public const float FrontHipX = 0.23f;  // 前脚は外側に付ける (脚の太さ 5 cm + 開きぶんを見て、後脚との差は 12 cm)
        public const float HipZ = 0.28f;       // (前後)。長いほどピッチ方向に安定し、ハードルを越えるとき前後の脚が同時に引っ掛からない
        public const float Splay = 10f;        // 脚を外へ開く角度 [deg] (股関節の外転は無いので固定)。接地幅を股関節の幅より広くする
        /// <summary>横の安定の補助: ロールを戻すトルク [N·m/rad] と減衰。脚にピッチしか無いので、無いと 2 本脚で接地した
        /// 瞬間に横へ倒れる (実機は股関節の外転で踏ん張る)。0 にすれば補助なし。</summary>
        public const float RollAssist = 55f;   // 股関節の幅を広げたぶん少し弱く
        public const float RollAssistDamping = 6f;
        /// <summary>向きの補助: 進行方向 (<see cref="Heading"/>) へ向け直すトルク [N·m/rad] と減衰。脚にピッチしか無いので
        /// 左右の脚の差で向きを直せず、歩くうちに回ってコースから出る。0 にすれば補助なし。</summary>
        public const float YawAssist = 60f;
        public const float YawAssistDamping = 10f;
        /// <summary>ピッチの補助 (弱め): 胴体を水平に戻すトルク [N·m/rad] と減衰。ハードルを越えるときに前を持ち上げる動きは
        /// 残したいので弱くする。0 にすれば補助なし。</summary>
        public const float PitchAssist = 25f;
        public const float PitchAssistDamping = 4f;

        /// <summary>向きの補助が向ける進行方向 (水平)。zero なら向きの補助なし。</summary>
        public Vector3 Heading;
        public const float BodyMass = 6f;
        public const float HipLimit = 90f;     // 股関節の可動域 [deg] (前後)。ハードル (30 cm) を越えるには腿を水平近くまで上げる
        public const float KneeMin = 5f;       // 膝 (脛を後ろへ曲げる向きが正)
        public const float KneeMax = 160f;     // 脚を畳んで脚先を股関節の近くまで引き上げられる
        public const float StandHip = 25f;     // 直立時の腿 (前へ 25 度)。膝 50 度と合わせると脚先が股関節の真下に来て、胴体の高さは約 0.54 m
        public const float StandKnee = 50f;    // 直立時の膝
        // 関節のサーボ。ArticulationDrive の stiffness / damping は目標角との差 (Unity は度で指定するが内部はラジアン) に掛かる
        // ので、度で考えるより桁が大きい値が要る。胴体 6 kg を 4 本で支えるには股関節に 5〜10 N·m。
        // (最初 6 / 0.6 / 40 にしたら立てなかった)
        public const float Stiffness = 2500f;
        public const float Damping = 120f;
        public const float ForceLimit = 200f;  // [N·m] 実機 (Unitree Go2 23.7、MEVIUS 30 前後) より強め。手触り優先 (脚が長いぶん必要)

        public ArticulationBody Body { get; private set; }
        public ArticulationBody[] Hips { get; } = new ArticulationBody[4];
        public ArticulationBody[] Knees { get; } = new ArticulationBody[4];
        public Transform[] Feet { get; } = new Transform[4];

        /// <summary>脚先のワールド位置。</summary>
        public Vector3 Foot(int i) => Feet[i].position;

        private void FixedUpdate()
        {
            if (Body == null || RollAssist <= 0f) return;
            // 横の安定の補助: 胴体のロール (進行方向の軸回りの傾き) を戻す
            Vector3 up = Body.transform.up;
            Vector3 forward = Body.transform.forward;
            float roll = Mathf.Asin(Mathf.Clamp(Vector3.Dot(up, Vector3.Cross(Vector3.up, forward).normalized), -1f, 1f));
            float rollRate = Vector3.Dot(Body.angularVelocity, forward);
            // 右へ傾くと roll > 0。forward 軸回りの正のトルクは (左手系なので) 胴体を左へ傾けるので、roll にそのまま掛ける
            Body.AddTorque(forward * (roll * RollAssist - rollRate * RollAssistDamping), ForceMode.Force);

            if (PitchAssist > 0f)
            {
                // 前が上がると pitch > 0。right 軸回りの正のトルクは (左手系なので) 前を下げる
                Vector3 right = Body.transform.right;
                float pitch = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f));
                float pitchRate = Vector3.Dot(Body.angularVelocity, right);
                Body.AddTorque(right * (pitch * PitchAssist - pitchRate * PitchAssistDamping), ForceMode.Force);
            }
            if (YawAssist > 0f && Heading.sqrMagnitude > 1e-4f)
            {
                // 進行方向との向きの差 (上から見て時計回りが正) を戻す。y 軸回りの正のトルクは上から見て時計回り
                Vector3 flatForward = Vector3.ProjectOnPlane(forward, Vector3.up);
                if (flatForward.sqrMagnitude > 1e-4f)
                {
                    float yawErr = Vector3.SignedAngle(flatForward, Heading, Vector3.up) * Mathf.Deg2Rad;
                    float yawRate = Vector3.Dot(Body.angularVelocity, Vector3.up);
                    Body.AddTorque(Vector3.up * (yawErr * YawAssist - yawRate * YawAssistDamping), ForceMode.Force);
                }
            }
        }
        public bool Fallen => Body.transform.up.y < 0.35f;

        /// <summary>
        /// 関節の目標角 [deg]。hip は前へ振る向きが正、knee は脛を後ろへ曲げる向きが正。
        /// 対角ペア A (前左 + 後右) と B (前右 + 後左)。
        /// </summary>
        public void SetTargets(float hipA, float kneeA, float hipB, float kneeB)
        {
            SetLeg(0, hipA, kneeA);
            SetLeg(3, hipA, kneeA);
            SetLeg(1, hipB, kneeB);
            SetLeg(2, hipB, kneeB);
        }

        private void SetLeg(int i, float hip, float knee)
        {
            // Unity の x 軸回りの正の回転は腿を後ろへ振るので符号を反転する
            ArticulationDrive h = Hips[i].xDrive;
            h.target = Mathf.Clamp(-hip, -HipLimit, HipLimit);
            Hips[i].xDrive = h;
            ArticulationDrive k = Knees[i].xDrive;
            k.target = Mathf.Clamp(knee, KneeMin, KneeMax);
            Knees[i].xDrive = k;
        }

        public static Strider Create(Vector3 position, Quaternion rotation)
        {
            Material bodyMat = MaterialLibrary.Color(new Color(0.85f, 0.55f, 0.15f), 0.5f, 0.3f);
            Material legMat = MaterialLibrary.Color(new Color(0.3f, 0.32f, 0.36f), 0.4f, 0.5f);
            Material footMat = MaterialLibrary.Color(new Color(0.1f, 0.1f, 0.12f), 0.2f);
            var grip = new PhysicsMaterial("strider_foot") { dynamicFriction = 1.0f, staticFriction = 1.2f, frictionCombine = PhysicsMaterialCombine.Maximum };

            var root = new GameObject("Strider");
            root.transform.SetPositionAndRotation(position, rotation);
            root.layer = Tuning.LayerRobot;
            var body = root.AddComponent<ArticulationBody>();
            body.mass = BodyMass;
            body.solverIterations = 32;
            body.solverVelocityIterations = 32;
            body.linearDamping = 0.05f;
            body.angularDamping = 0.5f;
            body.centerOfMass = new Vector3(0f, -0.03f, 0f);   // 重心は少し低く (バッテリーが底にある想定)
            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(0.30f, 0.10f, HipZ * 2f + 0.08f);
            Visuals.Create("body", PrimitiveType.Cube, root.transform, Vector3.zero, Quaternion.identity, new Vector3(0.30f, 0.10f, HipZ * 2f + 0.08f), bodyMat);
            Visuals.Create("head", PrimitiveType.Cube, root.transform, new Vector3(0f, 0.03f, HipZ + 0.07f), Quaternion.identity, new Vector3(0.12f, 0.08f, 0.08f), footMat);
            // 前脚の股関節を胴体の外側まで出す肩 (見た目)
            Visuals.Create("shoulders", PrimitiveType.Cube, root.transform, new Vector3(0f, 0f, HipZ), Quaternion.identity, new Vector3(FrontHipX * 2f, 0.08f, 0.10f), bodyMat);
            var strider = root.AddComponent<Strider>();
            strider.Body = body;

            for (int i = 0; i < 4; i++)
            {
                float hx = i < 2 ? FrontHipX : HipX;       // 前脚は外側
                float sx = i % 2 == 0 ? -hx : hx;          // 偶数 = 左
                float sz = i < 2 ? HipZ : -HipZ;           // 0, 1 = 前
                var hipGo = new GameObject($"hip_{i}");
                hipGo.transform.SetParent(root.transform, false);
                hipGo.transform.localPosition = new Vector3(sx, 0f, sz);
                // 左脚は左へ、右脚は右へ開く。後脚は開かない。
                // z 軸回りの正の回転は (0,-1,0) を +x 側へ倒すので、左脚 (x < 0) は負の角で外 (−x) へ、右脚は正の角で外 (+x) へ。
                // (以前は符号が逆で、前脚が内側へ寄って後脚と当たっていた)
                float splay = i < 2 ? Splay : 0f;
                hipGo.transform.localRotation = Quaternion.Euler(0f, 0f, sx < 0f ? -splay : splay);
                hipGo.layer = Tuning.LayerRobot;
                var hip = hipGo.AddComponent<ArticulationBody>();
                hip.mass = 0.7f;
                hip.jointType = ArticulationJointType.RevoluteJoint;
                hip.anchorPosition = Vector3.zero;
                hip.anchorRotation = Quaternion.identity;            // 回転軸 = 親の x (左右) → ピッチ
                hip.twistLock = ArticulationDofLock.LimitedMotion;
                hip.xDrive = new ArticulationDrive { stiffness = Stiffness, damping = Damping, forceLimit = ForceLimit, lowerLimit = -HipLimit, upperLimit = HipLimit, target = -StandHip };
                hip.jointFriction = 0.02f;
                hip.angularDamping = 0.3f;
                var thigh = hipGo.AddComponent<CapsuleCollider>();
                thigh.direction = 1;   // y
                thigh.radius = 0.025f;
                thigh.height = Thigh;
                thigh.center = new Vector3(0f, -Thigh / 2f, 0f);
                Visuals.Create("thigh", PrimitiveType.Capsule, hipGo.transform, new Vector3(0f, -Thigh / 2f, 0f), Quaternion.identity,
                               new Vector3(0.05f, Thigh / 2f, 0.05f), legMat);
                Visuals.Create("hip_joint", PrimitiveType.Sphere, hipGo.transform, Vector3.zero, Quaternion.identity, new Vector3(0.07f, 0.07f, 0.07f), footMat);

                var kneeGo = new GameObject($"knee_{i}");
                kneeGo.transform.SetParent(hipGo.transform, false);
                kneeGo.transform.localPosition = new Vector3(0f, -Thigh, 0f);
                kneeGo.layer = Tuning.LayerRobot;
                var knee = kneeGo.AddComponent<ArticulationBody>();
                knee.mass = 0.5f;
                knee.jointType = ArticulationJointType.RevoluteJoint;
                knee.anchorPosition = Vector3.zero;
                knee.anchorRotation = Quaternion.identity;
                knee.twistLock = ArticulationDofLock.LimitedMotion;
                knee.xDrive = new ArticulationDrive { stiffness = Stiffness, damping = Damping, forceLimit = ForceLimit, lowerLimit = KneeMin, upperLimit = KneeMax, target = StandKnee };
                knee.jointFriction = 0.02f;
                knee.angularDamping = 0.3f;
                var calf = kneeGo.AddComponent<CapsuleCollider>();
                calf.direction = 1;
                calf.radius = 0.02f;
                calf.height = Calf;
                calf.center = new Vector3(0f, -Calf / 2f, 0f);
                calf.sharedMaterial = grip;
                var footCol = kneeGo.AddComponent<SphereCollider>();
                footCol.radius = 0.03f;
                footCol.center = new Vector3(0f, -Calf, 0f);
                footCol.sharedMaterial = grip;
                Visuals.Create("calf", PrimitiveType.Capsule, kneeGo.transform, new Vector3(0f, -Calf / 2f, 0f), Quaternion.identity,
                               new Vector3(0.04f, Calf / 2f, 0.04f), legMat);
                Visuals.Create("foot", PrimitiveType.Sphere, kneeGo.transform, new Vector3(0f, -Calf, 0f), Quaternion.identity, new Vector3(0.06f, 0.06f, 0.06f), footMat);
                var foot = new GameObject("foot_tip");
                foot.transform.SetParent(kneeGo.transform, false);
                foot.transform.localPosition = new Vector3(0f, -Calf, 0f);

                strider.Hips[i] = hip;
                strider.Knees[i] = knee;
                strider.Feet[i] = foot.transform;
            }
            return strider;
        }
    }
}
