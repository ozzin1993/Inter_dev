using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace StrategyCore
{
    // [Interflow 2026-08-01] Экспорт конфигов фракций во внешний серверный файл («конфиги вне билда»).
    // Источник — GameManager.prefab (factionData: имя расы + FactionConfig). Все ссылки на ассеты
    // пишутся путями относительно Resources; ассет вне Resources → предупреждение (сервер его не загрузит).
    // Файл попадает в StreamingAssets: при сборке копируется в билд ОТКРЫТЫМ ТЕКСТОМ и правится без пересборки.
    public static class InterflowFactionExport
    {
        const string PrefabPath = "Assets/Prefabs/GameManager.prefab";
        const string OutPath = "Assets/StreamingAssets/ServerConfigs/factions.json";
        static int notInResources;

        [MenuItem("Tools/Interflow/Экспорт серверных конфигов фракций")]
        public static void Export()
        {
            notInResources = 0;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameManager gm = prefab != null ? prefab.GetComponent<GameManager>() : null;
            if (gm == null || gm.factionData == null || gm.factionData.Length == 0)
            {
                Debug.LogError($"[Экспорт фракций] Не найден заполненный factionData в {PrefabPath}.");
                return;
            }

            var file = new FactionsFileDto
            {
                factions = gm.factionData
                    .Select(fd => new FactionEntryDto { name = fd.factionName, config = Conv(fd.config) })
                    .ToArray()
            };

            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            File.WriteAllText(OutPath, JsonUtility.ToJson(file, true));
            AssetDatabase.Refresh();
            int builds = CopyToBuilds();
            Debug.Log($"[Экспорт фракций] {OutPath}: фракций {file.factions.Length} " +
                      $"({string.Join(", ", file.factions.Select(f => f.name))}); ассетов вне Resources: {notInResources}" +
                      (notInResources > 0 ? " — ОНИ НЕ ЗАГРУЗЯТСЯ на сервере, перенеси их в Resources!" : ".") +
                      $" Скопировано в билды: {builds}.");
        }

        // [Interflow 2026-08-01] Автокопирование конфига в готовые билды: после экспорта файл сам
        // попадает в <корень проекта>/<папка из настроек>/*_Data/StreamingAssets/ServerConfigs/factions.json.
        // Папки перечислены в InterflowEditorSettings.serverBuildFolders (пути относительно корня проекта).
        static int CopyToBuilds()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string src = Path.Combine(projectRoot, OutPath);
            string[] folders = InterflowEditorSettings.GetOrCreate().serverBuildFolders ?? new string[0];
            int copied = 0;
            foreach (string folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                string buildDir = Path.Combine(projectRoot, folder.Trim());
                if (!Directory.Exists(buildDir))
                {
                    Debug.Log($"[Экспорт фракций] Папка билда не найдена, пропускаю: {buildDir}");
                    continue;
                }
                foreach (string dataDir in Directory.GetDirectories(buildDir, "*_Data", SearchOption.TopDirectoryOnly))
                {
                    string dstDir = Path.Combine(dataDir, "StreamingAssets", "ServerConfigs");
                    Directory.CreateDirectory(dstDir);
                    string dst = Path.Combine(dstDir, "factions.json");
                    File.Copy(src, dst, true);
                    Debug.Log($"[Экспорт фракций] Скопировано в билд: {dst}");
                    copied++;
                }
            }
            return copied;
        }

        // Путь ассета относительно папки Resources ("" — нет ссылки или ассет вне Resources).
        static string P(UnityEngine.Object o)
        {
            if (o == null) return "";
            string p = AssetDatabase.GetAssetPath(o);
            int i = p.IndexOf("/Resources/", StringComparison.Ordinal);
            if (i < 0)
            {
                notInResources++;
                Debug.LogWarning($"[Экспорт фракций] «{o.name}»: {p} — НЕ в Resources, сервер не сможет загрузить.");
                return "";
            }
            p = p.Substring(i + "/Resources/".Length);
            int dot = p.LastIndexOf('.');
            return dot >= 0 ? p.Substring(0, dot) : p;
        }

        static CostDto[] CostD(ResourceWrapper[] src) =>
            src == null ? new CostDto[0] : src.Select(c => new CostDto { type = P(c != null ? c.type : null), value = c != null ? c.value : 0 }).ToArray();

        static TechNodeDto NodeD(TechNode n)
        {
            if (n == null) return new TechNodeDto(); // JsonUtility null не пишет — пустой узел = отсутствие
            return new TechNodeDto
            {
                technology = P(n.technology),
                cost = CostD(n.cost),
                unlockUnits = n.unlockUnits != null ? n.unlockUnits.Select(P).ToArray() : new string[0],
                unlockAbilities = n.unlockAbilities != null
                    ? n.unlockAbilities.Select(a => new AbilityUnlockDto { ability = P(a != null ? a.ability : null), slot = a != null ? a.slot : 0 }).ToArray()
                    : new AbilityUnlockDto[0],
                unitSwaps = n.unitSwaps != null
                    ? n.unitSwaps.Select(s => new UnitSwapDto { from = P(s != null ? s.from : null), to = P(s != null ? s.to : null) }).ToArray()
                    : new UnitSwapDto[0],
                towerSwaps = n.towerSwaps != null
                    ? n.towerSwaps.Select(s => new TowerSwapDto { pointKey = (int)(s != null ? s.pointKey : PointKey.None), to = P(s != null ? s.to : null) }).ToArray()
                    : new TowerSwapDto[0],
            };
        }

        static TechBigOptionDto BigD(TechBigOption b) => b == null ? new TechBigOptionDto() : new TechBigOptionDto
        {
            node = NodeD(b.node),
            specializationA = NodeD(b.specializationA),
            specializationB = NodeD(b.specializationB),
            heroPrefab = P(b.heroPrefab),
        };

        static FactionConfigDto Conv(FactionConfig c)
        {
            if (c == null) return new FactionConfigDto();
            return new FactionConfigDto
            {
                waveUnits = c.waveUnits != null
                    ? c.waveUnits.Select(w => new WaveUnitDto { unit = P(w != null ? w.unit : null), role = (int)(w != null ? w.role : 0), count = w != null ? w.count : 0 }).ToArray()
                    : new WaveUnitDto[0],
                baseWaveIncome = c.baseWaveIncome,
                centralAbilities = c.centralAbilities != null ? c.centralAbilities.Select(P).ToArray() : new string[0],
                heroPrefab = P(c.heroPrefab),
                heroUnlockTech = P(c.heroUnlockTech),
                techTiers = c.techTiers != null
                    ? c.techTiers.Select(t => t == null ? new TechTierDto() : new TechTierDto
                        { levelUpgrade = NodeD(t.levelUpgrade), optionA = BigD(t.optionA), optionB = BigD(t.optionB) }).ToArray()
                    : new TechTierDto[0],
                mainBuildingShapesByLevel = c.mainBuildingShapesByLevel != null ? c.mainBuildingShapesByLevel.Select(P).ToArray() : new string[0],
                centreTower = P(c.centreTower),
                defence1Tower = P(c.defence1Tower),
                defence2Tower = P(c.defence2Tower),
                costModifiers = c.costModifiers != null
                    ? c.costModifiers.Select(m => new CostModifierDto { triggerTech = P(m != null ? m.triggerTech : null), resource = P(m != null ? m.resource : null), percentReduction = m != null ? m.percentReduction : 0f }).ToArray()
                    : new CostModifierDto[0],
                gravePrefab = P(c.gravePrefab),
                graveLifetime = c.graveLifetime,
                soulsResource = P(c.soulsResource),
                soulsPerMinuteByMbLevel = c.soulsPerMinuteByMbLevel,
                soulsPerTier = c.soulsPerTier,
                soulBranches = c.soulBranches != null
                    ? c.soulBranches.Select(b => new SoulBranchDto
                        {
                            tiers = b != null && b.tiers != null
                                ? b.tiers.Select(t => new SoulBranchTierDto
                                    {
                                        optionA = t != null && t.optionA != null ? new SoulBranchOptionDto { technology = P(t.optionA.technology), cost = CostD(t.optionA.cost) } : new SoulBranchOptionDto(),
                                        optionB = t != null && t.optionB != null ? new SoulBranchOptionDto { technology = P(t.optionB.technology), cost = CostD(t.optionB.cost) } : new SoulBranchOptionDto(),
                                    }).ToArray()
                                : new SoulBranchTierDto[0]
                        }).ToArray()
                    : new SoulBranchDto[0],
                soulBranchLimit = c.soulBranchLimit,
                skvernaStartRadius = c.skvernaStartRadius,
                skvernaGrowthPerSecond = c.skvernaGrowthPerSecond,
                skvernaMaxRadius = c.skvernaMaxRadius,
                exclusiveTechGroups = c.exclusiveTechGroups != null
                    ? c.exclusiveTechGroups.Select(g => new TechGroupDto { techs = g != null && g.techs != null ? g.techs.Select(P).ToArray() : new string[0] }).ToArray()
                    : new TechGroupDto[0],
                lockedBySouzlessTechs = c.lockedBySouzlessTechs != null ? c.lockedBySouzlessTechs.Select(P).ToArray() : new string[0],
            };
        }
    }
}
