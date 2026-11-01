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
        public int RobotsPerSide = 2;       // 1..4 (予選はアタッカー + ビルダーで 4 台)
        public int BestOf = 1;              // 1 / 3 / 5
        public bool PlayerIsHead = true;    // 自分をヘッドアタッカーにする
        public bool PlayerIsRed;            // false = 青同盟
        public int Magazine = 50;           // 弾倉の枚数。ルールには無い (機体設計次第)

        public float LinearScale => Speed == SpeedPreset.Game ? Tuning.GameSpeedScale : 1f;
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
            PlayerPrefs.SetInt("head", PlayerIsHead ? 1 : 0);
            PlayerPrefs.SetInt("red", PlayerIsRed ? 1 : 0);
            PlayerPrefs.SetInt("mag", Magazine);
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
            s.RobotsPerSide = Mathf.Clamp(PlayerPrefs.GetInt("perside", 2), 1, 4);
            s.BestOf = PlayerPrefs.GetInt("bestof", 1);
            s.PlayerIsHead = PlayerPrefs.GetInt("head", 1) != 0;
            s.PlayerIsRed = PlayerPrefs.GetInt("red", 0) != 0;
            s.Magazine = Mathf.Clamp(PlayerPrefs.GetInt("mag", 50), 10, 200);
            return s;
        }
    }
}
