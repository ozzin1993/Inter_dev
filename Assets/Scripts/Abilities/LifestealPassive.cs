using UnityEngine;

namespace StrategyCore
{
    // Кирпич B1 — Вампиризм: носитель лечится на долю нанесённого им урона. Passive-SO по образцу Basher/Crit
    // (правило 2). Хук OnAfterDamageDeal — колбэк копируется в Projectile → покрывает и дальних. Серверо-авторит.
    // Ассет StrategyCore не трогаем (правило 1). Числа — только в Inspector (правило 3).
    public class LifestealPassive : InterflowAbility
    {
        public override AbilityType type => AbilityType.Passive;

        [Header("Вампиризм (B1)")]
        [Tooltip("Доля нанесённого урона, возвращаемая носителю в ХП, по уровням (0.1 = 10%). [БАЛАНС — Влад]")]
        public float[] healPercent;

        [Tooltip("Лечить только от прямых атак (не от урона способностей/эффекторов/сплэша). " +
                 "§6.1: выбор режима — в будущем нужны оба; стартовое значение — только прямые.")]
        public bool onlyDirectAttack = true;

        [Header("Презентация")]
        // [Interflow 2026-09-17, решение Artsiom 30] Набор на СТАРОМ классе, хотя вампиризм уже выразим
        // кирпичом реакции 5 конструктора пассивок: пока класс жив, его событие тоже получает визуал.
        [Tooltip("Визуал события «вампиризм вернул здоровье»: играется на носителе. Пусто — без визуала.")]
        public EventPresentation healPresentation = new EventPresentation();

        /// <summary>Набор визуала по коду события: у этого класса единственное событие — возврат здоровья.</summary>
        public override EventPresentation PresentationFor(int eventCode)
            => (AbilityEventCode)eventCode == AbilityEventCode.Lifesteal ? healPresentation : null;

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null) return;
            // Идемпотентность: не добавлять второй колбэк при повторном Unlock (приёмка B1).
                        InterflowAbility.CallbackAdd(unit.OnAfterDamageDealCallbacks, this, level, LifestealApply);
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            InterflowAbility.CallbackRemove(unit.OnAfterDamageDealCallbacks, this, level);
        }

        // Лечение носителя (byUnit) на долю нанесённого урона. [Interflow fix 2026-09-09 hit-outcome] dmg —
        // ФАКТИЧЕСКИ СНЯТОЕ здоровье: с 09.09.2026 бьющий передаёт колбэкам снятое, а не заявленную величину
        // атаки (Units/Unit.Combat.cs, решение Artsiom по §11.3 промта «Боевой конвейер»). ПРИНЯТАЯ ЦЕНА:
        // возврат здоровья на бронированных целях стал меньше. ChangeHP клампит до maxHealth и синкает клиентам.
        void LifestealApply(Unit targetUnit, Vector3 targetPosition, Effector[] effectors, float dmg, bool directAttack,
                            DamageType damageType, Unit byUnit, Projectile byProjectile, int byOwner, int level)
        {
            if (NetworkConnectionHandler.isClient) return;                                  // страховка (правило 6)
            if (onlyDirectAttack && !directAttack) return;                                  // §6.1: только прямые (по флагу)
            if (byUnit == null) return;                                                     // некого лечить
            if (unitSelector.AnySelectors() && !UnitSelector.IsUnitCompatible(byOwner, targetUnit, unitSelector)) return;
            // Доля — общей выборкой по уровням (Б8): раньше уровень выше длины массива молча выключал вампиризм.
            float heal = dmg * InterflowAbility.LevelValue(healPercent, level);
            if (heal <= 0f) return;
            byUnit.ChangeHP(heal);
            AbilityFacts.Proc(this, byUnit);   // [2026-09-10] показ срабатывания — см. AbilityFacts

            // [Interflow 2026-09-17] Хозяин набора «вампиризм»: показ НА НОСИТЕЛЕ (лечится он, а не цель).
            EmitEventPresentation(this, (int)AbilityEventCode.Lifesteal, healPresentation,
                                  byUnit, level, byUnit, byUnit.transform.position);
        }
    }
}
