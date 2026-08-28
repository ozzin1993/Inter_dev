using System.Buffers.Text;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Unit that has a limited lifespan. After the specified amount of time it dies.

    public class LifetimeUnit : MonoBehaviour
    {
        [Tooltip("Duration of life of this unit")]
        public float lifespan = 10f;
        [HideInInspector] public float currentLifeSpan;

        Unit thisUnit;
        // Called by Initialize() of thisUnit
        public void Initialize()
        {
            currentLifeSpan = lifespan;
            GameManager.Instance.Tick += UpdateLifeTime;
            thisUnit = GetComponent<Unit>();
            thisUnit.OnDie += Die;
        }

        void Die(Unit unitThatDies, int playerKiller, Unit unitKiller, bool rewards)
        {
            GameManager.Instance.Tick -= UpdateLifeTime;
            thisUnit.OnDie -= Die;
        }

        void UpdateLifeTime()
        {
            currentLifeSpan -= GameManager.Instance.currentDeltaTime;
            if (currentLifeSpan < 0)
            {
                // [Interflow fix 2026-08-01 client-data] Диагностика «призванные не умирают»: явный след в логе.
                Debug.Log($"[LifetimeUnit] Время жизни истекло — {thisUnit.unitName} (netID {thisUnit.netID}) умирает.");
                thisUnit.Die(-1, null, false);
            }
        }

        public void SetLifetime(float time)
        {
            lifespan = time + currentLifeSpan;
        }
    }
}
