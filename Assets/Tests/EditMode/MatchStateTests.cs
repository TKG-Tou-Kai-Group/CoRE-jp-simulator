using CoreSim.Rules;
using NUnit.Framework;

namespace CoreSim.Tests
{
    public class MatchStateTests
    {
        [Test]
        public void TeamAssignmentAlternatesStartingWithBlue()
        {
            Assert.AreEqual(Team.Blue, MatchState.TeamOf(0));
            Assert.AreEqual(Team.Red, MatchState.TeamOf(1));
            Assert.AreEqual(Team.Blue, MatchState.TeamOf(6));
            Assert.AreEqual(Team.Red, MatchState.TeamOf(7));
        }

        [Test]
        public void IndividualWinnerIsTheRobotWithMostHp()
        {
            var outcome = MatchState.Decide(MatchMode.Individual, new[] { 100, 180, 50 });
            Assert.AreEqual(OutcomeKind.RobotWin, outcome.Kind);
            Assert.AreEqual(1, outcome.RobotIndex);
        }

        [Test]
        public void IndividualTieIsADraw()
        {
            var outcome = MatchState.Decide(MatchMode.Individual, new[] { 180, 180, 50 });
            Assert.AreEqual(OutcomeKind.Draw, outcome.Kind);
        }

        [Test]
        public void TeamWinnerIsDecidedBySumOfHp()
        {
            Assert.AreEqual(OutcomeKind.BlueWin, MatchState.Decide(MatchMode.Team, new[] { 200, 0, 50, 100 }).Kind);
            Assert.AreEqual(OutcomeKind.RedWin, MatchState.Decide(MatchMode.Team, new[] { 0, 10, 0, 0 }).Kind);
            Assert.AreEqual(OutcomeKind.Draw, MatchState.Decide(MatchMode.Team, new[] { 100, 100 }).Kind);
        }

        [Test]
        public void PhasesAdvanceCountdownPlayingResultIdle()
        {
            var state = new MatchState { CountdownSeconds = 3f, MatchSeconds = 5f, ResultSeconds = 2f };
            int[] Hp() => new[] { 200, 150 };

            state.Begin();
            Assert.AreEqual(MatchPhase.Countdown, state.Phase);
            Assert.AreEqual(3, state.RemainingCeil);

            Assert.IsFalse(state.Tick(1f, Hp));
            Assert.IsTrue(state.Tick(2.5f, Hp));
            Assert.AreEqual(MatchPhase.Playing, state.Phase);
            Assert.AreEqual(5, state.RemainingCeil);

            Assert.IsTrue(state.Tick(5f, Hp));
            Assert.AreEqual(MatchPhase.Result, state.Phase);
            Assert.AreEqual(OutcomeKind.RobotWin, state.Outcome.Kind);
            Assert.AreEqual(0, state.Outcome.RobotIndex);

            Assert.IsTrue(state.Tick(2f, Hp));
            Assert.AreEqual(MatchPhase.Idle, state.Phase);
        }

        [Test]
        public void UnlimitedMatchNeverEndsByTime()
        {
            var state = new MatchState { CountdownSeconds = 0.5f, MatchSeconds = 1f, Unlimited = true };
            state.Begin();
            state.Tick(1f, () => new[] { 1 });
            Assert.AreEqual(MatchPhase.Playing, state.Phase);
            state.Tick(1000f, () => new[] { 1 });
            Assert.AreEqual(MatchPhase.Playing, state.Phase);
        }

        [Test]
        public void FinishCanBeCalledEarly()
        {
            var state = new MatchState { Mode = MatchMode.Team };
            state.Begin();
            state.Tick(state.CountdownSeconds + 1f, () => new int[0]);
            state.Finish(new[] { 0, 200 });
            Assert.AreEqual(MatchPhase.Result, state.Phase);
            Assert.AreEqual(OutcomeKind.RedWin, state.Outcome.Kind);
        }
    }

    public class HealthModelTests
    {
        [Test]
        public void EachHitRemovesDamagePerHit()
        {
            var hp = new HealthModel(200, 10);
            Assert.AreEqual(10, hp.Hit());
            Assert.AreEqual(190, hp.Hp);
            Assert.AreEqual(30, hp.Hit(3));
            Assert.AreEqual(160, hp.Hp);
            Assert.AreEqual(4, hp.Hits);
        }

        [Test]
        public void HpNeverGoesBelowZeroAndDeadRobotsTakeNoDamage()
        {
            var hp = new HealthModel(25, 10);
            hp.Hit(5);
            Assert.AreEqual(0, hp.Hp);
            Assert.IsFalse(hp.IsAlive);
            Assert.AreEqual(0, hp.Hit());
            Assert.AreEqual(3, hp.Hits);
        }

        [Test]
        public void ResetRestoresFullHp()
        {
            var hp = new HealthModel(200, 10);
            hp.Hit(7);
            hp.Reset();
            Assert.AreEqual(200, hp.Hp);
            Assert.AreEqual(0, hp.Hits);
            Assert.AreEqual(1f, hp.Ratio);
        }
    }
}
