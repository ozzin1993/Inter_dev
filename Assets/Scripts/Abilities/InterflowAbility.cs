using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

namespace StrategyCore
{
    /// <summary>
    /// Общая база всех наших способностей. Собирает в одном месте бойлерплейт, который до этого
    /// был скопирован по 30+ файлам: выборка значения по уровню, серверный гейт, ForceSync,
    /// идемпотентная регистрация боевых колбэков, пер-юнит состояние с чисткой мёртвых,
    /// подписка на штатный тик, презентация (сокеты, VFX с туманом войны, звук) и фильтр категорий.
    ///
    /// Сама по себе ничего не делает и Use() не определяет — только protected-хелперы
    /// и переопределяемый сброс рантайм-состояния. Наследники: CompositeSkill и мигрированные кирпичи.
    ///
    /// ВАЖНО про ScriptableObject: ассет живёт между Play-сессиями редактора, поэтому любое
    /// пер-юнит состояние обязано чиститься в <see cref="ResetRuntimeState"/> (зовётся из Init()).
    /// </summary>
    public abstract class InterflowAbility : Ability
    {
        // ================================================================= ЗНАЧЕНИЯ ПО УРОВНЯМ ==
        // В кодовой базе исторически сложились ДВЕ разные семантики выборки из массива по уровню.
        // Обе сохранены осознанно: при миграции класс обязан остаться на своей, иначе тихо поедет баланс.

        /// <summary>
        /// Значение по уровню с КЛАМПОМ к последнему элементу: массив короче уровня — берётся последний.
        /// Семантика по умолчанию для новых способностей (так работали EffectorArea, Counterattack и др.).
        /// Пустой/нулевой массив — fallback.
        /// </summary>
        public static float LevelValue(float[] arr, int level, float fallback = 0f)
        {
            if (arr == null || arr.Length == 0) return fallback;
            if (level < 0) level = 0;
            if (level >= arr.Length) level = arr.Length - 1;

            return arr[level];
        }

        /// <summary>
        /// Значение по уровню со семантикой «КОРОТКИЙ МАССИВ → 0»: если элемента для этого уровня нет,
        /// возвращается 0 (а не последний элемент). Так вели себя FlameCloakActive / HeavensBlessingActive /
        /// IronVerdictActive / SacrificialPyreActive — при миграции их ассетов поведение обязано совпасть 1:1.
        /// Отрицательный уровень тоже даёт 0 (в исходных классах такой вход падал с IndexOutOfRange).
        /// </summary>
        public static float LevelValueOrZero(float[] arr, int level)
        {
            if (arr == null || level < 0 || arr.Length <= level) return 0f;

            return arr[level];
        }

        // ========================================================================== СТОИМОСТЬ В ХП ==

        /// <summary>
        /// Списать здоровье «в стоимость» (не урон: броня и тип урона не участвуют) и, если списание
        /// оказалось смертельным, честно провести смерть.
        ///
        /// Зачем: штатный <see cref="Unit.ChangeHP"/> НЕ убивает — он зажимает здоровье в ноль и возвращает
        /// true (его собственный комментарий отсылает к GetDamage). Без этого вызова кастер с выключенным
        /// «Запретить каст, если стоимость добьёт» оставался ходячим с нулём ХП: бил, кастовал, регенился.
        ///
        /// Награды за смерть зависят от того, чьё это списание:
        ///  — собственная стоимость каста или самосожжение на себе (`killer` пуст или равен жертве) —
        ///    без наград кому-либо: `Die(-1, null, false)`, та же форма жертвенной смерти, что в
        ///    CorruptionBurstActive, MatchManager.CallToArms и LifetimeUnit;
        ///  — списание от ЧУЖОГО юнита (урон-во-времени вражеского бафа) — убийство засчитывается ему,
        ///    как при обычном уроне (решение Artsiom 2026-08-02).
        /// Только сервер (правило 6).
        /// </summary>
        /// <param name="unit">С кого списываем.</param>
        /// <param name="amount">Сколько здоровья снять (положительное число).</param>
        /// <param name="killer">Кто списывает. Пусто или сама жертва — смерть без наград.</param>
        public static void PayHealth(Unit unit, float amount, Unit killer = null)
        {
            if (IsClientPeer) return;
            if (unit == null || unit.dead || amount <= 0f) return;

            if (!unit.ChangeHP(-amount)) return;

            bool rewarded = killer != null && killer != unit;
            unit.Die(rewarded ? killer.owner : -1, rewarded ? killer : null, rewarded);
        }

        // ================================================================================ СЕТЬ ==

        /// <summary>Текущий пир — клиент (не сервер и не хост). Всё, что меняет состояние мира, за этим гейтом.</summary>
        public static bool IsClientPeer => NetworkConnectionHandler.isClient;

        /// <summary>Текущий пир — сервер или хост.</summary>
        public static bool IsServerPeer => !NetworkConnectionHandler.isClient;

        /// <summary>
        /// Попросить немедленную синхронизацию состояния клиентам (батч ХП и пр.).
        /// Зовётся ОДИН раз после пачки изменений, а не на каждую цель. На клиенте — ничего не делает.
        /// </summary>
        public static void RequestForceSync()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            if (NetworkDataSync.Instance == null) return;

            NetworkDataSync.Instance.ForceSync();
        }

        // =================================================================== БОЕВЫЕ КОЛБЭКИ ==
        // Штатные списки Unit (OnBeforeGetDamageCallbacks / OnDamageDealModifyCallbacks / OnAfterDamageDealCallbacks)
        // помечают запись парой (Ability, Level) — по ней же и снимают. Дженерик по типу структуры невозможен
        // без правки ядра (у структур нет общего интерфейса), поэтому — перегрузки на два используемых типа.

        /// <summary>Добавить колбэк изменения урона. Идемпотентно: пара (ability, level) не дублируется.</summary>
        public static void CallbackAdd(List<DamageModifyCallback> list, Ability ability, int level,
                                          Func<Unit, int, float, bool, float> handler)
        {
            if (list == null || ability == null || handler == null) return;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Ability == ability && list[i].Level == level) return; // уже стоит

            list.Add(new DamageModifyCallback { Callback = handler, Ability = ability, Level = level });
        }

        /// <summary>Снять колбэк изменения урона по паре (ability, level). true — запись была найдена.</summary>
        public static bool CallbackRemove(List<DamageModifyCallback> list, Ability ability, int level)
        {
            if (list == null || ability == null) return false;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Ability == ability && list[i].Level == level) { list.RemoveAt(i); return true; }

            return false;
        }

        /// <summary>Добавить колбэк «после нанесения урона». Идемпотентно по паре (ability, level).</summary>
        public static void CallbackAdd(List<AfterDamageDealCallback> list, Ability ability, int level,
                                          Action<Unit, Vector3, Effector[], float, bool, DamageType, Unit, Projectile, int, int> handler)
        {
            if (list == null || ability == null || handler == null) return;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Ability == ability && list[i].Level == level) return;

            list.Add(new AfterDamageDealCallback { Callback = handler, Ability = ability, Level = level });
        }

        /// <summary>Снять колбэк «после нанесения урона» по паре (ability, level). true — запись была найдена.</summary>
        public static bool CallbackRemove(List<AfterDamageDealCallback> list, Ability ability, int level)
        {
            if (list == null || ability == null) return false;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Ability == ability && list[i].Level == level) { list.RemoveAt(i); return true; }

            return false;
        }

        // ============================================================= ПЕР-ЮНИТ СОСТОЯНИЕ ==

        /// <summary>
        /// Словарь «юнит → состояние» для способностей-SO (один ассет обслуживает всех носителей).
        /// Умеет чистить мёртвых/уничтоженных носителей: Unit.Die не зовёт Lock(), поэтому без чистки
        /// записи копились бы до конца матча, а подписки на тик никогда не снимались.
        ///
        /// Сравнение с null делается через приведение к object: у уничтоженного Unity-объекта оператор ==
        /// врёт (объект «как бы null»), но ссылка остаётся валидным ключом словаря — именно по ней и удаляем.
        /// </summary>
        protected sealed class UnitStateMap<T>
        {
            readonly Dictionary<Unit, T> map = new Dictionary<Unit, T>();
            readonly List<Unit> buffer = new List<Unit>();

            public int Count => map.Count;

            public bool Contains(Unit unit) => (object)unit != null && map.ContainsKey(unit);

            public bool TryGet(Unit unit, out T value)
            {
                if ((object)unit != null) return map.TryGetValue(unit, out value);
                value = default;
                return false;
            }

            public void Set(Unit unit, T value)
            {
                if ((object)unit == null) return;
                map[unit] = value;
            }

            /// <summary>Удалить запись. Работает и для уже уничтоженных носителей (ключ — ссылка).</summary>
            public bool Remove(Unit unit) => (object)unit != null && map.Remove(unit);

            public void Clear()
            {
                map.Clear();
                buffer.Clear();
            }

            /// <summary>Скопировать ключи в переданный список — словарь нельзя менять во время обхода.</summary>
            public void CopyKeysTo(List<Unit> into)
            {
                if (into == null) return;
                into.Clear();
                foreach (Unit u in map.Keys) into.Add(u);
            }

            /// <summary>
            /// Убрать мёртвых и уничтоженных носителей. onRemoved (опц.) зовётся до удаления —
            /// туда выносят снятие подписок/колбэков конкретной способности. Возвращает число удалённых.
            /// ВНИМАНИЕ: в onRemoved юнит может быть УЖЕ УНИЧТОЖЕН (оператор == вернёт true для null) —
            /// обработчик обязан это проверять, обращение к полям такого объекта кинет MissingReferenceException.
            /// </summary>
            public int PruneDead(Action<Unit, T> onRemoved = null)
            {
                if (map.Count == 0) return 0;

                buffer.Clear();
                foreach (Unit u in map.Keys)
                    if (u == null || u.dead) buffer.Add(u); // здесь нужен именно Unity-оператор ==

                for (int i = 0; i < buffer.Count; i++)
                {
                    Unit u = buffer[i];
                    if (onRemoved != null && map.TryGetValue(u, out T value)) onRemoved(u, value);
                    map.Remove(u);
                }

                int removed = buffer.Count;
                buffer.Clear();
                return removed;
            }
        }

        // ============================================================================== ТИК ==

        /// <summary>
        /// Подписка на штатный GameManager.Tick (0.1 с) с защитой от двойной подписки.
        /// Подписываемся, когда появился первый носитель, и отписываемся, когда последний ушёл, —
        /// иначе ассет тикает весь матч впустую.
        /// </summary>
        protected sealed class TickHook
        {
            readonly Action handler;
            bool wired;

            public TickHook(Action handler) { this.handler = handler; }

            public bool Wired => wired;

            public void Wire()
            {
                if (handler == null || GameManager.Instance == null) return;

                // Сначала снимаем, потом подписываем. Вычитание неподписанного делегата в C# — no-op,
                // зато так подписка не задвоится и, главное, восстановится в НОВОМ матче: ассет живёт
                // между Play-сессиями, и флаг wired мог остаться true от прошлой (тик бы молча не заработал).
                GameManager.Instance.Tick -= handler;
                GameManager.Instance.Tick += handler;
                wired = true;
            }

            /// <summary>
            /// Отписаться. Флаг сбрасывается даже если GameManager уже уничтожен (конец матча) —
            /// иначе следующий матч решил бы, что подписка ещё жива, и тик не заработал бы.
            /// </summary>
            public void Unwire()
            {
                if (!wired) return;
                if (GameManager.Instance != null) GameManager.Instance.Tick -= handler;
                wired = false;
            }
        }

        // ==================================================================== ЖИЗНЕННЫЙ ЦИКЛ ==

        /// <summary>
        /// Зовётся штатно из GameManager при старте матча. Здесь же сбрасывается рантайм-состояние:
        /// ассет — ScriptableObject, он переживает Play-сессию редактора вместе со всем, что в нём накопилось.
        /// </summary>
        public override void Init()
        {
            base.Init();
            ResetRuntimeState();
        }

        /// <summary>
        /// Очистка пер-юнит состояния между матчами. Наследник обязан переопределить, если хранит
        /// словари носителей, подписки на тик или зарегистрированные колбэки.
        /// </summary>
        protected virtual void ResetRuntimeState() { }

        // =========================================================== ПУБЛИКАЦИЯ ФАКТОВ ==
        // Сервер сообщает о срабатывании умения; рисует единственный подписчик — клиентский презентер.
        // Само умение про визуал не знает: ни VFX, ни анимаций отсюда не запускается (правило 6).

        /// <summary>
        /// Сервер: опубликовать факт «умение сработало». Локальный подъём события нужен ХОСТУ —
        /// сообщение с SendTo.NotServer до него не доходит; на выделенном сервере подписчиков нет
        /// и подъём уходит в никуда. Дублей не возникает: каждый пир получает факт ровно один раз.
        /// </summary>
        public static void EmitSkillFired(Unit caster, Ability ability, int level, Unit aimUnit, Vector3 aimPoint)
        {
            if (ability == null) return;
            if (IsClientPeer) return;   // публикует только сервер (правило 6)

            SkillPresentationEvents.RaiseSkillFired(caster, ability.id, level, aimUnit, aimPoint);

            if (NetworkDataSync.Instance != null)
                NetworkDataSync.Instance.SkillFiredSend(caster, ability.id, level, aimUnit, aimPoint);
        }

        // ====================================================================== ПРЕЗЕНТАЦИЯ ==

        // Подъём VFX над землёй, чтобы плоские эффекты зон не мерцали сквозь террейн.
        // Значение перенесено из EffectorArea — единственного места, где FoW-гейт VFX уже был сделан правильно.
        const float GroundVfxLift = 0.1f;

        /// <summary>
        /// Точка привязки на модели кастера. Нет компонента CharacterSockets или сокет не заполнен —
        /// возвращается сам transform юнита. null только если юнита нет.
        /// </summary>
        public static Transform ResolveSocket(Unit unit, SkillSocketType socket)
        {
            if (unit == null) return null;

            // Ищем и в детях: точки привязки естественно живут под костями модели, а не на корне юнита.
            CharacterSockets sockets = unit.GetComponentInChildren<CharacterSockets>(true);
            return sockets != null ? sockets.GetSocket(socket) : unit.transform;
        }

        /// <summary>Мировая позиция сокета со смещением в ЕГО локальных осях (смещение «чуть перед ладонью»).</summary>
        public static Vector3 SocketPosition(Unit unit, SkillSocketType socket, Vector3 localOffset)
        {
            Transform point = ResolveSocket(unit, socket);
            if (point == null) return unit != null ? unit.transform.position : Vector3.zero;

            return localOffset == Vector3.zero ? point.position : point.TransformPoint(localOffset);
        }

        /// <summary>
        /// VFX из сокета кастера. Зовётся на ВСЕХ пирах (до серверного гейта) — иначе клиент не увидит эффект:
        /// VFX по сети не реплицируются, каждый пир создаёт свой. Урок FlameCloak 2026-06-21.
        /// </summary>
        public static void PlaySocketVFX(Unit unit, SkillSocketType socket, Vector3 localOffset,
                                            VFXReferencer vfx, float lifetime)
        {
            if (unit == null || vfx == null || lifetime <= 0f) return;

            Transform point = ResolveSocket(unit, socket);
            if (point == null) return;
            if (!VisibleForLocalViewer(point.position)) return;

            Vector3 spawnAt = localOffset == Vector3.zero ? point.position : point.TransformPoint(localOffset);
            VFXReferencer instance = Instantiate(vfx, spawnAt, point.rotation);
            Destroy(instance.gameObject, lifetime);
        }

        /// <summary>
        /// VFX в точке на земле (зона, место попадания). Зовётся на всех пирах, гейтится туманом войны
        /// по клетке САМОЙ ТОЧКИ, а не кастера: зона может лежать в 10 метрах, и видимость у них разная.
        /// </summary>
        public static void PlayPointVFX(Vector3 position, VFXReferencer vfx, float lifetime)
        {
            if (vfx == null || lifetime <= 0f) return;
            if (!VisibleForLocalViewer(position)) return;

            VFXReferencer instance = Instantiate(vfx, position + new Vector3(0f, GroundVfxLift, 0f), Quaternion.identity);
            Destroy(instance.gameObject, lifetime);
        }

        /// <summary>
        /// Видна ли точка локальному зрителю. На выделенном сервере тумана и слотов нет —
        /// возвращается false, и презентация просто не создаётся (состояние мира от этого не зависит).
        /// </summary>
        public static bool VisibleForLocalViewer(Vector3 position)
        {
            if (FogOfWar.Instance == null || SlotManager.Instance == null) return false;

            return FogOfWar.Instance.IsVisible(FogOfWar.GetCellByPosition(position), SlotManager.Instance.currentTeam);
        }

        /// <summary>
        /// Звук через хаб презентации (ADR-005): напрямую SoundFXManager из игровой сборки недоступен.
        /// На сервере хаб пуст — вызов просто ничего не делает.
        /// </summary>
        public static void PlaySound(AudioClip clip, Transform at, float volume)
        {
            if (clip == null || volume <= 0f) return;

            if (at != null) Presentation.Audio?.PlaySoundClip(clip, at, volume, false);
            else Presentation.Audio?.PlaySoundClip(clip, volume, false);
        }

        // ========================================================================== ФИЛЬТРЫ ==

        /// <summary>
        /// Проходит ли юнит по фильтру боевых ролей. Пустой список — фильтра нет, проходят все
        /// (та же семантика, что в CorruptionBurstActive / SacrificialPyreActive / SoulHarvest).
        /// </summary>
        public static bool CategoryAllowed(Unit unit, Unit.UnitCategory[] categories)
        {
            if (categories == null || categories.Length == 0) return true;
            if (unit == null) return false;

            for (int i = 0; i < categories.Length; i++)
                if (categories[i] == unit.unitCategory) return true;

            return false;
        }
    }
}
