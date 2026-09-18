using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace StrategyCore
{
    // ==== INTERFLOW EDITOR — ВАЛИДАТОР: ОСЬ «РЕАКЦИИ» КОНСТРУКТОРА ПАССИВОК (правило 22) ====
    // Правила Р32–Р36 проекта «Смерть_и_Убийство_Проект» §4.4 (решения Artsiom 36–40 от 17.09.2026).
    // Отдельным партиалом, а не добавкой в InterflowValidator.cs: там уже больше 1800 строк, и наращивать
    // его вширь правило 22 запрещает. Образец — InterflowValidator.Presentation.cs.
    //
    // Что здесь ловится: настройки, при которых блок ГАРАНТИРОВАННО не сработает (ошибка) или сработает
    // не так, как читается по заданию (предупреждение). Числовых порогов баланса нет (правило 7).
    //
    // Нумерация правил сквозная по проекту. На момент записи занято по Р31 включительно
    // (InterflowValidator.Presentation.cs, семья «презентация события»), свободные — Р32 и дальше.
    public static partial class InterflowValidator
    {
        /// <summary>Задано ли в списке по уровням хоть одно положительное число.</summary>
        static bool AnyPositive(float[] byLevel) => byLevel != null && byLevel.Any(v => v > 0f);

        /// <summary>
        /// Правила реакций 2, 3 и 7. Отдельным проходом по ассетам пассивок, а не по носителям:
        /// все пять правил — про САМ ассет, юнит-носитель для них ничего не решает.
        /// </summary>
        static void ValidatePassiveReactions(List<InterflowIssue> issues)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:CompositePassive"))
            {
                var p = AssetDatabase.LoadAssetAtPath<CompositePassive>(AssetDatabase.GUIDToAssetPath(guid));
                if (p == null) continue;

                string owner = "Пассивка «" + p.name + "»";

                ValidateDeathReaction(issues, p, owner);
                ValidateKillBonus(issues, p, owner);
                ValidateAllyDeathReaction(issues, p, owner);
            }
        }

        /// <summary>Р32–Р34: реакция 2 «носитель погиб».</summary>
        static void ValidateDeathReaction(List<InterflowIssue> issues, CompositePassive p, string owner)
        {
            PassiveOnDeathBlock b = p.onDeath;
            if (b == null || !b.enabled) return;

            bool damages = AnyPositive(b.enemyDamage);
            bool heals = AnyPositive(b.allyHealFlat) || AnyPositive(b.allyHealPercentOfMaxHp);

            // Р32. У моего блока селекторы приходят БЕЗ инициализатора — все флаги false. Штатная проверка
            // UnitSelector.IsUnitCompatible при таком селекторе не пропускает никого (Core/Utils/UnitSlector.cs:43-56),
            // то есть блок отработает вхолостую и молча. У ассетов Саши это умолчание было обратным.
            if (damages && !b.enemySelector.AnySelectors())
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"{owner}: реакция «носитель погиб» бьёт врагов, но селектор врага ПУСТ — целей всегда будет ноль.",
                    "Смерть_и_Убийство_Проект §4.4 (Р32); UnitSelector.IsUnitCompatible", p));

            if (heals && !b.allySelector.AnySelectors())
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"{owner}: реакция «носитель погиб» лечит союзников, но селектор союзника ПУСТ — целей всегда будет ноль.",
                    "Смерть_и_Убийство_Проект §4.4 (Р32); UnitSelector.IsUnitCompatible", p));

            // Р33. Умолчание у моего блока обратное привычному по ассетам Саши: там лечился ближайший,
            // здесь при снятой галке лечатся ВСЕ в радиусе. Это рабочая настройка, поэтому предупреждение.
            if (heals && !b.healOnlyNearest)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    $"{owner}: реакция «носитель погиб» лечит ВСЕХ союзников в радиусе — галка «лечить только ближайшего» снята.",
                    "Смерть_и_Убийство_Проект §4.4 (Р33)", p));

            // Р34. Обе ветки исполнения гейтятся радиусом: при нулевом радиусе блок включён, а не делает ничего.
            if (damages && !AnyPositive(b.radius))
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"{owner}: реакция «носитель погиб» бьёт врагов, но радиус не задан — урон не нанесётся никому.",
                    "Смерть_и_Убийство_Проект §4.4 (Р34); CompositePassive.ReactionsRuntime.DeathBurst", p));

            // У лечения с 17.09.2026 свой радиус (решение Artsiom 36): пустой берёт радиус урона,
            // поэтому ругаемся только когда пусты ОБА — иначе правило кричало бы на рабочую настройку.
            if (heals && !AnyPositive(b.radius) && !AnyPositive(b.allyHealRadius))
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"{owner}: реакция «носитель погиб» лечит союзников, но не задан ни радиус лечения, ни радиус урона — " +
                    "лечить будет некого.",
                    "Смерть_и_Убийство_Проект §4.4 (Р34); CompositePassive.ReactionsRuntime.DeathBurst", p));
        }

        /// <summary>Р35: прибавка к урону за убийство у реакции 3.</summary>
        static void ValidateKillBonus(List<InterflowIssue> issues, CompositePassive p, string owner)
        {
            PassiveOnKillBlock b = p.onKill;
            if (b == null || !AnyPositive(b.damagePercentPerKill)) return;

            // Счётчик убийств растёт в обработчике реакции 3: выключенный блок не подписан на хаб смертей,
            // и прибавка не сдвинется с нуля никогда.
            if (!b.enabled)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"{owner}: задана прибавка к урону за убийство, но реакция «носитель кого-то убил» ВЫКЛЮЧЕНА — " +
                    "убийства не считаются, прибавка навсегда останется нулевой.",
                    "Смерть_и_Убийство_Проект §4.4 (Р35)", p));

            if (b.damagePerKillMaxStacks <= 0)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    $"{owner}: прибавка к урону за убийство задана БЕЗ ПРЕДЕЛА — она растёт, пока носитель жив.",
                    "Смерть_и_Убийство_Проект §4.4 (Р35)", p));
        }

        /// <summary>Р36: реакция 7 «погиб союзник».</summary>
        static void ValidateAllyDeathReaction(List<InterflowIssue> issues, CompositePassive p, string owner)
        {
            PassiveOnAllyDeathBlock b = p.onAllyDeath;
            if (b == null || !b.enabled) return;

            if (!AnyPositive(b.healFlat) && !AnyPositive(b.healPercentOfMaxHp))
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    $"{owner}: реакция «погиб союзник» включена, но лечение носителю нулевое — блок ничего не делает.",
                    "Смерть_и_Убийство_Проект §4.4 (Р36)", p));

            // Пустая строка в списке заготовок ничего не отбирает и легко принимается за настройку
            // (образец — то же правило у селектора заготовок умения, InterflowValidator.cs Р7).
            if (b.victimPrefabs != null && b.victimPrefabs.Any(u => u == null))
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"{owner}: в списке заготовок жертв реакции «погиб союзник» есть ПУСТАЯ запись — она ничего не отбирает.",
                    "Смерть_и_Убийство_Проект §4.4 (Р36)", p));
        }
    }
}
