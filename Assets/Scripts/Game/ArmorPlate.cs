using UnityEngine;

namespace CoreSim
{
    /// <summary>
    /// 装甲板。ディスクが当たると持ち主の HP を減らす。ROS 2 版の contact センサ + hp_manager に相当。
    /// </summary>
    public sealed class ArmorPlate : MonoBehaviour
    {
        public RobotAgent Owner;
        public int Index;

        private Renderer m_Renderer;
        private Material m_Normal;
        private Material m_Flash;
        private float m_FlashUntil;

        public void Setup(RobotAgent owner, int index, Color teamColor)
        {
            Owner = owner;
            Index = index;
            m_Renderer = GetComponent<Renderer>();
            m_Normal = MaterialLibrary.Color(teamColor, 0.6f);
            m_Flash = MaterialLibrary.Emissive(Color.white);
            m_Renderer.sharedMaterial = m_Normal;
        }

        public void RegisterHit(Disc disc, Vector3 point)
        {
            if (Owner == null)
            {
                return;
            }
            Owner.OnArmorHit(this, disc, point);
            m_FlashUntil = Time.time + 0.15f;
            m_Renderer.sharedMaterial = m_Flash;
        }

        /// <summary>被弾の光だけ (HP の処理は呼び手が済ませている)。</summary>
        public void FlashOnly()
        {
            m_FlashUntil = Time.time + 0.15f;
            m_Renderer.sharedMaterial = m_Flash;
        }

        private void Update()
        {
            if (m_FlashUntil > 0f && Time.time > m_FlashUntil)
            {
                m_FlashUntil = 0f;
                m_Renderer.sharedMaterial = m_Normal;
            }
        }

        /// <summary>KO 時などに色を差し替える。</summary>
        public void SetBaseColor(Color color)
        {
            m_Normal = MaterialLibrary.Color(color, 0.6f);
            if (m_FlashUntil <= 0f)
            {
                m_Renderer.sharedMaterial = m_Normal;
            }
        }
    }
}
