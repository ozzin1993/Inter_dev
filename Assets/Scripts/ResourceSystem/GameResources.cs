using System;
using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    public class GameResources : MonoBehaviour
    {
        public static GameResources Instance { get; private set; }

        [Tooltip("Define what resources are in the game with their corresponding initial amounts. For limited resources, it sets the initial limit.")]
        public ResourceWrapper[] gameResources;

        // Store resource amounts of players. playerID + resourceID * gameResources.Length.
        [HideInInspector] public int[] playerResources;
        // Store current resource limits for the resources that need it. playerResourceLimits[player][Resource] = current limit for the player
        [HideInInspector] public int[] playerResourceLimits;


        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            // SlotManager обязан быть поднят РАНЬШЕ — порядок задаёт SceneStartup.

            // Initialize resources
            playerResources = new int[Enum.GetNames(typeof(StrategyCore.Players)).Length * gameResources.Length];
            playerResourceLimits = new int[Enum.GetNames(typeof(StrategyCore.Players)).Length * gameResources.Length];

            for (int i = 0; i < gameResources.Length; i++)
            {
                for (int p = 0; p < Enum.GetNames(typeof(Players)).Length; p++)
                {
                    // Initialize playerResourceLimits
                    if (gameResources[i].type.limited)
                    {
                        // Change limit
                        playerResourceLimits[i + p * gameResources.Length] = gameResources[i].value;
                    }
                    else
                    {
                        // Regular resource
                        ChangeAmount(p, gameResources[i]);
                    }
                }
            }
        }

        // No checks are performed. Use CheckAmount() beforehand.
        public void ChangeAmount(int player, ResourceWrapper[] resource, float multiplier = 1, bool decrease = false, bool calledByServer = false)
        {
            if (calledByServer && NetworkConnectionHandler.isClient) return; // Only server can change the value

            for (int i = 0; i < resource.Length; i++)
            {
                ChangeAmount(player, resource[i], multiplier, decrease, calledByServer);
            }
        }

        public void ChangeAmount(int player, ResourceWrapper resource, float multiplier = 1, bool decrease = false, bool calledByServer = false)
        {
            if (calledByServer && NetworkConnectionHandler.isClient) return; // Only server can change the value

            int resourceID = GetResourceID(resource);
            if (resourceID == -1) return; // No such resource is defined in the game

            if (resource.type.limited)
            {
                // Limited resource - For limited resource decrease will add to the current value, increase will subtract it
                if (decrease) playerResources[resourceID + player * gameResources.Length] += (int)(resource.value * multiplier);
                else playerResources[resourceID + player * gameResources.Length] -= (int)(resource.value * multiplier);

                // [Interflow fix 2026-08-01 limited-res-sync] РАСХОД лимитного ресурса (лидерство) клиентам НЕ уходил:
                // блок синка стоял только в ветке нелимитных. В хост-модели это не замечалось (хост = локальный игрок),
                // на выделенном сервере клиент опирался на собственный локальный подсчёт и расходился с сервером.
                // Теперь сервер — единственный источник истины: шлём и расход, и лимит (LimitedResourceSend).
                if (calledByServer && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && NetworkDataSync.Instance != null)
                    NetworkDataSync.Instance.LimitedResourceSend(player, resourceID);
            }
            else
            {
                // Standard resource
                if (decrease) playerResources[resourceID + player * gameResources.Length] -= (int)(resource.value * multiplier);
                else playerResources[resourceID + player * gameResources.Length] += (int)(resource.value * multiplier);

                // Send info to clients
                if (calledByServer && NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.ResourceSendAdd(player, resourceID, playerResources[resourceID + player * gameResources.Length]);
            }

            if (SlotManager.Instance.currentPlayer == player) Presentation.UI?.UpdateResourceTab(resourceID);
        }

        // Changes the resource limit for the specified player
        public void ChangeLimit(int player, ResourceWrapper resourceLimited, bool subtract = false, bool calledByServer = false)
        {
            if (calledByServer && NetworkConnectionHandler.isClient) return; // Only server can change the value

            int resourceID = GetResourceID(resourceLimited);
            if (resourceID == -1) return; // No such resource is defined in the game

            if (subtract) playerResourceLimits[resourceID + player * gameResources.Length] -= resourceLimited.value;
            else
            {
                playerResourceLimits[resourceID + player * gameResources.Length] += resourceLimited.value;

                // Check max limit and if current limit is more, cap it
                if (resourceLimited.type.maxLimit != 0 && playerResourceLimits[resourceID + player * gameResources.Length] > resourceLimited.type.maxLimit)
                {
                    playerResourceLimits[resourceID + player * gameResources.Length] = resourceLimited.type.maxLimit;
                }
            }

            // Send info to clients
            // [Interflow fix 2026-08-01 limited-res-sync] Было: лимит слался через ResourceSendAdd, а ResourceSend
            // при отправке ПЕРЕЧИТЫВАЛ playerResources (расход) — клиенту в лимит прилетал расход. Теперь явный канал.
            if (calledByServer && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && NetworkDataSync.Instance != null)
                NetworkDataSync.Instance.LimitedResourceSend(player, resourceID);

            if (SlotManager.Instance.currentPlayer == player) Presentation.UI?.UpdateResourceTab(GetResourceID(resourceLimited));
        }

        // Returns true if player has enough amount of this resource
        public bool CheckAmount(int player, ResourceWrapper resource, float multiplier = 1)
        {
            int resourceID = GetResourceID(resource);
            if (resourceID == -1) return false; // No such resource is defined in the game

            if (resource.type.limited)
            {
                // Limited
                if (playerResources[resourceID + player * gameResources.Length] + (int)(resource.value * multiplier) <= playerResourceLimits[resourceID + player * gameResources.Length])
                {
                    return true;
                }
            }
            else
            {
                // Standard
                if ((int)(resource.value * multiplier) <= playerResources[resourceID + player * gameResources.Length])
                {
                    return true;
                }
            }

            return false;
        }

        public bool CheckAmount(int player, ResourceWrapper[] resource, float multiplier = 1, bool ignoreLimited = false)
        {
            for (int i = 0; i < resource.Length; i++)
            {
                if (ignoreLimited && resource[i].type.limited) continue;
                if (!CheckAmount(player, resource[i], multiplier)) return false;
            }
            return true;
        }

        public int GetResourceID(ResourceWrapper resource)
        {
            for (int i = 0; i < gameResources.Length; i++)
            {
                if (gameResources[i].type == resource.type) return i;
            }
            return -1;
        }
    }
}
