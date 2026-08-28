using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    public class TechnologyManager : MonoBehaviour
    {
        public static TechnologyManager Instance { get; private set; }
        // Convert to hashset?

        // Tech tree holder for all players
        // First index is playerNumber, second index is Technology, valus is true if Unlocked and false if Locked
        public Dictionary<Technology, bool>[] TechTree;// = new Dictionary<Technology, bool>[Enum.GetNames(typeof(Players)).Length]; // TechTree[player][Technology] = Unlocked true/false
        // When new technology is being processed, we should keep track of that to maku sure we can not research the same thing
        public Dictionary<Technology, bool>[] TechTreeProcessing;// = new Dictionary<Technology, bool>[Enum.GetNames(typeof(Players)).Length]; // TechTree[player][Technology] = Processing true/false

        public Action[] OnTechUnlock = new Action[Enum.GetNames(typeof(Players)).Length]; // Each unit will subscribe to this action and unlock its abilities if they required the tech
        public Action[] OnTechLock = new Action[Enum.GetNames(typeof(Players)).Length]; // Each unit will subscribe to this action and lock its abilities if they require the tech

        // Holds the amount of units that unlock technology upon their creation, to make sure that upon their death technology locks back
        // Dictionary<technologyID, counter> Unit that unlock the technology will add the counter indicating that the tech is available for the player, when int reaches 0 technology is no longer available
        // Example: unlockingCount[playerID][technologyID] returns the amount of units unlocking the said technolgoy
        public static Dictionary<int, int>[] unlockingCount; 

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            Initialize();
        }

        public void Initialize()
        {
            // Instantiate TechTree
            Technology[] techs = Resources.LoadAll<Technology>("");
            TechTree = new Dictionary<Technology, bool>[Enum.GetNames(typeof(Players)).Length];
            TechTreeProcessing = new Dictionary<Technology, bool>[Enum.GetNames(typeof(Players)).Length];
            unlockingCount = new Dictionary<int, int>[Enum.GetNames(typeof(Players)).Length];

            for (int i = 0; i < TechTree.Length; i++)
            {
                TechTree[i] = new Dictionary<Technology, bool>();
                TechTreeProcessing[i] = new Dictionary<Technology, bool>();
                unlockingCount[i] = new Dictionary<int, int>();

                for (int t = 0; t < techs.Length; t++)
                {
                    TechTree[i].Add(techs[t], false);
                    TechTreeProcessing[i].Add(techs[t], false);
                }
            }
        }

        // This method will unlock technology
        public void UnlockTech(Technology tech, int playerIndex)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (tech == null) return;

            bool wasUnlocked = false;

            if (!TechTree[playerIndex][tech])
            {
                TechTree[playerIndex][tech] = true;
                wasUnlocked = true;

                // Send a message previously unknown tech was unlocked
                Instance.OnTechUnlock[playerIndex]?.Invoke();
            }

            // Shared tech
            if (tech.shared)
            {
                int[] allies = SlotManager.Instance.GetPlayerAllies(playerIndex);

                for (int i = 0; i < allies.Length; i++)
                {
                    if (!TechTree[allies[i]][tech])
                    {
                        TechTree[allies[i]][tech] = true;
                        wasUnlocked = true;

                        // Send a message previously unknown tech was unlocked
                        Instance.OnTechUnlock[allies[i]]?.Invoke();
                    }
                }
            }

            if (wasUnlocked)
                if (NetworkManager.Singleton.IsServer)
                    NetworkDataSync.Instance.TechnologySync(playerIndex, tech.id, true);
        }

        // Upon unit creation if it has unlockTech, unlocks the tech and adds unit to the list
        public void UnlockTech(Unit unit)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (unit.unlockTech == null) return;

            bool wasUnlocked = false;

            if (!TechTree[unit.owner][unit.unlockTech])
            {
                TechTree[unit.owner][unit.unlockTech] = true;
                wasUnlocked = true;

                // Send a message previously unknown tech was unlocked
                Instance.OnTechUnlock[unit.owner]?.Invoke();
            }

            // Shared tech
            if (unit.unlockTech.shared)
            {
                int[] allies = SlotManager.Instance.GetPlayerAllies(unit.owner);

                for (int i = 0; i < allies.Length; i++)
                {
                    if (!TechTree[allies[i]][unit.unlockTech])
                    {
                        TechTree[allies[i]][unit.unlockTech] = true;
                        wasUnlocked = true;

                        // Send a message previously unknown tech was unlocked
                        Instance.OnTechUnlock[allies[i]]?.Invoke();
                    }
                }
            }

            if (wasUnlocked)
                if (NetworkManager.Singleton.IsServer)
                    NetworkDataSync.Instance.TechnologySync(unit.owner, unit.unlockTech.id, true);

            // Add to unlock counter
            if (unlockingCount[unit.owner].ContainsKey(unit.unlockTech.id)) unlockingCount[unit.owner][unit.unlockTech.id]++;
            else unlockingCount[unit.owner][unit.unlockTech.id] = 1;
        }

        // This method is used to lock the technology
        public void LockTech(Technology tech, int playerIndex)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (tech == null) return;

            TechTree[playerIndex][tech] = false;

            // Shared tech
            if (tech.shared)
            {
                int[] allies = SlotManager.Instance.GetPlayerAllies(playerIndex);

                for (int i = 0; i < allies.Length; i++)
                {
                    TechTree[allies[i]][tech] = false;

                    // Send a message tech was locked
                    Instance.OnTechLock[allies[i]]?.Invoke();
                }
            }

            if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.TechnologySync(playerIndex, tech.id, false);

            // Send a message tech was locked
            Instance.OnTechLock[playerIndex]?.Invoke();
        }

        // When unit dies this method is called, it will lock the technology if no units unlocking it are alive
        public void LockTeck(Unit unit)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (unit.unlockTech == null) return;

            // Remove from the list
            unlockingCount[unit.owner][unit.unlockTech.id]--;
            if (unlockingCount[unit.owner][unit.unlockTech.id] > 0)
            {
                // It means there are still units unlockig the technology
                return;
            }

            // Shared tech. Test ally units
            if (unit.unlockTech.shared)
            {
                int[] allies = SlotManager.Instance.GetPlayerAllies(unit.owner);

                for (int i = 0; i < allies.Length; i++)
                {
                    if (unlockingCount[allies[i]].ContainsKey(unit.unlockTech.id) && unlockingCount[allies[i]][unit.unlockTech.id] > 0)
                    {
                        // It means there are still units unlockig the technology
                        return;
                    }
                }

                // Lock for allies
                for (int i = 0; i < allies.Length; i++)
                {
                    TechTree[allies[i]][unit.unlockTech] = false;

                    // Send a message tech was locked
                    Instance.OnTechLock[allies[i]]?.Invoke();
                }
            }

            // No unit unlocking the tech has been found, lock it for owner
            TechTree[unit.owner][unit.unlockTech] = false;
            if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.TechnologySync(unit.owner, unit.unlockTech.id, false);

            // Send a message tech was locked
            Instance.OnTechLock[unit.owner]?.Invoke();
        }

        // Takes an array of required techTypes and returns true if all techTypes are unlocked
        public bool isUnlocked(Technology[] tech, int playerIndex)
        {
            if (tech == null) return true;

            for (int i = 0; i < tech.Length; i++)
            {
                if (!TechTree[playerIndex][tech[i]])
                {
                    return false;
                }
            }
            return true;
        }

        public bool isUnlocked(Technology tech, int playerIndex)
        {
            if (tech == null) return true;

            if (!TechTree[playerIndex][tech])
            {
                return false;
            }
            return true;
        }

        // Set technology processing status. This will help to avoid researching the same technology two times.
        public void TechBeingProcessed(Technology tech, int playerIndex)
        {
            TechTreeProcessing[playerIndex][tech] = true;

            // Shared tech. Set for allies
            if (tech.shared)
            {
                int[] allies = SlotManager.Instance.GetPlayerAllies(playerIndex);

                for (int i = 0; i < allies.Length; i++)
                {
                    TechTreeProcessing[allies[i]][tech] = true;
                }
            }
        }

        public void TechFinishedProcessing(Technology tech, int playerIndex)
        {
            TechTreeProcessing[playerIndex][tech] = false;

            // Shared tech. Set for allies
            if (tech.shared)
            {
                int[] allies = SlotManager.Instance.GetPlayerAllies(playerIndex);

                for (int i = 0; i < allies.Length; i++)
                {
                    TechTreeProcessing[playerIndex][tech] = false;
                }
            }
        }

        public bool IsTechBeingProcessed(Technology tech, int playerIndex)
        {
            return TechTreeProcessing[playerIndex][tech];
        }

        public Technology GetTechByID(int id)
        {
            foreach (KeyValuePair<Technology, bool> tech in TechTree[0])
            {
                if (tech.Key.id == id) return tech.Key;
            }
            return null;
        }
    }
}