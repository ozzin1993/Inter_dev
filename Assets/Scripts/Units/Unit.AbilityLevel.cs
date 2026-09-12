using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    // Unit.AbilityLevel.cs — уровни умений (LEVEL UP). Вырезано 1:1 из Unit.Ability.cs (разрезка на partial-ы, правило 22).
    public partial class Unit
    {
        // ============================= LEVEL UP =============================

        /// <summary>
        /// Command used to increase the level of an ability. Unit needs to be Levelling Unit and have abilityPoints.  
        /// </summary>
        /// <param name="abilityIndex">Global ability index. You can get it with Utils.GetAbilityIndex.</param>
        /// <returns></returns>
        public bool LevelUpAbilityCommand(Ability ability, int abilityIndex)
        {
            // If client, send command to server
            if (NetworkConnectionHandler.isClient)
            {
                NetworkCommandSync.Instance.LevelUpAbility(this, ability, abilityIndex);
                return false;
            }

            // Границы пула умений: номер приходит от клиента, а ниже он идёт в три массива подряд.
            if (abilities == null || abilityLevel == null || abilityLocked == null
                || abilityIndex < 0 || abilityIndex >= abilities.Length
                || abilityIndex >= abilityLevel.Length || abilityIndex >= abilityLocked.Length) return false;

            // Умение берём СВОЁ по индексу, а не присланное: право проверялось по abilities[abilityIndex],
            // а Lock/Unlock уходили у присланного объекта — у пассивки это реальная выдача и снятие свойств
            // чужого умения. Образец правильного подхода — RestoreAbilityLevel ниже.
            Ability owned = abilities[abilityIndex];
            if (owned == null) return false;

            // If leveling unit, ability not locked and still has not reached the maximum level
            if (levelingUnit && !abilityLocked[abilityIndex] && abilityLevel[abilityIndex] < owned.maxLevels - 1 && levelingUnit.abilityPoints > 0)
            {
                LevelUpAbility(owned, abilityIndex);

                // If level up successful server will inform clients that ability level has changed
                if (NetworkManager.Singleton.IsServer)
                {
                    NetworkDataSync.Instance.LevelUpAbilitySend(this, owned, abilityIndex);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Increases the level of an ability. Assumes unit is Levelling Unit and has abilityPoints.
        /// </summary>
        /// <param name="abilityIndex">Global ability index. You can get it with Utils.GetAbilityIndex.</param>
        public void LevelUpAbility(Ability ability, int abilityIndex)
        {
            // Снимаем умение на СТАРОМ уровне и выдаём на новом (Б9): пассивка держит выданное состояние по уровню,
            // а её Unlock идемпотентен — без снятия она осталась бы на прежних числах.
            if (!ability.isItem) ability.Lock(this, this.owner, abilityLevel[abilityIndex]);

            abilityLevel[abilityIndex]++; // Increase ability level
            levelingUnit.abilityPoints--; // Decrease ability points

            AllAbilityLockLevelsCalculate();

            if (!ability.isItem) ability.Unlock(this, this.owner, abilityLevel[abilityIndex]); // Выдаём на новом уровне
        }

        /// <summary>
        /// Сервер: поставить улучшаемому умению героя сохранённый уровень (восстановление после смерти, блок Б9).
        /// Очки не тратит и пределов не проверяет — уровень уже был оплачен до гибели. Снимает старое состояние и выдаёт новое.
        /// </summary>
        /// <param name="abilityIndex">Глобальный индекс умения в пуле юнита.</param>
        /// <param name="level">Сохранённый уровень умения.</param>
        public void RestoreAbilityLevel(int abilityIndex, int level)
        {
            if (NetworkConnectionHandler.isClient) return;                       // состояние мира — сервер (правило 6)
            if (abilities == null || abilityIndex < 0 || abilityIndex >= abilityLevel.Length) return;
            if (level <= abilityLevel[abilityIndex]) return;                     // не ниже текущего: восстановление только вверх

            Ability ability = (abilityIndex < abilities.Length) ? abilities[abilityIndex] : null;
            if (ability == null || ability.isItem) return;

            ability.Lock(this, this.owner, abilityLevel[abilityIndex]);
            abilityLevel[abilityIndex] = level;

            AllAbilityLockLevelsCalculate();

            ability.Unlock(this, this.owner, level);

            // Клиент строит уровни умений из префаба и о восстановлении сам не узнал бы: его тултипы, значки и фильтр
            // прокачки разошлись бы с сервером навсегда (правило 6).
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && NetworkDataSync.Instance != null)
                NetworkDataSync.Instance.AbilityLevelSetSend(this, abilityIndex, level);
        }
    }
}
