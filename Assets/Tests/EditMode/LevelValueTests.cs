using NUnit.Framework;

namespace StrategyCore.Tests
{
    /// <summary>
    /// Выборка значения по уровню — общая для ВСЕХ способностей Interflow (InterflowAbility).
    /// Тихое расхождение здесь двигает баланс всего контента и в игре не видно: живые ассеты
    /// сегодня имеют maxLevels = 1, поэтому прогон матча эту логику не задевает вовсе.
    ///
    /// Две семантики сознательно разные и обе обязаны сохраниться:
    ///   LevelValue        — КЛАМП к последнему элементу (EffectorArea, Counterattack и др.);
    ///   LevelValueOrZero  — «короткий массив → 0» (FlameCloakActive и семейство).
    /// Значения ниже сняты с живого кода, а не придуманы.
    /// </summary>
    public class LevelValueTests
    {
        const float Fallback = -1f;

        static readonly float[] One = { 7f };
        static readonly float[] Two = { 7f, 8f };
        static readonly float[] Three = { 7f, 8f, 9f };

        // ------------------------------------------------------- LevelValue (кламп) --

        [Test]
        public void LevelValue_МассивNull_ВозвращаетFallback()
        {
            Assert.AreEqual(Fallback, InterflowAbility.LevelValue(null, 0, Fallback));
        }

        [Test]
        public void LevelValue_МассивПустой_ВозвращаетFallback()
        {
            Assert.AreEqual(Fallback, InterflowAbility.LevelValue(new float[0], 0, Fallback));
        }

        [Test]
        public void LevelValue_ПоУмолчаниюFallbackНоль()
        {
            Assert.AreEqual(0f, InterflowAbility.LevelValue(null, 0));
        }

        [Test]
        public void LevelValue_ВДиапазоне_БерётСвойЭлемент()
        {
            Assert.AreEqual(7f, InterflowAbility.LevelValue(Three, 0, Fallback));
            Assert.AreEqual(8f, InterflowAbility.LevelValue(Three, 1, Fallback));
            Assert.AreEqual(9f, InterflowAbility.LevelValue(Three, 2, Fallback));
        }

        [Test]
        public void LevelValue_УровеньВышеДлины_КлампитКПоследнему()
        {
            Assert.AreEqual(8f, InterflowAbility.LevelValue(Two, 2, Fallback));
            Assert.AreEqual(8f, InterflowAbility.LevelValue(Two, 99, Fallback));
            Assert.AreEqual(7f, InterflowAbility.LevelValue(One, 3, Fallback));
        }

        [Test]
        public void LevelValue_ОтрицательныйУровень_БерётПервый()
        {
            Assert.AreEqual(7f, InterflowAbility.LevelValue(Three, -1, Fallback));
            Assert.AreEqual(7f, InterflowAbility.LevelValue(Three, -99, Fallback));
        }

        // ------------------------------------ LevelValueOrZero («короткий массив → 0») --

        [Test]
        public void LevelValueOrZero_МассивNullИлиПустой_Ноль()
        {
            Assert.AreEqual(0f, InterflowAbility.LevelValueOrZero(null, 0));
            Assert.AreEqual(0f, InterflowAbility.LevelValueOrZero(new float[0], 0));
        }

        [Test]
        public void LevelValueOrZero_ВДиапазоне_БерётСвойЭлемент()
        {
            Assert.AreEqual(7f, InterflowAbility.LevelValueOrZero(Three, 0));
            Assert.AreEqual(8f, InterflowAbility.LevelValueOrZero(Three, 1));
            Assert.AreEqual(9f, InterflowAbility.LevelValueOrZero(Three, 2));
        }

        [Test]
        public void LevelValueOrZero_УровеньВышеДлины_Ноль_АНеПоследний()
        {
            // Ключевое отличие от LevelValue: НЕ кламп. Именно на это опирались
            // FlameCloakActive / HeavensBlessingActive / IronVerdictActive / SacrificialPyreActive.
            Assert.AreEqual(0f, InterflowAbility.LevelValueOrZero(Two, 2));
            Assert.AreEqual(0f, InterflowAbility.LevelValueOrZero(One, 1));
        }

        [Test]
        public void LevelValueOrZero_ОтрицательныйУровень_Ноль()
        {
            Assert.AreEqual(0f, InterflowAbility.LevelValueOrZero(Three, -1));
        }

        // ---------------------------------------------------------------- Расхождение --

        [Test]
        public void ДвеСемантикиРасходятсяРовноЗаПределамиМассива()
        {
            // Внутри массива обе функции обязаны давать одно и то же...
            for (int level = 0; level < Three.Length; level++)
                Assert.AreEqual(InterflowAbility.LevelValue(Three, level),
                                InterflowAbility.LevelValueOrZero(Three, level),
                                "уровень " + level + ": внутри массива семантики обязаны совпадать");

            // ...и разойтись сразу за его границей: кламп против нуля.
            Assert.AreEqual(9f, InterflowAbility.LevelValue(Three, 3));
            Assert.AreEqual(0f, InterflowAbility.LevelValueOrZero(Three, 3));
        }
    }
}
