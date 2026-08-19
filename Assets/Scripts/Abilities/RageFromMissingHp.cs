using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Кирпич B5 — Масштаб статов от нехватки ХП: чем меньше ХП носителя, тем больше его урон и/или броня.
    // Passive-SO (образец Crit/Basher, правило 2). Урон — OnDamageDealModify (без состояния, читает HP вживую).
    // Броня — пересчёт на OnHPChange (ТОЛЬКО сервер, правило 6): храним свой вклад и снимаем старый перед новым
    // (аддитивно с тех-грейдами, без накопления). Ассет не трогаем (правило 1). Кривые/числа — в Inspector (правило 3).
    public class RageFromMissingHp : InterflowAbility
    {
        public override AbilityType type => AbilityType.Passive;

        [Header("Ярость от нехватки ХП (B5)")]
        [Tooltip("Кривая: доля НЕДОСТАЮЩЕГО ХП (0..1) → множитель урона (1 = без бонуса). [БАЛАНС — Влад]")]
        public AnimationCurve damageByMissingHp = AnimationCurve.Constant(0f, 1f, 1f);

        [Tooltip("Кривая: доля НЕДОСТАЮЩЕГО ХП (0..1) → добавка брони (флэт). [БАЛАНС — Влад]")]
        public AnimationCurve armorByMissingHp = AnimationCurve.Constant(0f, 1f, 0f);

        [Tooltip("Кривая: доля НЕДОСТАЮЩЕГО ХП (0..1) → прибавка СКОРОСТИ АТАКИ в долях (0.4 = +40%, 0 = без бонуса). " +
                 "Добавлено 2026-07-24 для «Священного рвения» (Тир 2 [Б]): например 0 при 0.5 недостающего и 0.4 дальше. [БАЛАНС — Влад]")]
        public AnimationCurve attackSpeedByMissingHp = AnimationCurve.Constant(0f, 1f, 0f);

        [Tooltip("Порог доли ТЕКУЩЕГО ХП, ниже которой включается доп. множитель урона (0 = выкл). [БАЛАНС — Влад]")]
        public float lowHpThreshold = 0f;

        [Tooltip("Доп. множитель урона ниже порога (для Апокалипсиса; 1 = без добавки). [БАЛАНС — Влад]")]
        public float belowThresholdDamageMult = 1f;

        // Пер-юнит состояние (SO общий на всех): подписка OnHPChange (Action без параметров → замыкание на юнит) и
        // последняя применённая добавка брони (снимаем СВОЙ вклад). Ревью §4.1: Unit.Die не зовёт Lock → чистим в Init().
        readonly Dictionary<Unit, Action> hpHandlers = new Dictionary<Unit, Action>();
        readonly Dictionary<Unit, float> appliedArmor = new Dictionary<Unit, float>();
        readonly Dictionary<Unit, float> appliedAttackSpeed = new Dictionary<Unit, float>(); // наш вклад в скорость атаки (в долях)

        public override void Init() { base.Init(); hpHandlers.Clear(); appliedArmor.Clear(); appliedAttackSpeed.Clear(); }

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null) return;

            // Урон — колбэк-модификатор (без состояния). Идемпотентность: не дублировать.
            InterflowAbility.CallbackAdd(unit.OnDamageDealModifyCallbacks, this, level, DamageApply);

            // Броня — подписка на OnHPChange (сервер пересчитывает). Замыкание на юнит (OnHPChange без параметров).
            if (!hpHandlers.ContainsKey(unit))
            {
                Action h = () => RecalcStats(unit);
                unit.OnHPChange += h;
                hpHandlers[unit] = h;
                appliedArmor[unit] = 0f;
                appliedAttackSpeed[unit] = 0f;
                RecalcStats(unit); // разовый начальный пересчёт
            }
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null) return;

            InterflowAbility.CallbackRemove(unit.OnDamageDealModifyCallbacks, this, level);

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

            // Вернуть свой вклад в скорость атаки: снятие «+p» — это применение «−p» (ассет: >0 делит, <0 умножает)
            if (appliedAttackSpeed.TryGetValue(unit, out float appliedAS))
            {
                if (Mathf.Abs(appliedAS) > 0.0001f && !NetworkConnectionHandler.isClient) unit.ChangeAttackSpeed(-appliedAS, true);
                appliedAttackSpeed.Remove(unit);
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

        // Пересчёт брони и скорости атаки по нехватке ХП (ТОЛЬКО сервер, правило 6).
        // Снимаем старый вклад, ставим новый. ChangeArmor/ChangeAttackSpeed не меняют HP → рекурсии по OnHPChange нет.
        void RecalcStats(Unit unit)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (unit == null) return;

            float missing = unit.maxHealth > 0f ? Mathf.Clamp01(1f - unit.health / unit.maxHealth) : 0f;

            if (InterflowDebug.VerboseOn)
                InterflowDebug.Verbose("ЯРОСТЬ пересчёт у " + InterflowDebug.Name(unit) + ": потеряно " +
                                       (missing * 100f).ToString("0") + "% ХП (" + unit.health.ToString("0") + "/" +
                                       unit.maxHealth.ToString("0") + ") — способность " + name);

            // Броня — флэтом
            float desired = armorByMissingHp != null ? armorByMissingHp.Evaluate(missing) : 0f;
            float prev = appliedArmor.TryGetValue(unit, out float p) ? p : 0f;
            float delta = desired - prev;
            if (Mathf.Abs(delta) > 0.0001f)
            {
                unit.ChangeArmor(delta);
                appliedArmor[unit] = desired;
            }

            // Скорость атаки — процентом. Ассет хранит attackSpeed как ИНТЕРВАЛ между ударами:
            // ChangeAttackSpeed(+0.4, true) делит интервал на 1.4, то есть бьёт на 40% чаще.
            float desiredAS = attackSpeedByMissingHp != null ? attackSpeedByMissingHp.Evaluate(missing) : 0f;
            float prevAS = appliedAttackSpeed.TryGetValue(unit, out float pa) ? pa : 0f;
            if (Mathf.Abs(desiredAS - prevAS) > 0.0001f)
            {
                if (Mathf.Abs(prevAS) > 0.0001f) unit.ChangeAttackSpeed(-prevAS, true); // снять прошлый вклад
                if (Mathf.Abs(desiredAS) > 0.0001f) unit.ChangeAttackSpeed(desiredAS, true);
                appliedAttackSpeed[unit] = desiredAS;
            }
        }
    }
}
