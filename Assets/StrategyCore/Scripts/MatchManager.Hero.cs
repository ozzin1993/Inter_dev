using System;
using UnityEngine;

namespace StrategyCore
{
    // ============================= ГЕРОЙ (партиал MatchManager) ==
    // Сервер-авторитетно (правило 6). Один живой герой на команду (одновременно только один).
    // Призыв — АВТОСПАВН с волной (TryAutoSpawnHero, Волна 2.0), бесплатно; после смерти пауза heroWavesToSkip волн.
    // Спавн в слоте сетки замка (как юнит волны); герой попадает в teamUnits
    // (подчиняется командам Атака/Защита); лидерство НЕ занимает (компенсация как у «Призыва к Оружию»).
    // Уровень сохраняется между смертями (heroLevel[team]).
    // Умения героя — на его префабе: Active кастуются из таблицы (CastHeroAbilityById — отдельный шаг),
    // автоматические — штатный компонент AutoAbilityUser на префабе. Ассет StrategyCore не трогаем (правило 1).
    public partial class MatchManager
    {
        // Живой герой команды (сервер). null — героя нет. Одновременно только один на команду.
        readonly Unit[] heroUnit = new Unit[2];

        // Сохранённый уровень героя команды: персистентность между смертями (D8).
        // 0 — ещё не задан (первый призыв берёт уровень префаба как базовый).
        readonly int[] heroLevel = new int[2];

        // [UI-сессия 2026-07-06] Клиентское зеркало «жив ли герой» (heroUnit на клиент не синкается).
        // Заполняется через HeroAliveClientRpc → ApplyHeroAliveClient. Нужно для UI-дизейбла кнопки призыва.
        readonly bool[] heroAliveClient = new bool[2];

        // [UI-сессия 2026-07-06] Клиентское зеркало уровня героя (для .locked умений по уровню) + дедуп broadcast.
        readonly int[] heroLevelClient = new int[2];
        readonly int[] lastBroadcastHeroLevel = new int[2];

        // Событие для UI (сервер): герой команды изменился — призван (unit) или погиб (null).
        public event Action<int, Unit> OnHeroChanged;

        /// <summary>Живой герой команды (для UI/логики). null — нет.</summary>
        public Unit HeroUnit(int team) => (team == 0 || team == 1) ? heroUnit[team] : null;

        /// <summary>Жив ли сейчас герой команды. Хост — по heroUnit; клиент — по синхронному зеркалу heroAliveClient.</summary>
        public bool HeroAlive(int team)
        {
            if (team != 0 && team != 1) return false;
            if (NetworkConnectionHandler.isClient) return heroAliveClient[team];   // [UI-сессия] клиент: heroUnit не синкается
            return heroUnit[team] != null && !heroUnit[team].dead;
        }

        /// <summary>
        /// Сервер: призвать героя команды за золото. Одновременно только один живой герой на команду.
        /// Спавн в слоте сетки замка (как юнит волны), в teamUnits (подчиняется Атака/Защита), лидерство не занимает,
        /// уровень восстанавливается из heroLevel[team]. Точка входа кнопки призыва: хост — напрямую,
        /// клиент — через NetworkDataSync.Hero (отдельный шаг).
        /// </summary>
        public void SummonHero(int team)
        {
            if (NetworkConnectionHandler.isClient) return; // спавн/золото/смерть — только сервер (правило 6)
            if (team != 0 && team != 1) return;

            if (HeroAlive(team))
            {
                Debug.Log($"[MatchManager] Герой команды {team}: уже жив — призыв отклонён (одновременно только один).");
                return;
            }

            TeamWaveConfig cfg = Team(team);
            if (cfg == null) { Debug.LogWarning($"[MatchManager] Герой: нет конфига команды {team}."); return; }
            if (cfg.heroPrefab == null)
            {
                Debug.LogWarning($"[MatchManager] Герой команды {team}: не задан heroPrefab (заполни FactionConfig.heroPrefab расы).");
                return;
            }

            // Гейт по теху (T2): раса задала heroUnlockTech и он не разблокирован — призыв отклонён.
            // Пусто — герой доступен сразу. TechUnlockedSafe — та же безопасная проверка, что и в дереве техов (partial того же класса).
            if (cfg.heroUnlockTech != null && !TechUnlockedSafe(cfg.heroUnlockTech, cfg.ownerPlayer))
            {
                Debug.Log($"[MatchManager] Герой команды {team}: тех «{cfg.heroUnlockTech.name}» не разблокирован — призыв отклонён (гейт героя).");
                return;
            }

            // Спавн в слоте сетки замка (строй), если spawnGrid задан; иначе — фоллбэк в точку спавна.
            Unit hero = SpawnSlotPose(cfg, team, cfg.heroPrefab.formationPriority, out Vector3 slotPos, out float slotYaw)
                ? Unit.Spawn(cfg.heroPrefab, slotPos, slotYaw, cfg.ownerPlayer, 0f)
                : (cfg.spawnPoint != null ? Unit.Spawn(cfg.heroPrefab, cfg.spawnPoint.position, 0f, cfg.ownerPlayer, 0f) : null);

            if (hero == null)
            {
                Debug.LogWarning($"[MatchManager] Герой команды {team}: спавн не удался (точка занята?).");
                return;
            }

            heroUnit[team] = hero;

            // В общий список команды: подчиняется Атака/Защита; переотдача при волне/захвате его видит.
            teamUnits[team].Add(hero);
            AssignSlot(team, hero);            // слот строя (как у юнита волны) — «слот в защитной сетке»
            CompensateSummonLimitedCost(hero); // лидерство не занимает: нетто-ноль (как «Призыв к Оружию»)

            // Восстановление сохранённого уровня (персистентность между смертями). Уровень префаба — базовый.
            RestoreHeroLevel(team, hero);

            // [UI-сессия] Синк уровня героя клиенту (для .locked умений по уровню): старт + при прокачке (OnXPChange).
            LevelingUnit heroLvl = hero.GetComponent<LevelingUnit>();
            Action onHeroXpChanged = null;
            if (heroLvl != null)
            {
                onHeroXpChanged = () => SyncHeroLevel(team);
                heroLvl.OnXPChange += onHeroXpChanged;
            }
            SyncHeroLevel(team);

            int capt = team;
            hero.OnDie += (u, _, _, _) =>
            {
                CaptureHeroLevel(capt, u);     // сохранить уровень до уничтожения (между смертями)
                if (Team(capt) != null) Team(capt).heroWavesToSkip = heroRespawnWavesSkipped; // Волна 2.0: после смерти герой пропускает волны
                teamUnits[capt].Remove(u);
                OnDefenceUnitDied(capt, u);     // компакция слота (крайний → в дыру)
                if (heroUnit[capt] == u) heroUnit[capt] = null;
                if (heroLvl != null && onHeroXpChanged != null) heroLvl.OnXPChange -= onHeroXpChanged;   // [UI-сессия] отписка
                try { OnHeroChanged?.Invoke(capt, null); }
                catch (Exception e) { Debug.LogError($"[MatchManager] OnHeroChanged(die): {e.Message}"); }
                BroadcastHeroAlive(capt, false);   // [UI-сессия] синк клиенту: герой мёртв
                Debug.Log($"[MatchManager] Герой команды {capt} погиб. Сохранённый уровень={heroLevel[capt]}.");
            };

            // Подхватываем текущий режим команды (как обычный новый юнит; None → idle).
            ReissueCurrentCommand(hero);

            // Покрытие хаба смертей/могилок (§6.1: инвок OnUnitSpawned из наших путей спавна, кроме башен) — как
            // SpawnWave, чтобы DeathEventHub и Graves подписались на OnDie героя.
            try { OnUnitSpawned?.Invoke(team, hero); }
            catch (Exception e) { Debug.LogError($"[MatchManager] OnUnitSpawned (hero): {e.Message}"); }

            try { OnHeroChanged?.Invoke(team, hero); }
            catch (Exception e) { Debug.LogError($"[MatchManager] OnHeroChanged(summon): {e.Message}"); }
            BroadcastHeroAlive(team, true);   // [UI-сессия] синк клиенту: герой жив (дизейбл кнопки призыва)
            Debug.Log($"[MatchManager] Герой команды {team} (player={cfg.ownerPlayer}) призван. Уровень={heroLevel[team]}.");
        }

        /// <summary>Волна 2.0: автоспавн героя с волной. Открыт (heroUnlockTech), не жив и не на паузе перерождения (heroWavesToSkip==0) → SummonHero (бесплатно).</summary>
        public void TryAutoSpawnHero(int team)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (team != 0 && team != 1) return;
            TeamWaveConfig cfg = Team(team);
            if (cfg == null || cfg.heroPrefab == null) return;
            if (cfg.heroWavesToSkip > 0) return;                                                        // ещё на паузе перерождения
            if (HeroAlive(team)) return;                                                                 // уже жив (один живой)
            if (cfg.heroUnlockTech != null && !TechUnlockedSafe(cfg.heroUnlockTech, cfg.ownerPlayer)) return; // не открыт техой
            SummonHero(team);
        }

        // Поднять уровень свежезаспавненного героя до сохранённого (heroLevel[team]). Только вверх (SetLevel не опускает).
        // Первый призыв (heroLevel не задан) — берём уровень префаба как базовый.
        void RestoreHeroLevel(int team, Unit hero)
        {
            LevelingUnit lvl = hero != null ? hero.GetComponent<LevelingUnit>() : null;
            if (lvl == null) return; // герой без прокачки — ничего не делаем
            if (heroLevel[team] < lvl.level) heroLevel[team] = lvl.level;      // база из префаба
            if (heroLevel[team] > lvl.level)
                lvl.SetLevel(heroLevel[team], 0, 0, true, true, true);        // levelUp=true (статы/замки), noVFX=true
        }

        // Сохранить текущий уровень героя (на смерти) для восстановления при следующем призыве.
        void CaptureHeroLevel(int team, Unit hero)
        {
            LevelingUnit lvl = hero != null ? hero.GetComponent<LevelingUnit>() : null;
            if (lvl != null && lvl.level > heroLevel[team]) heroLevel[team] = lvl.level;
        }

        // [UI-сессия 2026-07-06] Синк клиенту факта «жив ли герой» (для UI-дизейбла кнопки призыва).
        // Серверо-авторитетно (правило 6): шлёт только сервер, через штатный ClientRpc (правило 2).
        void BroadcastHeroAlive(int team, bool alive)
        {
            if (NetworkConnectionHandler.isClient) return;                      // шлёт только сервер
            if (team != 0 && team != 1) return;
            if (NetworkDataSync.instance != null) NetworkDataSync.instance.HeroAliveClientRpc(team, alive);
        }

        /// <summary>
        /// [UI-сессия] Клиент: применить синк «жив ли герой» и уведомить UI (у клиента серверное OnHeroChanged
        /// не срабатывает). hero-параметр события у клиента null — важен сам факт (HeroAlive для кнопки призыва).
        /// </summary>
        public void ApplyHeroAliveClient(int team, bool alive)
        {
            if (team != 0 && team != 1) return;
            heroAliveClient[team] = alive;
            try { OnHeroChanged?.Invoke(team, HeroUnit(team)); }
            catch (Exception e) { Debug.LogError($"[MatchManager] OnHeroChanged(client): {e.Message}"); }
        }

        // [UI-сессия] Синк уровня героя клиенту (для .locked умений по уровню). Шлём только при изменении.
        // Серверо-авторитетно (правило 6): шлёт только сервер, штатным ClientRpc (правило 2).
        void SyncHeroLevel(int team)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (team != 0 && team != 1) return;
            Unit hero = heroUnit[team];
            LevelingUnit lvl = hero != null ? hero.GetComponent<LevelingUnit>() : null;
            int level = lvl != null ? lvl.level : 0;
            if (level == lastBroadcastHeroLevel[team]) return;             // без изменения — не шлём
            lastBroadcastHeroLevel[team] = level;
            if (NetworkDataSync.instance != null) NetworkDataSync.instance.HeroLevelClientRpc(team, level);
            try { OnHeroChanged?.Invoke(team, hero); }                     // [UI-сессия] хост-UI: перерисовать умения (.locked при прокачке)
            catch (Exception e) { Debug.LogError($"[MatchManager] OnHeroChanged(hostlevel): {e.Message}"); }
        }

        /// <summary>[UI-сессия] Клиент: применить синк уровня героя + перерисовать умения (.locked по уровню).</summary>
        public void ApplyHeroLevelClient(int team, int level)
        {
            if (team != 0 && team != 1) return;
            heroLevelClient[team] = level;
            try { OnHeroChanged?.Invoke(team, HeroUnit(team)); }           // UI перерисует умения героя
            catch (Exception e) { Debug.LogError($"[MatchManager] OnHeroChanged(level): {e.Message}"); }
        }

        /// <summary>[UI-сессия] Уровень героя, известный клиенту (синк). Для UI .locked умений по уровню.</summary>
        public int HeroLevelClient(int team) => (team == 0 || team == 1) ? heroLevelClient[team] : 0;

        // ======================== КАСТ УМЕНИЙ ГЕРОЯ (кастер = герой) ========================

        // Сервер: активировать умение героя из таблицы по стабильному ключу Ability.id (кастер — живой герой команды).
        // Зеркало CastCentralAbilityById, но кастер = heroUnit[team] (а не abilityCaster ГЗ). Индекс ищем перебором
        // по id (обход бага Utils.GetAbilityIndex, как в центральной таблице). Уровень/замок/КД проверяет сам ассет
        // в UseAbilityItem. Точка входа UI: хост — напрямую, клиент — через NetworkDataSync.Hero (отдельный шаг).
        public void CastHeroAbilityById(int team, int abilityId)
        {
            if (NetworkConnectionHandler.isClient) return;               // каст исполняет сервер (правило 6)
            if (team != 0 && team != 1) return;

            Unit caster = heroUnit[team];
            if (caster == null || caster.dead || caster.abilities == null)
            {
                Debug.LogWarning($"[MatchManager] CastHeroAbilityById: нет живого героя/способностей команды {team}.");
                return;
            }

            int index = -1;
            for (int i = 0; i < caster.abilities.Length; i++)
                if (caster.abilities[i] != null && caster.abilities[i].id == abilityId) { index = i; break; }

            if (index < 0)
            {
                Debug.LogWarning($"[MatchManager] CastHeroAbilityById: умение id={abilityId} не найдено у героя команды {team}.");
                return;
            }

            caster.UseAbilityItem(index, false, null, Vector3.zero, true);   // уровень/замок/КД/ману проверяет сам ассет (правило 2)
        }
    }
}
