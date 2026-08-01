using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// «Священный огонь»: урон врагу и одновременное лечение тех союзников, кто этого врага атакует.
    /// Потребитель: Тир 3 [Б] Инквизитор-Экзорцист, специализация 1.
    ///
    /// Связка «кто атакует эту цель» берётся из штатного поля <c>Unit.target</c> — отдельного реестра агрессии
    /// в ассете нет, и заводить его не требуется: достаточно осмотреть союзников вокруг цели.
    /// </summary>
    [CreateAssetMenu(fileName = "HolyFire", menuName = "StrategyCore/Abilities/Interflow/HolyFire (Урон врагу + хил его атакующих)")]
    public class HolyFire : InterflowAbility
    {
        public override AbilityType type { get { return AbilityType.Unit; } }

        [Header("Урон цели")]
        [Tooltip("Урон врагу, по уровням.")]
        public float[] damage = new float[1] { 30f };

        [Tooltip("Тип урона (например Свет). Без него способность не сработает.")]
        public DamageType damageType;

        [Tooltip("Эффекторы на цель (горение, дебафф). Пусто — только урон.")]
        public Effector[] targetEffectors;

        [Header("Лечение атакующих союзников")]
        [Tooltip("Сколько ХП получает каждый союзник, атакующий эту цель, по уровням.")]
        public float[] healFlat = new float[1] { 20f };

        [Tooltip("Дополнительное лечение как доля от нанесённого урона. 0.5 = половина урона уходит в хил каждому.")]
        public float healPercentOfDamage = 0f;

        [Tooltip("В каком радиусе ВОКРУГ ЦЕЛИ искать союзников, которые её бьют.")]
        public float allySearchRadius = 6f;

        [Tooltip("Отбор союзников для лечения (свои/союзники). Штатный Unit Selector выше отвечает за ЦЕЛЬ каста (врага).")]
        public UnitSelector allySelector;

        [Tooltip("Лечить только тех, кто реально бьёт эту цель (Unit.target совпадает). ВЫКЛ — лечить всех союзников в радиусе вокруг цели.")]
        public bool onlyThoseAttackingTarget = true;

        [Tooltip("Максимум вылеченных союзников. 0 — без ограничения.")]
        [Min(0)]
        public int maxHealed = 0;

        public override void Use(Unit castingUnit, int castingPlayer, int level, Unit unit)
        {
            if (NetworkConnectionHandler.isClient) return; // правило 6
            if (unit == null || unit.dead) return;

            if (damageType == null)
            {
                Debug.LogWarning("[HolyFire] Не задан Damage Type в ассете умения — каст пропущен.");
                return;
            }

            float dmg = LevelValue(damage, level, 0f);
            if (dmg > 0f) unit.GetDamage(dmg, damageType, castingPlayer, castingUnit, false, out float _);
            if (targetEffectors != null && targetEffectors.Length > 0) Effector.EffectorAdd(castingPlayer, unit, targetEffectors);

            float heal = LevelValue(healFlat, level, 0f) + dmg * healPercentOfDamage;
            if (heal <= 0f || allySearchRadius <= 0f) return;

            Vector2 center = new Vector2(unit.transform.position.x, unit.transform.position.z);
            Unit[] allies = Utils.GetUnitsInRadius(center, allySearchRadius, castingPlayer, allySelector, -1, unit);
            if (allies == null) return;

            int healed = 0;
            for (int i = 0; i < allies.Length; i++)
            {
                Unit a = allies[i];
                if (a == null || a.dead) continue;
                if (onlyThoseAttackingTarget && a.target != unit) continue;

                a.ChangeHP(heal);

                healed++;
                if (maxHealed > 0 && healed >= maxHealed) break;
            }

            RequestForceSync();
        }

    }
}
