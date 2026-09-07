using System;
using UnityEngine;

namespace StrategyCore
{
    // ============================= УРОВЕНЬ ГЗ + ОБЩИЕ УТИЛИТЫ ТЕХОВ (партиал MatchManager) ==
    // Счётчик уровня главного здания (ГЗ) команды и общие серверные утилиты разблокировки/оплаты,
    // переиспользуемые деревом тиров (MatchManager.TechTiers) и ветками Душ (MatchManager.BranchTechs).
    // Серверо-авторитетно (правило 6): уровень ГЗ поднимается только на сервере (по набранному опыту —
    // MatchManager.Experience) и синхронизируется клиентам (его читают статы/облик/контент по уровню).
    // Разблокировка техов — штатным TechnologyManager.UnlockTech; ядро ассета не трогается (правило 1).
    // История: прежний рядный гейт «ветки × уровни» (открывашки 1-3-1-3) и его классы данных дерева снесены при
    // переходе на Технологии 2.0 (тиры) — 2026-07-21.
    public partial class MatchManager : MonoBehaviour
    {
        // Текущий уровень ГЗ каждой команды — счётчик: старт = startMainBuildingLevel, растёт по НАБРАННОМУ
        // ОПЫТУ (MatchManager.Experience). Читают статы/облик/контент по уровню.
        // Серверо-авторитетно; на клиент приходит через ApplyMainBuildingLevel. Вошедшему в середине матча
        // уровень досылается отдельно (Б11: MatchManager.ResendMatchStateTo).
        readonly int[] mainBuildingLevel = new int[2];

        [Tooltip("Стартовый уровень ГЗ каждой команды на старте матча (по умолчанию 0). Ноль означает: замок " +
                 "стоит таким, каким его задаёт заготовка, и ни один тир дерева технологий ещё не открыт — " +
                 "первый уровень набирается опытом (MatchManager.Experience). Инициализируется детерминированно " +
                 "на всех пирах в WireContentTriggers (InitStartingMainBuildingLevel).")]
        [SerializeField] int startMainBuildingLevel = 0;

        /// <summary>Уровень ГЗ команды изменился (0=A,1=B). Подписчики: статы/облик/контент по уровню, UI.</summary>
        public event Action<int> OnMainBuildingLevelChanged;

        /// <summary>Текущий уровень ГЗ команды (0=A,1=B). 0 — базовый.</summary>
        public int MainBuildingLevel(int team) => (team == 0 || team == 1) ? mainBuildingLevel[team] : 0;

        /// <summary>Клиент: применить присланный сервером уровень ГЗ команды (гейтинг/статы/облик UI).</summary>
        public void ApplyMainBuildingLevel(int team, int level)
        {
            if (team != 0 && team != 1) return;
            mainBuildingLevel[team] = level;
            OnMainBuildingLevelChanged?.Invoke(team);
        }

        // Сервер: разослать клиентам новый уровень ГЗ команды.
        void BroadcastMainBuildingLevel(int team)
        {
            if (NetworkDataSync.Instance == null) return;
            NetworkDataSync.Instance.MainBuildingLevelSend(team, mainBuildingLevel[team]);
        }

        /// <summary>
        /// Стартовая инициализация уровня ГЗ команд (startMainBuildingLevel). Вызывается из WireContentTriggers
        /// (OnGameStart) на всех пирах — детерминированно, отдельный синк не нужен. Событие на старте НЕ дёргаем
        /// (иначе двойной начальный RecomputeUnlockedContent).
        /// </summary>
        void InitStartingMainBuildingLevel()
        {
            int start = Mathf.Max(0, startMainBuildingLevel);
            mainBuildingLevel[0] = start;
            mainBuildingLevel[1] = start;
        }

        // ----- Общие утилиты разблокировки/оплаты (переиспользуют TechTiers и BranchTechs) -----

        // Безопасная проверка разблокировки: без исключения, если технологии нет в TechTree (не в Resources).
        bool TechUnlockedSafe(Technology tech, int player)
        {
            TechnologyManager tm = TechnologyManager.Instance;
            if (tm == null || tech == null || tm.TechTree == null) return false;
            if (player < 0 || player >= tm.TechTree.Length) return false;
            return tm.TechTree[player].TryGetValue(tech, out bool unlocked) && unlocked;
        }

        // Хватает ли игроку ресурсов на стоимость (штатная проверка). Пустая стоимость — да.
        bool ResourcesEnough(int player, ResourceWrapper[] cost)
        {
            if (cost == null || cost.Length == 0) return true;
            if (GameResources.Instance == null) return false;
            return GameResources.Instance.CheckAmount(player, cost);
        }

        // Списать стоимость с игрока (серверо-авторитетно, авто-синк клиентам). Пустая стоимость — ничего.
        void PayResources(int player, ResourceWrapper[] cost)
        {
            if (cost == null || cost.Length == 0 || GameResources.Instance == null) return;
            GameResources.Instance.ChangeAmount(player, cost, 1, true, true); // decrease=true, calledByServer=true
        }
    }
}
