using System;
using System.IO;
using UnityEngine;

namespace StrategyCore
{
    // [Interflow 2026-08-01] «Конфиги вне билда»: реестр фракций из StreamingAssets/ServerConfigs/factions.json.
    // Файл лежит ОТКРЫТЫМ ТЕКСТОМ рядом с билдом (<Билд>_Data/StreamingAssets/ServerConfigs/factions.json) —
    // баланс правится на живом сервере без пересборки. В РЕДАКТОРЕ реестр выключен (источник — SO-ассеты,
    // обычная итерация через Interflow Editor); в БИЛДАХ — приоритетен (MatchManager.ResolveFaction).
    // Экспорт файла: Tools → Interflow → «Экспорт серверных конфигов фракций».
    public static class ServerFactionConfigs
    {
        static bool initialized;
        static FactionConfig[] configs;
        static int missing; // счётчик неотрезолвленных ссылок для сводки

        /// Конфиг расы по индексу (индексация = GameManager.factionData = SlotManager.playerFaction).
        public static bool TryGet(int factionIndex, out FactionConfig config)
        {
            config = null;
            if (Application.isEditor) return false; // редактор живёт на SO напрямую
            if (!initialized) LoadFile();
            if (configs == null || factionIndex < 0 || factionIndex >= configs.Length) return false;
            config = configs[factionIndex];
            return config != null;
        }

        /// Число фракций во внешнем файле (0 — файла нет или редактор). Триггерит ленивую загрузку.
        public static int Count
        {
            get
            {
                if (Application.isEditor) return 0;
                if (!initialized) LoadFile();
                return configs != null ? configs.Length : 0;
            }
        }

        static void LoadFile()
        {
            initialized = true;
            string path = Path.Combine(Application.streamingAssetsPath, "ServerConfigs/factions.json");
            try
            {
                if (!File.Exists(path))
                {
                    Debug.Log($"[ServerFactionConfigs] Файл не найден ({path}) — используются запечённые конфиги.");
                    return;
                }
                FactionsFileDto file = JsonUtility.FromJson<FactionsFileDto>(File.ReadAllText(path));
                if (file == null || file.factions == null || file.factions.Length == 0)
                {
                    Debug.LogWarning("[ServerFactionConfigs] Файл пуст/не разобран — используются запечённые конфиги.");
                    return;
                }
                configs = new FactionConfig[file.factions.Length];
                string namesLog = "";
                for (int i = 0; i < file.factions.Length; i++)
                {
                    configs[i] = Build(file.factions[i]);
                    namesLog += (i > 0 ? ", " : "") + file.factions[i].name;
                }
                Debug.Log($"[ServerFactionConfigs] Загружено фракций: {configs.Length} ({namesLog}); неотрезолвленных ссылок: {missing}.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ServerFactionConfigs] Ошибка чтения {path}: {e.Message} — используются запечённые конфиги.");
                configs = null;
            }
        }

        // ---------- резолв ссылок (пути относительно Resources) ----------

        static T Res<T>(string p) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(p)) return null;
            T o = Resources.Load<T>(p);
            if (o == null) { missing++; Debug.LogWarning($"[ServerFactionConfigs] Не найден ассет Resources/{p} ({typeof(T).Name})."); }
            return o;
        }

        static ResourceWrapper[] Cost(CostDto[] src)
        {
            if (src == null) return new ResourceWrapper[0];
            var dst = new ResourceWrapper[src.Length];
            for (int i = 0; i < src.Length; i++)
                dst[i] = new ResourceWrapper { type = Res<Resource>(src[i].type), value = src[i].value };
            return dst;
        }

        static bool NodeEmpty(TechNodeDto d) =>
            d == null || (string.IsNullOrEmpty(d.technology)
                          && (d.unlockUnits == null || d.unlockUnits.Length == 0)
                          && (d.unlockAbilities == null || d.unlockAbilities.Length == 0)
                          && (d.unitSwaps == null || d.unitSwaps.Length == 0)
                          && (d.towerSwaps == null || d.towerSwaps.Length == 0));

        static TechNode Node(TechNodeDto d)
        {
            if (NodeEmpty(d)) return null; // JsonUtility не отличает null от пустого объекта — пустой узел трактуем как отсутствие
            var n = new TechNode
            {
                technology = Res<Technology>(d.technology),
                icon = null, // сервер иконки не грузит
                cost = Cost(d.cost),
                unlockUnits = Units(d.unlockUnits),
                unlockAbilities = new AbilityUnlock[d.unlockAbilities != null ? d.unlockAbilities.Length : 0],
                unitSwaps = new UnitSwap[d.unitSwaps != null ? d.unitSwaps.Length : 0],
                towerSwaps = new TowerSwap[d.towerSwaps != null ? d.towerSwaps.Length : 0],
            };
            for (int i = 0; i < n.unlockAbilities.Length; i++)
                n.unlockAbilities[i] = new AbilityUnlock { ability = Res<Ability>(d.unlockAbilities[i].ability), slot = d.unlockAbilities[i].slot };
            for (int i = 0; i < n.unitSwaps.Length; i++)
                n.unitSwaps[i] = new UnitSwap { from = Res<Unit>(d.unitSwaps[i].from), to = Res<Unit>(d.unitSwaps[i].to) };
            for (int i = 0; i < n.towerSwaps.Length; i++)
                n.towerSwaps[i] = new TowerSwap { pointKey = (PointKey)d.towerSwaps[i].pointKey, to = Res<Unit>(d.towerSwaps[i].to) };
            return n;
        }

        static Unit[] Units(string[] paths)
        {
            if (paths == null) return new Unit[0];
            var dst = new Unit[paths.Length];
            for (int i = 0; i < paths.Length; i++) dst[i] = Res<Unit>(paths[i]);
            return dst;
        }

        static TechBigOption Big(TechBigOptionDto d)
        {
            if (d == null) return null;
            return new TechBigOption
            {
                node = Node(d.node),
                specializationA = Node(d.specializationA),
                specializationB = Node(d.specializationB),
                heroPrefab = Res<Unit>(d.heroPrefab),
            };
        }

        static FactionConfig Build(FactionEntryDto e)
        {
            var c = ScriptableObject.CreateInstance<FactionConfig>();
            c.name = e.name + " (внешний)";
            FactionConfigDto d = e.config;
            if (d == null) return c;

            if (d.waveUnits != null)
            {
                c.waveUnits = new WaveUnitEntry[d.waveUnits.Length];
                for (int i = 0; i < d.waveUnits.Length; i++)
                    c.waveUnits[i] = new WaveUnitEntry { unit = Res<Unit>(d.waveUnits[i].unit), role = (WaveUnitRole)d.waveUnits[i].role, count = d.waveUnits[i].count };
            }
            c.baseWaveIncome = d.baseWaveIncome;

            c.centralAbilities = new System.Collections.Generic.List<Ability>();
            if (d.centralAbilities != null)
                foreach (string p in d.centralAbilities) c.centralAbilities.Add(Res<Ability>(p));

            c.heroPrefab = Res<Unit>(d.heroPrefab);
            c.heroUnlockTech = Res<Technology>(d.heroUnlockTech);

            if (d.techTiers != null)
            {
                c.techTiers = new TechTier[d.techTiers.Length];
                for (int i = 0; i < d.techTiers.Length; i++)
                    c.techTiers[i] = d.techTiers[i] == null ? null : new TechTier
                    {
                        levelUpgrade = Node(d.techTiers[i].levelUpgrade),
                        optionA = Big(d.techTiers[i].optionA),
                        optionB = Big(d.techTiers[i].optionB),
                    };
            }

            c.mainBuildingShapesByLevel = Units(d.mainBuildingShapesByLevel);
            c.centreTower = Res<Unit>(d.centreTower);
            c.defence1Tower = Res<Unit>(d.defence1Tower);
            c.defence2Tower = Res<Unit>(d.defence2Tower);

            if (d.costModifiers != null)
            {
                c.costModifiers = new CostModifierRule[d.costModifiers.Length];
                for (int i = 0; i < d.costModifiers.Length; i++)
                    c.costModifiers[i] = new CostModifierRule
                    {
                        triggerTech = Res<Technology>(d.costModifiers[i].triggerTech),
                        resource = Res<Resource>(d.costModifiers[i].resource),
                        percentReduction = d.costModifiers[i].percentReduction,
                    };
            }

            c.gravePrefab = Res<GameObject>(d.gravePrefab);
            c.graveLifetime = d.graveLifetime;

            c.soulsResource = Res<Resource>(d.soulsResource);
            c.soulsPerMinuteByMbLevel = d.soulsPerMinuteByMbLevel ?? new float[0];
            c.soulsPerTier = d.soulsPerTier ?? new int[0];

            if (d.soulBranches != null)
            {
                c.soulBranches = new SoulBranch[d.soulBranches.Length];
                for (int i = 0; i < d.soulBranches.Length; i++)
                {
                    var bt = d.soulBranches[i];
                    var branch = new SoulBranch { tiers = new SoulBranchTier[bt != null && bt.tiers != null ? bt.tiers.Length : 0] };
                    for (int t = 0; t < branch.tiers.Length; t++)
                    {
                        var td = bt.tiers[t];
                        branch.tiers[t] = new SoulBranchTier
                        {
                            optionA = td != null && td.optionA != null && !string.IsNullOrEmpty(td.optionA.technology)
                                ? new SoulBranchOption { technology = Res<Technology>(td.optionA.technology), cost = Cost(td.optionA.cost) } : new SoulBranchOption(),
                            optionB = td != null && td.optionB != null && !string.IsNullOrEmpty(td.optionB.technology)
                                ? new SoulBranchOption { technology = Res<Technology>(td.optionB.technology), cost = Cost(td.optionB.cost) } : new SoulBranchOption(),
                        };
                    }
                    c.soulBranches[i] = branch;
                }
            }
            c.soulBranchLimit = d.soulBranchLimit;

            c.skvernaStartRadius = d.skvernaStartRadius;
            c.skvernaGrowthPerSecond = d.skvernaGrowthPerSecond;
            c.skvernaMaxRadius = d.skvernaMaxRadius;

            if (d.exclusiveTechGroups != null)
            {
                c.exclusiveTechGroups = new TechGroup[d.exclusiveTechGroups.Length];
                for (int i = 0; i < d.exclusiveTechGroups.Length; i++)
                {
                    string[] tp = d.exclusiveTechGroups[i] != null ? d.exclusiveTechGroups[i].techs : null;
                    var g = new TechGroup { techs = new Technology[tp != null ? tp.Length : 0] };
                    for (int t = 0; t < g.techs.Length; t++) g.techs[t] = Res<Technology>(tp[t]);
                    c.exclusiveTechGroups[i] = g;
                }
            }

            if (d.lockedBySouzlessTechs != null)
            {
                c.lockedBySouzlessTechs = new Technology[d.lockedBySouzlessTechs.Length];
                for (int i = 0; i < d.lockedBySouzlessTechs.Length; i++) c.lockedBySouzlessTechs[i] = Res<Technology>(d.lockedBySouzlessTechs[i]);
            }

            return c;
        }
    }
}
