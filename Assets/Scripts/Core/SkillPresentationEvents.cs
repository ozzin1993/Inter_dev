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

        // ============================== ЗОНЫ НА ЗЕМЛЕ ==============================

        /// <summary>Зона на земле появилась: (id зоны, id умения-источника, уровень, позиция).</summary>
        public static event Action<int, int, int, Vector3> ZoneSpawned;

        /// <summary>Зона на земле закончила жизнь: (id зоны).</summary>
        public static event Action<int> ZoneDespawned;

        /// <summary>Поднять факт «зона появилась». Зовётся приёмником RPC (NetworkDataSync.UnitStatus).</summary>
        public static void RaiseZoneSpawned(int zoneId, int abilityID, int level, Vector3 position)
            => ZoneSpawned?.Invoke(zoneId, abilityID, level, position);

        /// <summary>Поднять факт «зона исчезла». Зовётся приёмником RPC (NetworkDataSync.UnitStatus).</summary>
        public static void RaiseZoneDespawned(int zoneId)
            => ZoneDespawned?.Invoke(zoneId);
    }
}
