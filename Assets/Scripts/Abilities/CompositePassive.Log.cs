using System.Text;
using UnityEngine;

namespace StrategyCore
{
    // ===== КОНСТРУКТОР ПАССИВКИ: СБОРЩИКИ СТРОК ЛОГА (правило 22 — партиал по фиче) ==
    // Здесь собираются ВСЕ строки уровня «Полный» для конструктора пассивок: восемь блоков свойств,
    // пять реакций и состав выданного. В местах применения остаётся по одному вызову.
    //
    // ПРАВИЛА ЭТОГО ФАЙЛА:
    //   • Каждый вызов сборщика на месте применения стоит под `if (InterflowDebug.FullOn)`:
    //     реакции сидят на каждом входящем ударе, аура тикает десять раз в секунду у каждого носителя,
    //     и склейка строки без гейта стоила бы кадра даже при выключенном уровне.
    //   • Ни один сборщик НИЧЕГО не считает и не меняет — только читает уже посчитанное (правило 7).
    //   • Единый шаблон строки:
    //     ПАССИВКА «<имя>» <БЛОК N|РЕАКЦИЯ N> <название>: <что произошло> | носитель=<...> | <поле>=<значение> [| причина=<...>]
    //   • Существующие строки уровней «События» и «Подробно» этот файл не трогает и не дублирует.
    public partial class CompositePassive
    {
        // ============================================================ ОБЩЕЕ ==

        /// <summary>Начало любой строки: имя пассивки готовым методом.</summary>
        string LogHead() { return "ПАССИВКА «" + PassiveDisplayName() + "»"; }

        /// <summary>Написать строку блока свойств.</summary>
        void WriteBlock(int number, string body)
        {
            InterflowDebug.Full(LogHead() + " БЛОК " + number + " " + BlockName(number) + ": " + body);
        }

        /// <summary>Написать строку реакции.</summary>
        void WriteReaction(int number, string body)
        {
            InterflowDebug.Full(LogHead() + " РЕАКЦИЯ " + number + " " + ReactionName(number) + ": " + body);
        }

        /// <summary>Блок или реакция включены, но не сработали — пишем, какой именно фильтр отсёк.</summary>
        void LogBlockSkipped(int number, Unit unit, string stage, string reason)
        {
            WriteBlock(number, stage + " пропущена | носитель=" + InterflowDebug.Name(unit) + " | причина=" + reason);
        }

        /// <summary>То же для реакций.</summary>
        void LogReactionSkipped(int number, Unit unit, string stage, string reason)
        {
            WriteReaction(number, stage + " пропущено | носитель=" + InterflowDebug.Name(unit) + " | причина=" + reason);
        }

        static string BlockName(int number)
        {
            switch (number)
            {
                case 1: return "изменение характеристик";
                case 2: return "иммунитет к контролю";
                case 3: return "сопротивления и слабости";
                case 4: return "постоянная невидимость";
                case 5: return "пробитие брони";
                case 6: return "сплеш атаки";
                case 7: return "эффекторы к своим атакам";
                case 8: return "аура";
            }

            return "неизвестный блок";
        }

        static string ReactionName(int number)
        {
            switch (number)
            {
                case 1: return "носителя ударили";
                case 2: return "носитель погиб";
                case 3: return "носитель кого-то убил";
                case 4: return "здоровье ниже доли";
                case 5: return "носитель попал по цели";
            }

            return "неизвестная реакция";
        }

        // ==================================================== ЧИСЛА И ПЕРЕЧНИ ==

        static string N(float value) { return value.ToString("0.#"); }

        static string N2(float value) { return value.ToString("0.##"); }

        static string Signed(float value) { return (value > 0f ? "+" : "") + N(value); }

        /// <summary>Доля в процентах со знаком: 0.25 → «+25 %».</summary>
        static string SignedPercent(float fraction) { return (fraction > 0f ? "+" : "") + N(fraction * 100f) + " %"; }

        static string Yes(bool value) { return value ? "да" : "нет"; }

        static string TypeName(DamageType type) { return type != null ? type.name : "нет"; }

        static void Append(StringBuilder sb, string part)
        {
            if (sb.Length > 0) sb.Append("; ");
            sb.Append(part);
        }

        /// <summary>Одна характеристика: абсолютная прибавка и процентная. Ноль в обоих полях — «не меняли».</summary>
        static void AddStat(StringBuilder sb, string title, float flat, float percent)
        {
            if (flat != 0f) Append(sb, title + " " + Signed(flat));
            if (percent != 0f) Append(sb, title + " " + SignedPercent(percent));
        }

        /// <summary>
        /// Что именно меняет набор характеристик — только заполненные поля.
        /// Открыт для сборки: тем же набором пользуется базовый класс Passive (правило 5 — один
        /// источник истины для формата строки, вместо копии на каждый класс пассивки).
        /// </summary>
        internal static string StatsText(AbilityPassiveEffects effects)
        {
            if (effects == null) return "нет";

            StringBuilder sb = new StringBuilder();

            AddStat(sb, "урон", effects.damageChange, effects.damagePercentageChange);
            AddStat(sb, "скорость атаки", effects.attackSpeedChange, effects.attackSpeedPercentageChange);
            AddStat(sb, "дальность атаки", effects.attackRangeChange, effects.attackRangePercentageChange);
            AddStat(sb, "броня", effects.armorChange, effects.armorPercentageChange);
            AddStat(sb, "скорость движения", effects.moveSpeedChange, effects.moveSpeedPercentageChange);
            AddStat(sb, "здоровье", effects.healthChange, effects.healthPercentageChange);
            AddStat(sb, "восстановление здоровья", effects.healthRegenChange, effects.healthRegenPercentageChange);
            AddStat(sb, "мана", effects.manaChange, effects.manaPercentageChange);
            AddStat(sb, "восстановление маны", effects.manaRegenChange, effects.manaRegenPercentageChange);
            AddStat(sb, "опыт за убийство", effects.xpRewardChange, effects.xpRewardPercentageChange);

            if (effects.visionChange != 0) Append(sb, "обзор " + Signed(effects.visionChange));

            int attributes = (effects.attributeChanges != null ? effects.attributeChanges.Length : 0)
                           + (effects.attributePercentageChange != null ? effects.attributePercentageChange.Length : 0);
            if (attributes > 0) Append(sb, "атрибутов: " + attributes);

            return sb.Length > 0 ? sb.ToString() : "нет изменений";
        }

        /// <summary>Имена состояний через запятую.</summary>
        static string EffectorNames(Effector[] effectors)
        {
            if (effectors == null || effectors.Length == 0) return "нет";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < effectors.Length; i++)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(effectors[i] != null ? effectors[i].name : "пусто");
            }

            return sb.ToString();
        }

        /// <summary>Строки сопротивлений: категория и доля.</summary>
        static string ResistancesText(ResistanceEntry[] entries)
        {
            if (entries == null || entries.Length == 0) return "нет";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] == null) continue;

                if (sb.Length > 0) sb.Append("; ");
                sb.Append(entries[i].category).Append(" ").Append(SignedPercent(entries[i].value));
            }

            return sb.Length > 0 ? sb.ToString() : "нет";
        }

        // ================================================= 1–8. БЛОКИ СВОЙСТВ ==

        /// <summary>Блок 1: что передано в набор характеристик носителя.</summary>
        void LogStats(Unit unit, AbilityPassiveEffects effects, bool applied)
        {
            WriteBlock(1, (applied ? "выдано" : "возвращено") + " | носитель=" + InterflowDebug.Name(unit) +
                          " | характеристики=" + StatsText(effects));
        }

        /// <summary>Блок 2: иммунитет к контролю. Счётчик источников закрыт в ядре — виден только признак.</summary>
        void LogControlImmunity(Unit unit, bool applied, bool activeAfter)
        {
            WriteBlock(2, (applied ? "выдан" : "снят") + " | носитель=" + InterflowDebug.Name(unit) +
                          " | иммунитет после операции=" + Yes(activeAfter));
        }

        /// <summary>Блок 3: сопротивления и слабости.</summary>
        void LogResistances(Unit unit, ResistanceEntry[] entries, bool applied)
        {
            WriteBlock(3, applied
                ? "выдано | носитель=" + InterflowDebug.Name(unit) + " | строки=" + ResistancesText(entries)
                : "снято | носитель=" + InterflowDebug.Name(unit) + " | сняты все вклады источника");
        }

        /// <summary>Блок 4: постоянная невидимость.</summary>
        void LogInvisibility(Unit unit, bool applied)
        {
            WriteBlock(4, applied
                ? "включена | носитель=" + InterflowDebug.Name(unit)
                : "отметка снята | носитель=" + InterflowDebug.Name(unit) +
                  " | невидимость юнита НЕ гасится (в ядре нет счётчика источников)");
        }

        /// <summary>Блок 5: пробитие брони. Доля этого умения и то, что осталось у носителя после операции.</summary>
        void LogArmorPierce(Unit unit, float fraction, float totalAfter, bool applied)
        {
            WriteBlock(5, (applied ? "выдано" : "снято") + " | носитель=" + InterflowDebug.Name(unit) +
                          " | доля умения=" + N2(fraction) +
                          " | у носителя стало=" + N2(totalAfter));
        }

        /// <summary>Блок 6: сплеш атаки — до и после.</summary>
        void LogSplashApplied(Unit unit, SplashState before, bool splashAfter, float radiusAfter, float reductionAfter)
        {
            WriteBlock(6, "выдано | носитель=" + InterflowDebug.Name(unit) +
                          " | сплеш был=" + Yes(before.isSplash) + " стал=" + Yes(splashAfter) +
                          " | радиус " + N(before.radius) + " → " + N(radiusAfter) +
                          " | спад " + N2(before.reduction) + " → " + N2(reductionAfter));
        }

        /// <summary>Блок 6: возврат исходных параметров сплеша.</summary>
        void LogSplashRemoved(Unit unit, SplashState restored)
        {
            WriteBlock(6, "снято | носитель=" + InterflowDebug.Name(unit) +
                          " | вернули сплеш=" + Yes(restored.isSplash) +
                          " | радиус=" + N(restored.radius) +
                          " | спад=" + N2(restored.reduction));
        }

        /// <summary>Блок 7: набор состояний атаки до и после.</summary>
        void LogAttackEffectors(Unit unit, Effector[] before, Effector[] after, bool applied)
        {
            WriteBlock(7, (applied ? "выдано" : "снято") + " | носитель=" + InterflowDebug.Name(unit) +
                          " | было=" + EffectorNames(before) +
                          " | стало=" + EffectorNames(after));
        }

        /// <summary>Блок 8: аура включена у носителя.</summary>
        void LogAuraApplied(Unit unit, float radius, PassiveAuraCondition condition, float damagePerSecond,
                            DamageType damageType, Effector[] effectors)
        {
            WriteBlock(8, "включена | носитель=" + InterflowDebug.Name(unit) +
                          " | радиус=" + N(radius) +
                          " | условие=" + AuraConditionName(condition) +
                          " | урон в секунду=" + N(damagePerSecond) +
                          " | тип урона=" + TypeName(damageType) +
                          " | состояния=" + EffectorNames(effectors));
        }

        /// <summary>Блок 8: носитель убран из списка тика.</summary>
        void LogAuraRemoved(Unit unit)
        {
            WriteBlock(8, "снята | носитель=" + InterflowDebug.Name(unit) + " | носитель убран из списка тика");
        }

        /// <summary>
        /// Блок 8: тик ауры. Строка пишется ТОЛЬКО когда аура фактически сработала — условие выполнено
        /// и кому-то что-то досталось. Тик без срабатывания молчит: он идёт десять раз в секунду
        /// у каждого носителя, и строка «условие не выполнено» забила бы весь лог
        /// (осознанное отступление от правила «не сработал — пиши причину», решение Artsiom 08.09.2026).
        /// </summary>
        void LogAuraTick(Unit unit, int targets, float damagePerTarget, Effector[] effectors, bool includeSelf)
        {
            WriteBlock(8, "сработала | носитель=" + InterflowDebug.Name(unit) +
                          " | задето целей=" + targets +
                          " | урон за тик каждому=" + N2(damagePerTarget) +
                          " | состояния=" + EffectorNames(effectors) +
                          " | себе тоже=" + Yes(includeSelf));
        }

        static string AuraConditionName(PassiveAuraCondition condition)
        {
            switch (condition)
            {
                case PassiveAuraCondition.WhileMoving: return "пока носитель движется";
                case PassiveAuraCondition.WhileHurt: return "пока не хватает ХП";
            }

            return "всегда";
        }

        // ================================================= СОСТАВ ВЫДАННОГО ==

        /// <summary>Перечень блоков, реально выданных этому носителю при открытии умения.</summary>
        void LogGranted(Unit unit, Carrier c)
        {
            InterflowDebug.Full(LogHead() + " ОТКРЫТА: выдано: " + CarrierBlocks(c) +
                                " | носитель=" + InterflowDebug.Name(unit) +
                                " | уровень=" + c.level);
        }

        /// <summary>
        /// Перечень блоков, снятых при закрытии умения. Перечень берётся СНИМКОМ до снятия:
        /// методы Remove* гасят флаги в Carrier, и после них перечислять было бы нечего.
        /// </summary>
        void LogRevoked(Unit unit, string blocks)
        {
            InterflowDebug.Full(LogHead() + " ЗАКРЫТА: снято: " + blocks +
                                " | носитель=" + InterflowDebug.Name(unit));
        }

        /// <summary>
        /// Перечень включённых блоков носителя — общий для строк открытия и закрытия.
        /// При закрытии блок 4 помечается особо: снимается только отметка, сама невидимость
        /// у юнита остаётся (в ядре нет счётчика источников — см. RemoveInvisibility).
        /// </summary>
        string CarrierBlocks(Carrier c, bool forRevoke = false)
        {
            StringBuilder sb = new StringBuilder();

            if (c.stats) Append(sb, "1 характеристики");
            if (c.controlImmunity) Append(sb, "2 иммунитет к контролю");
            if (c.resistances) Append(sb, "3 сопротивления");
            if (c.invisibility) Append(sb, forRevoke ? "4 невидимость (только отметка)" : "4 невидимость");
            if (c.armorPierceFraction > 0f) Append(sb, "5 пробитие брони");
            if (c.splash != null) Append(sb, "6 сплеш");
            if (c.originalAttackEffectors != null) Append(sb, "7 эффекторы атаки");
            if (c.aura) Append(sb, "8 аура");

            return sb.Length > 0 ? sb.ToString() : "ничего";
        }

        // ==================================================== 1–4. РЕАКЦИИ ==

        /// <summary>Какие реакции подключены носителю и с какими числами его уровня.</summary>
        void LogReactionsWired(Unit unit, ReactionState st, int level)
        {
            StringBuilder sb = new StringBuilder();

            if (st.incomingRule != null) Append(sb, "1 правило входящего урона");
            if (st.damagedHandler != null) Append(sb, "1 ответный удар");
            if (st.hitMissedHandler != null) Append(sb, "1 ответ при уходе");
            if (st.dieHandler != null) Append(sb, "2 гибель");
            if (IsKillEnabled()) Append(sb, "3 добивание");
            if (st.hpHandler != null) Append(sb, "4 порог здоровья");

            InterflowDebug.Full(LogHead() + " РЕАКЦИИ ПОДКЛЮЧЕНЫ: " + (sb.Length > 0 ? sb.ToString() : "ничего") +
                                " | носитель=" + InterflowDebug.Name(unit) +
                                " | уровень=" + level);
        }

        /// <summary>Реакции включены в ассете, но ни одна не подключилась.</summary>
        void LogReactionsNotWired(Unit unit, string reason)
        {
            InterflowDebug.Full(LogHead() + " РЕАКЦИИ НЕ ПОДКЛЮЧЕНЫ | носитель=" + InterflowDebug.Name(unit) +
                                " | причина=" + reason);
        }

        /// <summary>Что снято с носителя. Путь снятия называем: их несколько, строка не должна написаться дважды.</summary>
        void LogReactionsUnwired(Unit unit, ReactionState st, string path)
        {
            StringBuilder sb = new StringBuilder();

            if (st.incomingRule != null) Append(sb, "1 правило входящего урона");
            if (st.damagedHandler != null) Append(sb, "1 ответный удар");
            if (st.hitMissedHandler != null) Append(sb, "1 ответ при уходе");
            if (st.dieHandler != null) Append(sb, "2 гибель");
            if (st.hpHandler != null) Append(sb, "4 порог здоровья");

            InterflowDebug.Full(LogHead() + " РЕАКЦИИ СНЯТЫ: " + (sb.Length > 0 ? sb.ToString() : "ничего") +
                                " | носитель=" + InterflowDebug.Name(unit) +
                                " | путь=" + path);
        }

        /// <summary>Реакция 1: с какими числами заведено правило входящего урона.</summary>
        void LogIncomingRuleWired(Unit unit, InterflowCombat.IncomingRule rule)
        {
            WriteReaction(1, "правило входящего урона заведено | носитель=" + InterflowDebug.Name(unit) +
                             " | множитель=" + N2(rule.multiplier) +
                             " | шанс ухода=" + N2(rule.evadeChance) +
                             " | вычет числом=" + N(rule.flatBlock) +
                             " | нижняя граница=" + N(rule.minDamage) +
                             " | удвоение вычета ниже ХП=" + N2(rule.doubleBlockBelowHp) +
                             " | только спереди=" + Yes(rule.onlyFromFront) +
                             " | сектор=" + N(rule.frontAngle) +
                             " | только прямой удар=" + Yes(rule.onlyDirectAttack) +
                             " | тип урона=" + TypeName(rule.onlyType));
        }

        /// <summary>Реакция 1: ответный удар подключён.</summary>
        void LogCounterWired(Unit unit, bool onlyOnEvade)
        {
            WriteReaction(1, "ответный удар подключён | носитель=" + InterflowDebug.Name(unit) +
                             " | режим=" + (onlyOnEvade ? "только когда удар не достиг цели" : "на каждый полученный удар"));
        }

        /// <summary>Реакция 1: ответный удар по кругу сработал.</summary>
        void LogCounterFired(Unit victim, int hit, float damage, DamageType type, Effector[] effectors)
        {
            WriteReaction(1, "ответный удар | носитель=" + InterflowDebug.Name(victim) +
                             " | задето целей=" + hit +
                             " | урон каждому=" + N(damage) +
                             " | тип урона=" + TypeName(type) +
                             " | состояния=" + EffectorNames(effectors));
        }

        /// <summary>Реакция 1: встречный удар по бьющему, когда удар не достиг цели.</summary>
        void LogCounterOnEvadeFired(Unit victim, Unit attacker, float damage, DamageType type, Effector[] effectors)
        {
            WriteReaction(1, "встречный удар при уходе | носитель=" + InterflowDebug.Name(victim) +
                             " | по кому=" + InterflowDebug.Name(attacker) +
                             " | урон=" + N(damage) +
                             " | тип урона=" + TypeName(type) +
                             " | состояния=" + EffectorNames(effectors));
        }

        /// <summary>Реакция 2: что произошло в месте гибели носителя.</summary>
        void LogDeathFired(Unit unit, float radius, int enemiesHit, float enemyDamage, DamageType enemyType,
                           int healed, float healFlat, float healPercent, bool zone, int summons)
        {
            WriteReaction(2, "сработала | носитель=" + InterflowDebug.Name(unit) +
                             " | радиус=" + N(radius) +
                             " | задето врагов=" + enemiesHit +
                             " | урон каждому=" + N(enemyDamage) +
                             " | тип урона=" + TypeName(enemyType) +
                             " | вылечено союзников=" + healed +
                             " | лечение числом=" + N(healFlat) +
                             " | лечение долей от максимума=" + N2(healPercent) +
                             " | пятно=" + Yes(zone) +
                             " | призвано=" + summons);
        }

        /// <summary>Реакция 3: носитель добил цель.</summary>
        void LogKillFired(Unit killer, Unit victim, float heal, int allies, Effector[] cryEffectors)
        {
            WriteReaction(3, "сработала | носитель=" + InterflowDebug.Name(killer) +
                             " | добита цель=" + InterflowDebug.Name(victim) +
                             " | лечение носителю=" + N(heal) +
                             " | задето союзников=" + allies +
                             " | состояния союзникам=" + EffectorNames(cryEffectors));
        }

        /// <summary>Реакция 4: здоровье пересекло порог вниз.</summary>
        void LogHpBelowFired(Unit unit, float threshold, float current, Effector[] selfEffectors, int allies)
        {
            WriteReaction(4, "сработала | носитель=" + InterflowDebug.Name(unit) +
                             " | порог=" + N2(threshold) +
                             " | текущая доля=" + N2(current) +
                             " | состояния себе=" + EffectorNames(selfEffectors) +
                             " | задето союзников=" + allies);
        }

        // ====================================== 5. НОСИТЕЛЬ ПОПАЛ ПО ЦЕЛИ ==

        /// <summary>Реакция 5: блок включён, но удар отсеян — какой именно фильтр.</summary>
        void LogOnHitSkipped(Unit byUnit, Unit target, string reason)
        {
            WriteReaction(5, "не сработала | носитель=" + InterflowDebug.Name(byUnit) +
                             " | цель=" + InterflowDebug.Name(target) +
                             " | причина=" + reason);
        }

        /// <summary>
        /// Реакция 5: счётчик «каждый N-й удар». Строка на КАЖДЫЙ засчитанный удар (решение Artsiom 08.09.2026):
        /// смысл счётчика виден только по накоплению.
        /// </summary>
        void LogOnHitCounter(Unit byUnit, int hits, int everyNth, bool reached)
        {
            WriteReaction(5, (reached ? "счёт достигнут, счётчик обнулён" : "удар засчитан") +
                             " | носитель=" + InterflowDebug.Name(byUnit) +
                             " | счёт=" + hits + "/" + everyNth);
        }

        /// <summary>Реакция 5: бросок шанса.</summary>
        void LogOnHitChance(Unit byUnit, float roll, float chance, bool passed)
        {
            WriteReaction(5, (passed ? "бросок шанса пройден" : "бросок шанса не прошёл") +
                             " | носитель=" + InterflowDebug.Name(byUnit) +
                             " | выпало=" + N2(roll) +
                             " | порог=" + N2(chance));
        }

        /// <summary>Реакция 5: поставлен собственный откат блока.</summary>
        void LogOnHitCooldown(Unit byUnit, float seconds)
        {
            WriteReaction(5, "откат поставлен | носитель=" + InterflowDebug.Name(byUnit) +
                             " | длительность=" + N(seconds) + " сек");
        }

        /// <summary>Реакция 5: сработавший эффект из десяти. Что именно — в аргументах, порядок задан кодом.</summary>
        void LogOnHitEffect(Unit byUnit, Unit target, string effect, string detail)
        {
            WriteReaction(5, effect + " | носитель=" + InterflowDebug.Name(byUnit) +
                             " | цель=" + InterflowDebug.Name(target) +
                             " | " + detail);
        }
    }
}
