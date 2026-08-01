using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Ослепление: носитель с шансом промахивается своими прямыми атаками.
    /// Потребители: Тир 6 [А] «Паровой выхлоп», Тир 3 [Б] «Жертвенный покров» (вспышка при пробитии щита).
    ///
    /// Штатной механики промаха в ассете нет (проверено). Шанс живёт в <see cref="InterflowCombat"/>,
    /// бросок делает только сервер — как у штатного Evasion, иначе клиент и сервер разойдутся.
    /// Компонент навешивается на цель кодом (не через Inspector) и снимает себя сам по истечении времени.
    /// </summary>
    public class BlindDebuff : MonoBehaviour
    {
        Unit unit;
        float remaining;
        bool subscribed;
        internal bool destroyed; // Destroy(this) отложен до конца кадра — компонент уже нельзя переиспользовать
        bool cleared;            // своё ослепление уже снято: OnDestroy не должен трогать чужое

        /// <summary>
        /// Ослепить юнита. Повторное наложение продлевает время и берёт больший шанс (не складывается).
        /// </summary>
        /// <param name="target">Кого ослепляем.</param>
        /// <param name="chance">Шанс промаха 0..1 (0.5 = половина атак мимо).</param>
        /// <param name="duration">Длительность в секундах.</param>
        public static void Apply(Unit target, float chance, float duration)
        {
            if (NetworkConnectionHandler.isClient) return; // правило 6
            if (target == null || target.dead || chance <= 0f || duration <= 0f) return;

            // Компонент, помеченный на снятие, ещё живёт до конца кадра — переиспользовать его нельзя,
            // иначе Unity уничтожит его вместе с только что выданным ослеплением
            BlindDebuff debuff = null;
            BlindDebuff[] all = target.GetComponents<BlindDebuff>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && !all[i].destroyed) { debuff = all[i]; break; }

            if (debuff == null) debuff = target.gameObject.AddComponent<BlindDebuff>();

            debuff.Init(target, chance, duration);
        }

        void Init(Unit target, float chance, float duration)
        {
            unit = target;
            remaining = Mathf.Max(remaining, duration);

            InterflowCombat.MissChanceSet(unit, chance);

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

            remaining -= GameManager.instance.currentDeltaTime;
            if (remaining <= 0f) Cleanup();
        }

        void Cleanup()
        {
            if (destroyed) return;
            destroyed = true;
            cleared = true;

            if (unit != null) InterflowCombat.MissChanceClear(unit);

            if (subscribed && GameManager.instance != null)
            {
                GameManager.instance.Tick -= OnTick;
                subscribed = false;
            }

            Destroy(this);
        }

        void OnDestroy()
        {
            // Только если ослепление ещё наше: старый компонент уничтожается в конце кадра,
            // и без этой проверки он стёр бы ослепление, выданное новым компонентом в том же кадре
            if (!cleared && unit != null)
            {
                cleared = true;
                InterflowCombat.MissChanceClear(unit);
            }

            if (subscribed && GameManager.instance != null)
            {
                GameManager.instance.Tick -= OnTick;
                subscribed = false;
            }
        }
    }
}
