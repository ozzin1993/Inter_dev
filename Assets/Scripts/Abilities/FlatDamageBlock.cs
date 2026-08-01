using UnityEngine;

namespace StrategyCore
{
    // Кирпич B13 — Флэт-блок входящего урона: снижает каждый входящий удар на фиксированную величину;
    // минимальный итог — настраиваемый пол (обычно 1, удар нельзя обнулить полностью). Опционально удваивает
    // блок ниже порога ХП (для «Трупного щита Роя» Нежити <30%). Passive-SO по образцу B1/B5 (правило 2).
    // Хук — ЖЕРТВА: Unit.OnBeforeGetDamageCallbacks (DamageModifyCallback, входящий урон, как Evasion).
    // Ассет StrategyCore не трогаем (правило 1). Числа — в Inspector (правило 3).
    // Ограничение канала: колбэк НЕ передаёт тип урона → блок применяется к ЛЮБОМУ типу (штатное ограничение).
    [CreateAssetMenu(fileName = "FlatDamageBlock", menuName = "StrategyCore/Abilities/Interflow/FlatDamageBlock (B13)")]
    public class FlatDamageBlock : InterflowAbility
    {
        public override AbilityType type => AbilityType.Passive;

        [Header("Флэт-блок урона (B13)")]
        [Tooltip("Сколько единиц вычитается из каждого входящего удара, по уровням (4 = «Плотная шкура» Дворфа). [БАЛАНС — Влад]")]
        public float[] blockAmount;

        [Tooltip("Минимальный итоговый урон после блока (обычно 1 — удар не может быть обнулён полностью).")]
        public float minDamage = 1f;

        [Tooltip("Порог доли ТЕКУЩЕГО ХП, ниже которой блок удваивается (0 = выкл; для «Трупного щита» <0.30). [БАЛАНС — Влад]")]
        public float doubleBelowHpFraction = 0f;

        // Состояния нет — колбэк детерминирован от входа (как DamageApply у B5). Идемпотентность — по Ability+Level.
        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null) return;
            for (int i = 0; i < unit.OnBeforeGetDamageCallbacks.Count; i++)
                if (unit.OnBeforeGetDamageCallbacks[i].Ability == this && unit.OnBeforeGetDamageCallbacks[i].Level == level) return;
            unit.OnBeforeGetDamageCallbacks.Add(new DamageModifyCallback { Callback = BlockApply, Ability = this, Level = level });
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null) return;
            for (int i = 0; i < unit.OnBeforeGetDamageCallbacks.Count; i++)
            {
                DamageModifyCallback c = unit.OnBeforeGetDamageCallbacks[i];
                if (c.Ability == this && c.Level == level) { unit.OnBeforeGetDamageCallbacks.RemoveAt(i); break; }
            }
        }

        // (self, level, dmg, directAttack) → изменённый dmg. Вычитаем блок, клампим по minDamage.
        float BlockApply(Unit unit, int level, float dmg, bool directAttack)
        {
            if (unit == null || blockAmount == null || level >= blockAmount.Length) return dmg;
            if (dmg <= minDamage) return dmg; // уже не больше пола — не трогаем
            float block = blockAmount[level];
            if (doubleBelowHpFraction > 0f && unit.maxHealth > 0f && (unit.health / unit.maxHealth) < doubleBelowHpFraction)
                block *= 2f;
            float result = dmg - block;
            return result < minDamage ? minDamage : result;
        }
    }
}
