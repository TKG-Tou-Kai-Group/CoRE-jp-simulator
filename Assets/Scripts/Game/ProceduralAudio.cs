using UnityEngine;

namespace CoreSim
{
    /// <summary>音源ファイルを持たずに済ませるための合成効果音。</summary>
    public sealed class ProceduralAudio : MonoBehaviour
    {
        public bool Enabled = true;

        private AudioSource m_Source;
        private AudioClip m_Shot, m_Hit, m_Beep, m_Go, m_End, m_Click, m_Foul, m_Skill;

        private void Awake()
        {
            m_Source = gameObject.AddComponent<AudioSource>();
            m_Source.spatialBlend = 0f;
            m_Source.volume = 0.6f;
            m_Shot = Noise("shot", 0.07f, 0.5f);
            m_Hit = Tone("hit", 880f, 0.09f, 0.7f);
            m_Beep = Tone("beep", 660f, 0.1f, 0.6f);
            m_Go = Tone("go", 1047f, 0.35f, 0.7f);
            m_End = Tone("end", 330f, 0.6f, 0.7f);
            m_Click = Tone("click", 1200f, 0.03f, 0.3f);
            m_Foul = Tone("foul", 220f, 0.4f, 0.6f);
            m_Skill = Tone("skill", 1320f, 0.18f, 0.5f);
        }

        public void PlayShot(Vector3 at) => Play(m_Shot, 0.5f);
        public void PlayHit(Vector3 at) => Play(m_Hit, 0.8f);
        public void PlayBeep() => Play(m_Beep, 0.7f);
        public void PlayGo() => Play(m_Go, 0.9f);
        public void PlayEnd() => Play(m_End, 0.9f);
        public void PlayClick() => Play(m_Click, 0.5f);
        public void PlayFoul() => Play(m_Foul, 0.8f);
        public void PlaySkill() => Play(m_Skill, 0.6f);

        private void Play(AudioClip clip, float volume)
        {
            if (Enabled && clip != null && m_Source != null)
            {
                m_Source.PlayOneShot(clip, volume);
            }
        }

        private static AudioClip Tone(string name, float freq, float seconds, float gain)
        {
            const int rate = 22050;
            int n = Mathf.CeilToInt(seconds * rate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float env = Mathf.Min(1f, (n - i) / (0.02f * rate)) * Mathf.Min(1f, i / (0.005f * rate));
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * gain * env;
            }
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip Noise(string name, float seconds, float gain)
        {
            const int rate = 22050;
            int n = Mathf.CeilToInt(seconds * rate);
            var data = new float[n];
            var rng = new System.Random(1);
            float last = 0f;
            for (int i = 0; i < n; i++)
            {
                float env = 1f - (float)i / n;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                last = 0.6f * last + 0.4f * white;   // 低域寄りに
                data[i] = last * gain * env * env;
            }
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
