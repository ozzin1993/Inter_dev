// [Interflow fix 2026-06-27] Все подсказки [Tooltip] в этом файле локализованы на русский (правка ассета, разрешена Artsiom; только текст Tooltip). Оригинал EN: _BACKUP_TOOLTIPS/Scripts/Unit.cs. Реестр: wiki concepts/asset-fork-debt.
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    // Unit.Processes.cs — процессы производства (PROCESSES). Вырезано 1:1 из Unit.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class Unit
    {
        // ============================= PROCESSES ==============================================================================

        /// <summary>
        /// Every update calculates the timer for processes, upon completion finishes the process and goes for the next one.
        /// </summary>
        void HandleProcesses()
        {
            if (canProcess)
            {
                // If there are processes
                if (activeProcess[0] != null)
                {
                    // If new process, set timer
                    if (currentProcessTimer == -1)
                    {
                        currentProcessTimer = activeProcess[0].castTime[processLevel[0]];
                    }

                    // Ongoing process, reduce timer
                    currentProcessTimer -= Time.deltaTime;

                    // Process finished
                    if (currentProcessTimer <= 0)
                    {
                        // Only server can finish the process
                        if (NetworkConnectionHandler.isClient) return;

                        // Process finished, do something
                        if (activeProcess[0] is UnitTraining)
                        {
                            if (waypointLocation != Vector2.zero) activeProcess[0].Use(this, this.owner, processLevel[0], new Vector3(waypointLocation.x, 0, waypointLocation.y));
                            else if (waypointUnit != null) activeProcess[0].Use(this, this.owner, processLevel[0], waypointUnit);
                            else activeProcess[0].Use(this, this.owner, processLevel[0]);
                        }
                        else
                        {
                            activeProcess[0].Use(this, this.owner, processLevel[0]);
                        }

                        ProcessMoveForward(0); // Move next process forward
                        OnProcessUpdate?.Invoke();

                        if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.FinishProcess(this);
                    }
                }
            }
        }

        /// <summary>
        /// Move next process forward, if it exists. Can be used to remove process at particular index.
        /// </summary>
        /// <param name="startIndex">Process index that is no longer there. For example: 0 means process 1 will take its place.</param>
        public void ProcessMoveForward(int startIndex)
        {
            if (startIndex == 0) currentProcessTimer = -1; // Timer Reset

            // Цикл доходит до ПОСЛЕДНЕЙ ячейки. Было «< maxProcessCount - 1»: при startIndex == 13 условие ложно
            // сразу, тело не выполнялось, и ячейка оставалась заполненной — а стоимость возвращается ДО сдвига
            // (CancelProcess ниже), поэтому повторная отмена того же номера возвращала её второй раз.
            // У последней ячейки соседа справа нет — чтение activeProcess[startIndex + 1] прикрыто границей.
            while (startIndex < GameManager.maxProcessCount)
            {
                if (startIndex + 1 < GameManager.maxProcessCount && activeProcess[startIndex + 1] != null)
                {
                    activeProcess[startIndex] = activeProcess[startIndex + 1];
                    activeProcess[startIndex + 1] = null;
                    processLevel[startIndex] = processLevel[startIndex + 1];
                    processLevel[startIndex + 1] = 0;
                    startIndex++;
                }
                else
                {
                    // No processes to move forward
                    activeProcess[startIndex] = null;
                    processLevel[startIndex] = 0;
                    break;
                }
            }

            OnRedrawAbilityView?.Invoke();
        }

        /// <summary>
        /// Cancels process at specified index.
        /// </summary>
        /// <param name="index">Index of the process.</param>
        /// <returns></returns>
        public bool CancelProcess(int index)
        {
            // Номер ячейки приходит от клиента (NetworkCommandSync.CancelProcessCommandServerRpc) и уходил
            // в массив без проверки: мусорное значение роняло сервер исключением там, где он должен был
            // молча отказать. Проверка стоит до ветки клиента — незачем гнать по сети заведомо негодный номер.
            // Очередь СОЗДАЁТСЯ только у юнита с canProcess (Unit.Init) — у остальных массив null.
            // Одной проверки диапазона мало: отмена, присланная на юнит без производства, роняла бы сервер
            // разыменованием null там же, где мусорный номер. AddProcess от этого прикрыт своим canProcess.
            if (activeProcess == null)
            {
                Debug.LogWarning("Отмена ячейки очереди отклонена: у юнита нет очереди производства. (CancelProcess Unit.Processes)");
                return false;
            }
            if (index < 0 || index >= GameManager.maxProcessCount)
            {
                Debug.LogWarning("Отмена ячейки очереди отклонена: номер " + index + " вне диапазона 0.." +
                                 (GameManager.maxProcessCount - 1) + ". (CancelProcess Unit.Processes)");
                return false;
            }

            if (NetworkConnectionHandler.isClient)
            {
                NetworkCommandSync.Instance.CancelProcessCommandSend(this, index);
                return false;
            }

            if (activeProcess[index] != null)
            {
                // If process in a research, cancel its processing status
                if (activeProcess[index] is Research)
                {
                    Research research = (Research)activeProcess[index];
                    TechnologyManager.Instance.TechFinishedProcessing(research.unlockTech[processLevel[index]], owner);
                }

                // Return the cost
                if (activeProcess[index].cost.Length > processLevel[index]) GameResources.Instance.ChangeAmount(owner, activeProcess[index].cost[processLevel[index]].data, 1, false, true);

                ProcessMoveForward(index);

                OnProcessUpdate?.Invoke();

                // Send to clients
                if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.CancelProcess(this, index);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Adds the process.
        /// </summary>
        /// <param name="abilityIndex">Global index of ability in the ability pool of the unit. You can get it via Utils.GetAbilityIndex.</param>
        /// <param name="isItem">Is the process an item?</param>
        /// <returns></returns>
        public bool AddProcess(int abilityIndex, bool isItem)
        {
            if (NetworkConnectionHandler.isClient)
            {
                NetworkCommandSync.Instance.AddProcessCommandSend(this, abilityIndex, isItem);
                return false;
            }

            if (canProcess)
            {
                Ability currentProcess = (isItem) ? items[abilityIndex] : abilities[abilityIndex]; // Utils.GetAbilityByIndexName(this, abilityPath, out int abilityIndex);
                int currentProcessLevel = (isItem) ? 0 : abilityLevel[abilityIndex];

                // Check the costs
                if (CheckAbilityItemRequirements(owner, abilityIndex, isItem)) return false;

                // If upgrade we do some checks
                if (currentProcess is Research)
                {
                    Research upgradeAbility = (Research)currentProcess;

                    // If this upgrade is already being processed do not allow to process it twice
                    if (upgradeAbility.unlockTech.Length > currentProcessLevel && !TechnologyManager.Instance.IsTechBeingProcessed(upgradeAbility.unlockTech[currentProcessLevel], owner))
                    {
                        // Set upgrade status to being processed, will make sure this upgrade can not be used twice
                        TechnologyManager.Instance.TechBeingProcessed(upgradeAbility.unlockTech[currentProcessLevel], owner);
                    }
                    else
                    {
                        if (upgradeAbility.abilityName.Length > currentProcessLevel) Presentation.NotifyMsg(upgradeAbility.abilityName[currentProcessLevel] + " is already being researched", owner, true);
                        else Presentation.NotifyMsg(upgradeAbility.abilityName[upgradeAbility.abilityName.Length - 1] + " is already being researched", owner, true);

                        return false;
                    }
                }

                // Get process count
                int index = 0;
                while (activeProcess[index] != null)
                {
                    index++;

                    if (index == GameManager.maxProcessCount)
                    {
                        // No more processes can be added
                        return false;
                    }
                }

                // Add process
                activeProcess[index] = currentProcess;
                processLevel[index] = currentProcessLevel;

                // Subtract costs
                SubtractAbilityItemCost(owner, abilityIndex, isItem);

                // Item charge decrease
                if (isItem) ItemChargesChange(abilityIndex);

                OnProcessUpdate?.Invoke();
                OnRedrawAbilityView?.Invoke();

                // Send to clients
                if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.AddProcess(this, index, abilityIndex, isItem);
                return true;
            }
            else
            {
                Presentation.NotifyMsg("Can`t add process, this unit does not support it. Tick canProcess box in the unit parameters", owner, true);
                return false;
            }
        }

    }
}
