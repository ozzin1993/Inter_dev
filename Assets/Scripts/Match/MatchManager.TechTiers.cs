using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ============================= ТЕХНОЛОГИИ 2.0 — ТИРЫ (серверные правила) ==
    // Партиал MatchManager: разблокировка дерева технологий по ТИРАМ (Технологии 2.0).
    // Тир = 3 ступени: (1) улучшение уровня → (2) большой выбор 1 из 2 → (3) специализация 1 из 2.
    // Невыбранные альтернативы (вариант и специализация) блокируются НАВСЕГДА в матче (эксклюзив по TechTree, без мутации).
    // Данные — TeamWaveConfig.techTiers (глубокий клон из FactionConfig.techTiers, см. MatchManager.Factions).
    //
    // Серверо-авторитетно (правило 6): покупка — только на сервере; клиент шлёт запрос одним ServerRpc
    // (NetworkDataSync.UnlockTechTierServerRpc → TryUnlockTierStep). Хост зовёт TryUnlockTierStep напрямую.
    // Разблокировка и синк — штатный TechnologyManager.UnlockTech (+ OnTechUnlock перерисовывает UI/контент).
    // Оплата и проверка TechTree — общие утилиты из MatchManager.TechUpgrade
    // (TechUnlockedSafe / ResourcesEnough / PayResources). Уровень ГЗ покупка узлов БОЛЬШЕ НЕ трогает
    // (целевая модель 2026-08-21) — он набирается опытом, см. MatchManager.Experience.
    //
    // Эффекты узлов — КОНТЕНТОМ (не здесь): юниты/умения/свопы лежат на самом узле (TechNode.unlock*) и
    // применяются пересчётом по OnTechUnlock (MatchManager.ContentUnlock.RecomputeUnlockedContent);
    // герой — резолвом heroUnlockTech/heroPrefab при покупке варианта-героя.
    // Технологии тиров разблокируются ПО ИГРОКУ (UnlockTech(tech, player)), а не через юнит — юнитовый
    // LockTeck их не лочит обратно (как в ветках Душ N4).

    /// <summary>Ступень покупки узла тира для единого серверного входа/RPC: 0=уровень, 1=большой выбор, 2=специализация.</summary>
    public enum TechTierStep { Level = 0, BigOption = 1, Specialization = 2 }

    public partial class MatchManager : MonoBehaviour
    {
        // ======================== ДОСТУП К ДАННЫМ (runtime-копия TeamWaveConfig.techTiers) ========================

        // Тир по индексу у команды. null — вне диапазона / не настроено.
        TechTier TierAt(int team, int tier)
        {
            TeamWaveConfig cfg = Team(team);
            if (cfg == null || cfg.techTiers == null || tier < 0 || tier >= cfg.techTiers.Length) return null;
            return cfg.techTiers[tier];
        }

        // Вариант (option: 0=A, 1=B) тира. null — тира/варианта нет.
        static TechBigOption OptionOf(TechTier t, int option)
            => t == null ? null : (option == 0 ? t.optionA : t.optionB);

        // Специализация (spec: 0=A, 1=B) варианта. null — варианта/спеца нет.
        static TechNode SpecOf(TechBigOption o, int spec)
            => o == null ? null : (spec == 0 ? o.specializationA : o.specializationB);

        // Куплен ли узел (есть теха и она разблокирована у владельца) — штатный TechTree, без мутации.
        bool NodeUnlocked(int ownerPlayer, TechNode node)
            => node != null && node.technology != null && TechUnlockedSafe(node.technology, ownerPlayer);

        // ======================== СОСТОЯНИЕ (для гейтов и UI) ========================

        /// <summary>Число тиров дерева технологий команды (для UI-панели). 0 — не настроено.</summary>
        public int TechTierCount(int team)
        {
            TeamWaveConfig cfg = Team(team);
            return (cfg != null && cfg.techTiers != null) ? cfg.techTiers.Length : 0;
        }

        /// <summary>Тир завершён: куплен уровень + один из вариантов + одна из его специализаций (гейт следующего тира, Т1).</summary>
        public bool TierCompleted(int team, int tier)
        {
            TeamWaveConfig cfg = Team(team);
            TechTier t = TierAt(team, tier);
            if (cfg == null || t == null) return false;
            if (!NodeUnlocked(cfg.ownerPlayer, t.levelUpgrade)) return false;
            return OptionCompleted(cfg.ownerPlayer, t.optionA) || OptionCompleted(cfg.ownerPlayer, t.optionB);
        }

        // Вариант завершён: сам куплен И куплена одна из его специализаций.
        bool OptionCompleted(int ownerPlayer, TechBigOption o)
        {
            if (o == null || !NodeUnlocked(ownerPlayer, o.node)) return false;
            return NodeUnlocked(ownerPlayer, o.specializationA) || NodeUnlocked(ownerPlayer, o.specializationB);
        }

        // ----- Ступень 1: улучшение уровня -----

        /// <summary>Есть ли у узла уровня тира заданная технология.</summary>
        public bool TierLevelExists(int team, int tier)
        {
            TechTier t = TierAt(team, tier);
            return t != null && t.levelUpgrade != null && t.levelUpgrade.technology != null;
        }

        /// <summary>Иконка узла уровня тира для UI. null — нет узла/иконки.</summary>
        public Texture2D TierLevelIcon(int team, int tier)
        {
            TechTier t = TierAt(team, tier);
            return (t != null && t.levelUpgrade != null) ? t.levelUpgrade.icon : null;
        }

        /// <summary>Куплен ли уровень тира.</summary>
        public bool IsTierLevelUnlocked(int team, int tier)
        {
            TeamWaveConfig cfg = Team(team);
            TechTier t = TierAt(team, tier);
            return cfg != null && t != null && NodeUnlocked(cfg.ownerPlayer, t.levelUpgrade);
        }

        /// <summary>Доступен ли уровень тира к покупке (без ресурсов): гейт Т1 (тир 0 или предыдущий завершён), не куплен, есть теха.</summary>
        public bool IsTierLevelUnlockable(int team, int tier)
        {
            if (team != 0 && team != 1) return false;
            TeamWaveConfig cfg = Team(team);
            TechTier t = TierAt(team, tier);
            if (cfg == null || t == null || t.levelUpgrade == null || t.levelUpgrade.technology == null) return false;
            if (NodeUnlocked(cfg.ownerPlayer, t.levelUpgrade)) return false;   // уже куплен
            if (tier > 0 && !TierCompleted(team, tier - 1)) return false;      // Т1: предыдущий тир завершён
            return true;
        }

        // ----- Ступень 2: большой выбор (вариант А/Б) -----

        /// <summary>Есть ли у большого выбора (option 0=A,1=B) заданная технология.</summary>
        public bool BigOptionExists(int team, int tier, int option)
        {
            TechBigOption o = OptionOf(TierAt(team, tier), option);
            return o != null && o.node != null && o.node.technology != null;
        }

        /// <summary>Иконка большого выбора для UI. null — нет узла/иконки.</summary>
        public Texture2D BigOptionIcon(int team, int tier, int option)
        {
            TechBigOption o = OptionOf(TierAt(team, tier), option);
            return (o != null && o.node != null) ? o.node.icon : null;
        }

        /// <summary>Куплен ли большой выбор (option 0=A,1=B).</summary>
        public bool IsBigOptionUnlocked(int team, int tier, int option)
        {
            TeamWaveConfig cfg = Team(team);
            TechBigOption o = OptionOf(TierAt(team, tier), option);
            return cfg != null && o != null && NodeUnlocked(cfg.ownerPlayer, o.node);
        }

        /// <summary>Доступен ли большой выбор к покупке: уровень тира куплен, противоположный вариант НЕ куплен (эксклюзив), сам не куплен, есть теха.</summary>
        public bool IsBigOptionUnlockable(int team, int tier, int option)
        {
            if (team != 0 && team != 1) return false;
            TeamWaveConfig cfg = Team(team);
            TechTier t = TierAt(team, tier);
            TechBigOption o = OptionOf(t, option);
            if (cfg == null || t == null || o == null || o.node == null || o.node.technology == null) return false;
            if (!NodeUnlocked(cfg.ownerPlayer, t.levelUpgrade)) return false;                         // сначала уровень тира
            if (NodeUnlocked(cfg.ownerPlayer, o.node)) return false;                                  // уже куплен
            if (NodeUnlocked(cfg.ownerPlayer, OptionOf(t, option == 0 ? 1 : 0)?.node)) return false;  // эксклюзив вариантов
            return true;
        }

        // ----- Ступень 3: специализация (спец А/Б выбранного варианта) -----

        /// <summary>Есть ли у специализации (option, spec: 0=A,1=B) заданная технология.</summary>
        public bool SpecExists(int team, int tier, int option, int spec)
        {
            TechNode s = SpecOf(OptionOf(TierAt(team, tier), option), spec);
            return s != null && s.technology != null;
        }

        /// <summary>Иконка специализации для UI. null — нет узла/иконки.</summary>
        public Texture2D SpecIcon(int team, int tier, int option, int spec)
        {
            TechNode s = SpecOf(OptionOf(TierAt(team, tier), option), spec);
            return s != null ? s.icon : null;
        }

        /// <summary>Куплена ли специализация.</summary>
        public bool IsSpecUnlocked(int team, int tier, int option, int spec)
        {
            TeamWaveConfig cfg = Team(team);
            TechNode s = SpecOf(OptionOf(TierAt(team, tier), option), spec);
            return cfg != null && NodeUnlocked(cfg.ownerPlayer, s);
        }

        /// <summary>Доступна ли специализация к покупке: соответствующий вариант куплен, противоположная спец НЕ куплена (эксклюзив), сама не куплена, есть теха.</summary>
        public bool IsSpecUnlockable(int team, int tier, int option, int spec)
        {
            if (team != 0 && team != 1) return false;
            TeamWaveConfig cfg = Team(team);
            TechBigOption o = OptionOf(TierAt(team, tier), option);
            TechNode s = SpecOf(o, spec);
            if (cfg == null || o == null || s == null || s.technology == null) return false;
            if (!NodeUnlocked(cfg.ownerPlayer, o.node)) return false;                        // сначала куплен вариант
            if (NodeUnlocked(cfg.ownerPlayer, s)) return false;                              // уже куплена
            if (NodeUnlocked(cfg.ownerPlayer, SpecOf(o, spec == 0 ? 1 : 0))) return false;   // эксклюзив спецух
            return true;
        }

        // ----- Тексты узлов для UI (штатные поля Technology) -----
        // Панель не лезет в данные сама: доступ к дереву остаётся в одном месте (правило 5), UI получает готовую строку.

        static string NodeTitle(TechNode node)
            => node != null && node.technology != null ? (node.technology.displayName ?? string.Empty) : string.Empty;

        static string NodeDescription(TechNode node)
            => node != null && node.technology != null ? (node.technology.description ?? string.Empty) : string.Empty;

        /// <summary>Название большого выбора (option 0=A,1=B). Пусто — узла/технологии нет.</summary>
        public string BigOptionTitle(int team, int tier, int option)
            => NodeTitle(OptionOf(TierAt(team, tier), option)?.node);

        /// <summary>Описание большого выбора (Technology.description). Пусто — описания нет.</summary>
        public string BigOptionDescription(int team, int tier, int option)
            => NodeDescription(OptionOf(TierAt(team, tier), option)?.node);

        /// <summary>Название специализации. Пусто — узла/технологии нет.</summary>
        public string SpecTitle(int team, int tier, int option, int spec)
            => NodeTitle(SpecOf(OptionOf(TierAt(team, tier), option), spec));

        /// <summary>Описание специализации (Technology.description). Пусто — описания нет.</summary>
        public string SpecDescription(int team, int tier, int option, int spec)
            => NodeDescription(SpecOf(OptionOf(TierAt(team, tier), option), spec));

        // ======================== СЕРВЕРНЫЕ ДЕЙСТВИЯ (правило 6) ========================

        /// <summary>
        /// Единый серверный вход покупки узла тира (одна точка для хоста и клиентского RPC, правило 5).
        /// step: 0=уровень, 1=большой выбор, 2=специализация. option/spec: 0=A, 1=B (для своих ступеней). true при успехе.
        /// </summary>
        public bool TryUnlockTierStep(int team, int tier, int step, int option, int spec)
        {
            if (NetworkConnectionHandler.isClient) return false;
            switch ((TechTierStep)step)
            {
                case TechTierStep.Level:          return TryUnlockTierLevel(team, tier);
                case TechTierStep.BigOption:      return TryUnlockBigOption(team, tier, option);
                case TechTierStep.Specialization: return TryUnlockSpecialization(team, tier, option, spec);
                default:
                    Debug.LogWarning($"[MatchManager] TryUnlockTierStep: неизвестная ступень {step} (команда {team}, тир {tier}).");
                    return false;
            }
        }

        // Ступень 1 — улучшение уровня тира: гейт Т1, оплата, UnlockTech.
        // Уровень ГЗ покупка БОЛЬШЕ НЕ поднимает (целевая модель 2026-08-21): уровень набирается опытом
        // (MatchManager.Experience) и сам открывает очередной тир. Прежний блок «инкремент → синк → событие»
        // переехал туда без изменений (RaiseMainBuildingLevel) — иначе уровень рос бы из двух источников сразу.
        bool TryUnlockTierLevel(int team, int tier)
        {
            if (NetworkConnectionHandler.isClient) return false;
            if (!IsTierLevelUnlockable(team, tier))
            {
                return false;
            }
            TeamWaveConfig cfg = Team(team);
            return PayAndUnlock(cfg, TierAt(team, tier).levelUpgrade, $"уровень тира {tier}");
        }

        // Ступень 2 — большой выбор: гейт (уровень куплен, эксклюзив), оплата, UnlockTech, резолв героя (вариант-герой).
        bool TryUnlockBigOption(int team, int tier, int option)
        {
            if (NetworkConnectionHandler.isClient) return false;
            if (!IsBigOptionUnlockable(team, tier, option))
            {
                return false;
            }
            TeamWaveConfig cfg = Team(team);
            TechBigOption o = OptionOf(TierAt(team, tier), option);
            if (!PayAndUnlock(cfg, o.node, $"большой выбор тира {tier}, вариант {option}")) return false;

            // Вариант-герой: перерезолвить тех-гейт и префаб героя команды (runtime, per-team). Существующий гейт
            // призыва (SummonHero проверяет heroUnlockTech) сработает без правок — тех только что разблокирован.
            // «За матч один герой» гарантируют эксклюзив вариантов + валидатор редактора («героев достижимо ≤1»).
            if (o.heroPrefab != null)
            {
                cfg.heroUnlockTech = o.node.technology;
                cfg.heroPrefab     = o.heroPrefab;
            }
            return true;
        }

        /// <summary>
        /// Атомарная покупка «большой выбор + специализация» одним вызовом — для карточки выбора в UI.
        /// Обе ступени проверяются ДО первого списания: если специализация не пройдёт гейт или не хватит
        /// суммарной цены, ВАРИАНТ НЕ ПОКУПАЕТСЯ. Иначе игрок терял бы противоположный путь навсегда
        /// (эксклюзив А/Б необратим), не получив специализацию, а тир оставался бы незавершённым.
        /// Пошаговый TryUnlockTierStep остаётся для ступени уровня и прежнего RPC.
        /// </summary>
        public bool TryUnlockBigOptionWithSpec(int team, int tier, int option, int spec)
        {
            if (NetworkConnectionHandler.isClient) return false;

            TeamWaveConfig cfg = Team(team);
            TechBigOption o = OptionOf(TierAt(team, tier), option);
            TechNode specNode = SpecOf(o, spec);

            if (!IsBigOptionUnlockable(team, tier, option))
            {
                return false;
            }

            // Гейт специализации проверяем до покупки варианта, поэтому НЕ через IsSpecUnlockable
            // (тот требует уже купленного варианта): нужны сам узел и отсутствие обеих специализаций.
            if (cfg == null || specNode == null || specNode.technology == null
                || NodeUnlocked(cfg.ownerPlayer, specNode)
                || NodeUnlocked(cfg.ownerPlayer, SpecOf(o, spec == 0 ? 1 : 0)))
            {
                return false;
            }

            // Остальные предпосылки PayAndUnlock — тоже ДО первого списания, иначе вторая ступень могла бы
            // отвалиться после покупки первой (найдено ревью 2026-07-25): технология вне Resources/Technology
            // или пустой элемент в цене — и игрок теряет противоположный путь навсегда без специализации.
            if (!NodePurchasable(cfg, o.node) || !NodePurchasable(cfg, specNode))
            {
                Debug.LogWarning($"[MatchManager] Карточка выбора [{team}/{tier}/{option}/{spec}] отклонена: технология варианта или " +
                                 "специализации отсутствует в TechTree (положите ассет в Resources/Technology) либо её цена задана неполно.");
                return false;
            }

            // Суммарная цена обеих ступеней — до первого списания (иначе половинчатая покупка).
            if (!ResourcesEnough(cfg.ownerPlayer, MergeCosts(o.node.cost, specNode.cost)))
            {
                return false;
            }

            if (!TryUnlockBigOption(team, tier, option)) return false;
            if (!TryUnlockSpecialization(team, tier, option, spec))
            {
                // Сюда попасть не должны: гейты и суммарная цена проверены выше. Если попали — данные разъехались.
                Debug.LogError($"[MatchManager] Специализация [{team}/{tier}/{option}/{spec}] не купилась после варианта — " +
                               "тир остался незавершённым. Проверьте дерево технологий команды.");
                return false;
            }
            return true;
        }

        // Можно ли узел вообще купить: технология есть в TechTree игрока и цена задана корректно.
        // Повторяет предпроверки PayAndUnlock, чтобы атомарная покупка могла отказать ДО первого списания.
        bool NodePurchasable(TeamWaveConfig cfg, TechNode node)
        {
            if (cfg == null || node == null || node.technology == null) return false;

            TechnologyManager tm = TechnologyManager.Instance;
            if (tm == null || tm.TechTree == null || cfg.ownerPlayer < 0 || cfg.ownerPlayer >= tm.TechTree.Length) return false;
            if (!tm.TechTree[cfg.ownerPlayer].ContainsKey(node.technology)) return false;

            return CostValid(node.cost);
        }

        // Цена корректна, если каждый её элемент заполнен: штатный CheckAmount на пустом элементе падает.
        static bool CostValid(ResourceWrapper[] cost)
        {
            if (cost == null) return true;
            for (int i = 0; i < cost.Length; i++)
                if (cost[i] == null || cost[i].type == null) return false;
            return true;
        }

        // Слияние двух списков цены с СУММИРОВАНИЕМ по одному ресурсу: штатный CheckAmount сверяет каждый
        // элемент массива отдельно, и без слияния две цены по 30 золота прошли бы при балансе 40.
        static ResourceWrapper[] MergeCosts(ResourceWrapper[] a, ResourceWrapper[] b)
        {
            List<ResourceWrapper> merged = new List<ResourceWrapper>();
            AddCosts(merged, a);
            AddCosts(merged, b);
            return merged.ToArray();
        }

        static void AddCosts(List<ResourceWrapper> merged, ResourceWrapper[] cost)
        {
            if (cost == null) return;
            for (int i = 0; i < cost.Length; i++)
            {
                if (cost[i] == null || cost[i].type == null) continue;

                bool found = false;
                for (int j = 0; j < merged.Count; j++)
                {
                    if (merged[j].type != cost[i].type) continue;
                    merged[j].value += cost[i].value;
                    found = true;
                    break;
                }
                if (!found) merged.Add(new ResourceWrapper(cost[i].type, cost[i].value));   // копия: ассет не мутируем
            }
        }

        // Ступень 3 — специализация: гейт (вариант куплен, эксклюзив спецух), оплата, UnlockTech.
        bool TryUnlockSpecialization(int team, int tier, int option, int spec)
        {
            if (NetworkConnectionHandler.isClient) return false;
            if (!IsSpecUnlockable(team, tier, option, spec))
            {
                return false;
            }
            TeamWaveConfig cfg = Team(team);
            TechNode node = SpecOf(OptionOf(TierAt(team, tier), option), spec);
            return PayAndUnlock(cfg, node, $"специализация тира {tier}, вариант {option}, спец {spec}");
        }

        // Общая оплата+разблокировка узла: проверка наличия техи в TechTree (в Resources), достаточности ресурсов,
        // списание, штатный UnlockTech. false без списания, если техи нет в TechTree или ресурсов не хватает.
        // Идемпотентность (не купить дважды) — на вызывающей стороне (Is*Unlockable уже отсекли купленное).
        bool PayAndUnlock(TeamWaveConfig cfg, TechNode node, string what)
        {
            if (cfg == null || node == null || node.technology == null) return false;

            TechnologyManager tm = TechnologyManager.Instance;
            if (tm == null || tm.TechTree == null || cfg.ownerPlayer < 0 || cfg.ownerPlayer >= tm.TechTree.Length
                || !tm.TechTree[cfg.ownerPlayer].ContainsKey(node.technology))
            {
                Debug.LogWarning($"[MatchManager] Технология '{node.technology.name}' ({what}) отсутствует в TechTree игрока " +
                                 $"{cfg.ownerPlayer} — положите ассет в Resources/Technology. Разблокировка пропущена.");
                return false;
            }
            if (!ResourcesEnough(cfg.ownerPlayer, node.cost))
            {
                return false;
            }
            PayResources(cfg.ownerPlayer, node.cost);
            tm.UnlockTech(node.technology, cfg.ownerPlayer); // штатно: TechTree + синк клиентам + OnTechUnlock
            return true;
        }
    }
}
