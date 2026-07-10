# Project Overview

- **Game Title:** Interflow (StrategyCore-based RTS)
- **High-Level Concept:** Real-time strategy game with networked multiplayer, resource management, units, and tech trees.
- **Players:** Single player + networked multiplayer (Unity Netcode)
- **Render Pipeline:** Built-in
- **Target Platform:** StandaloneWindows64
- **Unity Version:** 6000.4.7f1

# Problem Statement

`NullReferenceException` thrown during scene load:

```
NullReferenceException: Object reference not set to an instance of an object
StrategyCore.GameResources.ChangeAmount (...) at Assets/StrategyCore/Scripts/Resources/GameResources.cs:86
StrategyCore.GameResources.Awake () at Assets/StrategyCore/Scripts/Resources/GameResources.cs:46
```

## Root Cause

`GameResources.Awake()` (line 46) calls `ChangeAmount(p, gameResources[i])` while
initializing each player's resources. Inside `ChangeAmount`, line 86 runs:

```csharp
if (SlotManager.instance.currentPlayer == player) UIManager.instance.UpdateResourceTab(resourceID);
```

- `SlotManager.instance` is valid (guaranteed by line 27 of `Awake`).
- `currentPlayer` defaults to `0`, and the loop starts at `p = 0`, so the condition
  is `true` on the first iteration.
- This dereferences **`UIManager.instance`, which is still `null`** because
  `UIManager.Awake()` (where `instance` is assigned) has not yet run. This is a
  **script execution order** issue.

The author recently added this UI refresh call and added a guard inside
`UpdateResourceTab` (line 2756: `if (resourceTab == null) return; // GameResource calls it in Awake...`),
but the guard sits one level too deep — it does not protect against `UIManager.instance`
itself being null.

# Chosen Fix (Option 1 — Null-check at call site)

Make the UI update at line 86 of `GameResources.cs` conditional on
`UIManager.instance` being non-null. This is the safest, minimal change and is
consistent with the existing `resourceTab == null` guard already inside
`UpdateResourceTab`. When `UIManager` later initializes, it refreshes all resource
tabs via `UpdateResourceTabAll`/`UpdateResourceTab` from its own Start, so no data
is lost by skipping the UI update during `GameResources.Awake()`.

# Key Asset & Context

- **File to modify:** `Assets/StrategyCore/Scripts/Resources/GameResources.cs`
- **Line 86 (current):**
  ```csharp
  if (SlotManager.instance.currentPlayer == player) UIManager.instance.UpdateResourceTab(resourceID);
  ```
- **Line 86 (new):**
  ```csharp
  if (UIManager.instance != null && SlotManager.instance.currentPlayer == player) UIManager.instance.UpdateResourceTab(resourceID);
  ```
- **Related (no change needed):** `UIManager.cs` line 2756 already guards `resourceTab == null`; UIManager refreshes resource tabs in its own Start.

# Implementation Steps

### Step 1 — Add null guard for UIManager.instance
- **Description:** In `Assets/StrategyCore/Scripts/Resources/GameResources.cs`,
  update line 86 inside `ChangeAmount(int, ResourceWrapper, ...)` to check
  `UIManager.instance != null` before calling `UpdateResourceTab`.
- **Assigned role:** developer
- **Dependencies:** None
- **Parallelizable:** No (single isolated change)

# Verification & Testing

1. **Compile check:** Project compiles with no errors in the Unity Console.
2. **Enter Play Mode** from `Assets/StrategyCore/Scenes/Menu.unity` and start a game
   so the gameplay scene loads. Confirm the previous `NullReferenceException` at
   `GameResources.cs:86` no longer appears in the Console.
3. **Resource UI correctness:** Once in-game, verify the resource tab in the HUD
   shows the correct starting amounts for the current player (confirming UIManager's
   own Start-time refresh populates the values).
4. **Regression check:** Spend/earn a resource in-game and confirm the resource tab
   updates live (ensuring `ChangeAmount` still updates the UI after UIManager exists).
