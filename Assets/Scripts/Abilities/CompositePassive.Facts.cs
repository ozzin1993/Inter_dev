using UnityEngine;

namespace StrategyCore
{
    // ====== КОНСТРУКТОР ПАССИВКИ: ПОКАЗ СРАБАТЫВАНИЙ (правило 22 — партиал по фиче) ==
    // [Interflow 2026-09-09 passive-facts] До этого шага пассивное умение не имело НИ ОДНОГО
    // визуального отображения: ни в восьми блоках свойств, ни в четырёх реакциях не было полей
    // под значок, зрительный эффект или звук, а срабатывания уходили только в консоль.
    // Проверять их в бою было нечем.
    //
    // Своего механизма показа здесь не заводится (правило 2): факты едут УЖЕ СУЩЕСТВУЮЩИМ каналом
    // разовых фактов боя (§15 схемы, решения Artsiom Р1–Р7 от 07.09.2026) — тем же, которым идут
    // «Мимо», «Щит» и числа урона. Добавлены только номера причин в BattleFactReason и одно
    // сообщение «факт в точке» для реакции «носитель погиб» (носителя к этому моменту уже нет).
    //
    // Разделение прежнее: боевой код поднимает ФАКТ и ПРИЧИНУ и про слова, цвета и надписи
    // не знает ничего (решение Р6) — их выбирает клиентский презентер по ассету настроек.
    //
    // Всё серверное: гейты внутри канала (ServerCanSend / StillRegistered). На клиенте вызовы
    // отсюда становятся пустыми — сообщение не уходит и локально факт не поднимается.
    //
    // Выключатель показа — InterflowDebug.showPassiveFacts (отдельный от уровня логов: смотреть
    // надписи обычно надо без потока строк в консоли, и наоборот).
    public partial class CompositePassive
    {
        // ============================================== НОМЕРА БЛОКОВ ==
        // Номер блока едет ЧИСЛОМ факта: он же порядковый номер заголовка в ассете
        // («Блок 1 — изменение характеристик» и далее). Клиент по этому числу берёт название
        // блока из ассета настроек презентации — слова живут там, а не здесь (правило 3).

        const int BlockStats = 1;
        const int BlockControlImmunity = 2;
        const int BlockResistances = 3;
        const int BlockInvisibility = 4;
        const int BlockArmorPierce = 5;
        const int BlockSplash = 6;
        const int BlockAttackEffectors = 7;
        const int BlockAura = 8;

        // ============================================== ОТПРАВКА ==

        /// <summary>
        /// Разовый факт по юниту. Показ выключен или сети ещё нет — молча выходим: показ
        /// проверочный, ронять из-за него боевой путь нельзя.
        /// </summary>
        static void Fact(Unit unit, BattleFactReason reason, float value, float before = 0f, float after = 0f)
        {
            if (!InterflowDebug.showPassiveFacts) return;
            if (unit == null) return;

            NetworkDataSync sync = NetworkDataSync.Instance;
            if (sync == null) return;

            sync.UnitBattleFactSend(unit, reason, value, before, after);
        }

        /// <summary>
        /// Разовый факт в ТОЧКЕ. Нужен там, где носителя уже нет — реакция «носитель погиб»:
        /// её netID снят с учёта смертью, и сообщение «про юнита» отсеялось бы гейтом канала.
        /// </summary>
        static void FactAt(Vector3 position, BattleFactReason reason, float value, float before = 0f, float after = 0f)
        {
            if (!InterflowDebug.showPassiveFacts) return;

            NetworkDataSync sync = NetworkDataSync.Instance;
            if (sync == null) return;

            sync.BattleFactAtPointSend(position, reason, value, before, after);
        }

        // ============================================== БЛОКИ СВОЙСТВ ==

        /// <summary>
        /// Надписи по блокам свойств: по одной на КАЖДЫЙ фактически выданный блок.
        /// Читаем Carrier, а не галки ассета: галку могли выключить между выдачей и снятием,
        /// и по галкам надпись разошлась бы с тем, что на юните на самом деле лежит.
        ///
        /// При снятии зовётся ДО методов Remove*: они гасят флаги в Carrier, и после них
        /// перечислять было бы уже нечего.
        /// </summary>
        void FactBlocks(Unit unit, Carrier c, BattleFactReason reason)
        {
            if (!InterflowDebug.showPassiveFacts) return;
            if (unit == null || c == null) return;

            if (c.stats) Fact(unit, reason, BlockStats);
            if (c.controlImmunity) Fact(unit, reason, BlockControlImmunity);
            if (c.resistances) Fact(unit, reason, BlockResistances);
            if (c.invisibility) Fact(unit, reason, BlockInvisibility);
            if (c.armorPierceFraction > 0f) Fact(unit, reason, BlockArmorPierce);
            if (c.splash != null) Fact(unit, reason, BlockSplash);
            if (c.originalAttackEffectors != null) Fact(unit, reason, BlockAttackEffectors);
            if (c.aura) Fact(unit, reason, BlockAura);
        }
    }
}
