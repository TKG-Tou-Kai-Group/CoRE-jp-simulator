using System.Collections.Generic;
using CoreSim.Net;
using UnityEngine;
using UnityEngine.UI;

namespace CoreSim
{
    /// <summary>ネット対戦の入口 (部屋を作る / 参加する) とロビー。</summary>
    public sealed class NetworkMenuView : MonoBehaviour
    {
        private GameDirector m_Director;
        private GameSettings m_Settings;

        private RectTransform m_Setup;
        private RectTransform m_LobbyPanel;
        private InputField m_NameField, m_PortField, m_AddressField, m_PasswordField;
        private Button m_TransportButton;
        private Text m_Error;
        private RectTransform m_ScanList;
        private readonly List<GameObject> m_ScanButtons = new List<GameObject>();
        private string m_LastScanKey = "";

        private Text m_LobbyTitle, m_LobbyList, m_LobbyStatus;
        private Button m_ModeButton, m_TimeButton, m_CpuButton, m_SpeedButton, m_FireButton, m_StartButton, m_BestOfButton, m_BuildersButton;
        private bool m_IsHost;

        public static NetworkMenuView Create(Transform parent, GameSettings settings, GameDirector director)
        {
            RectTransform root = UiKit.Panel(parent, "NetworkMenu", new Color(0.05f, 0.07f, 0.1f, 0.6f),
                                             Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var view = root.gameObject.AddComponent<NetworkMenuView>();
            view.m_Director = director;
            view.m_Settings = settings;
            view.BuildSetup(root);
            view.BuildLobby(root);
            view.m_LobbyPanel.gameObject.SetActive(false);
            return view;
        }

        // ------------------------------------------------------------------ 入口

        private void BuildSetup(RectTransform root)
        {
            m_Setup = UiKit.Panel(root, "Setup", new Color(0, 0, 0, 0), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            RectTransform title = UiKit.Rect(m_Setup, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(1400f, 100f));
            UiKit.Label(title, "Text", "ネット対戦", 72, Color.white);
            RectTransform note = UiKit.Rect(m_Setup, "Note", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(1500f, 60f));
            UiKit.Label(note, "Text",
                "通信はこのポート番号 1 つだけで行います。同じ LAN なら下の一覧に部屋が出ます。\n" +
                "閉域網ではホストの IP アドレスを入力してください。Secure Share Net なら公開アドレス (例: dummy.io:9999) をそのまま入力し、通信方式はツール側の設定 (TCP 推奨) に合わせます。",
                22, new Color(0.85f, 0.88f, 0.95f));

            // 左: 共通設定とホスト
            RectTransform left = UiKit.Rect(m_Setup, "Left", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(480f, -60f), new Vector2(860f, 700f));
            UiKit.Image(left, "bg", new Color(0f, 0f, 0f, 0.55f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var leftLayout = left.gameObject.AddComponent<VerticalLayoutGroup>();
            Layout(leftLayout);
            UiKit.Label(Row(left, 40f), "l1", "名前", 24, Color.white, TextAnchor.MiddleLeft);
            m_NameField = UiKit.InputField(left, "Name", new Vector2(800f, 56f), m_Settings.PlayerName, "名前");
            UiKit.Label(Row(left, 40f), "l2", "ポート番号 (UDP / TCP 共通)", 24, Color.white, TextAnchor.MiddleLeft);
            m_PortField = UiKit.InputField(left, "Port", new Vector2(800f, 56f), m_Settings.NetPort.ToString(), "7777", InputField.ContentType.IntegerNumber);
            UiKit.Label(Row(left, 40f), "l3", "合言葉 (任意。会場の共有ネットワークでは付けること)", 24, Color.white, TextAnchor.MiddleLeft);
            m_PasswordField = UiKit.InputField(left, "Password", new Vector2(800f, 56f), m_Settings.NetPassword, "合言葉");
            m_TransportButton = UiKit.Button(left, "", new Vector2(800f, 56f), () => { m_Settings.NetUseTcp = !m_Settings.NetUseTcp; RefreshTransportLabel(); }, fontSize: 26);
            RefreshTransportLabel();
            UiKit.Button(left, "部屋を作る (ホストになる)", new Vector2(800f, 76f), () => { ReadFields(); m_Director.HostGame(); },
                         new Color(0.85f, 0.35f, 0.2f, 0.95f), 32);
            m_Error = UiKit.Label(Row(left, 60f), "Error", "", 24, new Color(1f, 0.6f, 0.5f), TextAnchor.MiddleLeft);

            // 右: 参加
            RectTransform right = UiKit.Rect(m_Setup, "Right", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-480f, -60f), new Vector2(860f, 700f));
            UiKit.Image(right, "bg", new Color(0f, 0f, 0f, 0.55f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var rightLayout = right.gameObject.AddComponent<VerticalLayoutGroup>();
            Layout(rightLayout);
            UiKit.Label(Row(right, 40f), "r1", "ホストの IP アドレス (アドレス:ポート も可)", 24, Color.white, TextAnchor.MiddleLeft);
            m_AddressField = UiKit.InputField(right, "Address", new Vector2(800f, 56f), m_Settings.LastHostAddress, "例: 192.168.1.10 / dummy.io:9999");
            UiKit.Button(right, "参加する", new Vector2(800f, 76f), () => { ReadFields(); m_Director.JoinGame(m_Settings.LastHostAddress); },
                         new Color(0.2f, 0.5f, 0.85f, 0.95f), 32);
            UiKit.Label(Row(right, 40f), "r2", "同じ LAN 内の部屋 (自動検索)", 24, Color.white, TextAnchor.MiddleLeft);
            m_ScanList = Row(right, 230f);
            var scanLayout = m_ScanList.gameObject.AddComponent<VerticalLayoutGroup>();
            Layout(scanLayout);
            scanLayout.padding = new RectOffset(0, 0, 0, 0);
            scanLayout.spacing = 6f;

            RectTransform foot = UiKit.Rect(m_Setup, "Foot", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(400f, 64f));
            UiKit.Button(foot, "戻る", new Vector2(400f, 64f), () => m_Director.CloseNetworkMenu(), null, 30);
        }

        private static void Layout(VerticalLayoutGroup layout)
        {
            layout.padding = new RectOffset(30, 30, 20, 20);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
        }

        private static RectTransform Row(Transform parent, float height)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(800f, height);
            return rt;
        }

        private void RefreshTransportLabel()
        {
            UiKit.SetButtonLabel(m_TransportButton, m_Settings.NetUseTcp ? "通信方式: TCP (UDP が通らないとき)" : "通信方式: UDP (推奨)");
        }

        private void ReadFields()
        {
            m_Settings.PlayerName = string.IsNullOrWhiteSpace(m_NameField.text) ? "Player" : m_NameField.text.Trim();
            if (int.TryParse(m_PortField.text, out int port))
            {
                m_Settings.NetPort = Mathf.Clamp(port, 1024, 65535);
            }
            m_Settings.LastHostAddress = m_AddressField.text.Trim();
            m_Settings.NetPassword = m_PasswordField.text.Trim();
            m_Settings.Save();
        }

        public void ShowError(string message)
        {
            m_Error.text = message;
        }

        public void RefreshScan(IReadOnlyList<LanScanner.Found> hosts)
        {
            var key = new System.Text.StringBuilder();
            foreach (LanScanner.Found h in hosts)
            {
                key.Append(h.Address).Append(':').Append(h.Info.Port).Append(':').Append(h.Info.Players).Append(':').Append(h.Info.State).Append(';');
            }
            if (key.ToString() == m_LastScanKey)
            {
                return;
            }
            m_LastScanKey = key.ToString();
            foreach (GameObject go in m_ScanButtons) Destroy(go);
            m_ScanButtons.Clear();
            if (hosts.Count == 0)
            {
                Text t = UiKit.Label(Row(m_ScanList, 40f), "none", "(見つかりません)", 22, new Color(1f, 1f, 1f, 0.5f), TextAnchor.MiddleLeft);
                m_ScanButtons.Add(t.transform.parent.gameObject);
                return;
            }
            foreach (LanScanner.Found h in hosts)
            {
                LanScanner.Found found = h;
                string state = found.Info.State == LobbyState.Waiting ? "募集中" : "試合中";
                string label = $"{found.Info.HostName}  {found.Address}:{found.Info.Port}  {found.Info.Transport}  {found.Info.Players} 人  {state}";
                Button b = UiKit.Button(m_ScanList, label, new Vector2(800f, 48f), () =>
                {
                    m_AddressField.text = found.Address.ToString();
                    m_PortField.text = found.Info.Port.ToString();
                    m_Settings.NetUseTcp = found.Info.Transport == TransportKind.Tcp;
                    RefreshTransportLabel();
                    ReadFields();
                    m_Director.JoinGame(found.Address.ToString());
                }, new Color(0.2f, 0.3f, 0.45f, 0.95f), 22);
                m_ScanButtons.Add(b.gameObject);
            }
        }

        // ------------------------------------------------------------------ ロビー

        private void BuildLobby(RectTransform root)
        {
            m_LobbyPanel = UiKit.Panel(root, "Lobby", new Color(0, 0, 0, 0), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            RectTransform title = UiKit.Rect(m_LobbyPanel, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(1400f, 100f));
            m_LobbyTitle = UiKit.Label(title, "Text", "ロビー", 60, Color.white);

            RectTransform list = UiKit.Rect(m_LobbyPanel, "List", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(480f, -40f), new Vector2(860f, 560f));
            UiKit.Image(list, "bg", new Color(0f, 0f, 0f, 0.55f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            m_LobbyList = UiKit.Label(list, "Text", "", 28, Color.white, TextAnchor.UpperLeft, false);
            m_LobbyList.rectTransform.offsetMin = new Vector2(30f, 20f);
            m_LobbyList.rectTransform.offsetMax = new Vector2(-30f, -20f);
            m_LobbyList.lineSpacing = 1.3f;

            RectTransform opts = UiKit.Rect(m_LobbyPanel, "Options", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-480f, -40f), new Vector2(860f, 560f));
            UiKit.Image(opts, "bg", new Color(0f, 0f, 0f, 0.55f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var layout = opts.gameObject.AddComponent<VerticalLayoutGroup>();
            Layout(layout);
            Vector2 size = new Vector2(800f, 60f);
            m_ModeButton = UiKit.Button(opts, "", size, () => { if (m_Director.Host != null) { m_Director.Host.Mode = (byte)((m_Director.Host.Mode + 1) % 3); } }, fontSize: 26);
            m_BestOfButton = UiKit.Button(opts, "", size, () => { if (m_Director.Host != null) { m_Director.Host.BestOf = (byte)(m_Director.Host.BestOf switch { 1 => 3, 3 => 5, _ => 1 }); } }, fontSize: 26);
            m_BuildersButton = UiKit.Button(opts, "", size, () => { if (m_Director.Host != null) { m_Director.Host.Builders = (byte)((m_Director.Host.Builders + 1) % 3); } }, fontSize: 26);
            m_TimeButton = UiKit.Button(opts, "", size, () => { if (m_Director.Host != null) { m_Director.Host.MatchSeconds = (ushort)(m_Director.Host.MatchSeconds switch { 60 => 120, 120 => 180, _ => 60 }); } }, fontSize: 26);
            m_CpuButton = UiKit.Button(opts, "", size, () => { if (m_Director.Host != null) { m_Director.Host.CpuCount = (m_Director.Host.CpuCount + 1) % 4; } }, fontSize: 26);
            m_SpeedButton = UiKit.Button(opts, "", size, () =>
            {
                m_Settings.Speed = m_Settings.Speed == SpeedPreset.Game ? SpeedPreset.Real : SpeedPreset.Game;
                if (m_Director.Host != null) m_Director.Host.Speed = (byte)(m_Settings.Speed == SpeedPreset.Game ? 1 : 0);
            }, fontSize: 26);
            m_FireButton = UiKit.Button(opts, "", size, () =>
            {
                m_Settings.FireInterval = GameSettings.NextFireInterval(m_Settings.FireInterval);
                if (m_Director.Host != null) m_Director.Host.FireMs = (ushort)Mathf.RoundToInt(m_Settings.FireInterval * 1000f);
            }, fontSize: 26);
            m_LobbyStatus = UiKit.Label(Row(opts, 70f), "Status", "", 24, new Color(0.85f, 0.9f, 1f), TextAnchor.MiddleLeft);
            m_StartButton = UiKit.Button(opts, "試合開始", new Vector2(800f, 80f), () => m_Director.StartMatch(), new Color(0.85f, 0.35f, 0.2f, 0.95f), 36);
            UiKit.Button(opts, "退出", new Vector2(800f, 56f), () => m_Director.LeaveNetwork(), null, 26);
        }

        public void ShowLobby(bool isHost)
        {
            m_IsHost = isHost;
            m_Setup.gameObject.SetActive(false);
            m_LobbyPanel.gameObject.SetActive(true);
            m_StartButton.gameObject.SetActive(isHost);
            m_ModeButton.interactable = isHost;
            m_BestOfButton.interactable = isHost;
            m_BuildersButton.interactable = isHost;
            m_TimeButton.interactable = isHost;
            m_CpuButton.interactable = isHost;
            m_SpeedButton.interactable = isHost;
            m_FireButton.interactable = isHost;
            string kind = m_Settings.NetUseTcp ? "TCP" : "UDP";
            m_LobbyTitle.text = isHost ? $"ロビー (ホスト: {kind} ポート {m_Settings.NetPort})" : $"ロビー ({(m_Settings.LastHostAddress.Contains(":") ? m_Settings.LastHostAddress : m_Settings.LastHostAddress + ":" + m_Settings.NetPort)} {kind})";
        }

        public void RefreshLobby(LobbyMessage lobby, string status)
        {
            m_LobbyStatus.text = status;
            if (lobby == null)
            {
                m_LobbyList.text = "(ホストからの応答を待っています)";
                return;
            }
            var sb = new System.Text.StringBuilder();
            var numbers = new List<int>();
            foreach (LobbySlot s in lobby.Slots) numbers.Add(s.Slot);
            foreach (LobbySlot s in lobby.Slots)
            {
                string team = Rules.MatchState.TeamOf(s.Slot) == Rules.Team.Blue ? "Blue" : "Red";
                string kind = s.Kind == SlotKind.Host ? "ホスト" : s.Kind == SlotKind.Cpu ? "CPU" : "プレイヤー";
                string role = "";
                if (lobby.Mode == 2)
                {
                    bool builder = GameDirector.KindForSlot(s.Slot, numbers, lobby.Builders) == Rules.RobotKind.Builder;
                    role = builder ? "  ビルダー" : s.Slot <= 1 ? "  ★ヘッドアタッカー" : "  アタッカー";
                }
                sb.Append($"{s.Slot + 1}.  {s.Name}    [{team}]  {kind}{role}\n");
            }
            m_LobbyList.text = sb.ToString();
            UiKit.SetButtonLabel(m_ModeButton, lobby.Mode == 2 ? "形式: 大会ルール (CoRE-1 V27、偶数番 青 / 奇数番 赤)" : lobby.Mode == 1 ? "形式: チーム戦 (奇数番 Blue / 偶数番 Red)" : "形式: 個人戦 (全員が敵)");
            m_BestOfButton.gameObject.SetActive(lobby.Mode == 2);
            m_BuildersButton.gameObject.SetActive(lobby.Mode == 2);
            UiKit.SetButtonLabel(m_BuildersButton, $"各同盟のビルダー: {lobby.Builders} 台 (番号の大きい側から。アタッカーは 1 台以上)");
            m_TimeButton.gameObject.SetActive(lobby.Mode != 2);
            UiKit.SetButtonLabel(m_BestOfButton, lobby.BestOf switch { 3 => "試合: 準決勝形式 (3 ラウンド 2 本先取)", 5 => "試合: 決勝形式 (5 ラウンド 3 本先取)", _ => "試合: 予選形式 (1 ラウンド)" });
            UiKit.SetButtonLabel(m_TimeButton, $"試合時間: {lobby.MatchSeconds} 秒");
            int cpu = 0;
            foreach (LobbySlot s in lobby.Slots) if (s.Kind == SlotKind.Cpu) cpu++;
            UiKit.SetButtonLabel(m_CpuButton, $"CPU を追加: {cpu} 台");
            UiKit.SetButtonLabel(m_SpeedButton, lobby.Speed == 1 ? "機体の速さ: ゲーム (実機の 2 倍)" : "機体の速さ: リアル (実機相当)");
            UiKit.SetButtonLabel(m_FireButton, GameSettings.FireText(lobby.FireMs / 1000f));
            m_StartButton.interactable = m_IsHost && lobby.Slots.Count >= 2;
        }
    }
}
