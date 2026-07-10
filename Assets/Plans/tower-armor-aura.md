# Project Overview
- Game Title: StrategyCore Demo
- High-Level Concept: RTS core framework for Unity.
- Players: Single player / Multiplayer.
- Render Pipeline: Built-in.

# Game Mechanics
## Core Gameplay Loop
- Building structures, training units, researching technologies, and engaging in combat.
## Controls and Input Methods
- RTS style mouse controls (selection, movement, attack commands).

# UI
- Standard RTS HUD with resources, unit selection info, and command grid.

# Key Asset & Context
- `EffectorAura.cs`: A script to handle aura logic via effectors (already created).
- `ArmorBuffEffector`: A ScriptableObject to define the +Armor bonus.
- `ArmorAuraAbility`: A ScriptableObject of type `EffectorAura` to distribute the buff.
- `ArrowTower.prefab`: The target building to receive the aura.

# Implementation Steps
1. **Create Armor Effector**:
   - Use `Create -> StrategyCore -> Effectors -> Create`.
   - Set `DisplayName` to "Armor Buff".
   - Set `Duration` to 0.4 (aura standard).
   - Set `PassiveEffectsOn` to true.
   - Set `ArmorChange` in `PassiveEffects` to 5.
2. **Create Armor Aura Ability**:
   - Use `Create -> StrategyCore -> Abilities -> EffectorAura`.
   - Set `AbilityName` to "Armor Aura".
   - Set `Radius` to 10.
   - Set `UnitSelector` to `Ally` and `Player`.
   - Assign the `ArmorBuffEffector` to the `Effectors` list.
3. **Assign to Tower**:
   - Open `ArrowTower.prefab` in inspector.
   - Find `Unit` component.
   - Add `ArmorAuraAbility` to the `Abilities` array.
   - Set `Ability Level` for this index to 0.

# Verification & Testing
- Enter Play Mode.
- Select the Arrow Tower.
- Move a friendly unit (e.g., Footman) near the tower.
- Check the Footman's armor in the UI to see if it increased by 5.
- Move the Footman away and verify the armor returns to normal.
