using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CoreSim
{
    /// <summary>
    /// オートアタッカーの「地点へ移動」の地点を、フィールドの真上からの絵をクリック (タップ) して選ぶ画面。
    /// 絵は自同盟が赤側 (左) になる向きで、選んだ点も赤側の座標で保存する (青同盟では実行時に点対称に写す)。
    /// </summary>
    public sealed class MapPickerView : MonoBehaviour, IPointerClickHandler
    {
        private const float FieldW = 27.6f;   // 補給エリアを含む幅 [m]
        private const float FieldH = 15f;
        private const int TexW = 1104, TexH = 600;

        private RectTransform m_Image;
        private RectTransform m_Marker;
        private Text m_Coord;
        private Camera m_Camera;
        private RenderTexture m_Texture;
        private bool m_Has;
        private float m_X, m_Z;
        private System.Action<float, float> m_OnPick;

        public static MapPickerView Create(Transform parent, bool hasInitial, float x, float z, System.Action<float, float> onPick, System.Action onCancel)
        {
            RectTransform root = UiKit.Panel(parent, "MapPicker", new Color(0.03f, 0.04f, 0.06f, 0.96f),
                                             Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var view = root.gameObject.AddComponent<MapPickerView>();
            view.m_OnPick = onPick;
            view.m_Has = hasInitial;
            view.m_X = x;
            view.m_Z = z;

            RectTransform title = UiKit.Rect(root, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(1500f, 60f));
            UiKit.Label(title, "Text", "地図をクリックして地点を選ぶ  (左が自同盟の陣地側、右が相手側)", 32, Color.white);

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
            view.m_Image = imgGo.GetComponent<RectTransform>();
            view.m_Image.anchorMin = new Vector2(0.5f, 0.5f);
            view.m_Image.anchorMax = new Vector2(0.5f, 0.5f);
            view.m_Image.sizeDelta = new Vector2(TexW, TexH);
            var raw = imgGo.AddComponent<RawImage>();
            raw.texture = view.m_Texture;
            raw.raycastTarget = true;
            imgGo.AddComponent<MapClickRelay>().Owner = view;

            // 目安の文字
            Label(view.m_Image, "自陣地", -9f, 5.2f);
            Label(view.m_Image, "自スタート", -10.4f, -5.8f);
            Label(view.m_Image, "相手陣地", 9f, -5.2f);
            Label(view.m_Image, "相手スタート", 10.4f, 5.8f);
            Label(view.m_Image, "中央", 0f, 0f);

            RectTransform marker = UiKit.Rect(view.m_Image, "Marker", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(28f, 28f));
            UiKit.Image(marker, "ring", new Color(1f, 0.9f, 0.2f, 0.95f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            UiKit.Image(marker, "dot", new Color(0.1f, 0.1f, 0.1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-5f, -5f), new Vector2(5f, 5f));
            view.m_Marker = marker;

            RectTransform foot = UiKit.Rect(root, "Foot", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(1200f, 70f));
            var layout = foot.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 16f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childAlignment = TextAnchor.MiddleCenter;
            view.m_Coord = UiKit.Label(UiKit.Rect(foot, "Coord", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(420f, 60f)), "Text", "", 24, new Color(0.85f, 0.9f, 1f));
            UiKit.Button(foot, "この点にする", new Vector2(260f, 60f), () => { if (view.m_Has) onPick?.Invoke(view.m_X, view.m_Z); }, new Color(0.2f, 0.45f, 0.3f, 0.95f), 26);
            UiKit.Button(foot, "やめる", new Vector2(200f, 60f), () => onCancel?.Invoke(), new Color(0.3f, 0.3f, 0.35f, 0.95f), 26);
            view.UpdateMarker();
            return view;
        }

        private static void Label(RectTransform map, string text, float x, float z)
        {
            RectTransform r = UiKit.Rect(map, "L_" + text, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), ToLocal(x, z), new Vector2(200f, 30f));
            UiKit.Label(r, "Text", text, 18, new Color(1f, 1f, 1f, 0.8f));
        }

        private static Vector2 ToLocal(float x, float z) => new Vector2(x / FieldW * TexW, z / FieldH * TexH);

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Image, eventData.position, eventData.pressEventCamera, out Vector2 local)) return;
            m_X = Mathf.Clamp(local.x / TexW * FieldW, -FieldW / 2f, FieldW / 2f);
            m_Z = Mathf.Clamp(local.y / TexH * FieldH, -FieldH / 2f, FieldH / 2f);
            m_X = Mathf.Round(m_X * 10f) / 10f;
            m_Z = Mathf.Round(m_Z * 10f) / 10f;
            m_Has = true;
            UpdateMarker();
        }

        private void UpdateMarker()
        {
            m_Marker.gameObject.SetActive(m_Has);
            if (m_Has) m_Marker.anchoredPosition = ToLocal(m_X, m_Z);
            m_Coord.text = m_Has ? $"選んだ点: x {m_X:0.0} m, z {m_Z:0.0} m" : "まだ選んでいません";
        }

        private void OnDestroy()
        {
            if (m_Camera != null) m_Camera.targetTexture = null;
            if (m_Texture != null) m_Texture.Release();
        }

        /// <summary>RawImage に付けてクリックを本体へ回す。</summary>
        private sealed class MapClickRelay : MonoBehaviour, IPointerClickHandler
        {
            public MapPickerView Owner;
            public void OnPointerClick(PointerEventData eventData) => Owner?.OnPointerClick(eventData);
        }
    }
}
