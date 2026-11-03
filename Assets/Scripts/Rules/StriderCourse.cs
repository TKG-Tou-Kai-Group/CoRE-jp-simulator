using System.Collections.Generic;

namespace CoreSim.Rules
{
    /// <summary>ストライダーエリアのゾーン (3.8)。スタート側から順。</summary>
    public enum StriderZone : byte
    {
        Flat = 0,     // 平地
        Step = 1,     // 段差
        Gravel = 2,   // 砂利
        Bumps = 3,    // 凹凸
        Slope = 4,    // スロープ
        Hurdle = 5,   // ハードル
        Stairs = 6,   // 階段 (終わりのテープをまたぐと全ゾーン踏破)
    }

    /// <summary>
    /// ストライダーの踏破の判定 (7.7)。どれか 1 本の脚がゾーンの終わりの線をまたぐとそのゾーンを踏破。
    /// 階段ゾーンの終わりのテープをまたぐと全ゾーン踏破。得点は最初の 1 回だけ。
    /// 線の位置はルールナビの 3D モデルの白線 (赤サイドのエリア。青は点対称)。
    /// </summary>
    public sealed class StriderCourse
    {
        /// <summary>赤サイドのエリアの線の x [m]: スタートゾーンの終わり、各ゾーンの終わり (最後は階段ゾーンの終わりのテープ)。</summary>
        public static readonly float[] RedLines = { -10.8f, -7.6f, -4.5f, -1.7f, 1.08f, 5.38f, 8.58f, 10.8f };
        public const float AreaEndX = 12f;           // エリアの端 (ゴールゾーンの終わり)
        public const float LaneNearZ = 7.7f;         // エリアの幅 1.5 m: |z| が 7.7..9.2
        public const float LaneFarZ = 9.2f;
        public const int ZoneCount = 7;
        public const int ZoneVp = 10;                // 5.3 (平地〜ハードルの 6 ゾーン)
        public const int AllZonesVp = 40;            // 5.3 全ゾーン踏破
        public static int ZoneRp(StriderZone z) => z == StriderZone.Slope || z == StriderZone.Hurdle ? 60 : z == StriderZone.Stairs ? 0 : 40;   // 5.4

        public Side Side { get; }
        public int ClearedMask { get; private set; }
        public bool AllCleared => ClearedMask == (1 << ZoneCount) - 1;
        public int Vp { get; private set; }
        public int Rp { get; private set; }

        public StriderCourse(Side side) { Side = side; }

        /// <summary>進行方向 (+1 = x が増える向き)。赤は -12 から +12 へ。</summary>
        public float Direction => Side == Side.Red ? 1f : -1f;
        /// <summary>線 i の x (0 = スタートゾーンの終わり、i+1 = ゾーン i の終わり)。</summary>
        public float LineX(int i) => Side == Side.Red ? RedLines[i] : -RedLines[i];
        public float LaneCenterZ => Side == Side.Red ? -(LaneNearZ + LaneFarZ) / 2f : (LaneNearZ + LaneFarZ) / 2f;
        public float StartX => Side == Side.Red ? -AreaEndX + 0.6f : AreaEndX - 0.6f;
        public float ZoneStartX(StriderZone z) => LineX((int)z);
        public float ZoneEndX(StriderZone z) => LineX((int)z + 1);

        public bool InLane(float x, float z)
        {
            float lz = Side == Side.Red ? -z : z;
            return lz >= LaneNearZ && lz <= LaneFarZ && x >= -AreaEndX && x <= AreaEndX;
        }

        public bool IsCleared(StriderZone z) => (ClearedMask & (1 << (int)z)) != 0;

        /// <summary>まだ踏破していない最初のゾーン (全部踏破なら null)。</summary>
        public StriderZone? Current
        {
            get
            {
                for (int i = 0; i < ZoneCount; i++) if (!IsCleared((StriderZone)i)) return (StriderZone)i;
                return null;
            }
        }

        /// <summary>
        /// 脚先の x を渡して踏破を判定する。順に、終わりの線を越えた脚があれば踏破。1 回の呼び出しで複数のゾーンを
        /// 踏破することもある (線を飛ばして渡した場合)。戻り値は新しく踏破したゾーン。
        /// </summary>
        public List<StriderZone> Update(IEnumerable<float> footXs)
        {
            var cleared = new List<StriderZone>();
            float best = float.NegativeInfinity;
            foreach (float x in footXs) best = System.Math.Max(best, x * Direction);
            if (float.IsNegativeInfinity(best)) return cleared;
            for (int i = 0; i < ZoneCount; i++)
            {
                var z = (StriderZone)i;
                if (IsCleared(z)) continue;
                float end = ZoneEndX(z) * Direction;
                if (best <= end) break;    // ここで止まる (手前のゾーンから順)
                ClearedMask |= 1 << i;
                cleared.Add(z);
                if (z == StriderZone.Stairs) Vp += AllZonesVp;
                else { Vp += ZoneVp; Rp += ZoneRp(z); }
            }
            return cleared;
        }

        public void Reset()
        {
            ClearedMask = 0;
            Vp = 0;
            Rp = 0;
        }

        public static string ZoneName(StriderZone z) => z switch
        {
            StriderZone.Flat => "平地",
            StriderZone.Step => "段差",
            StriderZone.Gravel => "砂利",
            StriderZone.Bumps => "凹凸",
            StriderZone.Slope => "スロープ",
            StriderZone.Hurdle => "ハードル",
            _ => "階段",
        };
    }
}
