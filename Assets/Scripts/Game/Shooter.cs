using UnityEngine;

namespace CoreSim
{
    /// <summary>
    /// 射出機構。ROS 2 版は逆回転する 2 枚の射出輪でディスクを物理的に挟んで加速していたが、
    /// 方位が 1 回あたり 11 度前後散るうえ物理レートに敏感だった (README の実測値)。
    /// ここでは射出口から初速を直接与え、ばらつきは乱数で付ける。
    /// </summary>
    public sealed class Shooter : MonoBehaviour
    {
        public Transform Muzzle;
        public Transform[] Rollers = new Transform[0];
        public float Speed = Tuning.DiscSpeed;
        public float Interval = Tuning.FireInterval;
        public float DispersionDeg = Tuning.DispersionDeg;

        private RobotAgent m_Owner;
        private Rigidbody m_Body;
        private float m_NextFireTime;
        private float m_RollerSpin;

        public float Cooldown => Mathf.Max(0f, m_NextFireTime - Time.time);
        public bool Ready => Time.time >= m_NextFireTime;

        public void Setup(RobotAgent owner)
        {
            m_Owner = owner;
            m_Body = owner.GetComponent<Rigidbody>();
        }

        public bool TryFire()
        {
            if (!Ready || Muzzle == null)
            {
                return false;
            }
            var director = GameDirector.Instance;
            if (director != null && director.Tournament != null && !director.Tournament.TryConsumeShot(m_Owner))
            {
                return false;   // 残弾なし・停止中
            }
            m_NextFireTime = Time.time + Interval;

            float yaw = Random.Range(-DispersionDeg, DispersionDeg);
            float pitch = Random.Range(-DispersionDeg * 0.5f, DispersionDeg * 0.5f);
            Vector3 dir = Muzzle.rotation * Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
            Vector3 inherited = m_Body != null ? m_Body.linearVelocity : Vector3.zero;
            Disc.Launch(m_Owner, Muzzle.position, dir, Speed, inherited);
            if (m_Owner != null) m_Owner.ShotCount++;
            m_RollerSpin = 1f;
            GameDirector.Instance?.Audio.PlayShot(Muzzle.position);
            return true;
        }

        private void Update()
        {
            if (m_RollerSpin <= 0f)
            {
                return;
            }
            m_RollerSpin = Mathf.Max(0f, m_RollerSpin - Time.deltaTime);
            float deg = 1800f * m_RollerSpin * Time.deltaTime;
            for (int i = 0; i < Rollers.Length; i++)
            {
                if (Rollers[i] != null)
                {
                    Rollers[i].Rotate(0f, i == 0 ? deg : -deg, 0f, Space.Self);
                }
            }
        }
    }
}
