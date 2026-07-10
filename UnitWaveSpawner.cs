using System;
using System.Collections;
using UnityEngine;

namespace StrategyCore
{
    // ============================= WAVE ENTRY ==
    [Serializable]
    public class WaveEntry
    {
        public Unit unitToSpawn;
        [Tooltip("Количество юнитов данного типа в волне")]
        public int count = 1;
    }

    // ============================= SPAWNER ==
    public class UnitWaveSpawner : MonoBehaviour
    {
        [Header("Wave Setup")]
        [Tooltip("Состав волны: список типов юнитов и их количество")]
        public WaveEntry[] waveComposition;
        [Tooltip("Интервал в секундах между волнами")]
        public float interval = 30f;
        [Tooltip("Индекс игрока-владельца спавнящихся юнитов")]
        public int ownerPlayer = 0;
        [Tooltip("Задержка в секундах между спавном каждого юнита внутри волны. 0 — без задержки")]
        public float spawnDelay = 0f;

        [Header("Positions")]
        [Tooltip("Точка спавна юнитов")]
        public Transform spawnPoint;
        [Tooltip("Точка назначения — юниты идут туда в режиме AttackMove")]
        public Transform targetPoint;

        // ============================= LIFECYCLE ==
        void Start()
        {
            if (NetworkConnectionHandler.isClient) return;
            StartCoroutine(WaveLoop());
        }

        // ============================= COROUTINE ==
        IEnumerator WaveLoop()
        {
            yield return new WaitUntil(() => SlotManager.instance != null && SlotManager.instance.gameOn);
            while (true)
            {
                yield return StartCoroutine(SpawnWave());
                yield return new WaitForSeconds(interval);
            }
        }

        // ============================= SPAWN ==
        IEnumerator SpawnWave()
        {
            if (spawnPoint == null || targetPoint == null)
            {
                Debug.LogWarning("[UnitWaveSpawner] spawnPoint или targetPoint не назначены.");
                yield break;
            }
            if (waveComposition == null || waveComposition.Length == 0)
            {
                Debug.LogWarning("[UnitWaveSpawner] waveComposition пуст — волна не будет заспавнена.");
                yield break;
            }

            Vector2 attackTarget = new Vector2(targetPoint.position.x, targetPoint.position.z);

            foreach (WaveEntry entry in waveComposition)
            {
                for (int i = 0; i < entry.count; i++)
                {
                    Unit spawned = Unit.Spawn(entry.unitToSpawn, spawnPoint.position, 0f, ownerPlayer);
                    if (spawned != null) spawned.AttackMove(attackTarget);

                    if (spawnDelay > 0)
                        yield return new WaitForSeconds(spawnDelay);
                }
            }
        }
    }
}