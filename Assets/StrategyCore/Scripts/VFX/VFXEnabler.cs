using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
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
            FogOfWar.instance.CellAssignVFX(this);
            if (!FogOfWar.instance.IsVisible(FoWCell, SlotManager.instance.currentTeam) && !destroyUponDiscovery) Disable();
        }

        private void OnDestroy()
        {
            FogOfWar.instance.CellRemoveVFX(this);
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
