using UnityEngine;

namespace StrategyCore
{
    // ============ КОНСТРУКТОР СКИЛЛА: РЕЖИМЫ СРАБАТЫВАНИЯ (правило 22 — партиал по фиче) ==
    // Ось «когда умение работает», независимая от оси «кого задевает». Всё на штатных рельсах ядра
    // (правило 2): переключатель и аура живут в everyFrameAbilities юнита, ядро само зовёт Use каждый тик
    // и само проверяет ману у переключателя. Умение-канал собирается штатным флагом Ability.continuous —
    // отдельного значения для него не заводим (правило 5: один механизм, один источник истины).

    /// <summary>Когда умение применяет свои блоки.</summary>
    public enum SkillTrigger
    {
        [InspectorName("Разовый каст")]            Instant,
        [InspectorName("Переключатель (вкл/выкл)")] Toggle,
        [InspectorName("Аура (сама, каждый тик)")]  Aura
    }

    public partial class CompositeSkill
    {
        /// <summary>Режимы, где блоки применяются повторно каждый тик, а не один раз за каст.</summary>
        public bool IsEveryTick => trigger == SkillTrigger.Toggle || trigger == SkillTrigger.Aura;

        /// <summary>
        /// Аура открывается вместе с умением: регистрируем её в цикле юнита, дальше ядро само зовёт Use
        /// каждый тик (паттерн EffectorAura). Переключатель сюда не попадает — его включает игрок кнопкой,
        /// и регистрацией занимается ядро (Unit.UseToggleAbility_Internal).
        /// </summary>
        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            base.Unlock(unit, castingPlayer, level);

            if (trigger != SkillTrigger.Aura || unit == null) return;

            unit.AddEveryFrameAbility(this, -1, isItem, level);
        }

        /// <summary>Умение закрыли (потеря техи, смена облика) — снимаем ауру из цикла юнита.</summary>
        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            base.Lock(unit, castingPlayer, level);

            if (trigger != SkillTrigger.Aura || unit == null) return;

            unit.RemoveEveryFrameIfExists(this, isItem, level);
        }
    }
}
