using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Кирпич B2 — «Каждая N-я атака»: носитель считает свои атаки и на N-й наносит доп. урон (и, позже, нокбэк B9).
    // Passive-SO (образец Basher/Crit, правило 2). Счётчик — пер-юнит в Dictionary внутри SO (SO общий на всех).
    // Хук OnAfterDamageDeal (есть targetUnit; колбэк копируется в Projectile → покрывает дальних). Серверо-авторит.
    // Ассет StrategyCore не трогаем (правило 1). Числа — только в Inspector (правило 3).
    public class EveryNthAttack : InterflowAbility
    {
        public override AbilityType type => AbilityType.Passive;

        [Header("Каждая N-я атака (B2)")]
        [Tooltip("Каждая какая по счёту атака усилена (N). [БАЛАНС — Влад]")]
        public int everyN = 3;

        [Tooltip("Множитель доп. урона усиленной атаки по уровням (доля от урона атаки; 0 = без добавки). [БАЛАНС — Влад]")]
        public float[] bonusDamageMultiplier;

        [Tooltip("Тип доп. урона усиленной атаки (пусто → тип базовой атаки)")]
        public DamageType bonusDamageType;

        [Tooltip("Считать только прямые атаки (не урон способностей/сплэш). Дизайн: счёт только по главной цели.")]
        public bool onlyDirectAttack = true;

        [Tooltip("Вызвать нокбэк (кирпич B9) на цели при усиленной атаке.")]
        public bool triggerKnockback;

        [Tooltip("Дальность нокбэка усиленной атаки (клетки → юниты Unity), если triggerKnockback. [БАЛАНС — Влад]")]
        public float knockbackDistance = 2f;

        [Tooltip("Заморозка цели на миг нокбэка, сек (0 = нет). [БАЛАНС — Влад]")]
        public float knockbackStunTime = 0f;

        [Tooltip("Нокбэк уважает иммунитет к контролю (B12).")]
        public bool knockbackRespectControlImmunity = true;

        [Header("Глубокая рана (урон за передвижение) — добавлено 2026-07-24")]
        [Tooltip("Вешать на цель усиленной атаки «Глубокую рану»: цель получает урон, пока двигается. " +
                 "Потребитель — «Рассекающие цепи» (Тир 5 [Б] Флагеллант-Всадник).")]
        public bool triggerBleedOnMove;

        [Tooltip("Урон раны за каждый пройденный целью метр. [БАЛАНС — Влад]")]
        public float bleedDamagePerMeter = 2f;

        [Tooltip("Сколько секунд держится рана. [БАЛАНС — Влад]")]
        public float bleedDuration = 4f;

        [Tooltip("Тип урона раны. Пусто — тип базовой атаки носителя.")]
        public DamageType bleedDamageType;

        // Пер-юнит счётчик атак (SO общий → состояние в словаре). §6.1: счёт БЕЗ сброса при апгрейде теха
        // (Lock счётчик не трогает). Ревью §4.1: Unit.Die не зовёт Lock → чистим в OnEnable (SO переживает
        // Play-сессии в редакторе) + лениво снимаем мёртвые/null-ключи (PruneDeadKeys).
        readonly Dictionary<Unit, int> attackCounters = new Dictionary<Unit, int>();

        // Штатный хук инициализации SO (зовётся из GameManager.Awake; OnEnable ассета НЕ переопределяем).
        // Чистим счётчик — SO переживает Play-сессии в редакторе (ревью §4.1).
        public override void Init() { base.Init(); attackCounters.Clear(); }

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null) return;
            if (!attackCounters.ContainsKey(unit)) attackCounters[unit] = 0; // §6.1: существующий счёт не сбрасываем (апгрейд)
            PruneDeadKeys(); // чистка мёртвых ключей на нечастом пути (не в колбэке атаки — перф)

                        InterflowAbility.CallbackAdd(unit.OnAfterDamageDealCallbacks, this, level, NthApply);
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            // Снимаем ТОЛЬКО колбэк; счётчик НЕ трогаем (§6.1: без сброса при апгрейде). Мёртвые ключи — OnEnable/лениво.
            InterflowAbility.CallbackRemove(unit.OnAfterDamageDealCallbacks, this, level);
        }

        void NthApply(Unit targetUnit, Vector3 targetPosition, Effector[] effectors, float dmg, bool directAttack,
                      DamageType damageType, Unit byUnit, Projectile byProjectile, int byOwner, int level)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (onlyDirectAttack && !directAttack) return;   // сплэш/бонус идут directAttack=false → счёт только по главной
            if (byUnit == null || everyN <= 0) return;

            int c = attackCounters.TryGetValue(byUnit, out int v) ? v : 0;
            c++;
            if (c >= everyN)
            {
                c = 0;
                ApplyEmpowered(targetUnit, dmg, damageType, byUnit, byOwner, level);
            }
            attackCounters[byUnit] = c;
        }

        void ApplyEmpowered(Unit targetUnit, float dmg, DamageType damageType, Unit byUnit, int byOwner, int level)
        {
            // Доп. урон через GetDamage (НЕ DealDamage — тот заново вызовет OnAfterDamageDeal → рекурсия). Как Basher.
            if (targetUnit != null && bonusDamageMultiplier != null && level < bonusDamageMultiplier.Length
                && bonusDamageMultiplier[level] > 0f)
            {
                DamageType dt = bonusDamageType != null ? bonusDamageType : damageType;
                targetUnit.GetDamage(dmg * bonusDamageMultiplier[level], dt, byOwner, byUnit, true, out _);
                RequestForceSync();
            }

            // Нокбэк (кирпич B9) на усиленной атаке — от носителя к цели (композиция «Тяжёлые болты»).
            if (triggerKnockback && targetUnit != null && byUnit != null)
                Knockback.Apply(targetUnit, byUnit.transform.position, knockbackDistance, knockbackStunTime, knockbackRespectControlImmunity);

            // «Глубокая рана»: урон цели за передвижение, пока рана держится («Рассекающие цепи»).
            if (triggerBleedOnMove && targetUnit != null)
            {
                DamageType bleedType = bleedDamageType != null ? bleedDamageType : damageType;
                BleedOnMove.Apply(targetUnit, byUnit, byOwner, bleedType, bleedDamagePerMeter, bleedDuration);
            }
        }

        // Снять из словаря уничтоженные/null-ключи (Unit.Die не зовёт Lock — ревью §4.1). Модификацию делаем после обхода.
        void PruneDeadKeys()
        {
            if (attackCounters.Count == 0) return;
            List<Unit> dead = null;
            foreach (Unit k in attackCounters.Keys)
                if (k == null) (dead ??= new List<Unit>()).Add(k);
            if (dead != null)
                for (int i = 0; i < dead.Count; i++) attackCounters.Remove(dead[i]);
        }
    }
}
