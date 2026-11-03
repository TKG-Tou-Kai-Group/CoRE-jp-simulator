using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace CoreSim
{
    /// <summary>
    /// 外周の高台の谷部 (3.6)。高台は中央で幅 0.38 m・深さ 0.2 m の溝に分かれていて、ブロック 2 個で埋めると渡れる。
    /// 機体は車輪の間隔 (0.7 m) が溝より広いので真っ直ぐなら溝をまたげるが、斜めに入ると車輪が落ちる。
    /// ブロックが入っている間は溝の上に機体だけが乗れる薄い橋を置き、NavMesh でも高台が左右で繋がるようにする。
    /// ブロックを抜かれたら橋を外し、NavMesh を削って CPU の経路から高台の横断を消す。
    /// </summary>
    public sealed class ValleyBridge : MonoBehaviour
    {
        public const float HighlandDepth = 1.5f;   // 高台の奥行き (フィールドの壁から)。ルールナビの 3D モデルより
        public const float SlitWidth = 0.38f;

        private List<PropSlot> m_Slots;
        private PropRegistry m_Registry;
        private BoxCollider m_Col;
        private NavMeshObstacle m_Obstacle;
        private float m_NextCheck;

        public bool Bridged { get; private set; } = true;

        public static ValleyBridge Create(Transform parent, PropRegistry registry, string side, float halfDepth)
        {
            var slots = new List<PropSlot>(registry.SlotsOf(PlaceKind.Valley, side));
            if (slots.Count == 0) return null;
            float x = 0f, zSum = 0f, top = 0f;
            foreach (PropSlot s in slots)
            {
                x += s.Position.x;
                zSum += s.Position.z;
                top = Mathf.Max(top, s.Position.y + FieldProp.SizeOf(PropKind.Block).y);   // ブロックの上面 = 高台の床
            }
            x /= slots.Count;
            float zSign = Mathf.Sign(zSum);

            var go = new GameObject("ValleyBridge_" + side);
            go.layer = Tuning.LayerBridge;
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, top - 0.01f, zSign * (halfDepth - HighlandDepth / 2f));
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(SlitWidth + 0.1f, 0.02f, HighlandDepth);
            var obstacle = go.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = new Vector3(SlitWidth + 0.1f, 0.6f, HighlandDepth);
            obstacle.carving = true;
            obstacle.carveOnlyStationary = false;
            obstacle.enabled = false;   // 橋がある間は削らない (焼いた NavMesh は橋込み)
            var bridge = go.AddComponent<ValleyBridge>();
            bridge.m_Slots = slots;
            bridge.m_Registry = registry;
            bridge.m_Col = col;
            bridge.m_Obstacle = obstacle;
            return bridge;
        }

        private void Update()
        {
            if (Time.time < m_NextCheck) return;
            m_NextCheck = Time.time + 0.5f;
            bool bridged = m_Registry.IsRemote;   // クライアントは置き場所の中身を持たない (物理も回さない) ので橋は出したまま
            if (!bridged)
            {
                bridged = true;
                foreach (PropSlot s in m_Slots) if (!s.Full) { bridged = false; break; }
            }
            if (bridged == Bridged) return;
            Bridged = bridged;
            m_Col.enabled = bridged;
            m_Obstacle.enabled = !bridged;
        }
    }
}
