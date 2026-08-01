using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Волна 2.0 — ВРЕМЕННАЯ панель пометок состава (для теста; красивый финальный UI — отдельный этап).
    /// Доступные юниты — из MatchManager.AvailableWaveUnits(team) (эффективный набор с учётом апгрейдов).
    /// На каждый юнит ряд: [иконка] [Авто] [Разовый] [Снять] [инфо: цена ×count / состояние пометки].
    /// Кнопки дёргают серверные пометки TrySetAuto / TrySetOneShot / TryClearMark (хост — напрямую, клиент — через RPC).
    /// ВРЕМЕННО: вместо утверждённого жеста «одинарный/двойной клик» — явные кнопки (надёжнее для теста).
    /// Финальный жест — на UI-этапе. Все ссылки/иконки — в Inspector.
    /// </summary>
    public class WaveBuilderUI : MonoBehaviour
    {
        [Header("Связи")]
        [Tooltip("UIManager с нижними таблицами — туда выводятся кнопки панели.")]
        [SerializeField] UIManager uiManager;

        [Tooltip("Имя панели-сетки (SlotPanelConfig.name в UIManager.slotPanels) под кнопки волны. " +
                 "Ряд юнита: иконка/Авто/Разовый/Снять/инфо (cols≥5).")]
        [SerializeField] string waveSlotPanelName = "Волна";

        [Header("Иконки кнопок (временная панель)")]
        [Tooltip("Иконка кнопки «Автопризыв» (пометить тип на каждую волну).")]
        [SerializeField] Texture2D autoIcon;
        [Tooltip("Иконка кнопки «Разовый призыв» (одна волна, с резервом золота).")]
        [SerializeField] Texture2D oneShotIcon;
        [Tooltip("Иконка кнопки «Снять пометку».")]
        [SerializeField] Texture2D clearIcon;

        bool wired;
        int lastBuiltTeam = -1;
        int waveSlotPanelIndex = -1;

        void Start() => TryWire();

        void LateUpdate()
        {
            if (!wired) { TryWire(); return; }
            // [Interflow fix 2026-06-26] У клиента команда локального игрока резолвится позже первой отрисовки — пересобираем при смене.
            if (uiManager != null && uiManager.CommandTeamSlot != lastBuiltTeam) Rebuild();
        }

        void OnDestroy()
        {
            if (MatchManager.instance != null)
            {
                MatchManager.instance.OnWaveMarksChanged -= OnMarksChanged;
                MatchManager.instance.OnTeamContentChanged -= OnMarksChanged;
            }
        }

        void TryWire()
        {
            if (wired) return;
            if (uiManager == null || !uiManager.SlotPanelsReady) return;
            if (MatchManager.instance == null) return;

            waveSlotPanelIndex = uiManager.SlotPanelIndexByName(waveSlotPanelName);
            if (waveSlotPanelIndex < 0)
            {
                Debug.LogWarning($"[WaveBuilderUI] Панель-сетка волны '{waveSlotPanelName}' не найдена в UIManager.slotPanels — задай имя панели.");
                return;
            }

            wired = true;
            MatchManager.instance.OnWaveMarksChanged += OnMarksChanged;
            MatchManager.instance.OnTeamContentChanged += OnMarksChanged; // апгрейды: набор доступных/свопы изменились → перерисовать
            Rebuild();
        }

        void OnMarksChanged(int teamIndex)
        {
            if (uiManager != null && teamIndex == uiManager.CommandTeamSlot) Rebuild();
        }

        // Строит ряды кнопок доступных юнитов локальной команды в нижней таблице UIManager.
        void Rebuild()
        {
            if (uiManager == null || !uiManager.SlotPanelsReady || waveSlotPanelIndex < 0) return;

            int team = uiManager.CommandTeamSlot;
            lastBuiltTeam = team;

            MatchManager mm = MatchManager.instance;
            IReadOnlyList<Unit> units = mm != null ? mm.AvailableWaveUnits(team) : null;
            if (units == null || units.Count == 0)
            {
                Debug.LogWarning($"[WaveBuilderUI] Нет доступных юнитов для команды {team} — добавь юнитов с ролью «Доступный» в waveUnits FactionConfig расы (или открой их технологией).");
                uiManager.SetSlotPanelButtons(waveSlotPanelIndex, null);
                return;
            }

            List<BottomTableButton> buttons = new List<BottomTableButton>();
            for (int i = 0; i < units.Count; i++)
            {
                Unit prefab = units[i];
                if (prefab == null) continue;
                Unit captured = prefab;

                // Иконка — спроецированного юнита (unitSwaps): игрок видит, что реально заспавнится.
                Unit displayPrefab = mm != null ? mm.ResolveSwap(team, prefab) : prefab;
                buttons.Add(new BottomTableButton { icon = displayPrefab != null ? displayPrefab.icon : prefab.icon });
                buttons.Add(new BottomTableButton { icon = autoIcon,    onClick = () => OnMarkButton(team, captured, 0) });
                buttons.Add(new BottomTableButton { icon = oneShotIcon, onClick = () => OnMarkButton(team, captured, 1) });
                buttons.Add(new BottomTableButton { icon = clearIcon,   onClick = () => OnMarkButton(team, captured, 2) });
                buttons.Add(BuildInfoCell(mm, team, prefab));
            }

            uiManager.SetSlotPanelButtons(waveSlotPanelIndex, buttons);
        }

        // Инфо-ячейка: цена «золото / лидерство» ×count сверху, состояние пометки снизу (Авто/Разовый/—).
        static BottomTableButton BuildInfoCell(MatchManager mm, int team, Unit prefab)
        {
            int cnt   = mm != null ? mm.WaveCountOf(team, prefab) : 1;
            int gold  = mm != null ? mm.EffectiveResourceCost(team, prefab, mm.GoldResource) * cnt : 0;
            int lead  = mm != null ? mm.EffectiveResourceCost(team, prefab, mm.LeadershipResource) * cnt : 0;
            int state = mm != null ? mm.MarkState(team, prefab) : 0;
            string mark = state == 1 ? "Авто" : state == 2 ? "Разовый" : "—";

            return new BottomTableButton
            {
                topText    = gold + " / " + lead,
                bottomText = mark,
                displayOnly = true,
            };
        }

        // op: 0 — авто, 1 — разовый, 2 — снять. Хост правит напрямую; клиент — через ServerRpc.
        void OnMarkButton(int team, Unit prefab, int op)
        {
            if (prefab == null) return;

            if (!NetworkConnectionHandler.isClient)
            {
                MatchManager mm = MatchManager.instance;
                if (mm == null) return;
                if (op == 0) mm.TrySetAuto(team, prefab.unitTypeID);
                else if (op == 1) mm.TrySetOneShot(team, prefab.unitTypeID);
                else mm.TryClearMark(team, prefab.unitTypeID);
            }
            else if (NetworkDataSync.instance != null)
            {
                NetworkDataSync.instance.WaveMarkServerRpc(team, prefab.unitTypeID, op);
            }
        }
    }
}
