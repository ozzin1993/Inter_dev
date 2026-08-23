using System;
using UnityEngine;

namespace StrategyCore
{
    // ============================= ВЕТКИ ДУШ (модель данных) ==
    // Данные конфигурируются в FactionConfig.soulBranches (Inspector, per-faction). Ядро ассета не трогается:
    // разблокировка узла идёт штатным TechnologyManager.UnlockTech, а структура веток/тиров/опций и правила
    // (лимит веток, эксклюзив А/Б, последовательность тиров) — наши данные и серверная логика.

    /// <summary>Одна опция тира ветки (А или Б): технология + иконка для UI + стоимость в Душах.</summary>
    [Serializable]
    public class SoulBranchOption
    {
        [Tooltip("Технология этой опции (ScriptableObject из Resources/Technology). Разблокируется штатным " +
                 "TechnologyManager.UnlockTech. Пусто — опции нет (ячейка пустая).")]
        public Technology technology;

        [Tooltip("Иконка опции для UI. У Technology нет своего поля иконки, поэтому задаётся здесь (Texture2D).")]
        public Texture2D icon;

        [Tooltip("Стоимость покупки опции. Обычно один элемент — «Души» (по дизайну 50/150/400 на тир 1/2/3). " +
                 "Любые ресурсы. Пусто — бесплатно.")]
        public ResourceWrapper[] cost;
    }

    /// <summary>Один тир ветки: две взаимоисключающие опции А и Б. Игрок покупает РОВНО одну из них.</summary>
    [Serializable]
    public class SoulBranchTier
    {
        [Tooltip("Опция А этого тира.")]
        public SoulBranchOption optionA;
        [Tooltip("Опция Б этого тира. Взаимоисключающа с А: купив одну, вторую в этом тире купить уже нельзя.")]
        public SoulBranchOption optionB;
    }

    /// <summary>Одна ветка Душ (столбец панели). Тиры по возрастанию: элемент 0 = тир 1 (верхний ряд).</summary>
    [Serializable]
    public class SoulBranch
    {
        [Tooltip("Тиры ветки по возрастанию: элемент 0 = тир 1 (верхний ряд панели). Ожидается 3 тира. " +
                 "Тир N+1 доступен только если в тире N той же ветки куплена опция (А или Б).")]
        public SoulBranchTier[] tiers;
    }

    // ============================= ЛОГИКА ==
    /// <summary>
    /// Партиал MatchManager: ветки Душ Нежити («2 из 5» × 3 тира × выбор А/Б). Серверо-авторитетно (правило 6):
    /// покупка узла идёт только на сервере (клиент шлёт запрос через NetworkDataSync — шаг 3). Данные читаются
    /// из FactionConfig.soulBranches через ResolveFaction (детерминированно на всех пирах, как и весь конфиг).
    /// Разблокировка и её синк — штатный TechnologyManager.UnlockTech (+ событие OnTechUnlock перерисовывает UI).
    /// Оплата — штатные GameResources.CheckAmount/ChangeAmount (переиспользованы из MatchManager.TechUpgrade).
    /// Правила покупки: (1) лимит занятых веток soulBranchLimit; (2) эксклюзив А/Б внутри тира; (3) тир N+1
    /// требует купленный тир N той же ветки. Эффекты узлов здесь НЕ реализуются (N5) — только структура и гейт.
    /// </summary>
    public partial class MatchManager : MonoBehaviour
    {
        // ----- Доступ к данным (читается и на клиенте: конфиг детерминирован) -----

        // FactionConfig команды (0=A,1=B) через штатный резолв. null — команды/расы нет.
        FactionConfig SoulFactionOf(int team)
        {
            TeamWaveConfig cfg = Team(team);
            return cfg != null ? ResolveFaction(cfg.ownerPlayer) : null;
        }

        // Ветка по индексу у команды. null — вне диапазона / не настроено.
        SoulBranch SoulBranchAt(int team, int branch)
        {
            FactionConfig f = SoulFactionOf(team);
            if (f == null || f.soulBranches == null || branch < 0 || branch >= f.soulBranches.Length) return null;
            return f.soulBranches[branch];
        }

        // Опция (branch, tier, option: 0=A,1=B) команды. null — вне диапазона.
        SoulBranchOption SoulOptionAt(int team, int branch, int tier, int option)
        {
            SoulBranch br = SoulBranchAt(team, branch);
            if (br == null || br.tiers == null || tier < 0 || tier >= br.tiers.Length) return null;
            return OptionInTier(br.tiers[tier], option);
        }

        // Опция тира по индексу (0=A,1=B). null — тир пуст.
        static SoulBranchOption OptionInTier(SoulBranchTier tr, int option)
            => tr == null ? null : (option == 0 ? tr.optionA : tr.optionB);

        // ----- Публичные read-методы для UI (шаг 3) -----

        /// <summary>Число веток Душ у команды (столбцов панели). 0 — система веток не настроена (не Нежить).</summary>
        public int SoulBranchCount(int team)
        {
            FactionConfig f = SoulFactionOf(team);
            return (f != null && f.soulBranches != null) ? f.soulBranches.Length : 0;
        }

        /// <summary>Число тиров в ветке (рядов панели).</summary>
        public int SoulBranchTierCount(int team, int branch)
        {
            SoulBranch br = SoulBranchAt(team, branch);
            return (br != null && br.tiers != null) ? br.tiers.Length : 0;
        }

        /// <summary>Лимит занятых веток команды (дизайн: 2). 0 — без лимита.</summary>
        public int SoulBranchLimit(int team)
        {
            FactionConfig f = SoulFactionOf(team);
            return f != null ? f.soulBranchLimit : 0;
        }

        /// <summary>Есть ли в узле (branch, tier, option) заданная технология.</summary>
        public bool SoulOptionExists(int team, int branch, int tier, int option)
        {
            SoulBranchOption opt = SoulOptionAt(team, branch, tier, option);
            return opt != null && opt.technology != null;
        }

        /// <summary>Иконка узла (branch, tier, option) для UI. null — узла/иконки нет.</summary>
        public Texture2D SoulOptionIcon(int team, int branch, int tier, int option)
        {
            SoulBranchOption opt = SoulOptionAt(team, branch, tier, option);
            return opt != null ? opt.icon : null;
        }

        /// <summary>Стоимость узла (branch, tier, option) для показа в UI. null — бесплатно/нет узла.</summary>
        public ResourceWrapper[] SoulOptionCost(int team, int branch, int tier, int option)
        {
            SoulBranchOption opt = SoulOptionAt(team, branch, tier, option);
            return opt != null ? opt.cost : null;
        }

        /// <summary>Куплена ли опция узла (читает штатный TechTree, синхронизированный ассетом).</summary>
        public bool IsSoulOptionUnlocked(int team, int branch, int tier, int option)
        {
            TeamWaveConfig cfg = Team(team);
            return SoulOptionUnlocked(cfg, SoulOptionAt(team, branch, tier, option));
        }

        /// <summary>
        /// Доступна ли опция к покупке (без проверки ресурсов): узел есть, не куплен, противоположная опция тира
        /// не куплена (эксклюзив А/Б), предыдущий тир ветки куплен (последовательность), и лимит веток не мешает
        /// (ветка уже занята ИЛИ есть свободный слот занятости).
        /// </summary>
        public bool IsSoulOptionUnlockable(int team, int branch, int tier, int option)
        {
            if (team != 0 && team != 1) return false;
            TeamWaveConfig cfg = Team(team);
            FactionConfig faction = cfg != null ? ResolveFaction(cfg.ownerPlayer) : null;
            SoulBranch[] branches = faction != null ? faction.soulBranches : null;
            if (branches == null || branch < 0 || branch >= branches.Length) return false;

            SoulBranch br = branches[branch];
            if (br == null || br.tiers == null || tier < 0 || tier >= br.tiers.Length) return false;
            SoulBranchTier tr = br.tiers[tier];
            SoulBranchOption opt = OptionInTier(tr, option);
            if (opt == null || opt.technology == null) return false;

            // Уже куплена — не предлагаем.
            if (SoulOptionUnlocked(cfg, opt)) return false;

            // Эксклюзив А/Б: противоположная опция этого тира не должна быть куплена.
            if (SoulOptionUnlocked(cfg, OptionInTier(tr, option == 0 ? 1 : 0))) return false;

            // Последовательность тиров: тир N+1 требует купленной опции тира N той же ветки.
            if (tier > 0 && !IsSoulTierPurchased(cfg, br, tier - 1)) return false;

            // Лимит веток: если ветка ещё не занята и число занятых достигло лимита — блок.
            int limit = faction.soulBranchLimit;
            if (limit > 0 && !SoulBranchOccupied(cfg, br) && OccupiedSoulBranchCount(cfg, branches) >= limit)
                return false;

            return true;
        }

        // ----- Внутренние проверки состояния -----

        // Куплена ли опция (есть теха и она разблокирована у владельца команды).
        bool SoulOptionUnlocked(TeamWaveConfig cfg, SoulBranchOption opt)
            => cfg != null && opt != null && opt.technology != null && TechUnlockedSafe(opt.technology, cfg.ownerPlayer);

        // Куплен ли тир (любая из опций А/Б).
        bool IsSoulTierPurchased(TeamWaveConfig cfg, SoulBranch br, int tier)
        {
            if (br == null || br.tiers == null || tier < 0 || tier >= br.tiers.Length) return false;
            SoulBranchTier tr = br.tiers[tier];
            if (tr == null) return false;
            return SoulOptionUnlocked(cfg, tr.optionA) || SoulOptionUnlocked(cfg, tr.optionB);
        }

        // Занята ли ветка (куплена хотя бы одна опция любого её тира).
        bool SoulBranchOccupied(TeamWaveConfig cfg, SoulBranch br)
        {
            if (br == null || br.tiers == null) return false;
            for (int t = 0; t < br.tiers.Length; t++)
                if (IsSoulTierPurchased(cfg, br, t)) return true;
            return false;
        }

        // Сколько веток команды уже заняты (для лимита).
        int OccupiedSoulBranchCount(TeamWaveConfig cfg, SoulBranch[] branches)
        {
            int n = 0;
            for (int b = 0; b < branches.Length; b++)
                if (SoulBranchOccupied(cfg, branches[b])) n++;
            return n;
        }

        // ----- Серверное действие (правило 6) -----

        /// <summary>
        /// Сервер: купить опцию узла (branch, tier, option) команды за Души с проверкой всех правил веток.
        /// Идемпотентно (как TryUnlockTech): уже купленная/недоступная опция → false. true при успехе.
        /// </summary>
        public bool TryUnlockSoulOption(int team, int branch, int tier, int option)
        {
            if (NetworkConnectionHandler.isClient) return false;
            if (!IsSoulOptionUnlockable(team, branch, tier, option))
            {
                return false;
            }

            TeamWaveConfig cfg = Team(team);
            FactionConfig faction = ResolveFaction(cfg.ownerPlayer);
            SoulBranchOption opt = OptionInTier(faction.soulBranches[branch].tiers[tier], option);

            // Технология должна быть в TechTree (лежать в Resources/Technology) — иначе штатный UnlockTech упадёт.
            TechnologyManager tm = TechnologyManager.instance;
            if (tm == null || tm.TechTree == null || cfg.ownerPlayer < 0 || cfg.ownerPlayer >= tm.TechTree.Length
                || !tm.TechTree[cfg.ownerPlayer].ContainsKey(opt.technology))
            {
                Debug.LogWarning($"[MatchManager] Ветка: технология '{opt.technology.name}' отсутствует в TechTree игрока " +
                                 $"{cfg.ownerPlayer} — положите ассет в Resources/Technology. Покупка пропущена.");
                return false;
            }

            if (!ResourcesEnough(cfg.ownerPlayer, opt.cost))
            {
                return false;
            }

            PayResources(cfg.ownerPlayer, opt.cost);
            tm.UnlockTech(opt.technology, cfg.ownerPlayer); // штатно: TechTree + синк клиентам + OnTechUnlock
            return true;
        }

        /// <summary>Хватает ли команде ресурсов (Душ) на опцию узла — для состояния UI. Пустая цена → да.</summary>
        public bool CanAffordSoulOption(int team, int branch, int tier, int option)
        {
            TeamWaveConfig cfg = Team(team);
            SoulBranchOption opt = SoulOptionAt(team, branch, tier, option);
            if (cfg == null || opt == null) return false;
            if (opt.cost == null || opt.cost.Length == 0) return true;
            return GameResources.instance != null && GameResources.instance.CheckAmount(cfg.ownerPlayer, opt.cost);
        }
    }
}
