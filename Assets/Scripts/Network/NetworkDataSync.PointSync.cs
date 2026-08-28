using Unity.Netcode;

namespace StrategyCore
{
    /// <summary>
    /// Наше partial-расширение NetworkDataSync (ядро ассета не правится, кроме слова 'partial').
    /// Синхронизация владения точками интереса с сервера на клиентов.
    /// Сюда же добавлять будущие пары Send/ClientRpc по PoI-логике.
    /// </summary>
    public partial class NetworkDataSync
    {
        /// <summary>
        /// Сервер → клиенты: новый владелец точки линии. index — позиция точки в Lane.points.
        /// Поздний вход (mid-game join) НЕ покрыт: рассылается только живой захват.
        /// </summary>
        public void PointTeamSend(int index, int team)
        {
            if (!IsSpawned) return; // нет активной сети (локальный тест) — клиентов нет
            PointTeamClientRpc(index, team);
        }

        // Только реальные клиенты (сервер уже применил владение локально через PointOfInterest.SetTeam).
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void PointTeamClientRpc(int index, int team)
        {
            if (MatchManager.Instance == null) return;
            MatchManager.Instance.ApplyPointTeam(index, team);
        }
    }
}
