using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ============================= СОСТАВ ЛИНИИ (партиал MatchManager) — кирпич B22 ==
    // Событийный учёт живого состава по категориям (Unit.UnitCategory) per-team. Счётчик ведётся по
    // OnUnitSpawned / OnUnitDeathServer (без перебора каждый тик). Ленивая инициализация EnsureLineCompositionWired
    // (без правки Awake): при первом обращении подписывается и пересчитывает уже живых. Потребитель — компонент
    // LineCompositionAura (порог N живых категории → аура-эффектор). Единый счётчик (правило 5).
    // Серверо-авторитетно (правило 6). Новый partial-файл — ассет StrategyCore не трогается (правило 1).
    public partial class MatchManager
    {
        // Живые по команде и категории: compositionCounts[team][category] = число живых. null до инициализации.
        Dictionary<Unit.UnitCategory, int>[] compositionCounts;
        bool lineCompositionWired;

        /// <summary>
        /// Лениво включить счётчик состава (идемпотентно): подписка на спавн/смерть + пересчёт уже живых.
        /// Вызывается из LineCompositionAura (первый носитель ауры). Не требует правки Awake — событие смерти
        /// (OnUnitDeathServer) уже проведено DeathEvents, OnUnitSpawned — штатное событие менеджера.
        /// </summary>
        public void EnsureLineCompositionWired()
        {
            if (lineCompositionWired) return;
            lineCompositionWired = true;
            compositionCounts = new Dictionary<Unit.UnitCategory, int>[2]
            {
                new Dictionary<Unit.UnitCategory, int>(),
                new Dictionary<Unit.UnitCategory, int>()
            };

            // Пересчёт уже живых боевых юнитов (учесть заспавненных ДО подписки): обычные (teamUnits) +
            // призванные-не-слушающие (summonedUnits — их нет в teamUnits). Слушающие призванные уже в teamUnits.
            for (int t = 0; t < 2; t++)
            {
                BumpAlive(t, teamUnits != null ? teamUnits[t] : null);
                BumpAlive(t, summonedUnits != null ? summonedUnits[t] : null);
            }

            OnUnitSpawned += OnCompositionUnitSpawned;
            OnUnitDeathServer += OnCompositionUnitDied;
        }

        void OnCompositionUnitSpawned(int team, Unit u)
        {
            if (NetworkConnectionHandler.isClient || u == null || team < 0 || team > 1) return;
            Bump(team, u.unitCategory, +1);
        }

        void OnCompositionUnitDied(Unit victim, int killerPlayer, Unit killerUnit, bool rewards)
        {
            if (NetworkConnectionHandler.isClient || victim == null) return;
            int team = TeamIndexOfOwner(victim.owner);
            if (team < 0 || team > 1) return;
            Bump(team, victim.unitCategory, -1);
        }

        // Изменить счётчик категории команды на delta (не опускается ниже 0).
        void Bump(int team, Unit.UnitCategory cat, int delta)
        {
            if (compositionCounts == null) return;
            Dictionary<Unit.UnitCategory, int> d = compositionCounts[team];
            d.TryGetValue(cat, out int n);
            d[cat] = Mathf.Max(0, n + delta);
        }

        // Учесть живых из списка в счётчике (для стартового пересчёта).
        void BumpAlive(int team, List<Unit> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && !list[i].dead)
                    Bump(team, list[i].unitCategory, +1);
        }

        /// <summary>Число живых юнитов категории cat у команды team (событийный счётчик, сервер). 0 до инициализации.</summary>
        public int AliveOfCategory(int team, Unit.UnitCategory cat)
        {
            if (compositionCounts == null || team < 0 || team > 1) return 0;
            return compositionCounts[team].TryGetValue(cat, out int n) ? n : 0;
        }

        /// <summary>Число живых юнитов категории cat у команды игрока player. Обёртка для компонентов вне
        /// MatchManager (team по владельцу через приватный TeamIndexOfOwner, доступный в этом партиале).</summary>
        public int AliveOfCategoryForOwner(int player, Unit.UnitCategory cat)
        {
            int team = TeamIndexOfOwner(player);
            return team < 0 ? 0 : AliveOfCategory(team, cat);
        }
    }
}
