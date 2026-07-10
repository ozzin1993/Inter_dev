using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Кирпич B5 — Масштаб статов от нехватки ХП: чем меньше ХП носителя, тем больше его урон и/или броня.
    // Passive-SO (образец Crit/Basher, правило 2). Урон — OnDamageDealModify (без состояния, читает HP вживую).
    // Броня — пересчёт на OnHPChange (ТОЛЬКО сервер, правило 6): храним свой вклад и снимаем старый перед новым
    // (аддитивно с тех-грейдами, без накопления). Ассет не трогаем (правило 1). Кривые/числа — в Inspector (правило 3).
    [CreateAssetMenu(fileName = "RageFromMissingHp", menuName = "StrategyCore/Abilities/Interflow/RageFromMissingHp (B5)")]
    public class RageFromMissingHp : Ability
    {
        public override AbilityType type => AbilityType.Passive;

        [Header("Ярость от нехватки ХП (B5)")]
        [Tooltip("Кривая: доля НЕДОСТАЮЩЕГО ХП (0..1) → множитель урона (1 = без бонуса). [БАЛАНС — Влад]")]
        public AnimationCurve damageByMissingHp = AnimationCurve.Constant(0f, 1f, 1f);

        [Tooltip("Кривая: доля НЕДОСТАЮЩЕГО ХП (0..1) → добавка брони (флэт). [БАЛАНС — Влад]")]
        public AnimationCurve armorByMissingHp = AnimationCurve.Constant(0f, 1f, 0f);

        [Tooltip("Порог доли ТЕКУЩЕГО ХП, ниже которой включается доп. множитель урона (0 = выкл). [БАЛАНС — Влад]")]
        public float lowHpThreshold = 0f;

        [Tooltip("Доп. множитель урона ниже порога (для Апокалипсиса; 1 = без добавки). [БАЛАНС — Влад]")]
        public float belowThresholdDamageMult = 1f;

        // Пер-юнит состояние (SO общий на всех): подписка OnHPChange (Action без параметров → замыкание на юнит) и
        // последняя применённая добавка брони (снимаем СВОЙ вклад). Ревью §4.1: Unit.Die не зовёт Lock → чистим в Init().
        readonly Dictionary<Unit, Action> hpHandlers = new Dictionary<Unit, Action>();
        readonly Dictionary<Unit, float> appliedArmor = new Dictionary<Unit, float>();

        public override void Init() { base.Init(); hpHandlers.Clear(); appliedArmor.Clear(); }

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null) return;

            // Урон — колбэк-модификатор (без состояния). Идемпотентность: не дублировать.
            bool has = false;
            for (int i = 0; i < unit.OnDamageDealModifyCallbacks.Count; i++)
                if (unit.OnDamageDealModifyCallbacks[i].Ability == this && unit.OnDamageDealModifyCallbacks[i].Level == level) { has = true; break; }
            if (!has)
                unit.OnDamageDealModifyCallbacks.Add(new DamageModifyCallback { Callback = DamageApply, Ability = this, Level = level });

            // Броня — подписка на OnHPChange (сервер пересчитывает). Замыкание на юнит (OnHPChange без параметров).
            if (!hpHandlers.ContainsKey(unit))
            {
                Action h = () => RecalcArmor(unit);
                unit.OnHPChange += h;
                hpHandlers[unit] = h;
                appliedArmor[unit] = 0f;
                RecalcArmor(unit); // разовый начальный пересчёт
            }
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null) return;

            for (int i = 0; i < unit.OnDamageDealModifyCallbacks.Count; i++)
            {
                DamageModifyCallback c = unit.OnDamageDealModifyCallbacks[i];
                if (c.Ability == this && c.Level == level) { unit.OnDamageDealModifyCallbacks.RemoveAt(i); break; }
            }

            // Снять подписку OnHPChange + вернуть СВОЙ вклад брони (как было).
            if (hpHandlers.TryGetValue(unit, out Action h))
            {
                unit.OnHPChange -= h;
                hpHandlers.Remove(unit);
            }
            if (appliedArmor.TryGetValue(unit, out float applied))
            {
                if (Mathf.Abs(applied) > 0.0001f && !NetworkConnectionHandler.isClient) unit.ChangeArmor(-applied);
                appliedArmor.Remove(unit);
            }
        }

        // Множитель урона от нехватки ХП (детерминирован от HP, читается вживую → без состояния).
        float DamageApply(Unit unit, int level, float dmg, bool directAttack)
        {
            if (unit == null || unit.maxHealth <= 0f) return dmg;
            float missing = Mathf.Clamp01(1f - unit.health / unit.maxHealth);
            float mult = damageByMissingHp != null ? damageByMissingHp.Evaluate(missing) : 1f;
            if (lowHpThreshold > 0f && belowThresholdDamageMult != 1f && (unit.health / unit.maxHealth) < lowHpThreshold)
                mult *= belowThresholdDamageMult;
            return dmg * mult;
        }

        // Пересчёт брони по нехватке ХП (ТОЛЬКО сервер, правило 6). Снимаем старый вклад, ставим новый.
        // ChangeArmor меняет броню (флэт), не HP → OnHPChange повторно не дёргает (без рекурсии).
        void RecalcArmor(Unit unit)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (unit == null) return;
            float missing = unit.maxHealth > 0f ? Mathf.Clamp01(1f - unit.health / unit.maxHealth) : 0f;
            float desired = armorByMissingHp != null ? armorByMissingHp.Evaluate(missing) : 0f;
            float prev = appliedArmor.TryGetValue(unit, out float p) ? p : 0f;
            float delta = desired - prev;
            if (Mathf.Abs(delta) > 0.0001f)
            {
                unit.ChangeArmor(delta);
                appliedArmor[unit] = desired;
            }
        }
    }
}
