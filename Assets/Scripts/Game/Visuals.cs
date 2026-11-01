using UnityEngine;

namespace CoreSim
{
    /// <summary>
    /// 当たり判定を持たない見た目だけの物体を作る。
    /// </summary>
    /// <remarks>
    /// <c>GameObject.CreatePrimitive</c> + <c>Destroy(collider)</c> は使わない。Destroy はフレーム末まで
    /// 遅延するので、生成したフレームの物理ステップではコライダがまだ生きている。ディスクはこれで
    /// 自機のシュータに当たって (IgnoreCollision は球にしか掛けていない)、出た瞬間に消えていた。
    /// </remarks>
    public static class Visuals
    {
        public static Mesh Mesh(PrimitiveType type)
        {
            string name = type switch
            {
                PrimitiveType.Sphere => "Sphere.fbx",
                PrimitiveType.Capsule => "Capsule.fbx",
                PrimitiveType.Cylinder => "Cylinder.fbx",
                PrimitiveType.Plane => "Plane.fbx",
                PrimitiveType.Quad => "Quad.fbx",
                _ => "Cube.fbx",
            };
            return Resources.GetBuiltinResource<Mesh>(name);
        }

        public static GameObject Create(string name, PrimitiveType type, Transform parent,
                                        Vector3 localPosition, Quaternion localRotation, Vector3 localScale,
                                        Material material, int layer = 0)
        {
            var go = new GameObject(name);
            go.layer = layer;
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = Mesh(type);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return go;
        }
    }
}
