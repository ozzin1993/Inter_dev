using System.Text;
using UnityEngine;

namespace StrategyCore
{
    // ===== КОНСТРУКТОР УМЕНИЯ: СБОРЩИКИ СТРОК ЛОГА (правило 22 — партиал по фиче) ==
    // Здесь собираются ВСЕ строки уровня «Полный» для конструктора умения: общие строки каста
    // и строки блоков. В местах применения остаётся по одному короткому вызову — файлы блоков
    // не раздуваются (CompositeSkill.Blocks.cs и без того ходил за порог крупного файла).
    //
    // ПРАВИЛА ЭТОГО ФАЙЛА:
    //   • Каждый вызов сборщика на месте применения стоит под `if (InterflowDebug.FullOn)`:
    //     каст по области перебирает десятки целей, и склейка строки без гейта стоила бы кадра
    //     даже при выключенном уровне.
    //   • Ни один сборщик не меняет состояния — только читает уже посчитанное (правило 7).
    //     Исключение одно и осознанное: радиус области в строке «цели собраны» берётся тем же
    //     LevelValue, что и внутри CollectTargets, — наружу метод его не отдаёт, а без радиуса
    //     строка не отвечает на вопрос «почему целей столько».
    //   • Состояния на ассете НЕТ и быть не может: CompositeSkill — ScriptableObject, один экземпляр
    //     на всех носителей и оба пира. Всё, что нужно строке, приходит параметром.
    //   • Единый шаблон строки:
    //     УМЕНИЕ «<имя>» БЛОК <N> <название>: <сработал|не сработал> | цель=<...> | <поле>=<значение> [| причина=<...>]
    //   • Нумерация блоков — сквозная нумерация конструктора (1–19), как в редакторе умений
    //     и в промте линии. Комментарии внутри файлов блоков нумеруют их по-своему — на строку
    //     лога это не влияет (решение Artsiom 08.09.2026).
    //   • Существующие строки уровней «События» и «Подробно» этот файл не трогает и не дублирует.
    public partial class CompositeSkill
    {
        // ============================================================ ОБЩЕЕ ==

        /// <summary>
        /// Имя умения для лога: первый элемент abilityName, если он заполнен, иначе имя ассета.
        /// То же правило, что у существующей строки уровня «Подробно» (CompositeSkill.Cast.cs).
        /// </summary>
        string SkillDisplayName()
        {
            return (abilityName != null && abilityName.Length > 0 && !string.IsNullOrEmpty(abilityName[0]))
                   ? abilityName[0] : name;
        }

        /// <summary>Начало любой строки.</summary>
        string LogHead() { return "УМЕНИЕ «" + SkillDisplayName() + "»"; }

        /// <summary>Общая строка каста — без номера блока.</summary>
        void WriteCast(string body) { InterflowDebug.Full(LogHead() + ": " + body); }

        /// <summary>Строка блока.</summary>
        void WriteBlock(int number, string body)
        {
            InterflowDebug.Full(LogHead() + " БЛОК " + number + " " + BlockName(number) + ": " + body);
        }

        /// <summary>Блок включён и сработал.</summary>
        void LogBlockDone(int number, Unit target, string fields)
        {
            WriteBlock(number, "сработал | цель=" + InterflowDebug.Name(target) + " | " + fields);
        }

        /// <summary>Блок включён, но ничего не сделал — пишем ПРИЧИНУ, ради неё лог и заводился.</summary>
        void LogBlockSkipped(int number, Unit target, string reason)
        {
            WriteBlock(number, "не сработал | цель=" + InterflowDebug.Name(target) + " | причина=" + reason);
        }

        /// <summary>Названия покрытых блоков. Номера — сквозные, по конструктору.</summary>
        static string BlockName(int number)
        {
            switch (number)
            {
                case 5: return "контроль";
                case 6: return "состояния";
                case 10: return "щит";
                case 11: return "ослепление";
                case 16: return "призыв";
            }

            return "неизвестный блок";
        }

        // ==================================================== ЧИСЛА И ПЕРЕЧНИ ==

        static string N(float value) { return value.ToString("0.#"); }

        static string Sec(float value) { return N(value) + " с"; }

        static string Yes(bool value) { return value ? "да" : "нет"; }

        /// <summary>Записи блока состояний: имя ассета, сила и длительность на этом уровне.</summary>
        string EffectorRecordsText(int level)
        {
            if (effectors == null || effectors.records == null || effectors.records.Length == 0) return "нет";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < effectors.records.Length; i++)
            {
                SkillEffectorRecord r = effectors.records[i];
                if (r == null || r.effector == null) continue;

                if (sb.Length > 0) sb.Append("; ");

                float duration = RecordDuration(r, level);
                sb.Append(r.effector.name)
                  .Append(" сила=").Append(N(RecordPower(r, level)))
                  .Append(" длительность=").Append(duration > 0f ? Sec(duration) : "из ассета");
            }

            return sb.Length > 0 ? sb.ToString() : "нет";
        }

        // ============================================================== КАСТ ==

        /// <summary>Цель выбрана стратегией умения (каст с кнопки, режимы «умного выбора»).</summary>
        void LogCastAim(Unit castingUnit, Unit picked)
        {
            WriteCast("цель выбрана стратегией " + targetStrategy +
                      " | кастер=" + InterflowDebug.Name(castingUnit) +
                      " | цель=" + InterflowDebug.Name(picked));
        }

        /// <summary>Стратегия цель не нашла — каст сгорает впустую, откат тратится штатно.</summary>
        void LogCastNoTarget()
        {
            WriteCast("каста нет | причина=стратегия " + targetStrategy + " не нашла цель");
        }

        /// <summary>Чем доставляются урон, оглушение и эффекторы.</summary>
        void LogCastDelivery(bool viaProjectile)
        {
            WriteCast("доставка=" + (viaProjectile ? "снарядом" : "мгновенно"));
        }

        /// <summary>
        /// Клиент дальше не считает. Гейтов два: ранний — в ветке выбора цели стратегией,
        /// поздний — общий серверный гейт перед блоками эффектов (правило 6).
        /// </summary>
        /// <param name="reason">Причина раннего выхода; null у общего гейта.</param>
        void LogCastClientStop(string reason)
        {
            WriteCast("клиент дальше не считает" + (string.IsNullOrEmpty(reason) ? "" : " | причина=" + reason));
        }

        /// <summary>
        /// Набор целей. Радиус берётся тем же LevelValue, что и внутри CollectTargets:
        /// наружу метод его не отдаёт, а без него неясно, почему целей столько.
        /// </summary>
        void LogCastTargets(int level, int collected)
        {
            WriteCast("цели собраны | режим=" + targetMode +
                      " | радиус=" + N(LevelValue(radius, level)) +
                      " | лимит=" + (maxTargets > 0 ? maxTargets.ToString() : "без лимита") +
                      " | отбор=" + multiPick +
                      " | включая себя=" + Yes(includeSelf) +
                      " | собрано=" + collected);
        }

        // ============================================================ БЛОКИ ==

        /// <summary>Блок 5 «контроль»: оглушение, безоружие, немота.</summary>
        void LogStatus(Unit target, float stun, float disarm, float mute, bool stunCarriedByProjectile)
        {
            if (stun <= 0f && disarm <= 0f && mute <= 0f)
            {
                LogBlockSkipped(5, target, stunCarriedByProjectile
                    ? "оглушение отдано снаряду, безоружие и немота нулевые"
                    : "все три поля нулевые");
                return;
            }

            LogBlockDone(5, target, "оглушение=" + Sec(stun) +
                                    " | безоружие=" + Sec(disarm) +
                                    " | немота=" + Sec(mute) +
                                    (stunCarriedByProjectile ? " | оглушение отдано снаряду" : ""));
        }

        /// <summary>Блок 6 «состояния»: записи блока плюс отдельный значок состояния.</summary>
        void LogEffectors(Unit target, int level, int applied)
        {
            bool blockOn = effectors != null && effectors.enabled;

            // Блок выключен и значка нет — молчим: иначе каждый каст даёт строку ни о чём.
            if (!blockOn && statusEffector == null) return;

            string badge = " | значок=" + (statusEffector != null ? statusEffector.name : "нет");

            if (applied == 0)
            {
                if (statusEffector == null)
                {
                    LogBlockSkipped(6, target, blockOn ? "ни одной заполненной записи" : "блок выключен");
                    return;
                }

                LogBlockDone(6, target, "записей наложено=0" + badge);
                return;
            }

            LogBlockDone(6, target, "записей наложено=" + applied +
                                    " | " + EffectorRecordsText(level) + badge);
        }

        /// <summary>Блок 10 «щит»: объём, срок и включённые довески.</summary>
        void LogShieldApplied(Unit target, float amount, float duration,
                              bool hasReaction, bool hasRetaliation, bool hasIncomingRule)
        {
            StringBuilder extras = new StringBuilder();
            if (hasReaction) extras.Append("реакция на пробитие");
            if (hasRetaliation) { if (extras.Length > 0) extras.Append(", "); extras.Append("ответ на удар"); }
            if (hasIncomingRule) { if (extras.Length > 0) extras.Append(", "); extras.Append("снижение входящего урона"); }

            LogBlockDone(10, target, "объём=" + N(amount) +
                                     " | длительность=" + (duration > 0f ? Sec(duration) : "бессрочно") +
                                     " | довески=" + (extras.Length > 0 ? extras.ToString() : "нет"));
        }

        /// <summary>Блок 10 «щит»: включён, но не наложен.</summary>
        void LogShieldSkipped(Unit target, string reason) { LogBlockSkipped(10, target, reason); }

        /// <summary>Блок 11 «ослепление»: шанс промаха и срок.</summary>
        void LogBlind(Unit target, float chance, float duration)
        {
            if (chance <= 0f || duration <= 0f)
            {
                LogBlockSkipped(11, target, chance <= 0f && duration <= 0f
                    ? "шанс и длительность нулевые"
                    : (chance <= 0f ? "шанс нулевой" : "длительность нулевая"));
                return;
            }

            LogBlockDone(11, target, "шанс=" + N(chance) + " | длительность=" + Sec(duration));
        }

        /// <summary>
        /// Блок 16 «призыв»: цели у него нет — призыв идёт разом на весь каст.
        /// Поля печатаются ПО РЕЖИМУ, только те, что реально ушли в вызов менеджера матча:
        /// «фикс. отряд» не принимает время жизни, «дубль последней волны» — количество.
        /// </summary>
        /// <param name="spawned">Фактически призвано; −1 — режим этого числа не возвращает.</param>
        void LogSummon(Unit castingUnit, int count, float lifetime, int spawned)
        {
            if (spawned == 0)
            {
                LogSummonSkipped(castingUnit, "призвано ноль: пустой префаб, нулевое количество или достигнут лимит живых");
                return;
            }

            string fields;
            switch (summon.mode)
            {
                case SkillSummonMode.FromCaster:
                    fields = "призвано=" + spawned + " из " + count +
                             " | время жизни=" + (lifetime > 0f ? Sec(lifetime) : "навсегда");
                    break;

                case SkillSummonMode.FixedSquadAtEnemyPoint:
                    fields = "призвано=" + spawned + " из " + count; // время жизни этот режим не принимает
                    break;

                default: // LastWave: состав берётся из последней волны, количество в вызов не идёт
                    fields = "время жизни=" + (lifetime > 0f ? Sec(lifetime) : "навсегда");
                    break;
            }

            WriteBlock(16, "сработал | кастер=" + InterflowDebug.Name(castingUnit) +
                           " | режим=" + summon.mode + " | " + fields);
        }

        /// <summary>Блок 16 «призыв»: включён, но не сработал.</summary>
        void LogSummonSkipped(Unit castingUnit, string reason)
        {
            WriteBlock(16, "не сработал | кастер=" + InterflowDebug.Name(castingUnit) + " | причина=" + reason);
        }

        // ========================================================== АВТОКАСТ ==

        /// <summary>
        /// Умение НЕ сработало само. Пишется только отказ: успешный автокаст виден по строкам каста.
        /// ВНИМАНИЕ: метод готовности зовётся на тике у каждого носителя, и подавить повтор нечем —
        /// состояние хранить негде (ассет один на всех). Цена решения Artsiom от 08.09.2026.
        /// </summary>
        void LogAutoCastRefused(Unit castingUnit, string reason)
        {
            WriteCast("автокаст не сработал | носитель=" + InterflowDebug.Name(castingUnit) +
                      " | причина=" + reason);
        }
    }
}
