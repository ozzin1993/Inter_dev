using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Щит на союзника: поглощающий барьер (кирпич B17 <see cref="AbsorbShield"/>) плюс, по настройке,
    /// снижение входящего урона, ответная заморозка бьющих и вспышка при пробитии.
    ///
    /// Потребители:
    /// • Тир 3 [А] «Ледяная корка» — щит союзнику-танку, замораживающий врагов ближнего боя, которые его бьют;
    /// • Тир 3 [Б] «Жертвенный покров» — щит в долях от макс. ХП цели, −50% входящего, при пробитии — ослепление вокруг.
    ///
    /// Цель выбирает <see cref="AutoAbilityUser"/> (стратегии «Ближайший союзник заданной категории» и
    /// «Раненый союзник ниже порога ХП»). Штатные поля Ability: <c>cooldown</c> — период, <c>castRange</c> — дальность,
    /// <c>unitSelector</c> — кого можно накрыть.
    /// </summary>
    [CreateAssetMenu(fileName = "ShieldAlly", menuName = "StrategyCore/Abilities/Interflow/ShieldAlly (Щит союзнику)")]
    public class ShieldAlly : InterflowAbility
    {
        public override AbilityType type { get { return AbilityType.Unit; } }

        [Header("Щит")]
        [Tooltip("Фиксированный объём поглощения, по уровням. Складывается с долей от макс. ХП ниже.")]
        public float[] shieldFlat = new float[1] { 0f };

        [Tooltip("Объём поглощения как доля от МАКСИМАЛЬНОГО ХП цели, по уровням. 0.3 = 30% её макс. ХП.")]
        public float[] shieldPercentOfMaxHp = new float[1] { 0.3f };

        [Tooltip("Сколько секунд держится щит, если его не пробьют. 0 — до исчерпания объёма.")]
        public float[] shieldDuration = new float[1] { 8f };

        [Tooltip("Не кастовать на цель, у которой щит уже висит. ВЫКЛ — каст обновит щит (берётся больший объём).")]
        public bool skipIfAlreadyShielded = true;

        [Header("Снижение входящего урона (пока щит держится)")]
        [Tooltip("Множитель входящего урона под щитом: 0.5 = −50%. 1 — без снижения.")]
        public float incomingDamageMultiplier = 1f;

        [Header("Кого считать врагом (для заморозки и вспышки)")]
        [Tooltip("Отбор ВРАГОВ для ответной заморозки и вспышки. Штатный Unit Selector выше отвечает за ЦЕЛЬ каста (союзника), " +
                 "здесь — за тех, по кому бьёт ответная реакция.")]
        public UnitSelector enemySelector;

        [Header("Ответная заморозка бьющих")]
        [Tooltip("Радиус, в котором ищутся враги для заморозки, когда носителя щита бьют. 0 — не морозить.")]
        public float freezeRadius = 0f;

        [Tooltip("Длительность оглушения (заморозки) врагов, сек. Уважает иммунитет к контролю.")]
        public float freezeStunSeconds = 1f;

        [Tooltip("Морозить только врагов ближнего боя (поле melee на юните).")]
        public bool freezeOnlyMelee = true;

        [Tooltip("Эффекторы, накладываемые замороженным (визуал льда, замедление). Пусто — только оглушение.")]
        public Effector[] freezeEffectors;

        [Header("Вспышка при ПРОБИТИИ щита")]
        [Tooltip("Радиус вспышки вокруг носителя, когда щит пробит. 0 — вспышки нет.")]
        public float burstRadius = 0f;

        [Tooltip("Шанс промаха (ослепление) у задетых вспышкой, 0..1. 0 — не ослеплять.")]
        [Range(0f, 1f)]
        public float burstBlindChance = 0f;

        [Tooltip("Длительность ослепления от вспышки, сек.")]
        public float burstBlindDuration = 2f;

        // Check намеренно НЕ переопределяем: ассет при Check == false просто выходит из UseAbilityImmediately,
        // не снимая состояние AbilityCasting, — юнит завис бы с выключенным агентом. Все проверки делаем в Use,
        // тихим выходом (каст «сгорает», юнит продолжает бой).

        // Реакция «морозить бьющих», навешенная на цель, — чтобы снять её вместе со щитом
        readonly Dictionary<Unit, InterflowCombat.DamagedHandler> retaliationByTarget =
            new Dictionary<Unit, InterflowCombat.DamagedHandler>();

        public override void Init()
        {
            base.Init();
            retaliationByTarget.Clear();
        }

        public override void Use(Unit castingUnit, int castingPlayer, int level, Unit unit)
        {
            if (NetworkConnectionHandler.isClient) return; // правило 6
            if (unit == null || unit.dead) return;
            if (skipIfAlreadyShielded && AbsorbShield.IsActiveOn(unit)) return;

            float amount = LevelValue(shieldFlat, level, 0f) + unit.maxHealth * LevelValue(shieldPercentOfMaxHp, level, 0f);
            if (amount <= 0f) return;

            float duration = LevelValue(shieldDuration, level, 0f);

            Unit shielded = unit;

            System.Action<Unit> onDepleted = carrier => Burst(carrier, castingPlayer);

            // Щит снят по ЛЮБОЙ причине (пробит, истёк, носитель погиб) — снимаем реакцию
            // и сопутствующее снижение входящего урона
            float incomingMult = incomingDamageMultiplier;
            System.Action<Unit> onEnded = carrier =>
            {
                RemoveRetaliation(carrier);
                if (carrier != null && incomingMult != 1f) IncomingDamageModifier.RemoveRule(carrier, incomingMult);
            };

            // ПОРЯДОК ВАЖЕН: сначала щит. Перекаст поверх живого щита дёргает его прежний onEnded,
            // а тот снимает реакцию с этой же цели — поставь мы новую реакцию раньше, он снёс бы именно её.
            AbsorbShield.Apply(unit, amount, duration, onDepleted, onEnded);

            RemoveRetaliation(shielded); // подстраховка, если реакция осталась от щита, снятого мимо onEnded

            // Реакция на бьющих, пока щит держится
            if (freezeRadius > 0f)
            {
                InterflowCombat.DamagedHandler retaliation = (victim, attacker, damageType, damageDealt, directAttack) =>
                    FreezeAround(victim, castingUnit, castingPlayer);

                retaliationByTarget[shielded] = retaliation;
                InterflowCombat.DamagedListenerAdd(shielded, retaliation);
            }

            // Снижение входящего урона живёт ровно столько, сколько сам щит:
            // при раннем пробитии его снимает onEnded, а не таймер
            if (incomingDamageMultiplier != 1f && duration > 0f)
            {
                IncomingDamageModifier.Apply(unit, incomingDamageMultiplier, duration);
            }

            RequestForceSync();
        }

        /// <summary>Снять реакцию «морозить бьющих» с цели. target == null (носитель уничтожен) — чистим все мёртвые записи.</summary>
        void RemoveRetaliation(Unit target)
        {
            if (target == null)
            {
                List<Unit> dead = null;
                foreach (Unit u in retaliationByTarget.Keys)
                    if (u == null || u.dead) (dead ?? (dead = new List<Unit>())).Add(u);

                if (dead == null) return;
                for (int i = 0; i < dead.Count; i++)
                {
                    if (dead[i] != null) InterflowCombat.DamagedListenerRemove(dead[i], retaliationByTarget[dead[i]]);
                    retaliationByTarget.Remove(dead[i]);
                }

                return;
            }

            if (!retaliationByTarget.TryGetValue(target, out var handler)) return;

            InterflowCombat.DamagedListenerRemove(target, handler);
            retaliationByTarget.Remove(target);
        }

        /// <summary>Заморозить врагов вокруг носителя щита (реакция на удар по нему).</summary>
        void FreezeAround(Unit carrier, Unit caster, int casterPlayer)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (carrier == null || carrier.dead || freezeRadius <= 0f) return;

            Vector2 center = new Vector2(carrier.transform.position.x, carrier.transform.position.z);
            Unit[] enemies = Utils.GetUnitsInRadius(center, freezeRadius, carrier.owner, enemySelector, -1, carrier);
            if (enemies == null) return;

            for (int i = 0; i < enemies.Length; i++)
            {
                Unit e = enemies[i];
                if (e == null || e.dead) continue;
                if (freezeOnlyMelee && !e.melee) continue;

                if (freezeStunSeconds > 0f) e.Stun(freezeStunSeconds); // штатный стан уважает ControlImmunity
                if (freezeEffectors != null && freezeEffectors.Length > 0) Effector.EffectorAdd(casterPlayer, e, freezeEffectors);
            }
        }

        /// <summary>Вспышка при пробитии щита: ослепляет врагов вокруг носителя.</summary>
        void Burst(Unit carrier, int casterPlayer)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (carrier == null || carrier.dead || burstRadius <= 0f) return;
            if (burstBlindChance <= 0f || burstBlindDuration <= 0f) return;

            Vector2 center = new Vector2(carrier.transform.position.x, carrier.transform.position.z);
            Unit[] enemies = Utils.GetUnitsInRadius(center, burstRadius, carrier.owner, enemySelector, -1, carrier);
            if (enemies == null) return;

            for (int i = 0; i < enemies.Length; i++)
            {
                Unit e = enemies[i];
                if (e == null || e.dead) continue;

                BlindDebuff.Apply(e, burstBlindChance, burstBlindDuration);
            }
        }

    }
}
