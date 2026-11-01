using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace CoreSim.Net
{
    /// <summary>
    /// ホスト側のセッション。参加者の管理、操作の受け取り、ロビーとスナップショットの配信。
    /// ゲーム本体 (Unity) からは <see cref="Pump"/> を毎フレーム呼び、
    /// 試合中は <see cref="BroadcastSnapshot"/> を 30 Hz で呼ぶ。
    /// </summary>
    public sealed class HostSession : IDisposable
    {
        public sealed class Client
        {
            public NetEndpoint Endpoint;
            public byte Slot;
            public string Name;
            public uint Token;
            public double LastSeen;
            public InputMessage LastInput;
            public double LastInputTime = double.NegativeInfinity;
        }

        public const double ClientTimeout = 6.0;
        public const double LobbyInterval = 0.5;
        /// <summary>1 回の Pump で処理するパケット数の上限。受信の洪水で 1 フレームが伸びないようにする。</summary>
        public const int MaxPacketsPerPump = 2000;
        /// <summary>参加していない相手への応答 (Reject / Announce) の 1 秒あたりの上限。反射・増幅に使われないため。</summary>
        public const int MaxUnsolicitedRepliesPerSecond = 20;

        /// <summary>部屋の合言葉。空なら誰でも入れる。</summary>
        public string Password = "";

        public string HostName;
        public LobbyState State = LobbyState.Waiting;
        public byte Mode;
        public ushort MatchSeconds = 120;
        public byte Speed = 1;
        public byte BestOf = 1;
        public int CpuCount;
        public int MaxSlots = 8;

        /// <summary>クライアントから届いたスキル要求 (スロット, 種別)。ゲーム側が取り出す。</summary>
        public readonly Queue<(byte slot, byte kind)> SkillRequests = new Queue<(byte, byte)>();

        public event Action<Client> Joined;
        public event Action<Client> Left;

        private readonly INetTransport m_Transport;
        private readonly UdpTransport m_Discovery;   // TCP 運用時に部屋探しへ応える UDP。UDP 運用時は m_Transport と同じ
        private readonly ushort m_Port;
        private readonly TransportKind m_Kind;
        private readonly List<Client> m_Clients = new List<Client>();
        private readonly ByteWriter m_Writer = new ByteWriter();
        private readonly RandomNumberGenerator m_Rng = RandomNumberGenerator.Create();
        private double m_NextLobbyTime;
        private double m_ReplyWindowStart;
        private int m_RepliesInWindow;

        public IReadOnlyList<Client> Clients => m_Clients;
        public int Port => m_Port;
        public TransportKind Kind => m_Kind;

        public HostSession(INetTransport transport, UdpTransport discovery, string hostName, ushort port, TransportKind kind)
        {
            m_Transport = transport;
            m_Discovery = discovery;
            HostName = NetCodec.SanitizeName(hostName);
            if (string.IsNullOrEmpty(HostName)) HostName = "Host";
            m_Port = port;
            m_Kind = kind;
        }

        /// <summary>受信処理・タイムアウト・ロビー配信。<paramref name="now"/> は単調増加する秒。</summary>
        public void Pump(double now)
        {
            Received packet;
            int budget = MaxPacketsPerPump;
            while (budget-- > 0 && m_Transport.TryReceive(out packet))
            {
                Handle(packet, now, m_Transport);
            }
            if (m_Discovery != null && !ReferenceEquals(m_Discovery, m_Transport))
            {
                while (budget-- > 0 && m_Discovery.TryReceive(out packet))
                {
                    Handle(packet, now, m_Discovery);
                }
            }

            NetEndpoint gone;
            while (m_Transport.TryTakeDisconnected(out gone))
            {
                Remove(FindClient(gone));
            }
            for (int i = m_Clients.Count - 1; i >= 0; i--)
            {
                if (now - m_Clients[i].LastSeen > ClientTimeout)
                {
                    Remove(m_Clients[i]);
                }
            }

            if (now >= m_NextLobbyTime)
            {
                m_NextLobbyTime = now + LobbyInterval;
                BroadcastLobby();
            }
        }

        /// <summary>参加していない相手への応答を数え、上限を超えたら送らない。</summary>
        private bool AllowUnsolicitedReply(double now)
        {
            if (now - m_ReplyWindowStart >= 1.0)
            {
                m_ReplyWindowStart = now;
                m_RepliesInWindow = 0;
            }
            return ++m_RepliesInWindow <= MaxUnsolicitedRepliesPerSecond;
        }

        private uint NewToken()
        {
            var bytes = new byte[4];
            m_Rng.GetBytes(bytes);
            uint token = BitConverter.ToUInt32(bytes, 0);
            return token == 0 ? 1u : token;
        }

        private void Handle(Received packet, double now, INetTransport via)
        {
            MsgType type = NetCodec.PeekType(packet.Data, packet.Length);
            var reader = new ByteReader(packet.Data, packet.Length);
            Client client = FindClient(packet.From);
            try
            {
                switch (type)
                {
                    case MsgType.Hello:
                    {
                        NetCodec.ReadHello(reader, out ushort version, out string name, out string password);
                        if (client != null)
                        {
                            client.LastSeen = now;
                            SendWelcome(client);          // 再送された Hello には改めて答える
                            break;
                        }
                        if (version != NetCodec.ProtocolVersion)
                        {
                            if (AllowUnsolicitedReply(now)) SendReject(packet.From, "バージョンが違います");
                        }
                        else if (!string.IsNullOrEmpty(Password) && password != Password)
                        {
                            if (AllowUnsolicitedReply(now)) SendReject(packet.From, "合言葉が違います");
                        }
                        else if (State != LobbyState.Waiting)
                        {
                            if (AllowUnsolicitedReply(now)) SendReject(packet.From, "試合中です");
                        }
                        else if (m_Clients.Count + 1 >= MaxSlots)
                        {
                            if (AllowUnsolicitedReply(now)) SendReject(packet.From, "満員です");
                        }
                        else
                        {
                            string clean = NetCodec.SanitizeName(name);
                            client = new Client
                            {
                                Endpoint = packet.From,
                                Slot = NextFreeSlot(),
                                Name = string.IsNullOrEmpty(clean) ? "Player" : clean,
                                Token = NewToken(),
                                LastSeen = now,
                            };
                            m_Clients.Add(client);
                            SendWelcome(client);
                            BroadcastLobby();
                            Joined?.Invoke(client);
                        }
                        break;
                    }
                    case MsgType.Input:
                        if (client != null)
                        {
                            InputMessage input = NetCodec.ReadInput(reader, out uint token);
                            if (token != client.Token) break;   // 送信元を偽装した注入は捨てる
                            if (input.Seq >= client.LastInput.Seq || client.LastInputTime < 0)
                            {
                                client.LastInput = input;
                                client.LastInputTime = now;
                            }
                            client.LastSeen = now;
                        }
                        break;
                    case MsgType.Ping:
                        if (client != null)
                        {
                            uint stamp = NetCodec.ReadPing(reader, out uint token);
                            if (token != client.Token) break;
                            client.LastSeen = now;
                            NetCodec.WritePong(m_Writer, stamp);
                            m_Transport.Send(m_Writer.Buffer, m_Writer.Length, client.Endpoint);
                        }
                        break;
                    case MsgType.Skill:
                        if (client != null)
                        {
                            byte kind = NetCodec.ReadSkill(reader, out uint token);
                            if (token != client.Token) break;
                            client.LastSeen = now;
                            if (SkillRequests.Count < 64) SkillRequests.Enqueue((client.Slot, kind));
                        }
                        break;
                    case MsgType.Bye:
                        if (client != null && NetCodec.ReadByeToken(reader) == client.Token)
                        {
                            Remove(client);
                        }
                        break;
                    case MsgType.Discover:
                    {
                        if (!AllowUnsolicitedReply(now)) break;
                        NetCodec.WriteAnnounce(m_Writer, new AnnounceMessage
                        {
                            HostName = HostName,
                            Port = m_Port,
                            Transport = m_Kind,
                            Players = (byte)(m_Clients.Count + 1),
                            State = State,
                        });
                        via.Send(m_Writer.Buffer, m_Writer.Length, packet.From);
                        break;
                    }
                }
            }
            catch (Exception)
            {
                // 壊れたパケット・想定外の内容は捨てる。相手の送るものでホストを落とさない。
            }
        }

        private byte NextFreeSlot()
        {
            for (byte s = 1; s < MaxSlots; s++)
            {
                bool used = false;
                foreach (Client c in m_Clients)
                {
                    if (c.Slot == s) { used = true; break; }
                }
                if (!used) return s;
            }
            return (byte)(MaxSlots - 1);
        }

        private Client FindClient(NetEndpoint endpoint)
        {
            if (endpoint == null) return null;
            foreach (Client c in m_Clients)
            {
                if (c.Endpoint.Equals(endpoint)) return c;
            }
            return null;
        }

        private void Remove(Client client)
        {
            if (client == null) return;
            m_Clients.Remove(client);
            Left?.Invoke(client);
            BroadcastLobby();
        }

        private void SendWelcome(Client client)
        {
            NetCodec.WriteWelcome(m_Writer, client.Slot, client.Token);
            m_Transport.Send(m_Writer.Buffer, m_Writer.Length, client.Endpoint);
        }

        private void SendReject(NetEndpoint to, string reason)
        {
            NetCodec.WriteReject(m_Writer, reason);
            m_Transport.Send(m_Writer.Buffer, m_Writer.Length, to);
        }

        /// <summary>現在のロビー。スロット 0 はホスト、クライアントは参加順、CPU はその後ろ。</summary>
        public LobbyMessage BuildLobby()
        {
            var m = new LobbyMessage { State = State, Mode = Mode, MatchSeconds = MatchSeconds, Speed = Speed, BestOf = BestOf };
            m.Slots.Add(new LobbySlot { Slot = 0, Kind = SlotKind.Host, Name = HostName });
            foreach (Client c in m_Clients)
            {
                m.Slots.Add(new LobbySlot { Slot = c.Slot, Kind = SlotKind.Human, Name = c.Name });
            }
            int added = 0;
            for (byte s = 1; s < MaxSlots && added < CpuCount; s++)
            {
                bool used = false;
                foreach (LobbySlot ls in m.Slots) if (ls.Slot == s) { used = true; break; }
                if (used) continue;
                m.Slots.Add(new LobbySlot { Slot = s, Kind = SlotKind.Cpu, Name = $"CPU {s + 1}" });
                added++;
            }
            m.Slots.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            return m;
        }

        public void BroadcastLobby()
        {
            NetCodec.WriteLobby(m_Writer, BuildLobby());
            SendToAll(m_Writer.Buffer, m_Writer.Length);
        }

        public void BroadcastSnapshot(SnapshotMessage snapshot)
        {
            NetCodec.WriteSnapshot(m_Writer, snapshot);
            SendToAll(m_Writer.Buffer, m_Writer.Length);
        }

        private void SendToAll(byte[] data, int length)
        {
            foreach (Client c in m_Clients)
            {
                m_Transport.Send(data, length, c.Endpoint);
            }
        }

        /// <summary>そのスロットの最新の操作。<paramref name="age"/> は受信からの経過秒。</summary>
        public bool TryGetInput(byte slot, double now, out InputMessage input, out double age)
        {
            foreach (Client c in m_Clients)
            {
                if (c.Slot == slot && c.LastInputTime >= 0)
                {
                    input = c.LastInput;
                    age = now - c.LastInputTime;
                    return true;
                }
            }
            input = default;
            age = double.PositiveInfinity;
            return false;
        }

        public void Close()
        {
            State = LobbyState.Closed;
            NetCodec.WriteBye(m_Writer);
            SendToAll(m_Writer.Buffer, m_Writer.Length);
            NetCodec.WriteLobby(m_Writer, BuildLobby());
            SendToAll(m_Writer.Buffer, m_Writer.Length);
        }

        public void Dispose()
        {
            m_Transport.Dispose();
            if (m_Discovery != null && !ReferenceEquals(m_Discovery, m_Transport))
            {
                m_Discovery.Dispose();
            }
        }
    }
}
