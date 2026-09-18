using System.Collections.Generic;
using UnityEditor;

namespace StrategyCore
{
    // ==== INTERFLOW EDITOR — ВАЛИДАТОР: СЕМЬЯ «ЗДОРОВЬЕ НОСИТЕЛЯ И ИММУНИТЕТЫ» (правило 22) ====
    // Правила Р37–Р42 проекта «Здоровье_и_Иммунитеты_Проект» §4.5 (решения Artsiom 41–46 от 17.09.2026).
    // Отдельным партиалом, а не добавкой в InterflowValidator.cs: там больше 1800 строк, наращивать
    // его вширь правило 22 запрещает. Образец — InterflowValidator.Reactions.cs.
    //
    // Нумерация правил сквозная по проекту. На момент записи занято по Р36 включительно
    // (InterflowValidator.Reactions.cs, семья «смерть и убийство»), свободные — Р37 и дальше.
    //
    // Что здесь ловится: настройки, при которых блок ГАРАНТИРОВАННО не сработает (ошибка) или
    // сработает не так, как читается по заданию (предупреждение). Числовых порогов баланса нет
    // (правило 7): «порог здоровья 0,3 велик или мал» валидатор не решает.
    public static partial class InterflowValidator
    {
        /// <summary>
        /// Правила семьи «здоровье носителя и иммунитеты». Два прохода: по ассетам пассивок
        /// (Р37–Р40, Р42 — это про сам ассет) и по юнитам (Р41 — про НАБОР ассетов на носителе).
        /// </summary>
        static void ValidatePassiveHealth(List<InterflowIssue> issues, List<(Unit unit, string path)> units)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:CompositePassive"))
            {
                var p = AssetDatabase.LoadAssetAtPath<CompositePassive>(AssetDatabase.GUIDToAssetPath(guid));
                if (p == null) continue;

                string owner = "Пассивка «" + p.name + "»";

                ValidateHealthCondition(issues, p, owner);
                ValidateLastingVisuals(issues, p, owner);
                ValidateMissingHpStats(issues, p, owner);
                ValidateResistanceRows(issues, p, owner);
            }

            ValidateLifestealPairs(issues, units);
        }

        // ============================================================ Р37 ==

        /// <summary>
        /// Р37: порог здоровья вне диапазона «больше нуля и не больше единицы» при включённом условии.
        ///
        /// Ноль опасен не меньше значения вне шкалы: готовая функция порога читает долю ≤ 0 как
        /// «условия нет» (CompositeSkill.BlockTypes.HpAllowed), и выбранное в ассете условие
        /// «пока здоровья меньше доли» молча стало бы «всегда» — блок никогда не выключится.
        /// </summary>
        static void ValidateHealthCondition(List<InterflowIssue> issues, CompositePassive p, string owner)
        {
            if (p.controlImmunity != null && p.controlImmunity.enabled)
                CheckThreshold(issues, p, owner, "блок 2 «иммунитет к контролю»",
                               p.controlImmunity.carrierCondition, p.controlImmunity.carrierHpBelow);

            if (p.resistances != null && p.resistances.enabled)
                CheckThreshold(issues, p, owner, "блок 3 «сопротивления и слабости»",
                               p.resistances.carrierCondition, p.resistances.carrierHpBelow);

            // У реакции 5 условие носит ОДИН кирпич — вампиризм; проверяем, только когда он задан.
            if (p.onHit != null && p.onHit.enabled
                && (p.onHit.healFromDamagePercent > 0f || p.onHit.healFromDamagePercentUpgraded > 0f))
                CheckThreshold(issues, p, owner, "вампиризм реакции 5",
                               p.onHit.healCondition, p.onHit.healCarrierHpBelow);
        }

        static void CheckThreshold(List<InterflowIssue> issues, CompositePassive p, string owner, string place,
                                   PassiveBlockCondition condition, float hpBelow)
        {
            if (condition != PassiveBlockCondition.WhileHpBelow) return;
            if (hpBelow > 0f && hpBelow <= 1f) return;

            issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                $"{owner} ({place}): выбрано условие «пока здоровья меньше доли», но порог = {hpBelow:0.##} — " +
                "он обязан быть больше нуля и не больше единицы. Нулевой порог читается как «условия нет», " +
                "и блок будет работать всегда.",
                "Здоровье_и_Иммунитеты_Проект §4.5 (Р37); CompositeSkill.BlockTypes.HpAllowed", p));
        }

        // ======================================================== Р38 и Р39 ==

        /// <summary>
        /// Р38: у блока 10 задан порог показа, а самого визуала нет — показывать нечего.
        /// Р39: длящийся визуал обязан быть бессрочным состоянием без значка и без геймплея
        /// (клон Р29 визуала щита для трёх новых хозяев: блоков 2, 3 и 10).
        /// </summary>
        static void ValidateLastingVisuals(List<InterflowIssue> issues, CompositePassive p, string owner)
        {
            if (p.missingHpStats != null && p.missingHpStats.enabled
                && p.missingHpStats.presentationMissingHpThreshold > 0f && p.missingHpStats.visualEffector == null)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    $"{owner} (блок 10): задан порог показа длящегося визуала " +
                    $"({p.missingHpStats.presentationMissingHpThreshold:0.##}), но сам визуал не задан — показывать нечего.",
                    "Здоровье_и_Иммунитеты_Проект §4.5 (Р38)", p));

            if (p.controlImmunity != null && p.controlImmunity.enabled)
                CheckLastingVisual(issues, p, owner, "блок 2, длящийся визуал", p.controlImmunity.visualEffector);

            if (p.resistances != null && p.resistances.enabled)
                CheckLastingVisual(issues, p, owner, "блок 3, длящийся визуал", p.resistances.visualEffector);

            if (p.missingHpStats != null && p.missingHpStats.enabled)
                CheckLastingVisual(issues, p, owner, "блок 10, длящийся визуал", p.missingHpStats.visualEffector);
        }

        static void CheckLastingVisual(List<InterflowIssue> issues, CompositePassive p, string owner,
                                       string place, Effector e)
        {
            if (e == null) return;

            var wrong = new List<string>();

            if (!e.permanent) wrong.Add("оно не бессрочное (permanent выключен) — погаснет раньше условия");
            if (e.icon != null) wrong.Add("у него есть значок — это состояние, а не визуал");
            if (e.passiveEffectsOn) wrong.Add("у него включены пассивные изменения статов");
            if (e.stuns || e.mutes || e.disarms || e.blinds) wrong.Add("оно накладывает контроль");
            if (e.damageAmount > 0) wrong.Add("оно наносит периодический урон");

            if (wrong.Count == 0) return;

            issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                $"{owner} ({place}): состояние «{e.name}» не годится в визуал — " + string.Join("; ", wrong) + ".",
                "Здоровье_и_Иммунитеты_Проект §4.5 (Р39); образец — Р29 визуала щита", p));
        }

        // ============================================================ Р40 ==

        /// <summary>
        /// Р40: шаг квантования вне диапазона от нуля до единицы → ошибка;
        /// шаг задан, а все три кривые постоянны → предупреждение (ступеням нечего ступать).
        ///
        /// Ноль — рабочее значение («шага нет, кривые читаются плавно»), поэтому он не ошибка.
        /// Полем в редакторе значение вне диапазона не задать ([Range]) — правило ловит правку
        /// ассета текстом и перенос с чужого проекта.
        /// </summary>
        static void ValidateMissingHpStats(List<InterflowIssue> issues, CompositePassive p, string owner)
        {
            var b = p.missingHpStats;
            if (b == null || !b.enabled) return;

            if (b.missingHpStep < 0f || b.missingHpStep > 1f)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"{owner} (блок 10): шаг ступени = {b.missingHpStep:0.##} — он обязан лежать " +
                    "от нуля до единицы. 0 — шага нет, кривые читаются плавно.",
                    "Здоровье_и_Иммунитеты_Проект §4.5 (Р40)", p));

            bool anyCurve = !ConstantCurve(b.damageByMissingHp, 1f)
                            || !ConstantCurve(b.armorByMissingHp, 0f)
                            || !ConstantCurve(b.attackSpeedByMissingHp, 0f);

            if (b.missingHpStep > 0f && !anyCurve)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    $"{owner} (блок 10): шаг ступени задан, но все три кривые постоянны — " +
                    "блок включён и ничего не меняет.",
                    "Здоровье_и_Иммунитеты_Проект §4.5 (Р40)", p));
        }

        /// <summary>
        /// Постоянна ли кривая и равна ли она значению «ничего не менять». Пустая кривая (нет ключей)
        /// считается постоянной: она читается как ноль, а рабочей настройкой быть не может.
        /// </summary>
        static bool ConstantCurve(UnityEngine.AnimationCurve curve, float neutral)
        {
            if (curve == null || curve.keys == null || curve.keys.Length == 0) return true;

            for (int i = 0; i < curve.keys.Length; i++)
                if (!UnityEngine.Mathf.Approximately(curve.keys[i].value, neutral)) return false;

            return true;
        }

        // ============================================================ Р41 ==

        /// <summary>
        /// Р41: два ассета с вампиризмом на ОДНОМ носителе. У пассивок замки считаются по каждому
        /// ассету независимо (Units/Unit.AbilityLocks.cs), поэтому приём «второй ассет с requiredTech»
        /// здесь не работает: после открытия технологии открыты ОБА, и доли возврата складываются.
        /// Правильный способ улучшения — поля внутри блока (решение Artsiom 45).
        /// </summary>
        static void ValidateLifestealPairs(List<InterflowIssue> issues, List<(Unit unit, string path)> units)
        {
            foreach (var (unit, _) in units)
            {
                if (unit.abilities == null) continue;

                var carriers = new List<string>();

                foreach (var ability in unit.abilities)
                {
                    var cp = ability as CompositePassive;
                    if (cp != null && cp.onHit != null && cp.onHit.enabled && cp.onHit.healFromDamagePercent > 0f)
                    {
                        carriers.Add("«" + cp.name + "»");
                        continue;
                    }

                    // Старый класс вампиризма живёт рядом с конструктором (решение Artsiom 09.08.2026):
                    // его ассет на том же носителе даёт ровно ту же сумму долей.
                    var old = ability as LifestealPassive;
                    if (old != null) carriers.Add("«" + old.name + "» (старый класс)");
                }

                if (carriers.Count < 2) continue;

                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    $"У юнита «{unit.name}» вампиризм задан в {carriers.Count} умениях ({string.Join(", ", carriers)}) — " +
                    "замки пассивок считаются по каждому ассету отдельно, поэтому доли возврата СЛОЖАТСЯ. " +
                    "Улучшение технологией задаётся полями внутри блока, вторым ассетом — нельзя.",
                    "Здоровье_и_Иммунитеты_Проект §4.5 (Р41); Units/Unit.AbilityLocks.cs", unit));
            }
        }

        // ============================================================ Р42 ==

        /// <summary>
        /// Р42: блок 3 включён, а действующих строк нет — список пуст или во всех строках категория «Нет».
        /// До сих пор это писалось только в лог полного уровня и в редакторе не было видно вовсе.
        /// </summary>
        static void ValidateResistanceRows(List<InterflowIssue> issues, CompositePassive p, string owner)
        {
            var b = p.resistances;
            if (b == null || !b.enabled) return;

            bool anyRow = false;

            if (b.entries != null)
                for (int i = 0; i < b.entries.Length; i++)
                    if (b.entries[i] != null && b.entries[i].category != EffectorCategory.None) { anyRow = true; break; }

            if (anyRow) return;

            issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                $"{owner} (блок 3): блок включён, но действующих строк нет — список пуст либо во всех строках " +
                "категория «Нет». Носитель не получит ни сопротивлений, ни слабостей.",
                "Здоровье_и_Иммунитеты_Проект §4.5 (Р42); CompositePassive.Properties.ApplyResistances", p));
        }
    }
}
