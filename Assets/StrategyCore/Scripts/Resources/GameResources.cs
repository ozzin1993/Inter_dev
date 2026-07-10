using System;
using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    public class GameResources : MonoBehaviour
    {
        public static GameResources instance;

        [Tooltip("Define what resources are in the game with their corresponding initial amounts. For limited resources, it sets the initial limit.")]
        public ResourceWrapper[] gameResources;

        // Store resource amounts of players. playerID + resourceID * gameResources.Length.
        [HideInInspector] public int[] playerResources;
        // Store current resource limits for the resources that need it. playerResourceLimits[player][Resource] = current limit for the player
        [HideInInspector] public int[] playerResourceLimits;


        void Awake()
        {
            if (instance == null)
            {
                instance = this;
            }

            if (SlotManager.instance == null) GameObject.Find("ProjectManager").GetComponent<SlotManager>().InstanceSet();

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
            }
            else
            {
                // Standard resource
                if (decrease) playerResources[resourceID + player * gameResources.Length] -= (int)(resource.value * multiplier);
                else playerResources[resourceID + player * gameResources.Length] += (int)(resource.value * multiplier);

                // Send info to clients
                if (calledByServer && NetworkManager.Singleton.IsServer) NetworkDataSync.instance.ResourceSendAdd(player, resourceID, playerResources[resourceID + player * gameResources.Length]);
            }

            if (UIManager.instance != null && SlotManager.instance.currentPlayer == player) UIManager.instance.UpdateResourceTab(resourceID);
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
            if (calledByServer && NetworkManager.Singleton.IsServer) NetworkDataSync.instance.ResourceSendAdd(player, resourceID, playerResourceLimits[resourceID + player * gameResources.Length]);

            if (SlotManager.instance.currentPlayer == player) UIManager.instance.UpdateResourceTab(GetResourceID(resourceLimited));
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
