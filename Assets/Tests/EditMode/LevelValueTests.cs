using NUnit.Framework;

namespace StrategyCore.Tests
{
    /// <summary>
    /// Выборка значения по уровню — общая для ВСЕХ способностей Interflow (InterflowAbility).
    /// Тихое расхождение здесь двигает баланс всего контента и в игре не видно: живые ассеты
    /// сегодня имеют не больше одной строки, поэтому прогон матча эту логику не задевает вовсе.
    ///
    /// ОДНО правило (блок Б8, целевая модель §9, решение Artsiom 2026-09-06): элемент 0 — базовое значение,
    /// остальные — блок по уровням; нет строки для нужного уровня — берётся ПОСЛЕДНЯЯ заполненная, никогда ноль.
    /// Вторая семантика «короткий массив → 0» (LevelValueOrZero) снесена — тесты на неё удалены вместе с ней.
    /// Массивы объектов (тип урона, эффектор, статы) выбираются тем же правилом через LevelItem.
    /// </summary>
    public class LevelValueTests
    {
        const float Fallback = -1f;

        static readonly float[] One = { 7f };
        static readonly float[] Two = { 7f, 8f };
        static readonly float[] Three = { 7f, 8f, 9f };

        // ------------------------------------------------------- LevelValue (числа) --

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
        public void LevelValue_НетСтрокиДляУровня_БерётПоследнююЗаполненную_АНеНоль()
        {
            // Правило Б8: базовое 7, блок по уровням — 8; герой на уровне выше длины остаётся на 8, а не падает в 0.
            Assert.AreEqual(8f, InterflowAbility.LevelValue(Two, 2, Fallback));
            Assert.AreEqual(8f, InterflowAbility.LevelValue(Two, 99, Fallback));
            Assert.AreEqual(7f, InterflowAbility.LevelValue(One, 3, Fallback));
            Assert.AreNotEqual(0f, InterflowAbility.LevelValue(Two, 99));
        }

        [Test]
        public void LevelValue_БлокПоУровнямПуст_ВсегдаБазовое()
        {
            // Одна строка — умение работает на базовом значении при любом уровне носителя.
            for (int level = 0; level < 10; level++)
                Assert.AreEqual(7f, InterflowAbility.LevelValue(One, level, Fallback), "уровень " + level);
        }

        [Test]
        public void LevelValue_ОтрицательныйУровень_БерётБазовое()
        {
            // Уровень −1 (неулучшенное умение героя до Б9) обязан давать базовое значение, а не падать.
            Assert.AreEqual(7f, InterflowAbility.LevelValue(Three, -1, Fallback));
            Assert.AreEqual(7f, InterflowAbility.LevelValue(Three, -99, Fallback));
        }

        // -------------------------------------------------------- LevelItem (объекты) --

        class Marker { public readonly string name; public Marker(string n) { name = n; } }

        static readonly Marker A = new Marker("A");
        static readonly Marker B = new Marker("B");
        static readonly Marker[] OneItem = { A };
        static readonly Marker[] TwoItems = { A, B };

        [Test]
        public void LevelItem_МассивNullИлиПустой_Null()
        {
            Assert.IsNull(InterflowAbility.LevelItem<Marker>(null, 0));
            Assert.IsNull(InterflowAbility.LevelItem(new Marker[0], 0));
        }

        [Test]
        public void LevelItem_ВДиапазоне_БерётСвойЭлемент()
        {
            Assert.AreSame(A, InterflowAbility.LevelItem(TwoItems, 0));
            Assert.AreSame(B, InterflowAbility.LevelItem(TwoItems, 1));
        }

        [Test]
        public void LevelItem_НетСтрокиДляУровня_БерётПоследнюю()
        {
            // Тип урона «Дыхания дракона» на первом уровне героя раньше выходил за границы — теперь берётся последний.
            Assert.AreSame(B, InterflowAbility.LevelItem(TwoItems, 2));
            Assert.AreSame(A, InterflowAbility.LevelItem(OneItem, 5));
        }

        [Test]
        public void LevelItem_ОтрицательныйУровень_БерётБазовое()
        {
            Assert.AreSame(A, InterflowAbility.LevelItem(TwoItems, -1));
        }

        // ---------------------------------------------------------- Одно правило --

        [Test]
        public void ЧислаИОбъектыВыбираютсяОднимПравилом()
        {
            // Для любого уровня индекс, по которому берётся число, совпадает с индексом объекта той же длины массива.
            for (int level = -2; level < 6; level++)
            {
                float number = InterflowAbility.LevelValue(Two, level);
                Marker item = InterflowAbility.LevelItem(TwoItems, level);
                Assert.AreEqual(number == 7f ? A : B, item, "уровень " + level);
            }
        }
    }
}
