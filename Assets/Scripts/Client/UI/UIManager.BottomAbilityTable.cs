using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // UIManager.BottomAbilityTable.cs — ветка способностей и слоты умений ГЗ и героя. Вырезано 1:1 из UIManager.BottomTables.cs (разрезка на partial-ы, правило 22).
    public partial class UIManager
    {
        // --- Ветка способностей (центральная таблица) ---

        // Строит отдельный контейнер способностей и ставит его в нужную позицию нижнего ряда.
        // Тот же стиль/механика, что у общих таблиц (flex-wrap, .AbilityButton), но свои данные/рендер.
        void BuildAbilityTableBranch(float tableWidth)
        {
            VisualElement table = new VisualElement { name = "BottomAbilityTable" };
            table.style.flexDirection = FlexDirection.Row;
            table.style.flexWrap = Wrap.Wrap;
            table.style.width = tableWidth;
            // Клик — тот же обработчик: у Ability-ячеек userData не BottomTableButton, поэтому только подсветка.
            table.RegisterCallback<ClickEvent>(OnBottomTableClick);
            bottomTablesRoot.Add(table);
            bottomAbilityTableRoot = table;

            RenderAbilityTable();
        }

        // Перестраивает ячейки центральной таблицы из набора способностей текущей команды (commandTeamSlot).
        void RenderAbilityTable()
        {
            if (bottomAbilityTableRoot == null) return;

            bottomAbilityTableRoot.Clear();

            IReadOnlyList<Ability> abilities = CurrentTeamAbilities();
            int count = abilities != null ? abilities.Count : 0;

            // Занятые ячейки — иконки способностей.
            for (int i = 0; i < count; i++)
                bottomAbilityTableRoot.Add(BuildAbilityCell(abilities[i]));

            // Пустые ячейки до минимума — штатным методом ассета (рамка видна всегда).
            for (int i = count; i < bottomTableMinCells; i++)
                EmptyElementCreate(bottomAbilityTableRoot, i);
        }

        // Видимые способности ГЗ команды локального игрока — производное VisibleCentralAbilities(team).
        // Все способности на кастере с Awake; правила апгрейдов лишь показывают/скрывают (каст — по Ability.id).
        IReadOnlyList<Ability> CurrentTeamAbilities()
        {
            MatchManager mm = MatchManager.Instance;
            if (mm == null) return null;
            return mm.VisibleCentralAbilities(CommandTeamForLocalPlayer());
        }

        // Ячейка способности в стиле кнопки. Сама Ability хранится в userData (для будущей активации);
        // сейчас клик по ней даёт только подсветку (см. OnBottomTableClick).
        GroupBox BuildAbilityCell(Ability ability)
        {
            GroupBox cell = new GroupBox();
            cell.AddToClassList("AbilityButton");
            cell.userData = ability;

            GroupBox iconElement = new GroupBox();
            iconElement.AddToClassList("AbilityButtonIcon");
            if (ability != null && ability.icon != null && ability.icon.Length > 0 && ability.icon[0] != null)
                iconElement.style.backgroundImage = ability.icon[0];   // Texture2D → StyleBackground
            cell.Add(iconElement);

            return cell;
        }

        /// <summary>Перерисовать центральную таблицу способностей (напр. после смены команды игрока).</summary>
        public void RefreshAbilityTable()
        {
            RenderAbilityTable();
        }

        // --- Умения в панели-сетке (переделка UI, шаг 4; нижний ряд — Герой, шаг 5) ---

        /// <summary>
        /// Наполняет панель умений главного здания (1×3). Ячейка закреплена за умением: не открытое
        /// (или не настроенное) умение → пустая ячейка на своём месте, соседи не сдвигаются.
        /// Умения героя здесь больше не строятся — под них будет отдельная панель.
        /// </summary>
        public void RefreshAbilitySlots()
        {
            if (!SlotPanelsReady) return;
            int panel = SlotPanelIndexByName(abilitySlotPanelName);
            if (panel < 0) return;                       // панель не настроена в Inspector — тихо

            SetSlotPanelButtons(panel, BuildCentralAbilitySlots(CommandTeamForLocalPlayer()));
        }

        // Ячейки умений ГЗ: что лежит в ячейке — знает MatchManager (стартовые умения расы + открытые узлами
        // дерева). Пустая ячейка → null. Каст переиспользует ActivateAbilityCell (серверо-авторитетно).
        List<BottomTableButton> BuildCentralAbilitySlots(int team)
        {
            List<BottomTableButton> res = new List<BottomTableButton>();
            MatchManager mm = MatchManager.Instance;

            for (int slot = 0; slot < MatchManager.CentralAbilitySlotCount; slot++)
            {
                Ability ab = mm != null ? mm.CentralAbilityAtSlot(team, slot) : null;
                if (ab == null) { res.Add(null); continue; }   // пустая ячейка на своём месте

                Ability captured = ab;   // фиксируем для замыкания onClick
                Texture2D tex = (ab.icon != null && ab.icon.Length > 0) ? ab.icon[0] : null;
                res.Add(new BottomTableButton { icon = tex, onClick = () => ActivateAbilityCell(captured) });
            }
            return res;
        }

        // ВНИМАНИЕ: методы ряда героя ниже сейчас НИКЕМ не вызываются — ряд убран из панели умений ГЗ
        // (решение Artsiom 2026-07-24: панель ГЗ = 1×3). Код оставлен как заготовка под отдельную панель героя.
        //
        // Слоты умений Героя (нижний ряд): показываем ТОЛЬКО когда герой призван/жив (реш. Artsiom 1б).
        // Набор — способности живого героя (хост) или префаба расы (клиент/фоллбэк). Только Active (D3).
        // Замок по уровню героя снесён блоком Б8 (открытие только технологией): кнопки не запираются;
        // недоступный каст отклонит сервер (UseAbilityItem). Замок по технологии — панель героя Б9.
        List<BottomTableButton> BuildHeroAbilitySlots(int team, int count)
        {
            List<BottomTableButton> res = new List<BottomTableButton>();
            MatchManager mm = MatchManager.Instance;

            if (mm == null || !mm.HeroAlive(team))              // 1б: до призыва / после смерти — пусто
            {
                for (int i = 0; i < count; i++) res.Add(null);
                return res;
            }

            IReadOnlyList<Ability> all = HeroAbilitySet(team);
            for (int i = 0; i < count; i++)
            {
                Ability ab = (all != null && i < all.Count) ? all[i] : null;
                if (ab != null && ab.type == AbilityType.Active)
                {
                    Ability captured = ab;   // фиксируем для замыкания onClick
                    Texture2D tex = (ab.icon != null && ab.icon.Length > 0) ? ab.icon[0] : null;
                    res.Add(new BottomTableButton
                    {
                        icon = tex,
                        onClick = () => CastHeroAbilityCell(team, captured.id),
                        // Замок по уровню героя снесён блоком Б8 (открытие только технологией); замок по технологии
                        // и предложение улучшений — панель героя Б9.
                        locked = false,
                    });
                }
                else
                {
                    res.Add(null);   // пустой слот на позиции (не-Active / нет умения)
                }
            }
            return res;
        }

        // Набор умений героя: живой герой (хост) или префаб расы (клиент/фоллбэк — набор детерминирован).
        IReadOnlyList<Ability> HeroAbilitySet(int team)
        {
            MatchManager mm = MatchManager.Instance;
            if (mm == null) return null;
            Unit hero = mm.HeroUnit(team);
            if (hero != null && hero.abilities != null) return hero.abilities;              // хост: реальный герой
            TeamWaveConfig cfg = mm.Team(team);
            return cfg != null && cfg.heroPrefab != null ? cfg.heroPrefab.abilities : null; // фоллбэк: префаб расы
        }

        // HeroAbilityUnlocked (замок умения героя по requiredLevel) снесён блоком Б8 (2026-09-06): открытие только
        // технологией. Зеркало уровня героя у клиента (MatchManager.HeroLevelClient) осталось — его ждёт панель героя Б9.

        // Каст умения героя из таблицы: серверо-авторитетно (хост — напрямую, клиент — через RPC по Ability.id).
        void CastHeroAbilityCell(int team, int abilityId)
        {
            if (NetworkConnectionHandler.isClient)
            {
                if (NetworkDataSync.Instance != null) NetworkDataSync.Instance.CastHeroAbilityServerRpc(team, abilityId);
            }
            else
            {
                if (MatchManager.Instance != null) MatchManager.Instance.CastHeroAbilityById(team, abilityId);
            }
        }
    }
}
