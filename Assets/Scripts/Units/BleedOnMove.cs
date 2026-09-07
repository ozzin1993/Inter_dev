using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// «Глубокая рана»: цель получает урон за то, что двигается — стоя на месте не кровоточит.
    /// Потребитель: Тир 5 [Б] «Рассекающие цепи» (каждая третья атака вешает рану).
    ///
    /// Эффектором это не выразить: штатный Damage Over Time тикает всегда и не смотрит на движение.
    /// Компонент навешивается кодом на цель, снимает себя по истечении времени.
    /// </summary>
    public class BleedOnMove : MonoBehaviour
    {
        Unit unit;
        Unit source;          // кто нанёс рану (для засчёта убийства)
        int sourceOwner = -1;
        DamageType damageType;
        Ability sourceAbility; // умение-источник для диагностики очереди пакетов (решение Artsiom 05.09.2026); null — без умения
        float damagePerMeter;
        float remaining;
        Vector3 lastPosition;
        bool subscribed;
        internal bool destroyed; // Destroy(this) отложен до конца кадра — переиспользовать компонент нельзя

        /// <summary>
        /// Навесить рану. Повторное наложение продлевает время и берёт больший урон (не складывается).
        /// </summary>
        /// <param name="target">Кому.</param>
        /// <param name="byUnit">Кто нанёс (может быть null).</param>
        /// <param name="byOwner">Игрок-источник (для награды за убийство).</param>
        /// <param name="type">Тип урона раны.</param>
        /// <param name="damagePerMeterMoved">Сколько урона за каждый пройденный метр.</param>
        /// <param name="duration">Сколько секунд держится рана.</param>
        /// <param name="sourceAbility">Умение-источник для диагностики очереди пакетов (решение Artsiom 05.09.2026); null — без умения.</param>
        public static void Apply(Unit target, Unit byUnit, int byOwner, DamageType type, float damagePerMeterMoved, float duration, Ability sourceAbility)
        {
            if (NetworkConnectionHandler.isClient) return; // правило 6
            if (target == null || target.dead || type == null) return;
            if (damagePerMeterMoved <= 0f || duration <= 0f) return;

            BleedOnMove bleed = null;
            BleedOnMove[] all = target.GetComponents<BleedOnMove>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && !all[i].destroyed) { bleed = all[i]; break; }

            if (bleed == null) bleed = target.gameObject.AddComponent<BleedOnMove>();

            bleed.Init(target, byUnit, byOwner, type, damagePerMeterMoved, duration, sourceAbility);
        }

        void Init(Unit target, Unit byUnit, int byOwner, DamageType type, float damagePerMeterMoved, float duration, Ability byAbility)
        {
            unit = target;
            source = byUnit;
            sourceOwner = byOwner;
            damageType = type;
            sourceAbility = byAbility;
            damagePerMeter = Mathf.Max(damagePerMeter, damagePerMeterMoved);
            remaining = Mathf.Max(remaining, duration);
            lastPosition = target.transform.position;

            if (!subscribed && GameManager.Instance != null)
            {
                GameManager.Instance.Tick += OnTick;
                subscribed = true;
            }
        }

        void OnTick()
        {
            if (NetworkConnectionHandler.isClient || GameManager.Instance == null) return;

            if (unit == null || unit.dead)
            {
                Cleanup();
                return;
            }

            Vector3 now = unit.transform.position;
            float moved = (now - lastPosition).magnitude;
            lastPosition = now;

            if (moved > 0.01f)
            {
                DamagePacket packet = DamagePacket.Create(moved * damagePerMeter, damageType, sourceOwner, source, false, sourceAbility);   // [Interflow fix 2026-09-04 damage-full-packet] пакет одной записи
                unit.GetDamage(in packet, out float _);
            }

            remaining -= GameManager.Instance.currentDeltaTime;
            if (remaining <= 0f) Cleanup();
        }

        void Cleanup()
        {
            if (destroyed) return;
            destroyed = true;

            if (subscribed && GameManager.Instance != null)
            {
                GameManager.Instance.Tick -= OnTick;
                subscribed = false;
            }

            Destroy(this);
        }

        void OnDestroy()
        {
            if (subscribed && GameManager.Instance != null)
            {
                GameManager.Instance.Tick -= OnTick;
                subscribed = false;
            }
        }
    }
}
