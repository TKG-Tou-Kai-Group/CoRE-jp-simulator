using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSim.Net
{
    /// <summary>
    /// 対戦の通信プロトコル。ホスト権威型で、クライアントは操作だけを送り、
    /// ホストが物理と試合進行を回して全員へ状態 (スナップショット) を配る。
    /// </summary>
    /// <remarks>
    /// すべてのメッセージは「最新の状態」を表すので、落ちても次で追いつける。
    /// 再送や順序保証の層は持たない。UDP でも TCP でも同じバイト列を流す
    /// (TCP は 2 バイトの長さ接頭辞でフレーム化する)。
    ///
    /// 会場の閉域網 (Secure Share Net など) では使えるポートが限られるので、
    /// 通信はすべて 1 つのポート番号で済ませる。LAN 内の部屋探しも同じポートへの
    /// ブロードキャストで行う。
    /// </remarks>
    public enum MsgType : byte
    {
        Hello = 1,     // c→h 参加要求
        Welcome = 2,   // h→c 参加許可 (スロット番号)
        Reject = 3,    // h→c 参加拒否 (理由)
        Lobby = 4,     // h→c ロビーの状態 (0.5 秒ごと)
        Input = 5,     // c→h 操作 (30 Hz)
        Snapshot = 6,  // h→c 試合の状態 (30 Hz)
        Ping = 7,      // c→h 生存確認
        Pong = 8,      // h→c
        Bye = 9,       // 双方向 退出
        Discover = 10, // c→broadcast 部屋探し
        Announce = 11, // h→c 部屋の案内
        Skill = 12,    // c→h 総大将スキルの発動要求 (大会ルール)
        Program = 13,  // c→h オートアタッカーのプログラム (ロビーで送る。ホストが実行する)
    }

    public enum LobbyState : byte
    {
        Waiting = 0,
        Playing = 1,
        Closed = 2,
    }

    public enum SlotKind : byte
    {
        Host = 0,
        Human = 1,
        Cpu = 2,
    }

    public enum TransportKind : byte
    {
        Udp = 0,
        Tcp = 1,
    }

    public struct LobbySlot
    {
        public byte Slot;
        public SlotKind Kind;
        public string Name;
    }

    public sealed class LobbyMessage
    {
        public LobbyState State;
        public byte Mode;            // 0 個人戦 / 1 チーム戦 / 2 大会ルール (CoRE-1 V27)
        public ushort MatchSeconds;
        public byte Speed;           // 0 リアル / 1 ゲーム
        public byte BestOf = 1;      // 大会ルール: 1 / 3 / 5
        public byte Builders;        // 各同盟のビルダーの数 (同盟の番号の大きい側から割り当てる)
        public ushort FireMs = 600;  // アタッカーの連射間隔 [ms] (ホストの設定を全員に見せる)
        public byte Autos;           // 各同盟のオートアタッカーの数 (0 / 1。同盟の番号の最大側)
        public List<LobbySlot> Slots = new List<LobbySlot>();
    }

    public struct InputMessage
    {
        public uint Seq;
        public float MoveX, MoveY, Turn;
        public bool Fire, Turbo;
        public float Lift;           // ビルダーの昇降 -1..1
        public bool Grab;            // ビルダーのつかむ / 放す
    }

    public struct RobotState
    {
        public byte Slot;
        public float Px, Py, Pz;
        public float Qx, Qy, Qz, Qw;
        public ushort Hp;
        public bool Ko, Turbo;
        public ushort Shots, Hits;   // 累計。クライアントは差分で効果音を鳴らす
        // ---- 大会ルールのとき ----
        public bool IsHead;
        public byte Magazine;
        public float ReviveIn;       // 復活までの秒。負は復活しない
        public float InvincibleFor;  // 無敵の残り秒
        public float FrozenFor;      // ピットイン停止の残り秒
        public ushort MaxHp;
        public bool PitInGranted;    // ピットインが発動されている
        // ---- オートアタッカー ----
        public bool IsAuto;
        public bool AutoStarted;     // 始動の命令を受けた
        public bool AutoStopped;     // 停止の命令を受けた (再始動なし)
        // ---- ビルダー ----
        public bool IsBuilder;
        public float Lift;           // ハンドの高さ [m]
        public ushort Held;          // 持っている物体の番号 (0 = 無し)
    }

    /// <summary>ブロック・コンテナの位置。番号は manifest の並び順で、ホストとクライアントで同じ。</summary>
    public struct PropState
    {
        public ushort Id;
        public bool Held, InSlot;
        public float Px, Py, Pz;
        public float Qx, Qy, Qz, Qw;
    }

    /// <summary>大会ルールの同盟の状態 (スナップショットに 2 つ入る)。</summary>
    public struct AllianceState
    {
        public int Vp;               // 反則で負になることがある
        public ushort Rp;
        public ushort BaseHp;
        public bool BaseDestroyed;
        public ushort Discs;
        public float BoostFor;
        public byte BarrierLevel;
        public float BarrierFor;
        public float SkillLockFor;
        public byte PitInCredits;
        public byte Wins;            // シリーズの勝ち数
        public byte Sites;           // 獲得したビルドサイト (bit 0 = 1)
        public uint Spots;           // 得点済みのスポット (bit 0 = 1)
        public byte Containers;      // スポットに置かれたコンテナの数
    }

    public struct DiscState
    {
        public ushort Id;
        public bool Mini;            // オートアタッカーのミニディスク
        public float Px, Py, Pz;
        public float Vx, Vy, Vz;
    }

    public sealed class SnapshotMessage
    {
        public uint Tick;
        /// <summary>何試合目か (ホストが試合を始めるたびに +1)。参加者は変わったら試合を作り直す (再戦)。</summary>
        public byte Serial;
        public byte Phase;           // CoreSim.Rules.MatchPhase
        public float Remaining;
        public byte OutcomeKind;     // CoreSim.Rules.OutcomeKind
        public sbyte OutcomeIndex;
        public List<RobotState> Robots = new List<RobotState>();
        public List<PropState> Props = new List<PropState>();
        public List<DiscState> Discs = new List<DiscState>();
        // ---- 大会ルールのとき ----
        public bool Tournament;
        public float RoundTime;      // ラウンド開始からの秒
        public byte RoundNumber;
        public byte RoundWinner;     // 0 赤 / 1 青 / 2 引き分け / 255 未定
        public AllianceState Red;
        public AllianceState Blue;
    }

    public struct AnnounceMessage
    {
        public string HostName;
        public ushort Port;
        public TransportKind Transport;
        public byte Players;
        public LobbyState State;
    }

    public sealed class ByteWriter
    {
        private byte[] m_Buffer = new byte[1024];
        public int Length { get; private set; }
        public byte[] Buffer => m_Buffer;

        public void Reset() => Length = 0;

        private void Ensure(int n)
        {
            if (Length + n > m_Buffer.Length)
            {
                Array.Resize(ref m_Buffer, Math.Max(m_Buffer.Length * 2, Length + n));
            }
        }

        public void Byte(byte v) { Ensure(1); m_Buffer[Length++] = v; }
        public void SByte(sbyte v) => Byte(unchecked((byte)v));
        public void Bool(bool v) => Byte(v ? (byte)1 : (byte)0);
        public void UShort(ushort v) { Ensure(2); m_Buffer[Length++] = (byte)v; m_Buffer[Length++] = (byte)(v >> 8); }
        public void UInt(uint v) { Ensure(4); for (int i = 0; i < 4; i++) m_Buffer[Length++] = (byte)(v >> (8 * i)); }
        public void Int(int v) => UInt(unchecked((uint)v));
        public void Float(float v) => UInt(BitConverter.ToUInt32(BitConverter.GetBytes(v), 0));
        /// <summary>長い文字列 (2 バイト長)。maxBytes を超えるぶんは切る。</summary>
        public void LongString(string s, int maxBytes)
        {
            byte[] b = Encoding.UTF8.GetBytes(s ?? "");
            int n = Math.Min(b.Length, Math.Min(maxBytes, ushort.MaxValue));
            UShort((ushort)n);
            Ensure(n);
            Array.Copy(b, 0, m_Buffer, Length, n);
            Length += n;
        }

        public void String(string s)
        {
            byte[] b = Encoding.UTF8.GetBytes(s ?? "");
            int n = Math.Min(b.Length, 255);
            Byte((byte)n);
            Ensure(n);
            Array.Copy(b, 0, m_Buffer, Length, n);
            Length += n;
        }

        public byte[] ToArray()
        {
            var a = new byte[Length];
            Array.Copy(m_Buffer, a, Length);
            return a;
        }
    }

    public sealed class ByteReader
    {
        private readonly byte[] m_Data;
        private readonly int m_Length;
        private int m_Pos;

        public ByteReader(byte[] data, int length)
        {
            m_Data = data;
            m_Length = length;
        }

        public int Remaining => m_Length - m_Pos;

        private void Need(int n)
        {
            if (m_Pos + n > m_Length) throw new InvalidOperationException("message truncated");
        }

        public byte Byte() { Need(1); return m_Data[m_Pos++]; }
        public sbyte SByte() => unchecked((sbyte)Byte());
        public bool Bool() => Byte() != 0;
        public ushort UShort() { Need(2); ushort v = (ushort)(m_Data[m_Pos] | (m_Data[m_Pos + 1] << 8)); m_Pos += 2; return v; }
        public uint UInt() { Need(4); uint v = 0; for (int i = 0; i < 4; i++) v |= (uint)m_Data[m_Pos + i] << (8 * i); m_Pos += 4; return v; }
        public int Int() => unchecked((int)UInt());
        public float Float() => BitConverter.ToSingle(BitConverter.GetBytes(UInt()), 0);
        public string LongString(int maxBytes)
        {
            int n = UShort();
            if (n > maxBytes) throw new InvalidOperationException("文字列が長すぎる");
            Need(n);
            string s = Encoding.UTF8.GetString(m_Data, m_Pos, n);
            m_Pos += n;
            return s;
        }

        public string String()
        {
            int n = Byte();
            Need(n);
            string s = Encoding.UTF8.GetString(m_Data, m_Pos, n);
            m_Pos += n;
            return s;
        }
    }

    public static class NetCodec
    {
        public const ushort ProtocolVersion = 6;

        /// <summary>名前に使える文字数。表示を崩す制御文字と改行は落とす。</summary>
        public const int MaxNameLength = 16;

        public static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            // <size=..> のようなタグはまるごと落とす (uGUI のリッチテキストとして解釈されるのを防ぐ)。
            name = System.Text.RegularExpressions.Regex.Replace(name, "<[^>]*>", "");
            var sb = new StringBuilder();
            foreach (char c in name)
            {
                if (char.IsControl(c) || c == '<' || c == '>') continue;
                sb.Append(c);
                if (sb.Length >= MaxNameLength) break;
            }
            return sb.ToString().Trim();
        }

        public static MsgType PeekType(byte[] data, int length) => length > 0 ? (MsgType)data[0] : (MsgType)0;

        // ---- 書き込み ----------------------------------------------------

        /// <param name="password">部屋の合言葉 (無ければ空)。平文で流れるので LAN 内の覗き見には効かないが、
        /// 同じ会場の無関係な人が間違えて (あるいは面白半分で) 入るのを防ぐ。</param>
        public static void WriteHello(ByteWriter w, string name, string password)
        {
            w.Reset(); w.Byte((byte)MsgType.Hello); w.UShort(ProtocolVersion); w.String(name); w.String(password ?? "");
        }

        /// <param name="token">参加者ごとの乱数。以後のメッセージに付けさせ、送信元の偽装による操作の注入を防ぐ。</param>
        public static void WriteWelcome(ByteWriter w, byte slot, uint token)
        {
            w.Reset(); w.Byte((byte)MsgType.Welcome); w.Byte(slot); w.UInt(token);
        }

        public static void WriteReject(ByteWriter w, string reason)
        {
            w.Reset(); w.Byte((byte)MsgType.Reject); w.String(reason);
        }

        public static void WriteLobby(ByteWriter w, LobbyMessage m)
        {
            w.Reset(); w.Byte((byte)MsgType.Lobby);
            w.Byte((byte)m.State); w.Byte(m.Mode); w.UShort(m.MatchSeconds); w.Byte(m.Speed); w.Byte(m.BestOf); w.Byte(m.Builders); w.UShort(m.FireMs); w.Byte(m.Autos);
            w.Byte((byte)Math.Min(m.Slots.Count, 255));
            foreach (LobbySlot s in m.Slots)
            {
                w.Byte(s.Slot); w.Byte((byte)s.Kind); w.String(s.Name);
            }
        }

        public static void WriteInput(ByteWriter w, InputMessage m, uint token)
        {
            w.Reset(); w.Byte((byte)MsgType.Input); w.UInt(token);
            w.UInt(m.Seq); w.Float(m.MoveX); w.Float(m.MoveY); w.Float(m.Turn);
            w.Byte((byte)((m.Fire ? 1 : 0) | (m.Turbo ? 2 : 0) | (m.Grab ? 4 : 0)));
            w.Float(m.Lift);
        }

        public static void WriteSnapshot(ByteWriter w, SnapshotMessage m)
        {
            w.Reset(); w.Byte((byte)MsgType.Snapshot);
            w.UInt(m.Tick); w.Byte(m.Serial); w.Byte(m.Phase); w.Float(m.Remaining); w.Byte(m.OutcomeKind); w.SByte(m.OutcomeIndex);
            w.Bool(m.Tournament);
            w.Byte((byte)Math.Min(m.Robots.Count, 255));
            foreach (RobotState r in m.Robots)
            {
                w.Byte(r.Slot);
                w.Float(r.Px); w.Float(r.Py); w.Float(r.Pz);
                w.Float(r.Qx); w.Float(r.Qy); w.Float(r.Qz); w.Float(r.Qw);
                w.UShort(r.Hp);
                w.Byte((byte)((r.Ko ? 1 : 0) | (r.Turbo ? 2 : 0) | (r.IsHead ? 4 : 0) | (r.PitInGranted ? 8 : 0) | (r.IsBuilder ? 16 : 0)
                              | (r.IsAuto ? 32 : 0) | (r.AutoStarted ? 64 : 0) | (r.AutoStopped ? 128 : 0)));
                w.UShort(r.Shots); w.UShort(r.Hits);
                w.Float(r.Lift); w.UShort(r.Held);
                if (m.Tournament)
                {
                    w.Byte(r.Magazine); w.Float(r.ReviveIn); w.Float(r.InvincibleFor); w.Float(r.FrozenFor); w.UShort(r.MaxHp);
                }
            }
            w.Byte((byte)Math.Min(m.Discs.Count, 255));
            foreach (DiscState d in m.Discs)
            {
                w.UShort(d.Id);
                w.Byte((byte)(d.Mini ? 1 : 0));
                w.Float(d.Px); w.Float(d.Py); w.Float(d.Pz);
                w.Float(d.Vx); w.Float(d.Vy); w.Float(d.Vz);
            }
            w.Byte((byte)Math.Min(m.Props.Count, 255));
            foreach (PropState p in m.Props)
            {
                w.UShort(p.Id);
                w.Byte((byte)((p.Held ? 1 : 0) | (p.InSlot ? 2 : 0)));
                w.Float(p.Px); w.Float(p.Py); w.Float(p.Pz);
                w.Float(p.Qx); w.Float(p.Qy); w.Float(p.Qz); w.Float(p.Qw);
            }
            if (m.Tournament)
            {
                w.Float(m.RoundTime); w.Byte(m.RoundNumber); w.Byte(m.RoundWinner);
                WriteAlliance(w, m.Red);
                WriteAlliance(w, m.Blue);
            }
        }

        private static void WriteAlliance(ByteWriter w, AllianceState a)
        {
            w.Int(a.Vp); w.UShort(a.Rp); w.UShort(a.BaseHp); w.Bool(a.BaseDestroyed); w.UShort(a.Discs);
            w.Float(a.BoostFor); w.Byte(a.BarrierLevel); w.Float(a.BarrierFor); w.Float(a.SkillLockFor);
            w.Byte(a.PitInCredits); w.Byte(a.Wins);
            w.Byte(a.Sites); w.UInt(a.Spots); w.Byte(a.Containers);
        }

        private static AllianceState ReadAlliance(ByteReader r)
        {
            return new AllianceState
            {
                Vp = r.Int(), Rp = r.UShort(), BaseHp = r.UShort(), BaseDestroyed = r.Bool(), Discs = r.UShort(),
                BoostFor = r.Float(), BarrierLevel = r.Byte(), BarrierFor = r.Float(), SkillLockFor = r.Float(),
                PitInCredits = r.Byte(), Wins = r.Byte(),
                Sites = r.Byte(), Spots = r.UInt(), Containers = r.Byte(),
            };
        }

        public const int MaxProgramBytes = 4000;
        public static void WriteProgram(ByteWriter w, string text, uint token) { w.Reset(); w.Byte((byte)MsgType.Program); w.UInt(token); w.LongString(text, MaxProgramBytes); }
        public static string ReadProgram(ByteReader r, out uint token) { r.Byte(); token = r.UInt(); return r.LongString(MaxProgramBytes); }

        public static void WriteSkill(ByteWriter w, byte kind, uint token) { w.Reset(); w.Byte((byte)MsgType.Skill); w.UInt(token); w.Byte(kind); }
        public static byte ReadSkill(ByteReader r, out uint token) { r.Byte(); token = r.UInt(); return r.Byte(); }

        public static void WritePing(ByteWriter w, uint stamp, uint token) { w.Reset(); w.Byte((byte)MsgType.Ping); w.UInt(token); w.UInt(stamp); }
        public static void WritePong(ByteWriter w, uint stamp) { w.Reset(); w.Byte((byte)MsgType.Pong); w.UInt(stamp); }
        public static void WriteBye(ByteWriter w, uint token = 0) { w.Reset(); w.Byte((byte)MsgType.Bye); w.UInt(token); }
        public static void WriteDiscover(ByteWriter w) { w.Reset(); w.Byte((byte)MsgType.Discover); w.UShort(ProtocolVersion); }

        public static void WriteAnnounce(ByteWriter w, AnnounceMessage m)
        {
            w.Reset(); w.Byte((byte)MsgType.Announce);
            w.UShort(ProtocolVersion); w.String(m.HostName); w.UShort(m.Port); w.Byte((byte)m.Transport);
            w.Byte(m.Players); w.Byte((byte)m.State);
        }

        // ---- 読み出し (先頭の種別バイトは読み飛ばし済みでない前提: 全部ここで読む) ----

        public static bool ReadHello(ByteReader r, out ushort version, out string name, out string password)
        {
            r.Byte(); version = r.UShort(); name = r.String(); password = r.String(); return true;
        }

        public static byte ReadWelcome(ByteReader r, out uint token) { r.Byte(); byte slot = r.Byte(); token = r.UInt(); return slot; }
        public static string ReadReject(ByteReader r) { r.Byte(); return r.String(); }

        public static LobbyMessage ReadLobby(ByteReader r)
        {
            r.Byte();
            var m = new LobbyMessage
            {
                State = (LobbyState)r.Byte(),
                Mode = r.Byte(),
                MatchSeconds = r.UShort(),
                Speed = r.Byte(),
                BestOf = r.Byte(),
                Builders = r.Byte(),
                FireMs = r.UShort(),
                Autos = r.Byte(),
            };
            int n = r.Byte();
            for (int i = 0; i < n; i++)
            {
                m.Slots.Add(new LobbySlot { Slot = r.Byte(), Kind = (SlotKind)r.Byte(), Name = r.String() });
            }
            return m;
        }

        public static InputMessage ReadInput(ByteReader r, out uint token)
        {
            r.Byte();
            token = r.UInt();
            var m = new InputMessage { Seq = r.UInt(), MoveX = r.Float(), MoveY = r.Float(), Turn = r.Float() };
            byte f = r.Byte();
            m.Fire = (f & 1) != 0;
            m.Turbo = (f & 2) != 0;
            m.Grab = (f & 4) != 0;
            m.Lift = r.Float();
            return m;
        }

        public static SnapshotMessage ReadSnapshot(ByteReader r)
        {
            r.Byte();
            var m = new SnapshotMessage
            {
                Tick = r.UInt(),
                Serial = r.Byte(),
                Phase = r.Byte(),
                Remaining = r.Float(),
                OutcomeKind = r.Byte(),
                OutcomeIndex = r.SByte(),
            };
            m.Tournament = r.Bool();
            int n = r.Byte();
            for (int i = 0; i < n; i++)
            {
                var s = new RobotState
                {
                    Slot = r.Byte(),
                    Px = r.Float(), Py = r.Float(), Pz = r.Float(),
                    Qx = r.Float(), Qy = r.Float(), Qz = r.Float(), Qw = r.Float(),
                    Hp = r.UShort(),
                };
                byte f = r.Byte();
                s.Ko = (f & 1) != 0;
                s.Turbo = (f & 2) != 0;
                s.IsHead = (f & 4) != 0;
                s.PitInGranted = (f & 8) != 0;
                s.IsBuilder = (f & 16) != 0;
                s.IsAuto = (f & 32) != 0;
                s.AutoStarted = (f & 64) != 0;
                s.AutoStopped = (f & 128) != 0;
                s.Shots = r.UShort();
                s.Hits = r.UShort();
                s.Lift = r.Float();
                s.Held = r.UShort();
                if (m.Tournament)
                {
                    s.Magazine = r.Byte(); s.ReviveIn = r.Float(); s.InvincibleFor = r.Float(); s.FrozenFor = r.Float(); s.MaxHp = r.UShort();
                }
                m.Robots.Add(s);
            }
            int d = r.Byte();
            for (int i = 0; i < d; i++)
            {
                m.Discs.Add(new DiscState
                {
                    Id = r.UShort(),
                    Mini = (r.Byte() & 1) != 0,
                    Px = r.Float(), Py = r.Float(), Pz = r.Float(),
                    Vx = r.Float(), Vy = r.Float(), Vz = r.Float(),
                });
            }
            int np = r.Byte();
            for (int i = 0; i < np; i++)
            {
                var p = new PropState { Id = r.UShort() };
                byte pf = r.Byte();
                p.Held = (pf & 1) != 0;
                p.InSlot = (pf & 2) != 0;
                p.Px = r.Float(); p.Py = r.Float(); p.Pz = r.Float();
                p.Qx = r.Float(); p.Qy = r.Float(); p.Qz = r.Float(); p.Qw = r.Float();
                m.Props.Add(p);
            }
            if (m.Tournament)
            {
                m.RoundTime = r.Float(); m.RoundNumber = r.Byte(); m.RoundWinner = r.Byte();
                m.Red = ReadAlliance(r);
                m.Blue = ReadAlliance(r);
            }
            return m;
        }

        /// <summary>Pong の時刻。</summary>
        public static uint ReadStamp(ByteReader r) { r.Byte(); return r.UInt(); }
        /// <summary>Ping のトークンと時刻。</summary>
        public static uint ReadPing(ByteReader r, out uint token) { r.Byte(); token = r.UInt(); return r.UInt(); }
        public static uint ReadByeToken(ByteReader r) { r.Byte(); return r.Remaining >= 4 ? r.UInt() : 0u; }

        public static AnnounceMessage ReadAnnounce(ByteReader r)
        {
            r.Byte();
            r.UShort();
            return new AnnounceMessage
            {
                HostName = r.String(),
                Port = r.UShort(),
                Transport = (TransportKind)r.Byte(),
                Players = r.Byte(),
                State = (LobbyState)r.Byte(),
            };
        }
    }
}
