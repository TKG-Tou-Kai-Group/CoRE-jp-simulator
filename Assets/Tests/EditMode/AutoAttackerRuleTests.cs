using CoreSim.Rules;
using NUnit.Framework;

namespace CoreSim.Tests
{
    public class AutoAttackerRuleTests
    {
        private static TournamentRound Make(out TournamentRobot auto, out TournamentRobot attacker)
        {
            var round = new TournamentRound();
            round.AddRobot(0, Side.Red, true);
            attacker = round.AddRobot(2, Side.Red, false);
            auto = round.AddRobot(4, Side.Red, false, RobotKind.AutoAttacker);
            round.AddRobot(1, Side.Blue, true);
            round.Begin();
            return round;
        }

        [Test]
        public void AutoAttackerHasHp200AndOwnMiniDiscs()
        {
            var round = Make(out var auto, out var attacker);
            Assert.AreEqual(200, auto.Hp);
            Assert.AreEqual(round.Config.MiniDiscsPerAlliance, auto.Magazine);
            Assert.AreEqual(400 - 2 * round.Config.MagazineCapacity, round.Red.Discs);   // ヘッドとアタッカーのぶんだけ減る
            Assert.Throws<System.ArgumentException>(() => new TournamentRound().AddRobot(6, Side.Red, true, RobotKind.AutoAttacker));
        }

        [Test]
        public void MovesAndFiresOnlyAfterStartAndNeverAfterStop()
        {
            var round = Make(out var auto, out var attacker);
            Assert.IsFalse(auto.AutoActive);
            Assert.IsFalse(round.TryFire(4));
            Assert.IsTrue(round.StartAuto(4, out _));
            Assert.IsTrue(auto.AutoActive);
            Assert.IsFalse(round.StartAuto(4, out string why));   // 二度目は無効
            Assert.IsTrue(round.TryFire(4));
            Assert.AreEqual(round.Config.MiniDiscsPerAlliance - 1, auto.Magazine);
            Assert.IsTrue(round.StopAuto(4, out _));
            Assert.IsFalse(auto.AutoActive);
            Assert.IsFalse(round.TryFire(4));
            Assert.IsFalse(round.StartAuto(4, out _));           // 停止後は再始動できない (12.7)
            Assert.IsFalse(round.StartAuto(2, out _));           // アタッカーには無関係
        }

        [Test]
        public void NoPitInAndSupplyAreaIsFoul()
        {
            var round = Make(out var auto, out var attacker);
            Assert.IsFalse(round.CanActivate(Side.Red, SkillKind.PitIn, out string reason, 4));
            StringAssert.Contains("オートアタッカー", reason);
            int vp = round.Red.Vp;
            int fouls = round.Red.Fouls;
            Assert.IsFalse(round.EnterSupplyArea(4));
            Assert.AreEqual(fouls + 1, round.Red.Fouls);
            Assert.IsTrue(round.CanActivate(Side.Red, SkillKind.PitIn, out _, 2));   // アタッカーは今までどおり
        }
    }
}
