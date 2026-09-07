using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ============ КОНСТРУКТОР ПАССИВКИ: ОСЬ «СВОЙСТВА» (правило 22 — партиал по фиче) ==
    // Восемь блоков-галок. Каждый блок — тонкая обёртка над УЖЕ СУЩЕСТВУЮЩИМ механизмом движка
    // (правило 2); ни одна механика здесь не изобретается заново, все они взяты из классов,
    // которые эти блоки заменяют для НОВЫХ пассивок: Passive, ControlImmunityPassive,
    // PassiveInvisibility, ArmorPierce, SplashModifier, ExtraAttackEffectors, MovementAura, ScalingAura.
    // Старые классы остаются жить — на них висят 33 ассета (решение Artsiom 2026-08-09).
    // Исключение — блок 3: с 2026-09-03 это «сопротивления и слабости» поверх приёмника состояний
    // (Units/UnitResistances.cs); прежний блок «иммунитет к замедлениям» и класс SlowImmunity снесены
    // (решение Artsiom 29.08.2026, Р5: иммунитет к замедлениям = сопротивление 100 %).

    /// <summary>Когда аура работает.</summary>
    public enum PassiveAuraCondition
    {
        [InspectorName("Всегда")]              Always,
        [InspectorName("Пока носитель движется")] WhileMoving,
        [InspectorName("Пока не хватает ХП")]  WhileHurt
    }

    /// <summary>1. Изменение характеристик носителя, пока умение открыто.</summary>
    [Serializable]
    public class PassiveStatsBlock
    {
        [Tooltip("Включить блок: пассивка меняет характеристики носителя.")]
        public bool enabled;

        [Tooltip("Что именно меняется. Абсолютные значения прибавляются, процентные умножают. " +
                 "ВАЖНО: ноль в процентном поле — это «НЕ МЕНЯТЬ». 0.25 — это +25 %, −0.15 — это −15 %. " +
                 "Единица здесь означает +100 %, а не «оставить как было».")]
        public AbilityPassiveEffects effects = new AbilityPassiveEffects();
    }

    /// <summary>2. Иммунитет к оглушению, обезоруживанию и немоте.</summary>
    [Serializable]
    public class PassiveControlImmunityBlock
    {
        [Tooltip("Включить блок: носителя нельзя оглушить, обезоружить и заглушить. " +
                 "Уже идущее оглушение не снимается — иммунитет действует на НОВЫЕ попытки контроля.")]
        public bool enabled;
    }

    /// <summary>3. Сопротивления и слабости носителя к категориям состояний.</summary>
    [Serializable]
    public class PassiveResistancesBlock
    {
        [Tooltip("Включить блок: носитель получает сопротивления или слабости к категориям состояний, пока умение открыто. " +
                 "Категории контроля (оглушение, немота, безоружие, слепота) сопротивление режет по ВРЕМЕНИ, " +
                 "числовые (замедления, периодический урон) — по СИЛЕ. Иммунитет к контролю (блок 2) — отдельная " +
                 "бинарная механика, сопротивление действует поверх него.")]
        public bool enabled;

        [Tooltip("Строки «категория — доля». Из всех источников на юните (пассивки, бафы) действует ОДНО значение: " +
                 "самая сильная слабость, а когда слабостей нет — самое сильное сопротивление; значения не перемножаются. " +
                 "1 и больше — состояния категории не действуют вовсе (так задаётся «игнорирует любые замедления»).")]
        public ResistanceEntry[] entries;
    }

    /// <summary>4. Постоянная невидимость носителя.</summary>
    [Serializable]
    public class PassiveInvisibilityBlock
    {
        [Tooltip("Включить блок: носитель невидим, пока умение открыто. " +
                 "ВНИМАНИЕ: при закрытии умения невидимость НЕ снимается — так же ведёт себя штатный " +
                 "PassiveInvisibility. Причина в ядре: рефкаунта невидимости нет, и снятие погасило бы " +
                 "невидимость, выданную эффектором или другим умением.")]
        public bool enabled;
    }

    /// <summary>5. Атаки носителя игнорируют долю физической защиты цели.</summary>
    [Serializable]
    public class PassiveArmorPierceBlock
    {
        [Tooltip("Включить блок: атаки носителя пробивают броню.")]
        public bool enabled;

        [Tooltip("Какая доля брони цели игнорируется, по уровням. 0.2 — пятая часть, 1 — броня не учитывается вовсе.")]
        public float[] fraction = new float[1] { 0.2f };
    }

    /// <summary>6. Изменение сплеша автоатаки носителя.</summary>
    [Serializable]
    public class PassiveSplashBlock
    {
        [Tooltip("Включить блок: у носителя меняются параметры взрыва автоатаки.")]
        public bool enabled;

        [Tooltip("Прибавка к радиусу взрыва в долях. 0.4 = +40 %. Отрицательное значение уменьшает.")]
        public float radiusPercentChange = 0.4f;

        [Tooltip("Прибавка к радиусу взрыва в метрах (применяется после процентной).")]
        public float radiusFlatChange;

        [Tooltip("Новый спад урона к краю зоны: 1 — урон одинаков по всей зоне, 0.5 — на краю вдвое меньше. −1 — не менять.")]
        public float newSplashReduction = -1f;

        [Tooltip("Включить сплеш, если у юнита его не было. ВЫКЛ — для юнита без сплеша блок ничего не делает.")]
        public bool enableSplashIfDisabled;
    }

    /// <summary>7. Дополнительные эффекторы к автоатаке носителя.</summary>
    [Serializable]
    public class PassiveAttackEffectorsBlock
    {
        [Tooltip("Включить блок: к атакам носителя добавятся эффекторы.")]
        public bool enabled;

        [Tooltip("Эффекторы, которые добавятся к атакам носителя. Накладываются на цель удара, " +
                 "на цели сплеша и на цели снаряда — как штатные Attack Effectors юнита.")]
        public Effector[] effectors;
    }

    /// <summary>8. Аура вокруг носителя: урон в секунду и эффекторы по радиусу.</summary>
    [Serializable]
    public class PassiveAuraBlock
    {
        [Tooltip("Включить блок: вокруг носителя работает аура. Радиус берётся из поля «Радиус по уровням» " +
                 "самого умения, а кого задевает — из поля «Кто вообще может быть целью».")]
        public bool enabled;

        [Tooltip("Когда аура работает.")]
        public PassiveAuraCondition condition = PassiveAuraCondition.Always;

        [Tooltip("Урон в СЕКУНДУ задетым. 0 — только эффекторы.")]
        public float damagePerSecond;

        [Tooltip("Тип урона ауры. Обязателен, если урон больше нуля.")]
        public DamageType damageType;

        [Tooltip("Эффекторы, накладываемые задетым (например замедление). Длительность эффектора должна " +
                 "быть короткой — он обновляется каждый тик, пока цель в радиусе.")]
        public Effector[] effectors;

        [Tooltip("Задевать только юнитов ближнего боя. ВЫКЛ — всех, кто проходит по «Кто вообще может быть целью».")]
        public bool onlyMeleeUnits;

        [Tooltip("Только для условия «пока носитель движется»: минимальное смещение за тик, которое " +
                 "считается движением (мировые единицы). Отсекает дрожание на месте.")]
        public float movementEpsilon = 0.02f;

        [Tooltip("Только для условия «пока не хватает ХП»: доля НЕДОСТАЮЩЕГО здоровья, с которой аура " +
                 "включается. 0.25 — работает, когда потеряна четверть здоровья и больше.")]
        [Range(0f, 1f)]
        public float missingHpFrom = 0.25f;

        [Tooltip("Накладывать эффекторы ауры и на самого носителя. Урон себе не наносится никогда.")]
        public bool includeSelf;
    }

    public partial class CompositePassive
    {
        // ============================================================ 1. ХАРАКТЕРИСТИКИ ==

        void ApplyStats(Unit unit, Carrier c)
        {
            if (stats == null || !stats.enabled || stats.effects == null) return;

            stats.effects.AddEffect(unit);
            c.stats = true;
        }

        void RemoveStats(Unit unit, Carrier c)
        {
            if (!c.stats || stats == null || stats.effects == null) return;

            stats.effects.RemoveEffect(unit);
            c.stats = false;
        }

        // ========================================================= 2. ИММУНИТЕТ К КОНТРОЛЮ ==

        void ApplyControlImmunity(Unit unit, Carrier c)
        {
            if (controlImmunity == null || !controlImmunity.enabled) return;

            ControlImmunity ci = unit.GetComponent<ControlImmunity>();
            if (ci == null) ci = unit.gameObject.AddComponent<ControlImmunity>();

            ci.Add();                 // рефкаунт: снимаем ровно столько, сколько выдали
            c.controlImmunity = true;
        }

        void RemoveControlImmunity(Unit unit, Carrier c)
        {
            if (!c.controlImmunity) return;

            ControlImmunity ci = unit.GetComponent<ControlImmunity>();
            if (ci != null) ci.Remove();
            c.controlImmunity = false;
        }

        // ================================================= 3. СОПРОТИВЛЕНИЯ И СЛАБОСТИ ==
        // [Interflow fix 2026-09-03 status-resistances] По образцу блока 2: носитель — компонент на юните
        // (UnitResistances), навешивается лениво при первой выдаче; источник вклада — этот ассет,
        // по нему же вклады и снимаются, ровно выданные (флаг в Carrier).

        void ApplyResistances(Unit unit, Carrier c)
        {
            if (resistances == null || !resistances.enabled) return;
            if (resistances.entries == null || resistances.entries.Length == 0) return;

            UnitResistances holder = unit.GetComponent<UnitResistances>();
            if (holder == null) holder = unit.gameObject.AddComponent<UnitResistances>();

            for (int i = 0; i < resistances.entries.Length; i++)
            {
                ResistanceEntry entry = resistances.entries[i];
                if (entry == null) continue;

                holder.Add(this, entry.category, entry.value);   // строки с категорией «Нет» носитель отбрасывает сам
            }

            c.resistances = true;
        }

        void RemoveResistances(Unit unit, Carrier c)
        {
            if (!c.resistances) return;

            UnitResistances holder = unit.GetComponent<UnitResistances>();
            if (holder != null) holder.Remove(this);   // снимает все вклады этого ассета разом
            c.resistances = false;
        }

        // ============================================================== 4. НЕВИДИМОСТЬ ==

        void ApplyInvisibility(Unit unit, Carrier c)
        {
            if (invisibility == null || !invisibility.enabled) return;

            unit.SetInvisibility(true);
            c.invisibility = true;
        }

        void RemoveInvisibility(Unit unit, Carrier c)
        {
            // Намеренно НИЧЕГО не делаем — см. тултип блока: в ядре нет рефкаунта невидимости,
            // и снятие погасило бы невидимость от эффектора или другого умения. Поведение
            // повторяет штатный PassiveInvisibility (его Lock тоже пуст).
            c.invisibility = false;
        }

        // ============================================================ 5. ПРОБИТИЕ БРОНИ ==

        void ApplyArmorPierce(Unit unit, Carrier c, int level)
        {
            if (armorPierce == null || !armorPierce.enabled) return;

            float fraction = LevelValue(armorPierce.fraction, level, 0f);
            if (fraction <= 0f) return;

            InterflowCombat.ArmorPierceAdd(unit, fraction);
            c.armorPierceFraction = fraction;   // снимаем ТЕМ ЖЕ числом, иначе доля уплывёт
        }

        void RemoveArmorPierce(Unit unit, Carrier c)
        {
            if (c.armorPierceFraction <= 0f) return;

            InterflowCombat.ArmorPierceRemove(unit, c.armorPierceFraction);
            c.armorPierceFraction = 0f;
        }

        // =================================================================== 6. СПЛЕШ ==

        /// <summary>Исходные параметры сплеша юнита, чтобы вернуть их при закрытии умения.</summary>
        class SplashState
        {
            public bool isSplash;
            public float radius;
            public float reduction;
            public bool followTarget;
        }

        void ApplySplash(Unit unit, Carrier c)
        {
            if (splash == null || !splash.enabled) return;
            if (!unit.isSplash && !splash.enableSplashIfDisabled) return;

            c.splash = new SplashState
            {
                isSplash = unit.isSplash,
                radius = unit.splashRadius,
                reduction = unit.splashReduction,
                followTarget = unit.projectileFollowTarget
            };

            float newRadius = unit.splashRadius * (1f + splash.radiusPercentChange) + splash.radiusFlatChange;
            if (newRadius < 0f) newRadius = 0f;

            float reduction = splash.newSplashReduction >= 0f ? splash.newSplashReduction : unit.splashReduction;

            unit.ChangeSplash(true, newRadius, reduction, unit.projectileFollowTarget);
        }

        void RemoveSplash(Unit unit, Carrier c)
        {
            if (c.splash == null) return;

            unit.ChangeSplash(c.splash.isSplash, c.splash.radius, c.splash.reduction, c.splash.followTarget);
            c.splash = null;
        }

        // ================================================== 7. ЭФФЕКТОРЫ К СВОИМ АТАКАМ ==

        void ApplyAttackEffectors(Unit unit, Carrier c)
        {
            if (attackEffectors == null || !attackEffectors.enabled) return;
            if (attackEffectors.effectors == null || attackEffectors.effectors.Length == 0) return;

            Effector[] current = unit.attackEffectors != null ? unit.attackEffectors : new Effector[0];
            c.originalAttackEffectors = current;

            List<Effector> merged = new List<Effector>(current);
            for (int i = 0; i < attackEffectors.effectors.Length; i++)
            {
                Effector eff = attackEffectors.effectors[i];
                if (eff == null || merged.Contains(eff)) continue;

                merged.Add(eff);
            }

            unit.attackEffectors = merged.ToArray();
        }

        void RemoveAttackEffectors(Unit unit, Carrier c)
        {
            if (c.originalAttackEffectors == null) return;

            unit.attackEffectors = c.originalAttackEffectors;
            c.originalAttackEffectors = null;
        }

        // ==================================================================== 8. АУРА ==

        void ApplyAura(Unit unit, Carrier c)
        {
            if (aura == null || !aura.enabled) return;

            c.aura = true;            // работает тиком
        }

        /// <summary>Один тик ауры. Только сервер.</summary>
        void TickAura(Unit unit, Carrier c)
        {
            Vector3 now = unit.transform.position;
            float moved = (now - c.lastPosition).magnitude;
            c.lastPosition = now;

            if (!AuraConditionMet(unit, moved)) return;

            float r = LevelValue(radius, c.level, 0f);
            if (r <= 0f) return;

            float dt = GameManager.Instance.currentDeltaTime;
            bool dealsDamage = aura.damagePerSecond > 0f && aura.damageType != null;

            // Урон без типа молча не наносится — предупреждаем один раз за матч, а не глотаем (правило 9).
            if (aura.damagePerSecond > 0f && aura.damageType == null && !auraDamageTypeWarned)
            {
                auraDamageTypeWarned = true;
                Debug.LogWarning("[CompositePassive] '" + name + "': у ауры задан урон в секунду, " +
                                 "но не задан тип урона — урон не наносится.");
            }

            bool hasEffectors = aura.effectors != null && aura.effectors.Length > 0;

            if (aura.includeSelf && hasEffectors && !unit.dead)
                Effector.EffectorAdd(unit, unit, aura.effectors);

            Unit[] targets = Utils.GetUnitsInRadius(new Vector2(now.x, now.z), r, unit.owner, unitSelector, -1, unit);
            if (targets == null) return;

            for (int t = 0; t < targets.Length; t++)
            {
                Unit target = targets[t];
                if (target == null || target.dead) continue;
                if (aura.onlyMeleeUnits && !target.melee) continue;

                if (dealsDamage)
                {
                    DamagePacket packet = DamagePacket.Create(aura.damagePerSecond * dt, aura.damageType, unit.owner, unit, false, this);   // [Interflow fix 2026-09-04 damage-full-packet] пакет одной записи
                    target.GetDamage(in packet, out float _);
                }

                if (target.dead) continue;   // погибла от этого же урона — эффекторы на труп не вешаем

                if (hasEffectors) Effector.EffectorAdd(unit, target, aura.effectors);
            }
        }

        bool auraDamageTypeWarned;

        bool AuraConditionMet(Unit unit, float movedThisTick)
        {
            switch (aura.condition)
            {
                case PassiveAuraCondition.WhileMoving:
                    return movedThisTick >= aura.movementEpsilon;

                case PassiveAuraCondition.WhileHurt:
                    if (unit.maxHealth <= 0f) return false;
                    return Mathf.Clamp01(1f - unit.health / unit.maxHealth) >= aura.missingHpFrom;

                default:
                    return true;
            }
        }
    }
}
