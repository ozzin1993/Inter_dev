using Unity.Netcode;

namespace StrategyCore
{
    /// <summary>
    /// Наше partial-расширение NetworkDataSync (ядро ассета не правится, кроме слова 'partial').
    /// Синхронизация состава волны: клиент просит изменить состав своей команды, сервер валидирует
    /// и применяет, затем рассылает актуальный состав всем клиентам.
    /// Образец — NetworkDataSync.PointSync.cs.
    /// </summary>
    public partial class NetworkDataSync
    {
        /// <summary>
        /// Клиент → сервер: запрос изменить состав волны команды (добавить/убрать один юнит типа unitTypeID).
        /// Сервер определяет отправителя и проверяет, что он владелец этой команды (ownerPlayer),
        /// затем применяет через MatchManager. Рассылка клиентам идёт штатно из Try*WaveUnit.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void WaveCompositionChangeServerRpc(int teamIndex, int unitTypeID, bool add, RpcParams rpcParams = default)
        {
            if (MatchManager.instance == null) return;
            if (teamIndex != 0 && teamIndex != 1) return; // в MVP только две команды A/B

            // Авторизация: отправитель должен управлять этой командой.
            int senderPlayer = SlotManager.instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            TeamWaveConfig cfg = MatchManager.instance.Team(teamIndex);
            if (cfg == null || senderPlayer != cfg.ownerPlayer)
            {
                // [Interflow fix 2026-06-26] Диагностика: видно, если запрос состава волны режется owner-гейтом (sender vs ownerPlayer).
                UnityEngine.Debug.LogWarning($"[WaveComposition] Запрос отклонён owner-гейтом: team={teamIndex}, sender={senderPlayer}, ownerPlayer={(cfg != null ? cfg.ownerPlayer : -1)}.");
                return; // нет прав на эту команду
            }

            // Резолв префаба по typeID — штатно через GameManager.gameUnits.
            if (!GameManager.instance.gameUnits.TryGetValue(unitTypeID, out Unit prefab) || prefab == null) return;

            if (add) MatchManager.instance.TryAddWaveUnit(teamIndex, prefab);
            else     MatchManager.instance.TryRemoveWaveUnit(teamIndex, prefab);
        }

        /// <summary>
        /// Сервер → клиенты: полный актуальный состав команды (параллельные массивы typeID + count).
        /// Вызывается из MatchManager после каждого успешного изменения (и хостом, и по запросу клиента).
        /// Поздний вход (mid-game join) НЕ покрыт — как и PointSync, рассылается только живое изменение.
        /// </summary>
        public void WaveCompositionSend(int teamIndex, int[] typeIDs, int[] counts)
        {
            if (!IsSpawned) return; // нет активной сети (локальный тест) — клиентов нет
            WaveCompositionClientRpc(teamIndex, typeIDs, counts);
        }

        // Только реальные клиенты (сервер уже применил состав локально в Try*WaveUnit).
        [Rpc(SendTo.NotServer)]
        private void WaveCompositionClientRpc(int teamIndex, int[] typeIDs, int[] counts)
        {
            if (MatchManager.instance == null) return;
            MatchManager.instance.ApplyWaveComposition(teamIndex, typeIDs, counts);
        }
    }
}
