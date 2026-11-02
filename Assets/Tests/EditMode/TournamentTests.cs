using CoreSim.Rules;
using NUnit.Framework;

namespace CoreSim.Tests
{
    public class TournamentTests
    {
        private static TournamentRound Make(out TournamentRobot redHead, out TournamentRobot red2, out TournamentRobot blueHead, out TournamentRobot blue2)
        {
            var round = new TournamentRound();
            redHead = round.AddRobot(0, Side.Red, true);
            red2 = round.AddRobot(2, Side.Red, false);
            blueHead = round.AddRobot(1, Side.Blue, true);
            blue2 = round.AddRobot(3, Side.Blue, false);
            round.Begin();
            return round;
        }

        [Test]
        public void BeginSetsHpRpAndMagazines()
        {
            var round = Make(out var rh, out var r2, out var bh, out var b2);
            Assert.AreEqual(300, rh.Hp);
            Assert.AreEqual(60, r2.Hp);
            Assert.AreEqual(100, round.Red.Rp);
            Assert.AreEqual(200, round.Red.BaseHp);
            int mag = round.Config.MagazineCapacity;
            Assert.AreEqual(mag, r2.Magazine);
            Assert.AreEqual(400 - 2 * mag, round.Red.Discs);
        }

        [Test]
        public void HitRemovesTenAndKillGivesTenVp()
        {
            var round = Make(out var rh, out var r2, out var bh, out var b2);
            for (int i = 0; i < 5; i++) Assert.AreEqual(10, round.Hit(1, 2));
            Assert.AreEqual(10, r2.Hp);
            Assert.IsFalse(r2.Destroyed);
            Assert.AreEqual(10, round.Hit(1, 2));
            Assert.IsTrue(r2.Destroyed);
            Assert.AreEqual(10, round.Blue.Vp);
            Assert.AreEqual(0, round.Hit(1, 2));           // 撃破中は当たらない
            Assert.AreEqual(60, round.Blue.DamageDealt);
        }

        [Test]
        public void FriendlyFireCountsButGivesNoAllianceDamage()
        {
            var round = Make(out var rh, out var r2, out var bh, out var b2);
            Assert.AreEqual(10, round.Hit(0, 2));
            Assert.AreEqual(50, r2.Hp);
            Assert.AreEqual(0, round.Red.DamageDealt);
        }

        [Test]
        public void AutoRevivalOnlyWithinThreeMinutes()
        {
            var round = Make(out var rh, out var r2, out var bh, out var b2);
            round.Tick(140f);
            for (int i = 0; i < 6; i++) round.Hit(1, 2);
            Assert.IsTrue(r2.Destroyed);
            Assert.AreEqual(170f, r2.ReviveAt, 0.01f);
            round.Tick(29f);
            Assert.IsTrue(r2.Destroyed);
            round.Tick(1.5f);
            Assert.IsFalse(r2.Destroyed);
            Assert.AreEqual(60, r2.Hp);
            Assert.IsTrue(r2.IsInvincible(round.Time));
            Assert.AreEqual(0, round.Hit(1, 2));           // 復活後 5 秒は無敵
            round.Tick(5.1f);
            Assert.AreEqual(10, round.Hit(1, 2));

            // 2 分 50 秒に撃破 → 30 秒後は 3 分 20 秒なので復活しない
            var round2 = Make(out _, out var r2b, out _, out _);
            round2.Tick(170f);
            for (int i = 0; i < 6; i++) round2.Hit(1, 2);
            Assert.AreEqual(-1f, r2b.ReviveAt);
            round2.Tick(100f);
            Assert.IsTrue(r2b.Destroyed);
        }

        [Test]
        public void BaseIsImmuneForThreeMinutesThenTakesDamageAndRp()
        {
            var round = Make(out var rh, out var r2, out var bh, out var b2);
            Assert.AreEqual(0, round.HitBase(1, Side.Red, false));
            round.Tick(181f);
            Assert.AreEqual(10, round.HitBase(1, Side.Red, false));
            Assert.AreEqual(190, round.Red.BaseHp);
            Assert.AreEqual(95, round.Red.Rp);
            Assert.AreEqual(20, round.HitBase(1, Side.Red, true));
            Assert.AreEqual(85, round.Red.Rp);
        }

        [Test]
        public void BaseDestroyedGivesVpRpStopsRegenAndAwardsHeadVp()
        {
            var round = Make(out var rh, out var r2, out var bh, out var b2);
            round.Tick(181f);
            round.Hit(1, 0);                               // 赤ヘッド 300 → 290
            round.Tick(0.5f);                              // 回復は 1 秒ごとなので、まだ 290 台
            for (int i = 0; i < 20; i++) round.HitBase(1, Side.Red, false);
            Assert.IsTrue(round.Red.BaseDestroyed);
            int headLoss = 300 - rh.Hp;
            Assert.AreEqual(100 + headLoss, round.Blue.Vp);
            Assert.AreEqual(200, round.Blue.Rp);
            Assert.AreEqual(headLoss, rh.HeadVpAwarded);
            // 回復が止まり、以後の減少は即 VP
            round.Tick(10f);
            Assert.AreEqual(300 - headLoss, rh.Hp);
            round.Hit(1, 0);
            Assert.AreEqual(100 + headLoss + 10, round.Blue.Vp);
        }

        [Test]
        public void HeadDestroyedBeforeBaseGivesThreeHundredAndNoKillVp()
        {
            var round = Make(out var rh, out var r2, out var bh, out var b2);
            for (int i = 0; i < 30; i++) round.Hit(1, 0);
            Assert.IsTrue(rh.Destroyed);
            Assert.AreEqual(300, round.Blue.Vp);
            Assert.AreEqual(-1f, rh.ReviveAt);
            round.Tick(181f);
            for (int i = 0; i < 20; i++) round.HitBase(1, Side.Red, false);
            Assert.AreEqual(400, round.Blue.Vp);          // 陣地の 100 だけ。ヘッド分は増えない
        }

        [Test]
        public void HeadRegeneratesFivePerSecond()
        {
            var round = Make(out var rh, out _, out _, out _);
            round.Hit(1, 0);
            round.Hit(1, 0);
            Assert.AreEqual(280, rh.Hp);
            round.Tick(2f);
            Assert.AreEqual(290, rh.Hp);
            round.Tick(10f);
            Assert.AreEqual(300, rh.Hp);
        }

        [Test]
        public void DamageMultipliersBoostBarrierBunker()
        {
            var round = Make(out var rh, out var r2, out var bh, out var b2);
            round.Blue.Rp = 500;
            round.Red.Rp = 500;
            Assert.IsTrue(round.ActivateSkill(Side.Blue, SkillKind.BoostLv1, out _));
            Assert.AreEqual(20, round.Hit(1, 2));
            Assert.IsTrue(round.ActivateSkill(Side.Red, SkillKind.BarrierLv1, out _));
            Assert.AreEqual(10, round.Hit(1, 2));         // 10 x 2 x 1/2
            r2.OnBunker = true;
            Assert.AreEqual(5, round.Hit(1, 2));          // 10 x 2 x 1/2 x 1/2
            Assert.AreEqual(20, round.Hit(1, 0));         // ヘッドにはバリアが効かない
            round.Red.BarrierLevel = 2;
            Assert.AreEqual(0, round.Hit(1, 2));          // 無敵
        }

        [Test]
        public void SkillsAreExclusiveAndLockForFiveSeconds()
        {
            var round = Make(out _, out var r2, out _, out _);
            round.Red.Rp = 1000;
            Assert.IsTrue(round.ActivateSkill(Side.Red, SkillKind.BoostLv1, out _));
            Assert.IsFalse(round.ActivateSkill(Side.Red, SkillKind.BarrierLv1, out string why));
            Assert.IsTrue(round.ActivateSkill(Side.Red, SkillKind.PitIn, out _, 2));    // ピットインは重ねられる
            Assert.IsTrue(round.ActivateSkill(Side.Red, SkillKind.Supply, out _));
            Assert.AreEqual(400 - 2 * round.Config.MagazineCapacity + 100, round.Red.Discs);
            round.Tick(20.5f);
            Assert.IsFalse(round.ActivateSkill(Side.Red, SkillKind.HealingLv1, out _));  // 終了後 5 秒はロック
            round.Tick(5f);
            round.Hit(1, 2);
            Assert.IsTrue(round.ActivateSkill(Side.Red, SkillKind.HealingLv1, out _));
            Assert.AreEqual(60, r2.Hp);
            Assert.AreEqual(1000 - 50 - 20 - 100 - 50, round.Red.Rp);
        }

        [Test]
        public void RegeneRevivesEarliestDestroyedNonHead()
        {
            var round = Make(out _, out var r2, out _, out _);
            round.Red.Rp = 200;
            Assert.IsFalse(round.ActivateSkill(Side.Red, SkillKind.Regene, out _));
            round.Tick(200f);
            for (int i = 0; i < 6; i++) round.Hit(1, 2);
            Assert.IsTrue(r2.Destroyed);
            Assert.AreEqual(-1f, r2.ReviveAt);
            Assert.IsTrue(round.ActivateSkill(Side.Red, SkillKind.Regene, out _));
            Assert.IsFalse(r2.Destroyed);
            Assert.AreEqual(60, r2.Hp);
        }

        [Test]
        public void PitInRefillsAndNoPitInIsAFoul()
        {
            var round = Make(out _, out var r2, out _, out _);
            int mag = round.Config.MagazineCapacity;
            int pool = 400 - 2 * mag;
            for (int i = 0; i < 12; i++) Assert.IsTrue(round.TryFire(2));
            Assert.AreEqual(mag - 12, r2.Magazine);
            Assert.IsFalse(round.EnterSupplyArea(2));
            Assert.AreEqual(-50, round.Red.Vp);
            Assert.IsFalse(round.ActivateSkill(Side.Red, SkillKind.PitIn, out _));       // 対象の指定が要る
            Assert.IsTrue(round.ActivateSkill(Side.Red, SkillKind.PitIn, out _, 2));
            Assert.IsFalse(round.ActivateSkill(Side.Red, SkillKind.PitIn, out _, 2));    // 同じ機体に二重には出せない
            Assert.AreEqual(1, round.Red.PitInCredits);
            Assert.IsFalse(round.EnterSupplyArea(0));                                   // 許可は機体 2 のもの
            Assert.AreEqual(-100, round.Red.Vp);
            Assert.IsTrue(round.EnterSupplyArea(2));
            Assert.AreEqual(0, round.Red.PitInCredits);
            Assert.AreEqual(mag, r2.Magazine);
            Assert.AreEqual(pool - 12, round.Red.Discs);
            Assert.IsTrue(r2.IsFrozen(round.Time));
            Assert.IsFalse(round.TryFire(2));
            round.Tick(20.5f);
            Assert.IsTrue(round.TryFire(2));
        }

        [Test]
        public void RoundEndsAtVp500OrSevenMinutesWithTiebreak()
        {
            var round = Make(out _, out _, out _, out _);
            round.Blue.Vp = 499;
            round.Tick(1f);
            Assert.IsFalse(round.Finished);
            round.Blue.Vp = 500;
            round.Tick(0.1f);
            Assert.IsTrue(round.Finished);
            Assert.AreEqual(Side.Blue, round.Winner);

            var r2 = Make(out _, out _, out _, out _);
            r2.Tick(300f);
            Assert.AreEqual(200, r2.Red.Rp);                // 5 分で +100
            r2.Hit(1, 2);
            r2.Tick(121f);
            Assert.IsTrue(r2.Finished);
            Assert.AreEqual(Side.Blue, r2.Winner);          // VP 同点、与ダメージで青
        }

        [Test]
        public void SeriesCountsWins()
        {
            var s = new TournamentSeries(3);
            s.Record(Side.Red);
            Assert.IsFalse(s.Decided);
            s.Record(null);
            s.Record(Side.Red);
            Assert.IsTrue(s.Decided);
            Assert.AreEqual(Side.Red, s.Champion);
            Assert.AreEqual(3, s.RoundNumber);
        }
    }
}

namespace CoreSim.Tests
{
    public class BuilderRulesTests
    {
        private static TournamentRound Make(out TournamentRobot redBuilder, out TournamentRobot blueAttacker)
        {
            var round = new TournamentRound();
            round.AddRobot(0, Side.Red, true);
            redBuilder = round.AddRobot(2, Side.Red, false, RobotKind.Builder);
            blueAttacker = round.AddRobot(1, Side.Blue, true);
            round.AddRobot(3, Side.Blue, false, RobotKind.Builder);
            round.Begin();
            return round;
        }

        [Test]
        public void BuilderHasEightyHpAndNoDiscs()
        {
            var round = Make(out var rb, out var ba);
            Assert.AreEqual(80, rb.Hp);
            Assert.AreEqual(0, rb.Magazine);
            Assert.AreEqual(400 - round.Config.MagazineCapacity, round.Red.Discs);   // ヘッドの分だけ
            Assert.IsFalse(round.TryFire(2));
            Assert.Throws<System.ArgumentException>(() => new TournamentRound().AddRobot(5, Side.Red, true, RobotKind.Builder));
        }

        [Test]
        public void SpotPlacementScoresOncePerSpotAndRowBonus()
        {
            var round = Make(out var rb, out var ba);
            Assert.IsTrue(round.PlaceContainerOnSpot(2, Side.Red, 1));
            Assert.AreEqual(10, round.Red.Vp);
            Assert.AreEqual(130, round.Red.Rp);
            Assert.IsTrue(round.PlaceContainerOnSpot(2, Side.Red, 1));   // 同じ番号は点にならない
            Assert.AreEqual(10, round.Red.Vp);
            Assert.AreEqual(2, round.Red.Containers);
            round.PlaceContainerOnSpot(2, Side.Red, 2);
            round.PlaceContainerOnSpot(2, Side.Red, 4);
            Assert.AreEqual(30, round.Red.Vp);
            round.PlaceContainerOnSpot(2, Side.Red, 3);                  // 1-2-3-4 で 4 個並び
            Assert.AreEqual(40 + 50, round.Red.Vp);
            Assert.AreEqual(100 + 4 * 30 + 100, round.Red.Rp);
            round.PlaceContainerOnSpot(2, Side.Red, 5);                  // 5 個目は段の得点なし
            Assert.AreEqual(100, round.Red.Vp);
            // 中段は列をまたいでもよい: 9,10,11,12
            foreach (int i in new[] { 9, 10, 11, 12 }) round.PlaceContainerOnSpot(2, Side.Red, i);
            Assert.AreEqual(100 + 40 + 50, round.Red.Vp);
        }

        [Test]
        public void EnemySpotIsFoulAndEnemyHolderIsFoul()
        {
            var round = Make(out var rb, out var ba);
            Assert.IsFalse(round.PlaceContainerOnSpot(2, Side.Blue, 1));
            Assert.AreEqual(-50, round.Red.Vp);
            Assert.AreEqual(1, round.Red.Fouls);
            Assert.IsNotNull(round.PickUpContainer(2, ContainerOrigin.HolderA, Side.Blue, false, null));
            Assert.AreEqual(-100, round.Red.Vp);
            Assert.IsNull(round.PickUpContainer(2, ContainerOrigin.Common, null, false, null));
            Assert.IsNull(round.PickUpContainer(2, ContainerOrigin.HolderA, Side.Red, false, null));
            Assert.AreEqual(-100, round.Red.Vp);
        }

        [Test]
        public void HolderBAndCNeedSites3And4()
        {
            var round = Make(out var rb, out var ba);
            Assert.IsNotNull(round.ContainerPickupViolation(Side.Red, ContainerOrigin.HolderB, Side.Red, false, null));
            Assert.IsFalse(round.PlaceBlockOnSite(2, Side.Red, 3, 1));
            Assert.IsTrue(round.PlaceBlockOnSite(2, Side.Red, 3, 2));
            Assert.IsTrue(round.Red.SiteAcquired(3));
            Assert.IsFalse(round.PlaceBlockOnSite(2, Side.Red, 3, 2));   // 2 回目は無い
            Assert.IsNull(round.ContainerPickupViolation(Side.Red, ContainerOrigin.HolderB, Side.Red, false, null));
            Assert.IsNotNull(round.ContainerPickupViolation(Side.Red, ContainerOrigin.HolderC, Side.Red, false, null));
            Assert.IsFalse(round.PlaceBlockOnSite(2, Side.Blue, 4, 2));  // 相手のサイトには効果なし
            Assert.AreEqual(0, round.Red.Vp);
        }

        [Test]
        public void Site1HalvesBuilderDamageAndSite2ReducesBaseDamage()
        {
            var round = Make(out var rb, out var ba);
            Assert.AreEqual(10, round.Hit(1, 2));
            round.PlaceBlockOnSite(2, Side.Red, 1, 2);
            Assert.AreEqual(5, round.Hit(1, 2));
            Assert.AreEqual(65, rb.Hp);
            round.Tick(181f);
            Assert.AreEqual(10, round.HitBase(1, Side.Red, false));
            round.PlaceBlockOnSite(2, Side.Red, 2, 2);
            Assert.AreEqual(7, round.HitBase(1, Side.Red, false));    // 10 x 2/3 = 6.67 → 7
        }

        [Test]
        public void TakingFromSpotOrSiteIsFoulAndAttackerHoldingBlockIsFoul()
        {
            var round = Make(out var rb, out var ba);
            round.PlaceContainerOnSpot(2, Side.Red, 1);
            Assert.IsNotNull(round.PickUpContainer(2, ContainerOrigin.HolderA, Side.Red, true, Side.Red));
            Assert.AreEqual(0, round.Red.Containers);
            Assert.AreEqual(10 - 50, round.Red.Vp);
            round.RemoveBlockFromSite(2);
            Assert.AreEqual(-90, round.Red.Vp);
            round.NonBuilderHeldBlock(2);   // ビルダーなら反則なし
            Assert.AreEqual(-90, round.Red.Vp);
            round.NonBuilderHeldBlock(0);
            Assert.AreEqual(-140, round.Red.Vp);
        }

        [Test]
        public void ContainersBreakVpTie()
        {
            var round = Make(out var rb, out var ba);
            round.PlaceContainerOnSpot(2, Side.Red, 1);
            round.Hit(2, 1);   // 青の VP は 0 のまま; 赤 10 VP
            round.PlaceContainerOnSpot(3, Side.Blue, 1);
            Assert.AreEqual(round.Red.Vp, round.Blue.Vp);
            round.PlaceContainerOnSpot(3, Side.Blue, 1);   // 得点なしだがコンテナ数 2
            round.Finish("test");
            Assert.AreEqual(Side.Blue, round.Winner);
        }
    }
}
