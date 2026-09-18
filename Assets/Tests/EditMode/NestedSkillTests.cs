using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StrategyCore.Tests
{
    /// <summary>
    /// Семья Б шага 4 слияния: «умение внутри умения» и событие прилёта снаряда.
    ///
    /// Сам CompositeSkill.Use в режиме редактора не живёт (нужны менеджеры сцены, сеть и выборка целей),
    /// поэтому правила вложения проверяются на ЧИСТОЙ функции CompositeSkill.NestedPlan — той самой,
    /// по которой принимает решение ExecuteNested: второй копии правил нет.
    ///
    /// Селектор умения у свежесозданного ассета ПУСТ, а UnitSelector.IsUnitCompatible при пустом
    /// селекторе никого не пропускает и до SlotManager не доходит (все три отношения выключены —
    /// проверка коротко замыкается). Поэтому «цель не по селектору» воспроизводится без сцены.
    /// </summary>
    public class NestedSkillTests : UnitTestHarness
    {
        CompositeSkill MakeSkill(string name = "Nested")
        {
            CompositeSkill skill = MakeAsset<CompositeSkill>();
            skill.name = name;
            return skill;
        }

        // ============================================ Вложенное умение ==

        [Test]
        public void Вложенное_НаКлиенте_НеИсполняется()
        {
            CompositeSkill skill = MakeSkill();
            Unit caster = MakeBareUnit("Caster");

            Assert.AreEqual(CompositeSkill.NestedOutcome.RefusedClient,
                            skill.NestedPlan(true, 0, null, 0, caster),
                            "клиент вложенное не исполняет (правило 6)");

            // Гейт клиента стоит ПЕРВЫМ: он важнее и глубины, и признака атаки.
            skill.projectileDirectAttack = true;
            Assert.AreEqual(CompositeSkill.NestedOutcome.RefusedClient,
                            skill.NestedPlan(true, 5, null, 0, caster));
        }

        [Test]
        public void Вложенное_ГлубинаДва_Отказ()
        {
            CompositeSkill skill = MakeSkill();
            Unit caster = MakeBareUnit("Caster");

            Assert.AreEqual(CompositeSkill.NestedOutcome.ByPoint,
                            skill.NestedPlan(false, 0, null, 0, caster),
                            "первый уровень вложения разрешён");

            Assert.AreEqual(CompositeSkill.NestedOutcome.RefusedDepth,
                            skill.NestedPlan(false, 1, null, 0, caster),
                            "вложенное внутри вложенного запрещено");
            Assert.AreEqual(CompositeSkill.NestedOutcome.RefusedDepth,
                            skill.NestedPlan(false, 2, null, 0, caster));

            // Признак прямой атаки у вложенного — отдельный отказ (Р16б валидатора, здесь рантайм-гард).
            skill.projectileDirectAttack = true;
            Assert.AreEqual(CompositeSkill.NestedOutcome.RefusedDirectAttack,
                            skill.NestedPlan(false, 0, null, 0, caster),
                            "снаряд вложенного не может быть прямой атакой — прилёт снова кормил бы проки носителя");
        }

        [Test]
        public void Вложенное_ЦельНеПоСелектору_Отказ()
        {
            CompositeSkill skill = MakeSkill();
            Unit caster = MakeBareUnit("Caster");
            Unit target = MakeBareUnit("Target");

            // Селектор умения пуст — цель отбор не проходит. Цель отбрасывается, точка остаётся.
            Assert.IsFalse(skill.IsEligibleTarget(target, 0, caster), "предпосылка теста: цель отбор не проходит");
            Assert.AreEqual(CompositeSkill.NestedOutcome.ByPoint,
                            skill.NestedPlan(false, 0, target, 0, caster),
                            "не прошедшая отбор цель отбрасывается, исполняем по точке");

            // Отбор цели идёт ПОСЛЕ отказов: у вложенного с признаком атаки цель уже не считается.
            skill.projectileDirectAttack = true;
            Assert.AreEqual(CompositeSkill.NestedOutcome.RefusedDirectAttack,
                            skill.NestedPlan(false, 0, target, 0, caster));
        }

        [Test]
        public void Вложенное_БезЦели_ТочкаПередаётся()
        {
            CompositeSkill skill = MakeSkill();
            Unit caster = MakeBareUnit("Caster");

            // Цели нет вовсе (прилёт снаряда в пустую землю, погибшая цель прока) — исполняем по точке.
            Assert.AreEqual(CompositeSkill.NestedOutcome.ByPoint,
                            skill.NestedPlan(false, 0, null, 0, caster),
                            "без цели вложенное идёт по точке, а не отменяется");

            // Кастера тоже может не быть — решение от этого не меняется.
            Assert.AreEqual(CompositeSkill.NestedOutcome.ByPoint,
                            skill.NestedPlan(false, 0, null, 0, null));
        }

        // ============================================ Колбэк прилёта ==

        [Test]
        public void КолбэкПрилёта_ДобавлениеИдемпотентно()
        {
            CompositeSkill skill = MakeSkill();
            var list = new List<ProjectileImpactCallback>();

            System.Action<Vector3, Unit, Unit, int, bool, Projectile, int> handler =
                (point, target, byUnit, byOwner, directAttack, projectile, level) => { };

            InterflowAbility.CallbackAdd(list, skill, 0, handler);
            InterflowAbility.CallbackAdd(list, skill, 0, handler);
            Assert.AreEqual(1, list.Count, "пара (умение, уровень) не дублируется");

            // Другой уровень — отдельная запись: так же устроены соседние списки колбэков.
            InterflowAbility.CallbackAdd(list, skill, 1, handler);
            Assert.AreEqual(2, list.Count);

            Assert.IsTrue(InterflowAbility.CallbackRemove(list, skill, 0));
            Assert.AreEqual(1, list.Count);
            Assert.IsFalse(InterflowAbility.CallbackRemove(list, skill, 0), "снятой записи больше нет");

            Assert.IsTrue(InterflowAbility.CallbackRemove(list, skill, 1));
            Assert.AreEqual(0, list.Count);
        }

        // ============================================ Гейт реакции 5 ==

        [Test]
        public void ГейтРеакции5_ПорядокСчётБросокОткат_НеИзменился()
        {
            CompositePassive passive = MakeAsset<CompositePassive>();
            passive.name = "Проверка гейта";
            passive.onHit.enabled = true;
            passive.onHit.everyNthHit = 3;
            passive.onHit.chance = 1f;      // бросок не делается — поток случайных чисел не трогаем
            passive.onHit.cooldown = 0f;    // откат не ставится — тик не заводится

            Unit carrier = MakeBareUnit("Carrier");
            Unit target = MakeBareUnit("Target");

            // Порядок «откат → счёт → бросок → откат» даёт ровно ту же последовательность, что была
            // до выноса гейта из OnHitApply: срабатывает каждый третий удар, счётчик обнуляется.
            bool[] expected = { false, false, true, false, false, true, false, false, true };
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], passive.OnHitGatePassed(carrier, target),
                                "удар №" + (i + 1) + " при «каждый 3-й»");

            // Счёт ведётся отдельно по каждому носителю.
            Unit second = MakeBareUnit("Second");
            Assert.IsFalse(passive.OnHitGatePassed(second, target), "у второго носителя счёт свой, с нуля");

            // Цели может не быть вовсе (прилёт в пустую землю) — гейт решает про НОСИТЕЛЯ.
            Assert.IsFalse(passive.OnHitGatePassed(second, null));
            Assert.IsTrue(passive.OnHitGatePassed(second, null));
        }
    }
}
