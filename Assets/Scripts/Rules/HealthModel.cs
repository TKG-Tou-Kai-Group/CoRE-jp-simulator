using System;

namespace CoreSim.Rules
{
    /// <summary>
    /// 1 台ぶんの HP。装甲板への被弾 1 回につき <see cref="DamagePerHit"/> 減る。
    /// </summary>
    /// <remarks>
    /// CoRE-jp-Unity-ROS2-packages の hp_manager/manager_node.py に対応する。
    /// そちらは接触回数の累積値の差分から被弾数を出していたが、このゲームでは
    /// ディスクと装甲板の衝突イベントを直接数えるので、差分計算は要らない。
    /// </remarks>
    public sealed class HealthModel
    {
        public int MaxHp { get; }
        public int DamagePerHit { get; }
        public int Hp { get; private set; }
        /// <summary>被弾した回数の累計 (リセットで 0)。</summary>
        public int Hits { get; private set; }

        public bool IsAlive => Hp > 0;
        public float Ratio => MaxHp > 0 ? (float)Hp / MaxHp : 0f;

        public HealthModel(int maxHp, int damagePerHit)
        {
            if (maxHp <= 0) throw new ArgumentOutOfRangeException(nameof(maxHp));
            if (damagePerHit <= 0) throw new ArgumentOutOfRangeException(nameof(damagePerHit));
            MaxHp = maxHp;
            DamagePerHit = damagePerHit;
            Hp = maxHp;
        }

        /// <summary>被弾を適用し、実際に減った HP を返す。HP 0 の機体には効かない。</summary>
        public int Hit(int count = 1)
        {
            if (count <= 0 || Hp <= 0)
            {
                return 0;
            }
            int before = Hp;
            // HP を 0 にするのに要る発数までを被弾として数える (余りは当たっていない扱い)。
            int effective = Math.Min(count, (Hp + DamagePerHit - 1) / DamagePerHit);
            Hp = Math.Max(0, Hp - DamagePerHit * effective);
            Hits += effective;
            return before - Hp;
        }

        public void Reset()
        {
            Hp = MaxHp;
            Hits = 0;
        }

        /// <summary>ネット対戦のクライアントがホストの値を写すときに使う。</summary>
        public void Set(int hp, int hits)
        {
            Hp = Math.Max(0, Math.Min(MaxHp, hp));
            Hits = Math.Max(0, hits);
        }
    }
}
