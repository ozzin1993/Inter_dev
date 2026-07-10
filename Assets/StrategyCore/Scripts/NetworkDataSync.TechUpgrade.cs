using Unity.Netcode;

namespace StrategyCore
{
    /// <summary>
    /// Наше partial-расширение NetworkDataSync (ядро ассета не правится, кроме слова 'partial').
    /// Серверо-авторитетные запросы по технологиям/улучшению главного здания и синк уровня ГЗ клиентам.
    /// Образец — NetworkDataSync.TeamCommand.cs / PointSync.cs.
    /// </summary>
    public partial class NetworkDataSync
    {
        /// <summary>
        /// Клиент → сервер: запрос улучшить главное здание СВОЕЙ команды. expectedLevel — ожидаемый клиентом
        /// текущий уровень (идемпотентность против дубль/устаревших кликов). Сервер проверяет владельца и гейт.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void UpgradeMainBuildingServerRpc(int teamIndex, int expectedLevel, RpcParams rpcParams = default)
        {
            if (MatchManager.instance == null) return;
            if (teamIndex != 0 && teamIndex != 1) return;

            // Авторизация: отправитель должен управлять этой командой.
            int senderPlayer = SlotManager.instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            TeamWaveConfig cfg = MatchManager.instance.Team(teamIndex);
            if (cfg == null || senderPlayer != cfg.ownerPlayer) return; // нет прав на эту команду

            MatchManager.instance.TryUpgradeMainBuilding(teamIndex, expectedLevel);
        }

        /// <summary>Клиент → сервер: запрос разблокировать узел технологии (branch, level) СВОЕЙ команды. Сервер проверяет владельца и гейт.</summary>
        [Rpc(SendTo.Server)]
        public void UnlockTechServerRpc(int teamIndex, int branch, int level, RpcParams rpcParams = default)
        {
            if (MatchManager.instance == null) return;
            if (teamIndex != 0 && teamIndex != 1) return;

            int senderPlayer = SlotManager.instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            TeamWaveConfig cfg = MatchManager.instance.Team(teamIndex);
            if (cfg == null || senderPlayer != cfg.ownerPlayer) return; // нет прав на эту команду

            MatchManager.instance.TryUnlockTech(teamIndex, branch, level);
        }

        /// <summary>Сервер → клиенты: новый уровень главного здания команды (для гейтинга UI).</summary>
        public void MainBuildingLevelSend(int teamIndex, int level)
        {
            if (!IsSpawned) return; // нет активной сети (локальный тест) — клиентов нет
            MainBuildingLevelClientRpc(teamIndex, level);
        }

        // Только реальные клиенты (сервер уже применил уровень локально).
        [Rpc(SendTo.NotServer)]
        private void MainBuildingLevelClientRpc(int teamIndex, int level)
        {
            if (MatchManager.instance == null) return;
            MatchManager.instance.ApplyMainBuildingLevel(teamIndex, level);
        }
    }
}
