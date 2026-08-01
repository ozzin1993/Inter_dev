using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Наше partial-расширение NetworkDataSync (ядро ассета не правится, кроме слова 'partial').
    /// Сеть МОГИЛОК (ADR-002 Вариант А): сервер → клиенты — спавн/деспавн ВИЗУАЛА могилки.
    /// Реестр и время жизни — на сервере (MatchManager.Graves). Серверо-авторитетно (правило 6).
    /// Образец паттерна — NetworkDataSync.Hero.cs.
    /// </summary>
    public partial class NetworkDataSync
    {
        /// <summary>Сервер → клиенты: заспавнить визуал могилки. Применяется в MatchManager.ClientSpawnGrave.</summary>
        [Rpc(SendTo.NotServer)]
        public void GraveSpawnClientRpc(int graveId, int unitTypeID, int owner, int team, int unitCategory, int tier, Vector3 position)
        {
            // Опоздавший клиент (mid-game join) в стадии загрузки сцены — не принимаем (как DieClientRpc).
            if (NetworkConnectionHandler.instance != null && NetworkConnectionHandler.instance.connectionStage == 2) return;
            if (MatchManager.instance == null) return;

            GraveData data = new GraveData
            {
                unitTypeID = unitTypeID,
                owner = owner,
                team = team,
                unitCategory = (Unit.UnitCategory)unitCategory,
                tier = tier,
                position = position
            };
            MatchManager.instance.ClientSpawnGrave(graveId, data);
        }

        /// <summary>Сервер → клиенты: убрать визуал могилки по id. Применяется в MatchManager.ClientDespawnGrave.</summary>
        [Rpc(SendTo.NotServer)]
        public void GraveDespawnClientRpc(int graveId)
        {
            if (MatchManager.instance == null) return;
            MatchManager.instance.ClientDespawnGrave(graveId);
        }
    }
}
