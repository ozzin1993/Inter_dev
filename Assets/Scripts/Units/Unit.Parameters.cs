// [Interflow fix 2026-06-27] Все подсказки [Tooltip] в этом файле локализованы на русский (правка ассета, разрешена Artsiom; только текст Tooltip). Оригинал EN: _BACKUP_TOOLTIPS/Scripts/Unit.cs. Реестр: wiki concepts/asset-fork-debt.
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    // Unit.Parameters.cs — параметры (PARAMETERS). Вырезано 1:1 из Unit.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class Unit
    {
        // ============================= PARAMETERS ==============================================================================

        /// <summary>
        /// Modifies the unit's maximum HP and adjusts current HP proportionally.
        /// </summary>
        /// <param name="amount">The value to adjust the maximum HP by (positive or negative).</param>
        public void ChangeMaxHP(float amount)
        {
            // 1st: We reverse the percentage change, then add amount, then recalculate percentage change
            maxHealth = ((maxHealth / passiveEffects.healthChange) + amount) * passiveEffects.healthChange;

            if (maxHealth <= 0) health = 0;
            else health = (health / (maxHealth - amount)) * maxHealth;

            OnHPChange?.Invoke();
        }

        /// <summary>
        /// Modifies the unit's maximum HP and adjusts current HP proportionally.
        /// </summary>
        /// <param name="percentage">The percentage to adjust the maximum HP by (positive or negative).</param>
        /// <param name="percentageChange">Indicates that change is percentage wise.</param>
        public void ChangeMaxHP(float percentage, bool percentageChange)
        {
            if (percentage < 0)
            {
                passiveEffects.healthChange /= (1 - percentage);
                maxHealth /= (1 - percentage);
                health /= (1 - percentage);
            }
            else
            {
                passiveEffects.healthChange *= (1 + percentage);
                maxHealth *= (1 + percentage);
                health *= (1 + percentage);
            }

            OnHPChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s current HP. Returns true if unit dies. If negative change, use GetDamage(), to properly kill the unit and do anything else that might be necessary.
        /// </summary>
        /// <param name="amount">HP change amount.</param>
        /// <param name="noSync">Should server sync the HP change with the clients.</param>
        /// <param name="countAsHeal">Считать ли ПОЛОЖИТЕЛЬНУЮ величину лечением. По умолчанию да —
        /// тогда любое новое место лечения попадёт под множитель получаемого лечения само, без правки
        /// вызова. Значение false передаёт только набор здоровья зданием при стройке и ремонте
        /// (<see cref="ConstructionUnit"/>): постройка лечением не считается (решение Artsiom 27.08.2026),
        /// ремонт тоже (решение Artsiom 30.08.2026). На отрицательные величины не влияет — урон
        /// и списание здоровья проходят мимо врезки нетронутыми.</param>
        /// <returns>Did you unit die.</returns>
        public bool ChangeHP(float amount, bool noSync = false, bool countAsHeal = true)
        {
            // [Interflow fix 2026-08-29 heal-through-receiver] Мёртвому здоровье не меняют (решение
            // Artsiom 30.08.2026). Это инвариант шагов 0–2 схемы «пакет и приёмник»: до приёмника
            // доходит только живой, потому что обращение к нему создаёт компонент на игровом объекте
            // (ReceiverEnsure, Unit.Combat.cs), а на трупе этого делать нельзя. У урона, контроля и
            // состояний отсев стоит в их собственных воронках; у лечения воронка — этот метод.
            // Сегодняшнего поведения гард НЕ меняет: ни один из существующих вызовов сюда с мёртвым
            // юнитом не доходит — урон отсекает GetDamage (Unit.Combat.cs, гард dead в воронке), списание здоровья —
            // PayHealth (InterflowAbility.cs:77) и PercentHpCost (:21, :32), лечение — свои проверки.
            // Гард страхует будущие места вызова. Возврат false читается как «этот вызов юнита
            // не убил»: мёртвый повторно не умирает и так (Die, Unit.Combat.cs:223).
            if (dead) return false;

            // [Interflow fix 2026-08-29 heal-through-receiver] Шаг 3 схемы «пакет и приёмник» (§12):
            // входящее лечение спрашивает приёмник и продолжает с той величиной, которую он вернул.
            // Приёмник РЕШАЕТ (множитель получаемого лечения из состояний), исполняет по-прежнему этот
            // метод — граница та же, что на шаге 1 у контроля.
            //
            // Врезка, а не отдельный Unit.Heal() с переводом всех вызовов (решение Artsiom 27.08.2026):
            // лечение — это ЛЮБАЯ положительная величина здесь, кроме набора здоровья зданием.
            // Умолчание true выбрано намеренно: забыть исключение можно только для стройки и ремонта,
            // а новое место лечения подхватывается само.
            //
            // Условие amount > 0 держит отрицательный путь нетронутым: урон (UnitReceiver.Receive),
            // PayHealth и PercentHpCost сюда не заходят. Ноль тоже не заходит — умножать нечего,
            // и приёмник зря не создаётся (полное восстановление на полигоне у целого юнита даёт 0).
            if (amount > 0 && countAsHeal)
            {
                HealPacket packet = new HealPacket(amount);
                amount = ReceiverEnsure().Receive(in packet);
            }

            health += amount;

            if (health > maxHealth)
            {
                health = maxHealth;
                OnHPChange?.Invoke();
            }
            else if (health < 0.05) // 0.05 is just a float error eliminator 
            {
                health = 0;
                OnHPChange?.Invoke();
                return true;
            }
            else
            {
                OnHPChange?.Invoke();
            }

            // Sync with clients
            if (!noSync && NetworkManager.Singleton.IsServer && !hpSync)
            {
                NetworkDataSync.Instance.hpChangedUnits.Add(netID);
                hpSync = true;
                NetworkDataSync.Instance.onHPCleared += HPSyncFalse;
            }

            return false;
        }

        /// <summary>
        /// Sets the unit`s current HP to value.
        /// </summary>
        /// <param name="value">New current HP value.</param>
        public void SetHP(float value)
        {
            health = value;

            OnHPChange?.Invoke();

            // Sync with clients
            if (NetworkManager.Singleton.IsServer && !hpSync)
            {
                NetworkDataSync.Instance.hpChangedUnits.Add(netID);
                hpSync = true;
                NetworkDataSync.Instance.onHPCleared += HPSyncFalse;
            }
        }

        /// <summary>
        /// Changes the unit`s HP regeneration. HP regeneration can go negative.
        /// </summary>
        /// <param name="amount">The value to adjust the HP regeneration by (Positive or Negative).</param>
        public void ChangeHealthRegen(float amount)
        {
            // 1st: We reverse the percentage change, then add amount, then recalculate percentage change
            healthRegen = ((healthRegen / passiveEffects.healthRegenChange) + amount) * passiveEffects.healthRegenChange;

            OnHPChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s HP regeneration percentage wise. HP regeneration can go negative.
        /// </summary>
        /// <param name="percentage">The percentage to adjust the HP regeneration by (Positive or Negative).</param>
        /// <param name="percentageChange">Indicates that change is percentage wise.</param>
        public void ChangeHealthRegen(float percentage, bool percentageChange)
        {
            if (percentage < 0)
            {
                passiveEffects.healthRegenChange /= (1 - percentage);
                healthRegen /= (1 - percentage);
            }
            else
            {
                passiveEffects.healthRegenChange *= (1 + percentage);
                healthRegen *= (1 + percentage);
            }

            OnHPChange?.Invoke();
        }

        // ------ MP ------

        /// <summary>
        /// Modifies the unit's maximum MP and adjusts current MP proportionally.
        /// </summary>
        /// <param name="amount">The value to adjust the maximum MP by (positive or negative).</param>
        public void ChangeMaxMP(float amount)
        {
            // 1st: We reverse the percentage change, then add amount, then recalculate percentage change
            maxMana = ((maxMana / passiveEffects.manaChange) + amount) * passiveEffects.manaChange;

            if (maxMana <= 0) mana = 0;
            else mana = (mana / (maxMana - amount)) * maxMana;

            OnMPChange?.Invoke();
        }

        /// <summary>
        /// Modifies the unit's maximum MP and adjusts current MP proportionally.
        /// </summary>
        /// <param name="percentage">The percentage to adjust the maximum MP by (positive or negative).</param>
        /// <param name="percentageChange">Indicates that change is percentage wise.</param>
        public void ChangeMaxMP(float percentage, bool percentageChange)
        {
            if (percentage < 0)
            {
                passiveEffects.manaChange /= (1 - percentage);
                maxMana /= (1 - percentage);
                mana /= (1 - percentage);
            }
            else
            {
                passiveEffects.manaChange *= (1 + percentage);
                maxMana *= (1 + percentage);
                mana *= (1 + percentage);
            }

            OnMPChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s current MP.
        /// </summary>
        /// <param name="amount">MP change amount.</param>
        /// <param name="noSync">Should server sync the MP change with the clients.</param>
        public void ChangeMP(float amount, bool noSync = false)
        {
            mana += amount;
            if (mana > maxMana) mana = maxMana;
            else if (mana < 0) mana = 0;
            OnMPChange?.Invoke();

            // Sync with clients
            if (!noSync && NetworkManager.Singleton.IsServer && !mpSync)
            {
                NetworkDataSync.Instance.mpChangedUnits.Add(netID);
                mpSync = true;
                NetworkDataSync.Instance.onMPCleared += MPSyncFalse;
            }
        }

        /// <summary>
        /// Sets the unit`s current MP to value.
        /// </summary>
        /// <param name="value">New current MP value.</param>
        public void SetMP(float value)
        {
            mana = value;

            OnMPChange?.Invoke();

            // Sync with clients
            if (NetworkManager.Singleton.IsServer && !mpSync)
            {
                NetworkDataSync.Instance.mpChangedUnits.Add(netID);
                mpSync = true;
                NetworkDataSync.Instance.onMPCleared += MPSyncFalse;
            }
        }

        /// <summary>
        /// Changes the unit`s MP regeneration. MP regeneration can go negative.
        /// </summary>
        /// <param name="amount">The value to adjust the MP regeneration by (Positive or Negative).</param>
        public void ChangeManaRegen(float amount)
        {
            // 1st: We reverse the percentage change, then add amount, then recalculate percentage change
            manaRegen = ((manaRegen / passiveEffects.manaRegenChange) + amount) * passiveEffects.manaRegenChange;

            OnMPChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s MP regeneration percentage wise. MP regeneration can go negative.
        /// </summary>
        /// <param name="percentage">The percentage to adjust the MP regeneration by (Positive or Negative).</param>
        /// <param name="percentageChange">Indicates that change is percentage wise.</param>
        public void ChangeManaRegen(float percentage, bool percentageChange)
        {
            if (percentage < 0)
            {
                passiveEffects.manaRegenChange /= (1 - percentage);
                manaRegen /= (1 - percentage);
            }
            else
            {
                passiveEffects.manaRegenChange *= (1 + percentage);
                manaRegen *= (1 + percentage);
            }

            OnMPChange?.Invoke();
        }

        // ------ ATTACK ------

        /// <summary>
        /// Changes the unit`s attack damage.
        /// </summary>
        /// <param name="amount">The value to adjust the attack damage by (Positive or Negative).</param>
        public void ChangeDamage(float amount)
        {
            // 1st: We reverse the percentage change, then add amount, then recalculate percentage change
            attackDamage = ((attackDamage / passiveEffects.damageChange) + amount) * passiveEffects.damageChange;

            // if (attackDamage < 0) attackDamage = 0; // Damage cant be lower than 0, Uncomment if needed

            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s attack damage percentage wise.
        /// </summary>
        /// <param name="percentage">The percentage to adjust the attack damage by (Positive or Negative).</param>
        /// <param name="percentageChange">Indicates that change is percentage wise.</param>
        public void ChangeDamage(float percentage, bool percentageChange)
        {
            if (percentage < 0)
            {
                passiveEffects.damageChange /= (1 - percentage);
                attackDamage /= (1 - percentage);
            }
            else
            {
                passiveEffects.damageChange *= (1 + percentage);
                attackDamage *= (1 + percentage);
            }

            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s attack range.
        /// </summary>
        /// <param name="amount">The value to adjust the attack range by (Positive or Negative).</param>
        public void ChangeAttackRange(float amount)
        {
            // 1st: We reverse the percentage change, then add amount, then recalculate percentage change
            attackRange = ((attackRange / passiveEffects.attackRangeChange) + amount) * passiveEffects.attackRangeChange;

            if (attackRange > unitRadius + Utils.stopDistanceOffset) melee = false;
            else
            {
                melee = true;
                attackRange = unitRadius + Utils.stopDistanceOffset;
            }

            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s attack range percentage wise.
        /// </summary>
        /// <param name="percentage">The percentage to adjust the attack range by (Positive or Negative).</param>
        /// <param name="percentageChange">Indicates that change is percentage wise.</param>
        public void ChangeAttackRange(float percentage, bool percentageChange)
        {
            if (percentage < 0)
            {
                passiveEffects.attackRangeChange /= (1 - percentage); ;
                attackRange /= (1 - percentage);
            }
            else
            {
                passiveEffects.attackRangeChange *= (1 + percentage);
                attackRange *= (1 + percentage);
            }

            if (attackRange > unitRadius) melee = false;
            else
            {
                melee = true;
                attackRange = unitRadius + Utils.stopDistanceOffset;
            }
            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s attack speed. Final attack speed value should not be lower than 0, preferably it should not go below ~0.1.
        /// </summary>
        /// <param name="amount">The value to adjust the attack speed by (Positive or Negative).</param>
        public void ChangeAttackSpeed(float amount)
        {
            // 1st: We reverse the percentage change, then add amount, then recalculate percentage change
            attackSpeed = ((attackSpeed / passiveEffects.attackSpeedChange) + amount) * passiveEffects.attackSpeedChange;

            // if (attackSpeed < 0) attackSpeed = 0;

            ChangeAttackAnimationSpeed();

            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s attack speed percentage wise. Final attack speed value should not be lower than 0, preferably it should not go below ~0.1.
        /// </summary>
        /// <param name="percentage">The percentage to adjust the attack speed by (Positive or Negative).</param>
        /// <param name="percentageChange">Indicates that change is percentage wise.</param>
        public void ChangeAttackSpeed(float percentage, bool percentageChange)
        {
            // Only attack speed is calculated this way, to prevent reaching 0 attackSpeed
            if (percentage < 0)
            {
                passiveEffects.attackSpeedChange *= (1 - percentage);
                attackSpeed *= (1 - percentage);
            }
            else
            {
                passiveEffects.attackSpeedChange /= (1 + percentage);
                attackSpeed /= (1 + percentage);
            }

            ChangeAttackAnimationSpeed();

            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the damage type of the unit.
        /// </summary>
        /// <param name="type">New damage type.</param>
        public void ChangeDamageType(DamageType type)
        {
            damageType = type;

            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the attack selector of the unit.
        /// </summary>
        /// <param name="attackSelectorNew">New attack selector.</param>
        public void ChangeAttackSelector(UnitSelector attackSelectorNew)
        {
            attackUnitSelector = attackSelectorNew;
            AttackSelectorInitialize();

            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the Multitarget attack modificator`s parameters.
        /// </summary>
        /// <param name="multitargetOn">Should multitarget be on.</param>
        /// <param name="multiCount">How many additional units beside the main one should this unit attack.</param>
        public void ChangeMultitarget(bool multitargetOn, int multiCount)
        {
            multiTarget = multitargetOn;
            multiTargetCount = multiCount;
            additionalTargets = new Unit[multiTargetCount + 1];
            if (!melee && attackType == AttackType.Continuous && projectileGO.GetComponent<VFXLine>())
            {
                // If attack type is continuous we instantiate attackVFX at launchSite(s)
                attackVFXLine = VFXLine.CreateVFX(projectileGO.GetComponent<VFXLine>(), this);
            }
        }

        /// <summary>
        /// Changes the Splash attack modificator`s parameters.
        /// </summary>
        /// <param name="splashOn">Should splash be on.</param>
        /// <param name="radius">Radius of the splash.</param>
        /// <param name="reduction">Reduction of the damage.</param>
        /// <param name="followTarget">For ranged only. Should the projectile follow the target.</param>
        public void ChangeSplash(bool splashOn, float radius, float reduction, bool followTarget)
        {
            isSplash = splashOn;
            splashRadius = radius;
            splashReduction = reduction;
            projectileFollowTarget = followTarget;
        }

        /// <summary>
        /// Changes the Bounce attack modificator`s parameters.
        /// </summary>
        /// <param name="count">How many times the bounce should happen. 0 means no bounce modificator.</param>
        /// <param name="range">Range of the bounce.</param>
        /// <param name="reducton">Damage reduction for each consequent target.</param>
        public void ChangeBounce(int count, float range, float reducton)
        {
            bounceCount = count;
            bounceRange = range;
            bounceReduction = reducton;
            if (!melee && attackType == AttackType.Continuous && projectileGO.GetComponent<VFXLine>())
            {
                // If attack type is continuous we instantiate attackVFX at launchSite(s)
                attackVFXLine = VFXLine.CreateVFX(projectileGO.GetComponent<VFXLine>(), this);
            }
        }

        // ------ ARMOR ------

        /// <summary>
        /// Changes the unit`s armor points.
        /// </summary>
        /// <param name="amount">The value to adjust the armor points by (Positive or Negative).</param>
        public void ChangeArmor(float amount)
        {
            // 1st: We reverse the percentage change, then add amount, then recalculate percentage change
            armor = ((armor / passiveEffects.armorChange) + amount) * passiveEffects.armorChange;

            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s armor points percentage wise.
        /// </summary>
        /// <param name="percentage">The percentage to adjust the armor points by (Positive or Negative).</param>
        /// <param name="percentageChange">Indicates that change is percentage wise.</param>
        public void ChangeArmor(float percentage, bool percentageChange)
        {
            if (percentage < 0)
            {
                passiveEffects.armorChange /= (1 - percentage);
                armor /= (1 - percentage);
            }
            else
            {
                passiveEffects.armorChange *= (1 + percentage);
                armor *= (1 + percentage);
            }

            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the armor type of the unit.
        /// </summary>
        /// <param name="type">New armor type.</param>
        public void ChangeArmorType(ArmorType type)
        {
            armorType = type;

            OnCharacteristicsChange?.Invoke();
        }

        // ------ MOVE ------

        /// <summary>
        /// Changes the unit`s movement speed.
        /// </summary>
        /// <param name="amount">The value to adjust the movement speed by (Positive or Negative).</param>
        public void ChangeMoveSpeed(float amount)
        {
            // 1st: We reverse the percentage change, then add amount, then recalculate percentage change
            moveSpeed = ((moveSpeed / passiveEffects.moveSpeedChange) + amount) * passiveEffects.moveSpeedChange;

            if (moveSpeed < 0) agent.speed = 0;
            else agent.speed = moveSpeed;
            if (invisibleAgent) invisibleAgent.speed = agent.speed;

            ChangeMoveSpeedAnimationSpeed();

            OnCharacteristicsChange?.Invoke();
        }

        /// <summary>
        /// Changes the unit`s movement speed percentage wise.
        /// </summary>
        /// <param name="percentage">The percentage to adjust the movement speed by (Positive or Negative).</param>
        /// <param name="percentageChange">Indicates that change is percentage wise.</param>
        public void ChangeMoveSpeed(float percentage, bool percentageChange)
        {
            if (percentage < 0)
            {
                passiveEffects.moveSpeedChange /= (1 - percentage);
                moveSpeed /= (1 - percentage);
            }
            else
            {
                passiveEffects.moveSpeedChange *= (1 + percentage);
                moveSpeed *= (1 + percentage);
            }

            if (moveSpeed < 0) agent.speed = 0;
            else agent.speed = moveSpeed;
            if (invisibleAgent) invisibleAgent.speed = agent.speed;

            ChangeMoveSpeedAnimationSpeed();

            OnCharacteristicsChange?.Invoke();
        }

        // ------ OTHER ------

        /// <summary>
        /// Changes the unit`s XP reward.
        /// </summary>
        /// <param name="amount">The value to adjust the XP reward by (Positive or Negative).</param>
        public void ChangeXpReward(float amount)
        {
            // 1st: We reverse the percentage change, then add amount, then recalculate percentage change
            xpReward = (int)(((xpReward / passiveEffects.xpRewardChange) + amount) * passiveEffects.xpRewardChange);
        }

        /// <summary>
        /// Changes the unit`s XP reward percentage wise.
        /// </summary>
        /// <param name="percentage">The percentage to adjust the XP reward by (Positive or Negative).</param>
        /// <param name="percentageChange">Indicates that change is percentage wise.</param>
        public void ChangeXpReward(float percentage, bool percentageChange)
        {
            if (percentage < 0)
            {
                passiveEffects.xpRewardChange /= (1 - percentage);
                xpReward = (int)(xpReward / (1 - percentage));
            }
            else
            {
                passiveEffects.xpRewardChange *= (1 + percentage);
                xpReward = (int)(xpReward * (1 + percentage));
            }
        }

        /// <summary>
        /// Changes the unit`s vision range.
        /// </summary>
        /// <param name="amount">The value to adjust the vision range by (Positive or Negative).</param>
        public void ChangeVisionRange(int visionRange)
        {
            if (this.visionRange == visionRange) return;
            FogOfWar.Instance.CellRemove(this);
            this.visionRange += visionRange;
            if (visionRange < 0) visionRange = 1;
            else if (visionRange > Utils.maxVisionRange) visionRange = Utils.maxVisionRange;
            FogOfWar.Instance.CellAssignment(this, true);
        }

    }
}
