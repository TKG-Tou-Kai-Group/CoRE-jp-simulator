using UnityEngine;

namespace CoreSim
{
    public enum GameMode
    {
        /// <summary>個人戦。プレイヤー 1 人 + CPU。</summary>
        SoloVsCpu,
        /// <summary>チーム戦。プレイヤー + CPU の味方 vs CPU 2 台。</summary>
        TeamVsCpu,
        /// <summary>画面分割のローカル対戦 (PC + ゲームパッド 2 台)。</summary>
        LocalVersus,
        /// <summary>時間無制限の練習。撃ち返してこない的が 1 台いる。</summary>
        Practice,
    }

    public enum Ruleset
    {
        /// <summary>HP 200 の撃ち合い (ROS 2 版と同じ簡易ルール)。</summary>
        Casual,
        /// <summary>CoRE-1 ルールブック V27.1.3 (2027) 準拠。アタッカーのみ。</summary>
        Tournament,
    }

    /// <summary>大会ルールで自分が操縦する機体。</summary>
    public enum PlayerRole
    {
        /// <summary>ヘッドアタッカー (HP 300、毎秒 5 回復、復活なし)。</summary>
        Head,
        /// <summary>アタッカー (HP 60、30 秒で復活)。</summary>
        Attacker,
        /// <summary>ビルダー (HP 80、射出なし、ブロックとコンテナを運ぶ)。</summary>
        Builder,
    }

    public enum SpeedPreset
    {
        /// <summary>実機相当 (0.5 m/s、Turbo 1.0 m/s)。</summary>
        Real,
        /// <summary>速度 2 倍・加速度 4 倍。</summary>
        Game,
    }

    /// <summary>メニューで選ぶ設定。PlayerPrefs に保存する。</summary>
    [System.Serializable]
    public sealed class GameSettings
    {
        public GameMode Mode = GameMode.SoloVsCpu;
        public int CpuCount = 1;            // SoloVsCpu の相手の数 (1..3)
        public int MatchSeconds = Tuning.DefaultMatchSeconds;
        public SpeedPreset Speed = SpeedPreset.Game;
        public bool ShowTouchControls = Application.isMobilePlatform;
        public bool Sound = true;
        public string PlayerName = "Player";
        public int NetPort = 7777;
        public bool NetUseTcp;              // false = UDP (既定)、true = TCP
        public string LastHostAddress = "";
        public string NetPassword = "";     // 部屋の合言葉 (空なら無し)
        // ---- 大会ルール ----
        public Ruleset Rules = Ruleset.Casual;
        public int RobotsPerSide = 3;       // 1..4 (予選はアタッカー + ビルダーで 4 台)
        public int BuildersPerSide = 1;     // 0..2 (RobotsPerSide のうちビルダーにする台数。アタッカーは 1 台以上)
        public int BestOf = 1;              // 1 / 3 / 5
        public PlayerRole Role = PlayerRole.Head;   // 自分の機体
        public bool PlayerIsRed;            // false = 青同盟
        public int Magazine = 50;           // 弾倉の枚数。ルールには無い (機体設計次第)
        public float FireInterval = Tuning.FireInterval;   // アタッカー全員の連射間隔 [s]。ゲームとして調整するための値

        public static readonly float[] FireIntervals = { 0.6f, 0.4f, 0.3f, 0.2f, 0.15f };
        public static float NextFireInterval(float current)
        {
            for (int i = 0; i < FireIntervals.Length; i++)
            {
                if (Mathf.Abs(FireIntervals[i] - current) < 0.01f) return FireIntervals[(i + 1) % FireIntervals.Length];
            }
            return FireIntervals[0];
        }
        public static string FireText(float interval)
        {
            string name = Mathf.Abs(interval - 0.6f) < 0.01f ? "実機相当" : interval >= 0.35f ? "やや速い" : interval >= 0.25f ? "速い" : interval >= 0.18f ? "とても速い" : "最速";
            return $"連射: {name} ({interval:0.##} 秒に 1 発、全アタッカー共通)";
        }

        public float LinearScale => Speed == SpeedPreset.Game ? Tuning.GameSpeedScale : 1f;
        /// <summary>互換用: 以前の「自分をヘッドアタッカーにする」。</summary>
        public bool PlayerIsHead => Role == PlayerRole.Head;
        /// <summary>実際のビルダーの数 (アタッカーが 1 台は残るように丸める)。</summary>
        public int EffectiveBuilders => Mathf.Clamp(BuildersPerSide, 0, Mathf.Max(0, RobotsPerSide - 1));
        public float AccelScale => Speed == SpeedPreset.Game ? Tuning.GameAccelScale : 1f;

        public void Save()
        {
            PlayerPrefs.SetInt("mode", (int)Mode);
            PlayerPrefs.SetInt("cpu", CpuCount);
            PlayerPrefs.SetInt("sec", MatchSeconds);
            PlayerPrefs.SetInt("speed", (int)Speed);
            PlayerPrefs.SetInt("touch", ShowTouchControls ? 1 : 0);
            PlayerPrefs.SetInt("sound", Sound ? 1 : 0);
            PlayerPrefs.SetString("name", PlayerName);
            PlayerPrefs.SetInt("port", NetPort);
            PlayerPrefs.SetInt("tcp", NetUseTcp ? 1 : 0);
            PlayerPrefs.SetString("host", LastHostAddress);
            PlayerPrefs.SetString("pass", NetPassword);
            PlayerPrefs.SetInt("rules", (int)Rules);
            PlayerPrefs.SetInt("perside", RobotsPerSide);
            PlayerPrefs.SetInt("bestof", BestOf);
            PlayerPrefs.SetInt("role", (int)Role);
            PlayerPrefs.SetInt("builders", BuildersPerSide);
            PlayerPrefs.SetInt("red", PlayerIsRed ? 1 : 0);
            PlayerPrefs.SetInt("mag", Magazine);
            PlayerPrefs.SetInt("fire", Mathf.RoundToInt(FireInterval * 1000f));
            PlayerPrefs.Save();
        }

        public static GameSettings Load()
        {
            var s = new GameSettings();
            s.Mode = (GameMode)PlayerPrefs.GetInt("mode", (int)s.Mode);
            s.CpuCount = Mathf.Clamp(PlayerPrefs.GetInt("cpu", s.CpuCount), 1, 3);
            s.MatchSeconds = PlayerPrefs.GetInt("sec", s.MatchSeconds);
            s.Speed = (SpeedPreset)PlayerPrefs.GetInt("speed", (int)s.Speed);
            s.ShowTouchControls = PlayerPrefs.GetInt("touch", s.ShowTouchControls ? 1 : 0) != 0;
            s.Sound = PlayerPrefs.GetInt("sound", 1) != 0;
            s.PlayerName = PlayerPrefs.GetString("name", s.PlayerName);
            s.NetPort = Mathf.Clamp(PlayerPrefs.GetInt("port", s.NetPort), 1024, 65535);
            s.NetUseTcp = PlayerPrefs.GetInt("tcp", 0) != 0;
            s.LastHostAddress = PlayerPrefs.GetString("host", "");
            s.NetPassword = PlayerPrefs.GetString("pass", "");
            s.Rules = (Ruleset)PlayerPrefs.GetInt("rules", 0);
            s.RobotsPerSide = Mathf.Clamp(PlayerPrefs.GetInt("perside", 3), 1, 4);
            s.BuildersPerSide = Mathf.Clamp(PlayerPrefs.GetInt("builders", 1), 0, 2);
            s.BestOf = PlayerPrefs.GetInt("bestof", 1);
            s.Role = (PlayerRole)Mathf.Clamp(PlayerPrefs.GetInt("role", PlayerPrefs.GetInt("head", 1) != 0 ? 0 : 1), 0, 2);
            s.PlayerIsRed = PlayerPrefs.GetInt("red", 0) != 0;
            s.Magazine = Mathf.Clamp(PlayerPrefs.GetInt("mag", 50), 10, 200);
            s.FireInterval = Mathf.Clamp(PlayerPrefs.GetInt("fire", 600) / 1000f, 0.1f, 2f);
            return s;
        }
    }
}
