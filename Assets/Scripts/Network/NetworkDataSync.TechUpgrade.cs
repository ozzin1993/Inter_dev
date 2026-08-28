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
        /// <summary>Клиент → сервер: запрос купить узел дерева ТИРОВ технологий СВОЕЙ команды (Технологии 2.0).
        /// step: 0=уровень, 1=большой выбор, 2=специализация; option/spec: 0=A,1=B (для своих ступеней). Сервер проверяет владельца и гейты.</summary>
        [Rpc(SendTo.Server)]
        public void UnlockTechTierServerRpc(int teamIndex, int tier, int step, int option, int spec, RpcParams rpcParams = default)
        {
            if (MatchManager.Instance == null) return;
            if (teamIndex != 0 && teamIndex != 1) return;

            int senderPlayer = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            TeamWaveConfig cfg = MatchManager.Instance.Team(teamIndex);
            if (cfg == null || senderPlayer != cfg.ownerPlayer) return; // нет прав на эту команду

            MatchManager.Instance.TryUnlockTierStep(teamIndex, tier, step, option, spec);
        }

        /// <summary>Клиент → сервер: АТОМАРНАЯ покупка «большой выбор + специализация» СВОЕЙ команды
        /// (карточка выбора в панели технологий). option/spec: 0=A, 1=B. Сервер проверяет владельца и оба гейта.</summary>
        [Rpc(SendTo.Server)]
        public void UnlockTechBigWithSpecServerRpc(int teamIndex, int tier, int option, int spec, RpcParams rpcParams = default)
        {
            if (MatchManager.Instance == null) return;
            if (teamIndex != 0 && teamIndex != 1) return;

            int senderPlayer = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            TeamWaveConfig cfg = MatchManager.Instance.Team(teamIndex);
            if (cfg == null || senderPlayer != cfg.ownerPlayer) return; // нет прав на эту команду

            MatchManager.Instance.TryUnlockBigOptionWithSpec(teamIndex, tier, option, spec);
        }

        /// <summary>Сервер → клиенты: новый уровень главного здания команды (для гейтинга UI).</summary>
        public void MainBuildingLevelSend(int teamIndex, int level)
        {
            if (!IsSpawned) return; // нет активной сети (локальный тест) — клиентов нет
            MainBuildingLevelClientRpc(teamIndex, level);
        }

        // Только реальные клиенты (сервер уже применил уровень локально).
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void MainBuildingLevelClientRpc(int teamIndex, int level)
        {
            if (MatchManager.Instance == null) return;
            MatchManager.Instance.ApplyMainBuildingLevel(teamIndex, level);
        }

        /// <summary>Сервер → клиенты: накопленный опыт главного здания команды (для показа игроку).
        /// Уровень рассылается отдельно (MainBuildingLevelSend) и здесь не дублируется.</summary>
        public void TeamExperienceSend(int teamIndex, int experience)
        {
            if (!IsSpawned) return; // нет активной сети (локальный тест) — клиентов нет
            TeamExperienceClientRpc(teamIndex, experience);
        }

        // Только реальные клиенты (на сервере значение уже актуально — он его и считает).
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void TeamExperienceClientRpc(int teamIndex, int experience)
        {
            if (MatchManager.Instance == null) return;
            MatchManager.Instance.ApplyTeamExperience(teamIndex, experience);
        }
    }
}
