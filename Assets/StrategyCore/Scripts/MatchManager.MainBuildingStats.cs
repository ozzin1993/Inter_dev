using System;
using UnityEngine;

namespace StrategyCore
{
    // Партиал MatchManager: применение статов ГЗ (главного здания) по уровню (Фаза 2 плана «Улучшения ГЗ и техи»).
    //   • При смене уровня ГЗ (штатное событие OnMainBuildingLevelChanged) применяет статы уровня ДЕЛЬТОЙ
    //     через штатные Change*-методы Unit, РАЗДЕЛЬНО: HP/броня → mainBuilding (замок, условие победы),
    //     мана/реген → abilityCaster (объект-кастер способностей).
    //   • Максимумы (ChangeMaxHP/ChangeMaxMP/ChangeArmor/ChangeManaRegen) НЕ синкаются сами → применяем
    //     детерминированно на ОБЕИХ сторонах (событие приходит и на хост, и на клиента: MatchManager.TechUpgrade.cs).
    //   • Долечивание до нового максимума (SetHP) — серверо-авторитетно (синкает клиенту), правило 6.
    //   • Всё в Inspector (правило 3). Новый partial-файл — ассет StrategyCore не трогается (правило 1).
    //   • ДОПУЩЕНИЕ: база префаба замка = статам строки стартового уровня (по дизайну — ур.1). Иначе итог сместится
    //     на разницу; проверить в приёмке.

    [Serializable]
    public class MainBuildingLevelStats
    {
        [Tooltip("Максимальное здоровье замка на этом уровне ГЗ.")]
        public float maxHealth;
        [Tooltip("Броня замка на этом уровне ГЗ.")]
        public float armor;
        [Tooltip("Максимум маны кастера (пул центральных способностей) на этом уровне ГЗ.")]
        public float maxMana;
        [Tooltip("Реген маны кастера в секунду на этом уровне ГЗ.")]
        public float manaRegen;
    }

    public partial class MatchManager
    {
        [Header("Статы главного здания по уровню")]
        [SerializeField, Tooltip("Статы замка по уровням ГЗ (индекс 0 = уровень 1). По дизайну: " +
                                 "2500/5/100/1.5 · 3500/8/120/1.8 · 4800/12/150/2.2 · 6500/15/180/2.6 · 8500/20/200/3.0. " +
                                 "HP/броня применяются к замку (mainBuilding), мана/реген — к кастеру (abilityCaster); " +
                                 "базы их префабов должны соответствовать стартовому уровню.")]
        MainBuildingLevelStats[] mainBuildingStatsByLevel = new MainBuildingLevelStats[]
        {
            new MainBuildingLevelStats { maxHealth = 2500, armor = 5,  maxMana = 100, manaRegen = 1.5f },
            new MainBuildingLevelStats { maxHealth = 3500, armor = 8,  maxMana = 120, manaRegen = 1.8f },
            new MainBuildingLevelStats { maxHealth = 4800, armor = 12, maxMana = 150, manaRegen = 2.2f },
            new MainBuildingLevelStats { maxHealth = 6500, armor = 15, maxMana = 180, manaRegen = 2.6f },
            new MainBuildingLevelStats { maxHealth = 8500, armor = 20, maxMana = 200, manaRegen = 3.0f },
        };

        // Уровень, чьи статы уже применены к замку команды (для дельт). Индекс = команда. База = стартовый уровень ГЗ.
        readonly int[] appliedStatsLevel = new int[2];

        // Подписка на смену уровня ГЗ (событие OnMainBuildingLevelChanged, MatchManager.TechUpgrade.cs). Как HeroWire.
        void MainBuildingStatsWire()
        {
            appliedStatsLevel[0] = appliedStatsLevel[1] = Mathf.Max(1, startMainBuildingLevel);
            OnMainBuildingLevelChanged += ApplyMainBuildingStats;
        }

        void MainBuildingStatsUnwire()
        {
            OnMainBuildingLevelChanged -= ApplyMainBuildingStats;
        }

        /// <summary>
        /// Применить статы нового уровня ГЗ к abilityCaster команды дельтой от ранее применённого уровня.
        /// Максимумы — на обеих сторонах (не синкаются сами); долечивание HP — только сервер (SetHP синкает).
        /// Идемпотентно: повторный тот же уровень — no-op.
        /// </summary>
        void ApplyMainBuildingStats(int team)
        {
            if (team < 0 || team > 1) return;
            if (mainBuildingStatsByLevel == null || mainBuildingStatsByLevel.Length == 0) return;

            TeamWaveConfig cfg = (team == 0 ? teamA : teamB);
            if (cfg == null) return;
            Unit building = cfg.mainBuilding;   // HP/броня — на замке (условие победы, живучесть ГЗ)
            Unit caster = cfg.abilityCaster;    // мана/реген — на объекте-кастере (пул центральных способностей)
            if (building == null && caster == null) return;

            int newLevel = MainBuildingLevel(team);
            int prevLevel = appliedStatsLevel[team];
            if (newLevel == prevLevel) return;

            MainBuildingLevelStats to = StatsForLevel(newLevel);
            MainBuildingLevelStats from = StatsForLevel(prevLevel);

            // HP/броня — замку. Долечивание до нового максимума серверо-авторитетно (SetHP синкает клиенту).
            if (building != null)
            {
                building.ChangeMaxHP(to.maxHealth - from.maxHealth);
                building.ChangeArmor(to.armor - from.armor);
                if (!NetworkConnectionHandler.isClient)
                    building.SetHP(building.maxHealth);
            }

            // Мана/реген — объекту-кастеру (пул центральных способностей).
            if (caster != null)
            {
                caster.ChangeMaxMP(to.maxMana - from.maxMana);
                caster.ChangeManaRegen(to.manaRegen - from.manaRegen);
            }

            appliedStatsLevel[team] = newLevel;
        }

        // Статы для уровня ГЗ (1-based). Клампится в границы таблицы, чтобы не ловить выход за диапазон.
        MainBuildingLevelStats StatsForLevel(int level)
        {
            int idx = Mathf.Clamp(level - 1, 0, mainBuildingStatsByLevel.Length - 1);
            return mainBuildingStatsByLevel[idx];
        }
    }
}
