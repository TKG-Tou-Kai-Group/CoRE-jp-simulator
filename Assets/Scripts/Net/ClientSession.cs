using System;

namespace CoreSim.Net
{
    /// <summary>クライアント側のセッション。参加 → ロビー → 試合 (スナップショット受信、操作送信)。</summary>
    public sealed class ClientSession : IDisposable
    {
        public enum Status
        {
            Connecting,
            Connected,
            Rejected,
            Disconnected,
        }

        public const double HelloInterval = 0.5;
        public const double PingInterval = 1.0;
        public const double ServerTimeout = 6.0;
        public const double ConnectTimeout = 10.0;

        public Status State { get; private set; } = Status.Connecting;
        public byte Slot { get; private set; }
        public string RejectReason { get; private set; } = "";
        public LobbyMessage Lobby { get; private set; }
        public SnapshotMessage LatestSnapshot { get; private set; }
        public bool HasNewSnapshot { get; private set; }
        public double LastReceived { get; private set; }
        public double RttMs { get; private set; }

        private readonly INetTransport m_Transport;
        private readonly NetEndpoint m_Server;
        private readonly string m_Name;
        private readonly string m_Password;
        private uint m_Token;
        private readonly ByteWriter m_Writer = new ByteWriter();
        private double m_Started = double.NaN;
        private double m_NextHello;
        private double m_NextPing;
        private uint m_InputSeq;

        public ClientSession(INetTransport transport, NetEndpoint server, string name, string password = "")
        {
            m_Transport = transport;
            m_Server = server;
            m_Name = NetCodec.SanitizeName(name);
            m_Password = password ?? "";
        }

        public void Pump(double now)
        {
            if (double.IsNaN(m_Started))
            {
                m_Started = now;
                LastReceived = now;
            }
            if (State == Status.Rejected || State == Status.Disconnected)
            {
                return;
            }

            if (State == Status.Connecting && now >= m_NextHello)
            {
                m_NextHello = now + HelloInterval;
                NetCodec.WriteHello(m_Writer, m_Name, m_Password);
                m_Transport.Send(m_Writer.Buffer, m_Writer.Length, m_Server);
            }
            if (State == Status.Connected && now >= m_NextPing)
            {
                m_NextPing = now + PingInterval;
                NetCodec.WritePing(m_Writer, (uint)(now * 1000.0), m_Token);
                m_Transport.Send(m_Writer.Buffer, m_Writer.Length, m_Server);
            }

            Received packet;
            while (m_Transport.TryReceive(out packet))
            {
                Handle(packet, now);
            }
            NetEndpoint gone;
            while (m_Transport.TryTakeDisconnected(out gone))
            {
                State = Status.Disconnected;
                RejectReason = "接続が切れました";
            }
            if (m_Transport is TcpClientTransport tcp && tcp.Failed)
            {
                State = Status.Disconnected;
                RejectReason = "接続できませんでした";
            }

            double timeout = State == Status.Connecting ? ConnectTimeout : ServerTimeout;
            if (now - LastReceived > timeout)
            {
                State = Status.Disconnected;
                RejectReason = State == Status.Connecting ? "応答がありません" : "ホストからの応答が途絶えました";
            }
        }

        private void Handle(Received packet, double now)
        {
            MsgType type = NetCodec.PeekType(packet.Data, packet.Length);
            var reader = new ByteReader(packet.Data, packet.Length);
            try
            {
                switch (type)
                {
                    case MsgType.Welcome:
                        Slot = NetCodec.ReadWelcome(reader, out m_Token);
                        if (State == Status.Connecting) State = Status.Connected;
                        LastReceived = now;
                        break;
                    case MsgType.Reject:
                        RejectReason = NetCodec.ReadReject(reader);
                        State = Status.Rejected;
                        break;
                    case MsgType.Lobby:
                        Lobby = NetCodec.ReadLobby(reader);
                        LastReceived = now;
                        if (Lobby.State == LobbyState.Closed)
                        {
                            State = Status.Disconnected;
                            RejectReason = "ホストが部屋を閉じました";
                        }
                        break;
                    case MsgType.Snapshot:
                    {
                        SnapshotMessage s = NetCodec.ReadSnapshot(reader);
                        if (LatestSnapshot == null || s.Tick >= LatestSnapshot.Tick)
                        {
                            LatestSnapshot = s;
                            HasNewSnapshot = true;
                        }
                        LastReceived = now;
                        break;
                    }
                    case MsgType.Pong:
                    {
                        uint stamp = NetCodec.ReadStamp(reader);
                        RttMs = Math.Max(0.0, now * 1000.0 - stamp);
                        LastReceived = now;
                        break;
                    }
                    case MsgType.Bye:
                        State = Status.Disconnected;
                        RejectReason = "ホストが退出しました";
                        break;
                }
            }
            catch (Exception)
            {
                // 壊れたパケット・想定外の内容は捨てる
            }
        }

        /// <summary>最新のスナップショットを取り出す (未読フラグを下ろす)。</summary>
        public SnapshotMessage TakeSnapshot()
        {
            HasNewSnapshot = false;
            return LatestSnapshot;
        }

        public void SendInput(float moveX, float moveY, float turn, bool fire, bool turbo)
        {
            if (State != Status.Connected) return;
            var m = new InputMessage { Seq = ++m_InputSeq, MoveX = moveX, MoveY = moveY, Turn = turn, Fire = fire, Turbo = turbo };
            NetCodec.WriteInput(m_Writer, m, m_Token);
            m_Transport.Send(m_Writer.Buffer, m_Writer.Length, m_Server);
        }

        public void SendSkill(byte kind)
        {
            if (State != Status.Connected) return;
            NetCodec.WriteSkill(m_Writer, kind, m_Token);
            m_Transport.Send(m_Writer.Buffer, m_Writer.Length, m_Server);
        }

        public void Close()
        {
            NetCodec.WriteBye(m_Writer, m_Token);
            m_Transport.Send(m_Writer.Buffer, m_Writer.Length, m_Server);
        }

        public void Dispose() => m_Transport.Dispose();
    }
}
