using UnityEngine;

namespace StrategyCore
{
    [CreateAssetMenu(fileName = "UpgradeBuilding", menuName = "StrategyCore/Abilities/UpgradeBuilding")]
    public class UpgradeBuilding : Ability
    {
        // If needed - Cost for limited resources must be set manually depending on the casting unit. 
        // For example if upgradeUnit consumes 5 food, the casting unit consumes 3 - the cost for this ability should be 2 food.

        // This is a base class for UpgradeBuilding abilities
        public override AbilityType type { get { return AbilityType.Active; } } // Specify type

        [Header("Ability specific")]
        [Tooltip("To which unit we should upgrade this building")]
        public Unit upgradeUnit;
        [Tooltip("Amount of time needed to upgrade this building")]
        public float upgradeTime;

        // This function is called before the Use() to run custom requirements scheck
        public override bool Check(Unit castingUnit, int castingPlayer, int level)
        {
            // On clients we forcefully use the ability, on the server we check and send to clients if usable
            if (NetworkConnectionHandler.isClient) return true;

            if (castingUnit.activeProcess[0] != null)
            {
                Debug.LogWarning("Somehow UpgradeBuilding was called on a unit with active process, it is not supposed to happen!");
                return false;
            }

            if (castingUnit.transportUnit && castingUnit.transportUnit.units.Count > 0)
            {
                Debug.LogWarning("Somehow UpgradeBuilding was called on a unit with transported units inside, it is not supposed to happen!");
                return false;
            }

            ConstructionUnit cu = castingUnit.GetComponent<ConstructionUnit>();
            if (cu == null || !cu.completed)
            {
                Debug.LogWarning("Upgrade building was called on a unit without ConstructionUnit! You must add it!");
                return false;
            }

            // We should not transform when the unit is already transformed
            if (castingUnit.polymorphed)
            {
                UIManager.instance.ShowNotifyMsg("Unit can not currently be upgraded!", castingUnit.owner, true);
                return false;
            }
            return true;
        }

        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            ConstructionUnit cu = castingUnit.GetComponent<ConstructionUnit>();
            // Cost defined
            if (cost != null && cost.Length > 0) cu.UpgradeBuilding(upgradeUnit, upgradeTime, cost[0].data);
            else cu.UpgradeBuilding(upgradeUnit, upgradeTime, null);
        }
    }
}
