using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System;
using System.Text;

namespace StrategyCore
{
    // For syncing various game data from server to clients

    public partial class NetworkDataSync : NetworkBehaviour // [Interflow fix 2026-07-09] partial для наших расширений NetworkDataSync.*.cs
    {
        public static NetworkDataSync Instance { get; private set; }

        // Sending and Receiving scene data
        private const int CHUNK_SIZE = 1000; // Save file will be divided into chunks to not overflow the buffer
        private Dictionary<int, byte[]> receivedChunks = new Dictionary<int, byte[]>();
        private int totalChunksExpected = -1;

        // Position sync
        public List<UInt16> directPositionSyncList = new List<UInt16>(); // List of networkIDs to sync their position directly
        public List<UInt16> positionSyncList = new List<UInt16>(); // List of networkIDs to sync their position
        public List<UInt16> removeSyncList = new List<UInt16>(); // List of networkIDs that will be sent to clients, indicating previously send position was their last position

        // MSG/Minimap ping
        public int currentPingCount; // To prevernt spamming
        public int maxPingPerTick = 3; // Spam prevention

        // HP/MP/XP Sync
        public Action onHPCleared; // Units that have their hp changed subscribe to this to clear their ID added flag
        public Action onMPCleared; // Units that have their mp changed subscribe to this to clear their ID added flag
        public Action onXPCleared; // Units that have their xp changed subscribe to this to clear their ID added flag
        // [Interflow fix 2026-09-09 characteristics-sync] Четвёртый канал семьи: характеристики.
        public Action onCharCleared; // Units that have their characteristics changed subscribe to this to clear their ID added flag

        // Server
        int tickCount = 0;

        void Awake()
        {
            if (Instance == null) Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            // Идемпотентность: снятие перед подпиской — повторный вход не плодит дубликаты
            NetworkManager.NetworkTickSystem.Tick -= ProjectWideTick;
            NetworkManager.NetworkTickSystem.Tick += ProjectWideTick;
            if (SlotManager.Instance.gameStarted == GameState.Started)
            {
                NetworkManager.NetworkTickSystem.Tick -= Tick;
                NetworkManager.NetworkTickSystem.Tick += Tick;
            }
            base.OnNetworkSpawn();
        }

        public override void OnNetworkDespawn()
        {
            // Отписка от сетевого тика — иначе часы продолжают дёргать умерший объект (утечка + ошибки)
            if (NetworkManager != null && NetworkManager.NetworkTickSystem != null)
            {
                NetworkManager.NetworkTickSystem.Tick -= ProjectWideTick;
                NetworkManager.NetworkTickSystem.Tick -= Tick;
            }
            base.OnNetworkDespawn();
        }

        // ProjectWide Tick
        public void ProjectWideTick()
        {
            currentPingCount = 0;
        }

        // InGame Tick
        public void Tick()
        {
            if (!SlotManager.Instance.gameOn) return;

            if (IsServer)
            {
                tickCount++;
                SyncPositionSend();
                SyncDirectPositionSend();
                ResourceSend();
                XPChangeSend();

                // [Interflow fix 2026-09-09 force-sync-miss] Сравнение НЕ на равенство: счётчик
                // увеличивается ДО проверки, поэтому принудительный запрос (ForceSync ставит 10)
                // проскакивал равенство — 11 никогда не равно 10, и периодическая отправка здоровья
                // и маны выпадала до конца матча. Лечила только пауза: ForceSend обнуляет счётчик.
                if (tickCount >= 10)
                {
                    tickCount = 0;
                    HPChangeSend();
                    MPChangeSend();
                    CharacteristicsChangeSend();
                }
            }
        }

        // Forces the server to sync with clients by sending all the data 
        public void ForceSend()
        {
            tickCount = 0;
            SyncPositionSend();
            SyncDirectPositionSend();
            ResourceSend();
            XPChangeSend();
            HPChangeSend();
            MPChangeSend();
            CharacteristicsChangeSend();
        }

        // Forces the game to sync MP and HP at the next tick
        public void ForceSync()
        {
            tickCount = 10;
        }
    }
}
