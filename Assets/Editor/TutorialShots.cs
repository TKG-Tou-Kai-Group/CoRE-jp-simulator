using System.Collections.Generic;
using System.IO;
using CoreSim;
using CoreSim.Rules;
using UnityEditor;
using UnityEngine;

namespace CoreSim.Editor
{
    /// <summary>
    /// チュートリアル・タイトル用の絵を Editor で作る (Unity メニュー「CoRE → チュートリアルの絵を撮る」)。
    /// フィールドと機体を一時的に組み立て、好きな位置に置いてカメラで撮り、Assets/Resources/Tutorial/ に PNG を書く。
    /// 構図は <see cref="Shots"/> の一覧で決める (カメラの位置と向き、機体の種類・位置・向き、飛んでいるディスク)。
    /// 一人称の画面では撮れない「全景」「撃ち合う 2 台を斜め上から」「3 台の横並び」などを作るためのもの。
    /// </summary>
    public static class TutorialShots
    {
        private const string OutDir = "Assets/Resources/Tutorial";

        private struct Bot
        {
            public RobotKind Kind;
            public bool Head;
            public bool Red;
            public Vector3 Pos;       // 床の上の位置 (y は 0)
            public float YawDeg;      // 0 = +z を向く、90 = +x を向く
            public Bot(RobotKind kind, bool red, Vector3 pos, float yaw, bool head = false) { Kind = kind; Red = red; Pos = pos; YawDeg = yaw; Head = head; }
        }

        private sealed class Shot
        {
            public string Name;
            public int Width, Height;
            public Vector3 Camera, LookAt;
            public float Fov = 50f;
            public Bot[] Bots = new Bot[0];
            public Vector3[] Discs = new Vector3[0];   // 飛んでいるディスク (見た目だけ)
            public Vector3 DiscDir = Vector3.right;
            public bool Strider;
            public Vector3 StriderPos;
            public float StriderYaw;
        }

        /// <summary>構図の一覧。カメラや機体の位置はここを書き換えて調整する。</summary>
        private static readonly Shot[] Shots =
        {
            new Shot
            {
                Name = "intro_field", Width = 1520, Height = 860, Fov = 52f,
                // 注視点をフィールドの中心 (原点) にすると、フィールドの中心が画像の中心に来る
                Camera = new Vector3(-15f, 12.5f, -16f), LookAt = new Vector3(0f, 0f, 0f),
                Bots = new[]
                {
                    new Bot(RobotKind.Attacker, true, new Vector3(-5f, 0f, -1.5f), 70f, head: true),
                    new Bot(RobotKind.Attacker, true, new Vector3(-6.5f, 0f, 2.5f), 100f),
                    new Bot(RobotKind.Builder, true, new Vector3(-3.5f, 0f, -4.5f), 40f),
                    new Bot(RobotKind.Attacker, false, new Vector3(4.5f, 0f, 1.5f), -110f, head: true),
                    new Bot(RobotKind.Attacker, false, new Vector3(6f, 0f, -2f), -80f),
                    new Bot(RobotKind.AutoAttacker, false, new Vector3(2.5f, 0f, 3.5f), -140f),
                },
                Discs = new[] { new Vector3(-2.5f, 0.5f, -0.6f), new Vector3(-0.5f, 0.55f, -0.1f), new Vector3(1.5f, 0.5f, 0.4f) },
                DiscDir = new Vector3(1f, 0f, 0.25f),
            },
            new Shot
            {
                // 中央の共通コンテナ置き場 (x ±1.15, z ±0.8) と重ならないよう、北側の空き地 (z ≈ 3.5) で撮る。カメラは北の壁の外から南向き
                Name = "mode_shoot", Width = 840, Height = 480, Fov = 45f,
                Camera = new Vector3(-0.4f, 3.4f, 9.9f), LookAt = new Vector3(-1f, 0.35f, 3.4f),
                Bots = new[]
                {
                    new Bot(RobotKind.Attacker, true, new Vector3(-4.2f, 0f, 3.7f), 85f),
                    new Bot(RobotKind.Attacker, false, new Vector3(2.2f, 0f, 3.1f), -95f),
                },
                Discs = new[] { new Vector3(-2.8f, 0.5f, 3.6f), new Vector3(-1.2f, 0.55f, 3.45f), new Vector3(0.4f, 0.5f, 3.3f) },
                DiscDir = new Vector3(1f, 0f, -0.08f),
            },
            new Shot
            {
                // 横並び: ヘッド (中央)、ビルダー (画面の右)、アタッカー (画面の左)。カメラは北から南向きなので、画面の右 = -x。
                // 左右は少し外を向く。中央の構造物を避けて北側の空き地で撮る
                Name = "mode_tournament", Width = 840, Height = 480, Fov = 40f,
                Camera = new Vector3(-1f, 1.5f, 9.1f), LookAt = new Vector3(-1f, 0.45f, 3.5f),
                Bots = new[]
                {
                    new Bot(RobotKind.Attacker, true, new Vector3(-1f, 0f, 3.5f), 0f, head: true),
                    new Bot(RobotKind.Builder, true, new Vector3(-2.7f, 0f, 3.7f), -20f),
                    new Bot(RobotKind.Attacker, true, new Vector3(0.7f, 0f, 3.7f), 20f),
                },
            },
            new Shot
            {
                Name = "mode_strider", Width = 840, Height = 480, Fov = 45f,
                Camera = new Vector3(-9.5f, 1.7f, -11.3f), LookAt = new Vector3(-6.3f, 0.3f, -8.45f),
                Strider = true, StriderPos = new Vector3(-7.2f, 0f, -8.45f), StriderYaw = 90f,
            },
            new Shot
            {
                Name = "role_attacker", Width = 880, Height = 440, Fov = 38f,
                Camera = new Vector3(-6.4f, 1.6f, -2.8f), LookAt = new Vector3(-4f, 0.4f, 0f),
                Bots = new[] { new Bot(RobotKind.Attacker, true, new Vector3(-4f, 0f, 0f), 150f) },
            },
            new Shot
            {
                Name = "role_builder", Width = 880, Height = 440, Fov = 38f,
                Camera = new Vector3(-6.4f, 1.7f, -2.8f), LookAt = new Vector3(-4f, 0.5f, 0f),
                Bots = new[] { new Bot(RobotKind.Builder, true, new Vector3(-4f, 0f, 0f), 150f) },
            },
            new Shot
            {
                Name = "role_auto", Width = 880, Height = 440, Fov = 38f,
                Camera = new Vector3(-6.4f, 1.6f, -2.8f), LookAt = new Vector3(-4f, 0.4f, 0f),
                Bots = new[] { new Bot(RobotKind.AutoAttacker, true, new Vector3(-4f, 0f, 0f), 150f) },
            },
        };

        [MenuItem("CoRE/チュートリアルの絵を撮る (全部)")]
        public static void CaptureAll()
        {
            foreach (Shot s in Shots) Capture(s);
            AssetDatabase.Refresh();
            Debug.Log($"[TutorialShots] {Shots.Length} 枚を {OutDir} に書いた");
        }

        [MenuItem("CoRE/チュートリアルの絵を撮る (intro_field だけ)")]
        public static void CaptureIntro() { Capture(Shots[0]); AssetDatabase.Refresh(); }

        private static void Capture(Shot shot)
        {
            var temp = new GameObject("TutorialShot_" + shot.Name);
            GameObject arena = null;
            // Edit モードではシェーダの変種が非同期にコンパイルされ、終わるまで仮のシェーダ (ピンク) で描かれる。
            // 撮っている間は同期にして、本物のシェーダで描く
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                arena = ArenaBuilder.Build(lowDetail: false);
                var settings = new GameSettings();
                int red = 1, blue = 0;
                foreach (Bot b in shot.Bots)
                {
                    int index = b.Red ? red : blue;      // 奇数 = 赤、偶数 = 青
                    if (b.Red) red += 2; else blue += 2;
                    var pose = new Pose(b.Pos, Quaternion.Euler(0f, b.YawDeg, 0f));
                    RobotAgent agent = RobotFactory.Create(index, pose, settings, b.Kind);
                    agent.transform.SetParent(temp.transform, true);
                    if (b.Head) agent.MarkAsHead();
                    if (agent.Lift != null)
                    {
                        // ビルダーのハンドを少し上げる (見た目)
                        agent.Lift.Setup(agent, agent.Lift.Carriage, agent.Lift.Hand, BuilderLift.MinHeight);
                        agent.Lift.ApplyRemote(0.45f, null);
                    }
                }
                foreach (Vector3 p in shot.Discs)
                {
                    Visuals.Create("Disc", PrimitiveType.Cylinder, temp.transform, p, Quaternion.LookRotation(shot.DiscDir.normalized, Vector3.up),
                                   new Vector3(Tuning.DiscRadius * 2f, Tuning.DiscThickness / 2f, Tuning.DiscRadius * 2f), Disc.Material);
                }
                if (shot.Strider)
                {
                    Strider s = Strider.Create(shot.StriderPos + Vector3.up * (Strider.Thigh * 2f * Mathf.Cos(Strider.StandHip * Mathf.Deg2Rad)), Quaternion.Euler(0f, shot.StriderYaw, 0f));
                    s.transform.SetParent(temp.transform, true);
                    s.SetTargets(Strider.StandHip + 20f, Strider.StandKnee + 15f, Strider.StandHip - 20f, Strider.StandKnee - 15f);
                    ApplyJointPose(s);
                }

                var camGo = new GameObject("ShotCamera");
                camGo.transform.SetParent(temp.transform, false);
                var cam = camGo.AddComponent<Camera>();
                cam.transform.position = shot.Camera;
                cam.transform.LookAt(shot.LookAt);
                cam.fieldOfView = shot.Fov;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 200f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.07f, 0.09f, 0.13f);
                cam.cullingMask = ~((1 << Tuning.LayerMinimapOnly) | (1 << 5));
                cam.allowMSAA = true;

                var rt = new RenderTexture(shot.Width, shot.Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
                cam.targetTexture = rt;
                cam.Render();   // 1 回目でシェーダがコンパイルされる。念のため 2 回描く
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(shot.Width, shot.Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, shot.Width, shot.Height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;
                Directory.CreateDirectory(OutDir);
                File.WriteAllBytes(Path.Combine(OutDir, shot.Name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                rt.Release();
                Object.DestroyImmediate(rt);
                Debug.Log($"[TutorialShots] {shot.Name}.png ({shot.Width}x{shot.Height})");
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async;
                if (arena != null) Object.DestroyImmediate(arena);
                Object.DestroyImmediate(temp);
                UnityEngine.AI.NavMesh.RemoveAllNavMeshData();
            }
        }

        /// <summary>Edit モードでは物理が回らないので、関節の目標角をそのまま姿勢に反映する (見た目だけ)。</summary>
        private static void ApplyJointPose(Strider s)
        {
            for (int i = 0; i < 4; i++)
            {
                s.Hips[i].transform.localRotation = s.Hips[i].transform.localRotation * Quaternion.Euler(s.Hips[i].xDrive.target, 0f, 0f);
                s.Knees[i].transform.localRotation = Quaternion.Euler(s.Knees[i].xDrive.target, 0f, 0f);
            }
        }
    }
}
