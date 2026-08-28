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
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        public void GraveSpawnClientRpc(int graveId, int unitTypeID, int owner, int team, int unitCategory, int tier, Vector3 position)
        {
            // Опоздавший клиент (mid-game join) в стадии загрузки сцены — не принимаем (как DieClientRpc).
            if (NetworkConnectionHandler.Instance != null && NetworkConnectionHandler.Instance.connectionStage == 2) return;
            if (MatchManager.Instance == null) return;

            GraveData data = new GraveData
            {
                unitTypeID = unitTypeID,
                owner = owner,
                team = team,
                unitCategory = (Unit.UnitCategory)unitCategory,
                tier = tier,
                position = position
            };
            MatchManager.Instance.ClientSpawnGrave(graveId, data);
        }

        /// <summary>Сервер → клиенты: убрать визуал могилки по id. Применяется в MatchManager.ClientDespawnGrave.</summary>
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        public void GraveDespawnClientRpc(int graveId)
        {
            if (MatchManager.Instance == null) return;
            MatchManager.Instance.ClientDespawnGrave(graveId);
        }
    }
}
