using NUnit.Framework;

namespace StrategyCore.Tests
{
    public class GaugeTests : UnitTestHarness
    {
        // ================================================ HitGain ==

        [Test]
        public void HitGain_НормальныйПрирост()
        {
            Assert.AreEqual(15f, CompositePassive.GaugeHitGain(10f, 100f, 5f));
        }

        [Test]
        public void HitGain_ЗажимНаМаксимуме()
        {
            Assert.AreEqual(100f, CompositePassive.GaugeHitGain(98f, 100f, 5f));
        }

        [Test]
        public void HitGain_НулевойGain()
        {
            Assert.AreEqual(50f, CompositePassive.GaugeHitGain(50f, 100f, 0f));
        }

        // ================================================ TickGain ==

        [Test]
        public void TickGain_НормальныйПрирост()
        {
            Assert.AreEqual(10.1f, CompositePassive.GaugeTickGain(10f, 100f, 1f, 0.1f), 0.001f);
        }

        [Test]
        public void TickGain_ЗажимНаМаксимуме()
        {
            Assert.AreEqual(100f, CompositePassive.GaugeTickGain(99.5f, 100f, 10f, 0.1f));
        }

        [Test]
        public void TickGain_НулевойDt()
        {
            Assert.AreEqual(50f, CompositePassive.GaugeTickGain(50f, 100f, 1f, 0f));
        }

        // ================================================ GaugeHitDecision ==
        // Гейты удара (приёмка «Веры», п.2). Блок-образец: копит 5 за удар, бой помнит 5 секунд.

        static PassiveGaugeBlock HitBlock(bool onlyRanged = false, bool notWhileMorphed = false)
        {
            return new PassiveGaugeBlock
            {
                enabled = true, maxGauge = 100f, hitGain = 5f, tickGain = 1f, combatMemory = 5f,
                onlyRanged = onlyRanged, notWhileMorphed = notWhileMorphed
            };
        }

        [Test]
        public void Вера_ДальнийПрямойУдар_ДаётHitGain()
        {
            bool extendCombat;
            float gain = CompositePassive.GaugeHitDecision(HitBlock(onlyRanged: true), directAttack: true,
                carrierMelee: false, polymorphed: false, dmg: 12f, enemy: true, extendCombat: out extendCombat);

            Assert.AreEqual(5f, gain, Tolerance);
            Assert.IsTrue(extendCombat);
        }

        [Test]
        public void Вера_БлижнийНоситель_ПриOnlyRanged_НеДаётНоПродлеваетБой()
        {
            bool extendCombat;
            float gain = CompositePassive.GaugeHitDecision(HitBlock(onlyRanged: true), directAttack: true,
                carrierMelee: true, polymorphed: false, dmg: 12f, enemy: true, extendCombat: out extendCombat);

            Assert.AreEqual(0f, gain, Tolerance);
            Assert.IsTrue(extendCombat);
        }

        [Test]
        public void Вера_УронУмения_НеДаётНоПродлеваетБой()
        {
            bool extendCombat;
            float gain = CompositePassive.GaugeHitDecision(HitBlock(), directAttack: false,
                carrierMelee: false, polymorphed: false, dmg: 12f, enemy: true, extendCombat: out extendCombat);

            Assert.AreEqual(0f, gain, Tolerance);
            Assert.IsTrue(extendCombat);
        }

        [Test]
        public void Вера_НольУрона_НеДаётИНеПродлевает()
        {
            bool extendCombat;
            float gain = CompositePassive.GaugeHitDecision(HitBlock(), directAttack: true,
                carrierMelee: false, polymorphed: false, dmg: 0f, enemy: true, extendCombat: out extendCombat);

            Assert.AreEqual(0f, gain, Tolerance);
            Assert.IsFalse(extendCombat);
        }

        [Test]
        public void Вера_ВОблике_НеКопитсяИТаймерНеПродлевается()
        {
            bool extendCombat;
            float gain = CompositePassive.GaugeHitDecision(HitBlock(notWhileMorphed: true), directAttack: true,
                carrierMelee: false, polymorphed: true, dmg: 12f, enemy: true, extendCombat: out extendCombat);

            Assert.AreEqual(0f, gain, Tolerance);
            Assert.IsFalse(extendCombat);
        }

        [Test]
        public void Вера_СвояКоманда_НеДаётИНеПродлевает()
        {
            bool extendCombat;
            float gain = CompositePassive.GaugeHitDecision(HitBlock(), directAttack: true,
                carrierMelee: false, polymorphed: false, dmg: 12f, enemy: false, extendCombat: out extendCombat);

            Assert.AreEqual(0f, gain, Tolerance);
            Assert.IsFalse(extendCombat);
        }

        [Test]
        public void Вера_БлокВыключен_НеДаётИНеПродлевает()
        {
            PassiveGaugeBlock block = HitBlock();
            block.enabled = false;

            bool extendCombat;
            float gain = CompositePassive.GaugeHitDecision(block, directAttack: true,
                carrierMelee: false, polymorphed: false, dmg: 12f, enemy: true, extendCombat: out extendCombat);

            Assert.AreEqual(0f, gain, Tolerance);
            Assert.IsFalse(extendCombat);
        }

        // ================================================ SkillGaugeBlock.Ready ==

        [Test]
        public void Ready_BlockNull_ВозвращаетTrue()
        {
            Assert.IsTrue(SkillGaugeBlock.Ready(null, null));
        }

        [Test]
        public void Ready_Disabled_ВозвращаетTrue()
        {
            var block = new SkillGaugeBlock { enabled = false, requireFull = true };
            Assert.IsTrue(SkillGaugeBlock.Ready(null, block));
        }

        [Test]
        public void Ready_MaxGaugeНоль_ВозвращаетFalse()
        {
            var unit = MakeBareUnit();
            unit.maxGauge = 0f;
            var block = new SkillGaugeBlock { enabled = true };
            Assert.IsFalse(SkillGaugeBlock.Ready(unit, block));
        }

        [Test]
        public void Ready_RequireFull_ШкалаПолная_True()
        {
            var unit = MakeBareUnit();
            unit.maxGauge = 100f;
            unit.gauge = 100f;
            var block = new SkillGaugeBlock { enabled = true, requireFull = true };
            Assert.IsTrue(SkillGaugeBlock.Ready(unit, block));
        }

        [Test]
        public void Ready_RequireFull_ШкалаНеПолная_False()
        {
            var unit = MakeBareUnit();
            unit.maxGauge = 100f;
            unit.gauge = 50f;
            var block = new SkillGaugeBlock { enabled = true, requireFull = true };
            Assert.IsFalse(SkillGaugeBlock.Ready(unit, block));
        }

        [Test]
        public void Ready_БезRequireFull_ЛюбаяШкала_True()
        {
            var unit = MakeBareUnit();
            unit.maxGauge = 100f;
            unit.gauge = 1f;
            var block = new SkillGaugeBlock { enabled = true, requireFull = false };
            Assert.IsTrue(SkillGaugeBlock.Ready(unit, block));
        }
    }
}
