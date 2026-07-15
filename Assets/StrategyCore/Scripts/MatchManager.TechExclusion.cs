using UnityEngine;

namespace StrategyCore
{
    // ============================= ВЗАИМОИСКЛЮЧЕНИЕ ТЕХОВ (партиал MatchManager) — C3 «Без Союза» ==
    // Взаимоисключение технологий (пути А/Б) + блок «ветки синергии Союза». Данные — FactionConfig.exclusiveTechGroups
    // и lockedBySouzlessTechs (Inspector, per-faction; резолв ResolveFaction). Гейт-хук — в IsTechUnlockable
    // (MatchManager.TechUpgrade.cs): заблокированный тех недоступен и к покупке (TryUnlockTech), и в UI.
    // Эксклюзив ПОСТОЯННЫЙ (как ветки А/Б BranchTechs): проверка по штатному TechTree, БЕЗ мутации/лока техов —
    // RecomputeUnlockedContent/OnTechLock его не сбрасывает (правило 8). Метод read-only (безопасен и на клиенте
    // для UI); само действие покупки — серверное (правило 6). Ассет StrategyCore не трогается (правило 1).
    public partial class MatchManager
    {
        /// <summary>
        /// Разрешена ли технология tech к разблокировке у игрока по правилам C3.
        /// (1) exclusiveTechGroups: tech в группе, где уже разблокирован ДРУГОЙ тех → запрещено (пути А/Б).
        /// (2) lockedBySouzlessTechs: tech в списке синергии И игрок уже выбрал путь (разблокирован любой тех
        ///     из exclusiveTechGroups) → запрещено. true — ограничений нет / конфиг не задан.
        /// </summary>
        public bool IsTechAllowedByExclusion(Technology tech, int player)
        {
            if (tech == null) return true;
            FactionConfig f = ResolveFaction(player);
            if (f == null) return true;

            // (1) Взаимоисключающие группы (пути А/Б): один тех группы блокирует остальные навсегда.
            if (f.exclusiveTechGroups != null)
            {
                for (int g = 0; g < f.exclusiveTechGroups.Length; g++)
                {
                    TechGroup group = f.exclusiveTechGroups[g];
                    if (group == null || !TechArrayContains(group.techs, tech)) continue;   // tech не в этой группе
                    for (int i = 0; i < group.techs.Length; i++)
                    {
                        Technology other = group.techs[i];
                        if (other != null && other != tech && TechUnlockedSafe(other, player)) return false;
                    }
                }
            }

            // (2) Ветка синергии: блокируется, как только выбран любой путь (тех из exclusiveTechGroups куплен).
            if (TechArrayContains(f.lockedBySouzlessTechs, tech) && AnyExclusiveTechUnlocked(f, player))
                return false;

            return true;
        }

        // Разблокирован ли у игрока хоть один тех из любой взаимоисключающей группы (= путь выбран).
        bool AnyExclusiveTechUnlocked(FactionConfig f, int player)
        {
            if (f == null || f.exclusiveTechGroups == null) return false;
            for (int g = 0; g < f.exclusiveTechGroups.Length; g++)
            {
                TechGroup group = f.exclusiveTechGroups[g];
                if (group == null || group.techs == null) continue;
                for (int i = 0; i < group.techs.Length; i++)
                    if (group.techs[i] != null && TechUnlockedSafe(group.techs[i], player)) return true;
            }
            return false;
        }

        // Есть ли технология tech в массиве (сравнение по ссылке SO).
        static bool TechArrayContains(Technology[] arr, Technology tech)
        {
            if (arr == null || tech == null) return false;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] == tech) return true;
            return false;
        }
    }
}
