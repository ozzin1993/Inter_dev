using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// ВИЗУАЛЬНЫЕ статусы юнита от скиллов-конструкторов: значок в панели состояний и VFX.
    /// Ровно презентация и ничего больше — ни урона, ни модификаторов статов, ни таймеров геймплея.
    ///
    /// Зачем отдельный носитель. В движке `Effector` — это геймплейный объект, который живёт в локальной
    /// симуляции каждого пира, а значок и VFX появляются лишь как следствие его присутствия в `unit.effectors`.
    /// По сети эффекторы не передаются, поэтому раньше был выбор: либо клиент накладывает настоящий эффектор
    /// у себя (и локально крутит его геймплей), либо не видит статуса вовсе. Решение Artsiom 2026-08-01 —
    /// строгое разделение: состояние живёт только на сервере, а клиенту приходит явное «покажи вот это»
    /// (тем же путём, каким ядро реплицирует стан). Здесь и хранится то, что пришло.
    ///
    /// Компонент добавляется по требованию и самоуничтожается, когда показывать нечего.
    /// Движение юнита серверное (позиции синхронизируются), поэтому замедление и прочие модификаторы
    /// статов клиенту применять не нужно — он и так получит уже изменённое движение.
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
        Unit unit;
        bool subscribed;

        /// <summary>Ключ записи для VFX бафа: id скилла в отрицательной области, чтобы не пересечься с id эффекторов.</summary>
        public static int BuffKey(int abilityId) => -abilityId - 1;

        void Awake() => unit = GetComponent<Unit>();

        // ============================================================== ПОКАЗ ==

        /// <summary>Показать значок и VFX эффектора. Длительность берётся из самого эффектора.</summary>
        public static void ShowEffector(Unit target, Effector effector)
        {
            if (target == null || target.dead || effector == null) return;

            // Значок в панели состояний штатно рисуется только у НЕстакающих эффекторов с иконкой —
            // у остальных показываем один VFX, без записи значка.
            Effector iconSource = (!effector.stacks && effector.icon != null) ? effector : null;
            if (iconSource == null && effector.VFX == null) return;

            float duration = effector.permanent ? float.PositiveInfinity : effector.duration;
            Get(target).Add(effector.id, iconSource, effector.VFX, duration, 0f, effector.aboveHead);
        }

        /// <summary>Показать VFX длящегося бафа. Значка у него нет — значок даёт отдельный эффектор-статус.</summary>
        public static void ShowBuffVfx(Unit target, int abilityId, VFXReferencer vfx, float duration, float auraRadius)
        {
            if (target == null || target.dead || vfx == null || duration <= 0f) return;

            Get(target).Add(BuffKey(abilityId), null, vfx, duration, auraRadius, false);
        }

        static SkillVisualStatus Get(Unit target)
        {
            SkillVisualStatus holder = target.GetComponent<SkillVisualStatus>();
            if (holder == null) holder = target.gameObject.AddComponent<SkillVisualStatus>();
            return holder;
        }

        void Add(int key, Effector iconSource, VFXReferencer vfx, float duration, float auraRadius, bool aboveHead)
        {
            if (unit == null) unit = GetComponent<Unit>();
            if (unit == null) return;

            Entry entry = Find(key);
            if (entry != null)
            {
                // Повтор того же статуса продлевает показ, но не плодит второй значок и второй VFX.
                entry.remaining = Mathf.Max(entry.remaining, duration);
                return;
            }

            entry = new Entry { key = key, iconSource = iconSource, remaining = duration };

            if (vfx != null)
            {
                // VFX цепляется к юниту: когда юнит скрыт туманом войны, его дети скрыты вместе с ним —
                // отдельная проверка видимости не нужна, этим занимается сам механизм VFX-холдера.
                entry.vfxInstance = unit.AddVFX(vfx, aboveHead, auraRadius > 0f);
                if (entry.vfxInstance != null && auraRadius > 0f)
                    entry.vfxInstance.transform.SetGlobalScale(new Vector3(
                        unit.unitRadius * 2f + auraRadius, unit.unitHeight, unit.unitRadius * 2f + auraRadius));
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

        void Cleanup()
        {
            for (int i = 0; i < entries.Count; i++) DestroyVfx(entries[i]);
            entries.Clear();

            if (subscribed && GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            subscribed = false;

            Destroy(this);
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
