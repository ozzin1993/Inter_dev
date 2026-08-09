using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// ВИЗУАЛЬНЫЕ статусы юнита, присланные единым каналом синка статусов (2026-08-05:
    /// NetworkDataSync.UnitStatus — эффекторы от ЛЮБОГО источника, флаги вроде слепоты, VFX бафов
    /// скиллов): значок в панели состояний/шкале и VFX.
    /// Ровно презентация и ничего больше — ни урона, ни модификаторов статов, ни таймеров геймплея.
    ///
    /// Зачем отдельный носитель. В движке `Effector` — это геймплейный объект, который живёт в локальной
    /// симуляции каждого пира, а значок и VFX появляются лишь как следствие его присутствия в `unit.effectors`.
    /// По сети эффекторы не передаются, поэтому раньше был выбор: либо клиент накладывает настоящий эффектор
    /// у себя (и локально крутит его геймплей), либо не видит статуса вовсе. Решение Artsiom 2026-08-01 —
    /// строгое разделение: состояние живёт только на сервере, а клиенту приходит явное «покажи вот это»
    /// (тем же путём, каким ядро реплицирует стан). Здесь и хранится то, что пришло.
    ///
    /// Компонент добавляется по требованию и живёт до конца жизни юнита. Опустев (показывать нечего),
    /// он отписывается от тика, но СЕБЯ НЕ УНИЧТОЖАЕТ: `Destroy(this)` сносит компонент лишь в конце
    /// кадра, а `GetComponent` всё это время продолжает его возвращать — статус, пришедший в том же кадре, лёг бы
    /// в обречённый компонент и умер вместе с ним. Пустой компонент ничего не стоит: подписки нет, записей нет.
    /// Движение юнита серверное (позиции синхронизируются), поэтому замедление и прочие модификаторы
    /// статов клиенту применять не нужно — он и так получит уже изменённое движение.
    ///
    /// На выделенном сервере компонент не создаётся вовсе — показывать некому (гейт `Utils.Headless`,
    /// та же конвенция, что у хелсбара, иконки миникарты и аниматора в `Unit.Init`).
    /// </summary>
    public class SkillVisualStatus : MonoBehaviour
    {
        /// <summary>Одна показываемая запись. Ключ нужен, чтобы повтор продлевал, а не дублировал.</summary>
        class Entry
        {
            public int key;                 // ≥ 0 — id эффектора; < 0 — VFX бафа скилла (см. BuffKey)
            public Effector iconSource;     // источник значка; null — запись только с VFX
            public VFXReferencer vfxInstance;
            public float remaining;         // бесконечность — висит, пока юнит жив
        }

        readonly List<Entry> entries = new List<Entry>();
        // Флаги наших состояний без эффектора (слепота и т.п.) — приходят тем же каналом.
        readonly HashSet<UnitStatusFlag> flags = new HashSet<UnitStatusFlag>();
        Unit unit;
        bool subscribed;

        /// <summary>Ключ записи для VFX бафа: id скилла в отрицательной области, чтобы не пересечься с id эффекторов.</summary>
        public static int BuffKey(int abilityId) => -abilityId - 1;

        void Awake() => unit = GetComponent<Unit>();

        // ============================================================== ПОКАЗ ==

        /// <summary>Показать значок и VFX эффектора.</summary>
        /// <param name="durationOverride">Фактическая длительность наложения, присланная сервером.
        /// Значение ≤ 0 — брать из ассета. С 2026-08-02 числа задаёт умение, и один и тот же эффектор
        /// от разных умений живёт разное время — считать по ассету больше нельзя.</param>
        public static void ShowEffector(Unit target, Effector effector, float durationOverride = -1f)
        {
            if (Utils.Headless) return; // выделенному серверу показывать некому
            if (target == null || target.dead || effector == null) return;

            // Формула стакинга ровно та же, что у сервера в Effector.EffectorAdd: невидимость стакаться
            // не может. Иначе клиент решал бы про значок иначе, чем сервер, на том же эффекторе.
            bool stacks = effector.stacks && !effector.makeInvisible;

            // Значок в панели состояний штатно рисуется только у НЕстакающих эффекторов с иконкой —
            // у остальных показываем один VFX, без записи значка.
            Effector iconSource = (!stacks && effector.icon != null) ? effector : null;
            if (iconSource == null && effector.VFX == null) return;

            float duration;
            if (effector.permanent) duration = float.PositiveInfinity;
            else
            {
                duration = durationOverride > 0f ? durationOverride : effector.duration;
                // Тот же нижний предел, что и на сервере: короче двух тиков эффектор не живёт.
                if (duration < GameManager.tickRate * 2) duration = GameManager.tickRate * 2;
                // [2026-08-05 единый канал] Авторитетное снятие теперь приходит сообщением «статус снят»
                // (RemoveEffector, фикс §8.6). Локальный таймер — страховка с запасом на сетевую задержку,
                // чтобы значок не моргал между продлениями аур и не вис вечно при потере снятия.
                duration += 1f;
            }

            // unitCentre = false: ровно так же вешает VFX эффектора само ядро (`Effector.EffectorAdd`
            // зовёт `AddVFX(VFX, aboveHead)`), иначе у клиента и хоста визуал был бы на разной высоте.
            Get(target).Add(effector.id, iconSource, effector.VFX, duration, 0f, effector.aboveHead, false);
        }

        /// <summary>Показать VFX длящегося бафа. Значка у него нет — значок даёт отдельный эффектор-статус.</summary>
        public static void ShowBuffVfx(Unit target, int abilityId, VFXReferencer vfx, float duration, float auraRadius)
        {
            if (Utils.Headless) return; // выделенному серверу показывать некому
            if (target == null || target.dead || vfx == null || duration <= 0f) return;

            // unitCentre = true ВСЕГДА: все четыре старых буфа звали `AddVFX(vfx, false, true)`,
            // то есть визуал стоял на середине юнита, а не у ног. Привязывать это к наличию ауры нельзя:
            // у «Благословения Небес» и «Железного приговора» ауры нет, а визуал был по центру.
            Get(target).Add(BuffKey(abilityId), null, vfx, duration, auraRadius, false, true);
        }

        static SkillVisualStatus Get(Unit target)
        {
            SkillVisualStatus holder = target.GetComponent<SkillVisualStatus>();
            if (holder == null) holder = target.gameObject.AddComponent<SkillVisualStatus>();
            return holder;
        }

        void Add(int key, Effector iconSource, VFXReferencer vfx, float duration, float auraRadius, bool aboveHead, bool unitCentre)
        {
            if (unit == null) unit = GetComponent<Unit>();
            if (unit == null) return;

            Entry entry = Find(key);
            if (entry != null)
            {
                // Повтор того же статуса продлевает показ, но не плодит второй значок и второй VFX.
                entry.remaining = Mathf.Max(entry.remaining, duration);

                // Перекаст мог прийти с ДРУГОГО уровня скилла: размер ауры и источник значка берём свежие,
                // иначе сервер считал бы ауру по новому радиусу, а кольцо у клиента оставалось от первого каста.
                if (iconSource != null && entry.iconSource != iconSource)
                {
                    entry.iconSource = iconSource;
                    unit.OnStatusUpdate?.Invoke();
                }
                ApplyAuraScale(entry, auraRadius);
                return;
            }

            entry = new Entry { key = key, iconSource = iconSource, remaining = duration };

            if (vfx != null)
            {
                // VFX цепляется к юниту: когда юнит скрыт туманом войны, его дети скрыты вместе с ним —
                // отдельная проверка видимости не нужна, этим занимается сам механизм VFX-холдера.
                entry.vfxInstance = unit.AddVFX(vfx, aboveHead, unitCentre);
                ApplyAuraScale(entry, auraRadius);
            }

            entries.Add(entry);

            if (!subscribed && GameManager.instance != null)
            {
                GameManager.instance.Tick += OnTick;
                subscribed = true;
            }

            if (iconSource != null) unit.OnStatusUpdate?.Invoke(); // панель состояний перерисуется
        }

        Entry Find(int key)
        {
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].key == key) return entries[i];
            return null;
        }

        // ==================================================== СНЯТИЕ И ФЛАГИ ==

        /// <summary>
        /// Сервер сообщил: эффектора с этим id на юните больше нет (фикс §8.6: диспел и
        /// permanent-эффекторы теперь снимаются и у клиента, а не висят до конца матча).
        /// </summary>
        public static void RemoveEffector(Unit target, int effectorId)
        {
            if (Utils.Headless || target == null) return;

            SkillVisualStatus holder = target.GetComponent<SkillVisualStatus>();
            if (holder == null) return;

            Entry entry = holder.Find(effectorId);
            if (entry == null) return;

            bool hadIcon = entry.iconSource != null;
            holder.DestroyVfx(entry);
            holder.entries.Remove(entry);

            if (hadIcon && holder.unit != null) holder.unit.OnStatusUpdate?.Invoke();
            if (holder.entries.Count == 0) holder.Cleanup();
        }

        /// <summary>Сервер сообщил: флаг состояния без эффектора (слепота и т.п.) включён/выключен.</summary>
        public static void SetFlag(Unit target, UnitStatusFlag flag, bool state)
        {
            if (Utils.Headless || target == null || target.dead) return;

            if (state)
            {
                SkillVisualStatus holder = Get(target);
                if (holder.flags.Add(flag) && holder.unit != null) holder.unit.OnStatusUpdate?.Invoke();
            }
            else
            {
                SkillVisualStatus holder = target.GetComponent<SkillVisualStatus>();
                if (holder != null && holder.flags.Remove(flag) && holder.unit != null) holder.unit.OnStatusUpdate?.Invoke();
            }
        }

        /// <summary>Активен ли присланный сервером флаг (чтение для перечислителя значков).</summary>
        public bool HasFlag(UnitStatusFlag flag) => flags.Contains(flag);

        /// <summary>Растянуть визуал под радиус ауры. Ноль — ауры нет, визуал остаётся авторского размера.</summary>
        void ApplyAuraScale(Entry e, float auraRadius)
        {
            if (e.vfxInstance == null || auraRadius <= 0f) return;

            e.vfxInstance.transform.SetGlobalScale(new Vector3(
                unit.unitRadius * 2f + auraRadius, unit.unitHeight, unit.unitRadius * 2f + auraRadius));
        }

        // ================================================================ ТИК ==

        void OnTick()
        {
            if (unit == null || unit.dead) { Cleanup(); return; }
            if (GameManager.instance == null) return;

            float dt = GameManager.instance.currentDeltaTime;
            bool iconRemoved = false;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry e = entries[i];
                if (float.IsInfinity(e.remaining)) continue;

                e.remaining -= dt;
                if (e.remaining > 0f) continue;

                if (e.iconSource != null) iconRemoved = true;
                DestroyVfx(e);
                entries.RemoveAt(i);
            }

            if (iconRemoved && unit != null) unit.OnStatusUpdate?.Invoke();
            if (entries.Count == 0) Cleanup();
        }

        // ================================================== ЧТЕНИЕ ДЛЯ ПАНЕЛИ ==

        /// <summary>Сколько значков сейчас показывать.</summary>
        public int IconCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < entries.Count; i++) if (entries[i].iconSource != null) count++;
                return count;
            }
        }

        /// <summary>Значок по порядковому номеру: id эффектора и картинка. false — такого значка нет.</summary>
        public bool TryGetIcon(int index, out int effectorId, out Texture2D icon)
        {
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].iconSource == null) continue;
                if (count == index)
                {
                    effectorId = entries[i].iconSource.id;
                    icon = entries[i].iconSource.icon;
                    return true;
                }
                count++;
            }

            effectorId = 0;
            icon = null;
            return false;
        }

        // ============================================================= УБОРКА ==

        void DestroyVfx(Entry e)
        {
            // Снимаем прямым Destroy: штатный Unit.RemoveVFX падает, если у юнита не инициализирован vfxHolder.
            if (e.vfxInstance != null) Destroy(e.vfxInstance.gameObject);
            e.vfxInstance = null;
        }

        /// <summary>
        /// Показывать больше нечего: сносим VFX и отписываемся от тика. Компонент остаётся на юните пустым:
        /// `Destroy(this)` выполнился бы только в конце кадра, а `GetComponent` до того момента продолжает
        /// возвращать обречённый экземпляр — статус, пришедший в том же кадре, бесследно умирал бы в `OnDestroy`.
        /// </summary>
        void Cleanup()
        {
            for (int i = 0; i < entries.Count; i++) DestroyVfx(entries[i]);
            entries.Clear();

            if (subscribed && GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            subscribed = false;
        }

        void OnDestroy()
        {
            for (int i = 0; i < entries.Count; i++) DestroyVfx(entries[i]);
            entries.Clear();

            if (subscribed && GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            subscribed = false;
        }
    }
}
