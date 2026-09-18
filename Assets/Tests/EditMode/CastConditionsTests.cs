using NUnit.Framework;

namespace StrategyCore.Tests
{
    /// <summary>
    /// Блок 21 «условия каста» (семья А шага 4 слияния). Проверяются ЧИСТЫЕ функции блока
    /// и ранний выход самого гейта: всё остальное в CastConditionsMet упирается в выборку целей,
    /// а она в режиме редактора неподъёмна (нужны менеджеры сцены и сеть).
    /// </summary>
    public class CastConditionsTests : UnitTestHarness
    {
        // ================================================ Облик ==

        [Test]
        public void Облик_ИсходныйТребуется_ПриПолиморфеОтказ()
        {
            Unit shape = MakeBareUnit("Shape");

            Assert.IsTrue(SkillCastConditionsBlock.ShapeAllowed(false, null, true, null),
                          "в исходном облике условие «только исходный» проходит");
            Assert.IsFalse(SkillCastConditionsBlock.ShapeAllowed(true, shape, true, null),
                           "под подменой облика условие «только исходный» не проходит");
        }

        [Test]
        public void Облик_ТребуемыйПоUnitTypeID_ЧужойОбликОтказ()
        {
            Unit required = MakeBareUnit("Required");
            required.unitTypeID = 42;

            Unit sameType = MakeBareUnit("SameType");
            sameType.unitTypeID = 42;               // другой экземпляр, тот же тип — должен подойти

            Unit otherType = MakeBareUnit("OtherType");
            otherType.unitTypeID = 43;

            Assert.IsTrue(SkillCastConditionsBlock.ShapeAllowed(true, sameType, false, required),
                          "сравнение идёт по unitTypeID, а не по ссылке на префаб");
            Assert.IsFalse(SkillCastConditionsBlock.ShapeAllowed(true, otherType, false, required),
                           "чужой облик не проходит");
        }

        [Test]
        public void Облик_ТребуемыйБезПолиморфа_Отказ()
        {
            Unit required = MakeBareUnit("Required");
            required.unitTypeID = 42;

            Assert.IsFalse(SkillCastConditionsBlock.ShapeAllowed(false, null, false, required),
                           "требуемый облик без подмены облика не выполняется");
        }

        // ================================================ Здоровье ==

        [Test]
        public void Здоровье_НижеДоли_СтрогоМеньше()
        {
            Assert.IsTrue(SkillCastConditionsBlock.HpAllowed(29f, 100f, 0.3f), "29 из 100 ниже трети");
            Assert.IsFalse(SkillCastConditionsBlock.HpAllowed(30f, 100f, 0.3f), "ровно 30 из 100 — уже не ниже");
            Assert.IsFalse(SkillCastConditionsBlock.HpAllowed(31f, 100f, 0.3f), "31 из 100 выше порога");
            Assert.IsTrue(SkillCastConditionsBlock.HpAllowed(100f, 100f, 0f), "доля 0 — условия по здоровью нет");
        }

        [Test]
        public void Здоровье_МаксимумНоль_Отказ()
        {
            Assert.IsFalse(SkillCastConditionsBlock.HpAllowed(0f, 0f, 0.3f),
                           "доли от нулевого максимума не существует — условие не выполнено");
            Assert.IsTrue(SkillCastConditionsBlock.HpAllowed(0f, 0f, 0f),
                          "но при выключенном условии нулевой максимум ничего не запрещает");
        }

        // ================================================ Гейт целиком ==

        [Test]
        public void Блок_Выключен_Проходит()
        {
            CompositeSkill skill = MakeAsset<CompositeSkill>();
            skill.castConditions = new SkillCastConditionsBlock
            {
                enabled = false, requireOriginalForm = true, casterHpBelow = 0.3f
            };

            Unit caster = MakeBareUnit("Caster");
            caster.polymorphed = true;      // условие облика было бы нарушено
            caster.maxHealth = 100f;
            caster.health = 100f;           // и условие здоровья тоже

            string refusal;
            Assert.IsTrue(skill.CastConditionsMet(caster, caster.owner, 0, out refusal),
                          "выключенный блок не проверяет ничего");
            Assert.IsNull(refusal, "прошедшая проверка текста отказа не даёт");
        }
    }
}
