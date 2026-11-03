using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace CoreSim
{
    /// <summary>
    /// CPU の経路探索用の NavMesh を、組み立てたフィールドの当たり判定から実行時に焼く。
    /// </summary>
    /// <remarks>
    /// 2027 フィールドにはスロープの先が谷になっている高台や、片側が段差のバンカーがある。
    /// 方向だけで進む CPU はスロープを登った先の谷に落ちて動けなくなった。エージェントの
    /// 段差 (6 cm) と勾配 (25 度) を機体に合わせて焼いた NavMesh なら、谷の床は落ちない限り
    /// 繋がっていない領域になるので、経路は谷を通らない。進入禁止ゾーンとバンカーは歩けない領域、
    /// 手動補給エリアは Supply 領域にする。
    /// NavMeshSurface は使わず NavMeshBuilder を直接呼ぶ。NavMeshSurface は Editor では
    /// 「同じステージにある」NavMeshModifierVolume しか集めず、実行時に作った領域の指定が
    /// 無視されて、バンカーの帯の間を経路が通っていた。
    /// </remarks>
    public static class NavigationBuilder
    {
        /// <summary>手動補給エリアの NavMesh 領域番号 (NavMeshAreas.asset の "Supply")。</summary>
        public const int SupplyArea = 3;
        private const int NotWalkable = 1;

        private static NavMeshDataInstance s_Instance;

        public static void Build(GameObject arenaRoot, Field2027Data field)
        {
            float t0 = Time.realtimeSinceStartup;
            if (s_Instance.valid) NavMesh.RemoveNavMeshData(s_Instance);
            NavMeshTriangulation before = NavMesh.CalculateTriangulation();
            if (before.vertices != null && before.vertices.Length > 0)
            {
                Debug.LogWarning($"[Navigation] 焼く前から NavMesh がある (頂点 {before.vertices.Length})。消して焼き直す");
                NavMesh.RemoveAllNavMeshData();
            }

            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);   // 半径 0.45、高さ 0.8、段差 0.06、勾配 25 (NavMeshAreas.asset)
            settings.overrideVoxelSize = true;
            settings.voxelSize = 0.05f;
            settings.overrideTileSize = true;
            settings.tileSize = 128;

            int layerMask = ~((1 << Tuning.LayerDisc) | (1 << Tuning.LayerMinimapOnly) | (1 << Tuning.LayerRobot));
            var sources = new List<NavMeshBuildSource>();
            var markups = new List<NavMeshBuildMarkup>();
            NavMeshBuilder.CollectSources(arenaRoot.transform, layerMask, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
            int geometry = sources.Count;

            if (field != null)
            {
                foreach (PolyZoneDef z in field.Manifest.noEntryZones)
                {
                    // 回転した箱で囲む (外接矩形だと床に何もない所まで通れなくなる)。機体の半幅ぶん広げる
                    AddBox(sources, new Vector3(z.cx, 0.5f, z.cz), Quaternion.Euler(0f, z.yawDeg, 0f),
                           new Vector3(z.width + 1.0f, 1f, z.length + 1.0f), NotWalkable);
                }
                if (field.Manifest.bunkers != null)
                {
                    // バンカーは CPU の経路から外す。中央のバンカーは 150 mm の台の上に 290 mm の帯が 2 本あり、帯の間の
                    // 隙間 (約 1 m) を NavMesh が通路とみなして CPU を通そうとしたが、機体は物理的に通れずに端で回り続けた。
                    // 人は乗れる (段差側から降りるのも自由)
                    foreach (BunkerDef b in field.Manifest.bunkers)
                    {
                        AddBox(sources, new Vector3(b.cx, 0.5f, b.cz), Quaternion.Euler(0f, b.yawDeg, 0f),
                               new Vector3(b.width + 1.0f, 1f, b.length + 1.0f), NotWalkable);
                    }
                }
                foreach (RectDef r in field.Manifest.supplyZones)
                {
                    AddBox(sources, new Vector3((r.xMin + r.xMax) / 2f, 0.5f, (r.zMin + r.zMax) / 2f), Quaternion.identity,
                           new Vector3(r.xMax - r.xMin + 0.6f, 1f, r.zMax - r.zMin + 0.6f), SupplyArea);
                }
            }

            // フィールドと補給エリアを含む範囲だけ焼く (地面のプレーンはもっと広い)
            var bounds = new Bounds(Vector3.zero, new Vector3(32f, 6f, 20f));
            NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            s_Instance = NavMesh.AddNavMeshData(data);
            Debug.Log($"[Navigation] NavMesh を焼いた ({(Time.realtimeSinceStartup - t0) * 1000f:F0} ms、形状 {geometry}、領域の箱 {sources.Count - geometry})");
            LogSummary(field);
        }

        private static void AddBox(List<NavMeshBuildSource> sources, Vector3 center, Quaternion rotation, Vector3 size, int area)
        {
            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.ModifierBox,
                transform = Matrix4x4.TRS(center, rotation, Vector3.one),
                size = size,
                area = area,
            });
        }

        /// <summary>
        /// 焼けた NavMesh の大きさ、領域の指定が効いているか、スタートゾーンから中央・相手スタートゾーンへの
        /// 経路の有無をログに出す。CPU が動かないときに「NavMesh が無い / 繋がっていない / 遠回り」を切り分けるため。
        /// </summary>
        private static void LogSummary(Field2027Data field)
        {
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            if (tri.vertices == null || tri.vertices.Length == 0)
            {
                Debug.LogWarning("[Navigation] NavMesh が空 (頂点 0)。フィールドの当たり判定が集まっていない");
                return;
            }
            Vector3 min = tri.vertices[0], max = tri.vertices[0];
            foreach (Vector3 v in tri.vertices) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
            Debug.Log($"[Navigation] 頂点 {tri.vertices.Length}、三角形 {tri.indices.Length / 3}、範囲 x[{min.x:F1},{max.x:F1}] y[{min.y:F2},{max.y:F2}] z[{min.z:F1},{max.z:F1}]");
            if (field == null) return;
            if (field.Manifest.bunkers != null)
            {
                foreach (BunkerDef b in field.Manifest.bunkers) Sample($"バンカー ({b.cx:F1},{b.cz:F1})", new Vector3(b.cx, 0.15f, b.cz));
            }
            foreach (PolyZoneDef z in field.Manifest.noEntryZones) Sample($"進入禁止 ({z.cx:F1},{z.cz:F1})", new Vector3(z.cx, 0f, z.cz));
            foreach (RectDef r in field.Manifest.supplyZones) Sample($"補給 {r.side}", field.SupplyPoint(r.side));
            int mask = NavMesh.AllAreas & ~(1 << SupplyArea);
            foreach (string side in new[] { "red", "blue" })
            {
                string enemy = side == "red" ? "blue" : "red";
                Vector3 from = field.Spawn(side, 0).position;
                Probe($"{side} スタート → 中央", from, new Vector3(0f, 0f, side == "red" ? 2.5f : -2.5f), mask);
                Probe($"{side} スタート → {enemy} スタート", from, field.Spawn(enemy, 0).position, mask);
                Probe($"{side} スタート → {enemy} ブロックゾーン付近", from, side == "red" ? new Vector3(3f, 0f, 3.5f) : new Vector3(-3f, 0f, -3.5f), mask);
            }
        }

        private static void Sample(string label, Vector3 p)
        {
            bool ok = NavMesh.SamplePosition(p, out NavMeshHit hit, 0.3f, NavMesh.AllAreas);
            Debug.Log(ok ? $"[Navigation] {label}: NavMesh あり (領域 {hit.mask})" : $"[Navigation] {label}: NavMesh なし (歩けない)");
        }

        private static void Probe(string label, Vector3 from, Vector3 to, int mask)
        {
            var path = new NavMeshPath();
            bool a = NavMesh.SamplePosition(from, out NavMeshHit ha, 1.5f, NavMesh.AllAreas);
            bool b = NavMesh.SamplePosition(to, out NavMeshHit hb, 2.0f, mask);
            if (!a || !b)
            {
                Debug.LogWarning($"[Navigation] {label}: NavMesh の上に点が無い (from={a}, to={b})");
                return;
            }
            NavMesh.CalculatePath(ha.position, hb.position, mask, path);
            float length = 0f;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < path.corners.Length; i++)
            {
                if (i > 0) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                sb.Append($"({path.corners[i].x:F1},{path.corners[i].z:F1}) ");
            }
            Debug.Log($"[Navigation] {label}: {path.status}、{path.corners.Length} 角、{length:F1} m: {sb}");
        }
    }
}
