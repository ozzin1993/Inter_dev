using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>Приказ, который получает юнит, призванный с тестового полигона.</summary>
    public enum TestArenaOrder
    {
        /// <summary>Как в бою: штатная переотдача команды его ряда. Режима нет → атака-марш к вражеской точке.</summary>
        AsInGame,
        /// <summary>Держать позицию: стоит на месте, отбивается.</summary>
        Hold,
        /// <summary>Манекен: неуязвим и стоит на месте (для замера урона и разглядывания состояний).</summary>
        Dummy,
        /// <summary>
        /// В атаку: атака-марш к точке призыва ПРОТИВНИКА. Отличается от «как в бою» тем, что не
        /// зависит от режима ряда — юнит идёт через всю карту и вступает в бой со всем, что встретит.
        /// Заведено 10.09.2026: «как в бою» без режима доводит юнита до точки его ряда и там оставляет,
        /// и две стороны вставали в сотне метров друг от друга, так и не сойдясь.
        /// </summary>
        Attack,
    }

    // ===== ТЕСТОВЫЙ ПОЛИГОН (партиал MatchManager) =====
    // Открытое API сцены TestArena: призыв с полной обвязкой волны, уборка поля, восстановление здоровья/маны,
    // сброс откатов, ТОЧЕЧНОЕ открытие/закрытие технологий и назначение расы стороне явным ассетом.
    // Всё, что меняет мир, — только на сервере (правило 6). Ядро ассета не трогаем (правило 1),
    // логика призыва повторяет SpawnWave и ConfigureSummonedUnit (правило 5: единые штатные пути).
    public partial class MatchManager
    {
        // ===== ПРИКАЗ «В АТАКУ»: подтверждение =====
        // Одной команды мало. Штатная жизнь юнита возвращает его к точке своего ряда: авто-каст зовёт
        // ReissueCurrentCommand, приход волны и захват башни переотдают команды ряда. На полигоне из-за
        // этого две стороны доходили до своих точек и вставали в полусотне метров друг от друга.
        // Поэтому носителей приказа держим списком и раз в тик возвращаем марш тем, кто встал без цели.
        // Список живёт только в тестовой сцене и чистится смертью, уборкой поля и сменой приказа.

        [NonSerialized] readonly HashSet<Unit> testAttackers = new HashSet<Unit>();
        [NonSerialized] readonly List<Unit> testAttackersBuffer = new List<Unit>();
        [NonSerialized] bool testAttackTickWired;

        void TestAttackTick()
        {
            if (NetworkConnectionHandler.isClient) return;
            if (testAttackers.Count == 0) return;

            InterflowTestArena arena = InterflowTestArena.Instance;
            if (arena == null) return;

            testAttackersBuffer.Clear();
            testAttackersBuffer.AddRange(testAttackers);

            for (int i = 0; i < testAttackersBuffer.Count; i++)
            {
                Unit u = testAttackersBuffer[i];
                if (u == null || u.dead) { testAttackers.Remove(u); continue; }

                // Идёт, преследует, бьёт или кастует — не мешаем.
                if (u.target != null) continue;
                if (u.unitState != UnitStates.Idle && u.unitState != UnitStates.Hold) continue;

                int team = TestTeamOfOwner(u.owner);
                Vector3 to = arena.SpawnPositionOf(team == 0 ? 1 : 0);
                u.AttackMove(new Vector2(to.x, to.z), false);
            }
        }

        /// <summary>Пауза волн полигона: SpawnWave выходит сразу, состав разлочивается. Не сериализуется.</summary>
        [NonSerialized] public bool testWavesPaused;

        // ======================== ПРИЗЫВ ========================

        /// <summary>
        /// Сервер: призвать одного юнита за сторону teamIndex с ПОЛНОЙ обвязкой волны — учёт в списке команды,
        /// слот строя, снятие по смерти, компенсация лидерства («бесплатно», как «Призыв к Оружию»),
        /// событие OnUnitSpawned (могилки/хаб смертей) и приказ. Возвращает юнита либо null.
        /// </summary>
        public Unit TestSpawn(Unit prefab, Vector3 position, int teamIndex, float spread, TestArenaOrder order)
        {
            if (NetworkConnectionHandler.isClient) return null;
            if (prefab == null) { Debug.LogWarning("[Полигон] Призыв: не задан префаб."); return null; }

            TeamWaveConfig cfg = Team(teamIndex);
            if (cfg == null) { Debug.LogWarning($"[Полигон] Призыв: у стороны {TestSideName(teamIndex)} нет конфига."); return null; }

            Unit u = Unit.Spawn(prefab, position, 0f, cfg.ownerPlayer, spread);
            if (u == null)
            {
                Debug.LogWarning($"[Полигон] Не удалось заспавнить '{prefab.name}' за сторону {TestSideName(teamIndex)} " +
                                 "(точка занята, вне границ игровой зоны или нет коллайдера земли под точкой призыва).");
                return null;
            }

            // Обвязка как у юнита волны (SpawnWave): список команды + слот строя + снятие по смерти с компакцией.
            teamUnits[teamIndex].Add(u);
            AssignSlot(teamIndex, u);
            int capt = teamIndex;
            u.OnDie += (du, _, _, _) =>
            {
                teamUnits[capt].Remove(du);
                OnDefenceUnitDied(capt, du);
            };

            // Лимитные ресурсы (лидерство) не занимаем: полигон не должен упираться в лимит на 50 юнитах.
            CompensateSummonLimitedCost(u);

            // Хаб смертей и могилки подписываются на юнита через это событие (как SpawnWave и ConfigureSummonedUnit).
            try { OnUnitSpawned?.Invoke(teamIndex, u); }
            catch (Exception e) { Debug.LogError($"[Полигон] OnUnitSpawned: {e.Message}"); }

            TestGiveOrder(u, order);
            return u;
        }

        /// <summary>Сервер: призвать count копий префаба за сторону. Возвращает, сколько фактически встало.</summary>
        public int TestSpawnMany(Unit prefab, int count, Vector3 position, int teamIndex, float spread, TestArenaOrder order)
        {
            if (NetworkConnectionHandler.isClient) return 0;
            if (count <= 0) return 0;

            int spawned = 0;
            for (int i = 0; i < count; i++)
                if (TestSpawn(prefab, position, teamIndex, spread, order) != null) spawned++;

            if (spawned < count)
                Debug.LogWarning($"[Полигон] За сторону {TestSideName(teamIndex)} встало {spawned} из {count} " +
                                 $"'{(prefab != null ? prefab.name : "?")}' — остальным не нашлось свободного места у точки призыва (увеличь разброс).");
            return spawned;
        }

        /// <summary>Сервер: выдать юниту приказ полигона. Манекен — неуязвимость + стоять.</summary>
        public void TestGiveOrder(Unit u, TestArenaOrder order)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (u == null || u.dead) return;

            switch (order)
            {
                case TestArenaOrder.Hold:
                    testAttackers.Remove(u);   // сменили приказ — подтверждение марша больше не нужно
                    u.Hold();
                    break;
                case TestArenaOrder.Dummy:
                    testAttackers.Remove(u);
                    u.isInvulnerable = true;
                    u.Hold();
                    break;
                case TestArenaOrder.Attack:
                {
                    // Цель марша — точка призыва противника. Своего объекта на карте у неё нет,
                    // поэтому берём её у самой сцены полигона; сцены нет — приказ вырождается в «стоять».
                    int team = TestTeamOfOwner(u.owner);
                    InterflowTestArena arena = InterflowTestArena.Instance;
                    if (arena == null) { u.Hold(false); break; }

                    Vector3 to = arena.SpawnPositionOf(team == 0 ? 1 : 0);
                    u.AttackMove(new Vector2(to.x, to.z), false);

                    testAttackers.Add(u);
                    if (!testAttackTickWired && GameManager.Instance != null)
                    {
                        GameManager.Instance.Tick += TestAttackTick;
                        testAttackTickWired = true;
                    }
                    break;
                }
                default:
                    testAttackers.Remove(u);
                    ReissueCurrentCommand(u); // штатный путь: режим ряда, а без режима — атака-марш к вражеской точке
                    break;
            }
        }

        // ======================== ПОЛЕ ========================

        /// <summary>
        /// Сервер: убрать с поля всех БОЕВЫХ юнитов стороны (teamIndex = -1 — обеих). Здания, главное здание и
        /// объект-кастер центральных умений не трогаются: их смерть завершила бы матч и сняла умения.
        /// Возвращает, сколько убрано.
        /// </summary>
        public int TestClear(int teamIndex)
        {
            if (NetworkConnectionHandler.isClient) return 0;

            testAttackers.Clear();   // поле убрано — подтверждать марш некому

            int removed = 0;
            if (teamIndex < 0)
            {
                removed += TestClear(0);
                removed += TestClear(1);
                return removed;
            }

            List<Unit> list = TestUnitsOf(teamIndex);
            TeamWaveConfig cfg = Team(teamIndex);
            for (int i = 0; i < list.Count; i++)
            {
                Unit u = list[i];
                if (u == null || u.dead) continue;
                if (u.unitType != UnitType.Unit) continue;                       // здания и башни оставляем
                if (cfg != null && (u == cfg.mainBuilding || u == cfg.abilityCaster)) continue;

                // Смерть одного юнита не должна срывать уборку всего поля: в ядре ассета встречается падение
                // на пересчёте тумана войны (FogOfWar.HideTile при Unit.Die). Ошибку показываем как есть
                // (правило 9 — не глушить), но продолжаем убирать остальных.
                try
                {
                    u.Die(-1, null, false, true, true); // без наград, с уничтожением тела (без анимации смерти и могилы)
                    removed++;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Полигон] Не удалось убрать '{u.name}': {e.GetType().Name} — {e.Message}. Уборка продолжается.");
                }
            }
            return removed;
        }

        /// <summary>Сервер: восстановить здоровье и ману всем живым юнитам стороны (teamIndex = -1 — обеим).</summary>
        public void TestRestoreVitals(int teamIndex)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (teamIndex < 0) { TestRestoreVitals(0); TestRestoreVitals(1); return; }

            List<Unit> list = TestUnitsOf(teamIndex);
            for (int i = 0; i < list.Count; i++)
            {
                Unit u = list[i];
                if (u == null || u.dead) continue;
                // Штатные методы ассета (правило 1): сами зажимают в максимум, поднимают события полосок
                // и синхронизируют клиентам. Прямая запись полей этого не делает.
                u.ChangeHP(u.maxHealth - u.health);
                u.ChangeMP(u.maxMana - u.mana);
            }
        }

        /// <summary>Сервер: обнулить откаты умений у всех живых юнитов стороны (teamIndex = -1 — обеих).</summary>
        public void TestResetCooldowns(int teamIndex)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (teamIndex < 0) { TestResetCooldowns(0); TestResetCooldowns(1); return; }

            List<Unit> list = TestUnitsOf(teamIndex);
            for (int i = 0; i < list.Count; i++) TestResetCooldowns(list[i]);
        }

        /// <summary>Сервер: обнулить откаты умений одного юнита (штатные списки отката ассета).</summary>
        public void TestResetCooldowns(Unit u)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (u == null || u.dead) return;

            u.cooldownAbility.Clear();
            u.cooldownAbilityIndex.Clear();
            u.cooldownAbilityIsItem.Clear();
            u.OnRedrawAbilityView?.Invoke();
        }

        /// <summary>
        /// Живые юниты стороны — сканом сцены по владельцу (не teamUnits): в списке команды нет юнитов,
        /// расставленных в сцене руками, и объекта-кастера. Вызывать по событию, а не каждый кадр.
        /// </summary>
        public List<Unit> TestUnitsOf(int teamIndex)
        {
            List<Unit> result = new List<Unit>();
            TeamWaveConfig cfg = Team(teamIndex);
            if (cfg == null) return result;

            Unit[] all = UnityEngine.Object.FindObjectsByType<Unit>(FindObjectsInactive.Exclude);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && !all[i].dead && all[i].owner == cfg.ownerPlayer) result.Add(all[i]);

            return result;
        }

        /// <summary>Сколько живых БОЕВЫХ юнитов у стороны (для счётчика в шапке панели).</summary>
        public int TestUnitCount(int teamIndex)
        {
            List<Unit> list = TestUnitsOf(teamIndex);
            int count = 0;
            for (int i = 0; i < list.Count; i++)
                if (list[i].unitType == UnitType.Unit) count++;
            return count;
        }

        // ======================== ТЕХНОЛОГИИ ========================

        /// <summary>Все технологии проекта (ключи штатного дерева TechnologyManager), по алфавиту.</summary>
        public List<Technology> TestAllTechs()
        {
            List<Technology> result = new List<Technology>();
            TechnologyManager tm = TechnologyManager.Instance;
            if (tm == null || tm.TechTree == null || tm.TechTree.Length == 0 || tm.TechTree[0] == null) return result;

            foreach (KeyValuePair<Technology, bool> kv in tm.TechTree[0])
                if (kv.Key != null) result.Add(kv.Key);

            result.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            return result;
        }

        /// <summary>Открыта ли технология у стороны (чтение — безопасно и на клиенте).</summary>
        public bool TestIsTechUnlocked(int teamIndex, Technology tech)
        {
            TechnologyManager tm = TechnologyManager.Instance;
            if (tm == null || tech == null || !TechPlayerValid(teamIndex, out int player)) return false;
            if (tm.TechTree == null || tm.TechTree[player] == null || !tm.TechTree[player].ContainsKey(tech)) return false;
            return tm.isUnlocked(tech, player);
        }

        // Игрок-владелец стороны, пригодный для индексации дерева технологий. Дерево строится по всем слотам
        // (Resources.LoadAll в TechnologyManager.Initialize), поэтому проверяем и диапазон, и наличие ключа —
        // иначе на неверном ownerPlayer падаем в IndexOutOfRange/KeyNotFound внутри ассета.
        bool TechPlayerValid(int teamIndex, out int player)
        {
            player = TestOwnerOf(teamIndex);
            TechnologyManager tm = TechnologyManager.Instance;
            bool ok = player >= 0 && tm != null && tm.TechTree != null && player < tm.TechTree.Length;
            if (!ok) Debug.LogWarning($"[Полигон] У стороны {TestSideName(teamIndex)} владелец {player} вне дерева технологий — операция пропущена.");
            return ok;
        }

        /// <summary>
        /// Сервер: открыть или закрыть ОДНУ технологию стороне, без цен и без прохода по дереву тиров.
        /// После закрытия замки умений пересчитываются принудительно: юниты подписаны только на открытие.
        /// </summary>
        public void TestSetTech(int teamIndex, Technology tech, bool unlocked)
        {
            if (NetworkConnectionHandler.isClient) return;
            TechnologyManager tm = TechnologyManager.Instance;
            if (tm == null || tech == null || !TechPlayerValid(teamIndex, out int player)) return;
            if (tm.TechTree[player] == null || !tm.TechTree[player].ContainsKey(tech)) return;

            if (unlocked) tm.UnlockTech(tech, player);   // ассет сам поднимет OnTechUnlock → юниты пересчитаются
            else
            {
                tm.LockTech(tech, player);
                TestRefreshAbilityLocks(teamIndex);      // на закрытие юниты не подписаны — пересчитываем руками
            }
        }

        /// <summary>Сервер: то же для списка технологий — с одним пересчётом замков в конце.</summary>
        public void TestSetTechBatch(int teamIndex, IList<Technology> techs, bool unlocked)
        {
            if (NetworkConnectionHandler.isClient) return;
            TechnologyManager tm = TechnologyManager.Instance;
            if (tm == null || techs == null || !TechPlayerValid(teamIndex, out int player)) return;

            for (int i = 0; i < techs.Count; i++)
            {
                if (techs[i] == null) continue;
                if (tm.TechTree[player] == null || !tm.TechTree[player].ContainsKey(techs[i])) continue;
                if (unlocked) tm.UnlockTech(techs[i], player);
                else tm.LockTech(techs[i], player);
            }

            if (!unlocked) TestRefreshAbilityLocks(teamIndex); // на открытие пересчёт делает сам ассет
        }

        /// <summary>
        /// Принудительный пересчёт замков и уровней умений у всех юнитов стороны. Нужен при ЗАКРЫТИИ технологии:
        /// штатно юниты пересчитываются только по событию открытия (Unit.Init: подписка на OnTechUnlock).
        /// ЦЕНА: пересчёт ассета для умений БЕЗ требований зовёт Unlock безусловно (Unit.Ability.cs) — повторный
        /// Unlock у пассивки должен быть идемпотентным сам (CompositePassive им и является; цикл аур снесён Б7).
        /// Поэтому на ОТКРЫТИЕ технологии отсюда не зовём: там пересчёт уже делает сам ассет по OnTechUnlock.
        /// </summary>
        public void TestRefreshAbilityLocks(int teamIndex)
        {
            List<Unit> list = TestUnitsOf(teamIndex);
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && !list[i].dead) list[i].AllAbilityLockLevelsCalculate();
        }

        // ======================== РАСЫ И СТОРОНЫ ========================

        /// <summary>
        /// Назначить стороне расу ЯВНЫМ ассетом, минуя playerFaction и GameManager.factionData: при прямом
        /// запуске сцены слоты не разыгрываются и штатный резолв дал бы обеим сторонам одну расу.
        /// Это конфиг, а не игровое действие: детерминирован и идёт на всех пирах (как ApplyFactions в Awake).
        /// </summary>
        public void TestApplyFaction(int teamIndex, FactionConfig faction)
        {
            TeamWaveConfig cfg = Team(teamIndex);
            if (cfg == null || faction == null) return;

            ApplyFactionContent(cfg, faction);
            AssignCasterAbilities(cfg);
        }

        /// <summary>Игрок-владелец стороны; -1, если конфига нет.</summary>
        public int TestOwnerOf(int teamIndex)
        {
            TeamWaveConfig cfg = Team(teamIndex);
            return cfg != null ? cfg.ownerPlayer : -1;
        }

        /// <summary>Сторона (0 = A, 1 = B) по игроку-владельцу; -1, если ни одна сторона им не владеет.</summary>
        public int TestTeamOfOwner(int player) => TeamIndexOfOwner(player);

        /// <summary>Имя стороны для логов и подписей.</summary>
        public static string TestSideName(int teamIndex) => teamIndex == 0 ? "A" : "B";
    }
}
