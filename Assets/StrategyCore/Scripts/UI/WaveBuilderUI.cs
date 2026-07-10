using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Конструктор состава волны — отдельный компонент (логика волн вынесена из UIManager).
    /// Доступные юниты берутся из FactionConfig расы через MatchManager.AvailableWaveUnits(team)
    /// (эффективный набор с учётом правил апгрейдов). Показывается набор команды локального игрока
    /// (UIManager.CommandTeamSlot). На каждый юнит — две кнопки: «+» (добавить в волну) и «−» (убрать),
    /// дёргают штатные MatchManager.TryAddWaveUnit / TryRemoveWaveUnit (серверо-авторитетно).
    /// Кнопки выводятся в нижнюю таблицу UIManager через её публичное API (своей разметки компонент не создаёт).
    /// Все ссылки/наборы/иконки — в Inspector (правило «всё в Inspector»).
    /// </summary>
    public class WaveBuilderUI : MonoBehaviour
    {
        [Header("Связи")]
        [Tooltip("UIManager с нижними таблицами — туда выводятся кнопки конструктора.")]
        [SerializeField] UIManager uiManager;

        [Tooltip("Имя панели-сетки (SlotPanelConfig.name в UIManager.slotPanels) под кнопки волны. " +
                 "Панель: anchor=BottomRight, rows=4, cols=4 (ряд юнита = иконка/+/−/инфо).")]
        [SerializeField] string waveSlotPanelName = "Волна";

        [Header("Иконки кнопок")]
        [Tooltip("Иконка кнопки «+» (добавить юнит в волну).")]
        [SerializeField] Texture2D addIcon;

        [Tooltip("Иконка кнопки «−» (убрать юнит из волны).")]
        [SerializeField] Texture2D removeIcon;

        bool wired;   // подписка и первая отрисовка выполнены (панель и MatchManager готовы)
        int lastBuiltTeam = -1;   // [Interflow fix 2026-06-26] команда, под которую отрисованы кнопки
        int waveSlotPanelIndex = -1;   // индекс панели-сетки волны (резолв по имени в TryWire)

        void Start() => TryWire();

        // UIManager и MatchManager могут инициализироваться позже — повторяем, пока не готово.
        void LateUpdate()
        {
            if (!wired) { TryWire(); return; }
            // [Interflow fix 2026-06-26] У клиента команда локального игрока (CommandTeamSlot) резолвится ПОЗЖЕ первой отрисовки.
            // Без пересборки кнопки несут устаревший team → сервер режет запрос состава по owner-гейту. Пересобираем при смене команды.
            if (uiManager != null && uiManager.CommandTeamSlot != lastBuiltTeam) Rebuild();
        }

        void OnDestroy()
        {
            if (MatchManager.instance != null)
            {
                MatchManager.instance.OnWaveCompositionChanged -= OnWaveCompositionChanged;
                MatchManager.instance.OnTeamContentChanged -= OnWaveCompositionChanged;
            }
        }

        // Готовимся один раз: ждём готовности панелей-сеток и MatchManager (нужен для цен/количества),
        // подписываемся на изменение состава волны и делаем первую отрисовку.
        void TryWire()
        {
            if (wired) return;
            if (uiManager == null || !uiManager.SlotPanelsReady) return;      // ждём готовности панелей-сеток
            if (MatchManager.instance == null) return;                       // нужен для цен и кол-ва

            // Панель волны ищем по имени (индексы slotPanels могут меняться при добавлении панелей).
            waveSlotPanelIndex = uiManager.SlotPanelIndexByName(waveSlotPanelName);
            if (waveSlotPanelIndex < 0)
            {
                Debug.LogWarning($"[WaveBuilderUI] Панель-сетка волны '{waveSlotPanelName}' не найдена в UIManager.slotPanels — задай имя панели.");
                return;   // повторим в LateUpdate
            }

            wired = true;
            MatchManager.instance.OnWaveCompositionChanged += OnWaveCompositionChanged;
            MatchManager.instance.OnTeamContentChanged += OnWaveCompositionChanged; // апгрейды: своп изменил проекцию иконок → перерисовать
            Rebuild();
        }

        // Состав волны изменился — перерисовываем только для своей команды (живое кол-во в ячейке).
        void OnWaveCompositionChanged(int teamIndex)
        {
            if (uiManager != null && teamIndex == uiManager.CommandTeamSlot) Rebuild();
        }

        // Строит кнопки набора локальной команды и кладёт их в нижнюю таблицу UIManager.
        // Ряд юнита: [иконка] [+] [−] [цена/кол-во]. Цена и кол-во — из MatchManager (единый источник).
        void Rebuild()
        {
            if (uiManager == null || !uiManager.SlotPanelsReady || waveSlotPanelIndex < 0) return;

            int team = uiManager.CommandTeamSlot;
            lastBuiltTeam = team;   // [Interflow fix 2026-06-26] фиксируем отрисованную команду

            MatchManager mm = MatchManager.instance;
            // Доступные юниты — из FactionConfig расы (эффективный набор с учётом правил апгрейдов).
            IReadOnlyList<Unit> units = mm != null ? mm.AvailableWaveUnits(team) : null;
            if (units == null || units.Count == 0)
            {
                // Доступные юниты расы не заданы (FactionConfig.availableWaveUnits пуст) → кнопок нет.
                Debug.LogWarning($"[WaveBuilderUI] Нет доступных юнитов для команды {team} — заполни availableWaveUnits в FactionConfig расы.");
                uiManager.SetSlotPanelButtons(waveSlotPanelIndex, null);
                return;
            }

            List<BottomTableButton> buttons = new List<BottomTableButton>();
            for (int i = 0; i < units.Count; i++)
            {
                Unit prefab = units[i];
                if (prefab == null) continue;
                Unit captured = prefab;   // фиксируем для замыканий кнопок (+/− работают по БАЗОВОМУ типу — состав хранит базу)

                // Иконка — спроецированного юнита (unitSwaps): игрок видит, что реально заспавнится.
                Unit displayPrefab = mm != null ? mm.ResolveSwap(team, prefab) : prefab;
                buttons.Add(new BottomTableButton { icon = displayPrefab != null ? displayPrefab.icon : prefab.icon });
                // «+» и «−»: action несёт команду и юнит-идентификатор → TryAdd/TryRemove.
                buttons.Add(new BottomTableButton { icon = addIcon,    onClick = () => OnWaveButton(team, captured, true) });
                buttons.Add(new BottomTableButton { icon = removeIcon, onClick = () => OnWaveButton(team, captured, false) });
                // Текст: сверху цена «золото / лидерство», снизу текущее кол-во в волне.
                buttons.Add(BuildInfoCell(mm, team, prefab));
            }

            uiManager.SetSlotPanelButtons(waveSlotPanelIndex, buttons);
        }

        // Информационная ячейка: цена (золото / лидерство) сверху, текущее кол-во в волне снизу.
        // Данные — из MatchManager (EffectiveResourceCost с учётом B28 + WaveUnitCount), без дублирования цен.
        static BottomTableButton BuildInfoCell(MatchManager mm, int team, Unit prefab)
        {
            int gold       = mm != null ? mm.EffectiveResourceCost(team, prefab, mm.GoldResource) : 0;
            int leadership = mm != null ? mm.EffectiveResourceCost(team, prefab, mm.LeadershipResource) : 0;
            int count      = mm != null ? mm.WaveUnitCount(team, prefab) : 0;

            return new BottomTableButton
            {
                topText    = gold + " / " + leadership,
                bottomText = count.ToString(),
                displayOnly = true,
            };
        }

        // Единый action кнопки: команда (0/1) + префаб-идентификатор + направление (добавить/убрать).
        // Хост правит состав напрямую; клиент — через ServerRpc (логика спавна/состава серверо-авторитетна).
        void OnWaveButton(int team, Unit prefab, bool add)
        {
            if (prefab == null) return;

            if (!NetworkConnectionHandler.isClient)
            {
                MatchManager mm = MatchManager.instance;
                if (mm == null) return;
                if (add) mm.TryAddWaveUnit(team, prefab);
                else     mm.TryRemoveWaveUnit(team, prefab);
            }
            else if (NetworkDataSync.instance != null)
            {
                NetworkDataSync.instance.WaveCompositionChangeServerRpc(team, prefab.unitTypeID, add);
            }
        }
    }
}
