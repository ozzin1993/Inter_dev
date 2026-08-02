using NUnit.Framework;
using StrategyCore;

namespace Interflow.Tests.EditMode
{
    /// <summary>
    /// Выборка силы и длительности эффектора по уровню (ADR-006 §5.2: ассет — ЧТО, умение — СКОЛЬКО).
    /// Ключевая граница — «пусто или 0 = брать из ассета»: именно она держит совместимость с 31 точкой
    /// вызова EffectorAdd, где никаких чисел не передаётся.
    /// </summary>
    public class EffectorRecordTests
    {
        static SkillEffectorRecord Rec(float[] power, float[] duration) =>
            new SkillEffectorRecord { effector = null, power = power, duration = duration };

        // ------------------------------------------------------------------ СИЛА --

        [Test]
        public void Сила_ЗаписьNull_ДаётЕдиницу()
        {
            Assert.AreEqual(1f, CompositeSkill.RecordPower(null, 0));
        }

        [Test]
        public void Сила_ПустойМассив_ДаётЕдиницу()
        {
            Assert.AreEqual(1f, CompositeSkill.RecordPower(Rec(null, null), 0));
            Assert.AreEqual(1f, CompositeSkill.RecordPower(Rec(new float[0], null), 0));
        }

        [Test]
        public void Сила_Ноль_ЗначитБратьИзАссета()
        {
            Assert.AreEqual(1f, CompositeSkill.RecordPower(Rec(new[] { 0f }, null), 0));
        }

        [Test]
        public void Сила_БерётсяПоУровню()
        {
            var r = Rec(new[] { 1.6f, 2f, 2.4f }, null);
            Assert.AreEqual(1.6f, CompositeSkill.RecordPower(r, 0));
            Assert.AreEqual(2f, CompositeSkill.RecordPower(r, 1));
            Assert.AreEqual(2.4f, CompositeSkill.RecordPower(r, 2));
        }

        [Test]
        public void Сила_МассивКороче_КлампКПоследнему()
        {
            var r = Rec(new[] { 1.6f, 2f }, null);
            Assert.AreEqual(2f, CompositeSkill.RecordPower(r, 5));
        }

        [Test]
        public void Сила_ОтрицательныйУровень_БерётПервый()
        {
            var r = Rec(new[] { 1.6f, 2f }, null);
            Assert.AreEqual(1.6f, CompositeSkill.RecordPower(r, -1));
        }

        // ---------------------------------------------------------- ДЛИТЕЛЬНОСТЬ --

        [Test]
        public void Длительность_ЗаписьNull_ДаётМинусЕдиницу()
        {
            Assert.AreEqual(-1f, CompositeSkill.RecordDuration(null, 0));
        }

        [Test]
        public void Длительность_ПустойМассив_ЗначитБратьИзАссета()
        {
            Assert.AreEqual(-1f, CompositeSkill.RecordDuration(Rec(null, null), 0));
            Assert.AreEqual(-1f, CompositeSkill.RecordDuration(Rec(null, new float[0]), 0));
        }

        [Test]
        public void Длительность_Ноль_ЗначитБратьИзАссета()
        {
            Assert.AreEqual(-1f, CompositeSkill.RecordDuration(Rec(null, new[] { 0f }), 0));
        }

        [Test]
        public void Длительность_БерётсяПоУровню()
        {
            var r = Rec(null, new[] { 0.4f, 1f, 3f });
            Assert.AreEqual(0.4f, CompositeSkill.RecordDuration(r, 0));
            Assert.AreEqual(1f, CompositeSkill.RecordDuration(r, 1));
            Assert.AreEqual(3f, CompositeSkill.RecordDuration(r, 2));
        }

        [Test]
        public void Длительность_МассивКороче_КлампКПоследнему()
        {
            var r = Rec(null, new[] { 0.4f, 1f });
            Assert.AreEqual(1f, CompositeSkill.RecordDuration(r, 9));
        }

        // Сила и длительность независимы: заполнение одной не влияет на другую.
        [Test]
        public void СилаИДлительность_Независимы()
        {
            var r = Rec(new[] { 2f }, null);
            Assert.AreEqual(2f, CompositeSkill.RecordPower(r, 0));
            Assert.AreEqual(-1f, CompositeSkill.RecordDuration(r, 0));
        }
    }
}