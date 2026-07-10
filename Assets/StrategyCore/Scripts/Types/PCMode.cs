using UnityEngine;

namespace StrategyCore
{
    // PlayerControl.cs modes
    public enum PCMode
    {
        ResetNextFrame, // Resets to default state next frame
        Default, // Default state
        Area, // Area selection
        Unit, // Unit selection
        Position, // Position selection
        DragDrop, // Item drag&drop
        Placement, // Building placement
        ShopUnit, // Shopping unit change
        AttackMove // AttackMove command
    }

    // Old int mode - for reference , ignore
    // -1 - reset to 0 next frame
    // 0 - Nothing
    // 1 - Area
    // 2 - Unit
    // 3 - Position
    // 4 - Item drag&drop
    // 5 - Building placement
    // 6 - Shopping unit change
    // 7 - AttackMove command
}
