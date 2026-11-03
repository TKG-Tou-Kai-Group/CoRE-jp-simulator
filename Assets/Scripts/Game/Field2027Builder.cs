using System;
using System.Collections.Generic;
using CoreSim.Rules;
using UnityEngine;

namespace CoreSim
{
    // ---- tools/import_field2027.py が書く field2027.json の形 (JsonUtility で読む) ----
    [Serializable] public class FieldGroup { public string file; public string zone; public string material; public int triangles; public bool collider; }
    [Serializable] public class PaletteEntry { public string name; public string hex; }
    [Serializable] public class PanelDef { public string side; public bool high; public float[] center; public float[] normal; public float[] size; }
    [Serializable] public class RectDef { public string side; public float xMin; public float zMin; public float xMax; public float zMax; public float y; }
    [Serializable] public class ZoneDef { public string side; public float[] xz; }
    /// <summary>回転した四角形のゾーン。xz は頂点の列、cx/cz/yawDeg/length/width は同じ領域を囲む向き付きの箱。</summary>
    [Serializable] public class PolyZoneDef { public float[] xz; public float cx; public float cz; public float yawDeg; public float length; public float width; }
    [Serializable] public class SpawnPoint { public float x; public float y; public float z; public float yawDeg; }
    [Serializable] public class SpawnSet { public string side; public SpawnPoint[] points; }
    /// <summary>ブロック・コンテナの開始位置 (x,y,z は底面の中心)。origin: zone / valley / holderA / holderB / holderC / common。</summary>
    [Serializable] public class PropDef { public string kind; public string side; public float x; public float y; public float z; public float yawDeg; public string origin; public string color; }
    /// <summary>置き場所: site (1..4、2 段) / spot (1..18) / port (1..2) / valley (1..2)。x,y,z は 1 段目の底面の中心。</summary>
    [Serializable] public class SlotDef { public string kind; public string side; public float x; public float y; public float z; public float yawDeg; public int index; public int levels; public bool post; }
    [Serializable] public class CarrierDef { public string side; public float x; public float z; public float width; public float depth; public float height; }

    [Serializable]
    public class FieldManifest
    {
        public string source;
        public float fieldWidth;
        public float fieldDepth;
        public FieldGroup[] groups;
        public PaletteEntry[] palette;
        public PanelDef[] basePanels;
        public RectDef[] bunkerTops;
        public RectDef[] supplyZones;
        public PolyZoneDef[] noEntryZones;
        public ZoneDef[] startZones;
        public SpawnSet[] spawns;
        public PropDef[] props;
        public SlotDef[] slots;
        public CarrierDef[] carriers;
    }

    /// <summary>読み込んだ 2027 フィールドの情報。位置の問い合わせに使う。</summary>
    public sealed class Field2027Data
    {
        public FieldManifest Manifest;
        public readonly List<BasePanel> Panels = new List<BasePanel>();
        /// <summary>ブロック・コンテナと置き場所 (ビルダー用)。</summary>
        public readonly PropRegistry Props = new PropRegistry();

        public static bool Inside(RectDef r, Vector3 p) => p.x >= r.xMin && p.x <= r.xMax && p.z >= r.zMin && p.z <= r.zMax;

        public bool OnBunkerTop(Vector3 p)
        {
            foreach (RectDef r in Manifest.bunkerTops)
            {
                if (Inside(r, p) && Mathf.Abs(p.y - r.y) < 0.12f) return true;
            }
            return false;
        }

        public RectDef SupplyZone(string side)
        {
            foreach (RectDef r in Manifest.supplyZones) if (r.side == side) return r;
            return null;
        }

        public bool InSupplyZone(string side, Vector3 p)
        {
            RectDef r = SupplyZone(side);
            return r != null && Inside(r, p);
        }

        public bool InNoEntryZone(Vector3 p)
        {
            foreach (PolyZoneDef z in Manifest.noEntryZones) if (InsidePolygon(z.xz, p.x, p.z)) return true;
            return false;
        }

        /// <summary>多角形の内外判定 (交差数の偶奇)。xz は [x0, z0, x1, z1, ...]。</summary>
        public static bool InsidePolygon(float[] xz, float x, float z)
        {
            int n = xz.Length / 2;
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = xz[2 * i], zi = xz[2 * i + 1];
                float xj = xz[2 * j], zj = xz[2 * j + 1];
                if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        public Pose Spawn(string side, int index)
        {
            foreach (SpawnSet s in Manifest.spawns)
            {
                if (s.side != side || s.points.Length == 0) continue;
                SpawnPoint p = s.points[Mathf.Clamp(index, 0, s.points.Length - 1)];
                return new Pose(new Vector3(p.x, p.y, p.z), Quaternion.Euler(0f, p.yawDeg, 0f));
            }
            return new Pose(Vector3.zero, Quaternion.identity);
        }

        /// <summary>補給エリアの中央 (CPU がピットインに向かう点)。</summary>
        public Vector3 SupplyPoint(string side)
        {
            RectDef r = SupplyZone(side);
            return r == null ? Vector3.zero : new Vector3((r.xMin + r.xMax) / 2f, 0f, (r.zMin + r.zMax) / 2f);
        }

        /// <summary>相手の陣地のダメージパネルのうち、低い位置のものの中心を返す。</summary>
        public IEnumerable<BasePanel> PanelsOf(string side)
        {
            foreach (BasePanel p in Panels) if (p.SideName == side) yield return p;
        }
    }

    /// <summary>
    /// CoRE-1 2027 フィールド。公式ルールナビに埋め込まれた簡略 3D モデルを
    /// tools/import_field2027.py で STL にしたものを並べる。ダメージパネル・補給エリア・
    /// 進入禁止ゾーン・バンカー上面の位置は同じツールが manifest に書き出す。
    /// </summary>
    public static class Field2027Builder
    {
        public static Field2027Data Build(Transform parent, bool lowDetail)
        {
            var text = Resources.Load<TextAsset>("Field2027/field2027");
            if (text == null)
            {
                Debug.LogError("[Field2027] Resources/Field2027/field2027.json が無い。tools/import_field2027.py を実行すること");
                return null;
            }
            var data = new Field2027Data { Manifest = JsonUtility.FromJson<FieldManifest>(text.text) };
            FieldManifest m = data.Manifest;
            var palette = new Dictionary<string, Color>();
            foreach (PaletteEntry e in m.palette)
            {
                if (ColorUtility.TryParseHtmlString(e.hex, out Color c)) palette[e.name] = c;
            }

            var root = new GameObject("Field2027");
            root.transform.SetParent(parent, false);
            // 地面のプレーン (y = 0) と床板が同じ高さだと描画が交互に出てちらつくので、全体を 5 mm 浮かせる。
            // 床の線や塗りはさらに 2 mm 浮かせて床板と重ならないようにする。走行は地面のプレーンが受ける。
            const float LiftAll = 0.005f;
            const float LiftLines = 0.002f;
            root.transform.localPosition = new Vector3(0f, LiftAll, 0f);
            foreach (FieldGroup g in m.groups)
            {
                Mesh mesh = Resources.Load<Mesh>("Field2027/" + g.file);
                if (mesh == null)
                {
                    Debug.LogWarning($"[Field2027] メッシュが無い: {g.file}");
                    continue;
                }
                var go = new GameObject(g.file);
                go.transform.SetParent(root.transform, false);
                go.isStatic = true;
                if (g.material.StartsWith("line") || g.material.StartsWith("zone") || (g.zone == "supply" && g.material == "floor"))
                {
                    go.transform.localPosition = new Vector3(0f, LiftLines, 0f);
                }
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                Color color = palette.TryGetValue(g.material, out Color pc) ? pc : Color.gray;
                bool shiny = g.material == "alu" || g.material == "glass" || g.material == "plate";
                renderer.sharedMaterial = MaterialLibrary.Color(color, shiny ? 0.55f : 0.2f, g.material == "alu" ? 0.4f : 0f);
                renderer.shadowCastingMode = lowDetail || g.zone == "battle"
                    ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                if (g.collider)
                {
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
                }
            }

            // 陣地のダメージパネル: 厚みのある箱を面の少し手前に置き、ディスクを先に受ける。
            foreach (PanelDef p in m.basePanels)
            {
                var center = new Vector3(p.center[0], p.center[1], p.center[2]);
                var normal = new Vector3(p.normal[0], p.normal[1], p.normal[2]).normalized;
                var go = new GameObject($"BasePanel_{p.side}_{(p.high ? "high" : "low")}");
                go.transform.SetParent(root.transform, false);
                go.transform.position = center + normal * 0.03f;
                go.transform.rotation = Quaternion.LookRotation(normal, Vector3.up);
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(p.high ? 0.16f : 0.18f, p.high ? 0.18f : 0.18f, 0.06f);
                var panel = go.AddComponent<BasePanel>();
                panel.Setup(p.side, p.high);
                data.Panels.Add(panel);
            }

            // 見えない外周。ディスクと機体がフィールドから出ないようにする。補給エリアは
            // 短辺のフェンスの外にあるので、その分だけ外に広げる。
            Material none = null;
            float hw = m.fieldWidth / 2f, hd = m.fieldDepth / 2f, h = 2.5f;
            Wall(root, "bound_zNeg", new Vector3(0f, h / 2f, -hd - 0.1f), new Vector3(m.fieldWidth + 4f, h, 0.1f));
            Wall(root, "bound_zPos", new Vector3(0f, h / 2f, hd + 0.1f), new Vector3(m.fieldWidth + 4f, h, 0.1f));
            foreach (RectDef s in m.supplyZones)
            {
                float side = s.side == "red" ? -1f : 1f;
                float outerX = side < 0f ? s.xMin - 0.1f : s.xMax + 0.1f;
                // 補給エリアの外側の壁と、その前後の短辺の壁
                Wall(root, "bound_supply_" + s.side, new Vector3(outerX, h / 2f, (s.zMin + s.zMax) / 2f), new Vector3(0.1f, h, s.zMax - s.zMin + 0.2f));
                float innerX = side < 0f ? -hw - 0.05f : hw + 0.05f;
                float z0 = side < 0f ? -hd : s.zMax, z1 = side < 0f ? s.zMin : hd;   // 補給エリアの手前から短辺の端まで
                float zOther0 = side < 0f ? s.zMax : -hd, zOther1 = side < 0f ? hd : s.zMin;
                Wall(root, "bound_x_" + s.side + "_a", new Vector3(innerX, h / 2f, (z0 + z1) / 2f), new Vector3(0.1f, h, Mathf.Abs(z1 - z0)));
                Wall(root, "bound_x_" + s.side + "_b", new Vector3(innerX, h / 2f, (zOther0 + zOther1) / 2f), new Vector3(0.1f, h, Mathf.Abs(zOther1 - zOther0)));
                Wall(root, "bound_supply_side_" + s.side, new Vector3((outerX + innerX) / 2f, h / 2f, side < 0f ? s.zMin - 0.05f : s.zMax + 0.05f),
                     new Vector3(Mathf.Abs(outerX - innerX), h, 0.1f));
            }
            _ = none;
            BuildSlots(data);
            SpawnProps(root.transform, data, remote: false);
            SpawnCarriers(root.transform, data);
            // 谷部の NavMesh の繋ぎ (CPU の経路が外周の高台を通れる。物理的な橋は無い)
            foreach (string side in new[] { "red", "blue" })
            {
                ValleyBridge.Create(root.transform, data.Props, side, m.fieldDepth / 2f);
            }
            return data;
        }

        /// <summary>置き場所の一覧を manifest から作る。</summary>
        private static void BuildSlots(Field2027Data data)
        {
            FieldManifest m = data.Manifest;
            if (m.slots == null) return;
            foreach (SlotDef d in m.slots)
            {
                PlaceKind kind = d.kind switch { "site" => PlaceKind.Site, "valley" => PlaceKind.Valley, "port" => PlaceKind.Port, _ => PlaceKind.Spot };
                data.Props.Slots.Add(new PropSlot
                {
                    Kind = kind,
                    Side = d.side,
                    Index = d.index,
                    Position = new Vector3(d.x, d.y, d.z),
                    Rotation = Quaternion.Euler(0f, d.yawDeg, 0f),
                    Levels = Mathf.Max(1, d.levels),
                    Post = d.post,
                });
            }
        }

        /// <summary>
        /// ブロック 12 個 x 2 + 高台の谷部 2 個 x 2、コンテナ 4+5+5 x 2 + 共通 4 を開始位置に置く。
        /// 番号は manifest の並び順 (ホストとクライアントで同じ)。
        /// </summary>
        public static void SpawnProps(Transform parent, Field2027Data data, bool remote)
        {
            FieldManifest m = data.Manifest;
            if (m.props == null) return;
            var root = new GameObject("Props");
            root.transform.SetParent(parent, false);
            ushort id = 1;
            foreach (PropDef d in m.props)
            {
                PropKind kind = d.kind == "block" ? PropKind.Block : PropKind.Container;
                PropColor color = kind == PropKind.Block ? PropColor.None : d.color switch { "red" => PropColor.Red, "blue" => PropColor.Blue, _ => PropColor.Yellow };
                ContainerOrigin origin = d.origin switch { "holderB" => ContainerOrigin.HolderB, "holderC" => ContainerOrigin.HolderC, "common" => ContainerOrigin.Common, _ => ContainerOrigin.HolderA };
                PropSlot home = null;
                if (d.origin == "valley")
                {
                    // 谷部のブロックは置き場所に置かれた状態で始まる
                    var pos = new Vector3(d.x, d.y, d.z);
                    float best = 0.2f * 0.2f;
                    foreach (PropSlot s in data.Props.SlotsOf(PlaceKind.Valley, d.side))
                    {
                        float dist = (s.Position - pos).sqrMagnitude;
                        if (dist < best) { best = dist; home = s; }
                    }
                }
                FieldProp prop = FieldProp.Create(root.transform, id++, kind, color, origin, d.side,
                                                  new Vector3(d.x, d.y, d.z), Quaternion.Euler(0f, d.yawDeg, 0f), home, remote);
                data.Props.Add(prop);
            }
        }

        /// <summary>ブロックキャリアー (7.4): キャスター付きの台。今は動かない台として置く (牽引は未実装)。</summary>
        private static void SpawnCarriers(Transform parent, Field2027Data data)
        {
            FieldManifest m = data.Manifest;
            if (m.carriers == null) return;
            foreach (CarrierDef c in m.carriers)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "BlockCarrier_" + c.side;
                go.layer = Tuning.LayerProp;
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(c.x, c.height / 2f + 0.005f, c.z);
                go.transform.localScale = new Vector3(c.width, c.height, c.depth);
                go.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.Color(new Color(0.72f, 0.75f, 0.78f), 0.5f, 0.5f);
                go.isStatic = true;   // 牽引機構が無いので動かさない (ネット対戦で同期もしない)。上にブロックは置ける
            }
        }

        private static void Wall(GameObject root, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.position = center;
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            go.isStatic = true;
        }
    }

    /// <summary>陣地のダメージパネル (6.8)。ディスクが当たると持ち主の同盟の陣地 HP と RP が減る。</summary>
    public sealed class BasePanel : MonoBehaviour
    {
        public string SideName;   // "red" / "blue"
        public bool High;
        private float m_FlashUntil;
        private GameObject m_FlashVisual;

        public void Setup(string side, bool high)
        {
            SideName = side;
            High = high;
            // 当たったときに光る板
            m_FlashVisual = Visuals.Create("flash", PrimitiveType.Cube, transform, new Vector3(0f, 0f, -0.02f), Quaternion.identity,
                                           new Vector3(0.18f, 0.18f, 0.01f), MaterialLibrary.Emissive(Color.white));
            m_FlashVisual.SetActive(false);
        }

        public void Flash()
        {
            m_FlashUntil = Time.time + 0.2f;
            m_FlashVisual.SetActive(true);
        }

        private void Update()
        {
            if (m_FlashVisual.activeSelf && Time.time > m_FlashUntil)
            {
                m_FlashVisual.SetActive(false);
            }
        }
    }
}
