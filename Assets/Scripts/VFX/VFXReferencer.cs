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

        // [Interflow fix 2026-09-13 vfx-remove-by-prefab] Префаб-источник этого экземпляра: несколько VFX
        // умений на одном юните делят id == 0 (id присваивает только реестр Resources/VFX), RemoveVFX
        // раньше находил «первый попавшийся» вместо нужного. Заполняется в Unit.AddVFX после Instantiate.
        [HideInInspector] public VFXReferencer source;

        [Tooltip("If this VFX element is using line renderer or line particle system for continuous ability, add VFXLine component and assign this variable")]
        public VFXLine vfxLine;
    }
}
