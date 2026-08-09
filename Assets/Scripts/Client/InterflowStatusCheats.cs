using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// [Interflow 2026-08-07] Читы проверки шкалы статусов — работают ПРЯМО В ИГРЕ, включая билд:
    /// открыть чат, набрать команду. Диспетчер — штатный <see cref="Cheats.MsgAdded"/> (нужен debugMode у слота).
    ///
    /// Команды:
    ///   status   — повесить на выделенного полный набор: стан, немота, безоружие, слепота, горение, обморожение
    ///   skill    — то же, но штатным путём умения (_TEST_Skill_StatusShowcase): проверяет и рассылку клиенту
    ///   nostatus — снять всё разом (проверка, что значки исчезают)
    ///   dummy    — сделать выделенного манекеном: неуязвим и стоит на месте
    ///   bld      — проверить, что эффекторы не вешаются на здания
    ///   rows     — вывести в консоль состав рядов у всех юнитов (для проверки тумана войны)
    ///
    /// Меняющие состояние команды исполняются только на сервере/хосте (правило серверной авторитетности);
    /// «rows» — чистое чтение, поэтому доступна и на клиенте: там она и нужна для проверки приёма статусов.
    /// Ассеты берутся через Resources и реестр GameManager — без Editor-API, поэтому живут в билде.
    /// </summary>
    public static class InterflowStatusCheats
    {
        const string Tag = "[Тест] ";
        const float StatusSeconds = 20f;

        const int BurnEffectorId = 200500;   // Lyudi_Eff_Burn — значок горения
        const int SlowEffectorId = 200502;   // Lyudi_Eff_IceSlow — значок обморожения
        const string ShowcaseSkillResource = "Ability/_Test/_TEST_Skill_StatusShowcase";

        /// <summary>Разбор команды. Возвращает true, если команда наша (чтобы Cheats не искал дальше).</summary>
        public static bool Handle(string msg, bool isClient)
        {
            switch (msg)
            {
                case "rows": Rows(); return true;                      // только чтение — можно и клиенту
                case "status": if (!isClient) AllStatuses(); return true;
                case "skill": if (!isClient) CastShowcase(); return true;
                case "nostatus": if (!isClient) ClearAll(); return true;
                case "dummy": if (!isClient) Dummy(); return true;
                case "bld": if (!isClient) BuildingGate(); return true;
                default: return false;
            }
        }

        // ====================================================================== КОМАНДЫ ==

        static void AllStatuses()
        {
            Unit unit = Target();
            if (unit == null) return;

            unit.Stun(StatusSeconds);
            unit.Mute(StatusSeconds);
            unit.Disarm(StatusSeconds);
            BlindDebuff.Apply(unit, 1f, StatusSeconds);
            AddEffector(unit, BurnEffectorId, 0.02f);   // слабое горение: значок есть, юнит не сгорает
            AddEffector(unit, SlowEffectorId, 1f);

            Debug.Log(Tag + "На " + unit.name + " наложено на " + StatusSeconds + " с: стан, немота, безоружие, слепота, горение, обморожение.");
            Report(unit);
        }

        static void CastShowcase()
        {
            Unit unit = Target();
            if (unit == null) return;

            var skill = Resources.Load<CompositeSkill>(ShowcaseSkillResource);
            if (skill == null) { Debug.LogWarning(Tag + "Не найдено тест-умение в Resources: " + ShowcaseSkillResource); return; }

            skill.Use(unit, unit.owner, 0);
            Debug.Log(Tag + unit.name + " кастует тест-умение: тот же набор значков, но штатным путём (умение → статусы → рассылка клиенту).");
            Report(unit);
        }

        static void ClearAll()
        {
            Unit unit = Target();
            if (unit == null) return;

            unit.Stun(false);
            unit.Mute(false);
            unit.Disarm(false);
            foreach (var blind in unit.GetComponents<BlindDebuff>()) Object.Destroy(blind);

            int removed = 0;
            for (int i = unit.effectors.Count - 1; i >= 0; i--) { Effector.EffectorRemove(unit, unit.effectors[i]); removed++; }

            Debug.Log(Tag + "С " + unit.name + " снято всё (эффекторов снято " + removed + "). Значки должны исчезнуть за один тик.");
        }

        static void Dummy()
        {
            Unit unit = Target();
            if (unit == null) return;

            unit.isInvulnerable = true;
            unit.Hold();
            Debug.Log(Tag + unit.name + " — манекен: неуязвим и стоит. Удобно разглядывать ряд, пока идёт бой.");
        }

        static void BuildingGate()
        {
            Unit building = Selected();
            if (building == null || building.unitType != UnitType.Building)
                building = Alive().FirstOrDefault(u => u.unitType == UnitType.Building && u.team == MyTeam())
                        ?? Alive().FirstOrDefault(u => u.unitType == UnitType.Building);
            if (building == null) { Debug.LogWarning(Tag + "Здание не найдено — выдели здание и повтори."); return; }

            int before = building.effectors.Count;
            string error = "нет";
            try
            {
                AddEffector(building, BurnEffectorId, 0.02f);
                AddEffector(building, SlowEffectorId, 1f);
            }
            catch (System.Exception e) { error = e.GetType().Name; }

            bool ok = building.effectors.Count == before && error == "нет";
            Debug.Log(Tag + "Здание " + building.name + " — " + (ok
                ? "ОК: эффекторы не легли, исключений нет."
                : "ПРОВАЛ: было " + before + ", стало " + building.effectors.Count + ", исключение: " + error));
        }

        static void Rows()
        {
            var sb = new System.Text.StringBuilder(Tag + "Ряды значков (значков / ряд / полоска здоровья):\n");
            int shown = 0;
            foreach (Unit u in Alive().OrderBy(u => u.team).ThenBy(u => u.name))
            {
                Transform row = u.transform.Find("StatusIconsBar");
                if (row == null) continue;

                int icons = 0;
                if (row.gameObject.activeSelf) foreach (Transform quad in row) if (quad.gameObject.activeSelf) icons++;
                if (icons == 0 && !row.gameObject.activeSelf) continue;

                shown++;
                Transform healthBar = u.transform.Find("HealthBar(Clone)");
                sb.Append("  " + u.name + " (команда " + u.team + "): значков " + icons +
                          ", ряд " + (row.gameObject.activeSelf ? "виден" : "СКРЫТ") +
                          ", полоска " + (healthBar == null ? "нет" : healthBar.gameObject.activeSelf ? "видна" : "СКРЫТА") + "\n");
            }
            if (shown == 0) sb.Append("  (активных рядов нет — набери «status» на выделенном юните)");
            else sb.Append("Ряд и полоска должны быть в ОДНОМ состоянии: оба видны либо оба скрыты (туман войны).");
            Debug.Log(sb.ToString());
        }

        // ==================================================================== СЛУЖЕБНОЕ ==

        static int MyTeam() => SlotManager.instance != null ? SlotManager.instance.currentTeam : 0;

        static IEnumerable<Unit> Alive() =>
            Object.FindObjectsByType<Unit>(FindObjectsSortMode.None).Where(u => u != null && !u.dead);

        static Unit Selected() =>
            PlayerControl.instance != null ? PlayerControl.instance.activeUnit : null;

        /// <summary>Выделенный юнит; если не выделен — свой живой с наибольшим здоровьем.</summary>
        static Unit Target()
        {
            Unit unit = Selected();
            if (unit != null && !unit.dead) return unit;

            unit = Alive().Where(u => u.unitType == UnitType.Unit && u.team == MyTeam())
                          .OrderByDescending(u => u.health).FirstOrDefault();
            if (unit == null) { Debug.LogWarning(Tag + "Некого взять: выдели юнита кликом."); return null; }

            Debug.Log(Tag + "Юнит не выделен — взят свой живой: " + unit.name + ".");
            return unit;
        }

        static void AddEffector(Unit unit, int effectorId, float power)
        {
            Effector effector = null;
            if (GameManager.instance != null && GameManager.instance.gameEffectors != null)
                GameManager.instance.gameEffectors.TryGetValue(effectorId, out effector);

            if (effector == null) { Debug.LogWarning(Tag + "Эффектор с id " + effectorId + " не найден в реестре."); return; }
            Effector.EffectorAdd(unit, effector, null, unit.owner, 0f, power, StatusSeconds);
        }

        /// <summary>
        /// Сверка «сколько значков ждём» и «сколько видно». Замер отложен на полсекунды:
        /// шкала пересобирается на ближайшем тике, мгновенный замер показал бы недобор и напугал бы зря.
        /// </summary>
        static void Report(Unit unit)
        {
            if (GameManager.instance != null) GameManager.instance.StartCoroutine(ReportDelayed(unit));
        }

        static System.Collections.IEnumerator ReportDelayed(Unit unit)
        {
            yield return new WaitForSeconds(0.5f);
            if (unit == null || unit.dead) { Debug.Log(Tag + "Юнит погиб до замера — повтори на живом (или сначала «dummy»)."); yield break; }

            var buffer = new List<UnitStatusIcons.Entry>();
            int expected = UnitStatusIcons.Collect(unit, buffer);
            string names = expected == 0 ? "пусто" : string.Join(", ", buffer.Select(e => e.icon != null ? e.icon.name : "без текстуры"));

            Transform row = unit.transform.Find("StatusIconsBar");
            bool rowShown = row != null && row.gameObject.activeSelf;
            int quads = 0;
            if (rowShown) foreach (Transform quad in row) if (quad.gameObject.activeSelf) quads++;

            string verdict;
            if (!rowShown && expected == 0) verdict = " — ОК: статусов нет, ряд скрыт";
            else if (!rowShown) verdict = " — РЯД СКРЫТ при есть-статусах: ядро прячет юнита от тебя (туман/невидимость), полоска здоровья тоже должна быть скрыта";
            else if (quads == expected) verdict = " — СОВПАЛО";
            else verdict = " — НЕ СОВПАЛО: ряд виден, а квадратов не столько, сколько значков — это баг шкалы";

            Debug.Log(Tag + "Замер: ожидается значков " + expected + " [" + names + "], видно квадратов " + quads + verdict);
        }
    }
}
