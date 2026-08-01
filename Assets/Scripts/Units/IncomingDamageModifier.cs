using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Временное изменение входящего урона на юните — в обе стороны:
    /// уязвимость (множитель &gt; 1) и защита (множитель &lt; 1).
    ///
    /// Потребители: Тир 6 [Б] «Кара еретиков» (дебафф «Приговор»: +40% входящего от заданного типа урона),
    /// Тир 3 [Б] «Жертвенный покров» (−50% входящего под щитом).
    ///
    /// Почему не эффектором: эффектор умеет менять броню, но не множитель урона и не умеет
    /// различать ТИП урона. Правило живёт в <see cref="InterflowCombat"/>, который получает тип
    /// из <c>Unit.GetDamage</c> (маркер <c>[Interflow fix 2026-07-24 combat-hub]</c>).
    ///
    /// Компонент навешивается кодом и снимает себя сам. На одном юните может висеть несколько
    /// правил одновременно (например «Приговор» и щит) — они перемножаются.
    /// </summary>
    public class IncomingDamageModifier : MonoBehaviour
    {
        class Entry
        {
            public InterflowCombat.IncomingRule rule;
            public float remaining;
        }

        Unit unit;
        readonly System.Collections.Generic.List<Entry> entries = new System.Collections.Generic.List<Entry>();
        bool subscribed;
        internal bool destroyed; // Destroy(this) отложен до конца кадра — переиспользовать компонент нельзя

        /// <summary>
        /// Навесить временное изменение входящего урона.
        /// </summary>
        /// <param name="target">Носитель.</param>
        /// <param name="multiplier">Множитель входящего урона: 1.4 = +40%, 0.5 = −50%.</param>
        /// <param name="duration">Длительность в секундах.</param>
        /// <param name="onlyType">Тип урона, к которому применять. Пусто — любой.</param>
        /// <param name="onlyDirectAttack">Только прямые атаки юнитов (не эффекторы и зоны).</param>
        public static void Apply(Unit target, float multiplier, float duration, DamageType onlyType = null, bool onlyDirectAttack = false)
        {
            if (NetworkConnectionHandler.isClient) return; // правило 6
            if (target == null || target.dead || duration <= 0f) return;
            if (Mathf.Approximately(multiplier, 1f)) return;

            IncomingDamageModifier mod = null;
            IncomingDamageModifier[] all = target.GetComponents<IncomingDamageModifier>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && !all[i].destroyed) { mod = all[i]; break; }

            if (mod == null) mod = target.gameObject.AddComponent<IncomingDamageModifier>();

            mod.AddRule(target, multiplier, duration, onlyType, onlyDirectAttack);
        }

        /// <summary>
        /// Снять раньше срока изменение входящего урона с заданным множителем и типом.
        /// Нужно тем, у кого эффект живёт не по таймеру, а пока держится что-то ещё (например щит).
        /// </summary>
        public static void RemoveRule(Unit target, float multiplier, DamageType onlyType = null)
        {
            if (target == null) return;

            IncomingDamageModifier[] all = target.GetComponents<IncomingDamageModifier>();
            for (int i = 0; i < all.Length; i++)
            {
                IncomingDamageModifier mod = all[i];
                if (mod == null || mod.destroyed) continue;

                for (int e = mod.entries.Count - 1; e >= 0; e--)
                {
                    Entry entry = mod.entries[e];
                    if (entry.rule == null) continue;
                    if (!Mathf.Approximately(entry.rule.multiplier, multiplier) || entry.rule.onlyType != onlyType) continue;

                    InterflowCombat.IncomingRuleRemove(target, entry.rule);
                    mod.entries.RemoveAt(e);
                }
            }
        }

        void AddRule(Unit target, float multiplier, float duration, DamageType onlyType, bool onlyDirectAttack)
        {
            unit = target;

            // Не стакаем одинаковые: тот же множитель и тот же тип — только продлеваем
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                if (e.rule != null && Mathf.Approximately(e.rule.multiplier, multiplier) && e.rule.onlyType == onlyType)
                {
                    e.remaining = Mathf.Max(e.remaining, duration);
                    return;
                }
            }

            var rule = new InterflowCombat.IncomingRule
            {
                multiplier = multiplier,
                onlyType = onlyType,
                onlyDirectAttack = onlyDirectAttack
            };

            InterflowCombat.IncomingRuleAdd(unit, rule);
            entries.Add(new Entry { rule = rule, remaining = duration });

            if (!subscribed && GameManager.instance != null)
            {
                GameManager.instance.Tick += OnTick;
                subscribed = true;
            }
        }

        void OnTick()
        {
            if (NetworkConnectionHandler.isClient || GameManager.instance == null) return;

            if (unit == null || unit.dead)
            {
                Cleanup();
                return;
            }

            float dt = GameManager.instance.currentDeltaTime;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                entries[i].remaining -= dt;
                if (entries[i].remaining > 0f) continue;

                InterflowCombat.IncomingRuleRemove(unit, entries[i].rule);
                entries.RemoveAt(i);
            }

            if (entries.Count == 0) Cleanup();
        }

        void Cleanup()
        {
            if (destroyed) return;
            destroyed = true;

            RemoveAll();

            if (subscribed && GameManager.instance != null)
            {
                GameManager.instance.Tick -= OnTick;
                subscribed = false;
            }

            Destroy(this);
        }

        void RemoveAll()
        {
            if (unit == null) { entries.Clear(); return; }

            for (int i = 0; i < entries.Count; i++) InterflowCombat.IncomingRuleRemove(unit, entries[i].rule);
            entries.Clear();
        }

        void OnDestroy()
        {
            RemoveAll();

            if (subscribed && GameManager.instance != null)
            {
                GameManager.instance.Tick -= OnTick;
                subscribed = false;
            }
        }
    }
}
