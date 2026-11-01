using UnityEngine;

namespace CoreSim
{
    /// <summary>
    /// 1 人のプレイヤーが見る画面: 一人称カメラ、ミニマップ、HUD。
    /// CoRE の大会ルールに合わせて視点は機体のカメラ (一人称) だけ。
    /// 画面分割のときは <see cref="Viewport"/> で左右に分ける。
    /// </summary>
    public sealed class PlayerView : MonoBehaviour
    {
        /// <summary>ROS 2 版の操縦カメラ (sample_robot.simulation.xacro) の水平画角。</summary>
        public const float HorizontalFovDeg = 80f;

        public RobotAgent Robot;
        public PlayerCommandSource Input;
        public Rect Viewport = new Rect(0f, 0f, 1f, 1f);
        public HudView Hud;

        private Camera m_Camera;
        private Camera m_Minimap;
        private RenderTexture m_MinimapTexture;
        private int m_FrameCounter;

        public Camera Camera => m_Camera;

        public static PlayerView Create(RobotAgent robot, PlayerCommandSource input, Rect viewport, int playerIndex,
                                        GameSettings settings, Transform hudParent)
        {
            var go = new GameObject($"PlayerView_{playerIndex + 1}");
            var view = go.AddComponent<PlayerView>();
            view.Robot = robot;
            view.Input = input;
            view.Viewport = viewport;

            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(go.transform, false);
            view.m_Camera = camGo.AddComponent<Camera>();
            view.m_Camera.rect = viewport;
            view.m_Camera.nearClipPlane = 0.05f;
            view.m_Camera.farClipPlane = 300f;
            view.m_Camera.cullingMask = ~((1 << Tuning.LayerMinimapOnly) | (1 << 5));
            view.m_Camera.depth = playerIndex;
            camGo.AddComponent<AudioListener>().enabled = playerIndex == 0;
            view.ApplyFov();

            int size = Application.isMobilePlatform ? 192 : 256;
            view.m_MinimapTexture = new RenderTexture(size, size, 16) { name = "minimap" };
            var mapGo = new GameObject("MinimapCamera");
            mapGo.transform.SetParent(go.transform, false);
            view.m_Minimap = mapGo.AddComponent<Camera>();
            view.m_Minimap.orthographic = true;
            view.m_Minimap.orthographicSize = 7f;
            view.m_Minimap.targetTexture = view.m_MinimapTexture;
            view.m_Minimap.cullingMask = (1 << 0) | (1 << Tuning.LayerRobot) | (1 << Tuning.LayerMinimapOnly);
            view.m_Minimap.clearFlags = CameraClearFlags.SolidColor;
            view.m_Minimap.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
            view.m_Minimap.depth = -10;
            view.m_Minimap.nearClipPlane = 1f;
            view.m_Minimap.farClipPlane = 40f;
            view.m_Minimap.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            view.Hud = HudView.Create(hudParent, viewport, playerIndex, view.m_MinimapTexture, settings);
            view.SnapCamera();
            return view;
        }

        /// <summary>水平 80 度を、ビューポートのアスペクト比から垂直画角に直す。</summary>
        private void ApplyFov()
        {
            float aspect = (Screen.width * Viewport.width) / Mathf.Max(1f, Screen.height * Viewport.height);
            m_Camera.fieldOfView = Camera.HorizontalToVerticalFieldOfView(HorizontalFovDeg, aspect);
        }

        private void SnapCamera()
        {
            if (Robot == null)
            {
                return;
            }
            ComputeCamera(out Vector3 pos, out Quaternion rot);
            m_Camera.transform.SetPositionAndRotation(pos, rot);
        }

        /// <summary>機体の camera_link (0.45, 0, 0.28) に固定。機体の傾きはそのまま見える。</summary>
        private void ComputeCamera(out Vector3 pos, out Quaternion rot)
        {
            pos = Robot.CameraMount.position;
            rot = Robot.CameraMount.rotation;
        }

        private void LateUpdate()
        {
            if (Robot == null)
            {
                return;
            }
            if (Input != null && Time.timeScale > 0f && Input.PollToggleView())
            {
                Hud.SetMinimapVisible(!Hud.MinimapVisible);
            }
            ComputeCamera(out Vector3 pos, out Quaternion rot);
            m_Camera.transform.SetPositionAndRotation(pos, rot);
            if ((m_FrameCounter & 31) == 0)
            {
                ApplyFov();   // ウィンドウのリサイズに追従
            }

            // ミニマップは北固定、機体中心。モバイルでは 3 フレームに 1 回だけ描く。
            m_Minimap.transform.position = Robot.transform.position + Vector3.up * 20f;
            m_FrameCounter++;
            m_Minimap.enabled = Hud.MinimapVisible && (!Application.isMobilePlatform || m_FrameCounter % 3 == 0);
        }

        private void OnDestroy()
        {
            if (m_MinimapTexture != null)
            {
                m_MinimapTexture.Release();
                Destroy(m_MinimapTexture);
            }
            if (Hud != null)
            {
                Destroy(Hud.gameObject);
            }
        }
    }
}
