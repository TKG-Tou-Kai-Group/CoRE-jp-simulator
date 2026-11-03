using CoreSim.Rules;
using NUnit.Framework;

namespace CoreSim.Tests
{
    public class AutoProgramTests
    {
        [Test]
        public void ParseAndFormatRoundTrip()
        {
            string text = "hp<30 & enemy<8 > engage=6 @weakest\nbase > base\nalways > goto=left @head\nammo<1 > retreat\ntime>200 & ally<3 > hold @builder";
            AutoProgram p = AutoProgram.Parse(text);
            Assert.AreEqual(5, p.Rules.Count);
            Assert.AreEqual(2, p.Rules[0].Conditions.Count);
            Assert.AreEqual(AutoCondition.HpBelow, p.Rules[0].Conditions[0].Kind);
            Assert.AreEqual(30f, p.Rules[0].Conditions[0].Value);
            Assert.AreEqual(AutoAction.Engage, p.Rules[0].Action);
            Assert.AreEqual(6f, p.Rules[0].Param);
            Assert.AreEqual(AutoTarget.Weakest, p.Rules[0].Target);
            Assert.AreEqual(AutoAction.AttackBase, p.Rules[1].Action);
            Assert.AreEqual(AutoPoint.LeftLane, p.Rules[2].Point);
            Assert.AreEqual(0, p.Rules[2].Conditions.Count);
            Assert.AreEqual(AutoAction.Retreat, p.Rules[3].Action);
            Assert.AreEqual(AutoCondition.TimeAfter, p.Rules[4].Conditions[0].Kind);
            Assert.AreEqual(AutoCondition.AllyWithin, p.Rules[4].Conditions[1].Kind);
            Assert.AreEqual(text, AutoProgram.Format(p));
            Assert.AreEqual(AutoProgram.Format(p), AutoProgram.Format(AutoProgram.Parse(AutoProgram.Format(p))));
        }

        [Test]
        public void BadLinesAreSkippedAndLimitsHold()
        {
            AutoProgram p = AutoProgram.Parse("nonsense\nhp<abc > engage\n > hold\nenemy<5 > fly\n# comment\nhp<50 > hold");
            Assert.AreEqual(1, p.Rules.Count);
            Assert.AreEqual(AutoAction.Hold, p.Rules[0].Action);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 40; i++) sb.Append("always > hold\n");
            Assert.AreEqual(AutoProgram.MaxRules, AutoProgram.Parse(sb.ToString()).Rules.Count);
            Assert.AreEqual(0, AutoProgram.Parse(null).Rules.Count);
        }

        [Test]
        public void MapPickedPointRoundTrips()
        {
            AutoProgram p = AutoProgram.Parse("always > goto=-3.5,2.25 @head\nenemy<5 > goto=enemystart");
            Assert.IsTrue(p.Rules[0].Custom);
            Assert.AreEqual(AutoPoint.Custom, p.Rules[0].Point);
            Assert.AreEqual(-3.5f, p.Rules[0].X);
            Assert.AreEqual(2.25f, p.Rules[0].Z);
            Assert.AreEqual(AutoPoint.EnemyStart, p.Rules[1].Point);
            Assert.AreEqual("always > goto=-3.5,2.25 @head\nenemy<5 > goto=enemystart @nearest", AutoProgram.Format(p));
        }

        [Test]
        public void SelectsFirstMatchingRuleInOrder()
        {
            AutoProgram p = AutoProgram.Default();
            var s = new AutoSensors { EnemyDistance = 4f, AllyDistance = 100f, HpPercent = 100f, Ammo = 150, Time = 10f, BaseOpen = false };
            AutoRule r = p.Select(s);
            Assert.AreEqual(AutoAction.Engage, r.Action);
            Assert.AreEqual(5f, r.Param);
            s.HpPercent = 20f;
            Assert.AreEqual(8f, p.Select(s).Param);
            s.Ammo = 0;
            Assert.AreEqual(AutoAction.Retreat, p.Select(s).Action);
            s.Ammo = 50; s.HpPercent = 90f; s.EnemyDistance = 20f; s.BaseOpen = true;
            Assert.AreEqual(AutoAction.AttackBase, p.Select(s).Action);
            s.BaseOpen = false;
            Assert.AreEqual(AutoAction.GoTo, p.Select(s).Action);
            Assert.AreEqual(AutoPoint.Center, p.Select(s).Point);
            Assert.IsNull(new AutoProgram().Select(s));
        }
    }
}
