using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CoreSim.Net
{
    /// <summary>
    /// LAN 内の部屋探し。対戦用と同じポート番号へ UDP ブロードキャストを投げ、
    /// ホストの Announce を集める。ルータを越えない (閉域網では IP を直接入力する)。
    /// </summary>
    public sealed class LanScanner : IDisposable
    {
        public sealed class Found
        {
            public IPAddress Address;
            public AnnounceMessage Info;
            public double LastSeen;
        }

        public const double DiscoverInterval = 1.0;
        public const double Expire = 4.0;

        private readonly UdpTransport m_Udp = new UdpTransport(0);
        private readonly int m_Port;
        private readonly ByteWriter m_Writer = new ByteWriter();
        private readonly List<Found> m_Found = new List<Found>();
        private readonly HashSet<IPAddress> m_LocalAddresses = new HashSet<IPAddress>();
        private double m_NextDiscover;

        public IReadOnlyList<Found> Hosts => m_Found;

        public LanScanner(int port)
        {
            m_Port = port;
            // 自分の PC で立てた部屋はブロードキャストとループバックの両方から答えが来て
            // 2 件に見えるので、自分のアドレスはすべて 127.0.0.1 にまとめる。
            m_LocalAddresses.Add(IPAddress.Loopback);
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    foreach (UnicastIPAddressInformation addr in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            m_LocalAddresses.Add(addr.Address);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 列挙できない環境では重複表示を許す
            }
        }

        public void Pump(double now)
        {
            if (now >= m_NextDiscover)
            {
                m_NextDiscover = now + DiscoverInterval;
                NetCodec.WriteDiscover(m_Writer);
                m_Udp.SendBroadcast(m_Writer.Buffer, m_Writer.Length, m_Port);
                // 同じ PC で立てたホストにも届くように、ループバックへも送る。
                m_Udp.Send(m_Writer.Buffer, m_Writer.Length, new NetEndpoint(new IPEndPoint(IPAddress.Loopback, m_Port)));
            }
            Received packet;
            while (m_Udp.TryReceive(out packet))
            {
                if (NetCodec.PeekType(packet.Data, packet.Length) != MsgType.Announce) continue;
                try
                {
                    AnnounceMessage info = NetCodec.ReadAnnounce(new ByteReader(packet.Data, packet.Length));
                    IPAddress address = m_LocalAddresses.Contains(packet.From.Address.Address)
                        ? IPAddress.Loopback : packet.From.Address.Address;
                    Found entry = null;
                    foreach (Found f in m_Found)
                    {
                        if (f.Address.Equals(address) && f.Info.Port == info.Port) { entry = f; break; }
                    }
                    if (entry == null)
                    {
                        entry = new Found { Address = address };
                        m_Found.Add(entry);
                    }
                    entry.Info = info;
                    entry.LastSeen = now;
                }
                catch (InvalidOperationException)
                {
                }
            }
            for (int i = m_Found.Count - 1; i >= 0; i--)
            {
                if (now - m_Found[i].LastSeen > Expire) m_Found.RemoveAt(i);
            }
        }

        public void Dispose() => m_Udp.Dispose();
    }
}
