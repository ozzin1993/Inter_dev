using Unity.Netcode;

namespace StrategyCore
{
    /// <summary>
    /// Наше partial-расширение NetworkDataSync (ядро ассета не правится, кроме слова 'partial').
    /// Сетевые точки входа ГЕРОЯ: каст умения + синки живости/уровня. Ручной призыв СНЕСЁН 2026-07-24
    /// (Волна 2.0: герой автоспавнится с волной — MatchManager.TryAutoSpawnHero). Клиент шлёт запрос, сервер проверяет владельца команды
    /// и исполняет через MatchManager (единый источник, серверо-авторитетно, правило 6). Хост зовёт MatchManager
    /// напрямую, без RPC. Образец паттерна — NetworkDataSync.AbilityRequest.cs.
    /// </summary>
    public partial class NetworkDataSync
    {
        /// <summary>
        /// Клиент → сервер: активировать умение героя СВОЕЙ команды по Ability.id. Сервер проверяет владельца
        /// и зовёт MatchManager.CastHeroAbilityById (кастер = живой герой). Хост кастует напрямую, без RPC.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void CastHeroAbilityServerRpc(int teamIndex, int abilityId, RpcParams rpcParams = default)
        {
            if (MatchManager.Instance == null) return;
            if (teamIndex != 0 && teamIndex != 1) return; // MVP: только две команды A/B

            // Авторизация: отправитель должен управлять этой командой (как в CastCentralAbilityServerRpc).
            int senderPlayer = SlotManager.Instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            TeamWaveConfig cfg = MatchManager.Instance.Team(teamIndex);
            if (cfg == null || senderPlayer != cfg.ownerPlayer) return; // нет прав на эту команду

            MatchManager.Instance.CastHeroAbilityById(teamIndex, abilityId);
        }

        // [UI-сессия 2026-07-06] Синк состояния героя клиенту — для дизейбла кнопки призыва у клиента.
        /// <summary>
        /// Сервер → клиенты: факт «жив ли герой команды» (heroUnit клиенту не синхронизируется).
        /// Применяется в MatchManager.ApplyHeroAliveClient (обновляет зеркало + уведомляет UI).
        /// </summary>
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        public void HeroAliveClientRpc(int teamIndex, bool alive)
        {
            if (MatchManager.Instance == null) return;
            if (teamIndex != 0 && teamIndex != 1) return;
            MatchManager.Instance.ApplyHeroAliveClient(teamIndex, alive);
        }

        // [UI-сессия 2026-07-06] Синк уровня героя клиенту — для .locked умений героя по уровню у клиента.
        /// <summary>
        /// Сервер → клиенты: текущий уровень героя команды (LevelingUnit клиенту не синкается).
        /// Применяется в MatchManager.ApplyHeroLevelClient (обновляет зеркало + перерисовывает умения).
        /// </summary>
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        public void HeroLevelClientRpc(int teamIndex, int level)
        {
            if (MatchManager.Instance == null) return;
            if (teamIndex != 0 && teamIndex != 1) return;
            MatchManager.Instance.ApplyHeroLevelClient(teamIndex, level);
        }
    }
}
