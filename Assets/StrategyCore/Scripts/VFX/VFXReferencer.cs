using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Used by units to know which ability added the VFX
    // Assigned to any VFX element that should be added to unit (Aura, Toggle)
    public class VFXReferencer : MonoBehaviour
    {
        [HideInInspector] public int id;

        [Tooltip("If this VFX element is using line renderer or line particle system for continuous ability, add VFXLine component and assign this variable")]
        public VFXLine vfxLine;
    }
}
