using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Enables/Destroys object when it becomes visible
    // Used by abilities that spawn certain effects.
    // These effects can be enabled and disabled based on FoW cell visibility
    // Also used by static copy when the host unit is dead

    public class VFXEnabler : MonoBehaviour
    {
        public Coordinate FoWCell; // Current FoWCell
        [HideInInspector] public bool destroyUponDiscovery; // For static objects

        private void Start()
        {
            FogOfWar.Instance.CellAssignVFX(this);
            if (!FogOfWar.Instance.IsVisible(FoWCell, SlotManager.Instance.currentTeam) && !destroyUponDiscovery) Disable();
        }

        private void OnDestroy()
        {
            FogOfWar.Instance.CellRemoveVFX(this);
        }

        public void Enable()
        {
            if (destroyUponDiscovery) Destroy(gameObject);
            else if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        public void Disable()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
    }
}
