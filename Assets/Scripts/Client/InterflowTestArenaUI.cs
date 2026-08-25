using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StrategyCore
{
    /// <summary>
    /// [Interflow 2026-08-24] Панель тестового полигона: призыв юнитов, точечное открытие технологий, проверка
    /// умений выделенного юнита и уборка поля. Открывается клавишей F9 прямо в игре.
    ///
    /// Панель НИЧЕГО не делает сама: любое изменение мира идёт через открытое API MatchManager (партиал
    /// MatchManager.TestArena.cs), у которого свои серверные гейты (правило 6). Клиентская сборка
    /// (Interflow.Client, defineConstraints !UNITY_SERVER) — на выделенном сервере панели нет.
    ///
    /// Рисуется на IMGUI намеренно: это отладочный инструмент, ему не нужны ассеты UI Toolkit,
    /// вёрстка и место в дереве игрового интерфейса.
    /// </summary>
    public class InterflowTestArenaUI : MonoBehaviour
    {
        enum Tab { Units, Techs, Abilities, Field }

        [Header("Панель")]
        [Tooltip("Клавиша показа и скрытия панели.")]
        [SerializeField] Key toggleKey = Key.F9;
        [Tooltip("Ширина панели в пикселях.")]
        [SerializeField] float panelWidth = 560f;
        [Tooltip("Высота панели в пикселях.")]
        [SerializeField] float panelHeight = 640f;
        [Tooltip("Как часто пересчитывать счётчики живых юнитов, сек. Скан сцены — не каждый кадр.")]
        [SerializeField] float countersRefresh = 0.5f;

        // Постоянный номер окна IMGUI: GetInstanceID помечен устаревшим в этой версии Unity.
        const int WindowId = 32410;

        bool shown;
        Tab tab = Tab.Units;
        Rect window = new Rect(20, 20, 560, 640);

        // Вкладка «Юниты»
        string unitFilter = "";
        bool includeBuildings;
        string countText = "1";
        string spreadText = "6";
        TestArenaOrder order = TestArenaOrder.AsInGame;
        int spawnAt;                       // 0 — база стороны призыва, 1 — центр
        Vector2 unitsScroll;

        // Вкладка «Технологии»
        string techFilter = "";
        Vector2 techScroll;
        List<Technology> techs;

        // Вкладка «Умения»
        Vector2 abilityScroll;

        // Счётчики
        float countersNext;
        int countA, countB;

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard[toggleKey].wasPressedThisFrame) shown = !shown;
        }

        void OnGUI()
        {
            if (!shown) return;
            if (MatchManager.instance == null) return;

            if (Time.unscaledTime >= countersNext)
            {
                countersNext = Time.unscaledTime + Mathf.Max(0.1f, countersRefresh);
                countA = MatchManager.instance.TestUnitCount(0);
                countB = MatchManager.instance.TestUnitCount(1);
            }

            window.width = panelWidth;
            window.height = panelHeight;
            window = GUILayout.Window(WindowId, window, DrawWindow, "Тестовый полигон (F9)");
        }

        void DrawWindow(int id)
        {
            DrawHeader();
            DrawTabs();

            switch (tab)
            {
                case Tab.Units: DrawUnits(); break;
                case Tab.Techs: DrawTechs(); break;
                case Tab.Abilities: DrawAbilities(); break;
                case Tab.Field: DrawField(); break;
            }

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        // ======================== ШАПКА ========================

        void DrawHeader()
        {
            SlotManager sm = SlotManager.instance;
            MatchManager mm = MatchManager.instance;
            int side = mm.TestTeamOfOwner(sm != null ? sm.currentPlayer : -1);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Играю за:", GUILayout.Width(70));
            if (GUILayout.Toggle(side == 0, "A", GUI.skin.button, GUILayout.Width(40)) && side != 0) SwitchSide(0);
            if (GUILayout.Toggle(side == 1, "B", GUI.skin.button, GUILayout.Width(40)) && side != 1) SwitchSide(1);

            if (sm != null)
            {
                bool debug = GUILayout.Toggle(sm.debugMode, "Отладка", GUILayout.Width(80));
                if (debug != sm.debugMode) sm.debugMode = debug;
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label($"живых: A {countA} / B {countB}");
            GUILayout.EndHorizontal();
        }

        // Переключение активной стороны: игрок, команда, туман войны и панели интерфейса.
        void SwitchSide(int teamIndex)
        {
            MatchManager mm = MatchManager.instance;
            SlotManager sm = SlotManager.instance;
            if (mm == null || sm == null) return;

            int owner = mm.TestOwnerOf(teamIndex);
            if (owner < 0) { Debug.LogWarning($"[Полигон] У стороны {MatchManager.TestSideName(teamIndex)} нет владельца."); return; }

            sm.SetCurrentPlayer(owner);                       // штатная точка: currentPlayer + currentTeam + имя
            Presentation.Selection?.ResetSelection();          // выделение чужого юнита после смены стороны не держим
            GameManager.instance.OnTeamChange?.Invoke();       // юниты перечитывают принадлежность, туман — команду

            UIManager ui = UIManager.instance;
            if (ui != null)
            {
                ui.ResetUnitUI();
                ui.RefreshResourceTab();
                ui.RefreshTechTree();
                ui.RefreshWavePanel();
            }

            Debug.Log($"[Полигон] Играю за сторону {MatchManager.TestSideName(teamIndex)} (игрок {owner}).");
        }

        void DrawTabs()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(tab == Tab.Units, "Юниты", GUI.skin.button)) tab = Tab.Units;
            if (GUILayout.Toggle(tab == Tab.Techs, "Технологии", GUI.skin.button)) tab = Tab.Techs;
            if (GUILayout.Toggle(tab == Tab.Abilities, "Умения", GUI.skin.button)) tab = Tab.Abilities;
            if (GUILayout.Toggle(tab == Tab.Field, "Поле", GUI.skin.button)) tab = Tab.Field;
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
        }

        // ======================== ВКЛАДКА «ЮНИТЫ» ========================

        void DrawUnits()
        {
            InterflowTestArena arena = InterflowTestArena.instance;
            if (arena == null) { GUILayout.Label("На сцене нет объекта с InterflowTestArena — каталог недоступен."); return; }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Поиск", GUILayout.Width(45));
            unitFilter = GUILayout.TextField(unitFilter, GUILayout.Width(180));
            includeBuildings = GUILayout.Toggle(includeBuildings, "и здания", GUILayout.Width(90));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Кол-во", GUILayout.Width(45));
            countText = GUILayout.TextField(countText, GUILayout.Width(40));
            GUILayout.Label("Разброс", GUILayout.Width(55));
            spreadText = GUILayout.TextField(spreadText, GUILayout.Width(40));
            GUILayout.Label("Приказ", GUILayout.Width(45));
            if (GUILayout.Toggle(order == TestArenaOrder.AsInGame, "Как в бою", GUI.skin.button)) order = TestArenaOrder.AsInGame;
            if (GUILayout.Toggle(order == TestArenaOrder.Hold, "Стоять", GUI.skin.button)) order = TestArenaOrder.Hold;
            if (GUILayout.Toggle(order == TestArenaOrder.Dummy, "Манекен", GUI.skin.button)) order = TestArenaOrder.Dummy;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Ставить", GUILayout.Width(50));
            if (GUILayout.Toggle(spawnAt == 0, "у базы стороны", GUI.skin.button)) spawnAt = 0;
            if (GUILayout.Toggle(spawnAt == 1, "в центре", GUI.skin.button)) spawnAt = 1;
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            List<Unit> catalog = arena.Catalog(includeBuildings);
            unitsScroll = GUILayout.BeginScrollView(unitsScroll);
            int shown = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                Unit prefab = catalog[i];
                if (prefab == null) continue;
                if (!Matches(prefab.name, unitFilter)) continue;
                shown++;

                GUILayout.BeginHorizontal();
                GUILayout.Label(prefab.name, GUILayout.Width(260));
                if (GUILayout.Button("За A", GUILayout.Width(60))) Summon(prefab, 0);
                if (GUILayout.Button("За B", GUILayout.Width(60))) Summon(prefab, 1);
                GUILayout.EndHorizontal();
            }
            if (shown == 0) GUILayout.Label("Ничего не найдено.");
            GUILayout.EndScrollView();
            GUILayout.Label($"Записей: {shown} из {catalog.Count}");
        }

        void Summon(Unit prefab, int teamIndex)
        {
            InterflowTestArena arena = InterflowTestArena.instance;
            if (arena == null) return;

            if (!int.TryParse(countText, out int count) || count <= 0) count = 1;
            if (!float.TryParse(spreadText, out float spread) || spread < 0f) spread = arena.DefaultSpread;

            Vector3 at = spawnAt == 1 ? arena.CenterPosition : arena.SpawnPositionOf(teamIndex);
            int spawned = MatchManager.instance.TestSpawnMany(prefab, count, at, teamIndex, spread, order);
            Debug.Log($"[Полигон] За сторону {MatchManager.TestSideName(teamIndex)} призвано {spawned} × '{prefab.name}'.");
        }

        // ======================== ВКЛАДКА «ТЕХНОЛОГИИ» ========================

        void DrawTechs()
        {
            MatchManager mm = MatchManager.instance;
            // Пустой список не кэшируем: TechnologyManager мог быть ещё не готов на первом открытии вкладки.
            if (techs == null || techs.Count == 0) techs = mm.TestAllTechs();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Поиск", GUILayout.Width(45));
            techFilter = GUILayout.TextField(techFilter, GUILayout.Width(200));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            List<Technology> filtered = new List<Technology>();
            for (int i = 0; i < techs.Count; i++)
                if (techs[i] != null && Matches(techs[i].name, techFilter)) filtered.Add(techs[i]);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Ко всему в фильтре:", GUILayout.Width(130));
            if (GUILayout.Button("A вкл", GUILayout.Width(60))) mm.TestSetTechBatch(0, filtered, true);
            if (GUILayout.Button("A выкл", GUILayout.Width(60))) mm.TestSetTechBatch(0, filtered, false);
            if (GUILayout.Button("B вкл", GUILayout.Width(60))) mm.TestSetTechBatch(1, filtered, true);
            if (GUILayout.Button("B выкл", GUILayout.Width(60))) mm.TestSetTechBatch(1, filtered, false);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            techScroll = GUILayout.BeginScrollView(techScroll);
            for (int i = 0; i < filtered.Count; i++)
            {
                Technology tech = filtered[i];
                bool openA = mm.TestIsTechUnlocked(0, tech);
                bool openB = mm.TestIsTechUnlocked(1, tech);

                GUILayout.BeginHorizontal();
                GUILayout.Label(tech.name, GUILayout.Width(320));
                bool newA = GUILayout.Toggle(openA, "A", GUILayout.Width(40));
                bool newB = GUILayout.Toggle(openB, "B", GUILayout.Width(40));
                GUILayout.EndHorizontal();

                if (newA != openA) mm.TestSetTech(0, tech, newA);
                if (newB != openB) mm.TestSetTech(1, tech, newB);
            }
            GUILayout.EndScrollView();
            GUILayout.Label($"Технологий: {filtered.Count} из {techs.Count}");
        }

        // ======================== ВКЛАДКА «УМЕНИЯ» ========================

        void DrawAbilities()
        {
            Unit u = PlayerControl.instance != null ? PlayerControl.instance.activeUnit : null;
            if (u == null || u.dead) { GUILayout.Label("Выдели юнита щелчком — здесь будут его умения."); return; }

            MatchManager mm = MatchManager.instance;
            GUILayout.Label($"{u.unitName} (игрок {u.owner}) — здоровье {u.health:0}/{u.maxHealth:0}, " +
                            $"мана {u.mana:0}/{u.maxMana:0}, состояние {u.unitState}");

            GUILayout.BeginHorizontal();
            bool dummy = GUILayout.Toggle(u.isInvulnerable, "Манекен", GUILayout.Width(90));
            if (dummy != u.isInvulnerable)
            {
                u.isInvulnerable = dummy;
                if (dummy) u.Hold();
            }

            AutoAbilityUser auto = u.GetComponent<AutoAbilityUser>();
            if (auto != null)
            {
                bool autoOn = GUILayout.Toggle(!auto.testAutoCastSuspended, "Авто-применение", GUILayout.Width(150));
                auto.testAutoCastSuspended = !autoOn;
            }
            else GUILayout.Label("авто-умений нет", GUILayout.Width(150));

            if (GUILayout.Button("Откат 0", GUILayout.Width(80))) mm.TestResetCooldowns(u);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            int count = u.abilityLocked != null ? u.abilityLocked.Length : 0;
            if (count == 0) { GUILayout.Label("У юнита нет умений."); return; }

            abilityScroll = GUILayout.BeginScrollView(abilityScroll);
            for (int i = 0; i < count; i++)
            {
                Ability ability = Utils.GetAbilityByIndex(u, i);
                if (ability == null) continue;

                bool locked = u.abilityLocked[i];
                int level = (u.abilityLevel != null && i < u.abilityLevel.Length) ? u.abilityLevel[i] : 0;
                float cooldown = u.GetAbilityCooldown(i, false);
                bool isAuto = auto != null && IsAutoAbility(auto, ability);

                GUILayout.BeginHorizontal();
                GUILayout.Label($"{(locked ? "замок" : "  •  ")} {ability.name}{(isAuto ? " (авто)" : "")}", GUILayout.Width(300));
                GUILayout.Label($"ур. {level}", GUILayout.Width(50));
                GUILayout.Label(cooldown > 0f ? $"откат {cooldown:0.0}" : "готово", GUILayout.Width(80));
                if (GUILayout.Button("Применить", GUILayout.Width(90)))
                    u.UseAbilityItem(i, false, u.target, Vector3.zero, true);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        static bool IsAutoAbility(AutoAbilityUser auto, Ability ability)
        {
            CompositeSkill[] list = auto.AutoAbilities;
            if (list == null) return false;
            for (int i = 0; i < list.Length; i++) if (list[i] == ability) return true;
            return false;
        }

        // ======================== ВКЛАДКА «ПОЛЕ» ========================

        void DrawField()
        {
            MatchManager mm = MatchManager.instance;

            GUILayout.Label("Убрать юнитов (здания и кастера умений не трогаем):");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Сторону A")) Debug.Log($"[Полигон] Убрано юнитов A: {mm.TestClear(0)}.");
            if (GUILayout.Button("Сторону B")) Debug.Log($"[Полигон] Убрано юнитов B: {mm.TestClear(1)}.");
            if (GUILayout.Button("Убрать всех")) Debug.Log($"[Полигон] Убрано юнитов всего: {mm.TestClear(-1)}.");
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            GUILayout.Label("Восстановить здоровье и ману:");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("A")) mm.TestRestoreVitals(0);
            if (GUILayout.Button("B")) mm.TestRestoreVitals(1);
            if (GUILayout.Button("Всем")) mm.TestRestoreVitals(-1);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            GUILayout.Label("Сбросить откаты умений:");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("A")) mm.TestResetCooldowns(0);
            if (GUILayout.Button("B")) mm.TestResetCooldowns(1);
            if (GUILayout.Button("Всем")) { mm.TestResetCooldowns(0); mm.TestResetCooldowns(1); }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            GUILayout.Label("Пересчитать замки умений (нужно после закрытия технологии):");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("A")) mm.TestRefreshAbilityLocks(0);
            if (GUILayout.Button("B")) mm.TestRefreshAbilityLocks(1);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            bool paused = GUILayout.Toggle(mm.testWavesPaused, "Волны на паузе");
            if (paused != mm.testWavesPaused) mm.testWavesPaused = paused;
            GUILayout.Space(4);

            GUILayout.Label($"Скорость времени: ×{Time.timeScale:0.##}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("×0.25")) Time.timeScale = 0.25f;
            if (GUILayout.Button("×0.5")) Time.timeScale = 0.5f;
            if (GUILayout.Button("×1")) Time.timeScale = 1f;
            if (GUILayout.Button("×2")) Time.timeScale = 2f;
            if (GUILayout.Button("×4")) Time.timeScale = 4f;
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            GUILayout.Label($"Уровень логов Interflow: {LevelName(InterflowDebug.level)}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Тишина")) InterflowDebug.level = InterflowDebug.Level.Off;
            if (GUILayout.Button("События")) InterflowDebug.level = InterflowDebug.Level.Events;
            if (GUILayout.Button("Подробно")) InterflowDebug.level = InterflowDebug.Level.Verbose;
            GUILayout.EndHorizontal();
        }

        static string LevelName(InterflowDebug.Level level)
        {
            if (level == InterflowDebug.Level.Off) return "тишина";
            if (level == InterflowDebug.Level.Verbose) return "подробно";
            return "события";
        }

        // ======================== СЛУЖЕБНОЕ ========================

        static bool Matches(string name, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            return name != null && name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
