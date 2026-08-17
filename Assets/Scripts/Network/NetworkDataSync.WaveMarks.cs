using Unity.Netcode;

namespace StrategyCore
{
    /// <summary>
    /// Наше partial-расширение NetworkDataSync (ядро ассета не правится, кроме слова 'partial').
    /// Волна 2.0 — синхронизация ПОМЕТОК состава волны: клиент просит пометить/снять тип своей команды,
    /// сервер валидирует (owner-гейт) и применяет через MatchManager, затем рассылает актуальные пометки.
    /// Плюс предупреждение о превышении лидерства (t−warnSeconds). Образец — NetworkDataSync.PointSync.cs.
    /// </summary>
    public partial class NetworkDataSync
    {
        /// <summary>
        /// Клиент → сервер: пометить/снять тип в волне команды. op: 0 — автопризыв, 1 — разовый, 2 — снять.
        /// Сервер проверяет, что отправитель — владелец команды (ownerPlayer), затем применяет через MatchManager.
        /// Рассылка пометок клиентам идёт штатно из Try*/Clear (BroadcastMarks).
        /// </summary>
        [Rpc(SendTo.Server)]
        public void WaveMarkServerRpc(int teamIndex, int unitTypeID, int op, RpcParams rpcParams = default)
        {
            if (MatchManager.instance == null) return;
            if (teamIndex != 0 && teamIndex != 1) return; // в MVP только две команды A/B

            int senderPlayer = SlotManager.instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            TeamWaveConfig cfg = MatchManager.instance.Team(teamIndex);
            if (cfg == null || senderPlayer != cfg.ownerPlayer)
            {
                UnityEngine.Debug.LogWarning($"[WaveMarks] Запрос отклонён owner-гейтом: team={teamIndex}, sender={senderPlayer}, ownerPlayer={(cfg != null ? cfg.ownerPlayer : -1)}.");
                return;
            }

            switch (op)
            {
                case 0: MatchManager.instance.TrySetAuto(teamIndex, unitTypeID); break;
                case 1: MatchManager.instance.TrySetOneShot(teamIndex, unitTypeID); break;
                case 2: MatchManager.instance.TryClearMark(teamIndex, unitTypeID); break;
            }
        }

        /// <summary>
        /// Сервер → клиенты: актуальные пометки команды (авто-типы + разовые-типы). Вызывается из MatchManager
        /// после каждого изменения. Поздний вход (mid-game join) НЕ покрыт — как и PointSync.
        /// </summary>
        public void WaveMarksSend(int teamIndex, int[] autoIds, int[] oneShotIds)
        {
            if (!IsSpawned) return; // нет активной сети (локальный тест) — клиентов нет
            WaveMarksClientRpc(teamIndex, autoIds, oneShotIds);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void WaveMarksClientRpc(int teamIndex, int[] autoIds, int[] oneShotIds)
        {
            if (MatchManager.instance == null) return;
            MatchManager.instance.ApplyMarks(teamIndex, autoIds, oneShotIds);
        }

        /// <summary>Сервер → клиенты: предупреждение t−warnSeconds (состав команды не влезает в лидерство).</summary>
        public void WaveOverflowWarn(int teamIndex)
        {
            if (!IsSpawned) return;
            WaveOverflowWarnClientRpc(teamIndex);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void WaveOverflowWarnClientRpc(int teamIndex)
        {
            if (MatchManager.instance == null) return;
            MatchManager.instance.RaiseOverflowWarningClient(teamIndex);
        }
    }
}
