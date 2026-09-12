using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    // Defines a unit that is capable of carrying other units.

    public class TransportUnit : MonoBehaviour
    {
        [Tooltip("How many units it can carry. Maximum is 14 because the UI element (Processes) can only show 14 slots. Defined by GameManager.cs:maxProcessCount")]
        public int capacity = 14;
        [Tooltip("Should units carried die or be extracted when the transport dies")]
        public bool unitsDie = false;

        // Technical
        Unit thisUnit; // the unit reference of this script
        [HideInInspector] public List<Unit> units; // Units that are currently being carried

        [HideInInspector] public int currentCapacity = 0;
        [HideInInspector] public Unit currentUnitToTake = null;
        [HideInInspector] public Vector3 currentLocationToDisembark = Vector3.zero;

        public Action transportChange; // Called when unit is taken in or taken out

        // Called by Initialize() of thisUnit
        public void Initialize()
        {
            thisUnit = GetComponent<Unit>();
            units = new List<Unit>(capacity);
            if (!unitsDie) thisUnit.OnDie += DisembarkAll;
            else thisUnit.OnDie += KillAll;
            thisUnit.OnDie += Die;
        }

        void Die(Unit unitThatDies, int playerKiller, Unit unitKiller, bool rewards)
        {
            // Clean up
            thisUnit.OnDie -= Die;
            thisUnit.OnDie -= DisembarkAll;
            thisUnit.OnDie -= KillAll;

            thisUnit.OnFollowReach -= Embark;
            thisUnit.OnPositionReach -= Disembark;
            thisUnit.OnCommand -= TriggerReset;
        }

        // Takes unit into the transport
        public void Embark(Unit unitToTake)
        {
            if (unitToTake != null)
            {
                if (currentCapacity + unitToTake.transportWeight > capacity)
                {
                    Presentation.NotifyMsg("Not enough space!", thisUnit.owner, true);
                }
                else if (unitToTake.stunned || unitToTake.muted || unitToTake.disarmed || unitToTake.polymorphed)
                {
                    Presentation.NotifyMsg("This unit can not embark currently!", thisUnit.owner, true);
                }
                else
                {
                    units.Add(unitToTake);
                    unitToTake.Disable();
                    currentCapacity += unitToTake.transportWeight;

                    // Sync with clients
                    if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.EmbarkSync(thisUnit, unitToTake);
                }

                transportChange?.Invoke();
                thisUnit.OnRedrawAbilityView?.Invoke(); // Because of UpgradeBuilding (can not upgrade while units inside)
            }

            TriggerReset(false);
        }

        public void Embark()
        {
            Embark(currentUnitToTake);
        }

        public void DisembarkAll(Unit unitThatDies, int playerKiller, Unit unitKiller, bool rewards)
        {
            if (NetworkConnectionHandler.isClient) return;
            currentLocationToDisembark = thisUnit.transform.position;
            Disembark();
        }

        // Takes units out of the transport at location
        public void Disembark()
        {
            // We must disembark units at the location
            int terrainType = Utils.GetTerrainType(new Vector2(currentLocationToDisembark.x, currentLocationToDisembark.z));

            if (terrainType == -1)
            {
                Presentation.NotifyMsg("Can not unload at this location!", thisUnit.owner, true);
                return;
            }

            bool unitsOut = false;
            for (int i = units.Count - 1; i >= 0; i--)
            {
                if (terrainType == 1 || units[i].isAir || (terrainType == 0 && units[i].isGround) || (terrainType == 2 && units[i].isWater))
                {
                    if (Disembark(i, currentLocationToDisembark, false))
                    {
                        unitsOut = true;
                    }
                }
            }

            if (unitsOut)
            {
                transportChange?.Invoke();
                thisUnit.OnRedrawAbilityView?.Invoke(); // Because of UpgradeBuilding (can not upgrade while units inside)
            }
            else if (units.Count > 0)
            {
                Presentation.NotifyMsg("Can not unload at this location!", thisUnit.owner, true);
            }
        }

        // Disembark specified unit at location
        public bool Disembark(int unitIndex, Vector3 location, bool invoke = true)
        {
            if (NetworkConnectionHandler.isClient)
            {
                // Отправка снята: приёмник закрыт — прямого управления юнитами нет (решение Artsiom 09.09).
                // Клиент выходит здесь как и раньше; локально команда состояния не меняла, а сервер
                // те же вызовы делает у себя сам (ConstructionUnit, ResourceUnit и менеджеры матча).
                return true;
            }

            Vector3 spawnPoint = Utils.CircleCheck(new Vector2(location.x, location.z), thisUnit.unitRadius, units[unitIndex].unitRadius, units[unitIndex].isGround, units[unitIndex].isWater, units[unitIndex].isAir, 1);
            if (spawnPoint == Vector3.zero)
            {
                if (invoke)
                    Presentation.NotifyMsg("Can not unload at this location!", thisUnit.owner, true);

                return false;
            }
            else
            {
                units[unitIndex].transform.position = spawnPoint;
                units[unitIndex].Enable();
                units[unitIndex].Idle();
                currentCapacity -= units[unitIndex].transportWeight;

                units.RemoveAt(unitIndex);
                if (invoke)
                {
                    transportChange?.Invoke();
                    thisUnit.OnRedrawAbilityView?.Invoke(); // Because of UpgradeBuilding (can not upgrade while units inside)
                }

                // Sync with clients
                if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.DisembarkSync(thisUnit, unitIndex, spawnPoint);
            }
            return true;
        }

        // Network: called on clients
        public void DisembarkInternal(int unitIndex, Vector3 location)
        {
            units[unitIndex].transform.position = location;
            units[unitIndex].Enable();
            units[unitIndex].Idle();
            currentCapacity -= units[unitIndex].transportWeight;

            units.RemoveAt(unitIndex);
            transportChange?.Invoke();
            thisUnit.OnRedrawAbilityView?.Invoke(); // Because of UpgradeBuilding (can not upgrade while units inside)
        }

        public void DisembarkInternal(int unitsOut, Vector3[] locations)
        {
            for (int i = 0; i < unitsOut; i++)
            {
                units[units.Count - 1 - i].transform.position = locations[i];
                units[units.Count - 1 - i].Enable();
                units[units.Count - 1 - i].Idle();
                currentCapacity -= units[units.Count - 1 - i].transportWeight;
            }

            if (unitsOut == units.Count)
            {
                // We disembarked all the units
                units.Clear();
            }
            else
            {
                // Not enough space for disembarking for the rest of the units
                for (int q = 0; q < unitsOut; q++) units.RemoveAt(units.Count - 1);
            }
            transportChange?.Invoke();
            thisUnit.OnRedrawAbilityView?.Invoke(); // Because of UpgradeBuilding (can not upgrade while units inside)
        }

        public void TriggerReset(bool issuedByPlayer)
        {
            currentUnitToTake = null;
            currentLocationToDisembark = Vector3.zero;
            thisUnit.OnFollowReach -= Embark;
            thisUnit.OnPositionReach -= Disembark;
            thisUnit.OnCommand -= TriggerReset;
        }

        // If units should die upon death of the transport, we kill them all
        public void KillAll(Unit unitThatDies, int playerKiller, Unit unitKiller, bool rewards)
        {
            for (int i = 0; i < units.Count; i++)
            {
                units[i].Die(playerKiller, unitKiller, rewards, true, true);
            }
        }
    }
}
