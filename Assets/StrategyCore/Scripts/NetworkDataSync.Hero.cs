using Unity.Netcode;

namespace StrategyCore
{
    /// <summary>
    /// Наше partial-расширение NetworkDataSync (ядро ассета не правится, кроме слова 'partial').
    /// Сетевые точки входа ГЕРОЯ: призыв и каст умения. Клиент шлёт запрос, сервер проверяет владельца команды
    /// и исполняет через MatchManager (единый источник, серверо-авторитетно, правило 6). Хост зовёт MatchManager
    /// напрямую, без RPC. Образец паттерна — NetworkDataSync.AbilityRequest.cs.
    /// </summary>
    public partial class NetworkDataSync
    {
        /// <summary>
        /// Клиент → сервер: призвать героя СВОЕЙ команды. Сервер проверяет владельца и зовёт MatchManager.SummonHero
        /// (guard «один живой», золото, спавн в слоте). Хост призывает напрямую, без RPC.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void SummonHeroServerRpc(int teamIndex, RpcParams rpcParams = default)
        {
            if (MatchManager.instance == null) return;
            if (teamIndex != 0 && teamIndex != 1) return; // MVP: только две команды A/B

            // Авторизация: отправитель должен управлять этой командой (как в CastCentralAbilityServerRpc).
            int senderPlayer = SlotManager.instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            TeamWaveConfig cfg = MatchManager.instance.Team(teamIndex);
            if (cfg == null || senderPlayer != cfg.ownerPlayer) return; // нет прав на эту команду

            MatchManager.instance.SummonHero(teamIndex);
        }

        /// <summary>
        /// Клиент → сервер: активировать умение героя СВОЕЙ команды по Ability.id. Сервер проверяет владельца
        /// и зовёт MatchManager.CastHeroAbilityById (кастер = живой герой). Хост кастует напрямую, без RPC.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void CastHeroAbilityServerRpc(int teamIndex, int abilityId, RpcParams rpcParams = default)
        {
            if (MatchManager.instance == null) return;
            if (teamIndex != 0 && teamIndex != 1) return; // MVP: только две команды A/B

            // Авторизация: отправитель должен управлять этой командой (как в CastCentralAbilityServerRpc).
            int senderPlayer = SlotManager.instance.GetClientSlot(rpcParams.Receive.SenderClientId);
            TeamWaveConfig cfg = MatchManager.instance.Team(teamIndex);
            if (cfg == null || senderPlayer != cfg.ownerPlayer) return; // нет прав на эту команду

            MatchManager.instance.CastHeroAbilityById(teamIndex, abilityId);
        }

        // [UI-сессия 2026-07-06] Синк состояния героя клиенту — для дизейбла кнопки призыва у клиента.
        /// <summary>
        /// Сервер → клиенты: факт «жив ли герой команды» (heroUnit клиенту не синхронизируется).
        /// Применяется в MatchManager.ApplyHeroAliveClient (обновляет зеркало + уведомляет UI).
        /// </summary>
        [Rpc(SendTo.NotServer)]
        public void HeroAliveClientRpc(int teamIndex, bool alive)
        {
            if (MatchManager.instance == null) return;
            if (teamIndex != 0 && teamIndex != 1) return;
            MatchManager.instance.ApplyHeroAliveClient(teamIndex, alive);
        }

        // [UI-сессия 2026-07-06] Синк уровня героя клиенту — для .locked умений героя по уровню у клиента.
        /// <summary>
        /// Сервер → клиенты: текущий уровень героя команды (LevelingUnit клиенту не синкается).
        /// Применяется в MatchManager.ApplyHeroLevelClient (обновляет зеркало + перерисовывает умения).
        /// </summary>
        [Rpc(SendTo.NotServer)]
        public void HeroLevelClientRpc(int teamIndex, int level)
        {
            if (MatchManager.instance == null) return;
            if (teamIndex != 0 && teamIndex != 1) return;
            MatchManager.instance.ApplyHeroLevelClient(teamIndex, level);
        }
    }
}
