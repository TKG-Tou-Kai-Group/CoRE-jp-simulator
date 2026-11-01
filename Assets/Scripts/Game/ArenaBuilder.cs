using UnityEngine;

namespace CoreSim
{
    /// <summary>
    /// フィールドを組み立てる。すべてのルールで CoRE-1 2027 フィールド
    /// (ルールブック V27.1.3、24m x 15m) を使う。形状は <see cref="Field2027Builder"/> が読む。
    /// </summary>
    /// <remarks>
    /// v0.3 までは、かんたんルールに CoRE-jp-Unity-ROS2-packages の 2026 フィールド (core_stage.world の
    /// STL) を使っていたが、現行ルールのフィールドに統一した。2026 フィールドを戻すなら、
    /// tools/decimate_stl.py で間引いた STL を Resources に置き、ここで切り替える。
    /// </remarks>
    public static class ArenaBuilder
    {
        /// <summary>読み込んだ 2027 フィールドの情報。</summary>
        public static Field2027Data Field2027 { get; private set; }

        public static GameObject Build(bool lowDetail)
        {
            var root = new GameObject("Arena");

            // 地面。フィールドの床板には当たり判定を付けていないので、走行はこのプレーンが受ける。
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(root.transform, false);
            ground.transform.localScale = new Vector3(8f, 1f, 6f);   // 80 x 60 m
            ground.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.Color(new Color(0.32f, 0.33f, 0.36f), 0.1f);
            ground.isStatic = true;

            Field2027 = Field2027Builder.Build(root.transform, lowDetail);
            AddLight(root, lowDetail);
            NavigationBuilder.Build(root, Field2027);
            return root;
        }

        private static void AddLight(GameObject root, bool lowDetail)
        {
            var sun = new GameObject("Sun");
            sun.transform.SetParent(root.transform, false);
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = lowDetail ? LightShadows.None : LightShadows.Soft;
            light.shadowStrength = 0.6f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.52f);
            RenderSettings.sun = light;
        }
    }
}
