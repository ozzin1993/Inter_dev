using UnityEngine;

namespace StrategyCore
{
    // ===== КОНСТРУКТОР УМЕНИЯ: УМЕНИЕ ВНУТРИ УМЕНИЯ (правило 22 — партиал по фиче) =====
    // ОДИН исполнитель на обоих хозяев вложения (проект «Условия_Цели_и_Проки» §3.5, вариант «да»):
    //   • блок 22 «прилёт снаряда» — умение исполняется в точке, куда прилетел снаряд родителя;
    //   • реакция 5 пассивки (procSkill) — умение исполняется по цели прока.
    // Оба зовут ExecuteNested, поэтому правила вложения (сервер, глубина, признак атаки, отбор цели)
    // живут в одном месте, а не двумя копиями (правило 5).
    //
    // ЧТО ЭТОТ ВЫЗОВ НАМЕРЕННО ПРОПУСКАЕТ: откат, стоимость, Check, замок способности и рассылку
    // AbilityUseSend (Unit.Ability.cs). Вложенное умение — не каст носителя, а часть эффекта хозяина:
    // своей кнопки у него нет, платить за него второй раз нечем и незачем. Презентация при этом
    // штатная — её публикует EmitSkillFired внутри Execute.
    //
    // ПАКЕТЫ УРОНА вложенного собираются обычным путём Execute: attackingUnit = носитель,
    // directAttack = false (снаряд вложенного признаком атаки быть не может — см. Refused.DirectAttack),
    // sourceAbility = вложенное умение. Специально здесь ничего не делается.
    public partial class CompositeSkill
    {
        /// <summary>
        /// Что решено про вложенное исполнение ДО вызова Use. Чистое решение вынесено в
        /// <see cref="NestedPlan"/> отдельным перечислением, потому что сам Use в режиме редактора
        /// не живёт (нужны менеджеры сцены и сеть), а правила вложения проверять тестами надо.
        /// </summary>
        public enum NestedOutcome
        {
            /// <summary>Клиент вложенное не исполняет (правило 6).</summary>
            RefusedClient,
            /// <summary>Глубина вложения уже занята: вложенное внутри вложенного запрещено.</summary>
            RefusedDepth,
            /// <summary>У вложенного включён признак прямой атаки — его прилёт снова кормил бы проки носителя.</summary>
            RefusedDirectAttack,
            /// <summary>Исполняем по цели.</summary>
            ByUnit,
            /// <summary>Исполняем по точке: цели нет или она не прошла отбор умения.</summary>
            ByPoint
        }

        /// <summary>
        /// Текущая глубина вложения. Статическая и общая на ВСЕ ассеты умений намеренно: цепочка
        /// «A внутри B внутри A» обязана обрываться так же, как «A внутри A». Счётчик снимается
        /// в finally, поэтому исключение внутри Use его не оставит поднятым.
        /// </summary>
        static int nestedDepth;

        /// <summary>
        /// Чистое решение «что делать с вложенным исполнением» — без побочных эффектов.
        /// Ровно это решение принимает <see cref="ExecuteNested"/>; дублирования правил нет.
        /// </summary>
        /// <param name="isClientPeer">Текущий пир — чистый клиент.</param>
        /// <param name="depth">Текущая глубина вложения.</param>
        /// <param name="aimUnit">Предложенная цель; null — только точка.</param>
        /// <param name="castingPlayer">Владелец, от чьего лица исполняется вложенное.</param>
        /// <param name="caster">Носитель, от чьего лица исполняется вложенное.</param>
        public NestedOutcome NestedPlan(bool isClientPeer, int depth, Unit aimUnit, int castingPlayer, Unit caster)
        {
            if (isClientPeer) return NestedOutcome.RefusedClient;
            if (depth >= 1) return NestedOutcome.RefusedDepth;

            // Р16б валидатора ловит это в редакторе; здесь рантайм-гард на случай ассета, собранного мимо него.
            if (projectileDirectAttack) return NestedOutcome.RefusedDirectAttack;

            if (aimUnit != null && IsEligibleTarget(aimUnit, castingPlayer, caster)) return NestedOutcome.ByUnit;

            return NestedOutcome.ByPoint;   // цель не прошла отбор — точка остаётся
        }

        /// <summary>
        /// Исполнить это умение как вложенное — от лица носителя, по цели или по точке.
        /// Зовут блок 22 «прилёт снаряда» и реакция 5 пассивки.
        /// </summary>
        /// <returns>true — умение исполнено; false — отказ (причина ушла в лог).</returns>
        public bool ExecuteNested(Unit caster, int castingPlayer, int level, Unit aimUnit, Vector3 aimPoint)
        {
            NestedOutcome plan = NestedPlan(IsClientPeer, nestedDepth, aimUnit, castingPlayer, caster);

            switch (plan)
            {
                case NestedOutcome.RefusedClient:
                    if (InterflowDebug.FullOn) LogNestedRefused(caster, "клиент вложенное не исполняет");
                    return false;

                case NestedOutcome.RefusedDepth:
                    if (InterflowDebug.FullOn)
                        LogNestedRefused(caster, "глубина вложения уже " + nestedDepth + " — вложенное внутри вложенного запрещено");
                    return false;

                case NestedOutcome.RefusedDirectAttack:
                    Debug.LogWarning($"[{name}] Умение используется как вложенное, но у него включён признак " +
                                     "«снаряд считается прямой атакой» — прилёт снова кормил бы проки носителя. Вложенное не исполнено.");
                    if (InterflowDebug.FullOn) LogNestedRefused(caster, "у вложенного включён признак прямой атаки");
                    return false;
            }

            if (plan == NestedOutcome.ByPoint && aimUnit != null && InterflowDebug.FullOn)
                LogNestedAimDropped(caster, aimUnit);

            if (InterflowDebug.FullOn) LogNestedFired(caster, plan == NestedOutcome.ByUnit ? aimUnit : null, aimPoint);

            nestedDepth++;
            try
            {
                if (plan == NestedOutcome.ByUnit) Use(caster, castingPlayer, level, aimUnit);
                else Use(caster, castingPlayer, level, aimPoint);
            }
            finally { nestedDepth--; }

            return true;
        }
    }
}
