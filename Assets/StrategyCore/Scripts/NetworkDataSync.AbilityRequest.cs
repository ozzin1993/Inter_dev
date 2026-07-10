using Unity.Netcode;

namespace StrategyCore
{
    /// <summary>
    /// Наше partial-расширение NetworkDataSync (ядро ассета не правится, кроме слова 'partial').
    /// Каст центральной способности ПО ИДЕНТИЧНОСТИ: клиент шлёт Ability.id (стабильный ключ дровера
    /// [AbilityID]), сервер мапит id→index в СВОЁМ caster.abilities и кастует серверо-авторитетно.
    /// Убирает зависимость «массивы abilities байт-в-байт одинаковы на пирах» (NRE CheckAbilityItemRequirements
    /// при рассинхроне индексов). Образец паттерна — NetworkDataSync.TeamCommand.cs.
    /// </summary>
    public partial class NetworkDataSync
    {
        /// <summary>
        /// Клиент → сервер: запрос кастовать центральную способность (по Ability.id) кастером СВОЕЙ команды.
        /// Сервер определяет отправителя и проверяет, что он владелец команды (ownerPlayer), затем кастует
        /// через MatchManager.CastCentralAbilityById (единый источник). Хост кастует напрямую, без RPC.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void CastCentralAbilityServerRpc(int teamIndex, int abilityId, RpcParams rpcParams = default)
        {
            if (MatchManager.instance == null) return;
            if (teamIndex != 0 && teamIndex != 1) return; // MVP: только две команды A/B

            // Авторизация: отправитель должен управлять этой командой (как в TeamCommandServerRpc).
            int senderPlayer = SlotManager.instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            TeamWaveConfig cfg = MatchManager.instance.Team(teamIndex);
            if (cfg == null || senderPlayer != cfg.ownerPlayer) return; // нет прав на эту команду

            MatchManager.instance.CastCentralAbilityById(teamIndex, abilityId);
        }
    }
}
