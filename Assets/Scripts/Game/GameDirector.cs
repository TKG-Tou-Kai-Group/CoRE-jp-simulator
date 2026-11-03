using System.Collections.Generic;
using System.Net;
using CoreSim.Net;
using CoreSim.Rules;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CoreSim
{
    public enum NetRole
    {
        None,
        Host,
        Client,
    }

    /// <summary>
    /// ゲーム全体の進行役。フィールドを作り、メニュー → 試合 → 結果を回す。
    /// かんたんルールでは ROS 2 版の game_manager / hp_manager に相当する進行をそのまま回し、
    /// 大会ルールでは <see cref="TournamentController"/> がルールブックの判定を担う。
    /// ネット対戦ではホストがこの進行をそのまま回し、クライアントはスナップショットを写すだけ。
    /// </summary>
    public sealed class GameDirector : MonoBehaviour
    {
        public const double SnapshotInterval = 1.0 / 30.0;
        public const double InputInterval = 1.0 / 30.0;

        public static GameDirector Instance { get; private set; }

        public GameSettings Settings;
        public MatchState Match = new MatchState();
        public ProceduralAudio Audio;
        public float GoFlashUntil { get; private set; }
        public NetRole Role { get; private set; }
        public HostSession Host => m_Host;
        /// <summary>大会ルールのとき。かんたんルールでは null。</summary>
        public TournamentController Tournament { get; private set; }

        private readonly List<RobotAgent> m_Robots = new List<RobotAgent>();
        private readonly List<PlayerView> m_Views = new List<PlayerView>();
        private readonly List<PlayerCommandSource> m_PlayerSources = new List<PlayerCommandSource>();
        private readonly List<BotCommander> m_Commanders = new List<BotCommander>();
        private GameObject m_Arena;
        private Canvas m_Canvas;
        private MenuView m_Menu;
        private NetworkMenuView m_NetMenu;
        private Camera m_MenuCamera;
        private RectTransform m_Overlay;
        private bool m_Paused;
        private bool m_InMatch;
        private int m_LastBeepSecond = -1;

        // ---- ネット対戦 ----
        private HostSession m_Host;
        private ClientSession m_Client;
        private LanScanner m_Scanner;
        private double m_NextSnapshotTime;
        private double m_NextInputTime;
        private uint m_Tick;
        private byte m_MatchSerial;          // ホスト: 試合を始めるたびに +1 (スナップショットに載せる)
        private byte m_ClientSerial;         // 参加者: 今受けている試合の番号
        private bool m_ClientSerialKnown;
        private PlayerCommandSource m_ClientInput;
        private readonly Dictionary<int, GhostDisc> m_Ghosts = new Dictionary<int, GhostDisc>();
        private readonly Dictionary<int, int> m_LastShots = new Dictionary<int, int>();
        private readonly Dictionary<int, int> m_LastHits = new Dictionary<int, int>();
        private readonly HashSet<int> m_SeenDiscs = new HashSet<int>();
        private MatchPhase m_ClientPhase = MatchPhase.Idle;
        private bool m_ClientLostHandled;

        public IReadOnlyList<RobotAgent> Robots => m_Robots;
        public bool IsTeamMatch => Match.Mode == MatchMode.Team;
        public bool AcceptsDamage => m_InMatch && Match.Phase == MatchPhase.Playing;
        public bool RobotsMayMove => m_InMatch && Match.Phase == MatchPhase.Playing;
        public bool InMatch => m_InMatch;

        private static double Now => Time.realtimeSinceStartupAsDouble;

        private void Awake()
        {
            Instance = this;
            Settings = GameSettings.Load();
            Audio = gameObject.AddComponent<ProceduralAudio>();
            Audio.Enabled = Settings.Sound;

            var es = new GameObject("EventSystem");
            es.transform.SetParent(transform, false);
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();

            m_Canvas = UiKit.CreateCanvas("Canvas");
            m_Canvas.transform.SetParent(transform, false);

            m_Arena = ArenaBuilder.Build(lowDetail: Application.isMobilePlatform);
            ShowMenu();
        }

        private void OnDestroy()
        {
            CloseNetwork();
        }

        private void OnApplicationQuit()
        {
            CloseNetwork();
        }

        // ------------------------------------------------------------------ メニュー

        private void ShowMenu()
        {
            CloseNetwork();
            ClearMatch();
            m_InMatch = false;
            Match.Stop();
            Tournament = null;
            ArenaBuilder.Field2027?.Props.SetRemote(false);
            DestroyMenus();
            EnsureMenuCamera();
            m_Menu = MenuView.Create(m_Canvas.transform, Settings, StartMatch, OpenNetworkMenu, OpenSettings, () => Application.Quit());
        }

        private SettingsView m_Settings;

        public void OpenSettings()
        {
            DestroyMenus();
            m_Settings = SettingsView.Create(m_Canvas.transform, Settings, () => { Settings.Save(); ShowMenu(); }, OpenProgramEditor);
        }

        private ProgramView m_ProgramView;

        /// <summary>オートアタッカーのプログラムの編集画面を今の画面の上に出す (設定・ロビーから)。</summary>
        public void OpenProgramEditor()
        {
            if (m_ProgramView != null) return;
            m_ProgramView = ProgramView.Create(m_Canvas.transform, Settings, () =>
            {
                if (m_ProgramView != null) Destroy(m_ProgramView.gameObject);
                m_ProgramView = null;
                Settings.Save();
                SendProgramNow();   // 参加者なら編集した内容をホストへ
            });
        }

        private void EnsureMenuCamera()
        {
            if (m_MenuCamera != null)
            {
                return;
            }
            var camGo = new GameObject("MenuCamera");
            camGo.transform.SetParent(transform, false);
            m_MenuCamera = camGo.AddComponent<Camera>();
            m_MenuCamera.cullingMask = ~((1 << Tuning.LayerMinimapOnly) | (1 << 5));
            m_MenuCamera.fieldOfView = 50f;
            camGo.AddComponent<AudioListener>();
        }

        private void DestroyMenus()
        {
            if (m_Menu != null) { Destroy(m_Menu.gameObject); m_Menu = null; }
            if (m_NetMenu != null) { Destroy(m_NetMenu.gameObject); m_NetMenu = null; }
            if (m_Settings != null) { Destroy(m_Settings.gameObject); m_Settings = null; }
        }

        private void LeaveMenuForMatch()
        {
            DestroyMenus();
            if (m_Scanner != null)
            {
                m_Scanner.Dispose();   // 試合中まで部屋探しを続ける必要はない
                m_Scanner = null;
            }
            if (m_MenuCamera != null)
            {
                Destroy(m_MenuCamera.gameObject);
                m_MenuCamera = null;
            }
            Audio.Enabled = Settings.Sound;
        }

        // ------------------------------------------------------------------ ネット対戦の入口

        public void OpenNetworkMenu()
        {
            DestroyMenus();
            m_NetMenu = NetworkMenuView.Create(m_Canvas.transform, Settings, this);
            try
            {
                m_Scanner = new LanScanner(Settings.NetPort);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Net] LAN 検索を始められない: {e.Message}");
            }
        }

        public void CloseNetworkMenu()
        {
            ShowMenu();
        }

        public void HostGame()
        {
            CloseNetwork(keepMenu: true);
            try
            {
                var port = (ushort)Settings.NetPort;
                INetTransport main;
                UdpTransport discovery;
                if (Settings.NetUseTcp)
                {
                    main = new TcpHostTransport(port);
                    discovery = new UdpTransport(port);
                }
                else
                {
                    var udp = new UdpTransport(port);
                    main = udp;
                    discovery = udp;
                }
                m_Host = new HostSession(main, discovery, Settings.PlayerName, port,
                                         Settings.NetUseTcp ? TransportKind.Tcp : TransportKind.Udp)
                {
                    Password = Settings.NetPassword,
                    Mode = (byte)(Settings.Rules == Ruleset.Tournament ? 2 : 0),
                    MatchSeconds = (ushort)Settings.MatchSeconds,
                    Speed = (byte)(Settings.Speed == SpeedPreset.Game ? 1 : 0),
                    FireMs = (ushort)Mathf.RoundToInt(Settings.FireInterval * 1000f),
                    BestOf = (byte)Settings.BestOf,
                    CpuCount = 0,
                };
                Role = NetRole.Host;
                m_NetMenu.ShowLobby(true);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Net] ホストを始められない: {e}");
                m_NetMenu.ShowError($"部屋を作れません: {e.Message}");
                CloseNetwork(keepMenu: true);
            }
        }

        private bool m_ProgramSent;

        /// <summary>参加者: 編集したオートアタッカーのプログラムをホストへ送り直す。</summary>
        public void SendProgramNow()
        {
            if (m_Client != null && m_Client.State == ClientSession.Status.Connected) m_Client.SendProgram(Settings.AutoProgramText ?? "");
        }

        public void JoinGame(string address)
        {
            CloseNetwork(keepMenu: true);
            m_ProgramSent = false;
            // "host:port" 形式 (Secure Share Net の公開アドレスなど) ならポートはそちらを優先する
            int port = Settings.NetPort;
            string host = (address ?? string.Empty).Trim();
            NetAddress.SplitHostPort(ref host, ref port);
            IPAddress ip = NetAddress.ResolveHost(host);
            if (ip == null)
            {
                m_NetMenu.ShowError("IP アドレスが読めません");
                return;
            }
            try
            {
                var server = new IPEndPoint(ip, port);
                INetTransport transport;
                NetEndpoint endpoint;
                if (Settings.NetUseTcp)
                {
                    var tcp = new TcpClientTransport(server);
                    transport = tcp;
                    endpoint = tcp.Server;
                }
                else
                {
                    transport = new UdpTransport(0);
                    endpoint = new NetEndpoint(server);
                }
                m_Client = new ClientSession(transport, endpoint, Settings.PlayerName, Settings.NetPassword);
                m_ClientLostHandled = false;
                Role = NetRole.Client;
                m_NetMenu.ShowLobby(false);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Net] 参加できない: {e}");
                m_NetMenu.ShowError($"参加できません: {e.Message}");
                CloseNetwork(keepMenu: true);
            }
        }

        /// <summary>ロビーや試合から抜けてメニューへ戻る。</summary>
        public void LeaveNetwork()
        {
            ShowMenu();
        }

        private void CloseNetwork(bool keepMenu = false)
        {
            if (m_Host != null)
            {
                m_Host.Close();
                m_Host.Dispose();
                m_Host = null;
            }
            if (m_Client != null)
            {
                m_Client.Close();
                m_Client.Dispose();
                m_Client = null;
            }
            if (!keepMenu && m_Scanner != null)
            {
                m_Scanner.Dispose();
                m_Scanner = null;
            }
            Role = NetRole.None;
            m_ClientInput = null;
        }

        private void PumpNetwork()
        {
            double now = Now;
            if (m_Scanner != null && m_NetMenu != null)
            {
                m_Scanner.Pump(now);
                m_NetMenu.RefreshScan(m_Scanner.Hosts);
            }

            if (m_Host != null)
            {
                m_Host.Pump(now);
                if (m_NetMenu != null)
                {
                    m_NetMenu.RefreshLobby(m_Host.BuildLobby(), $"参加者 {m_Host.Clients.Count + 1} 人。2 人以上で開始できます");
                }
                if (m_InMatch)
                {
                    while (m_Host.SkillRequests.Count > 0)
                    {
                        (byte slot, byte kind) req = m_Host.SkillRequests.Dequeue();
                        RobotAgent agent = FindRobot(req.slot);
                        if (agent != null && kind_ok(req.kind))
                        {
                            ActivateSkillFor(agent, (SkillKind)req.kind);
                        }
                    }
                    if (now >= m_NextSnapshotTime)
                    {
                        m_NextSnapshotTime = now + SnapshotInterval;
                        m_Host.BroadcastSnapshot(BuildSnapshot());
                    }
                }
            }

            if (m_Client != null)
            {
                m_Client.Pump(now);
                if (m_Client.State == ClientSession.Status.Rejected || m_Client.State == ClientSession.Status.Disconnected)
                {
                    HandleClientLost(m_Client.RejectReason);
                    return;
                }
                if (!m_ProgramSent && m_Client.State == ClientSession.Status.Connected)
                {
                    // 参加したらオートアタッカーのプログラムをホストへ (編集したら SendProgramNow で送り直す)
                    m_ProgramSent = true;
                    m_Client.SendProgram(Settings.AutoProgramText ?? "");
                }
                if (m_NetMenu != null)
                {
                    string status = m_Client.State == ClientSession.Status.Connecting
                        ? "接続中…"
                        : $"接続済み (スロット {m_Client.Slot + 1}、応答 {m_Client.RttMs:F0} ms)。ホストの開始を待っています";
                    m_NetMenu.RefreshLobby(m_Client.Lobby, status);
                }
                if (!m_InMatch && m_Client.Lobby != null && m_Client.Lobby.State == LobbyState.Playing)
                {
                    StartClientMatch();
                }
                if (m_InMatch)
                {
                    if (m_Client.HasNewSnapshot)
                    {
                        SnapshotMessage s = m_Client.TakeSnapshot();
                        if (s != null)
                        {
                            if (m_ClientSerialKnown && s.Serial != m_ClientSerial)
                            {
                                // ホストが再戦を始めた: 結果画面と前の試合の機体を片付けて作り直す
                                StartClientMatch();
                            }
                            m_ClientSerial = s.Serial;
                            m_ClientSerialKnown = true;
                            ApplySnapshot(s);
                        }
                    }
                    if (m_ClientInput != null && now >= m_NextInputTime)
                    {
                        m_NextInputTime = now + InputInterval;
                        RobotCommand cmd = m_ClientInput.Read();
                        m_Client.SendInput(cmd.Move.x, cmd.Move.y, cmd.Turn, cmd.Fire, cmd.Turbo, cmd.Lift, cmd.Grab);
                    }
                    if (m_ClientInput != null && Tournament != null)
                    {
                        int skill = m_ClientInput.PollSkill();
                        if (skill >= 0)
                        {
                            m_Client.SendSkill((byte)skill);
                        }
                    }
                }
            }
        }

        private static bool kind_ok(byte kind) => kind < TournamentConfig.Skills.Length;

        private void HandleClientLost(string reason)
        {
            if (m_ClientLostHandled)
            {
                return;
            }
            m_ClientLostHandled = true;
            Debug.Log($"[Net] 切断: {reason}");
            if (m_InMatch)
            {
                CreateOverlay("メニューへ", ShowMenu, null, null, new Color(0f, 0f, 0f, 0.6f), 0f, reason);
            }
            else if (m_NetMenu != null)
            {
                CloseNetwork(keepMenu: true);
                Destroy(m_NetMenu.gameObject);
                m_NetMenu = NetworkMenuView.Create(m_Canvas.transform, Settings, this);
                m_NetMenu.ShowError(reason);
            }
        }

        // ------------------------------------------------------------------ 毎フレーム

        private void Update()
        {
            PumpNetwork();

            if (m_MenuCamera != null)
            {
                float a = Time.time * 0.08f;
                m_MenuCamera.transform.position = new Vector3(Mathf.Sin(a) * 22f, 9f, Mathf.Cos(a) * 22f);
                m_MenuCamera.transform.LookAt(new Vector3(0f, 0.5f, 0f));
                return;
            }
            if (!m_InMatch)
            {
                return;
            }

            bool pausePressed = false;
            foreach (PlayerCommandSource src in m_PlayerSources)
            {
                pausePressed |= src.PollPause();
            }
            if (pausePressed)
            {
                TogglePause();
            }
            if (m_Paused)
            {
                return;
            }

            if (Role == NetRole.Client)
            {
                foreach (PlayerView v in m_Views) v.Hud.Refresh(this);
                return;
            }

            MatchPhase before = Match.Phase;
            bool changed = Match.Tick(Time.deltaTime, CollectHp);
            if (Match.Phase == MatchPhase.Countdown)
            {
                int sec = Match.RemainingCeil;
                if (sec <= 3 && sec != m_LastBeepSecond)
                {
                    m_LastBeepSecond = sec;
                    Audio.PlayBeep();
                }
            }
            if (changed && before == MatchPhase.Countdown && Match.Phase == MatchPhase.Playing)
            {
                GoFlashUntil = Time.time + 1.2f;
                Audio.PlayGo();
            }

            if (Tournament != null)
            {
                if (Match.Phase == MatchPhase.Playing)
                {
                    Tournament.Tick(Time.deltaTime);
                    foreach (BotCommander c in m_Commanders) c.Think(Time.time, m_Robots);
                    foreach (RobotAgent agent in m_Robots)
                    {
                        if (!agent.IsPlayer) continue;
                        PlayerCommandSource src = PlayerInputFor(agent);
                        int skill = src != null ? src.PollSkill() : -1;
                        if (skill >= 0)
                        {
                            ActivateSkillFor(agent, (SkillKind)skill);
                        }
                    }
                    if (Tournament.Round.Finished)
                    {
                        Side? winner = Tournament.Round.Winner;
                        Tournament.Series.Record(winner);
                        Match.EndRound(new MatchOutcome(winner == Side.Red ? OutcomeKind.RedWin : winner == Side.Blue ? OutcomeKind.BlueWin : OutcomeKind.Draw));
                        changed = true;
                    }
                }
            }
            else if (Match.Phase == MatchPhase.Playing && !Match.Unlimited && OnlyOneSideLeft())
            {
                Match.Finish(CollectHp());
                changed = true;
            }
            if (changed && Match.Phase == MatchPhase.Result)
            {
                Audio.PlayEnd();
                ShowResultOverlay();
            }

            foreach (PlayerView v in m_Views)
            {
                v.Hud.Refresh(this);
            }
        }

        // ------------------------------------------------------------------ 試合 (ローカル / ホスト)

        private bool TournamentRequested => Role == NetRole.Host ? m_Host.Mode == 2 : Settings.Rules == Ruleset.Tournament;

        public void StartMatch()
        {
            ClearMatch();
            LeaveMenuForMatch();
            m_MatchSerial++;
            bool tournament = TournamentRequested;

            var slots = new List<(byte slot, SlotKind kind, string name, bool passive)>();
            int bestOf = Settings.BestOf;
            int buildersPerSide = 0;
            int autosPerSide = 0;
            if (Role == NetRole.Host)
            {
                LobbyMessage lobby = m_Host.BuildLobby();
                foreach (LobbySlot s in lobby.Slots)
                {
                    slots.Add((s.Slot, s.Kind, s.Name, false));
                }
                bestOf = m_Host.BestOf;
                buildersPerSide = tournament ? m_Host.Builders : 0;
                autosPerSide = tournament ? Mathf.Clamp(m_Host.Autos, 0, 1) : 0;
                Match = new MatchState
                {
                    Mode = m_Host.Mode == 1 ? MatchMode.Team : MatchMode.Individual,
                    MatchSeconds = m_Host.MatchSeconds,
                    CountdownSeconds = Tuning.PreCountdownSeconds,
                    ResultSeconds = Tuning.ResultSeconds,
                    Unlimited = tournament,
                };
                Settings.Speed = m_Host.Speed == 1 ? SpeedPreset.Game : SpeedPreset.Real;
                Settings.FireInterval = m_Host.FireMs / 1000f;
                m_Host.State = LobbyState.Playing;
            }
            else if (tournament)
            {
                // 偶数番 = 青、奇数番 = 赤。各同盟の最小番号がヘッドアタッカー、大きい番号からビルダー。
                int perSide = Mathf.Clamp(Settings.RobotsPerSide, 1, 4);
                int builders = Settings.EffectiveBuilders;
                int autos = Mathf.Clamp(Settings.AutosPerSide, 0, 1);
                if (Settings.Role == PlayerRole.Builder && builders == 0) { builders = 1; if (perSide < 2) perSide = 2; }
                if (Settings.Role == PlayerRole.AutoAttacker) autos = 1;
                int total = perSide + autos;   // オートアタッカーはアタッカー + ビルダーとは別枠 (10.6)
                int rank = Settings.Role switch
                {
                    PlayerRole.AutoAttacker => total - 1,                    // 同盟内で最後の番号 = オート
                    PlayerRole.Builder => perSide - 1,                       // その手前 = ビルダー
                    PlayerRole.Attacker => perSide - builders >= 2 ? 1 : 0,  // 2 台目 (アタッカーが 2 台以上いるとき)
                    _ => 0,
                };
                int playerSlot = (Settings.PlayerIsRed ? 1 : 0) + rank * 2;
                autosPerSide = autos;
                for (int i = 0; i < total * 2; i++)
                {
                    bool isPlayer = i == playerSlot;
                    string side = MatchState.TeamOf(i) == Team.Blue ? "青" : "赤";
                    slots.Add(((byte)i, isPlayer ? SlotKind.Host : SlotKind.Cpu, isPlayer ? "P1" : $"CPU {i + 1} ({side})", false));
                }
                buildersPerSide = builders;
                Match = new MatchState
                {
                    Mode = MatchMode.Team,
                    CountdownSeconds = Tuning.PreCountdownSeconds,
                    ResultSeconds = 15f,
                    Unlimited = true,
                };
            }
            else
            {
                Match = new MatchState
                {
                    Mode = Settings.Mode == GameMode.TeamVsCpu ? MatchMode.Team : MatchMode.Individual,
                    MatchSeconds = Settings.MatchSeconds,
                    CountdownSeconds = Settings.Mode == GameMode.Practice ? 3f : Tuning.PreCountdownSeconds,
                    ResultSeconds = Tuning.ResultSeconds,
                    Unlimited = Settings.Mode == GameMode.Practice,
                };
                switch (Settings.Mode)
                {
                    case GameMode.SoloVsCpu:
                        slots.Add((0, SlotKind.Host, "P1", false));
                        for (byte i = 0; i < Settings.CpuCount; i++) slots.Add(((byte)(i + 1), SlotKind.Cpu, $"CPU {i + 2}", false));
                        break;
                    case GameMode.TeamVsCpu:
                        slots.Add((0, SlotKind.Host, "P1", false));
                        slots.Add((1, SlotKind.Cpu, "CPU 2", false));
                        slots.Add((2, SlotKind.Cpu, "CPU 3", false));
                        slots.Add((3, SlotKind.Cpu, "CPU 4", false));
                        break;
                    case GameMode.LocalVersus:
                        slots.Add((0, SlotKind.Host, "P1", false));
                        slots.Add((1, SlotKind.Human, "P2", false));
                        break;
                    default:
                        slots.Add((0, SlotKind.Host, "P1", false));
                        slots.Add((1, SlotKind.Cpu, "的", true));
                        break;
                }
            }

            // 大会ルール: 各同盟の最小番号をヘッドアタッカー、大きい番号から buildersPerSide 台をビルダーにする
            var slotNumbers = new List<int>();
            foreach (var s in slots) slotNumbers.Add(s.slot);
            int blueHead = int.MaxValue, redHead = int.MaxValue;
            if (tournament)
            {
                foreach (var s in slots)
                {
                    if (MatchState.TeamOf(s.slot) == Team.Blue) blueHead = Mathf.Min(blueHead, s.slot);
                    else redHead = Mathf.Min(redHead, s.slot);
                }
            }

            if (tournament)
            {
                Tournament = new TournamentController { Field = ArenaBuilder.Field2027 };
                Tournament.OnEvent = OnTournamentEvent;
                Tournament.Config.MagazineCapacity = Mathf.Clamp(Settings.Magazine, 10, 200);
            }

            int localPlayers = 0;
            var sideCount = new Dictionary<Team, int> { { Team.Blue, 0 }, { Team.Red, 0 } };
            foreach (var slot in slots)
            {
                Team team = MatchState.TeamOf(slot.slot);
                Pose pose = ArenaBuilder.Field2027 != null
                    ? ArenaBuilder.Field2027.Spawn(team == Team.Red ? "red" : "blue", sideCount[team]++)
                    : Tuning.StartPoses[slot.slot];
                RobotKind kind = tournament
                    ? KindForSlot(slot.slot, slotNumbers, buildersPerSide, autosPerSide)
                    : (slot.kind == SlotKind.Host && Settings.Role == PlayerRole.Builder ? RobotKind.Builder
                       : slot.kind == SlotKind.Host && Settings.Role == PlayerRole.AutoAttacker ? RobotKind.AutoAttacker : RobotKind.Attacker);
                RobotAgent agent = RobotFactory.Create(slot.slot, pose, Settings, kind);
                if (agent.Lift != null) agent.Lift.Registry = ArenaBuilder.Field2027?.Props;
                agent.DisplayName = slot.name;
                agent.CanFire = !slot.passive;
                if (tournament && (slot.slot == blueHead || slot.slot == redHead))
                {
                    agent.MarkAsHead();
                }
                if (agent.IsAuto)
                {
                    // オートアタッカー: 人のものでもプログラムが操縦する。人は始動・停止・スキルと観戦だけ
                    string text = slot.kind == SlotKind.Host ? Settings.AutoProgramText
                                : slot.kind == SlotKind.Human && Role == NetRole.Host ? m_Host.ProgramOf(slot.slot) : "";
                    AutoProgram program = string.IsNullOrWhiteSpace(text) ? AutoProgram.Default() : AutoProgram.Parse(text);
                    if (program.Rules.Count == 0) program = AutoProgram.Default();
                    var auto = new AutoAttackerSource { Self = agent, Robots = () => m_Robots, Tournament = Tournament, Program = program };
                    if (slot.kind == SlotKind.Host || (slot.kind == SlotKind.Human && Role != NetRole.Host))
                    {
                        agent.IsPlayer = true;
                        auto.Operator = NewLocalSource(localPlayers++);
                    }
                    agent.Source = auto;
                    agent.ResetToStart();
                    m_Robots.Add(agent);
                    continue;
                }
                switch (slot.kind)
                {
                    case SlotKind.Host:
                        agent.IsPlayer = true;
                        agent.Source = NewLocalSource(localPlayers++);
                        break;
                    case SlotKind.Human:
                        if (Role == NetRole.Host)
                        {
                            agent.Source = new RemoteCommandSource(m_Host, slot.slot);
                        }
                        else
                        {
                            agent.IsPlayer = true;
                            agent.Source = NewLocalSource(localPlayers++);
                        }
                        break;
                    default:
                        if (agent.IsBuilder)
                        {
                            agent.Source = new BotBuilderSource { Self = agent, Robots = () => m_Robots, Tournament = Tournament };
                        }
                        else
                        {
                            agent.Source = new BotCommandSource
                            {
                                Self = agent,
                                Robots = () => m_Robots,
                                Passive = slot.passive,
                                Skill = Settings.Mode == GameMode.Practice && Role == NetRole.None && !tournament ? 0.3f : 0.6f,
                                Tournament = Tournament,
                            };
                        }
                        break;
                }
                agent.ResetToStart();
                m_Robots.Add(agent);
            }
            m_Robots.Sort((a, b) => a.Index.CompareTo(b.Index));
            ArenaBuilder.Field2027?.Props.SetRemote(false);   // ブロック・コンテナを開始位置へ (ホスト / ローカルは物理あり)

            if (tournament)
            {
                Tournament.SetupLocal(m_Robots, bestOf);
                // 人がいない同盟は CPU の総大将がスキルを使う
                foreach (Side side in new[] { Side.Red, Side.Blue })
                {
                    bool human = false;
                    foreach (RobotAgent r in m_Robots)
                    {
                        // CPU はアタッカー (BotCommandSource) もビルダー (BotBuilderSource) も IBotStatus。ビルダーを
                        // 人と数えると、その同盟に総大将が付かず、弾切れの CPU にピットインが出なかった
                        if (TournamentController.SideOf(r.Team) == side && !(r.Source is IBotStatus)) human = true;
                    }
                    if (!human) m_Commanders.Add(new BotCommander { Side = side, Tournament = Tournament });
                }
                Tournament.BeginRound();
            }

            int players = 0;
            foreach (RobotAgent agent in m_Robots)
            {
                if (!agent.IsPlayer)
                {
                    continue;
                }
                bool split = Role == NetRole.None && !tournament && Settings.Mode == GameMode.LocalVersus;
                Rect rect = split ? new Rect(players * 0.5f, 0f, 0.5f, 1f) : new Rect(0f, 0f, 1f, 1f);
                PlayerView view = PlayerView.Create(agent, PlayerInputFor(agent), rect, players, Settings, m_Canvas.transform);
                view.transform.SetParent(transform, false);
                if (tournament)
                {
                    RobotAgent captured = agent;
                    view.Hud.AttachTournament(TournamentController.SideOf(agent.Team), kind => ActivateSkillFor(captured, kind));
                }
                view.Hud.Bind(agent, m_Robots);
                m_Views.Add(view);
                players++;
            }

            m_LastBeepSecond = -1;
            m_InMatch = true;
            m_Paused = false;
            m_Tick = 0;
            Time.timeScale = 1f;
            Match.Begin();
            foreach (PlayerView v in m_Views)
            {
                v.Hud.Refresh(this);
            }
        }

        /// <summary>大会ルールの次のラウンド。機体は開始位置に戻り、HP・RP・陣地は初期化される。</summary>
        private void StartNextRound()
        {
            if (Tournament == null) return;
            if (m_Overlay != null) { Destroy(m_Overlay.gameObject); m_Overlay = null; }
            foreach (Disc d in FindObjectsByType<Disc>(FindObjectsSortMode.None)) Destroy(d.gameObject);
            Disc.Live.Clear();
            Tournament.BeginRound();
            m_LastBeepSecond = -1;
            Match.Begin();
        }

        /// <summary>
        /// 大会ルールの種別の割り当て。同盟 (番号の偶奇) ごとに番号順に並べ、最初の 1 台がヘッドアタッカー、
        /// 最後の buildersPerSide 台がビルダー (アタッカーが 1 台は残るように丸める)。ホストとクライアントで同じ計算をする。
        /// </summary>
        public static RobotKind KindForSlot(int slot, IReadOnlyList<int> allSlots, int buildersPerSide, int autosPerSide = 0)
        {
            Team team = MatchState.TeamOf(slot);
            var mine = new List<int>();
            foreach (int s in allSlots) if (MatchState.TeamOf(s) == team) mine.Add(s);
            mine.Sort();
            int n = mine.Count;
            int autos = Mathf.Clamp(autosPerSide, 0, Mathf.Max(0, n - 1));          // 最後の番号 = オートアタッカー (1 台まで)
            int builders = Mathf.Clamp(buildersPerSide, 0, Mathf.Max(0, n - 1 - autos));
            int rank = mine.IndexOf(slot);
            if (rank < 0) return RobotKind.Attacker;
            if (rank >= n - autos) return RobotKind.AutoAttacker;
            if (rank >= n - autos - builders) return RobotKind.Builder;
            return RobotKind.Attacker;
        }

        private PlayerCommandSource NewLocalSource(int playerIndex)
        {
            var src = new PlayerCommandSource(playerIndex);
            m_PlayerSources.Add(src);
            return src;
        }

        public PlayerCommandSource PlayerInputFor(RobotAgent agent)
        {
            if (agent == null) return null;
            if (Role == NetRole.Client) return m_ClientInput;
            if (agent.Source is AutoAttackerSource auto) return auto.Operator;
            return agent.Source as PlayerCommandSource;
        }

        /// <summary>この機体の操縦者が総大将としてスキルを発動できるか (同盟に属する人なら誰でも)。</summary>
        public bool CanCommand(RobotAgent agent) => agent != null && agent.IsPlayer && Tournament != null;

        private void ActivateSkillFor(RobotAgent agent, SkillKind kind)
        {
            if (Tournament == null || Tournament.Remote || agent == null) return;
            if (Tournament.ActivateSkill(TournamentController.SideOf(agent.Team), kind, out string reason, agent.Index))
            {
                Audio.PlaySkill();
            }
            else
            {
                Audio.PlayClick();
            }
        }

        private void OnTournamentEvent(TournamentEvent e)
        {
            switch (e.Kind)
            {
                case TournamentEventKind.Destroyed: Audio.PlayBeep(); break;
                case TournamentEventKind.BaseDestroyed: Audio.PlayGo(); break;
                case TournamentEventKind.Foul: Audio.PlayFoul(); break;
                case TournamentEventKind.Revived:
                case TournamentEventKind.PitIn:
                case TournamentEventKind.RpBonus: Audio.PlayClick(); break;
            }
        }

        private void ClearMatch()
        {
            foreach (PlayerView v in m_Views)
            {
                if (v != null) Destroy(v.gameObject);
            }
            m_Views.Clear();
            foreach (RobotAgent r in m_Robots)
            {
                if (r != null) Destroy(r.gameObject);
            }
            m_Robots.Clear();
            m_PlayerSources.Clear();
            m_Commanders.Clear();
            foreach (Disc d in FindObjectsByType<Disc>(FindObjectsSortMode.None))
            {
                Destroy(d.gameObject);
            }
            Disc.Live.Clear();
            foreach (GhostDisc g in m_Ghosts.Values)
            {
                if (g != null) Destroy(g.gameObject);
            }
            m_Ghosts.Clear();
            m_SeenDiscs.Clear();
            m_LastShots.Clear();
            m_LastHits.Clear();
            m_ClientPhase = MatchPhase.Idle;
            if (m_Overlay != null)
            {
                Destroy(m_Overlay.gameObject);
                m_Overlay = null;
            }
            Time.timeScale = 1f;
            m_Paused = false;
            m_InMatch = false;
            Tournament = null;
        }

        private RobotAgent FindRobot(int slot)
        {
            foreach (RobotAgent r in m_Robots)
            {
                if (r.Index == slot) return r;
            }
            return null;
        }

        /// <summary>スロット番号を添字にした HP の配列。居ないスロットは 0。</summary>
        private int[] CollectHp()
        {
            int max = -1;
            foreach (RobotAgent r in m_Robots) max = Mathf.Max(max, r.Index);
            var hp = new int[max + 1];
            foreach (RobotAgent r in m_Robots) hp[r.Index] = r.Health.Hp;
            return hp;
        }

        private bool OnlyOneSideLeft()
        {
            if (IsTeamMatch)
            {
                bool blue = false, red = false;
                foreach (RobotAgent r in m_Robots)
                {
                    if (r.IsKnockedOut) continue;
                    if (r.Team == Team.Blue) blue = true; else red = true;
                }
                return !(blue && red);
            }
            int alive = 0;
            foreach (RobotAgent r in m_Robots)
            {
                if (!r.IsKnockedOut) alive++;
            }
            return alive <= 1;
        }

        /// <summary>結果画面の文言。自分が勝ったかどうかで言い換える。</summary>
        public string ResultText(RobotAgent self)
        {
            if (Tournament != null)
            {
                Side? mySide = self != null ? TournamentController.SideOf(self.Team) : (Side?)null;
                string text = Tournament.ResultText(mySide);
                TournamentSeries series = Tournament.Series;
                if (series != null && series.WinsNeeded > 1)
                {
                    text += $"\n赤 {series.RedWins} - {series.BlueWins} 青";
                    if (series.Decided) text += series.Champion == Side.Red ? "   赤同盟の試合勝利" : "   青同盟の試合勝利";
                }
                return text;
            }
            MatchOutcome o = Match.Outcome;
            switch (o.Kind)
            {
                case OutcomeKind.Draw:
                    return "引き分け";
                case OutcomeKind.BlueWin:
                    return self != null && self.Team == Team.Blue ? "勝利!  BLUE WIN" : "敗北…  BLUE WIN";
                case OutcomeKind.RedWin:
                    return self != null && self.Team == Team.Red ? "勝利!  RED WIN" : "敗北…  RED WIN";
                case OutcomeKind.RobotWin:
                    RobotAgent winner = FindRobot(o.RobotIndex);
                    string name = winner != null ? winner.DisplayName : $"Robot {o.RobotIndex + 1}";
                    return winner == self ? "勝利!" : $"{name} の勝ち";
                default:
                    return "";
            }
        }

        // ------------------------------------------------------------------ 試合 (ホスト → スナップショット)

        private SnapshotMessage BuildSnapshot()
        {
            var s = new SnapshotMessage
            {
                Tick = ++m_Tick,
                Serial = m_MatchSerial,
                Phase = (byte)Match.Phase,
                Remaining = Match.Remaining,
                OutcomeKind = (byte)Match.Outcome.Kind,
                OutcomeIndex = (sbyte)Match.Outcome.RobotIndex,
                Tournament = Tournament != null,
            };
            foreach (RobotAgent r in m_Robots)
            {
                Vector3 p = r.transform.position;
                Quaternion q = r.transform.rotation;
                var rs = new RobotState
                {
                    Slot = (byte)r.Index,
                    Px = p.x, Py = p.y, Pz = p.z,
                    Qx = q.x, Qy = q.y, Qz = q.z, Qw = q.w,
                    Hp = (ushort)Mathf.Clamp(r.Health.Hp, 0, ushort.MaxValue),
                    Ko = r.IsKnockedOut,
                    Turbo = r.IsTurbo,
                    Shots = (ushort)r.ShotCount,
                    Hits = (ushort)r.Health.Hits,
                    IsHead = r.IsHeadAttacker,
                    IsBuilder = r.IsBuilder,
                    IsAuto = r.IsAuto,
                    Lift = r.Lift != null ? r.Lift.Height : 0f,
                    Held = r.Lift != null && r.Lift.Held != null ? r.Lift.Held.Id : (ushort)0,
                };
                if (Tournament != null && Tournament.View.Robots.TryGetValue(r.Index, out RobotExtra x))
                {
                    rs.Magazine = (byte)Mathf.Clamp(x.Magazine, 0, 255);
                    rs.ReviveIn = x.ReviveIn;
                    rs.InvincibleFor = x.InvincibleFor;
                    rs.FrozenFor = x.FrozenFor;
                    rs.MaxHp = (ushort)x.MaxHp;
                    rs.PitInGranted = x.PitInGranted;
                    rs.AutoStarted = x.AutoStarted;
                    rs.AutoStopped = x.AutoStopped;
                }
                s.Robots.Add(rs);
            }
            foreach (Disc d in Disc.Live)
            {
                if (d == null || d.Body == null) continue;
                Vector3 p = d.transform.position;
                Vector3 v = d.Body.linearVelocity;
                s.Discs.Add(new DiscState { Id = d.Id, Mini = d.Mini, Px = p.x, Py = p.y, Pz = p.z, Vx = v.x, Vy = v.y, Vz = v.z });
                if (s.Discs.Count >= 255) break;
            }
            // ブロック・コンテナ: 動いたものだけ毎回、全部を 1 秒に 1 回 (落として止まったものを取りこぼさない)
            if (ArenaBuilder.Field2027 != null)
            {
                bool full = m_Tick % 30 == 0;
                foreach (FieldProp prop in ArenaBuilder.Field2027.Props.Props)
                {
                    if (prop == null) continue;
                    if (!full && !prop.MovedSince(0.002f) && prop.HeldBy == null) continue;
                    Vector3 p = prop.transform.position;
                    Quaternion q = prop.transform.rotation;
                    s.Props.Add(new PropState
                    {
                        Id = prop.Id, Held = prop.HeldBy != null, InSlot = prop.Slot != null,
                        Px = p.x, Py = p.y, Pz = p.z, Qx = q.x, Qy = q.y, Qz = q.z, Qw = q.w,
                    });
                    prop.MarkSent();
                    if (s.Props.Count >= 255) break;
                }
            }
            if (Tournament != null)
            {
                TournamentView v = Tournament.View;
                s.RoundTime = v.Time;
                s.RoundNumber = (byte)v.RoundNumber;
                s.RoundWinner = v.Winner;
                s.Red = v.Red;
                s.Blue = v.Blue;
            }
            return s;
        }

        // ------------------------------------------------------------------ 試合 (クライアント)

        private void StartClientMatch()
        {
            ClearMatch();
            LeaveMenuForMatch();
            LobbyMessage lobby = m_Client.Lobby;
            bool tournament = lobby.Mode == 2;
            Match = new MatchState
            {
                Mode = lobby.Mode >= 1 ? MatchMode.Team : MatchMode.Individual,
                MatchSeconds = lobby.MatchSeconds,
            };
            Settings.Speed = lobby.Speed == 1 ? SpeedPreset.Game : SpeedPreset.Real;
            Settings.FireInterval = lobby.FireMs / 1000f;
            if (tournament)
            {
                Tournament = new TournamentController { Field = ArenaBuilder.Field2027 };
                Tournament.SetupRemote(lobby.BestOf);
            }
            m_ClientInput = NewLocalSource(0);
            ArenaBuilder.Field2027?.Props.SetRemote(true);
            m_InMatch = true;
            m_Paused = false;
            m_ClientPhase = MatchPhase.Idle;
            m_ClientSerialKnown = false;
            Time.timeScale = 1f;
        }

        private string LobbyName(int slot)
        {
            LobbyMessage lobby = m_Client != null ? m_Client.Lobby : null;
            if (lobby != null)
            {
                foreach (LobbySlot s in lobby.Slots)
                {
                    if (s.Slot == slot) return s.Name;
                }
            }
            return $"Robot {slot + 1}";
        }

        private void ApplySnapshot(SnapshotMessage s)
        {
            if (s == null)
            {
                return;
            }
            var phase = (MatchPhase)s.Phase;
            Match.Apply(phase, s.Remaining, new MatchOutcome((OutcomeKind)s.OutcomeKind, s.OutcomeIndex));

            bool setChanged = false;
            foreach (RobotState rs in s.Robots)
            {
                RobotAgent agent = FindRobot(rs.Slot);
                if (agent == null)
                {
                    Team team = MatchState.TeamOf(rs.Slot);
                    Pose pose = Tuning.StartPoses[Mathf.Clamp(rs.Slot, 0, Tuning.StartPoses.Length - 1)];
                    agent = RobotFactory.Create(rs.Slot, pose, Settings, rs.IsBuilder ? RobotKind.Builder : rs.IsAuto ? RobotKind.AutoAttacker : RobotKind.Attacker);
                    if (agent.Lift != null) agent.Lift.Registry = ArenaBuilder.Field2027?.Props;
                    agent.DisplayName = LobbyName(rs.Slot);
                    agent.IsPlayer = rs.Slot == m_Client.Slot;
                    agent.MakeRemote();
                    agent.ResetToStart();
                    if (rs.IsHead) agent.MarkAsHead();
                    m_Robots.Add(agent);
                    m_Robots.Sort((a, b) => a.Index.CompareTo(b.Index));
                    Tournament?.RegisterAgent(agent);
                    setChanged = true;
                    if (agent.IsPlayer && m_Views.Count == 0)
                    {
                        PlayerView view = PlayerView.Create(agent, m_ClientInput, new Rect(0f, 0f, 1f, 1f), 0, Settings, m_Canvas.transform);
                        view.transform.SetParent(transform, false);
                        if (Tournament != null)
                        {
                            view.Hud.AttachTournament(TournamentController.SideOf(agent.Team), kind => m_Client?.SendSkill((byte)kind));
                        }
                        m_Views.Add(view);
                    }
                }
                agent.SetRemoteTarget(new Vector3(rs.Px, rs.Py, rs.Pz), new Quaternion(rs.Qx, rs.Qy, rs.Qz, rs.Qw));
                agent.ApplyRemoteState(rs.Hp, rs.Hits, rs.Ko, rs.Turbo);
                if (agent.Lift != null)
                {
                    FieldProp held = rs.Held != 0 && ArenaBuilder.Field2027 != null ? ArenaBuilder.Field2027.Props.Find(rs.Held) : null;
                    agent.ApplyRemoteLift(rs.Lift, held);
                }

                if (m_LastShots.TryGetValue(rs.Slot, out int shots) && rs.Shots > shots) Audio.PlayShot(agent.transform.position);
                if (m_LastHits.TryGetValue(rs.Slot, out int hits) && rs.Hits > hits) Audio.PlayHit(agent.transform.position);
                m_LastShots[rs.Slot] = rs.Shots;
                m_LastHits[rs.Slot] = rs.Hits;
            }
            if (setChanged)
            {
                foreach (PlayerView v in m_Views)
                {
                    v.Hud.Bind(v.Robot, m_Robots);
                }
            }
            if (Tournament != null && s.Tournament)
            {
                Tournament.ApplyRemote(s);
            }
            if (ArenaBuilder.Field2027 != null)
            {
                foreach (PropState ps in s.Props)
                {
                    FieldProp prop = ArenaBuilder.Field2027.Props.Find(ps.Id);
                    if (prop == null || (prop.HeldBy != null && ps.Held)) continue;   // 持たれている間はハンドに付いて動く
                    prop.ApplyRemote(new Vector3(ps.Px, ps.Py, ps.Pz), new Quaternion(ps.Qx, ps.Qy, ps.Qz, ps.Qw), ps.Held, ps.InSlot);
                }
            }

            m_SeenDiscs.Clear();
            foreach (DiscState ds in s.Discs)
            {
                m_SeenDiscs.Add(ds.Id);
                var pos = new Vector3(ds.Px, ds.Py, ds.Pz);
                var vel = new Vector3(ds.Vx, ds.Vy, ds.Vz);
                if (m_Ghosts.TryGetValue(ds.Id, out GhostDisc ghost) && ghost != null)
                {
                    ghost.Apply(pos, vel);
                }
                else
                {
                    m_Ghosts[ds.Id] = GhostDisc.Create(pos, vel, ds.Mini);
                }
            }
            var gone = new List<int>();
            foreach (var kv in m_Ghosts)
            {
                if (!m_SeenDiscs.Contains(kv.Key)) gone.Add(kv.Key);
            }
            foreach (int id in gone)
            {
                if (m_Ghosts[id] != null) Destroy(m_Ghosts[id].gameObject);
                m_Ghosts.Remove(id);
            }

            if (phase != m_ClientPhase)
            {
                if (phase == MatchPhase.Playing && m_ClientPhase == MatchPhase.Countdown)
                {
                    GoFlashUntil = Time.time + 1.2f;
                    Audio.PlayGo();
                }
                if (phase == MatchPhase.Result)
                {
                    Audio.PlayEnd();
                    CreateOverlay("退出してメニューへ", ShowMenu, null, null, new Color(0f, 0f, 0f, 0.25f), 260f);
                }
                if (phase == MatchPhase.Countdown && m_Overlay != null)
                {
                    Destroy(m_Overlay.gameObject);
                    m_Overlay = null;
                }
                m_ClientPhase = phase;
            }
            if (phase == MatchPhase.Countdown)
            {
                int sec = Match.RemainingCeil;
                if (sec <= 3 && sec != m_LastBeepSecond)
                {
                    m_LastBeepSecond = sec;
                    Audio.PlayBeep();
                }
            }
        }

        // ------------------------------------------------------------------ オーバーレイ

        private void ShowResultOverlay()
        {
            if (Tournament != null && Tournament.Series != null && !Tournament.Series.Decided)
            {
                CreateOverlay($"第 {Tournament.Series.RoundNumber + 1} ラウンドへ", StartNextRound, "メニューへ", ShowMenu, new Color(0f, 0f, 0f, 0.25f), 260f);
                return;
            }
            CreateOverlay(Tournament != null ? "新しい試合" : "もう一度", StartMatch, "メニューへ", ShowMenu, new Color(0f, 0f, 0f, 0.25f), 260f);
        }

        private void TogglePause()
        {
            if (m_Overlay != null && !m_Paused)
            {
                return;
            }
            m_Paused = !m_Paused;
            Time.timeScale = m_Paused && Role == NetRole.None ? 0f : 1f;
            Audio.PlayClick();
            if (m_Paused)
            {
                CreateOverlay("再開", TogglePause, Role == NetRole.None ? "メニューへ" : "退出してメニューへ", ShowMenu,
                              new Color(0f, 0f, 0f, 0.6f), 0f, Role == NetRole.None ? "一時停止" : "メニュー");
            }
            else if (m_Overlay != null)
            {
                Destroy(m_Overlay.gameObject);
                m_Overlay = null;
            }
        }

        private void CreateOverlay(string primary, System.Action onPrimary, string secondary, System.Action onSecondary,
                                   Color backdrop, float yOffset, string title = null)
        {
            if (m_Overlay != null)
            {
                Destroy(m_Overlay.gameObject);
            }
            m_Overlay = UiKit.Panel(m_Canvas.transform, "Overlay", backdrop, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            m_Overlay.GetComponent<Image>().raycastTarget = true;
            if (!string.IsNullOrEmpty(title))
            {
                RectTransform t = UiKit.Rect(m_Overlay, "Title", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 140f), new Vector2(1200f, 120f));
                UiKit.Label(t, "Text", title, 60, Color.white);
            }
            RectTransform row = UiKit.Rect(m_Overlay, "Buttons", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -yOffset), new Vector2(900f, 90f));
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 40f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            UiKit.Button(row, primary, new Vector2(380f, 80f), () => { Audio.PlayClick(); onPrimary(); }, new Color(0.85f, 0.35f, 0.2f, 0.95f), 34);
            if (!string.IsNullOrEmpty(secondary))
            {
                UiKit.Button(row, secondary, new Vector2(380f, 80f), () => { Audio.PlayClick(); onSecondary(); }, null, 34);
            }
        }
    }
}
