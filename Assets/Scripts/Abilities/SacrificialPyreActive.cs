using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Ульта героя орков «Обряд Жертвенного Костра» (Active). Кастер — герой (heroUnit).
    // Герой мгновенно теряет selfCostPercent% ТЕКУЩЕГО ХП; на duration секунд все союзные орки (фильтр по
    // unitCategory) «горят»: аура auraDamagePerSecond маг-урона врагам вокруг каждого + сам орк теряет
    // selfBurnPerSecond ХП/сек; если горящий орк гибнет — детонация SkvernaExplosion (deathExplosionDamage по врагам).
    // Логика в per-unit бафе SacrificialPyreBuff. Взрыв — общий SkvernaExplosion (правило 5).
    // Ядро StrategyCore не трогаем (правило 1). Числа — в .asset (правило 3).
    // Сеть (правило 6): Use идёт на ВСЕХ пирах (VFX горения; урок FlameCloakActive) — урон/самосожжение/детонация
    // и −ХП героя гейтятся ВНУТРИ (if !isClient); AddVFX локален, по сети не реплицируется.
    [CreateAssetMenu(fileName = "SacrificialPyreActive", menuName = "StrategyCore/Abilities/SacrificialPyreActive")]
    public class SacrificialPyreActive : Ability
    {
        public override AbilityType type { get { return AbilityType.Active; } }

        [Header("Обряд Костра — жертва героя и цель")]
        [Tooltip("Сколько ПРОЦЕНТОВ текущего ХП мгновенно теряет герой при касте (по ТЗ 20 = 20%).")]
        public float selfCostPercent = 20f;
        [Tooltip("Категории союзных юнитов, которых поджигает обряд (по unitCategory). Пусто — любые союзники.")]
        public Unit.UnitCategory[] targetCategories;

        [Header("Горение — аура по врагам + самосожжение")]
        [Tooltip("Урон ауры горящего орка по врагам вокруг, в секунду (по ТЗ 15, магический).")]
        public float auraDamagePerSecond = 15f;
        [Tooltip("Тип урона ауры горения (магический).")]
        public DamageType auraDamageType;
        [Tooltip("Кого жжёт аура (обычно враги: Enemy + Unit + Ground/Water/Air). Также используется как цели детонации.")]
        public UnitSelector auraSelector;
        [Tooltip("Сколько ХП/сек теряет сам горящий орк (по ТЗ 5).")]
        public float selfBurnPerSecond = 5f;
        [Tooltip("VFX пламени; добавляется каждому горящему орку на время обряда.")]
        public VFXReferencer VFX;

        [Header("Детонация при смерти горящего")]
        [Tooltip("Урон по врагам при гибели горящего орка (по ТЗ 40).")]
        public float deathExplosionDamage = 40f;
        [Tooltip("Тип урона детонации при смерти. ТЗ не уточнил — задать в .asset.")]
        public DamageType deathExplosionDamageType;
        [Tooltip("Радиус детонации при смерти горящего (в юнитах Unity). ТЗ не задал — обязательно задать в .asset, иначе взрыва не будет.")]
        public float deathExplosionRadius = 0f;

        // База Ability:
        //  radius[level]  — добавка к радиусу ауры горения каждого орка (как у FlameCloak);
        //  duration[level]— длительность обряда (по ТЗ 6с);
        //  cooldown[level]— КД (по ТЗ 40с);
        //  unitSelector   — НЕ используется (враги — auraSelector; получатели — из MatchManager по категориям).

        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            // Use на всех пирах (VFX горения). Серверные эффекты гейтятся внутри бафа и здесь (−ХП героя).
            MatchManager mm = MatchManager.instance;
            if (mm == null) { Debug.LogWarning("[SacrificialPyre] MatchManager.instance == null."); return; }
            if (castingUnit == null || castingUnit.dead) { Debug.LogWarning("[SacrificialPyre] нет живого кастера-героя."); return; }

            float dur = (duration != null && duration.Length > level) ? duration[level] : 0f;
            if (dur <= 0f) { Debug.LogWarning($"[SacrificialPyre] duration[{level}] ≤ 0 — обряд не выполнен. Задай длительность в .asset."); return; }
            float auraRadius = (radius != null && radius.Length > level) ? radius[level] : 0f;

            // −selfCostPercent% текущего ХП героя — через общий хелпер B7 (правило 5). PayFromCaster гейтит сервер,
            // клампит/синкает. Поведение идентично прежнему прямому ChangeHP (регресс Обряда — в приёмку П2).
            if (selfCostPercent > 0f)
                PercentHpCost.PayFromCaster(castingUnit, selfCostPercent / 100f);

            List<Unit> teamUnits = mm.GetCommandUnitsForPlayer(castingPlayer);
            int affected = 0;
            for (int i = 0; i < teamUnits.Count; i++)
            {
                Unit u = teamUnits[i];
                if (u == null || u.dead) continue;
                if (!CategoryAllowed(u.unitCategory)) continue;
                SacrificialPyreBuff.Apply(u, this, level, castingUnit, auraRadius, dur);
                affected++;
            }
            Debug.Log($"[SacrificialPyre] герой={castingUnit.name} (-{selfCostPercent}% ХП) поджёг орков: {affected} " +
                      $"(аура={auraDamagePerSecond}/с, самосожжение={selfBurnPerSecond}/с, длит={dur}с).");
        }

        // Разрешена ли категория (пустой список = любые союзники).
        bool CategoryAllowed(Unit.UnitCategory c)
        {
            if (targetCategories == null || targetCategories.Length == 0) return true;
            for (int i = 0; i < targetCategories.Length; i++)
                if (targetCategories[i] == c) return true;
            return false;
        }
    }
}
