using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Автокаст на стороне умения: когда умение готово сработать само и по какой цели.
    /// Вынесено в партиал по правилу 22 — основной файл конструктора уже за порогом крупного.
    ///
    /// До 2026-08-08 этим занимался компонент на юните: у него были свой радиус, свои галки «свой/враг»
    /// и своя стратегия, которые дублировали настройки умения и расходились с ними. Теперь компонент
    /// хранит только список умений и проверяет готовность (с блока Б5, 2026-09-04 — мана носителя
    /// заполнена целиком; откат не учитывается), а всё остальное спрашивает здесь
    /// (решение Artsiom 2026-08-08).
    /// </summary>
    public partial class CompositeSkill
    {
        /// <summary>
        /// Готово ли умение сработать автокастом у этого носителя, и по какой цели.
        /// Готовность (мана носителя заполнена целиком) вызывающий проверяет ДО этого метода —
        /// поиск при неполной мане был бы холостым.
        ///
        /// Условие по режиму цели:
        /// • На себя / Вся команда / Область вокруг кастера — цель искать не нужно, хватает готовности;
        /// • Конус — нужна текущая цель атаки, и она должна быть в пределах удара (юнит уже дерётся,
        ///   а значит развёрнут к ней — доворачивать перед кастом нечего);
        /// • Умный выбор юнита/точки — цель подбирает стратегия умения; не нашла — каста нет.
        /// </summary>
        /// <param name="target">Цель для каста. Для режимов без цели остаётся null — так и надо.</param>
        public bool AutoCastReady(Unit castingUnit, int level, out Unit target)
        {
            target = null;
            if (castingUnit == null || castingUnit.dead) return false;

            // Условие по себе: пока носитель здоров, умение придерживается. Проверяем ДО поиска цели —
            // перебирать кандидатов, зная, что каста не будет, значит греть процессор впустую.
            if (autoCastSelfHpBelow > 0f)
            {
                if (castingUnit.maxHealth <= 0f) return false;
                if (castingUnit.health / castingUnit.maxHealth >= autoCastSelfHpBelow) return false;
            }

            switch (targetMode)
            {
                case SkillTargetMode.Self:
                case SkillTargetMode.WholeTeam:
                case SkillTargetMode.AreaAroundSelf:
                    return true;

                case SkillTargetMode.Cone:
                    // Цель наружу НЕ отдаём: конус бьёт туда, куда юнит смотрит (CollectTargets берёт
                    // LookDirection), а переданная точка на направление не влияет — зато включила бы
                    // штатную проверку дистанции с подходом к цели (Unit.State.cs:176).
                    return castingUnit.IsAttackTargetInStrikeRange();

                case SkillTargetMode.SmartUnit:
                case SkillTargetMode.SmartPoint:
                    // «Текущая цель атаки» кандидатов не перебирает — цель уже выбрана боем. Поиск здесь
                    // отработал бы вхолостую, а цель могла бы оказаться дальше castRange. Условие то же,
                    // что у конуса: юнит должен уже дотягиваться до цели, а не идти к ней.
                    if (targetStrategy == SkillTargetStrategy.CurrentAttackTarget)
                    {
                        if (!castingUnit.IsAttackTargetInStrikeRange()) return false;

                        target = castingUnit.target;
                        return IsEligibleTarget(target, castingUnit.owner);
                    }

                    // Без дальности умение бьёт по всей карте. Перебирать её каждый тик только чтобы
                    // узнать «есть ли кому» — расточительство: кастуем по готовности, а цель умение найдёт
                    // само в момент применения, тем же путём, что и при касте с кнопки
                    // (решение Artsiom 2026-08-08). Цели нет — каст сгорит, а с ним вся мана носителя (Б5).
                    if (LevelValue(castRange, level) <= 0f) return true;

                    target = PickAutoCastTarget(castingUnit, level);
                    return target != null;
            }

            return false;
        }

        /// <summary>
        /// Цель для автокаста в режимах «умный выбор»: кандидаты вокруг НОСИТЕЛЯ в пределах castRange,
        /// отбор — оба селектора умения, выбор — его стратегия. Точка отсчёта всегда носитель:
        /// настройка «точка отсчёта» работает только для каста с кнопки.
        /// Зовётся только при ненулевой дальности — без неё цель ищется уже в момент каста.
        /// </summary>
        Unit PickAutoCastTarget(Unit castingUnit, int level)
        {
            Unit[] found = SkillTargeting.Candidates(castingUnit, LevelValue(castRange, level), unitSelector);
            if (found == null || found.Length == 0) return null;

            // Неподходящих ЗАНУЛЯЕМ прямо в наборе, а не копим в отдельный список: метод зовётся
            // на тике у каждого носителя, и лишний список с ToArray каждый раз кормил бы сборщик мусора.
            // Все стратегии в SkillTargeting дырки в наборе пропускают штатно.
            // Селектор принадлежности уже применён поиском, боевые роли и мёртвых он не отсеивает.
            for (int i = 0; i < found.Length; i++)
                if (!IsEligibleTarget(found[i], castingUnit.owner)) found[i] = null;

            return SkillTargeting.Pick(targetStrategy, found, castingUnit,
                                       TargetingOptions(level, castingUnit.transform.position));
        }
    }
}
