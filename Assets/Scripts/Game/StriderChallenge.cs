using System.Collections.Generic;
using CoreSim.Rules;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoreSim
{
    /// <summary>
    /// ストライダーチャレンジ (単独のタイムアタック)。赤サイドのストライダーエリアを QWOP 方式で歩く:
    /// Q / W = 対角ペア A の腿を前 / 後ろ (B は逆)、O / P = ペア A の膝を曲げる / 伸ばす (B は逆)。
    /// 視点は矢印キーでロボット中心に回す (奥行きが分かるよう 2D には固定しない)。R でやり直し、Esc でメニュー。
    /// </summary>
    public sealed class StriderChallenge : MonoBehaviour
    {
        private const float HipRate = 140f;    // [deg/s] キーを押している間の腿の動き
        private const float KneeRate = 160f;
        private const float SteerGain = 1.6f;  // 通路の中心からのずれ [m] あたり、向きを中心へ寄せる量 (tan)。0.5 m ずれると約 39 度
        private const float SteerMaxDeg = 55f;
        private const float HipRange = 65f;    // 直立 (腿 25 度) からの振り幅。前へ 90 度まで上げてハードルを越える
        private const float KneeRange = 90f;   // 直立 (膝 50 度) からの曲げ幅。畳めば脚先を股関節の近くまで引き上げられる

        private GameDirector m_Director;
        private GameSettings m_Settings;
        private StriderCourse m_Course;
        private Strider m_Strider;
        private Camera m_Camera;
        private float m_Yaw = 180f, m_Pitch = 18f, m_Distance = 3.2f;
        private float m_HipA, m_KneeA;          // ペア A の目標 (B は逆位相)
        private float m_Time;
        private bool m_Started, m_Finished, m_Fallen, m_OutOfLane;
        private float m_MessageUntil;
        private string m_Message = "";
        private Text m_Status, m_Zones, m_Center, m_Hint, m_Guide;
        private int m_GuideStep;          // 手ほどき: 0 腿、1 膝、2 1 歩、3 段差、4 終わり
        private float m_GuideStartX;
        private bool m_GuideHipMoved, m_GuideKneeMoved;
        private RectTransform m_Root;
        private bool m_EscHeld, m_ResetHeld;
        private readonly List<float> m_FootX = new List<float>(4);

        public static StriderChallenge Create(GameDirector director, GameSettings settings, Transform canvas)
        {
            var go = new GameObject("StriderChallenge");
            var c = go.AddComponent<StriderChallenge>();
            c.m_Director = director;
            c.m_Settings = settings;
            c.m_Course = new StriderCourse(Side.Red);
            c.BuildUi(canvas);
            c.BuildGravel();
            c.m_Camera = new GameObject("StriderCamera").AddComponent<Camera>();
            c.m_Camera.transform.SetParent(go.transform, false);
            c.m_Camera.nearClipPlane = 0.05f;
            c.m_Camera.farClipPlane = 120f;
            c.m_Camera.fieldOfView = 55f;
            c.m_Camera.cullingMask = ~((1 << Tuning.LayerMinimapOnly) | (1 << 5));
            c.m_Camera.gameObject.AddComponent<AudioListener>();
            c.Spawn();
            return c;
        }

        private void BuildUi(Transform canvas)
        {
            m_Root = UiKit.Panel(canvas, "StriderHud", new Color(0f, 0f, 0f, 0f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            RectTransform top = UiKit.Rect(m_Root, "Top", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(480f, -70f), new Vector2(920f, 110f));
            UiKit.Round(UiKit.Image(top, "bg", new Color(0f, 0f, 0f, 0.5f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero));
            m_Status = UiKit.Label(top, "Status", "", 22, Color.white, TextAnchor.UpperLeft);
            m_Status.rectTransform.offsetMin = new Vector2(16f, 0f);
            m_Status.rectTransform.offsetMax = new Vector2(-16f, -10f);
            m_Zones = UiKit.Label(top, "Zones", "", 18, new Color(0.9f, 0.95f, 1f), TextAnchor.LowerLeft);
            m_Zones.rectTransform.offsetMin = new Vector2(16f, 10f);
            m_Zones.rectTransform.offsetMax = new Vector2(-16f, 0f);
            // 手ほどき (最初の 4 手順。達成したら次へ。ガイド OFF なら出ない)
            RectTransform guide = UiKit.Rect(m_Root, "Guide", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(480f, -160f), new Vector2(920f, 60f));
            UiKit.Round(UiKit.Image(guide, "bg", new Color(0.05f, 0.1f, 0.2f, 0.75f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero));
            m_Guide = UiKit.Label(guide, "Text", "", 18, new Color(1f, 0.92f, 0.6f), TextAnchor.MiddleLeft);
            m_Guide.rectTransform.offsetMin = new Vector2(16f, 0f);
            m_Guide.rectTransform.offsetMax = new Vector2(-16f, 0f);
            m_Guide.horizontalOverflow = HorizontalWrapMode.Wrap;
            RectTransform center = UiKit.Rect(m_Root, "Center", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 160f), new Vector2(1200f, 80f));
            m_Center = UiKit.Label(center, "Text", "", 32, new Color(1f, 0.85f, 0.4f));
            RectTransform hint = UiKit.Rect(m_Root, "Hint", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(1600f, 80f));
            UiKit.Round(UiKit.Image(hint, "bg", new Color(0f, 0f, 0f, 0.45f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero));
            m_Hint = UiKit.Label(hint, "Text",
                "Q / W: 腿を前 / 後ろ (対角のペアが逆に動く)   O / P: 膝を曲げる / 伸ばす   矢印: 視点を回す   ホイール: 距離   R: リトライ   Shift+R: 最初から   Esc: メニュー\n" +
                "パッド: 左スティック上下 = 腿、右スティック上下 = 膝、十字キー = 視点", 15, new Color(0.9f, 0.9f, 0.95f));
            m_Hint.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        /// <summary>砂利ゾーン: 滑りやすい床と小さな凹凸 (砂利そのものは置かない)。</summary>
        private void BuildGravel()
        {
            float x0 = -3.71f, x1 = -2.29f;
            var mat = new PhysicsMaterial("gravel") { dynamicFriction = 0.35f, staticFriction = 0.4f, frictionCombine = PhysicsMaterialCombine.Minimum };
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "GravelFloor";
            floor.transform.SetParent(transform, false);
            floor.transform.position = new Vector3((x0 + x1) / 2f, 0.1f - 0.005f, m_Course.LaneCenterZ);
            floor.transform.localScale = new Vector3(x1 - x0, 0.01f, StriderCourse.LaneFarZ - StriderCourse.LaneNearZ);
            floor.GetComponent<Collider>().sharedMaterial = mat;
            floor.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.Color(new Color(0.55f, 0.5f, 0.45f), 0.1f);
            var rng = new System.Random(7);
            for (int i = 0; i < 40; i++)
            {
                var pebble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pebble.name = "Pebble";
                pebble.transform.SetParent(transform, false);
                float px = x0 + 0.1f + (float)rng.NextDouble() * (x1 - x0 - 0.2f);
                float pz = m_Course.LaneCenterZ + ((float)rng.NextDouble() - 0.5f) * 1.2f;
                float r = 0.015f + (float)rng.NextDouble() * 0.02f;
                pebble.transform.position = new Vector3(px, 0.1f + r * 0.3f, pz);
                pebble.transform.localScale = Vector3.one * r * 2f;
                pebble.GetComponent<Collider>().sharedMaterial = mat;
                pebble.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.Color(new Color(0.6f, 0.58f, 0.55f), 0.2f);
                pebble.isStatic = true;
            }
        }

        /// <summary>リトライ: 機体をスタートゾーンに戻す。踏破の記録と計時は続く (7.7)。</summary>
        private void Retry()
        {
            if (m_Finished) { Spawn(); return; }
            Respawn();
            m_Fallen = false;
            m_OutOfLane = false;
            Say("リトライ: スタートゾーンから (踏破したゾーンの得点は残ります)", 3f);
        }

        private void Respawn()
        {
            if (m_Strider != null) Destroy(m_Strider.gameObject);
            Vector3 pos = new Vector3(m_Course.StartX, Strider.Thigh * 2f * Mathf.Cos(Strider.StandHip * Mathf.Deg2Rad) + 0.03f, m_Course.LaneCenterZ);
            Quaternion rot = Quaternion.Euler(0f, m_Course.Direction > 0f ? 90f : -90f, 0f);   // 進行方向 (+x) を向く
            m_Strider = Strider.Create(pos, rot);
            UpdateHeading();
            m_HipA = 0f;
            m_KneeA = 0f;
            m_Strider.SetTargets(Strider.StandHip, Strider.StandKnee, Strider.StandHip, Strider.StandKnee);
        }

        /// <summary>最初から (記録も計時も消す)。</summary>
        private void Spawn()
        {
            Respawn();
            m_Course.Reset();
            m_GuideStep = PlayerPrefs.GetInt("tut_strider", 0) != 0 ? 4 : 0;
            m_GuideStartX = m_Course.StartX;
            m_GuideHipMoved = m_GuideKneeMoved = false;
            m_Time = 0f;
            m_Started = false;
            m_Finished = false;
            m_Fallen = false;
            m_OutOfLane = false;
            Say("スタートゾーンから歩き出すと計時が始まります", 4f);
        }

        private void Say(string text, float seconds)
        {
            m_Message = text;
            m_MessageUntil = Time.time + seconds;
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            Gamepad pad = Gamepad.current;
            // メニューへ / やり直し
            bool esc = (kb != null && kb.escapeKey.isPressed) || (pad != null && pad.startButton.isPressed);
            if (esc && !m_EscHeld) { m_Director.BackToMenu(); return; }
            m_EscHeld = esc;
            // R = リトライ (7.7: スタートゾーンからやり直し。踏破済みのゾーンの得点は残り、計時も続く)。Shift+R = 最初から
            bool reset = (kb != null && kb.rKey.isPressed) || (pad != null && pad.buttonNorth.isPressed);
            if (reset && !m_ResetHeld)
            {
                bool full = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
                if (full) Spawn(); else Retry();
            }
            m_ResetHeld = reset;

            // 視点 (矢印 / 十字キー / 右スティック横、ホイール)
            float yaw = 0f, pitch = 0f;
            if (kb != null)
            {
                if (kb.leftArrowKey.isPressed) yaw -= 1f;
                if (kb.rightArrowKey.isPressed) yaw += 1f;
                if (kb.upArrowKey.isPressed) pitch += 1f;
                if (kb.downArrowKey.isPressed) pitch -= 1f;
            }
            if (pad != null)
            {
                if (pad.dpad.left.isPressed) yaw -= 1f;
                if (pad.dpad.right.isPressed) yaw += 1f;
                if (pad.dpad.up.isPressed) pitch += 1f;
                if (pad.dpad.down.isPressed) pitch -= 1f;
                yaw += pad.rightStick.ReadValue().x;
            }
            m_Yaw += yaw * 90f * Time.deltaTime;
            m_Pitch = Mathf.Clamp(m_Pitch + pitch * 60f * Time.deltaTime, -10f, 70f);
            if (Mouse.current != null) m_Distance = Mathf.Clamp(m_Distance - Mouse.current.scroll.ReadValue().y * 0.002f, 1.2f, 8f);

            if (m_Strider != null)
            {
                Vector3 target = m_Strider.Body.transform.position + Vector3.up * 0.1f;
                Quaternion q = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
                m_Camera.transform.position = target + q * new Vector3(0f, 0f, -m_Distance);
                m_Camera.transform.LookAt(target);
            }
            RefreshUi();
        }

        private void FixedUpdate()
        {
            if (m_Strider == null) return;
            Keyboard kb = Keyboard.current;
            Gamepad pad = Gamepad.current;
            float hip = 0f, knee = 0f;
            if (kb != null)
            {
                if (kb.qKey.isPressed) hip += 1f;
                if (kb.wKey.isPressed) hip -= 1f;
                if (kb.oKey.isPressed) knee += 1f;
                if (kb.pKey.isPressed) knee -= 1f;
            }
            if (pad != null)
            {
                hip += pad.leftStick.ReadValue().y;
                knee += pad.rightStick.ReadValue().y;
            }
            hip = Mathf.Clamp(hip, -1f, 1f);
            knee = Mathf.Clamp(knee, -1f, 1f);
            if (!m_Started && (Mathf.Abs(hip) > 0.1f || Mathf.Abs(knee) > 0.1f)) m_Started = true;
            if (m_Started && !m_Finished) m_Time += Time.fixedDeltaTime;
            m_HipA = Mathf.Clamp(m_HipA + hip * HipRate * Time.fixedDeltaTime, -HipRange, HipRange);
            m_KneeA = Mathf.Clamp(m_KneeA + knee * KneeRate * Time.fixedDeltaTime, -KneeRange, KneeRange);
            m_Strider.SetTargets(Strider.StandHip + m_HipA, Strider.StandKnee + m_KneeA, Strider.StandHip - m_HipA, Strider.StandKnee - m_KneeA);
            UpdateHeading();

            // 踏破の判定 (脚先の x)、エリア外、転倒
            m_FootX.Clear();
            bool outside = false;
            for (int i = 0; i < 4; i++)
            {
                Vector3 f = m_Strider.Foot(i);
                m_FootX.Add(f.x);
                if (f.y < 0.08f && !m_Course.InLane(f.x, f.z)) outside = true;
            }
            foreach (StriderZone z in m_Course.Update(m_FootX))
            {
                if (z == StriderZone.Stairs)
                {
                    m_Finished = true;
                    Say($"全ゾーン踏破!  タイム {FormatTime(m_Time)}   合計 +{m_Course.Vp}VP +{m_Course.Rp}RP", 30f);
                }
                else
                {
                    Say($"{StriderCourse.ZoneName(z)}ゾーン 踏破  +{StriderCourse.ZoneVp}VP +{StriderCourse.ZoneRp(z)}RP", 2.5f);
                }
                GameDirector.Instance?.Audio.PlayClick();
            }
            if (outside && !m_OutOfLane) Say("エリアの外に接地 (大会では反則)", 3f);
            m_OutOfLane = outside;
            if (m_Strider.Fallen && !m_Fallen) Say("転倒  (起き上がれなければ R でリトライ。踏破した得点は残ります)", 4f);
            m_Fallen = m_Strider.Fallen;
        }

        /// <summary>向きの補助の目標: 進行方向を基本に、通路の中心からずれたぶんだけ中心へ向ける (ずれが大きいほど強く)。</summary>
        private void UpdateHeading()
        {
            float lateral = m_Strider.Body.transform.position.z - m_Course.LaneCenterZ;   // +z 側にずれていれば正
            float steer = Mathf.Clamp(-lateral * SteerGain, -Mathf.Tan(SteerMaxDeg * Mathf.Deg2Rad), Mathf.Tan(SteerMaxDeg * Mathf.Deg2Rad));
            m_Strider.Heading = new Vector3(m_Course.Direction, 0f, steer).normalized;
        }

        private void RefreshUi()
        {
            StriderZone? cur = m_Course.Current;
            string where = cur.HasValue ? $"{StriderCourse.ZoneName(cur.Value)}ゾーン" : "ゴール";
            m_Status.text = $"ストライダーチャレンジ   タイム {FormatTime(m_Time)}   {where}   {m_Course.Vp}VP {m_Course.Rp}RP";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < StriderCourse.ZoneCount; i++)
            {
                var z = (StriderZone)i;
                sb.Append(m_Course.IsCleared(z) ? "■" : "□").Append(StriderCourse.ZoneName(z)).Append("  ");
            }
            m_Zones.text = sb.ToString();
            m_Center.text = Time.time < m_MessageUntil ? m_Message : "";
            UpdateGuide();
        }

        /// <summary>手ほどき: 腿 → 膝 → 1 歩 → 段差。達成はゲーム内の状態で判定する。</summary>
        private void UpdateGuide()
        {
            if (m_Guide == null) return;
            bool show = m_Settings == null || m_Settings.ShowGuide;
            if (!show || m_GuideStep >= 4 || m_Strider == null) { m_Guide.text = ""; return; }
            if (Mathf.Abs(m_HipA) > 25f) m_GuideHipMoved = true;
            if (Mathf.Abs(m_KneeA) > 25f) m_GuideKneeMoved = true;
            float advanced = (m_Strider.Body.transform.position.x - m_GuideStartX) * m_Course.Direction;
            switch (m_GuideStep)
            {
                case 0:
                    m_Guide.text = "手ほどき 1 / 4:  Q を押し続けて腿を前に振り、W で戻す (対角の 2 本が逆に動く)";
                    if (m_GuideHipMoved) Next();
                    break;
                case 1:
                    m_Guide.text = "手ほどき 2 / 4:  O で膝を曲げ、P で伸ばす (曲げた脚は床から浮く)";
                    if (m_GuideKneeMoved) Next();
                    break;
                case 2:
                    m_Guide.text = "手ほどき 3 / 4:  Q → P → W → O の順に押して 1 歩 (浮いた脚を前へ出して着地、を繰り返す)。1 m 進むと次へ";
                    if (advanced > 1f) Next();
                    break;
                case 3:
                    m_Guide.text = "手ほどき 4 / 4:  その調子で平地ゾーンを越え、段差ゾーン (10 cm の段) を越える。転んだら R";
                    if (m_Course.IsCleared(StriderZone.Step)) { Next(); PlayerPrefs.SetInt("tut_strider", 1); Say("手ほどき 完了! あとはゴールまで", 3f); }
                    break;
            }
        }

        private void Next()
        {
            m_GuideStep++;
            GameDirector.Instance?.Audio.PlaySkill();
        }

        private static string FormatTime(float t) => $"{(int)(t / 60f)}:{t % 60f:00.0}";

        private void OnDestroy()
        {
            if (m_Strider != null) Destroy(m_Strider.gameObject);
            if (m_Root != null) Destroy(m_Root.gameObject);
        }
    }
}
