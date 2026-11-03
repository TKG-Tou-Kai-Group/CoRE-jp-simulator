using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace CoreSim
{
    /// <summary>
    /// 外周の高台の谷部 (3.6) の NavMesh 上の繋ぎ。高台は中央で幅 0.38 m・深さ 0.2 m の溝に分かれていて、
    /// ブロック 2 個で埋めると渡れる。機体は車輪の間隔 (0.7 m) が溝より広いので真っ直ぐなら溝をまたげるが、
    /// NavMesh (半径 0.45 m) は溝で高台を切るので、CPU の経路のために <see cref="NavMeshLink"/> で左右を繋ぐ。
    /// 物理的な橋は置かない (実機と同じく、斜めに入れば車輪が溝に落ちる)。ブロックを抜かれたら繋ぎを切る。
    /// </summary>
    public sealed class ValleyBridge : MonoBehaviour
    {
        public const float HighlandDepth = 1.5f;   // 高台の奥行き (フィールドの壁から)。ルールナビの 3D モデルより
        public const float LinkHalfSpan = 0.75f;   // 溝の中心から両端まで (削られた NavMesh の上に乗る距離)

        private List<PropSlot> m_Slots;
        private PropRegistry m_Registry;
        private NavMeshLink m_Link;
        private float m_NextCheck;
        private bool m_Snapped;

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
            // 平らな側 (自同盟側) と、反対側の下りスロープ。スロープは溝の縁 (0.19) から 0.81 m で床に着く
            float flatSign = side == "red" ? -1f : 1f;
            float rampY = top * Mathf.Clamp01(1f - (LinkHalfSpan - 0.19f) / 0.81f);

            var go = new GameObject("ValleyLink_" + side);
            go.transform.SetParent(parent, false);
            // 壁ぎわ 0.45 m は NavMesh が削られるので、高台の奥行きの内側寄りに置く
            go.transform.position = new Vector3(x, top, zSign * (halfDepth - 0.6f));
            var link = go.AddComponent<NavMeshLink>();
            link.agentTypeID = 0;
            link.bidirectional = true;
            link.autoUpdate = false;
            link.width = 0.8f;
            link.startPoint = new Vector3(flatSign * LinkHalfSpan, 0f, 0f);
            link.endPoint = new Vector3(-flatSign * LinkHalfSpan, rampY - top, 0f);
            var bridge = go.AddComponent<ValleyBridge>();
            bridge.m_Slots = slots;
            bridge.m_Registry = registry;
            bridge.m_Link = link;
            return bridge;
        }

        private void Update()
        {
            if (!m_Snapped)
            {
                // NavMesh を焼いた後、両端を実際の NavMesh の上に合わせる (端が浮いていると繋がらない)
                m_Snapped = true;
                bool a = NavMesh.SamplePosition(transform.TransformPoint(m_Link.startPoint), out NavMeshHit ha, 0.7f, NavMesh.AllAreas);
                bool b = NavMesh.SamplePosition(transform.TransformPoint(m_Link.endPoint), out NavMeshHit hb, 0.7f, NavMesh.AllAreas);
                if (a) m_Link.startPoint = transform.InverseTransformPoint(ha.position);
                if (b) m_Link.endPoint = transform.InverseTransformPoint(hb.position);
                if (!a || !b) Debug.LogWarning($"[ValleyBridge] {name}: NavMesh の上に端が見つからない (start={a}, end={b})");
                m_Link.UpdateLink();
            }
            if (Time.time < m_NextCheck) return;
            m_NextCheck = Time.time + 0.5f;
            bool bridged = m_Registry.IsRemote;   // クライアントは置き場所の中身を持たない (CPU も動かさない)
            if (!bridged)
            {
                bridged = true;
                foreach (PropSlot s in m_Slots) if (!s.Full) { bridged = false; break; }
            }
            if (bridged == Bridged) return;
            Bridged = bridged;
            m_Link.enabled = bridged;
        }
    }
}
