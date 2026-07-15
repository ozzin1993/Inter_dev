using System;
using UnityEngine;

namespace StrategyCore
{
    // ============================= КОНФИГ ТЕХНОЛОГИЙ / УЛУЧШЕНИЯ ГЗ ==
    // Настраивается в Inspector внутри TeamWaveConfig (per-team, см. MatchManager.cs). Ядро ассета не трогается:
    // разблокировка идёт штатным TechnologyManager.UnlockTech, а ветки/уровни/иконки/гейт/оплата — наши данные.

    /// <summary>Один узел дерева технологий: технология + иконка для UI + стоимость разблокировки.</summary>
    [Serializable]
    public class TechCell
    {
        [Tooltip("Технология этого узла (ScriptableObject из Resources/Technology). Разблокируется штатным TechnologyManager.UnlockTech.")]
        public Technology technology;

        [Tooltip("Иконка узла для UI. У Technology нет своего поля иконки, поэтому задаётся здесь (Texture2D).")]
        public Texture2D icon;

        [Tooltip("Стоимость разблокировки этого узла. Любые ресурсы (обычно золото). Пусто — бесплатно.")]
        public ResourceWrapper[] cost;
    }

    /// <summary>Одна ветка дерева (столбец таблицы). Уровни по возрастанию: элемент 0 = уровень 1 (верхний ряд).</summary>
    [Serializable]
    public class TechBranch
    {
        [Tooltip("Уровни ветки по возрастанию силы: элемент 0 = уровень 1 (верхний ряд таблицы), элемент 1 = уровень 2, и т.д. Ожидается 3 уровня.")]
        public TechCell[] levels;
    }

    /// <summary>Стоимость одного улучшения главного здания (до соответствующего уровня).</summary>
    [Serializable]
    public class UpgradeCost
    {
        [Tooltip("Стоимость улучшения главного здания до этого уровня. Любые ресурсы (обычно золото). Пусто — бесплатно.")]
        public ResourceWrapper[] cost;
    }

    // ============================= ЛОГИКА ==
    /// <summary>
    /// Партиал MatchManager: уровень главного здания (ГЗ) и разблокировка технологий с гейтингом.
    /// Серверо-авторитетно (правило 6): улучшение ГЗ и разблокировка техов идут только на сервере; клиент шлёт
    /// запрос через NetworkDataSync (см. NetworkDataSync.TechUpgrade.cs). Уровень ГЗ синхронизируется клиентам для
    /// гейтинга UI; состояние техов синхронит штатный TechnologyManager.TechnologySync (+ событие OnTechUnlock).
    /// Гейт: технология уровня L (1-based) доступна, если уровень ГЗ ≥ L И предыдущий уровень той же ветки разблокирован.
    /// Оплата — штатные GameResources.CheckAmount/ChangeAmount. Всё конфигурируется в Inspector (TeamWaveConfig).
    /// </summary>
    public partial class MatchManager : MonoBehaviour
    {
        // Текущий уровень главного здания каждой команды (0 = базовый, ни одна теха не доступна).
        // Серверо-авторитетно; на клиент приходит через ApplyMainBuildingLevel (для гейтинга UI).
        // Поздний вход клиента (mid-game) НЕ покрыт — как и владение точек (синхронизируется только живое изменение).
        readonly int[] mainBuildingLevel = new int[2];

        [Tooltip("Стартовый уровень ГЗ каждой команды на старте матча (по умолчанию 1: ряд 1 веток доступен сразу, " +
                 "число апгрейдов до максимума — на 1 меньше). Инициализируется детерминированно на всех пирах в WireContentTriggers.")]
        [SerializeField] int startMainBuildingLevel = 1;

        /// <summary>Уровень ГЗ команды изменился (параметр — индекс команды 0=A, 1=B). Для перерисовки UI.</summary>
        public event Action<int> OnMainBuildingLevelChanged;

        // ----- Доступ для UI (читается и на клиенте: уровень синхронизирован, техи синхронит ассет) -----

        /// <summary>Текущий уровень ГЗ команды (0=A,1=B). 0 — базовый.</summary>
        public int MainBuildingLevel(int team) => (team == 0 || team == 1) ? mainBuildingLevel[team] : 0;

        /// <summary>Максимальный уровень ГЗ команды = число настроенных стоимостей улучшения (и кнопок правой таблицы).</summary>
        public int MainBuildingMaxLevel(int team)
        {
            TeamWaveConfig cfg = Team(team);
            return (cfg != null && cfg.mainBuildingUpgradeCosts != null) ? cfg.mainBuildingUpgradeCosts.Length : 0;
        }

        /// <summary>Число веток технологий команды (столбцов таблицы).</summary>
        public int TechBranchCount(int team)
        {
            TeamWaveConfig cfg = Team(team);
            return (cfg != null && cfg.techBranches != null) ? cfg.techBranches.Length : 0;
        }

        /// <summary>Максимальное число уровней среди веток команды (рядов таблицы).</summary>
        public int TechLevelCount(int team)
        {
            TeamWaveConfig cfg = Team(team);
            if (cfg == null || cfg.techBranches == null) return 0;
            int max = 0;
            for (int i = 0; i < cfg.techBranches.Length; i++)
            {
                TechBranch br = cfg.techBranches[i];
                if (br != null && br.levels != null && br.levels.Length > max) max = br.levels.Length;
            }
            return max;
        }

        /// <summary>Иконка узла (branch, level) команды для UI. null — если узла/иконки нет.</summary>
        public Texture2D TechIcon(int team, int branch, int level)
        {
            TechCell cell = CellAt(Team(team), branch, level);
            return cell != null ? cell.icon : null;
        }

        /// <summary>Есть ли в узле (branch, level) заданная технология.</summary>
        public bool TechExists(int team, int branch, int level)
        {
            TechCell cell = CellAt(Team(team), branch, level);
            return cell != null && cell.technology != null;
        }

        /// <summary>Разблокирована ли технология узла у команды (читает штатный TechTree, синхронизированный ассетом).</summary>
        public bool IsTechUnlocked(int team, int branch, int level)
        {
            TeamWaveConfig cfg = Team(team);
            TechCell cell = CellAt(cfg, branch, level);
            if (cell == null || cell.technology == null || cfg == null) return false;
            return TechUnlockedSafe(cell.technology, cfg.ownerPlayer);
        }

        /// <summary>
        /// Доступен ли узел для разблокировки (без проверки ресурсов): не открыт, есть теха,
        /// уровень ГЗ ≥ (level+1) и предыдущий уровень той же ветки разблокирован (для level 0 — без требования).
        /// </summary>
        public bool IsTechUnlockable(int team, int branch, int level)
        {
            if (team != 0 && team != 1) return false;
            TeamWaveConfig cfg = Team(team);
            TechCell cell = CellAt(cfg, branch, level);
            if (cell == null || cell.technology == null) return false;
            if (IsTechUnlocked(team, branch, level)) return false;
            // Кап по уровню ГЗ: уровень техи (level+1) не выше уровня здания.
            if (mainBuildingLevel[team] < level + 1) return false;
            // Предыдущий НЕПУСТОЙ уровень ветки должен быть разблокирован; пустые ячейки прозрачны для гейта.
            // Идём вверх, пропуская ячейки без технологии; первая непустая выше должна быть открыта.
            // Все ячейки до верха пустые → требования по предыдущему уровню нет (ветка может начинаться не с ряда 1).
            for (int prev = level - 1; prev >= 0; prev--)
            {
                TechCell prevCell = CellAt(cfg, branch, prev);
                if (prevCell == null || prevCell.technology == null) continue; // пустая ячейка — прозрачна
                if (!IsTechUnlocked(team, branch, prev)) return false;         // первая непустая выше должна быть открыта
                break;                                                          // требование удовлетворено
            }

            // C3 «Без Союза»: взаимоисключение путей А/Б + блок ветки синергии (MatchManager.TechExclusion).
            // Покрывает и покупку (TryUnlockTech зовёт этот метод), и доступность в UI. Правка — с ОК Artsiom (§30).
            if (!IsTechAllowedByExclusion(cell.technology, cfg.ownerPlayer)) return false;

            return true;
        }

        // ----- Серверные действия (правило 6) -----

        /// <summary>
        /// Сервер: улучшить ГЗ команды на следующий уровень за ресурсы. expectedLevel — ожидаемый текущий уровень
        /// (идемпотентность против дубль/устаревших кликов клиента). true при успехе.
        /// </summary>
        public bool TryUpgradeMainBuilding(int team, int expectedLevel)
        {
            if (NetworkConnectionHandler.isClient) return false;
            if (team != 0 && team != 1) return false;
            TeamWaveConfig cfg = Team(team);
            if (cfg == null) return false;

            int cur = mainBuildingLevel[team];
            // Идемпотентность: запрос относится к конкретному уровню; устаревший/дублирующий клик отклоняем.
            if (cur != expectedLevel)
            {
                Debug.Log($"[MatchManager] Uluchshenie GZ komandy {team}: ozhidaemyy uroven {expectedLevel} != tekushchiy {cur} — propusk (dubl/ustarevshiy klik).");
                return false;
            }

            int max = MainBuildingMaxLevel(team);
            if (cur >= max)
            {
                Debug.Log($"[MatchManager] Uluchshenie GZ komandy {team}: uzhe maksimalnyy uroven ({cur}).");
                return false;
            }

            UpgradeCost slot = cfg.mainBuildingUpgradeCosts[cur];
            ResourceWrapper[] cost = slot != null ? slot.cost : null;
            if (!ResourcesEnough(cfg.ownerPlayer, cost))
            {
                Debug.Log($"[MatchManager] Uluchshenie GZ komandy {team}: ne khvataet resursov na uroven {cur + 1}.");
                return false;
            }

            PayResources(cfg.ownerPlayer, cost);
            mainBuildingLevel[team] = cur + 1;
            Debug.Log($"[MatchManager] GZ komandy {team} uluchsheno: uroven {cur} -> {cur + 1}.");

            // Мост «уровень ГЗ → тех»: авто-разблокировка скрытого теха достигнутого уровня (до рассылки/события,
            // чтобы OnTechUnlock и OnMainBuildingLevelChanged дали согласованный пересчёт контента).
            AutoUnlockMainBuildingLevelTech(cfg, team, cur + 1);

            BroadcastMainBuildingLevel(team);
            OnMainBuildingLevelChanged?.Invoke(team);
            return true;
        }

        /// <summary>Сервер: разблокировать технологию узла (branch, level) команды за ресурсы с проверкой гейта. true при успехе.</summary>
        public bool TryUnlockTech(int team, int branch, int level)
        {
            if (NetworkConnectionHandler.isClient) return false;
            if (!IsTechUnlockable(team, branch, level))
            {
                Debug.Log($"[MatchManager] Razblokirovka tekha [{team}/{branch}/{level}] otklonena: geyt ne proyden.");
                return false;
            }

            TeamWaveConfig cfg = Team(team);
            TechCell cell = CellAt(cfg, branch, level);
            if (cell == null || cell.technology == null) return false;

            // Технология должна быть в TechTree (т.е. лежать в Resources/Technology) — иначе штатный UnlockTech упадёт.
            TechnologyManager tm = TechnologyManager.instance;
            if (tm == null || tm.TechTree == null || cfg.ownerPlayer < 0 || cfg.ownerPlayer >= tm.TechTree.Length
                || !tm.TechTree[cfg.ownerPlayer].ContainsKey(cell.technology))
            {
                Debug.LogWarning($"[MatchManager] Tekhnologiya '{cell.technology.name}' net v TechTree igroka {cfg.ownerPlayer} — " +
                                 "polozhite asset v Resources/Technology. Razblokirovka propushchena.");
                return false;
            }

            if (!ResourcesEnough(cfg.ownerPlayer, cell.cost))
            {
                Debug.Log($"[MatchManager] Razblokirovka '{cell.technology.name}' komandy {team}: ne khvataet resursov.");
                return false;
            }

            PayResources(cfg.ownerPlayer, cell.cost);
            tm.UnlockTech(cell.technology, cfg.ownerPlayer); // штатно: ставит TechTree + синкает клиентам + OnTechUnlock
            Debug.Log($"[MatchManager] Razblokirovana tekhnologiya '{cell.technology.name}' komandy {team} (uzel {branch}/{level}).");
            return true;
        }

        /// <summary>Клиент: применить присланный сервером уровень ГЗ команды (для гейтинга UI).</summary>
        public void ApplyMainBuildingLevel(int team, int level)
        {
            if (team != 0 && team != 1) return;
            mainBuildingLevel[team] = level;
            OnMainBuildingLevelChanged?.Invoke(team);
        }

        // Сервер: разослать клиентам новый уровень ГЗ команды.
        void BroadcastMainBuildingLevel(int team)
        {
            if (NetworkDataSync.instance == null) return;
            NetworkDataSync.instance.MainBuildingLevelSend(team, mainBuildingLevel[team]);
        }

        /// <summary>
        /// Стартовая инициализация уровня ГЗ команд (startMainBuildingLevel). Вызывается из WireContentTriggers
        /// (OnGameStart) на всех пирах — клиент и сервер сходятся детерминированно, отдельный синк не нужен.
        /// Сервер дополнительно применяет пол уровня героя по стартовому уровню: SummonHero пол по ГЗ не выставляет,
        /// а событие OnMainBuildingLevelChanged на старте намеренно НЕ дёргаем (иначе двойной начальный RecomputeUnlockedContent).
        /// </summary>
        void InitStartingMainBuildingLevel()
        {
            int start = Mathf.Max(0, startMainBuildingLevel);
            mainBuildingLevel[0] = start;
            mainBuildingLevel[1] = start;

            if (NetworkConnectionHandler.isClient) return; // пол уровня героя — только сервер (правило 6)
            ApplyHeroLevelFloor(0);
            ApplyHeroLevelFloor(1);
        }

        // Мост «уровень ГЗ → тех»: авто-разблокировка скрытого теха достигнутого уровня ГЗ (MB_LVL_2…5).
        // index = уровень − 2 (уровень 2 → index 0 … уровень 5 → index 3). Уровень 1 / пустой элемент — пропуск.
        // Идемпотентно (правило 8): уже разблокированный тех не трогаем. Теха нет в Resources/TechTree — warning + пропуск.
        void AutoUnlockMainBuildingLevelTech(TeamWaveConfig cfg, int team, int reachedLevel)
        {
            if (NetworkConnectionHandler.isClient) return;       // разблокировка теха — только сервер (правило 6)
            if (cfg == null || cfg.mainBuildingLevelTechs == null) return;

            int idx = reachedLevel - 2;                          // уровень 2 → index 0
            if (idx < 0 || idx >= cfg.mainBuildingLevelTechs.Length) return;

            Technology tech = cfg.mainBuildingLevelTechs[idx];
            if (tech == null) return;                            // уровень без теха — норма
            if (TechUnlockedSafe(tech, cfg.ownerPlayer)) return; // уже разблокирован — идемпотентность

            TechnologyManager tm = TechnologyManager.instance;
            if (tm == null || tm.TechTree == null || cfg.ownerPlayer < 0 || cfg.ownerPlayer >= tm.TechTree.Length
                || !tm.TechTree[cfg.ownerPlayer].ContainsKey(tech))
            {
                Debug.LogWarning($"[MatchManager] Most GZ->tekh: skrytyy tekh '{tech.name}' urovnya GZ {reachedLevel} komandy {team} " +
                                 "net v TechTree — polozhite asset v Resources/Technology. Avto-razblokirovka propushchena.");
                return;
            }

            tm.UnlockTech(tech, cfg.ownerPlayer);                // штатно: TechTree + синк клиентам + OnTechUnlock
            Debug.Log($"[MatchManager] Most GZ->tekh: razblokirovan skrytyy tekh '{tech.name}' urovnya GZ {reachedLevel} komandy {team}.");
        }

        // ----- Вспомогательные -----

        // Узел (branch, level) конфига команды. null — если индексы вне диапазона.
        TechCell CellAt(TeamWaveConfig cfg, int branch, int level)
        {
            if (cfg == null || cfg.techBranches == null) return null;
            if (branch < 0 || branch >= cfg.techBranches.Length) return null;
            TechBranch br = cfg.techBranches[branch];
            if (br == null || br.levels == null) return null;
            if (level < 0 || level >= br.levels.Length) return null;
            return br.levels[level];
        }

        // Безопасная проверка разблокировки: без исключения, если технологии нет в TechTree (не в Resources).
        bool TechUnlockedSafe(Technology tech, int player)
        {
            TechnologyManager tm = TechnologyManager.instance;
            if (tm == null || tech == null || tm.TechTree == null) return false;
            if (player < 0 || player >= tm.TechTree.Length) return false;
            return tm.TechTree[player].TryGetValue(tech, out bool unlocked) && unlocked;
        }

        // Хватает ли игроку ресурсов на стоимость (штатная проверка). Пустая стоимость — да.
        bool ResourcesEnough(int player, ResourceWrapper[] cost)
        {
            if (cost == null || cost.Length == 0) return true;
            if (GameResources.instance == null) return false;
            return GameResources.instance.CheckAmount(player, cost);
        }

        // Списать стоимость с игрока (серверо-авторитетно, авто-синк клиентам). Пустая стоимость — ничего.
        void PayResources(int player, ResourceWrapper[] cost)
        {
            if (cost == null || cost.Length == 0 || GameResources.instance == null) return;
            GameResources.instance.ChangeAmount(player, cost, 1, true, true); // decrease=true, calledByServer=true
        }
    }
}
