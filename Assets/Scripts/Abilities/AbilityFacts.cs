using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// [Interflow 2026-09-10 passive-facts-2] ПОКАЗ СРАБАТЫВАНИЙ ПАССИВОК, СОБРАННЫХ КОДОМ.
    ///
    /// До этого шага показ был только у конструктора пассивки (CompositePassive.Facts.cs), а два
    /// десятка старых классов-пассивок не показывали НИЧЕГО: ни надписи, ни зрительного эффекта,
    /// ни звука. Проверять их в бою было нечем.
    ///
    /// Своего механизма здесь не заводится (правило 2): факты едут тем же каналом разовых событий
    /// боя, что «Мимо», «Щит» и числа урона. Отличие от конструктора одно: слово выбирается не по
    /// номеру блока, а по САМОМУ УМЕНИЮ — число факта везёт его id, и клиентский презентер берёт
    /// название из ассета умения. Иначе на каждый из двадцати классов пришлось бы заводить свою
    /// причину и своё поле со словом в настройках.
    ///
    /// Отдельный статический класс, а не метод базового класса: часть старых пассивок наследуется
    /// от Ability, часть от InterflowAbility, а SkvernaExplosion — вообще статический помощник.
    /// Общего предка у них нет.
    ///
    /// Всё серверное: гейты внутри канала (ServerCanSend / StillRegistered). На клиенте вызовы
    /// отсюда становятся пустыми — сообщение не уходит и локально факт не поднимается.
    ///
    /// Выключатель показа общий с конструктором — InterflowDebug.showPassiveFacts.
    /// </summary>
    public static class AbilityFacts
    {
        /// <summary>
        /// Пассивка СРАБОТАЛА на носителе (удар, порог, клич — что угодно разовое).
        /// Замок технологии здесь не проверяется: это событие боя, а не открытие умения.
        /// </summary>
        public static void Proc(Ability ability, Unit carrier)
            => Send(ability, carrier, BattleFactReason.PassiveProc);

        /// <summary>
        /// Пассивка ВЫДАНА носителю. Пишется только у пассивок с замком технологии, то есть
        /// у специализаций: ядро зовёт Unlock у безтеховой пассивки ЗАНОВО на каждое открытие
        /// любой технологии, и без этого условия одно открытие тира залило бы экран надписями.
        /// Тот же отбор и по той же причине стоит у лога включения в <see cref="Passive"/>.
        /// </summary>
        public static void Granted(Ability ability, Unit carrier, int level)
        {
            if (!HasTechGate(ability, level)) return;
            Send(ability, carrier, BattleFactReason.PassiveGranted);
        }

        /// <summary>Пассивка СНЯТА с носителя. Отбор тот же, что у выдачи.</summary>
        public static void Revoked(Ability ability, Unit carrier, int level)
        {
            if (!HasTechGate(ability, level)) return;
            Send(ability, carrier, BattleFactReason.PassiveRevoked);
        }

        /// <summary>
        /// Пассивка сработала в ТОЧКЕ — носителя может уже не быть (посмертный взрыв). Отдельный путь
        /// нужен потому, что у мёртвого носителя netID снят с учёта и сообщение «про юнита» отсеялось бы
        /// гейтом канала (та же причина, что у FactAt в конструкторе пассивки).
        /// </summary>
        public static void ProcAt(Ability ability, Vector3 position)
        {
            if (!InterflowDebug.showPassiveFacts) return;
            if (ability == null) return;

            NetworkDataSync sync = NetworkDataSync.Instance;
            if (sync == null) return;

            sync.BattleFactAtPointSend(position, BattleFactReason.PassiveProc, ability.id);
        }

        /// <summary>
        /// Есть ли у умения требование технологии на этом уровне, то есть специализация ли это.
        /// Повторяет отбор из <see cref="Passive"/>: там он частный и приватный, здесь общий.
        /// </summary>
        static bool HasTechGate(Ability ability, int level)
        {
            if (ability == null) return false;
            if (ability.requiredTech == null || ability.requiredTech.Length == 0) return false;

            int i = Mathf.Clamp(level, 0, ability.requiredTech.Length - 1);
            return ability.requiredTech[i] != null &&
                   ability.requiredTech[i].data != null &&
                   ability.requiredTech[i].data.Length > 0;
        }

        /// <summary>
        /// Разовое ЧИСЛО боя по юниту без привязки к умению (рост урона, срез урона и подобное).
        /// Нужен местам вне конструктора пассивки: у них своего входа в канал фактов не было,
        /// а заводить второй такой же было бы дублированием (правило 5).
        /// </summary>
        public static void Number(Unit unit, BattleFactReason reason, float value, float before = 0f, float after = 0f)
            => Send(unit, reason, value, before, after);

        /// <summary>
        /// Общая отправка. Показ выключен, умения нет, носителя нет или сети ещё нет — молча выходим:
        /// показ проверочный, ронять из-за него боевой путь нельзя.
        /// </summary>
        static void Send(Ability ability, Unit carrier, BattleFactReason reason)
        {
            if (!InterflowDebug.showPassiveFacts) return;
            if (ability == null || carrier == null) return;

            NetworkDataSync sync = NetworkDataSync.Instance;
            if (sync == null) return;

            sync.UnitBattleFactSend(carrier, reason, ability.id);
        }

        /// <summary>Отправка произвольного числа по юниту. Гейты те же.</summary>
        static void Send(Unit unit, BattleFactReason reason, float value, float before = 0f, float after = 0f)
        {
            if (!InterflowDebug.showPassiveFacts) return;
            if (unit == null) return;

            NetworkDataSync sync = NetworkDataSync.Instance;
            if (sync == null) return;

            sync.UnitBattleFactSend(unit, reason, value, before, after);
        }
    }
}
