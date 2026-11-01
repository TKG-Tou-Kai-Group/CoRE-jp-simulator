using UnityEngine;
using UnityEngine.UI;

namespace CoreSim
{
    /// <summary>uGUI をコードから組み立てるための小道具。日本語フォントは OS のものを使う。</summary>
    public static class UiKit
    {
        /// <summary>
        /// 文字サイズの倍率。各所の fontSize は 1920x1080 基準の設計値で、読みにくいという
        /// 声があったので一括で大きくする。レイアウトの箱は変えず、文字だけ太らせる。
        /// </summary>
        public const float FontScale = 1.25f;

        public static int Scaled(int fontSize) => Mathf.RoundToInt(fontSize * FontScale);

        private static Font s_Font;

        /// <summary>
        /// Windows は Yu Gothic / Meiryo、Android は Noto Sans CJK、Linux は Noto / Droid を探す。
        /// 見つからなければ Unity 組み込みの LegacyRuntime (日本語は豆腐になる)。
        /// </summary>
        public static Font Font
        {
            get
            {
                if (s_Font != null)
                {
                    return s_Font;
                }
                string[] names =
                {
                    "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo", "MS UI Gothic",
                    "Noto Sans CJK JP", "Noto Sans JP", "Noto Sans CJK JP Regular",
                    "Hiragino Sans", "Droid Sans Fallback", "Droid Sans Japanese",
                };
                try
                {
                    s_Font = Font.CreateDynamicFontFromOSFont(names, 32);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[UiKit] OS フォントを開けない: {e.Message}");
                }
                if (s_Font == null)
                {
                    s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                return s_Font;
            }
        }

        public static Canvas CreateCanvas(string name, int sortingOrder = 0)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static RectTransform Panel(Transform parent, string name, Color color,
                                          Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            if (color.a > 0f)
            {
                var img = go.AddComponent<Image>();
                img.color = color;
            }
            return rt;
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                         Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Text Label(Transform parent, string name, string text, int size, Color color,
                                 TextAnchor anchor = TextAnchor.MiddleCenter, bool outline = true)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = Scaled(size);
            t.supportRichText = false;   // ネット越しの名前などでタグを解釈させない
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            if (outline)
            {
                var o = go.AddComponent<Outline>();
                o.effectColor = new Color(0f, 0f, 0f, 0.8f);
                o.effectDistance = new Vector2(1.5f, -1.5f);
            }
            return t;
        }

        public static Button Button(Transform parent, string label, Vector2 size, System.Action onClick,
                                    Color? color = null, int fontSize = 32)
        {
            var go = new GameObject("Button_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = color ?? new Color(0.16f, 0.2f, 0.28f, 0.95f);
            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            btn.colors = colors;
            if (onClick != null)
            {
                btn.onClick.AddListener(() => onClick());
            }
            Text text = Label(go.transform, "Label", label, fontSize, Color.white, TextAnchor.MiddleCenter, false);
            text.resizeTextForBestFit = true;      // 箱に収まらない長い文言は少し縮める
            text.resizeTextMinSize = Scaled(fontSize) * 2 / 3;
            text.resizeTextMaxSize = Scaled(fontSize);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            return btn;
        }

        public static Image Image(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax,
                                  Vector2 offsetMin, Vector2 offsetMax)
        {
            RectTransform rt = Panel(parent, name, color, anchorMin, anchorMax, offsetMin, offsetMax);
            var img = rt.GetComponent<Image>();
            if (img == null)
            {
                img = rt.gameObject.AddComponent<Image>();
                img.color = color;
            }
            img.raycastTarget = false;
            return img;
        }

        public static InputField InputField(Transform parent, string name, Vector2 size, string value, string placeholder,
                                            InputField.ContentType contentType = UnityEngine.UI.InputField.ContentType.Standard,
                                            int fontSize = 28)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = size;
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.12f, 0.16f, 0.95f);
            var field = go.AddComponent<InputField>();
            Text text = Label(go.transform, "Text", "", fontSize, Color.white, TextAnchor.MiddleLeft, false);
            text.rectTransform.offsetMin = new Vector2(12f, 4f);
            text.rectTransform.offsetMax = new Vector2(-12f, -4f);
            text.supportRichText = false;
            Text hint = Label(go.transform, "Placeholder", placeholder, fontSize, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleLeft, false);
            hint.rectTransform.offsetMin = new Vector2(12f, 4f);
            hint.rectTransform.offsetMax = new Vector2(-12f, -4f);
            hint.fontStyle = FontStyle.Italic;
            field.textComponent = text;
            field.placeholder = hint;
            field.contentType = contentType;
            field.characterLimit = 64;
            field.text = value;
            return field;
        }

        /// <summary>
        /// 縦スクロールする箱を作り、中身を並べる Content を返す。マウスホイールと指のドラッグで動く。
        /// Content は VerticalLayoutGroup で、子の高さに合わせて伸びる。
        /// </summary>
        public static RectTransform ScrollView(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                               Vector2 anchoredPos, Vector2 size, Color background, RectOffset padding, float spacing,
                                               bool controlChildSize)
        {
            RectTransform rt = Rect(parent, name, anchorMin, anchorMax, anchoredPos, size);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = background;
            var scroll = rt.gameObject.AddComponent<ScrollRect>();

            RectTransform viewport = Panel(rt, "Viewport", new Color(0f, 0f, 0f, 0f), Vector2.zero, Vector2.one, new Vector2(6f, 6f), new Vector2(-6f, -6f));
            var vpImage = viewport.gameObject.AddComponent<Image>();   // ドラッグを受けるための透明な面
            vpImage.color = new Color(0f, 0f, 0f, 0.01f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewport, false);
            var content = contentGo.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = padding;
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = controlChildSize;
            layout.childControlHeight = controlChildSize;
            layout.childForceExpandWidth = controlChildSize;
            layout.childForceExpandHeight = false;
            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            return content;
        }

        public static void SetButtonLabel(Button button, string text)
        {
            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = text;
            }
        }
    }
}
