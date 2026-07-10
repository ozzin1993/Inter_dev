# Fix: "SERVER" button in main menu does nothing

# Project Overview
- Game Title: Interflow (StrategyCore-based RTS)
- High-Level Concept: A networked top-down strategy game built on Unity Netcode for GameObjects (NGO), with a UI Toolkit main menu offering Host / Join / Server / Load.
- Players: Networking multiplayer (NGO). The "SERVER" option launches a dedicated server (no local player).
- Target Platform: StandaloneWindows64
- Render Pipeline: Built-in
- UI Framework: UI Toolkit (UIDocument + UXML/USS)
- Unity Version: 6000.4.7f1

# Problem Statement
Clicking the **SERVER** button in the main menu does nothing. No server starts and no console output appears.

# Root Cause (verified)
- `Assets/StrategyCore/UI/Menu.uxml` (line 14) defines a `Label` named **`ServerGame`** (text "SERVER"), styled like the other menu buttons.
- `Assets/StrategyCore/Scripts/Menu/UIManagerMenu.cs`:
  - In `Start()` (lines 56–66) the buttons **HostGame, JoinGame, Load, Quit, SaveButton** are each wired with `RegisterCallback<ClickEvent>(...)`.
  - A handler `ServerButton(ClickEvent evt)` exists (lines 134–138) that correctly calls `ServerBootstrap.LaunchServer()`.
  - **However, `ServerGame` is never queried and `ServerButton` is never registered.** The handler is dead code, so clicks on the SERVER label are ignored.
- Secondary inactive path: `Assets/StrategyCore/Scripts/ServerBootstrap.cs` can inject its own runtime SERVER button via `injectMenuButton`, but it defaults to `false` and would early-out anyway (line 213 skips if a `ServerGame` element already exists). So it is not a factor.

# Chosen Approach
Wire up the existing `ServerGame` UXML element to the existing `ServerButton` handler in `UIManagerMenu.Start()`, mirroring exactly how the other menu buttons are registered. This is the smallest, most consistent fix and uses the handler that already exists.

Rejected alternative: enabling `ServerBootstrap.injectMenuButton`. This would not work as-is (it skips when `ServerGame` already exists) and creates two parallel button systems. Less clean and inconsistent with the rest of the menu wiring.

# Key Asset & Context
- `Assets/StrategyCore/Scripts/Menu/UIManagerMenu.cs` — add field + registration. Existing handler to keep:
  ```csharp
  void ServerButton(ClickEvent evt)
  {
      chatBox.Clear();
      ServerBootstrap.LaunchServer();
  }
  ```
- `Assets/StrategyCore/UI/Menu.uxml` — element `name="ServerGame"` (no change needed).
- `Assets/StrategyCore/Scripts/ServerBootstrap.cs` — `public static void LaunchServer()` is the entry point (logs an error if no `ServerBootstrap` instance is present in the scene).

# Implementation Steps

## Step 1 — Add the serverButton field
- **Description**: In `UIManagerMenu.cs`, add a `VisualElement serverButton;` field alongside the other Menu button fields (near lines 18–22).
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: No (same file as Step 2)

## Step 2 — Register the ServerGame button callback
- **Description**: In `UIManagerMenu.Start()`, within the `// ---------- Menu ----------` block (after the `joinButton` registration, lines 59–60, to match the menu's visual order Host→Join→Server), add:
  ```csharp
  serverButton = UIDocument.rootVisualElement.Q("Menu").Q("ServerGame");
  serverButton.RegisterCallback<ClickEvent>(ServerButton);
  ```
- **Assigned role**: developer
- **Dependencies**: Depends on Step 1
- **Parallelizable**: No

## Step 3 — Confirm a ServerBootstrap instance exists in the Menu scene
- **Description**: `LaunchServer()` is static and forwards to a scene instance; if none exists it logs an error and does nothing. Verify a GameObject in `Assets/StrategyCore/Scenes/Menu.unity` has the `ServerBootstrap` component (likely `ProjectManager`). If absent, add the component to the appropriate manager GameObject so the click actually starts a server. (Note: `ServerBootstrap` uses `DontDestroyOnLoad`, so it must live in the Menu scene.)
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: Yes (independent of Steps 1–2)

# Verification & Testing
1. **Compile**: No errors in the Unity Console after the edit.
2. **Manual — happy path**: Enter Play Mode on `Menu.unity`, click **SERVER**.
   - Expected: Console shows `[ServerBootstrap] Сервер запущен (StartServer). Ожидание игроков: 2.` and the chat box clears.
   - `NetworkManager.Singleton.IsListening` becomes true (server mode).
3. **Manual — missing instance**: If no `ServerBootstrap` is in the scene, clicking SERVER should log `[ServerBootstrap] Нет инстанса ServerBootstrap в сцене.` — confirms the wiring works and points to Step 3.
4. **Regression**: Verify Host, Join, Load, Quit, Save buttons still behave as before (callbacks unaffected).
5. **Double-click / re-entry**: Clicking SERVER again while already listening logs `[ServerBootstrap] Сеть уже запущена ... — пропуск.` (no duplicate server).
