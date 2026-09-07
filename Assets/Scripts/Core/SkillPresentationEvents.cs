using System;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// [Interflow 2026-08-06] Хаб событий ПРЕЗЕНТАЦИИ УМЕНИЙ. Решение Artsiom: событийная инверсия —
    /// сетевой слой и симуляция про презентацию не знают ничего и лишь поднимают здесь событие-ФАКТ.
    /// Рисует единственный подписчик — клиентский презентер (SkillPresenter, сборка Interflow.Client).
    ///
    /// Зависимость односторонняя и держится границей сборок, а не дисциплиной: Interflow.Client ссылается
    /// на Interflow.Game, обратной ссылки нет. На выделенном сервере клиентской сборки нет вовсе —
    /// подписчиков ноль, Invoke уходит в никуда, кода презентации в билде нет.
    ///
    /// Прецедент событийного хаба в проекте — MatchManager.DeathEvents (OnUnitDeathServer).
    /// ВАЖНО: события статические — подписчик обязан отписываться (OnDisable/OnDestroy),
    /// иначе останется висячий обработчик уничтоженного объекта.
    /// </summary>
    public static class SkillPresentationEvents
    {
        // ============================== СРАБАТЫВАНИЕ УМЕНИЯ ==============================
        // ОДНО событие на любое срабатывание: автокаст, каст с кнопки, «умный выбор», прок пассивки.
        // Особых случаев нет (решение Artsiom 2026-08-06: отдельное SkillProc отменено — после перевода
        // пассивок в конструктор их прок и есть срабатывание умения, два сообщения описывали бы один факт).

        /// <summary>
        /// Умение сработало: (кастер, id умения, уровень, цель наведения, точка приложения).
        /// Кастер и цель могут быть null — умение без кастера или без конкретной цели.
        /// </summary>
        public static event Action<Unit, int, int, Unit, Vector3> SkillFired;

        /// <summary>Поднять факт «умение сработало». Зовётся сервером (локально) и приёмником RPC (у клиента).</summary>
        public static void RaiseSkillFired(Unit caster, int abilityID, int level, Unit aimUnit, Vector3 aimPoint)
            => SkillFired?.Invoke(caster, abilityID, level, aimUnit, aimPoint);

        // ============================== ЖИЗНЬ ЮНИТА ==============================
        // Нужны презентеру, чтобы заводить и снимать постоянный круг радиуса (ауры).
        // Серверное MatchManager.OnUnitSpawned для этого не годится: клиенту оно не приходит,
        // а юниты у него появляются репликацией, загрузкой сцены и данными сохранения.

        /// <summary>
        /// Юнит полностью инициализирован на ЭТОМ пире: умения приведены в рабочее состояние,
        /// уровни и блокировки проставлены. Раньше этого момента читать умения юнита нельзя.
        /// </summary>
        public static event Action<Unit> UnitReady;

        /// <summary>Юнит убран с этого пира (снятие netID). Зеркало UnitReady.</summary>
        public static event Action<Unit> UnitGone;

        /// <summary>Поднять факт «юнит готов». Зовётся в конце инициализации юнита.</summary>
        public static void RaiseUnitReady(Unit unit) => UnitReady?.Invoke(unit);

        /// <summary>Поднять факт «юнит убран». Зовётся при снятии netID.</summary>
        public static void RaiseUnitGone(Unit unit) => UnitGone?.Invoke(unit);

        // ============================== ЗАМАХ ==============================
        // Начало и конец замаха. Нужны, чтобы область действия появлялась ДО срабатывания.
        // Своих сообщений для этого не заводим: факт поднимается рядом с ОТПРАВКОЙ штатного сообщения
        // (для хоста) и в его ПРИЁМНИКЕ (для клиента) — сообщение идёт SendTo.NotServer и до хоста не доходит.

        /// <summary>Юнит начал каст: (кастер, id умения, цель, точка). Цель и точка могут быть пустыми.</summary>
        public static event Action<Unit, int, Unit, Vector3> CastStarted;

        /// <summary>Каст юнита прерван или завершён ядром: (кастер).</summary>
        public static event Action<Unit> CastStopped;

        /// <summary>Поднять факт «каст начался». Зовётся приёмником сообщения о начале каста.</summary>
        public static void RaiseCastStarted(Unit caster, int abilityID, Unit targetUnit, Vector3 targetPoint)
            => CastStarted?.Invoke(caster, abilityID, targetUnit, targetPoint);

        /// <summary>Поднять факт «каст прерван». Зовётся приёмником сообщения о прерывании.</summary>
        public static void RaiseCastStopped(Unit caster) => CastStopped?.Invoke(caster);

        // ============================== ПОГЛОЩАЮЩИЙ ЩИТ ==============================
        // Величина щита живёт на сервере (AbsorbShield); факт нужен клиентскому сегменту
        // на полоске здоровья (ShieldBarDisplay, сборка Interflow.Client).

        /// <summary>Объём поглощающего щита юнита изменился: (носитель, объём; 0 — щит снят).</summary>
        public static event Action<Unit, float> ShieldChanged;

        /// <summary>Поднять факт «щит изменился». Зовётся сервером (локально) и приёмником RPC (у клиента).</summary>
        public static void RaiseShieldChanged(Unit unit, float amount)
            => ShieldChanged?.Invoke(unit, amount);

        // ============================== РАЗОВЫЙ ФАКТ БОЯ ==============================
        // ОДНО событие на все шесть случаев §15 схемы (решения Artsiom Р1–Р7 от 07.09.2026): удар не достиг
        // цели, щит поглотил, неуязвим, состояние отбито иммунитетом, состояние отбито сопротивлением,
        // нанесён урон. Приёмник поднимает только ФАКТ и ПРИЧИНУ: про надписи, слова и цвета боевой код
        // не знает ничего (решение Р6), их выбирает клиентский презентер по настройкам.
        // Путь тот же, что у щита: подъём на сервере локально + своё сообщение сети клиентам.

        /// <summary>
        /// По юниту произошло разовое событие боя: (юнит, причина, число).
        /// Число осмысленно только у <see cref="BattleFactReason.DamageDealt"/> — это фактически снятое
        /// здоровье; у остальных причин 0.
        /// </summary>
        public static event Action<Unit, BattleFactReason, float> BattleFact;

        /// <summary>Поднять разовый факт боя. Зовётся сервером (локально) и приёмником сообщения (у клиента).</summary>
        public static void RaiseBattleFact(Unit unit, BattleFactReason reason, float value)
            => BattleFact?.Invoke(unit, reason, value);

        // ============================== ЗОНЫ НА ЗЕМЛЕ ==============================

        /// <summary>
        /// Зона на земле появилась: (id зоны, id умения-источника, уровень, позиция, носитель).
        /// Носитель не null — «аура на время» (блок Б7): копию зоны презентер вешает на юнит, и она идёт за ним.
        /// </summary>
        public static event Action<int, int, int, Vector3, Unit> ZoneSpawned;

        /// <summary>Зона на земле закончила жизнь: (id зоны).</summary>
        public static event Action<int> ZoneDespawned;

        /// <summary>Поднять факт «зона появилась». Зовётся приёмником RPC (NetworkDataSync.UnitStatus).</summary>
        public static void RaiseZoneSpawned(int zoneId, int abilityID, int level, Vector3 position, Unit carrier)
            => ZoneSpawned?.Invoke(zoneId, abilityID, level, position, carrier);

        /// <summary>Поднять факт «зона исчезла». Зовётся приёмником RPC (NetworkDataSync.UnitStatus).</summary>
        public static void RaiseZoneDespawned(int zoneId)
            => ZoneDespawned?.Invoke(zoneId);
    }

    /// <summary>
    /// Причина разового факта боя по юниту — шесть случаев набора Р1 (решение Artsiom 07.09.2026).
    /// Объявлено здесь, а не отдельным файлом: вне события <see cref="SkillPresentationEvents.BattleFact"/>
    /// перечисление смысла не имеет (правило 5 — меньше точек входа).
    ///
    /// ВАЖНО: нумерация СПЛОШНАЯ от нуля, и по сети причина едет числом (см. отправку факта боя в
    /// <c>NetworkDataSync.UnitStatus.cs</c>). Порядок значений менять нельзя — только дописывать в конец,
    /// иначе сервер и клиент разойдутся в толковании номера.
    /// </summary>
    public enum BattleFactReason
    {
        /// <summary>Удар не достиг цели. Одна причина на промах бьющего и уход жертвы (решение Р4).</summary>
        HitMissed = 0,

        /// <summary>Поглощающий щит принял на себя часть или весь урон этого удара.</summary>
        ShieldAbsorbed = 1,

        /// <summary>Жертва неуязвима — урон отбит целиком.</summary>
        Invulnerable = 2,

        /// <summary>Наложение состояния отбито иммунитетом к контролю.</summary>
        StatusImmune = 3,

        /// <summary>Наложение состояния отбито сопротивлением 100 % и выше.</summary>
        StatusResisted = 4,

        /// <summary>По юниту прошёл урон. Величина — в числе факта.</summary>
        DamageDealt = 5
    }
}
