using System.Collections.Generic;
using UnityEditor;

namespace StrategyCore
{
    // ==== INTERFLOW EDITOR — ВАЛИДАТОР: СЕМЬЯ «РАССЕЧЕНИЕ» (правило 22) ====
    // Правила Р52–Р55 проекта «Рассечение_Проект» §4.4 в редакции решений Artsiom 59–63 от 18.09.2026.
    // Отдельным партиалом, а не добавкой в InterflowValidator.cs: там больше 1800 строк, наращивать
    // его вширь правило 22 запрещает. Образец — InterflowValidator.HealthGate.cs.
    //
    // Нумерация правил сквозная по проекту. На момент записи занято по Р51 включительно
    // (InterflowValidator.Movement.cs, семья «движение, отброс, облик»), свободные — Р52 и дальше.
    //
    // Что здесь ловится: настройки, при которых блок ГАРАНТИРОВАННО не сработает (ошибка) или
    // сработает не так, как читается по заданию (предупреждение). Чисел баланса валидатор не решает
    // (правило 7): «радиус 2,5 велик или мал» — не его дело.
    public static partial class InterflowValidator
    {
        /// <summary>
        /// Правила семьи «рассечение». Два прохода: по ассетам пассивок (Р52–Р54 — это про сам ассет)
        /// и по ростеру фракций (Р55 — про то, есть ли у требуемого состояния источник).
        /// </summary>
        static void ValidatePassiveCleave(List<InterflowIssue> issues, List<FactionConfig> factions)
        {
            var passives = new List<CompositePassive>();

            foreach (string guid in AssetDatabase.FindAssets("t:CompositePassive"))
            {
                var p = AssetDatabase.LoadAssetAtPath<CompositePassive>(AssetDatabase.GUIDToAssetPath(guid));
                if (p == null || p.cleave == null || !p.cleave.enabled) continue;

                passives.Add(p);

                string owner = "Пассивка «" + p.name + "» (блок 11 «рассечение»)";

                ValidateCleaveNumbers(issues, p, owner);
                ValidateCleaveSelector(issues, p, owner);
                ValidateCleaveUpgrade(issues, p, owner);
            }

            ValidateCleaveRequiredEffector(issues, passives, factions);
        }

        // ================================================================== Р52 ==

        /// <summary>
        /// Р52: блок включён, а радиус или доля не больше нуля — рассечение не сделает ничего.
        ///
        /// Проверяются ОБЕ пары чисел: если улучшение технологией задано, после её открытия работают
        /// улучшенные числа, и нулевой улучшенный радиус молча выключил бы уже работавшее умение.
        /// </summary>
        static void ValidateCleaveNumbers(List<InterflowIssue> issues, CompositePassive p, string owner)
        {
            var b = p.cleave;

            if (!CompositePassive.CleaveNumbersUsable(b.radius, b.damageFraction))
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"{owner}: блок включён, но радиус = {b.radius:0.##} и доля урона = {b.damageFraction:0.##} — " +
                    "оба числа обязаны быть больше нуля: при нулевом радиусе задевать некого, при нулевой доле — нечем.",
                    "Рассечение_Проект §4.4 (Р52); CompositePassive.CleaveNumbersUsable", p));

            if (b.upgradeTechnology != null && !CompositePassive.CleaveNumbersUsable(b.upgradedRadius, b.upgradedDamageFraction))
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"{owner}: задана технология улучшения «{b.upgradeTechnology.name}», но улучшенный радиус = " +
                    $"{b.upgradedRadius:0.##} и улучшенная доля = {b.upgradedDamageFraction:0.##} — " +
                    "после открытия технологии рассечение ПЕРЕСТАНЕТ работать: улучшенные числа читаются вместо обычных.",
                    "Рассечение_Проект §4.4 (Р52); решение Artsiom 61", p));
        }

        // ================================================================== Р53 ==

        /// <summary>
        /// Р53: блок включён, а выборка пуста — ни одного флага принадлежности или типа.
        /// Штатная проверка совместимости при таком селекторе не пропускает никого
        /// (Core/Utils/UnitSlector.IsUnitCompatible), и круг всегда возвращается пустым.
        /// </summary>
        static void ValidateCleaveSelector(List<InterflowIssue> issues, CompositePassive p, string owner)
        {
            if (p.cleave.selector.AnySelectors()) return;

            issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                $"{owner}: блок включён, но выборка «кого задевает» пуста — не отмечено ни одного флага. " +
                "Круг всегда вернёт пусто, соседи не получат ничего.",
                "Рассечение_Проект §4.4 (Р53); Core/Utils/UnitSlector.AnySelectors", p));
        }

        // ================================================================== Р54 ==

        /// <summary>
        /// Р54: половина улучшения технологией. Две несимметричные по последствиям ветки:
        ///   • ссылка на технологию есть, а улучшенных чисел нет — ловится числами в Р52 (там же и текст);
        ///     здесь остаётся вторая половина: улучшенные числа ЗАДАНЫ, а ссылки на технологию НЕТ.
        ///     Числа тогда не читаются никогда, и настройка выглядит работающей, не будучи ею.
        ///   • ссылка есть, а числа стоят ровно как обычные — улучшение ничего не меняет.
        /// </summary>
        static void ValidateCleaveUpgrade(List<InterflowIssue> issues, CompositePassive p, string owner)
        {
            var b = p.cleave;

            if (b.upgradeTechnology == null && (b.upgradedRadius > 0f || b.upgradedDamageFraction > 0f))
            {
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    $"{owner}: заданы улучшенные числа (радиус {b.upgradedRadius:0.##}, доля {b.upgradedDamageFraction:0.##}), " +
                    "но технология улучшения не указана — читаться они не будут никогда.",
                    "Рассечение_Проект §4.4 (Р54); решение Artsiom 61", p));
                return;
            }

            if (b.upgradeTechnology == null) return;

            // Неработающие улучшенные числа уже названы правилом Р52 — второй строкой про то же не сорим.
            if (!CompositePassive.CleaveNumbersUsable(b.upgradedRadius, b.upgradedDamageFraction)) return;

            if (UnityEngine.Mathf.Approximately(b.upgradedRadius, b.radius)
                && UnityEngine.Mathf.Approximately(b.upgradedDamageFraction, b.damageFraction))
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    $"{owner}: технология улучшения «{b.upgradeTechnology.name}» задана, но улучшенные числа " +
                    "совпадают с обычными — открытие технологии ничего не изменит.",
                    "Рассечение_Проект §4.4 (Р54); решение Artsiom 61", p));
        }

        // ================================================================== Р55 ==

        /// <summary>
        /// Р55: у блока задано требуемое состояние НОСИТЕЛЯ, а в контенте фракций нет ни одного
        /// умения, которое это состояние накладывает, — условие не выполнится никогда и рассечение
        /// не сработает ни разу.
        ///
        /// Как ищется источник: штатным графом ссылок <c>AssetDatabase.GetDependencies</c> —
        /// тем же приёмом, которым в редакторе уже считаются обратные ссылки
        /// (Editor/Interflow/InterflowUsageScanner.cs, правило 1). Перебирать руками два десятка полей,
        /// через которые умение может наложить состояние, значило бы завести список, который молча
        /// разойдётся с кодом при первом новом блоке.
        ///
        /// ЧТО ПРАВИЛО НЕ ЛОВИТ (сказано прямо, а не умолчано):
        ///   • ссылка на ассет состояния ≠ его наложение: умение может ссылаться на состояние как
        ///     на УСЛОВИЕ (так делает и сам проверяемый блок). Поэтому правило — предупреждение,
        ///     а не ошибка: оно ловит «источника нет вовсе», а не «источник не тот»;
        ///   • сами пассивки с этим же блоком из поиска исключены — иначе три ассета семьи
        ///     подтверждали бы источник друг другу;
        ///   • ростер считается общим по всем фракциям: умение чужой фракции тоже сойдёт за источник.
        /// </summary>
        static void ValidateCleaveRequiredEffector(List<InterflowIssue> issues, List<CompositePassive> passives,
                                                   List<FactionConfig> factions)
        {
            if (passives.Count == 0) return;

            var needed = new List<CompositePassive>();
            foreach (var p in passives)
                if (p.cleave.requiredCasterEffector != null) needed.Add(p);

            if (needed.Count == 0) return;

            // Ростер: умения панели и героев, умения узлов дерева и умения юнитов фракций.
            var roster = CollectPanelAbilities(factions);
            foreach (var unit in CollectFactionUnits(factions))
            {
                if (unit == null || unit.abilities == null) continue;
                foreach (var a in unit.abilities) if (a != null) roster.Add(a);
            }

            // Кандидаты в источники: всё из ростера, кроме проверяемых пассивок.
            var sources = new List<string>();
            foreach (var a in roster)
            {
                var cp = a as CompositePassive;
                if (cp != null && cp.cleave != null && cp.cleave.enabled) continue;

                string path = AssetDatabase.GetAssetPath(a);
                if (!string.IsNullOrEmpty(path)) sources.Add(path);
            }

            HashSet<string> referenced = RosterDependencies(sources);

            foreach (var p in needed)
            {
                string effectorPath = AssetDatabase.GetAssetPath(p.cleave.requiredCasterEffector);
                if (string.IsNullOrEmpty(effectorPath)) continue;   // состояние не ассет — проверять нечем

                if (referenced.Contains(effectorPath)) continue;

                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    $"Пассивка «{p.name}» (блок 11 «рассечение»): требуемое состояние носителя " +
                    $"«{p.cleave.requiredCasterEffector.name}» не встречается ни в одном умении ростера фракций — " +
                    "накладывать его нечем, и рассечение не сработает ни разу.",
                    "Рассечение_Проект §4.4 (Р55); решение Artsiom 60", p));
            }
        }

        /// <summary>
        /// Всё, на что ссылается ростер, — ОДНИМ обходом графа сериализации. Перегрузка со списком
        /// путей существует именно для этого: вызов на каждый ассет по отдельности заново поднимал бы
        /// общие зависимости десятки раз.
        /// </summary>
        static HashSet<string> RosterDependencies(List<string> sourcePaths)
        {
            var set = new HashSet<string>();
            if (sourcePaths.Count == 0) return set;

            foreach (string dep in AssetDatabase.GetDependencies(sourcePaths.ToArray(), true)) set.Add(dep);

            return set;
        }
    }
}
