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
            return surface;
        }
    }
}
