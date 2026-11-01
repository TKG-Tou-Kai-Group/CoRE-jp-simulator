using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace CoreSim.EditorTools
{
    /// <summary>
    /// .stl を Mesh アセットとして取り込む。ROS 慣習 (x 前, y 左, z 上, 右手系) の座標を
    /// Unity (x 右, y 上, z 前, 左手系) へ頂点単位で変換する: (x, y, z) → (-y, z, x)。
    /// 鏡像になるので三角形の向きも反転する。法線は面ごと (CAD 由来の形状なので平面的で良い)。
    /// </summary>
    /// <remarks>
    /// Unity_ROS2_Robot_Simulator は同じ変換を実行時のローダでやっていた。ゲームでは
    /// 起動時間を縮めるため、インポート時に済ませてメッシュをアセットにしておく。
    /// </remarks>
    [ScriptedImporter(1, "stl")]
    public sealed class StlImporter : ScriptedImporter
    {
        [Tooltip("ROS (Z-up, 右手系) から Unity (Y-up, 左手系) へ変換する")]
        public bool rosToUnity = true;
        public float scale = 1f;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            byte[] bytes = File.ReadAllBytes(ctx.assetPath);
            List<Vector3> tris = IsBinary(bytes) ? ReadBinary(bytes) : ReadAscii(bytes);

            int count = tris.Count;
            var vertices = new Vector3[count];
            var normals = new Vector3[count];
            var indices = new int[count];
            for (int i = 0; i < count; i += 3)
            {
                Vector3 a = Convert(tris[i]);
                Vector3 b = Convert(tris[i + 1]);
                Vector3 c = Convert(tris[i + 2]);
                if (rosToUnity)
                {
                    // 鏡像変換なので巻き順を戻す
                    (b, c) = (c, b);
                }
                Vector3 n = Vector3.Cross(b - a, c - a);
                n = n.sqrMagnitude > 0f ? n.normalized : Vector3.up;
                vertices[i] = a; vertices[i + 1] = b; vertices[i + 2] = c;
                normals[i] = n; normals[i + 1] = n; normals[i + 2] = n;
                indices[i] = i; indices[i + 1] = i + 1; indices[i + 2] = i + 2;
            }

            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(ctx.assetPath) };
            mesh.indexFormat = count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();

            ctx.AddObjectToAsset("mesh", mesh);
            ctx.SetMainObject(mesh);
        }

        private Vector3 Convert(Vector3 v)
        {
            v *= scale;
            return rosToUnity ? new Vector3(-v.y, v.z, v.x) : v;
        }

        private static bool IsBinary(byte[] bytes)
        {
            if (bytes.Length < 84)
            {
                return false;
            }
            uint n = BitConverter.ToUInt32(bytes, 80);
            return bytes.Length == 84 + n * 50;
        }

        private static List<Vector3> ReadBinary(byte[] bytes)
        {
            uint n = BitConverter.ToUInt32(bytes, 80);
            var list = new List<Vector3>((int)n * 3);
            int o = 84;
            for (uint t = 0; t < n; t++)
            {
                o += 12;   // 法線は使わない (面から計算する)
                for (int v = 0; v < 3; v++)
                {
                    list.Add(new Vector3(BitConverter.ToSingle(bytes, o), BitConverter.ToSingle(bytes, o + 4), BitConverter.ToSingle(bytes, o + 8)));
                    o += 12;
                }
                o += 2;
            }
            return list;
        }

        private static List<Vector3> ReadAscii(byte[] bytes)
        {
            var list = new List<Vector3>();
            string text = Encoding.ASCII.GetString(bytes);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (!line.StartsWith("vertex", StringComparison.Ordinal))
                {
                    continue;
                }
                string[] p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 4)
                {
                    list.Add(new Vector3(float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv)));
                }
            }
            list.RemoveRange(list.Count - list.Count % 3, list.Count % 3);
            return list;
        }
    }
}
