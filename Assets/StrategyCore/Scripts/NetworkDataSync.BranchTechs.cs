using Unity.Netcode;

namespace StrategyCore
{
    /// <summary>
    /// Наше partial-расширение NetworkDataSync (ядро ассета не правится, кроме слова 'partial').
    /// Серверо-авторитетная покупка узлов веток Душ (N4). Образец — NetworkDataSync.TechUpgrade.cs.
    /// Синк разблокировки — штатный TechnologyManager.UnlockTech, отдельного ClientRpc не нужно.
    /// </summary>
    public partial class NetworkDataSync
    {
        /// <summary>
        /// Клиент → сервер: запрос купить опцию узла ветки Душ (branch, tier, option: 0=А, 1=Б) СВОЕЙ команды.
        /// Сервер проверяет владельца и все правила веток (лимит/эксклюзив/последовательность/оплату).
        /// </summary>
        [Rpc(SendTo.Server)]
        public void UnlockSoulOptionServerRpc(int teamIndex, int branch, int tier, int option, RpcParams rpcParams = default)
        {
            if (MatchManager.instance == null) return;
            if (teamIndex != 0 && teamIndex != 1) return;

            int senderPlayer = SlotManager.instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            TeamWaveConfig cfg = MatchManager.instance.Team(teamIndex);
            if (cfg == null || senderPlayer != cfg.ownerPlayer) return; // нет прав на эту команду

            MatchManager.instance.TryUnlockSoulOption(teamIndex, branch, tier, option);
        }
    }
}
