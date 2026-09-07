using System;

namespace StrategyCore
{
    // [Interflow 2026-08-01] «Конфиги вне билда»: DTO-схема файла StreamingAssets/ServerConfigs/factions.json.
    // Все ссылки на ассеты — строки-пути ОТНОСИТЕЛЬНО папки Resources (резолв Resources.Load в ServerFactionConfigs).
    // Иконки (Texture2D) не экспортируются — сервер не рисует, клиент берёт их из своих запечённых ассетов.
    // Порядок элементов factions = порядку GameManager.factionData (индексы согласованы с SlotManager.playerFaction).

    [Serializable] public class FactionsFileDto { public int version = 1; public FactionEntryDto[] factions; }
    [Serializable] public class FactionEntryDto { public string name; public FactionConfigDto config; }

    [Serializable]
    public class FactionConfigDto
    {
        public WaveUnitDto[] waveUnits;
        public int[] waveIncomeByLevel;
        public string[] centralAbilities;
        public string heroPrefab;
        public string heroUnlockTech;
        public TechTierDto[] techTiers;
        public string[] mainBuildingShapesByLevel;
        public string centreTower;
        public string defence1Tower;
        public string defence2Tower;
        public CostModifierDto[] costModifiers;
        public string gravePrefab;
        public float graveLifetime;
        public string soulsResource;
        public float[] soulsPerMinuteByMbLevel;
        public int[] soulsPerTier;
        public SoulBranchDto[] soulBranches;
        public int soulBranchLimit;
        public float skvernaStartRadius;
        public float skvernaGrowthPerSecond;
        public float skvernaMaxRadius;
        public TechGroupDto[] exclusiveTechGroups;
        public string[] lockedBySouzlessTechs;
    }

    [Serializable] public class WaveUnitDto { public string unit; public int role; public int count; }
    [Serializable] public class CostDto { public string type; public int value; }
    [Serializable] public class CostModifierDto { public string triggerTech; public string resource; public float percentReduction; }
    [Serializable] public class AbilityUnlockDto { public string ability; public int slot; }
    [Serializable] public class UnitSwapDto { public string from; public string to; }
    [Serializable] public class TowerSwapDto { public int pointKey; public string to; }

    [Serializable]
    public class TechNodeDto
    {
        public string technology;
        public CostDto[] cost;
        public string[] unlockUnits;
        public AbilityUnlockDto[] unlockAbilities;
        public UnitSwapDto[] unitSwaps;
        public TowerSwapDto[] towerSwaps;
    }

    [Serializable] public class TechBigOptionDto { public TechNodeDto node; public TechNodeDto specializationA; public TechNodeDto specializationB; public string heroPrefab; }
    [Serializable] public class TechTierDto { public TechNodeDto levelUpgrade; public TechBigOptionDto optionA; public TechBigOptionDto optionB; }
    [Serializable] public class TechGroupDto { public string[] techs; }
    [Serializable] public class SoulBranchOptionDto { public string technology; public CostDto[] cost; }
    [Serializable] public class SoulBranchTierDto { public SoulBranchOptionDto optionA; public SoulBranchOptionDto optionB; }
    [Serializable] public class SoulBranchDto { public SoulBranchTierDto[] tiers; }
}
