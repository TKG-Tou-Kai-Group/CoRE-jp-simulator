using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace CoreSim.Net
{
    /// <summary>通信相手。UDP ならアドレス、TCP なら接続番号で区別する。</summary>
    public sealed class NetEndpoint : IEquatable<NetEndpoint>
    {
        public readonly IPEndPoint Address;
        public readonly int ConnectionId;

        public NetEndpoint(IPEndPoint address, int connectionId = 0)
        {
            Address = address;
            ConnectionId = connectionId;
        }

        public bool Equals(NetEndpoint other) =>
            other != null && ConnectionId == other.ConnectionId && Equals(Address, other.Address);

        public override bool Equals(object obj) => Equals(obj as NetEndpoint);
        public override int GetHashCode() => (Address != null ? Address.GetHashCode() : 0) ^ (ConnectionId * 397);
        public override string ToString() => ConnectionId != 0 ? $"tcp#{ConnectionId} {Address}" : $"udp {Address}";
    }

    public struct Received
    {
        public byte[] Data;
        public int Length;
        public NetEndpoint From;
    }

    /// <summary>
    /// 送受信の口。受信は裏スレッドでキューに積み、<see cref="TryReceive"/> で主スレッドが取り出す。
    /// </summary>
    public interface INetTransport : IDisposable
    {
        bool TryReceive(out Received packet);
        void Send(byte[] data, int length, NetEndpoint to);
        /// <summary>切れた接続 (TCP のみ)。UDP では何も返さない。</summary>
        bool TryTakeDisconnected(out NetEndpoint endpoint);
        int LocalPort { get; }
    }

    /// <summary>UDP。1 ソケットで送受信とブロードキャストを行う。</summary>
    public sealed class UdpTransport : INetTransport
    {
        private readonly UdpClient m_Udp;
        private readonly ConcurrentQueue<Received> m_Queue = new ConcurrentQueue<Received>();
        private readonly Thread m_Thread;
        private volatile bool m_Closed;

        public int LocalPort => ((IPEndPoint)m_Udp.Client.LocalEndPoint).Port;

        /// <param name="bindPort">0 なら空いているポート。</param>
        public UdpTransport(int bindPort)
        {
            m_Udp = new UdpClient(AddressFamily.InterNetwork);
            m_Udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            m_Udp.EnableBroadcast = true;
            m_Udp.Client.Bind(new IPEndPoint(IPAddress.Any, bindPort));
            try
            {
                // Windows: 送り先が居ないときの ICMP で受信が例外になるのを止める (SIO_UDP_CONNRESET)。
                m_Udp.Client.IOControl(unchecked((IOControlCode)(-1744830452)), new byte[] { 0, 0, 0, 0 }, null);
            }
            catch (Exception)
            {
                // Linux / Android では無いオプション。
            }
            m_Thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "udp-recv" };
            m_Thread.Start();
        }

        private void ReceiveLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (!m_Closed)
            {
                try
                {
                    byte[] data = m_Udp.Receive(ref any);
                    m_Queue.Enqueue(new Received { Data = data, Length = data.Length, From = new NetEndpoint(new IPEndPoint(any.Address, any.Port)) });
                }
                catch (SocketException)
                {
                    if (m_Closed) return;
                    Thread.Sleep(5);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception)
                {
                    if (m_Closed) return;
                    Thread.Sleep(5);
                }
            }
        }

        public bool TryReceive(out Received packet) => m_Queue.TryDequeue(out packet);

        public void Send(byte[] data, int length, NetEndpoint to)
        {
            try
            {
                m_Udp.Send(data, length, to.Address);
            }
            catch (Exception)
            {
                // 相手が消えていても送信側は気にしない (タイムアウトで片付く)。
            }
        }

        public void SendBroadcast(byte[] data, int length, int port)
        {
            try
            {
                m_Udp.Send(data, length, new IPEndPoint(IPAddress.Broadcast, port));
            }
            catch (Exception)
            {
            }
        }

        public bool TryTakeDisconnected(out NetEndpoint endpoint)
        {
            endpoint = null;
            return false;
        }

        public void Dispose()
        {
            m_Closed = true;
            try { m_Udp.Close(); } catch (Exception) { }
        }
    }

    /// <summary>
    /// TCP の共通部分。メッセージは 2 バイト (リトルエンディアン) の長さ接頭辞でフレーム化する。
    /// UDP が通らない網 (TCP しか許可されないプロキシ越しなど) 用。
    /// </summary>
    internal static class TcpFraming
    {
        public static void WriteFrame(Stream s, byte[] data, int length)
        {
            var header = new byte[] { (byte)length, (byte)(length >> 8) };
            s.Write(header, 0, 2);
            s.Write(data, 0, length);
            s.Flush();
        }

        public static byte[] ReadFrame(Stream s)
        {
            var header = new byte[2];
            ReadExact(s, header, 2);
            int length = header[0] | (header[1] << 8);
            var data = new byte[length];
            ReadExact(s, data, length);
            return data;
        }

        private static void ReadExact(Stream s, byte[] buffer, int count)
        {
            int got = 0;
            while (got < count)
            {
                int n = s.Read(buffer, got, count - got);
                if (n <= 0) throw new IOException("connection closed");
                got += n;
            }
        }
    }

    /// <summary>TCP のホスト側。接続ごとに受信スレッドを持つ。</summary>
    public sealed class TcpHostTransport : INetTransport
    {
        /// <summary>同時接続の上限。参加できるのは 7 人なので、それ以上は接続を張るだけの相手。</summary>
        public const int MaxConnections = 16;
        /// <summary>最初のフレームが来るまでの猶予 [ms]。接続だけ張って何も送らない相手を残さない。</summary>
        public const int FirstFrameTimeoutMs = 15000;
        /// <summary>以後の受信の猶予 [ms]。参加者は 1 秒ごとに Ping を送るので、これを超えたら死んでいる。</summary>
        public const int IdleTimeoutMs = 30000;

        private sealed class Connection
        {
            public TcpClient Client;
            public NetworkStream Stream;
            public NetEndpoint Endpoint;
            public readonly object SendLock = new object();
        }

        private readonly TcpListener m_Listener;
        private readonly ConcurrentQueue<Received> m_Queue = new ConcurrentQueue<Received>();
        private readonly ConcurrentQueue<NetEndpoint> m_Disconnected = new ConcurrentQueue<NetEndpoint>();
        private readonly ConcurrentDictionary<int, Connection> m_Connections = new ConcurrentDictionary<int, Connection>();
        private int m_NextId;
        private volatile bool m_Closed;

        public int LocalPort => ((IPEndPoint)m_Listener.LocalEndpoint).Port;

        public TcpHostTransport(int port)
        {
            m_Listener = new TcpListener(IPAddress.Any, port);
            m_Listener.Start();
            new Thread(AcceptLoop) { IsBackground = true, Name = "tcp-accept" }.Start();
        }

        private void AcceptLoop()
        {
            while (!m_Closed)
            {
                TcpClient client;
                try
                {
                    client = m_Listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    if (m_Closed) return;
                    Thread.Sleep(10);
                    continue;
                }
                if (m_Connections.Count >= MaxConnections)
                {
                    try { client.Close(); } catch (Exception) { }
                    continue;
                }
                client.NoDelay = true;
                int id = Interlocked.Increment(ref m_NextId);
                var conn = new Connection
                {
                    Client = client,
                    Stream = client.GetStream(),
                    Endpoint = new NetEndpoint((IPEndPoint)client.Client.RemoteEndPoint, id),
                };
                m_Connections[id] = conn;
                new Thread(() => ReceiveLoop(conn)) { IsBackground = true, Name = $"tcp-recv-{id}" }.Start();
            }
        }

        private void ReceiveLoop(Connection conn)
        {
            try
            {
                conn.Stream.ReadTimeout = FirstFrameTimeoutMs;
                while (!m_Closed)
                {
                    byte[] data = TcpFraming.ReadFrame(conn.Stream);   // タイムアウトは IOException になり、接続を閉じる
                    conn.Stream.ReadTimeout = IdleTimeoutMs;
                    m_Queue.Enqueue(new Received { Data = data, Length = data.Length, From = conn.Endpoint });
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                m_Connections.TryRemove(conn.Endpoint.ConnectionId, out _);
                try { conn.Client.Close(); } catch (Exception) { }
                m_Disconnected.Enqueue(conn.Endpoint);
            }
        }

        public bool TryReceive(out Received packet) => m_Queue.TryDequeue(out packet);

        public void Send(byte[] data, int length, NetEndpoint to)
        {
            if (!m_Connections.TryGetValue(to.ConnectionId, out Connection conn))
            {
                return;
            }
            try
            {
                lock (conn.SendLock)
                {
                    TcpFraming.WriteFrame(conn.Stream, data, length);
                }
            }
            catch (Exception)
            {
                try { conn.Client.Close(); } catch (Exception) { }
            }
        }

        public bool TryTakeDisconnected(out NetEndpoint endpoint) => m_Disconnected.TryDequeue(out endpoint);

        public void Dispose()
        {
            m_Closed = true;
            try { m_Listener.Stop(); } catch (Exception) { }
            foreach (Connection c in m_Connections.Values)
            {
                try { c.Client.Close(); } catch (Exception) { }
            }
        }
    }

    /// <summary>TCP のクライアント側。接続は裏スレッドで行い、<see cref="Server"/> が出来たら送れる。</summary>
    public sealed class TcpClientTransport : INetTransport
    {
        private readonly TcpClient m_Client = new TcpClient(AddressFamily.InterNetwork);
        private readonly ConcurrentQueue<Received> m_Queue = new ConcurrentQueue<Received>();
        private readonly ConcurrentQueue<NetEndpoint> m_Disconnected = new ConcurrentQueue<NetEndpoint>();
        private readonly object m_SendLock = new object();
        private NetworkStream m_Stream;
        private volatile bool m_Closed;

        public NetEndpoint Server { get; }
        public volatile bool Connected;
        public volatile bool Failed;

        public int LocalPort => m_Client.Client.LocalEndPoint is IPEndPoint ep ? ep.Port : 0;

        public TcpClientTransport(IPEndPoint server)
        {
            Server = new NetEndpoint(server, 1);
            new Thread(() =>
            {
                try
                {
                    m_Client.NoDelay = true;
                    m_Client.Connect(server);
                    m_Stream = m_Client.GetStream();
                    Connected = true;
                    while (!m_Closed)
                    {
                        byte[] data = TcpFraming.ReadFrame(m_Stream);
                        m_Queue.Enqueue(new Received { Data = data, Length = data.Length, From = Server });
                    }
                }
                catch (Exception)
                {
                    if (!Connected) Failed = true;
                }
                finally
                {
                    Connected = false;
                    m_Disconnected.Enqueue(Server);
                }
            }) { IsBackground = true, Name = "tcp-client" }.Start();
        }

        public bool TryReceive(out Received packet) => m_Queue.TryDequeue(out packet);

        public void Send(byte[] data, int length, NetEndpoint to)
        {
            if (m_Stream == null || !Connected)
            {
                return;
            }
            try
            {
                lock (m_SendLock)
                {
                    TcpFraming.WriteFrame(m_Stream, data, length);
                }
            }
            catch (Exception)
            {
            }
        }

        public bool TryTakeDisconnected(out NetEndpoint endpoint) => m_Disconnected.TryDequeue(out endpoint);

        public void Dispose()
        {
            m_Closed = true;
            try { m_Client.Close(); } catch (Exception) { }
        }
    }
}
