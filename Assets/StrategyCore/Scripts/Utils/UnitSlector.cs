using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Unit selector is used for passing around selection parameters for the unit.
    // Used for abilities, unit searches

    [System.Serializable]
    public struct UnitSelector
    {
        public bool isOwn; // Select owned units
        public bool isAlly; // Select ally units
        public bool isEnemy; // Select enemy units
        public bool isUnit; // Select units (non-building, non-static)
        public bool isBuilding; // Select buildings
        public bool isStaticDestructible; // Select static destructibles (Barrels)
        public bool isTree; // Similar to static destructible, but used by Workers to collect resources
        public bool isGround; // Select ground units
        public bool isWater; // Select water units
        public bool isAir; // Select air units
        public bool includeInvisible; // By default returns all visible units, should include invisible units?
        public bool includeInvulnerable; // Include Invulnerable units

        public UnitSelector(bool Own, bool Ally, bool Enemy, bool Unit, bool Building, bool StaticDestructible, bool isTree, bool Ground, bool Water, bool Air, bool includeInvisible, bool includeInvulnerable)
        {
            this.isOwn = Own;
            this.isAlly = Ally;
            this.isEnemy = Enemy;
            this.isUnit = Unit;
            this.isBuilding = Building;
            this.isStaticDestructible = StaticDestructible;
            this.isTree = isTree;
            this.isGround = Ground;
            this.isWater = Water;
            this.isAir = Air;
            this.includeInvisible = includeInvisible;
            this.includeInvulnerable = includeInvulnerable;
        }

        // Checks if target unit meets the UnitSelector criteria
        public static bool IsUnitCompatible(int playerID, Unit targetUnit, UnitSelector unitSelector)
        {
            if ((((unitSelector.isTree && targetUnit.unitType == UnitType.Tree) || (unitSelector.isStaticDestructible && targetUnit.unitType == UnitType.StaticDestructible)) && ((unitSelector.isAir && targetUnit.isAir) || (unitSelector.isGround && targetUnit.isGround) || (unitSelector.isWater && targetUnit.isWater))) // If static destructible, tree skip ownership checks.
            || (((unitSelector.isOwn && playerID == targetUnit.owner) || (unitSelector.isAlly && playerID != targetUnit.owner && (SlotManager.instance.playerTeam[playerID] == targetUnit.team || SlotManager.instance.playerTeam[(int)Players.NeutralPassive] == targetUnit.team)) || (unitSelector.isEnemy && SlotManager.instance.playerTeam[playerID] != targetUnit.team && SlotManager.instance.playerTeam[(int)Players.NeutralPassive] != targetUnit.team)) // Owned/Ally/Enemy check
            && ((unitSelector.isUnit && targetUnit.unitType == UnitType.Unit) || (unitSelector.isBuilding && targetUnit.unitType == UnitType.Building) || (unitSelector.isTree && targetUnit.unitType == UnitType.Tree)) // Units/Building/StaticDestructible/Tree check
            && ((unitSelector.isAir && targetUnit.isAir == true) || (unitSelector.isGround && targetUnit.isGround == true) || (unitSelector.isWater && targetUnit.isWater == true)) // Air/Ground/Water type check
            && (unitSelector.includeInvisible || (!targetUnit.isInvisible || (targetUnit.canBeSeen.Length != 0 && targetUnit.canBeSeen[SlotManager.instance.playerTeam[playerID]]))) // Invisible units
            && (unitSelector.includeInvulnerable || !targetUnit.isInvulnerable) // Invulnerable units
            ))
            {
                return true;
            }
            return false;
        }

        // Returns true if at least 1 selector is present
        public bool AnySelectors()
        {
            if (isOwn) return true;
            if (isAlly) return true;
            if (isEnemy) return true;
            if (isUnit) return true;
            if (isBuilding) return true;
            if (isStaticDestructible) return true;
            if (isTree) return true;
            if (isGround) return true;
            if (isWater) return true;
            if (isAir) return true;
            if (includeInvisible) return true;
            if (includeInvulnerable) return true;
            return false;
        }
    }
}
