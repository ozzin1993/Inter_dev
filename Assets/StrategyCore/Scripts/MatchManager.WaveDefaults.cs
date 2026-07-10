using System;
using UnityEngine;

namespace StrategyCore
{
    // Партиал MatchManager: дефолт-состав волны по уровню ГЗ (Фаза 2 плана «Улучшения ГЗ и техи»).
    //   • При смене уровня ГЗ (штатное событие OnMainBuildingLevelChanged) ставит команде СТАНДАРТНЫЙ состав волны
    //     для нового уровня; игрок дальше правит его лидерством (штатная система состава). Ап уровня перезаписывает
    //     состав на новый дефолт (решение Artsiom).
    //   • Серверо-авторитетно (правило 6): состав меняется ТОЛЬКО на сервере тем же путём, что и ручная правка
    //     (cfg.waveComposition + OnWaveCompositionChanged + BroadcastWaveComposition); клиент получает синком (ApplyWaveComposition).
    //   • Составы фракционно-специфичны (разные префабы у рас) → держим здесь, ключ — раса (ResolveFaction),
    //     БЕЗ правки FactionConfig.cs. Всё в Inspector (правило 3). Ассет StrategyCore не трогается (правило 1).

    [Serializable]
    public class WaveLevelDefault
    {
        [Tooltip("Стандартный состав волны для этого уровня ГЗ (юнит + количество).")]
        public WaveEntry[] entries;
    }

    [Serializable]
    public class FactionWaveDefaults
    {
        [Tooltip("Раса (FactionConfig), к которой относятся эти дефолт-составы по уровням.")]
        public FactionConfig faction;
        [Tooltip("Стандартный состав волны по уровням ГЗ (индекс 0 = уровень 1).")]
        public WaveLevelDefault[] byLevel;
    }

    public partial class MatchManager
    {
        [Header("Дефолт-состав волны по уровню ГЗ")]
        [SerializeField, Tooltip("Стандартные составы волны по расам и уровням ГЗ. При достижении уровня состав команды " +
                                 "ставится в дефолт этого уровня; игрок дальше правит лидерством. Пусто — состав по уровню не меняется.")]
        FactionWaveDefaults[] factionWaveDefaults;

        // Уровень, чей дефолт-состав уже применён к команде (идемпотентность: тот же уровень не перетирает правки игрока).
        readonly int[] appliedWaveLevel = new int[2];

        // Подписка на смену уровня ГЗ (событие OnMainBuildingLevelChanged, MatchManager.TechUpgrade.cs). Как HeroWire.
        void WaveDefaultsWire()
        {
            appliedWaveLevel[0] = appliedWaveLevel[1] = Mathf.Max(1, startMainBuildingLevel);
            OnMainBuildingLevelChanged += ApplyDefaultWaveComposition;
        }

        void WaveDefaultsUnwire()
        {
            OnMainBuildingLevelChanged -= ApplyDefaultWaveComposition;
        }

        /// <summary>
        /// Поставить команде стандартный состав волны нового уровня ГЗ. Только сервер: пишет cfg.waveComposition
        /// (глубокая копия дефолта) и рассылает клиенту штатным путём (OnWaveCompositionChanged + BroadcastWaveComposition).
        /// Идемпотентно: тот же уровень — no-op (не перетирает правки игрока лидерством).
        /// </summary>
        void ApplyDefaultWaveComposition(int team)
        {
            if (NetworkConnectionHandler.isClient) return; // состав серверо-авторитетен; клиент получит через Broadcast
            if (team < 0 || team > 1) return;

            TeamWaveConfig cfg = team == 0 ? teamA : teamB;
            if (cfg == null) return;

            int level = MainBuildingLevel(team);
            if (level == appliedWaveLevel[team]) return;

            WaveEntry[] def = ResolveWaveDefault(cfg.ownerPlayer, level);
            if (def == null) return; // нет дефолта для расы/уровня — состав не трогаем

            cfg.waveComposition = CloneWaveComposition(def);
            appliedWaveLevel[team] = level;

            OnWaveCompositionChanged?.Invoke(team);
            BroadcastWaveComposition(team);
        }

        // Дефолт-состав для расы игрока и уровня ГЗ (1-based). null — если раса/уровень не заданы в таблице.
        WaveEntry[] ResolveWaveDefault(int ownerPlayer, int level)
        {
            if (factionWaveDefaults == null || factionWaveDefaults.Length == 0) return null;

            FactionConfig faction = ResolveFaction(ownerPlayer);
            if (faction == null) return null;

            for (int i = 0; i < factionWaveDefaults.Length; i++)
            {
                FactionWaveDefaults fwd = factionWaveDefaults[i];
                if (fwd == null || fwd.faction != faction || fwd.byLevel == null || fwd.byLevel.Length == 0) continue;

                int idx = Mathf.Clamp(level - 1, 0, fwd.byLevel.Length - 1);
                return fwd.byLevel[idx] != null ? fwd.byLevel[idx].entries : null;
            }
            return null;
        }
    }
}
