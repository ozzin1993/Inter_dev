using NUnit.Framework;
using UnityEngine;

namespace StrategyCore.Tests
{
    /// <summary>
    /// Фильтры выбора цели семьи А: сектор «цель только впереди» (SkillTargeting.InFront —
    /// та же формула, по которой отсекается конус) и третий селектор умения по заготовкам
    /// (CompositeSkill.PrefabAllowed).
    ///
    /// Направление взгляда у голого юнита берётся из transform.forward: horizontalPart есть только
    /// у собранного префаба, а Unit.LookDirection на его отсутствие падает на forward объекта.
    /// </summary>
    public class TargetFiltersTests : UnitTestHarness
    {
        /// <summary>Кастер в начале координат, смотрит вдоль +Z.</summary>
        Unit MakeCaster()
        {
            Unit caster = MakeBareUnit("Caster");
            caster.transform.position = Vector3.zero;
            caster.transform.rotation = Quaternion.identity;
            return caster;
        }

        Unit MakeTargetAt(Vector3 position)
        {
            Unit target = MakeBareUnit("Target");
            target.transform.position = position;
            return target;
        }

        // ================================================ Впереди ==

        [Test]
        public void Впереди_ВнутриПолуугла_Да()
        {
            Unit caster = MakeCaster();

            // Сектор 120° — это ±60° от взгляда. Цель под 45° вправо-вперёд обязана пройти.
            Unit target = MakeTargetAt(new Vector3(5f, 0f, 5f));

            Assert.IsTrue(SkillTargeting.InFront(caster, target, 120f));

            // У самой границы полуугла отсев ещё не срабатывает. Точное равенство 60° намеренно
            // не проверяем: сравнение с полууглом упирается в точность Vector3.Angle.
            Unit nearEdge = MakeTargetAt(new Vector3(Mathf.Sin(59f * Mathf.Deg2Rad), 0f, Mathf.Cos(59f * Mathf.Deg2Rad)));
            Assert.IsTrue(SkillTargeting.InFront(caster, nearEdge, 120f));

            Unit outside = MakeTargetAt(new Vector3(Mathf.Sin(61f * Mathf.Deg2Rad), 0f, Mathf.Cos(61f * Mathf.Deg2Rad)));
            Assert.IsFalse(SkillTargeting.InFront(caster, outside, 120f), "за границей сектора цель не берётся");
        }

        [Test]
        public void Впереди_ЗаСпиной_Нет()
        {
            Unit caster = MakeCaster();
            Unit behind = MakeTargetAt(new Vector3(0f, 0f, -5f));

            Assert.IsFalse(SkillTargeting.InFront(caster, behind, 180f), "цель точно сзади в сектор 180° не входит");
            Assert.IsFalse(SkillTargeting.InFront(caster, behind, 120f));

            // Высота на фильтр не влияет: вектор кладётся в горизонталь.
            Unit behindAbove = MakeTargetAt(new Vector3(0f, 12f, -5f));
            Assert.IsFalse(SkillTargeting.InFront(caster, behindAbove, 120f));
        }

        [Test]
        public void Впереди_УголНольИ360_НеФильтрует()
        {
            Unit caster = MakeCaster();
            Unit behind = MakeTargetAt(new Vector3(0f, 0f, -5f));

            Assert.IsTrue(SkillTargeting.InFront(caster, behind, 0f), "0 — фильтр выключен");
            Assert.IsTrue(SkillTargeting.InFront(caster, behind, 360f), "360 — круг, фильтра нет");
            Assert.IsTrue(SkillTargeting.InFront(caster, behind, 720f), "больше 360 — тоже круг");

            // Кастера нет — считать не от чего, отсекать нельзя.
            Assert.IsTrue(SkillTargeting.InFront(null, behind, 120f));

            // Стоят в одной точке — направления нет, считаем «впереди» (как у конуса).
            Unit same = MakeTargetAt(Vector3.zero);
            Assert.IsTrue(SkillTargeting.InFront(caster, same, 90f));
        }

        // ================================================ Заготовки ==

        [Test]
        public void Префабы_ПоUnitTypeID_Проходит()
        {
            Unit prefab = MakeBareUnit("Prefab");
            prefab.unitTypeID = 51727;

            Unit instance = MakeBareUnit("Instance");
            instance.unitTypeID = 51727;   // другой объект, тот же тип

            Assert.IsTrue(CompositeSkill.PrefabAllowed(instance, new[] { prefab }));
        }

        [Test]
        public void Префабы_ПустойСписок_ПропускаетВсех()
        {
            Unit instance = MakeBareUnit("Instance");
            instance.unitTypeID = 7;

            Assert.IsTrue(CompositeSkill.PrefabAllowed(instance, null), "null — фильтра нет");
            Assert.IsTrue(CompositeSkill.PrefabAllowed(instance, new Unit[0]), "пустой список — фильтра нет");
        }

        [Test]
        public void Префабы_НетВСписке_Отсев()
        {
            Unit allowed = MakeBareUnit("Allowed");
            allowed.unitTypeID = 10;

            Unit instance = MakeBareUnit("Instance");
            instance.unitTypeID = 11;

            Assert.IsFalse(CompositeSkill.PrefabAllowed(instance, new[] { allowed }));

            // Пустые элементы списка пропускаются, а не считаются совпадением.
            Assert.IsFalse(CompositeSkill.PrefabAllowed(instance, new Unit[] { null, allowed }));
            Assert.IsTrue(CompositeSkill.PrefabAllowed(allowed, new Unit[] { null, allowed }));
        }
    }
}
