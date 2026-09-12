using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    // Unit.AbilityActive.cs — активное умение и требования (ACTIVE ABILITY + UTILS). Вырезано 1:1 из Unit.Ability.cs (разрезка на partial-ы, правило 22).
    public partial class Unit
    {
        // ============================= ACTIVE ABILITY =============================

        // Метод SetActiveAbility (восстановление «активно исполняемого» умения из сохранения) СНЕСЁН
        // блоком Б6 (2026-09-04): он обслуживал только умения-каналы. Секция сохранения, которая его
        // звала, снесена там же — восстанавливать больше нечего.

        /// <summary>
        /// If active ability requires targetUnit we subscribe to reference changes of the target unit.
        /// </summary>
        /// <param name="newActive">New reference for activeAbilityUnit. Can be null.</param>
        public void AbilityUnitReferenceChange(Unit newActive)
        {
            activeAbilityUnit.OnReferenceChange -= AbilityUnitReferenceChange;

            // End the ability cast if null
            if (newActive == null)
            {
                // Only server can stop the ability
                if (!NetworkConnectionHandler.isClient)
                {
                    Idle();
                }
                return;
            }
            // New reference
            else
            {
                if (target != null && target == activeAbilityUnit) target = newActive;
                activeAbilityUnit = newActive;
                activeAbilityUnit.OnReferenceChange += AbilityUnitReferenceChange;
            }
        }

        /// <summary>
        /// Resets the ability state and syncs with the clients.
        /// </summary>
        public void ResetAbilityState(bool issuedByPlayer)
        {
            EndActiveAbility(true, true);
        }

        /// <summary>
        /// Ends active ability, optionally resetting ability state.
        /// </summary>
        /// <param name="reset">Should reset ability state?</param>
        /// <param name="calledByServer">Is command called by the server? When false does not sync with the clients, it means it should also be called by the clients locally.</param>
        public void EndActiveAbility(bool reset, bool calledByServer)
        {
            // Ветка завершения «активно исполняемого» умения (Deactivate + откат по обрыву) снесена блоком Б6
            // (2026-09-04) вместе с каналами: разовый каст ставит откат сам, в UseAbilityImmediately.

            // Reset the state
            if (reset)
            {
                OnCommand -= ResetAbilityState;
                if (activeAbilityUnit != null) activeAbilityUnit.OnReferenceChange -= AbilityUnitReferenceChange;

                activeAbility = null;
                activeAbilityIndex = -1;
                activeAbilityLevel = 0;
                activeAbilityUnit = null;
                activeAbilityLocation = Vector3.zero;
                activeAbilityRange = 0;
                activeAbilityCastTime = 0;
                activeAbilityDuration = 0;
                currentActionTime = 0;

                playCast = false;
                sendToClients = false;
            }

            // If server send signal to clients
            if (calledByServer && NetworkManager.Singleton.IsServer)
            {
                NetworkDataSync.Instance.AbilityStopSend(this);
            }
        }

        // ============================= UTILS =============================

        /// <summary>
        /// Returns true if requirements (lock, resource cost) for the given ability are not met.
        /// Цены в мане у умений нет (блок Б5, 2026-09-04): мана — шкала готовности авто-умения, см. AutoAbilityUser.
        /// </summary>
        /// <param name="player">Which player to check for resources.</param>
        /// <param name="abilityIndex">Global index of ability in the ability pool of the unit. You can get it by Utils.GetAbilityIndex.</param>
        /// <param name="isItem">Is it an item?</param>
        /// <param name="currentAbility">Optional parameter, when set ability index is not used to find the ability. Performance wise faster.</param>
        /// <returns></returns>
        private bool CheckAbilityItemRequirements(int player, int abilityIndex, bool isItem, Ability currentAbility = null)
        {
            if (isItem)
            {
                // No lock states for items and no multi-levels
                // No resource check for items when using them
                // No mana check: цена в мане снята (Б5)

                // No resource check for items
                // if (items[abilityIndex].cost.Length != 0)
                // {
                //     for (int i = 0; i < items[abilityIndex].cost[0].data.Length; i++)
                //     {
                //         if (!GameResources.Instance.CheckAmount(unitOwner, items[abilityIndex].cost[0].data[i]))
                //         {
                //             Presentation.NotifyMsg("Not enough " + items[abilityIndex].cost[0].data[i].type.name);
                //             return true;
                //         }
                //     }
                // }
            }
            else
            {
                if (currentAbility == null) currentAbility = Utils.GetAbilityByIndex(this, abilityIndex);

                // Check if ability is locked only when it is not herolevelable
                if (!currentAbility.heroLevelable && abilityLocked[abilityIndex]) return true;

                // No mana check: цена в мане снята (Б5)

                // Check resources — цена по уровню общей выборкой (Б8): нет строки — последняя заполненная
                // (раньше уровень выше длины массива делал каст бесплатным).
                var levelCost = InterflowAbility.LevelItem(currentAbility.cost, abilityLevel[abilityIndex]);
                if (levelCost != null && levelCost.data != null)
                {
                    for (int i = 0; i < levelCost.data.Length; i++)
                    {
                        if (!GameResources.Instance.CheckAmount(player, levelCost.data[i]))
                        {
                            Presentation.NotifyMsg("Not enough " + levelCost.data[i].type.displayName, owner, true);
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Subtracts costs of an ability or an item.
        /// </summary>
        /// <param name="player">Which player to subtract resources from.</param>
        /// <param name="abilityIndex">Global index of ability in the ability pool of the unit. You can get it by Utils.GetAbilityIndex.</param>
        /// <param name="isItem">Is it an item?</param>
        public void SubtractAbilityItemCost(int player, int abilityIndex, bool isItem)
        {
            if (isItem)
            {
                // No lock states for items and no multi-levels
                // No resource subtraction for items
                // No mana subtraction: цена в мане снята (Б5)

                // No resource subtraction
                // if (items[abilityIndex].cost.Length != 0)
                // {
                //     for (int i = 0; i < items[abilityIndex].cost[0].data.Length; i++)
                //     {
                //         GameResources.Instance.ChangeAmount(unitOwner, items[abilityIndex].cost[0].data[i], 1, true);
                //     }
                // }
            }
            else
            {
                Ability currentAbility = Utils.GetAbilityByIndex(this, abilityIndex);

                // Цены в мане у умения нет (Б5, целевая модель §9). Мана — шкала готовности авто-умения: его
                // срабатывание забирает ВСЮ ману носителя. Именно здесь, а не в компоненте: это точка, где ядро
                // списывало цену ПО ФАКТУ срабатывания — прерванный каст ману не тратит (решение Artsiom 2026-09-04).
                // На клиенте SetMP только локальный (без синка), сервер пришлёт своё значение штатным каналом маны.
                if (TryGetComponent(out AutoAbilityUser autoUser) && autoUser.IsAutoAbility(currentAbility))
                    SetMP(0f);

                // Subtract resources — та же выборка по уровню, что в проверке (Б8).
                var levelCost = InterflowAbility.LevelItem(currentAbility.cost, abilityLevel[abilityIndex]);
                if (levelCost != null && levelCost.data != null)
                {
                    for (int i = 0; i < levelCost.data.Length; i++)
                    {
                        GameResources.Instance.ChangeAmount(player, levelCost.data[i], 1, true, true);
                    }
                }
            }
        }
    }
}
