using Unity.AI.Navigation;
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
    /// 繋がっていない領域になるので、経路は谷を通らない。進入禁止ゾーンは歩けない領域にする。
    /// </remarks>
    public static class NavigationBuilder
    {
        /// <summary>手動補給エリアの NavMesh 領域番号 (NavMeshAreas.asset の "Supply")。</summary>
        public const int SupplyArea = 3;

        public static NavMeshSurface Build(GameObject arenaRoot, Field2027Data field)
        {
            var surface = arenaRoot.GetComponent<NavMeshSurface>();
            if (surface == null)
            {
                surface = arenaRoot.AddComponent<NavMeshSurface>();
            }
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;   // 見た目だけの部品は無視する
            surface.layerMask = ~((1 << Tuning.LayerDisc) | (1 << Tuning.LayerMinimapOnly) | (1 << Tuning.LayerRobot));
            surface.agentTypeID = 0;
            surface.overrideVoxelSize = true;
            surface.voxelSize = 0.05f;
            surface.overrideTileSize = true;
            surface.tileSize = 128;

            if (field != null)
            {
                foreach (PolyZoneDef z in field.Manifest.noEntryZones)
                {
                    // 回転した箱で囲む (外接矩形だと床に何もない所まで通れなくなる)
                    var go = new GameObject("NoEntryNav");
                    go.transform.SetParent(arenaRoot.transform, false);
                    go.transform.position = new Vector3(z.cx, 0.5f, z.cz);
                    go.transform.rotation = Quaternion.Euler(0f, z.yawDeg, 0f);
                    var mod = go.AddComponent<NavMeshModifierVolume>();
                    mod.size = new Vector3(z.width + 1.0f, 1f, z.length + 1.0f);   // 機体の半幅ぶん広げる
                    mod.area = 1;   // Not Walkable
                }
                // バンカーは CPU の経路から外す。中央のバンカーは 150 mm の台の上に 290 mm の帯が 2 本あり、帯の間の
                // 隙間 (約 1 m) を NavMesh が通路とみなして CPU を通そうとしたが、機体は物理的に通れずに端で回り続けた。
                // 人は乗れる (段差側から降りるのも自由)。機体の半幅ぶん広げる
                if (field.Manifest.bunkers != null)
                {
                    foreach (BunkerDef b in field.Manifest.bunkers)
                    {
                        var go = new GameObject("BunkerNav");
                        go.transform.SetParent(arenaRoot.transform, false);
                        go.transform.position = new Vector3(b.cx, 0.5f, b.cz);
                        go.transform.rotation = Quaternion.Euler(0f, b.yawDeg, 0f);
                        var mod = go.AddComponent<NavMeshModifierVolume>();
                        mod.size = new Vector3(b.width + 1.0f, 1f, b.length + 1.0f);
                        mod.area = 1;   // Not Walkable
                    }
                }
                foreach (RectDef r in field.Manifest.supplyZones)
                {
                    var go = new GameObject("SupplyNav_" + r.side);
                    go.transform.SetParent(arenaRoot.transform, false);
                    go.transform.position = new Vector3((r.xMin + r.xMax) / 2f, 0.5f, (r.zMin + r.zMax) / 2f);
                    var mod = go.AddComponent<NavMeshModifierVolume>();
                    mod.size = new Vector3(r.xMax - r.xMin + 0.6f, 1f, r.zMax - r.zMin + 0.6f);
                    mod.area = SupplyArea;
                }
            }

            float t0 = Time.realtimeSinceStartup;
            surface.BuildNavMesh();
            Debug.Log($"[Navigation] NavMesh を焼いた ({(Time.realtimeSinceStartup - t0) * 1000f:F0} ms)");
            LogSummary(field);
            return surface;
        }

        /// <summary>
        /// 焼けた NavMesh の大きさと、スタートゾーンから中央・相手スタートゾーンへの経路の有無をログに出す。
        /// CPU が動かないときに「NavMesh が無い / 繋がっていない / 遠回り」を切り分けるため。
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
