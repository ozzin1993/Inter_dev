using Unity.Netcode;

namespace StrategyCore
{
    /// <summary>
    /// Наше partial-расширение NetworkDataSync (ядро ассета не правится, кроме слова 'partial').
    /// Серверо-авторитетная команда нижней таблицы (Атака/Защита): клиент шлёт НАМЕРЕНИЕ своей команде,
    /// сервер проверяет, что отправитель владеет этой командой (ownerPlayer), и выполняет команду.
    /// Так режим команды (currentCommand) остаётся авторитетным на сервере → переотдача при спавне волны
    /// и гибели башни работает и для клиента. Образец паттерна — NetworkDataSync.AbilityRequest.cs.
    /// </summary>
    public partial class NetworkDataSync
    {
        /// <summary>
        /// Клиент → сервер: команда (Атака/Защита) своей команде для РЯДА groupIndex. action = (int)BottomTableAction.
        /// groupIndex — индекс ряда кнопок (MatchManager.commandGroups): какой набор классов юнитов затрагивается.
        /// Сервер определяет отправителя и проверяет владельца команды, затем зовёт Send*Command (единый источник).
        /// </summary>
        [Rpc(SendTo.Server)]
        public void TeamCommandServerRpc(int teamIndex, int action, int groupIndex, RpcParams rpcParams = default)
        {
            if (MatchManager.instance == null) return;
            if (teamIndex != 0 && teamIndex != 1) return; // MVP: только две команды A/B

            // Авторизация: отправитель должен управлять этой командой (как в CastCentralAbilityServerRpc).
            int senderPlayer = SlotManager.instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            TeamWaveConfig cfg = MatchManager.instance.Team(teamIndex);
            BottomTableAction a = (BottomTableAction)action;

            // [Interflow fix 2026-06-26] Лог источника команды: кто прислал (слот), на какую команду/ряд, владелец команды.
            UnityEngine.Debug.Log($"[TeamCommand] От sender(slot)={senderPlayer} clientId={rpcParams.Receive.SenderClientId}: " +
                                  $"team={teamIndex} ряд={groupIndex} action={a} ownerPlayer(team{teamIndex})={(cfg != null ? cfg.ownerPlayer : -1)}" +
                                  $"{((cfg == null || senderPlayer != cfg.ownerPlayer) ? " → ОТКЛОНЕНО (не владелец)" : " → принято")}.");

            if (cfg == null || senderPlayer != cfg.ownerPlayer) return; // нет прав на эту команду

            if (a == BottomTableAction.Attack)       MatchManager.instance.SendAttackCommand(teamIndex, groupIndex);
            else if (a == BottomTableAction.Defence)  MatchManager.instance.SendDefenceCommand(teamIndex, groupIndex);
        }
    }
}
