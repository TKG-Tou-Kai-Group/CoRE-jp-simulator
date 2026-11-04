using System.Collections.Generic;
using UnityEngine;

namespace CoreSim
{
    /// <summary>コードから作るマテリアルの置き場。同じ色は 1 枚を共有する。</summary>
    public static class MaterialLibrary
    {
        private static readonly Dictionary<Color, Material> s_Cache = new Dictionary<Color, Material>();
        private static Shader s_Standard;
        private static Shader s_Unlit;

        public static Material Color(Color color, float smoothness = 0.3f, float metallic = 0f)
        {
            if (s_Cache.TryGetValue(color, out Material cached))
            {
                return cached;
            }
            var m = new Material(StandardShader) { name = $"color_{ColorUtility.ToHtmlStringRGB(color)}" };
            m.color = color;
            m.SetFloat("_Glossiness", smoothness);
            m.SetFloat("_Metallic", metallic);
            s_Cache[color] = m;
            return m;
        }

        /// <summary>自己発光するマテリアル (ライトに影響されない目印用)。</summary>
        public static Material Emissive(Color color)
        {
            var m = new Material(StandardShader) { name = "emissive" };
            m.color = color;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color);
            return m;
        }

        public static Material Unlit(Color color)
        {
            var m = new Material(UnlitShader) { name = "unlit" };
            m.color = color;
            return m;
        }

        private static Material s_Text3D;

        /// <summary>
        /// 機体の番号札 (TextMesh) 用。フォントの標準マテリアル (GUI/Text Shader) は深度を見ずに描くので壁の向こうの
        /// 番号が透けて相手の位置が分かってしまう。深度を見るシェーダで同じフォントのテクスチャを使う。
        /// フォントのテクスチャは文字が増えると作り直されるので、そのたびに差し替える。
        /// </summary>
        public static Material Text3D
        {
            get
            {
                if (s_Text3D == null)
                {
                    Shader shader = Resources.Load<Shader>("Shaders/Text3D");
                    Font font = UiKit.Font;
                    if (shader == null || font == null) return font != null ? font.material : null;
                    s_Text3D = new Material(shader) { name = "text3d", mainTexture = font.material.mainTexture };
                    Font.textureRebuilt += f => { if (f == font && s_Text3D != null) s_Text3D.mainTexture = f.material.mainTexture; };
                }
                return s_Text3D;
            }
        }

        private static Shader StandardShader
        {
            get
            {
                if (s_Standard == null)
                {
                    s_Standard = Shader.Find("Standard");
                }
                return s_Standard;
            }
        }

        private static Shader UnlitShader
        {
            get
            {
                if (s_Unlit == null)
                {
                    s_Unlit = Shader.Find("Unlit/Color");
                    if (s_Unlit == null) s_Unlit = StandardShader;
                }
                return s_Unlit;
            }
        }

        private static PhysicsMaterial s_Frictionless;

        /// <summary>接地脚用。摩擦 0 で、走行は ChassisDrive の加力だけで決まる。</summary>
        public static PhysicsMaterial Frictionless
        {
            get
            {
                if (s_Frictionless == null)
                {
                    s_Frictionless = new PhysicsMaterial("frictionless")
                    {
                        dynamicFriction = 0f,
                        staticFriction = 0f,
                        frictionCombine = PhysicsMaterialCombine.Minimum,
                        bounciness = 0f,
                        bounceCombine = PhysicsMaterialCombine.Minimum,
                    };
                }
                return s_Frictionless;
            }
        }
    }
}
