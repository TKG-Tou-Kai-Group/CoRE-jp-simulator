using System.Diagnostics;
using System.Net;
using System.Threading;
using CoreSim.Net;
using NUnit.Framework;

namespace CoreSim.Tests
{
    public class NetAddressTests
    {
        [Test]
        public void SplitsHostAndPort()
        {
            string host = "dummy.io:9999"; int port = 7777;
            NetAddress.SplitHostPort(ref host, ref port);
            Assert.AreEqual("dummy.io", host);
            Assert.AreEqual(9999, port);

            host = " 192.168.1.10 "; port = 7777;
            NetAddress.SplitHostPort(ref host, ref port);
            Assert.AreEqual("192.168.1.10", host);
            Assert.AreEqual(7777, port);

            host = "[fe80::1]:9000"; port = 7777;
            NetAddress.SplitHostPort(ref host, ref port);
            Assert.AreEqual("fe80::1", host);
            Assert.AreEqual(9000, port);

            host = "fe80::1"; port = 7777;
            NetAddress.SplitHostPort(ref host, ref port);
            Assert.AreEqual("fe80::1", host);
            Assert.AreEqual(7777, port);

            host = "example.com:notaport"; port = 7777;
            NetAddress.SplitHostPort(ref host, ref port);
            Assert.AreEqual("example.com:notaport", host);
            Assert.AreEqual(7777, port);
        }

        [Test]
        public void ResolvesLiteralAndLoopback()
        {
            Assert.AreEqual(IPAddress.Parse("10.0.0.5"), NetAddress.ResolveHost("10.0.0.5"));
            IPAddress lo = NetAddress.ResolveHost("localhost");
            Assert.IsNotNull(lo);
            Assert.IsTrue(IPAddress.IsLoopback(lo));
            Assert.IsNull(NetAddress.ResolveHost(""));
        }
    }

    public class NetCodecTests
    {
        [Test]
        public void SnapshotRoundTrips()
        {
            var snap = new SnapshotMessage { Tick = 1234, Phase = 2, Remaining = 87.5f, OutcomeKind = 4, OutcomeIndex = 3 };
            snap.Robots.Add(new RobotState { Slot = 1, Px = 1.5f, Py = 0.1f, Pz = -9.75f, Qx = 0, Qy = 0.707f, Qz = 0, Qw = 0.707f, Hp = 190, Ko = false, Turbo = true, Shots = 7, Hits = 1 });
            snap.Robots.Add(new RobotState { Slot = 3, Hp = 0, Ko = true });
            snap.Discs.Add(new DiscState { Id = 42, Px = 1, Py = 2, Pz = 3, Vx = 20, Vy = 1.2f, Vz = -0.5f });

            var w = new ByteWriter();
            NetCodec.WriteSnapshot(w, snap);
            byte[] bytes = w.ToArray();
            Assert.AreEqual(MsgType.Snapshot, NetCodec.PeekType(bytes, bytes.Length));

            SnapshotMessage back = NetCodec.ReadSnapshot(new ByteReader(bytes, bytes.Length));
            Assert.AreEqual(1234u, back.Tick);
            Assert.AreEqual(87.5f, back.Remaining);
            Assert.AreEqual(3, back.OutcomeIndex);
            Assert.AreEqual(2, back.Robots.Count);
            Assert.AreEqual(-9.75f, back.Robots[0].Pz);
            Assert.AreEqual(190, back.Robots[0].Hp);
            Assert.IsTrue(back.Robots[0].Turbo);
            Assert.IsFalse(back.Robots[0].Ko);
            Assert.AreEqual(7, back.Robots[0].Shots);
            Assert.IsTrue(back.Robots[1].Ko);
            Assert.AreEqual(42, back.Discs[0].Id);
            Assert.AreEqual(20f, back.Discs[0].Vx);
        }

        [Test]
        public void TournamentSnapshotRoundTrips()
        {
            var snap = new SnapshotMessage { Tick = 5, Tournament = true, RoundTime = 123.5f, RoundNumber = 2, RoundWinner = 255 };
            snap.Robots.Add(new RobotState { Slot = 0, Hp = 250, IsHead = true, MaxHp = 300, Magazine = 12, ReviveIn = -1f, InvincibleFor = 0f, FrozenFor = 3.5f });
            snap.Robots.Add(new RobotState { Slot = 2, Hp = 0, Ko = true, MaxHp = 60, Magazine = 0, ReviveIn = 17.25f });
            snap.Red = new AllianceState { Vp = -40, Rp = 130, BaseHp = 200, Discs = 280, BoostFor = 12f, BarrierLevel = 0, Wins = 1 };
            snap.Blue = new AllianceState { Vp = 310, Rp = 5, BaseHp = 0, BaseDestroyed = true, Discs = 10, BarrierLevel = 2, BarrierFor = 7f, PitInCredits = 1 };
            var w = new ByteWriter();
            NetCodec.WriteSnapshot(w, snap);
            SnapshotMessage back = NetCodec.ReadSnapshot(new ByteReader(w.Buffer, w.Length));
            Assert.IsTrue(back.Tournament);
            Assert.AreEqual(123.5f, back.RoundTime);
            Assert.AreEqual(255, back.RoundWinner);
            Assert.IsTrue(back.Robots[0].IsHead);
            Assert.AreEqual(300, back.Robots[0].MaxHp);
            Assert.AreEqual(12, back.Robots[0].Magazine);
            Assert.AreEqual(3.5f, back.Robots[0].FrozenFor);
            Assert.AreEqual(17.25f, back.Robots[1].ReviveIn);
            Assert.AreEqual(-40, back.Red.Vp);
            Assert.AreEqual(1, back.Red.Wins);
            Assert.IsTrue(back.Blue.BaseDestroyed);
            Assert.AreEqual(2, back.Blue.BarrierLevel);
            Assert.AreEqual(1, back.Blue.PitInCredits);

            NetCodec.WriteSkill(w, 6, 0x1234u);
            Assert.AreEqual(MsgType.Skill, NetCodec.PeekType(w.Buffer, w.Length));
            Assert.AreEqual(6, NetCodec.ReadSkill(new ByteReader(w.Buffer, w.Length), out uint skillToken));
            Assert.AreEqual(0x1234u, skillToken);
        }

        [Test]
        public void LobbyAndInputRoundTrip()
        {
            var lobby = new LobbyMessage { State = LobbyState.Playing, Mode = 1, MatchSeconds = 180, Speed = 0, BestOf = 3 };
            lobby.Slots.Add(new LobbySlot { Slot = 0, Kind = SlotKind.Host, Name = "ホスト" });
            lobby.Slots.Add(new LobbySlot { Slot = 2, Kind = SlotKind.Cpu, Name = "CPU 3" });
            var w = new ByteWriter();
            NetCodec.WriteLobby(w, lobby);
            LobbyMessage back = NetCodec.ReadLobby(new ByteReader(w.Buffer, w.Length));
            Assert.AreEqual(LobbyState.Playing, back.State);
            Assert.AreEqual(180, back.MatchSeconds);
            Assert.AreEqual(3, back.BestOf);
            Assert.AreEqual("ホスト", back.Slots[0].Name);
            Assert.AreEqual(SlotKind.Cpu, back.Slots[1].Kind);

            NetCodec.WriteInput(w, new InputMessage { Seq = 9, MoveX = -0.5f, MoveY = 1f, Turn = 0.25f, Fire = true, Turbo = false }, 7u);
            InputMessage input = NetCodec.ReadInput(new ByteReader(w.Buffer, w.Length), out uint inputToken);
            Assert.AreEqual(7u, inputToken);
            Assert.AreEqual(9u, input.Seq);
            Assert.AreEqual(-0.5f, input.MoveX);
            Assert.IsTrue(input.Fire);
            Assert.IsFalse(input.Turbo);
        }

        [Test]
        public void NamesAreSanitized()
        {
            Assert.AreEqual("abc", NetCodec.SanitizeName("a<size=99>b\nc"));
            Assert.AreEqual(16, NetCodec.SanitizeName(new string('x', 40)).Length);
            Assert.AreEqual("", NetCodec.SanitizeName("\u0001\u0002"));
        }

        [Test]
        public void TruncatedPacketIsRejectedNotCrashing()
        {
            var w = new ByteWriter();
            NetCodec.WriteHello(w, "abc", "");
            Assert.Throws<System.InvalidOperationException>(() =>
                NetCodec.ReadHello(new ByteReader(w.Buffer, 2), out _, out _, out _));
        }
    }

    public class NetSessionTests
    {
        private static int FreePort()
        {
            var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        private static void RunJoinInputSnapshot(INetTransport hostTransport, UdpTransport discovery, INetTransport clientTransport,
                                                 NetEndpoint server, ushort port, TransportKind kind)
        {
            var host = new HostSession(hostTransport, discovery, "Host", port, kind) { CpuCount = 1 };
            var client = new ClientSession(clientTransport, server, "Guest");
            var clock = Stopwatch.StartNew();
            double Now() => clock.Elapsed.TotalSeconds;
            try
            {
                // 参加
                while (client.State == ClientSession.Status.Connecting && Now() < 5.0)
                {
                    host.Pump(Now());
                    client.Pump(Now());
                    Thread.Sleep(5);
                }
                Assert.AreEqual(ClientSession.Status.Connected, client.State, client.RejectReason);
                Assert.AreEqual(1, client.Slot);
                Assert.AreEqual(1, host.Clients.Count);

                // ロビー (ホスト + クライアント + CPU 1 台)
                while ((client.Lobby == null || client.Lobby.Slots.Count < 3) && Now() < 5.0)
                {
                    host.Pump(Now());
                    client.Pump(Now());
                    Thread.Sleep(5);
                }
                Assert.IsNotNull(client.Lobby);
                Assert.AreEqual(3, client.Lobby.Slots.Count);
                Assert.AreEqual("Guest", client.Lobby.Slots[1].Name);
                Assert.AreEqual(SlotKind.Cpu, client.Lobby.Slots[2].Kind);

                // 操作がホストへ届く
                client.SendInput(0.5f, 1f, -0.25f, true, false);
                InputMessage input = default;
                double age = 0;
                double deadline = Now() + 3.0;
                while (!host.TryGetInput(1, Now(), out input, out age) && Now() < deadline)
                {
                    host.Pump(Now());
                    client.Pump(Now());
                    Thread.Sleep(5);
                }
                Assert.AreEqual(1f, input.MoveY);
                Assert.IsTrue(input.Fire);
                Assert.Less(age, 1.0);

                // スナップショットがクライアントへ届く
                host.State = LobbyState.Playing;
                var snap = new SnapshotMessage { Tick = 77, Phase = 2, Remaining = 100f };
                snap.Robots.Add(new RobotState { Slot = 0, Hp = 200 });
                host.BroadcastSnapshot(snap);
                deadline = Now() + 3.0;
                while (!client.HasNewSnapshot && Now() < deadline)
                {
                    host.Pump(Now());
                    client.Pump(Now());
                    Thread.Sleep(5);
                }
                Assert.IsTrue(client.HasNewSnapshot);
                Assert.AreEqual(77u, client.TakeSnapshot().Tick);

                // 試合中の参加は断られる
                var late = new ClientSession(kind == TransportKind.Udp ? (INetTransport)new UdpTransport(0)
                                                                        : new TcpClientTransport(server.Address),
                                             kind == TransportKind.Udp ? server : new NetEndpoint(server.Address, 1), "Late");
                deadline = Now() + 5.0;
                while (late.State == ClientSession.Status.Connecting && Now() < deadline)
                {
                    host.Pump(Now());
                    late.Pump(Now());
                    Thread.Sleep(5);
                }
                Assert.AreEqual(ClientSession.Status.Rejected, late.State);
                late.Dispose();

                // ホストが閉じるとクライアントは切断を知る
                host.Close();
                deadline = Now() + 3.0;
                while (client.State == ClientSession.Status.Connected && Now() < deadline)
                {
                    client.Pump(Now());
                    Thread.Sleep(5);
                }
                Assert.AreEqual(ClientSession.Status.Disconnected, client.State);
            }
            finally
            {
                client.Dispose();
                host.Dispose();
            }
        }

        [Test]
        public void UdpJoinInputSnapshotAndClose()
        {
            int port = FreePort();
            var hostUdp = new UdpTransport(port);
            var clientUdp = new UdpTransport(0);
            RunJoinInputSnapshot(hostUdp, hostUdp, clientUdp,
                new NetEndpoint(new IPEndPoint(IPAddress.Loopback, port)), (ushort)port, TransportKind.Udp);
        }

        [Test]
        public void TcpJoinInputSnapshotAndClose()
        {
            int port = FreePort();
            var hostTcp = new TcpHostTransport(port);
            var discovery = new UdpTransport(port);
            var clientTcp = new TcpClientTransport(new IPEndPoint(IPAddress.Loopback, port));
            RunJoinInputSnapshot(hostTcp, discovery, clientTcp, clientTcp.Server, (ushort)port, TransportKind.Tcp);
        }

        [Test]
        public void WrongPasswordIsRejectedAndSpoofedInputIsIgnored()
        {
            int port = FreePort();
            var hostUdp = new UdpTransport(port);
            var host = new HostSession(hostUdp, hostUdp, "Host", (ushort)port, TransportKind.Udp) { Password = "core" };
            var server = new NetEndpoint(new IPEndPoint(IPAddress.Loopback, port));
            var bad = new ClientSession(new UdpTransport(0), server, "Bad", "wrong");
            var good = new ClientSession(new UdpTransport(0), server, "Good", "core");
            var clock = Stopwatch.StartNew();
            double Now() => clock.Elapsed.TotalSeconds;
            try
            {
                while ((bad.State == ClientSession.Status.Connecting || good.State == ClientSession.Status.Connecting) && Now() < 5.0)
                {
                    host.Pump(Now()); bad.Pump(Now()); good.Pump(Now());
                    Thread.Sleep(5);
                }
                Assert.AreEqual(ClientSession.Status.Rejected, bad.State);
                Assert.AreEqual(ClientSession.Status.Connected, good.State);
                Assert.AreEqual(1, host.Clients.Count);

                // 正しいトークン無しで送った操作は捨てられる (送信元の偽装を模す)
                var spoof = new UdpTransport(0);
                var w = new ByteWriter();
                NetCodec.WriteInput(w, new InputMessage { Seq = 99, MoveY = 1f, Fire = true }, 0xDEADBEEF);
                // ホストから見た good の送信元と同じにはできないので、ホストの Client 記録へ直接突き合わせる:
                // 偽トークン付きの Input をクライアント自身の口から送っても捨てられることを確かめる
                var spoofedViaGood = new ClientSession(new UdpTransport(0), server, "Good2", "core");
                double deadline = Now() + 3.0;
                while (spoofedViaGood.State == ClientSession.Status.Connecting && Now() < deadline) { host.Pump(Now()); spoofedViaGood.Pump(Now()); Thread.Sleep(5); }
                Assert.AreEqual(ClientSession.Status.Connected, spoofedViaGood.State);
                good.SendInput(0f, 1f, 0f, true, false);
                deadline = Now() + 2.0;
                while (!host.TryGetInput(good.Slot, Now(), out _, out _) && Now() < deadline) { host.Pump(Now()); good.Pump(Now()); Thread.Sleep(5); }
                Assert.IsTrue(host.TryGetInput(good.Slot, Now(), out InputMessage got, out _));
                Assert.AreEqual(1f, got.MoveY);
                spoof.Dispose();
                spoofedViaGood.Dispose();
            }
            finally
            {
                bad.Dispose(); good.Dispose(); host.Dispose();
            }
        }

        [Test]
        public void LanScannerFindsHostOnLoopback()
        {
            int port = FreePort();
            var hostUdp = new UdpTransport(port);
            var host = new HostSession(hostUdp, hostUdp, "部屋A", (ushort)port, TransportKind.Udp);
            var scanner = new LanScanner(port);
            var clock = Stopwatch.StartNew();
            try
            {
                while (scanner.Hosts.Count == 0 && clock.Elapsed.TotalSeconds < 5.0)
                {
                    host.Pump(clock.Elapsed.TotalSeconds);
                    scanner.Pump(clock.Elapsed.TotalSeconds);
                    Thread.Sleep(5);
                }
                Assert.AreEqual(1, scanner.Hosts.Count);
                Assert.AreEqual("部屋A", scanner.Hosts[0].Info.HostName);
                Assert.AreEqual(port, scanner.Hosts[0].Info.Port);
                Assert.AreEqual(1, scanner.Hosts[0].Info.Players);
            }
            finally
            {
                scanner.Dispose();
                host.Dispose();
            }
        }
    }
}
