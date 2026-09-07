using Unity.Netcode;

namespace StrategyCore
{
    /// <summary>
    /// Наше partial-расширение NetworkDataSync (ядро ассета не правится, кроме слова 'partial').
    /// Б11 «возврат в матч после обрыва связи»: АДРЕСНАЯ досылка состояния матча ОДНОМУ клиенту,
    /// который подключился в середине матча (в том числе вернулся после обрыва).
    ///
    /// Зачем отдельные пары, а не существующие широковещательные: все каналы состояния матча работают
    /// по правилу «изменилось — разослали» (MainBuildingLevelSend, TeamExperienceSend, WaveMarksSend,
    /// PointTeamSend, TeamCommandSend). Вернувшийся пропустил ровно эти события, а нового изменения
    /// может не случиться до конца матча. Повторный широковещательный вызов заставил бы всех остальных
    /// перерисовывать панели впустую, поэтому досылка идёт RpcTarget.Single — образец GroundZoneResendSend
    /// (NetworkDataSync.UnitStatus.cs:212).
    ///
    /// Единственный вызыватель — MatchManager.ResendMatchStateTo (MatchManager.MatchResend.cs).
    /// Сервер к этому моменту уже отправил слепок сцены (SendSceneData), в нём юниты, ресурсы и технологии;
    /// здесь идёт только то, чего в слепке нет.
    /// </summary>
    public partial class NetworkDataSync
    {
        /// <summary>
        /// Сервер → одному клиенту: уровень главного здания команды и накопленный опыт в текущем уровне.
        /// Обе величины в одном сообщении: их показывает одна и та же строка прогресса, а порядок применения
        /// важен (опыт рисуется вместе с уровнем — PushMainBuildingProgress).
        /// </summary>
        public void MainBuildingStateResendSend(ulong clientID, int teamIndex, int level, int experience)
        {
            if (!IsSpawned || !ServerCanSend()) return;   // как у парных вещательных каналов (PointSync, WaveMarks, TechUpgrade, TeamCommand)
            MainBuildingStateResendClientRpc(teamIndex, level, experience, RpcTarget.Single(clientID, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void MainBuildingStateResendClientRpc(int teamIndex, int level, int experience, RpcParams rpcParams = default)
        {
            if (MatchManager.Instance == null) return;
            // Сначала уровень: ApplyTeamExperience рисует прогресс и читает уже установленный уровень.
            MatchManager.Instance.ApplyMainBuildingLevel(teamIndex, level);
            MatchManager.Instance.ApplyTeamExperience(teamIndex, experience);
        }

        /// <summary>Сервер → одному клиенту: актуальные пометки состава волны команды (авто-типы + разовые-типы).</summary>
        public void WaveMarksResendSend(ulong clientID, int teamIndex, int[] autoIds, int[] oneShotIds)
        {
            if (!IsSpawned || !ServerCanSend()) return;   // как у парных вещательных каналов (PointSync, WaveMarks, TechUpgrade, TeamCommand)
            WaveMarksResendClientRpc(teamIndex, autoIds, oneShotIds, RpcTarget.Single(clientID, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void WaveMarksResendClientRpc(int teamIndex, int[] autoIds, int[] oneShotIds, RpcParams rpcParams = default)
        {
            if (MatchManager.Instance == null) return;
            MatchManager.Instance.ApplyMarks(teamIndex, autoIds, oneShotIds);
        }

        /// <summary>Сервер → одному клиенту: владельцы ВСЕХ точек линии. Индекс в массиве = индекс точки в Lane.</summary>
        public void PointTeamsResendSend(ulong clientID, int[] teamByPoint)
        {
            if (!IsSpawned || !ServerCanSend()) return;   // как у парных вещательных каналов (PointSync, WaveMarks, TechUpgrade, TeamCommand)
            PointTeamsResendClientRpc(teamByPoint, RpcTarget.Single(clientID, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void PointTeamsResendClientRpc(int[] teamByPoint, RpcParams rpcParams = default)
        {
            if (MatchManager.Instance == null || teamByPoint == null) return;
            for (int i = 0; i < teamByPoint.Length; i++)
                MatchManager.Instance.ApplyPointTeam(i, teamByPoint[i]);
        }

        /// <summary>
        /// Сервер → одному клиенту: текущий режим (Атака/Защита) одного ряда команды. Значение —
        /// (int)BottomTableAction, как в TeamCommandSend; клиент только подсвечивает кнопку.
        /// </summary>
        public void TeamCommandResendSend(ulong clientID, int teamIndex, int groupIndex, int action)
        {
            if (!IsSpawned || !ServerCanSend()) return;   // как у парных вещательных каналов (PointSync, WaveMarks, TechUpgrade, TeamCommand)
            TeamCommandResendClientRpc(teamIndex, groupIndex, action, RpcTarget.Single(clientID, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void TeamCommandResendClientRpc(int teamIndex, int groupIndex, int action, RpcParams rpcParams = default)
        {
            Presentation.UI?.ShowTeamCommand(teamIndex, groupIndex, (BottomTableAction)action); // [Interflow 2026-08-01 ADR-005]
        }
    }
}
