using System;

namespace CoreSim.Rules
{
    /// <summary>試合形式。CoRE-jp-Unity-ROS2-packages の game_manager の game_kind に対応する。</summary>
    public enum MatchMode
    {
        /// <summary>個人戦。残 HP が最も多い 1 台の勝ち、同点なら引き分け。</summary>
        Individual = 0,
        /// <summary>チーム戦。奇数番 (1,3,5,7) が Blue、偶数番 (2,4,6,8) が Red。HP の合計で決める。</summary>
        Team = 1,
    }

    public enum MatchPhase
    {
        Idle,
        /// <summary>試合開始前のカウントダウン。</summary>
        Countdown,
        Playing,
        /// <summary>勝敗を表示している時間。</summary>
        Result,
    }

    public enum Team
    {
        None,
        Blue,
        Red,
    }

    public enum OutcomeKind
    {
        None,
        Draw,
        BlueWin,
        RedWin,
        /// <summary>個人戦で 1 台が勝った。<see cref="MatchOutcome.RobotIndex"/> がその機体。</summary>
        RobotWin,
    }

    public readonly struct MatchOutcome
    {
        public readonly OutcomeKind Kind;
        /// <summary>0 始まりの機体番号 (RobotWin のときだけ有効)。</summary>
        public readonly int RobotIndex;

        public MatchOutcome(OutcomeKind kind, int robotIndex = -1)
        {
            Kind = kind;
            RobotIndex = robotIndex;
        }

        public override string ToString() =>
            Kind == OutcomeKind.RobotWin ? $"RobotWin({RobotIndex + 1})" : Kind.ToString();
    }

    /// <summary>
    /// 試合の進行 (カウントダウン → 試合 → 結果表示) と勝敗判定。
    /// Unity に依存しない純粋な C# なので、EditMode テストと dotnet の両方で検証できる。
    /// </summary>
    /// <remarks>
    /// 元になったのは CoRE-jp-Unity-ROS2-packages の game_manager/manager_node.py。
    /// そちらは 1 秒刻みの整数カウンタ (-10 → -1 で開始、initial_time → 0 で終了、
    /// その後 show_result_time) だったが、ここでは秒を浮動小数で数え、表示側で切り上げる。
    /// </remarks>
    public sealed class MatchState
    {
        public MatchMode Mode = MatchMode.Individual;
        public float MatchSeconds = 120f;
        public float CountdownSeconds = 10f;
        public float ResultSeconds = 10f;
        /// <summary>true なら試合時間を数えない (練習モード)。</summary>
        public bool Unlimited;

        public MatchPhase Phase { get; private set; } = MatchPhase.Idle;
        /// <summary>現在のフェーズの残り秒。</summary>
        public float Remaining { get; private set; }
        public MatchOutcome Outcome { get; private set; } = new MatchOutcome(OutcomeKind.None);

        /// <summary>表示用: 残り秒を切り上げた整数。</summary>
        public int RemainingCeil => (int)Math.Ceiling(Math.Max(0f, Remaining));

        public bool IsPlaying => Phase == MatchPhase.Playing;

        public void Begin()
        {
            Outcome = new MatchOutcome(OutcomeKind.None);
            Phase = MatchPhase.Countdown;
            Remaining = CountdownSeconds;
        }

        public void Stop()
        {
            Phase = MatchPhase.Idle;
            Remaining = 0f;
        }

        /// <summary>
        /// 時間を進める。試合終了時には <paramref name="hpProvider"/> から各機体の HP を取り、
        /// 勝敗を決める。戻り値はフェーズが変わったかどうか。
        /// </summary>
        public bool Tick(float dt, Func<int[]> hpProvider)
        {
            switch (Phase)
            {
                case MatchPhase.Countdown:
                    Remaining -= dt;
                    if (Remaining <= 0f)
                    {
                        Phase = MatchPhase.Playing;
                        Remaining = MatchSeconds;
                        return true;
                    }
                    return false;

                case MatchPhase.Playing:
                    if (Unlimited)
                    {
                        return false;
                    }
                    Remaining -= dt;
                    if (Remaining <= 0f)
                    {
                        Finish(hpProvider());
                        return true;
                    }
                    return false;

                case MatchPhase.Result:
                    Remaining -= dt;
                    if (Remaining <= 0f)
                    {
                        Phase = MatchPhase.Idle;
                        Remaining = 0f;
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>外部のルール (大会ルール) が決めた結果でラウンドを終える。</summary>
        public void EndRound(MatchOutcome outcome)
        {
            Outcome = outcome;
            Phase = MatchPhase.Result;
            Remaining = ResultSeconds;
        }

        /// <summary>ネット対戦のクライアントがホストから受けた状態をそのまま写す。</summary>
        public void Apply(MatchPhase phase, float remaining, MatchOutcome outcome)
        {
            Phase = phase;
            Remaining = remaining;
            Outcome = outcome;
        }

        /// <summary>試合を打ち切って勝敗を決める (全滅時などに呼ぶ)。</summary>
        public void Finish(int[] hp)
        {
            Outcome = Decide(Mode, hp);
            Phase = MatchPhase.Result;
            Remaining = ResultSeconds;
        }

        /// <summary>0 始まりの機体番号からチームを返す。奇数番 (1,3,5,7) が Blue。</summary>
        public static Team TeamOf(int robotIndex) => robotIndex % 2 == 0 ? Team.Blue : Team.Red;

        /// <summary>
        /// 勝敗判定。<paramref name="hp"/> は 0 始まりの機体番号順の残 HP。
        /// 参加していない機体は含めないこと (元の実装は -1 で埋めていたが、
        /// 個人戦の最大値探索には影響せず、チーム戦では合計を 1 下げるだけだった)。
        /// </summary>
        public static MatchOutcome Decide(MatchMode mode, int[] hp)
        {
            if (hp == null || hp.Length == 0)
            {
                return new MatchOutcome(OutcomeKind.Draw);
            }

            if (mode == MatchMode.Team)
            {
                int blue = 0, red = 0;
                for (int i = 0; i < hp.Length; i++)
                {
                    if (TeamOf(i) == Team.Blue) blue += hp[i];
                    else red += hp[i];
                }
                if (blue > red) return new MatchOutcome(OutcomeKind.BlueWin);
                if (red > blue) return new MatchOutcome(OutcomeKind.RedWin);
                return new MatchOutcome(OutcomeKind.Draw);
            }

            int max = int.MinValue, maxIndex = -1, maxCount = 0;
            for (int i = 0; i < hp.Length; i++)
            {
                if (hp[i] > max)
                {
                    max = hp[i];
                    maxIndex = i;
                    maxCount = 1;
                }
                else if (hp[i] == max)
                {
                    maxCount++;
                }
            }
            return maxCount > 1
                ? new MatchOutcome(OutcomeKind.Draw)
                : new MatchOutcome(OutcomeKind.RobotWin, maxIndex);
        }
    }
}
