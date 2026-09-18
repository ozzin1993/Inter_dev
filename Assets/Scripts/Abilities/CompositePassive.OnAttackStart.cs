using UnityEngine;

namespace StrategyCore
{
    // ==== КОНСТРУКТОР ПАССИВКИ: ОСЬ «РЕАКЦИИ», СОБЫТИЕ 6 (правило 22 — партиал по фиче) ====
    // «Носитель начал атаку» — шестое событие оси реакций (решение Artsiom 28 от 17.09.2026).
    // Блок несёт ОДНО поведение — показать набор визуала, поэтому данные и подписка живут в одном
    // партиале: разводить их по двум файлам ради двух полей было бы дороже, чем держать рядом.
    //
    // Точка подключения — штатный и до 17.09.2026 НИКЕМ НЕ ЗАНЯТЫЙ хук Unit.OnBeforeDamageDealCallbacks
    // (объявление Units/Unit.cs:362, перебор Units/Unit.State.AttackPlay). Хук идёт ДО удара и до спавна
    // снаряда, поэтому «начало атаки» видно и у ближнего, и у дальнего носителя. Правок ядра ассета
    // не требуется (правило 2).
    //
    // ПОКАЗ ЛОКАЛЬНЫЙ, СООБЩЕНИЙ НОЛЬ (решение 7.1). AttackPlay — общий код ВСЕХ пиров (клиент
    // проигрывает свою атаку сам), а подписки пассивки живут и у клиента: Unit.AbilityLocks зовёт
    // Unlock без клиентского гейта. Поэтому событие и так поднимается на каждом пире, и сетевое
    // сообщение было бы вторым показом того же. ПРИНЯТАЯ ЦЕНА: момент атаки у клиента может разойтись
    // с серверным на задержку сети.
    //
    // ПОЧЕМУ НЕ ВНУТРИ РЕАКЦИИ 5: включение onHit тянет её фильтры, откат, счёт «каждый N-й» и бросок
    // шанса (OnHitGatePassed) на умение, которому нужен только визуал.

    /// <summary>Реакция 6. Носитель начал атаку: показать визуал в момент замаха (локально, без сети).</summary>
    [System.Serializable]
    public class PassiveOnAttackStartBlock
    {
        [Tooltip("Включить блок: в момент, когда носитель начинает атаку, играется визуал ниже. " +
                 "Событие поднимается на КАЖДОМ пире отдельно, сообщений по сети не идёт.")]
        public bool enabled;

        [Tooltip("Визуал события «носитель начал атаку». Точка события — точка атаки (цель или земля). " +
                 "Пусто — блок ничего не делает.")]
        public EventPresentation presentation = new EventPresentation();
    }

    public partial class CompositePassive
    {
        [Header("Реакция 6 — носитель начал атаку")]
        public PassiveOnAttackStartBlock onAttackStart = new PassiveOnAttackStartBlock();

        /// <summary>Включена ли реакция 6 — отдельный предикат, чтобы читать его из соседнего партиала.</summary>
        bool IsAttackStartEnabled() { return onAttackStart != null && onAttackStart.enabled; }

        /// <summary>
        /// Подписка на «носитель начал атаку». Зовётся из WireReactions, то есть на ВСЕХ пирах:
        /// клиентского гейта здесь нет намеренно — именно на клиенте этот показ и работает.
        /// </summary>
        /// <returns>true — подписка поставлена (нужно для проверки «реакции не дали ни одной подписки»).</returns>
        bool WireAttackStart(Unit unit, int level)
        {
            if (unit == null || !IsAttackStartEnabled()) return false;

            CallbackAdd(unit.OnBeforeDamageDealCallbacks, this, level, OnAttackStartApply);
            return true;
        }

        /// <summary>Снятие подписки по паре (умение, уровень). На чужом списке CallbackRemove молчит.</summary>
        void UnwireAttackStart(Unit unit, int level)
        {
            if (unit == null) return;

            CallbackRemove(unit.OnBeforeDamageDealCallbacks, this, level);
        }

        /// <summary>
        /// Носитель начал атаку. Подпись задана штатной структурой BeforeDamageDealCallback
        /// (Core/Types/CallbackStructs.cs) — читаем из неё только цель, точку и носителя.
        /// </summary>
        /// <param name="targetUnit">Цель атаки. null — атака по земле.</param>
        /// <param name="targetPosition">Точка атаки: позиция цели или точка на земле.</param>
        /// <param name="byUnit">Носитель, начавший атаку.</param>
        void OnAttackStartApply(Unit targetUnit, Vector3 targetPosition, Effector[] effectors, float dmg,
                                DamageType damageType, Unit byUnit, int byOwner, int level)
        {
            if (!IsAttackStartEnabled()) return;

            RaiseEventPresentationLocal(this, (int)AbilityEventCode.PassiveAttackStart, onAttackStart.presentation,
                                        byUnit, level, targetUnit, targetPosition);
        }

        /// <summary>
        /// Наборы визуала пассивки по кодам событий. Клиентский презентер спрашивает НАБОР, а не блок:
        /// про устройство реакций он не знает (правило 5).
        /// </summary>
        public override EventPresentation PresentationFor(int eventCode)
        {
            switch ((AbilityEventCode)eventCode)
            {
                case AbilityEventCode.PassiveOnDamaged:   return onDamaged != null ? onDamaged.presentation : null;
                case AbilityEventCode.PassiveDeath:       return onDeath != null ? onDeath.presentation : null;
                case AbilityEventCode.PassiveDeathHeal:   return onDeath != null ? onDeath.healPresentation : null;
                case AbilityEventCode.PassiveKill:        return onKill != null ? onKill.presentation : null;
                case AbilityEventCode.PassiveHpBelow:     return onHpBelow != null ? onHpBelow.presentation : null;
                case AbilityEventCode.PassiveOnHit:       return onHit != null ? onHit.presentation : null;
                case AbilityEventCode.PassiveAttackStart: return onAttackStart != null ? onAttackStart.presentation : null;
                case AbilityEventCode.PassiveAllyDeath:   return onAllyDeath != null ? onAllyDeath.presentation : null;
                case AbilityEventCode.PassiveCleave:      return cleave != null ? cleave.presentation : null;
            }

            return null;
        }
    }
}
