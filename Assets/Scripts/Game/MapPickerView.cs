using CoreSim.Rules;
using UnityEngine;
using UnityEngine.UI;

namespace CoreSim
{
    /// <summary>
    /// オートアタッカーの「地点へ移動」の地点を、フィールドの真上からの絵の上の印から選ぶ画面。
    /// ルールに詳しくなくても場所を直感的に選べるようにするためのもので、選べるのは名前付きの地点だけ。
    /// 絵は自同盟が赤側 (左) になる向き。
    /// </summary>
    public sealed class MapPickerView : MonoBehaviour
    {
        private const float FieldW = 27.6f;   // 補給エリアを含む幅 [m]
        private const float FieldH = 15f;
        private const int TexW = 1104, TexH = 600;

        private Camera m_Camera;
        private RenderTexture m_Texture;

        public static MapPickerView Create(Transform parent, AutoPoint current, System.Action<AutoPoint> onPick, System.Action onCancel)
        {
            RectTransform root = UiKit.Panel(parent, "MapPicker", new Color(0.03f, 0.04f, 0.06f, 0.96f),
                                             Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var view = root.gameObject.AddComponent<MapPickerView>();

            RectTransform title = UiKit.Rect(root, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(1500f, 60f));
            UiKit.Label(title, "Text", "地図の印を押して行き先を選ぶ  (左が自同盟の陣地側、右が相手側)", 32, Color.white);

            // 真上からの絵
            view.m_Texture = new RenderTexture(TexW, TexH, 16) { name = "mappicker" };
            var camGo = new GameObject("MapPickerCamera");
            camGo.transform.SetParent(root, false);
            view.m_Camera = camGo.AddComponent<Camera>();
            view.m_Camera.orthographic = true;
            view.m_Camera.orthographicSize = FieldH / 2f;
            view.m_Camera.targetTexture = view.m_Texture;
            view.m_Camera.cullingMask = (1 << 0) | (1 << Tuning.LayerProp);
            view.m_Camera.clearFlags = CameraClearFlags.SolidColor;
            view.m_Camera.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
            view.m_Camera.depth = -5;
            view.m_Camera.nearClipPlane = 1f;
            view.m_Camera.farClipPlane = 60f;
            view.m_Camera.transform.position = new Vector3(0f, 30f, 0f);
            view.m_Camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // 画面の上 = +z、右 = +x (赤同盟は左)

            RectTransform frame = UiKit.Rect(root, "Frame", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(TexW + 8f, TexH + 8f));
            UiKit.Image(frame, "border", new Color(0.6f, 0.6f, 0.65f, 0.9f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var imgGo = new GameObject("Map", typeof(RectTransform));
            imgGo.transform.SetParent(frame, false);
            RectTransform map = imgGo.GetComponent<RectTransform>();
            map.anchorMin = new Vector2(0.5f, 0.5f);
            map.anchorMax = new Vector2(0.5f, 0.5f);
            map.sizeDelta = new Vector2(TexW, TexH);
            var raw = imgGo.AddComponent<RawImage>();
            raw.texture = view.m_Texture;
            raw.raycastTarget = false;

            // 目安の文字
            Label(map, "自陣地", -9f, 5.2f);
            Label(map, "自スタートゾーン", -10.4f, -6.6f);
            Label(map, "相手陣地", 9f, -5.2f);
            Label(map, "相手スタートゾーン", 10.4f, 6.6f);

            // 選べる地点の印 (赤同盟の側から見た位置)
            for (int i = 0; i < AutoRule.PointCount; i++)
            {
                var point = (AutoPoint)i;
                Vector3 p = AutoAttackerSource.NamedPoint(point, Side.Red);
                bool selected = point == current;
                RectTransform pin = UiKit.Rect(map, "Pin_" + point, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), ToLocal(p.x, p.z), new Vector2(230f, 54f));
                Button b = UiKit.Button(pin, AutoProgram.PointName(point), new Vector2(230f, 54f), () => onPick?.Invoke(point),
                                        selected ? new Color(0.95f, 0.65f, 0.2f, 0.95f) : new Color(0.15f, 0.35f, 0.6f, 0.9f), 20);
                var rt = (RectTransform)b.transform;
                rt.anchoredPosition = Vector2.zero;
            }

            RectTransform foot = UiKit.Rect(root, "Foot", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(600f, 70f));
            UiKit.Button(foot, "やめる", new Vector2(200f, 60f), () => onCancel?.Invoke(), new Color(0.3f, 0.3f, 0.35f, 0.95f), 26);
            return view;
        }

        private static void Label(RectTransform map, string text, float x, float z)
        {
            RectTransform r = UiKit.Rect(map, "L_" + text, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), ToLocal(x, z), new Vector2(260f, 30f));
            UiKit.Label(r, "Text", text, 18, new Color(1f, 1f, 1f, 0.8f));
        }

        private static Vector2 ToLocal(float x, float z) => new Vector2(x / FieldW * TexW, z / FieldH * TexH);

        private void OnDestroy()
        {
            if (m_Camera != null) m_Camera.targetTexture = null;
            if (m_Texture != null) m_Texture.Release();
        }
    }
}
