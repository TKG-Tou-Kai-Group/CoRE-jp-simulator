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
    /// 物理的な橋は置かない (実機と同じく、斜めに入れば車輪が溝に落ちる)。
    /// 繋ぎはブロックが 2 個以上入っている区間に合わせて動かし、1 個以下なら切る。
    /// CPU には陣地とスポットの間を通る道を優先させる (繋ぎのコストを高くする)。谷部は狭く、斜めに入った CPU が
    /// 車輪を落として動けなくなったため。繋ぎは陣地側の道が無いときの予備。
    /// </summary>
    public sealed class ValleyBridge : MonoBehaviour
    {
        public const float HighlandDepth = 1.5f;   // 高台の奥行き (フィールドの壁から)。ルールナビの 3D モデルより
        public const float LinkHalfSpan = 0.75f;   // 溝の中心から両端まで (削られた NavMesh の上に乗る距離)
        public const float LinkCost = 12f;         // 繋ぎを通る追加コスト [m 相当]。陣地側の道 (約 19 m) より高台 (約 15 m) を不利にする
        public const int MinBlocks = 2;            // 渡れるとみなす最少のブロック数 (0.6 m)

        private List<PropSlot> m_Slots;
        private PropRegistry m_Registry;
        private NavMeshLink m_Link;
        private float m_NextCheck;
        private int m_Applied = -1;    // 適用済みの埋まり方 (ビットマスク)
        private float m_FlatSign;
        private float m_Top;
        private float m_RampY;

        public bool Bridged { get; private set; }

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
            go.transform.position = new Vector3(x, top, zSign * (halfDepth - HighlandDepth / 2f));
            var link = go.AddComponent<NavMeshLink>();
            link.agentTypeID = 0;
            link.bidirectional = true;
            link.autoUpdate = false;
            link.costModifier = LinkCost;
            link.enabled = false;
            var bridge = go.AddComponent<ValleyBridge>();
            bridge.m_Slots = slots;
            bridge.m_Registry = registry;
            bridge.m_Link = link;
            bridge.m_FlatSign = flatSign;
            bridge.m_Top = top;
            bridge.m_RampY = rampY;
            return bridge;
        }

        private void Update()
        {
            if (m_Link == null || m_Registry == null || m_Slots == null) return;   // Editor のホットリロード後は参照が消える
            if (Time.time < m_NextCheck) return;
            m_NextCheck = Time.time + 0.5f;
            // 埋まっている置き場所 (クライアントは中身を持たないので開始時の 2 個とみなす)
            int mask = 0, count = 0;
            float zSum = 0f;
            for (int i = 0; i < m_Slots.Count; i++)
            {
                bool full = m_Registry.IsRemote ? m_Slots[i].Index <= 2 : m_Slots[i].Full;
                if (!full) continue;
                mask |= 1 << i;
                count++;
                zSum += m_Slots[i].Position.z;
            }
            if (mask == m_Applied) return;
            m_Applied = mask;
            Bridged = count >= MinBlocks;
            if (!Bridged)
            {
                m_Link.enabled = false;
                return;
            }
            // ブロックが入っている区間の中央に繋ぎを置き、幅をブロックの数ぶんにする。両端は NavMesh の面に合わせる
            // (壁ぎわ 0.45 m は削られているので、端が浮いていると繋がらない)
            float z = zSum / count;
            Vector3 a = new Vector3(transform.position.x + m_FlatSign * LinkHalfSpan, m_Top, z);
            Vector3 b = new Vector3(transform.position.x - m_FlatSign * LinkHalfSpan, m_RampY, z);
            bool okA = NavMesh.SamplePosition(a, out NavMeshHit ha, 0.7f, NavMesh.AllAreas);
            bool okB = NavMesh.SamplePosition(b, out NavMeshHit hb, 0.7f, NavMesh.AllAreas);
            if (!okA || !okB)
            {
                Debug.LogWarning($"[ValleyBridge] {name}: NavMesh の上に端が見つからない (start={okA}, end={okB})");
                m_Link.enabled = false;
                return;
            }
            m_Link.startPoint = transform.InverseTransformPoint(ha.position);
            m_Link.endPoint = transform.InverseTransformPoint(hb.position);
            m_Link.width = FieldProp.SizeOf(PropKind.Block).z * count;
            m_Link.enabled = true;
            m_Link.UpdateLink();
        }
    }
}
