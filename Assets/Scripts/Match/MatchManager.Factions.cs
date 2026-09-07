using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ===== ПРИМЕНЕНИЕ ФРАКЦИЙ (партиал MatchManager) =====
    // Резолв расы стороны в runtime-копию TeamWaveConfig, каст центральной способности по id, доступ для UI. Ассет не трогаем (правило 1).
    public partial class MatchManager
    {
        // ======================== ПРИМЕНЕНИЕ ФРАКЦИЙ (раса → сторона) ========================

        // Заполняет runtime-копию TeamWaveConfig обеих сторон контентом их расы. Вызов в Awake ДО
        // AssignCasterAbilities. Детерминированно на всех пирах: источник — playerFaction (синхронизирован
        // PlayerListSend после RandomizeSlotData), конфиги — общие ассеты. Это конфиг, не игровое действие —
        // правило 6 не применяется (как и у AssignCasterAbilities). Раса не резолвится → дефолтная раса (см. ResolveFaction).
        void ApplyFactions()
        {
            ApplyFaction(teamA);
            ApplyFaction(teamB);
        }

        // Резолвит расу владельца стороны и ПЕРЕЗАПИСЫВАЕТ контентные поля cfg значениями расы (копией —
        // runtime-конфиг изолируем от общего ассета). Раса не резолвится → дефолтная (ResolveFaction); совсем
        // нет конфигов → cfg пуст (контент-поля скрыты из Inspector, руками они больше не заполняются).
        // Scene-bound (spawnGrid/abilityCaster/spawnPoint/targetPoint/марш) не трогаем.
        // Башни (towers) — под-шаг 3б (резолв в момент отстройки по захватчику).
        void ApplyFaction(TeamWaveConfig cfg)
        {
            if (cfg == null) return;
            FactionConfig faction = ResolveFaction(cfg.ownerPlayer);
            if (faction == null) return;

            ApplyFactionContent(cfg, faction);
        }

        // Тело применения расы БЕЗ резолва: перезаписывает контентные поля cfg значениями переданной расы.
        // Отделено от ApplyFaction, чтобы тестовый полигон мог назначить расу стороне явным ассетом
        // (MatchManager.TestArena.TestApplyFaction) — при прямом запуске сцены резолв по playerFaction
        // дал бы обеим сторонам одну расу. Штатное поведение не меняется: ApplyFaction зовёт это же тело.
        void ApplyFactionContent(TeamWaveConfig cfg, FactionConfig faction)
        {
            if (cfg == null || faction == null) return;

            cfg.techTiers                = CloneTechTiers(faction.techTiers); // дерево тиров — ГЛУБОКИЙ клон (урок waveComposition; ассет не мутируем)
            cfg.mainBuildingShapesByLevel = CloneArray(faction.mainBuildingShapesByLevel);
            cfg.centralAbilities         = faction.centralAbilities != null
                                           ? new List<Ability>(faction.centralAbilities)
                                           : new List<Ability>();
            cfg.heroPrefab               = faction.heroPrefab; // префаб героя расы (single ref, клон не нужен)
            cfg.heroUnlockTech           = faction.heroUnlockTech; // тех-гейт призыва героя (single ref, клон не нужен)
            cfg.costModifiers            = CloneArray(faction.costModifiers); // модификаторы цены найма B28 (shallow; правила-ссылки не мутируем)
            cfg.soulsResource            = faction.soulsResource; // ресурс душ Нежити (N1; single ref, клон не нужен)
            cfg.soulsPerMinuteByMbLevel  = CloneArray(faction.soulsPerMinuteByMbLevel); // генерация душ/мин по уровню ГЗ
            cfg.soulsPerTier             = CloneArray(faction.soulsPerTier); // души за убийство по тиру

            // Волна 2.0: единый список юнитов волны + таблица дохода + сброс runtime-пометок.
            cfg.waveUnits                = CloneWaveUnits(faction.waveUnits);           // единый список (глубокий клон — ассет не мутируем)
            cfg.waveIncomeByLevel        = CloneArray(faction.waveIncomeByLevel);       // доход волны по уровню ГЗ (индекс = уровень, вкл. 0)
            cfg.autoSummon               = new HashSet<int>();
            cfg.oneShot                  = new Dictionary<int, int>();
            cfg.compositionLocked        = false;
            cfg.waveSkipped              = false;
            cfg.heroWavesToSkip          = 0;
        }

        // Раса по слоту-владельцу: playerFaction[slot] — 0-based индекс в GameManager.factionData[] (после
        // RandomizeSlotData); берём привязанный к расе config (FactionData.config → FactionConfig).
        // Раса не резолвится → фоллбэк на ДЕФОЛТНУЮ расу (factionData[0].config) с варнингом. Единая точка
        // истины: контент только из FactionConfig (Inspector-копии TeamWaveConfig скрыты); согласованно для
        // всех систем (каст/UI/свопы); тестовый запуск сцены без лобби продолжает работать.
        FactionConfig ResolveFaction(int ownerSlot)
        {
            GameManager gm = GameManager.Instance;
            SlotManager sm = SlotManager.Instance;

            int idx = -1;
            if (sm != null && sm.playerFaction != null && ownerSlot >= 0 && ownerSlot < sm.playerFaction.Length)
                idx = sm.playerFaction[ownerSlot];

            // [Interflow 2026-08-01] «Конфиги вне билда»: внешний реестр (StreamingAssets/ServerConfigs) — приоритет в билдах.
            if (ServerFactionConfigs.TryGet(idx, out FactionConfig external)) return external;

            if (idx >= 0 && gm != null && gm.factionData != null && idx < gm.factionData.Length && gm.factionData[idx].config != null)
                return gm.factionData[idx].config;

            // Фоллбэк на дефолтную расу: сперва внешний реестр, затем запечённые данные.
            if (ServerFactionConfigs.TryGet(0, out FactionConfig externalDefault))
            {
                Debug.LogWarning($"[MatchManager] ResolveFaction: раса слота {ownerSlot} (индекс {idx}) не резолвится — внешний конфиг [0] ({externalDefault.name}).");
                return externalDefault;
            }

            FactionConfig def = (gm != null && gm.factionData != null && gm.factionData.Length > 0)
                                ? gm.factionData[0].config : null;
            Debug.LogWarning($"[MatchManager] ResolveFaction: раса слота {ownerSlot} не резолвится — фоллбэк на дефолтную расу " +
                             $"({(def != null ? def.name : "factionData[0].config тоже пуст → null")}).")
;
            return def;
        }

        // Поверхностная копия массива (runtime-конфиг изолируем от общего ассета фракции).
        static T[] CloneArray<T>(T[] src) => src != null ? (T[])src.Clone() : null;

        // Глубокая копия единого списка юнитов волны: новые WaveUnitEntry, чтобы runtime не мутировал ассет фракции.
        static WaveUnitEntry[] CloneWaveUnits(WaveUnitEntry[] src)
        {
            if (src == null) return null;
            WaveUnitEntry[] dst = new WaveUnitEntry[src.Length];
            for (int i = 0; i < src.Length; i++)
                dst[i] = src[i] != null ? new WaveUnitEntry { unit = src[i].unit, role = src[i].role, count = src[i].count } : null;
            return dst;
        }

        // Глубокая копия дерева тиров: новые TechTier/TechBigOption/TechNode на КАЖДОМ уровне, чтобы runtime
        // НЕ мутировал ассет фракции (урок waveComposition) и teamA не делил объекты с teamB. Ссылки на ассеты
        // (Technology/Texture2D/Unit) и массив цены копируются как есть — их не мутируем.
        static TechTier[] CloneTechTiers(TechTier[] src)
        {
            if (src == null) return null;
            TechTier[] dst = new TechTier[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                if (src[i] == null) { dst[i] = null; continue; }
                dst[i] = new TechTier
                {
                    levelUpgrade = CloneTechNode(src[i].levelUpgrade),
                    optionA      = CloneTechBigOption(src[i].optionA),
                    optionB      = CloneTechBigOption(src[i].optionB),
                };
            }
            return dst;
        }

        static TechBigOption CloneTechBigOption(TechBigOption src)
        {
            if (src == null) return null;
            return new TechBigOption
            {
                node            = CloneTechNode(src.node),
                specializationA = CloneTechNode(src.specializationA),
                specializationB = CloneTechNode(src.specializationB),
                heroPrefab      = src.heroPrefab, // ссылка на префаб героя — клон не нужен
            };
        }

        static TechNode CloneTechNode(TechNode src)
        {
            if (src == null) return null;
            return new TechNode
            {
                technology      = src.technology,              // ссылка на ассет Technology
                icon            = src.icon,                    // ссылка на Texture2D
                cost            = CloneArray(src.cost),        // новый массив; ResourceWrapper-ссылки не мутируем
                // Эффекты узла (что он открывает) — новые массивы; сами записи-ссылки не мутируем.
                unlockUnits     = CloneArray(src.unlockUnits),
                unlockAbilities = CloneArray(src.unlockAbilities),
                unitSwaps       = CloneArray(src.unitSwaps),
                towerSwaps      = CloneArray(src.towerSwaps),
            };
        }

        /// <summary>
        /// Сервер: кастовать центральную способность команды ПО Ability.id (стабильный ключ дровера).
        /// Маппинг id→index в abilities[] кастера СВОЕЙ команды, затем штатный Unit.UseAbilityItem.
        /// Единый источник каста центральной таблицы: хост зовёт напрямую, клиент — через NetworkDataSync.
        /// </summary>
        public void CastCentralAbilityById(int teamIndex, int abilityId)
        {
            if (NetworkConnectionHandler.isClient) return;
            TeamWaveConfig cfg = Team(teamIndex);
            Unit caster = cfg != null ? cfg.abilityCaster : null;
            if (caster == null || caster.dead || caster.abilities == null)
            {
                Debug.LogWarning($"[MatchManager] CastCentralAbilityById: нет кастера/способностей команды {teamIndex}.");
                return;
            }

            int index = -1;
            for (int i = 0; i < caster.abilities.Length; i++)
                if (caster.abilities[i] != null && caster.abilities[i].id == abilityId) { index = i; break; }

            if (index < 0)
            {
                Debug.LogWarning($"[MatchManager] CastCentralAbilityById: способность id={abilityId} не найдена у кастера команды {teamIndex}.");
                return;
            }

            caster.UseAbilityItem(index, false, null, Vector3.zero, true);
        }

        // ======================== ДОСТУП ДЛЯ UI ========================

        /// <summary>Конфиг команды по индексу: 0 = A, иначе B.</summary>
        public TeamWaveConfig Team(int index) => index == 0 ? teamA : teamB;

    }
}
