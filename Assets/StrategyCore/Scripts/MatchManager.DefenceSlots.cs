using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Партиал MatchManager: управление слотами строя ЗАЩИТЫ через сетки [[DefenceGrid]]. Ассет не трогается.
    ///
    /// Модель (по ТЗ):
    ///  - занятость на команду по ЛИНИЯМ-приоритетам (`formationPriority`); линия = ряд(ы) сетки,
    ///    приоритет 1 — передний ряд (к врагу). Заполнение префиксом (сначала первый ряд от центра к краям);
    ///  - у юнита слот хранит [[UnitDefenceSlot]] (priority, index) — поле в Unit добавить нельзя (правило 1);
    ///  - при смерти юнита его слот занимает крайний (последний индекс линии) — компакция;
    ///  - спавн: юнит появляется сразу в слоте сетки замка; защита: Move в слот активной сетки
    ///    (сетка передовой своей точки); смена точки → переход на тот же слот у другой сетки.
    /// Серверо-авторитетно (правило 6): слоты/компакция/Move — на сервере; сетки статичны и одинаковы на пирах.
    /// </summary>
    public partial class MatchManager
    {
        // Поведение юнитов в режиме Защиты (тумблер в Inspector на MatchManager).
        public enum DefenceBehaviour
        {
            [InspectorName("Idle — преследовать и возвращаться")] Idle,
            [InspectorName("Hold — держать слот, не преследовать")] Hold
        }

        [Header("Поведение защиты")]
        [Tooltip("Idle — преследуют врага и сами возвращаются в слот (как сейчас). Hold — встают в слот и держат позицию: бьют только в attackRange, не преследуют, со слота не уходят. Компакция при смерти работает в обоих режимах.")]
        [SerializeField] DefenceBehaviour defenceBehaviour = DefenceBehaviour.Idle;

        // Занятость слотов на команду: teamBands[team][priorityIndex] = занявшие юниты в порядке заполнения.
        List<List<Unit>>[] teamBands;

        void EnsureBandsInit()
        {
            if (teamBands != null) return;
            teamBands = new List<List<Unit>>[2] { new List<List<Unit>>(), new List<List<Unit>>() };
        }

        // Линия (бэнд) команды по приоритету (1..). Растёт по мере надобности.
        List<Unit> Band(int team, int priority)
        {
            EnsureBandsInit();
            int bi = Mathf.Max(0, priority - 1);
            List<List<Unit>> bands = teamBands[team];
            while (bands.Count <= bi) bands.Add(new List<Unit>());
            return bands[bi];
        }

        // Назначить слот юниту (если ещё не назначен и не в линии). Вешает UnitDefenceSlot. Возвращает индекс.
        int AssignSlot(int team, Unit u)
        {
            if (u == null) return -1;
            UnitDefenceSlot s = u.GetComponent<UnitDefenceSlot>();
            if (s != null)
            {
                List<Unit> existing = Band(team, s.priority);
                if (s.index >= 0 && s.index < existing.Count && existing[s.index] == u) return s.index; // уже в линии
            }

            int prio = u.formationPriority;
            List<Unit> band = Band(team, prio);
            int idx = band.Count;
            band.Add(u);

            if (s == null) s = u.gameObject.AddComponent<UnitDefenceSlot>();
            s.priority = prio;
            s.index = idx;
            return idx;
        }

        // Предсказать индекс следующего слота линии (для спавна ДО создания юнита).
        int NextSlotIndex(int team, int priority) => Band(team, priority).Count;

        // Поза спавна юнита: позиция и поворот его слота в сетке замка (spawnGrid). false → сетки нет
        // (фоллбэк — точка спавна cfg.spawnPoint). Индекс берётся как следующий свободный в линии.
        bool SpawnSlotPose(TeamWaveConfig cfg, int team, int priority, out Vector3 pos, out float yaw)
        {
            pos = (cfg != null && cfg.spawnPoint != null) ? cfg.spawnPoint.position : Vector3.zero;
            yaw = 0f;
            if (cfg == null || cfg.spawnGrid == null) return false;
            int idx = NextSlotIndex(team, priority);
            pos = cfg.spawnGrid.SlotWorld(priority, idx);
            yaw = cfg.spawnGrid.Facing();
            return true;
        }

        // Активная сетка защиты команды = сетка передовой своей точки (FrontmostOwnedPoint).
        DefenceGrid ActiveDefenceGrid(int player)
        {
            PointOfInterest poi = DefenceTargetPOI(player);
            return poi != null ? DefenceGrid.For(poi) : null;
        }

        // Подвинуть юнита в его слот активной сетки защиты. false — нет слота/сетки (вызывающий решает фоллбэк).
        bool MoveToDefenceSlot(int team, Unit u)
        {
            if (NetworkConnectionHandler.isClient || u == null || u.dead) return false;
            TeamWaveConfig cfg = Team(team);
            if (cfg == null) return false;

            DefenceGrid grid = ActiveDefenceGrid(cfg.ownerPlayer);
            if (grid == null) return false;

            AssignSlot(team, u); // гарантируем, что слот есть
            UnitDefenceSlot s = u.GetComponent<UnitDefenceSlot>();
            if (s == null) return false;

            Vector3 w = grid.SlotWorld(s.priority, s.index);
            Vector2 w2 = new Vector2(w.x, w.z);

            if (defenceBehaviour == DefenceBehaviour.Hold)
            {
                // Hold: дойти до слота и встать намертво. Move вернёт true, если юнит уже в слоте (спавн в слоте).
                if (u.Move(w2)) u.Hold();
                else HoldOnReach(team, u); // по достижении слота → Hold
            }
            else
            {
                u.Move(w2); // Idle: штатное поведение ассета (преследование + возврат в слот)
            }
            return true;
        }

        // Однострел: когда юнит дойдёт до слота — перевести его в Hold. Подписку на OnPositionReach снимает сам.
        // Холдим, только если к моменту прихода режим всё ещё Hold и команда в Защите (иначе приказ сменился).
        void HoldOnReach(int team, Unit u)
        {
            System.Action onReach = null;
            onReach = () =>
            {
                if (u != null) u.OnPositionReach -= onReach;
                if (u == null || u.dead) return;
                if (defenceBehaviour != DefenceBehaviour.Hold) return;
                if (CurrentCommandForUnit(team, u) != BottomTableAction.Defence) return; // режим РЯДА юнита сменился
                u.Hold();
            };
            u.OnPositionReach += onReach;
        }

        // Реагирует ли юнит на команду данного типа (поля Unit; дефолт true — прежняя бэк-совместимость).
        static bool RespondsToCommand(Unit u, bool isAttack)
        {
            if (u == null) return true;
            return isAttack ? u.respondsToAttackCommand : u.respondsToDefenceCommand;
        }

        // Отфильтровать список юнитов по реакции на атаку (isAttack=true) или защиту (false).
        static List<Unit> FilterByCommand(List<Unit> units, bool isAttack)
        {
            List<Unit> result = new List<Unit>(units.Count);
            foreach (Unit u in units)
                if (u != null && !u.dead && RespondsToCommand(u, isAttack))
                    result.Add(u);
            return result;
        }

        // Защита: подвинуть в их слоты живых юнитов команды из указанного РЯДА (groupIndex),
        // реагирующих на защиту. Фильтр по классам ряда — FilterByCommandGroup.
        void IssueDefenceAll(int team, int groupIndex)
        {
            if (NetworkConnectionHandler.isClient) return;
            List<Unit> units = FilterByCommandGroup(GetGroupUnits(team), groupIndex);
            for (int i = 0; i < units.Count; i++)
            {
                Unit u = units[i];
                if (u == null || u.dead) continue;
                if (!RespondsToCommand(u, isAttack: false)) continue; // не реагирует на защиту — пропустить
                if (!MoveToDefenceSlot(team, u))
                {
                    // Фоллбэк: нет сетки у точки — собрать у точки защиты (как раньше).
                    Vector2 def = DefenceTarget(Team(team).ownerPlayer);
                    if (def != Vector2.zero) u.Move(def);
                }
            }
        }

        // Смерть юнита: убрать из линии; в его слот поставить крайнего (последний индекс линии) — компакция.
        // Крайнего двигаем в освободившийся слот только если команда сейчас в защите.
        void OnDefenceUnitDied(int team, Unit u)
        {
            if (u == null) return;
            UnitDefenceSlot s = u.GetComponent<UnitDefenceSlot>();
            if (s == null) return;

            List<Unit> band = Band(team, s.priority);
            int idx = band.IndexOf(u);
            if (idx < 0) return;

            int last = band.Count - 1;
            if (idx != last)
            {
                Unit mover = band[last];
                band[idx] = mover;
                UnitDefenceSlot ms = mover != null ? mover.GetComponent<UnitDefenceSlot>() : null;
                if (ms != null) ms.index = idx;
                if (CurrentCommandForUnit(team, mover) == BottomTableAction.Defence) MoveToDefenceSlot(team, mover); // защищается ли РЯД перемещаемого
            }
            band.RemoveAt(last);
        }
    }
}
