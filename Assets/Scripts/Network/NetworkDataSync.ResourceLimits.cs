using System;
using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    // [Interflow fix 2026-08-01 limited-res-sync] Явный канал синка ЛИМИТНЫХ ресурсов (лидерство/Supply).
    // Зачем: штатный ResourceChangeClientRpc для лимитных синкал ТОЛЬКО лимит («current value is changed
    // locally» — расчёт расхода лежал на клиенте), а сам расход не слался вовсе; при этом ResourceSend
    // перечитывал playerResources, из-за чего в лимит клиенту прилетал ЧУЖОЙ смысл (расход).
    // В хост-модели это не было видно (хост = локальный игрок). На выделенном сервере клиент показывал
    // застывшее/неверное лидерство. Теперь сервер шлёт ОБА числа (расход + лимит) одним пакетом.
    // Правило 6: рассылает только сервер, применяет клиент. Наш файл — ассет не трогаем.
    public partial class NetworkDataSync
    {
        /// Сервер → владельцу слота: актуальные расход и лимит ОДНОГО лимитного ресурса.
        public void LimitedResourceSend(int player, int resourceID)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            if (!IsSpawned) return; // сеть ещё/уже не поднята — молча пропускаем (как прочие релеи хаба)
            if (GameResources.instance == null || SlotManager.instance == null) return;
            if (player < 0 || player >= SlotManager.instance.slotType.Length) return;

            // Только реальным подключённым игрокам (не боты, не сам сервер) — как в штатном ResourceSend.
            if (SlotManager.instance.slotType[player] != SlotType.Player) return;
            int id = SlotManager.instance.playerID[player];
            if (id <= 0) return;

            int len = GameResources.instance.gameResources.Length;
            if (resourceID < 0 || resourceID >= len) return;
            int idx = resourceID + player * len;

            LimitedResourceClientRpc(new Vector3Int(resourceID, GameResources.instance.playerResources[idx],
                                                    GameResources.instance.playerResourceLimits[idx]),
                                     RpcTarget.Single((ulong)id, RpcTargetUse.Temp));
        }

        /// Сервер → всем игрокам: полный снимок ВСЕХ лимитных ресурсов (старт матча, догоняющий синк).
        public void LimitedResourceSyncAll()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            if (GameResources.instance == null || SlotManager.instance == null) return;

            int len = GameResources.instance.gameResources.Length;
            for (int p = 0; p < SlotManager.instance.slotType.Length; p++)
            {
                if (SlotManager.instance.slotType[p] != SlotType.Player) continue;
                for (int r = 0; r < len; r++)
                {
                    if (GameResources.instance.gameResources[r].type == null) continue;
                    if (!GameResources.instance.gameResources[r].type.limited) continue;
                    LimitedResourceSend(p, r);
                }
            }
        }

        // Клиент: применяем авторитетные расход и лимит, обновляем вкладку ресурсов.
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void LimitedResourceClientRpc(Vector3Int data, RpcParams rpcParams)
        {
            if (GameResources.instance == null || SlotManager.instance == null) return;
            int len = GameResources.instance.gameResources.Length;
            int resourceID = data.x;
            if (resourceID < 0 || resourceID >= len) return;

            int idx = resourceID + SlotManager.instance.currentPlayer * len;
            if (idx < 0 || idx >= GameResources.instance.playerResources.Length) return;

            GameResources.instance.playerResources[idx] = data.y;      // расход (usage)
            GameResources.instance.playerResourceLimits[idx] = data.z; // лимит
            Presentation.UI?.UpdateResourceTab(resourceID);
        }
    }
}
