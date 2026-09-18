using System.Collections.Generic;
using UnityEditor;

namespace StrategyCore
{
    // ==== INTERFLOW EDITOR — ВАЛИДАТОР: СЕМЬЯ «ДВИЖЕНИЕ КАСТЕРА, ОТБРОС ЦЕЛЕЙ, ОБЛИК» (правило 22) ====
    // Правила Р43–Р50 проекта «Движение_Отброс_Облик_Проект» §4.4 в редакции решений Artsiom 47–58
    // от 17–18.09.2026. Отдельным партиалом — InterflowValidator.cs больше 1800 строк (правило 22);
    // образец — InterflowValidator.HealthGate.cs.
    //
    // Нумерация сквозная по проекту. На момент записи занято по Р42 включительно
    // (InterflowValidator.HealthGate.cs, семья «здоровье носителя и иммунитеты»), свободные — Р43 и дальше.
    //
    // Правила §4.4 проекта, которые здесь НЕ реализованы, и почему:
    //   • «trailSkill ссылается на умение с блоком 22 или на само себя» — полей trailSkill нет
    //     (решение 56: шлейф стал режимом блока 17, вложенное умение не участвует);
    //   • «knockback.filter заполнен при выключённом блоке 15» и «filter.maxTier вне диапазона» —
    //     фильтра у блока 15 нет (решение 51);
    //   • «alongCasterFacing у умения без кастера» — поля нет (решение 52).
    //
    // СЛОВА В ТЕКСТАХ. Вкладка «Боевые умения» кладёт сообщение в карточку блока по словам
    // (ISSUE_ROUTES в InterflowSkillBuilderTab.cs). Поэтому: про блок 18 пишем «перемещение»
    // или «прыжок» и НИКОГДА «рывок» (этот ключ занят блоком 2 — рывком ЦЕЛИ к кастеру);
    // про блок 17 в режиме шлейфа — «шлейф»; про блок 12 — «облик» или «превращение»;
    // про блок 15 — «отброс».
    public static partial class InterflowValidator
    {
        /// <summary>
        /// Правила семьи для ОДНОГО умения. Зовётся из ValidateSkill — значит работает и в полном
        /// прогоне, и при правке умения в конструкторе.
        /// </summary>
        static void ValidateMovementFamily(List<InterflowIssue> issues, CompositeSkill skill, string n)
        {
            string owner = "Умение «" + n + "»";

            ValidateCasterMoveBlock(issues, skill, owner);
            ValidateKnockbackBlock(issues, skill, owner);
            ValidateTrailMode(issues, skill, owner);
            ValidateMorphBlock(issues, skill, owner);
        }

        // ====================================================== Р43, Р44, Р45 ==

        /// <summary>
        /// Р43: время или высота заданы у ВЫКЛЮЧЕННОГО блока 18 — ни перемещения, ни показа не будет.
        /// Р44: высота дуги задана при нулевом времени полёта — дуги без времени не существует.
        /// Р45: время полёта задано, а набор «старт перемещения» ПУСТ — полёта клиент не покажет.
        ///      Это не косметика: начальную точку клиент берёт ИЗ ФАКТА события, а пустой набор
        ///      сообщения не порождает вовсе (инвариант семьи презентации) — значит факта не будет
        ///      и вести модель будет неоткуда. Ошибка, а не предупреждение.
        /// </summary>
        static void ValidateCasterMoveBlock(List<InterflowIssue> issues, CompositeSkill skill, string owner)
        {
            var b = skill.casterMove;
            if (b == null) return;

            if (!b.enabled)
            {
                if (b.travelSeconds > 0f || b.leapHeight > 0f || b.passThroughDistance > 0f)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"{owner}: блок 18 «перемещение кастера» ВЫКЛЮЧЕН, но время полёта, высота прыжка " +
                        "или пролёт за точку заданы — ни одно из этих чисел не сыграет.",
                        "Движение_Отброс_Облик_Проект §4.4 (Р43)", skill));
                return;
            }

            if (b.leapHeight > 0f && b.travelSeconds <= 0f)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"{owner}: у блока 18 задана высота прыжка, а время полёта нулевое — дуги без времени " +
                    "не существует, перемещение останется мгновенным телепортом.",
                    "Движение_Отброс_Облик_Проект §4.4 (Р44)", skill));

            if (b.travelSeconds > 0f && (b.startPresentation == null || !b.startPresentation.Any))
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"{owner}: у блока 18 задано время полёта, но набор «старт перемещения» пуст — " +
                    "клиент полёта не покажет. Начальную точку он берёт из факта события, а пустой набор " +
                    "сообщения не порождает: задай хотя бы стейт анимации полёта.",
                    "Движение_Отброс_Облик_Проект §4.4 (Р45); InterflowAbility.EmitEventPresentation", skill));
        }

        // ============================================================ Р46, Р47 ==

        /// <summary>
        /// Р46: время полёта задано у ВЫКЛЮЧЕННОГО блока 15.
        /// Р47: время полёта отброса задано, а набор «цель отброшена» пуст — по той же причине, что Р45:
        ///      факта не будет, и клиент не узнает, кого и откуда вести.
        /// </summary>
        static void ValidateKnockbackBlock(List<InterflowIssue> issues, CompositeSkill skill, string owner)
        {
            var b = skill.knockback;
            if (b == null) return;

            if (!b.enabled)
            {
                if (b.travelSeconds > 0f)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"{owner}: блок 15 «отброс» ВЫКЛЮЧЕН, а время полёта задано — оно не сыграет.",
                        "Движение_Отброс_Облик_Проект §4.4 (Р46)", skill));
                return;
            }

            if (b.travelSeconds > 0f && (b.presentation == null || !b.presentation.Any))
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"{owner}: у блока 15 «отброс» задано время полёта, но набор «цель отброшена» пуст — " +
                    "клиент полёта цели не покажет (факт события с пустым набором не отправляется). " +
                    "Задай хотя бы стейт анимации или визуал.",
                    "Движение_Отброс_Облик_Проект §4.4 (Р47); InterflowAbility.EmitEventPresentation", skill));
        }

        // ================================================================== Р48 ==

        /// <summary>
        /// Р48: включён режим «шлейф по пути» блока 17, а условий для него нет —
        /// выключен блок 18 (прямой не существует) или шаг между зонами ≤ 0 (зонам негде встать).
        /// Отдельно Info: шлейф при нулевом времени полёта ложится весь одним кадром — это законно,
        /// но означает не дорожку во времени, а разом выставленный частокол.
        /// </summary>
        static void ValidateTrailMode(List<InterflowIssue> issues, CompositeSkill skill, string owner)
        {
            var z = skill.groundZone;
            if (z == null) return;

            if (!z.trailAlongCasterPath) return;

            if (!z.enabled)
            {
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    $"{owner}: режим «шлейф по пути» включён у ВЫКЛЮЧЕННОГО блока 17 — зоны не появятся.",
                    "Движение_Отброс_Облик_Проект §4.4 (Р48)", skill));
                return;
            }

            if (skill.casterMove == null || !skill.casterMove.enabled)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"{owner}: у блока 17 включён «шлейф по пути», но блок 18 «перемещение кастера» выключен — " +
                    "прямой «старт → приземление» не существует, шлейфу негде лечь.",
                    "Движение_Отброс_Облик_Проект §4.4 (Р48); CompositeSkill.ApplyGroundZone", skill));

            if (z.trailSpacing <= 0f)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"{owner}: у блока 17 включён «шлейф по пути», а шаг между зонами ≤ 0 — " +
                    "число зон посчитать нечем, шлейф не выложится.",
                    "Движение_Отброс_Облик_Проект §4.4 (Р48)", skill));

            if (skill.casterMove != null && skill.casterMove.enabled && skill.casterMove.travelSeconds <= 0f)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Info,
                    $"{owner}: шлейф по пути при нулевом времени полёта блока 18 ляжет ВЕСЬ одним кадром — " +
                    "это частокол сразу, а не дорожка во времени.",
                    "Движение_Отброс_Облик_Проект §4.4 (Р48)", skill));
        }

        // ============================================================ Р49, Р50 ==

        /// <summary>
        /// Р49: «облик на себя» вместе с требованием исходного облика блока 21 — второй каст не пройдёт.
        ///      Предупреждение, а не ошибка: это и есть замысел Inquisitor_Ascension (решение проекта §4.4).
        /// Р50: «способ атаки от заготовки» вместе с множителем ДАЛЬНОСТИ атаки в пассивных изменениях
        ///      статов того же блока — у дальности два источника, копия заготовки перезапишет множитель
        ///      (решение Artsiom 55 требует правила ровно на это).
        ///      Плюс две пустышки: флаг «способ атаки» без заготовки облика и флаг у выключенного блока.
        /// </summary>
        static void ValidateMorphBlock(List<InterflowIssue> issues, CompositeSkill skill, string owner)
        {
            var m = skill.morph;
            if (m == null) return;

            if (!m.enabled)
            {
                if (m.applyToCaster || m.copyAttackMode)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"{owner}: блок 12 «облик» ВЫКЛЮЧЕН, а флаги «облик на себя» или «способ атаки» " +
                        "заданы — превращения не будет вовсе.",
                        "Движение_Отброс_Облик_Проект §4.4 (Р50)", skill));
                return;
            }

            // Р49. Оба условия блока 21 «требовать исходный облик» и «облик на себя» у одного умения.
            if (m.applyToCaster && skill.castConditions != null && skill.castConditions.enabled
                && skill.castConditions.requireOriginalForm)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    $"{owner}: «облик на себя» вместе с условием блока 21 «требовать исходный облик» — " +
                    "второй каст под превращением не пройдёт. Если так и задумано (превращение " +
                    "не перезапускается), предупреждение можно оставить как есть.",
                    "Движение_Отброс_Облик_Проект §4.4 (Р49)", skill));

            if (!m.copyAttackMode) return;

            if (m.shapeUnit == null)
            {
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"{owner}: у блока 12 включён «способ атаки от заготовки», но сама заготовка облика " +
                    "не задана — копировать нечего.",
                    "Движение_Отброс_Облик_Проект §4.4 (Р50)", skill));
                return;
            }

            // Р50. Дальность атаки с двумя источниками.
            if (m.passiveEffects != null)
                for (int i = 0; i < m.passiveEffects.Length; i++)
                {
                    AbilityPassiveEffects pe = m.passiveEffects[i];
                    if (pe == null) continue;
                    if (pe.attackRangeChange == 0f && pe.attackRangePercentageChange == 0f) continue;

                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                        $"{owner}: у блока 12 «облик» включён «способ атаки от заготовки» И задано изменение " +
                        "дальности атаки в пассивных изменениях статов (запись " + (i + 1) + ") — " +
                        "у дальности два источника, копия заготовки перезапишет множитель. " +
                        "Оставь один способ.",
                        "Движение_Отброс_Облик_Проект §4.4 (Р50); решение Artsiom 55", skill));
                    break;
                }

            // Флаг ничего не меняет: способ атаки заготовки совпадает с носителем. Носителя у ассета
            // умения нет, поэтому сравнить можно только с заготовкой самой себя — этого не бывает;
            // правило §4.4 п.10 проекта («shapeUnit, чей способ атаки совпадает с носителем»)
            // здесь НЕ реализуемо: носитель известен только в рантайме. Факт назван в отчёте.
        }

        // ========================================================= СПРАВОЧНИК ==

        /// <summary>
        /// Р51: справочник иконок статусов не настроен под семью — не назначено служебное состояние
        /// «в полёте», либо у назначенного не та категория, либо есть значок или VFX (оно служебное
        /// и клиенту не уезжает), либо нет запрета действий. Без него отброс со временем полёта
        /// оставит цель действующей из точки, где её модель ещё не стоит.
        /// Проверка по проекту, а не по умению: справочник один.
        /// </summary>
        static void ValidateInFlightEffector(List<InterflowIssue> issues)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StatusIconCatalog>(
                "Assets/Resources/Catalogs/StatusIconCatalog.asset");
            if (catalog == null) return;   // «нет справочника» ловится своим правилом в ValidateMisc

            Effector e = catalog.inFlightEffector;

            if (e == null)
            {
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    "Справочник иконок статусов: не назначено служебное состояние «в полёте» — " +
                    "отброс с временем полёта не сможет запретить цели действия, пока её модель летит.",
                    "Движение_Отброс_Облик_Проект §4.4 (Р51); Units/Unit.InFlightStart", catalog));
                return;
            }

            if (e.category != EffectorCategory.InFlight)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"Служебное состояние «в полёте» ({e.name}): категория «{e.category}», а обязана быть " +
                    "«В полёте (служебная)» — именно по ней выводится флаг полёта и именно она проводит " +
                    "наложение мимо сопротивлений и иммунитета к контролю.",
                    "Движение_Отброс_Облик_Проект §4.4 (Р51); Units/Unit.Control.CollectControl", e));

            if (e.icon != null || e.VFX != null)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    $"Служебное состояние «в полёте» ({e.name}): задан значок или VFX — состояние служебное " +
                    "и клиенту уезжать не должно (решение Artsiom 50: «без значка»).",
                    "Движение_Отброс_Облик_Проект §4.4 (Р51)", e));

            if (!e.disarms && !e.mutes && !e.stuns)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"Служебное состояние «в полёте» ({e.name}): не включён ни один запрет действий — " +
                    "нужны признаки «Обезоруживает» и «Накладывает немоту», иначе летящая цель атакует " +
                    "и кастует из точки, где её модель ещё не стоит.",
                    "Движение_Отброс_Облик_Проект §4.4 (Р51)", e));
        }
    }
}
