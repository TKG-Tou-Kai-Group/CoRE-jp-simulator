using CoreSim.Rules;
using NUnit.Framework;

namespace CoreSim.Tests
{
    public class StriderCourseTests
    {
        [Test]
        public void ZonesClearInOrderWithVpAndRp()
        {
            var c = new StriderCourse(Side.Red);
            Assert.AreEqual(StriderZone.Flat, c.Current);
            Assert.AreEqual(0, c.Update(new[] { -11f, -10.9f }).Count);          // スタートゾーン内
            var got = c.Update(new[] { -8f, -7.5f });                           // 平地の終わり (-7.6) を越えた
            Assert.AreEqual(1, got.Count);
            Assert.AreEqual(StriderZone.Flat, got[0]);
            Assert.AreEqual(10, c.Vp);
            Assert.AreEqual(40, c.Rp);
            Assert.AreEqual(StriderZone.Step, c.Current);
            got = c.Update(new[] { 1.2f });                                     // 段差・砂利・凹凸をまとめて
            Assert.AreEqual(3, got.Count);
            Assert.AreEqual(40, c.Vp);
            Assert.AreEqual(160, c.Rp);
            c.Update(new[] { 5.5f });                                           // スロープ (+60RP)
            c.Update(new[] { 8.7f });                                           // ハードル (+60RP)
            Assert.AreEqual(60, c.Vp);
            Assert.AreEqual(280, c.Rp);
            Assert.IsFalse(c.AllCleared);
            got = c.Update(new[] { 10.9f });                                    // 階段の終わりのテープ
            Assert.AreEqual(StriderZone.Stairs, got[0]);
            Assert.IsTrue(c.AllCleared);
            Assert.AreEqual(100, c.Vp);
            Assert.AreEqual(280, c.Rp);
            Assert.IsNull(c.Current);
            Assert.AreEqual(0, c.Update(new[] { 11f }).Count);                  // 2 回目は得点しない
        }

        [Test]
        public void BlueIsPointSymmetric()
        {
            var c = new StriderCourse(Side.Blue);
            Assert.AreEqual(-1f, c.Direction);
            Assert.AreEqual(7.6f, c.ZoneEndX(StriderZone.Flat));
            Assert.IsTrue(c.InLane(0f, 8.5f));
            Assert.IsFalse(c.InLane(0f, -8.5f));
            Assert.IsTrue(new StriderCourse(Side.Red).InLane(0f, -8.5f));
            Assert.AreEqual(0, c.Update(new[] { 9f }).Count);
            Assert.AreEqual(1, c.Update(new[] { 7.5f }).Count);
        }
    }
}
