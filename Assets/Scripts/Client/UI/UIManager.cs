using Camera_TopDownNS;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace StrategyCore
{
    public partial class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        // Camera view rectangle on minimap
        public SpriteRenderer minimapRect;

        // Components
        [SerializeField] Camera_TopDownNS.Camera_TopDown cameraRig;
        public UIDocument uiDocument;
PlayerControl pc;

        // UI Elements
        VisualElement abilityView;
        ScrollView abilityScrollView;

        VisualElement unitBox;
        VisualElement unitIcon;
        Label unitName;
        Label invulnerable;

        ProgressBar manaBar;
        Label manaRegen;
        ProgressBar healthBar;
        Label healthRegen;

        ProgressBar lifetimeBar;

        // Descriptor
        VisualElement descriptor;
        Label descriptorName;
        Label descriptorHotkey;
        Label descriptorLevel;
        Label descriptorDescription;
        Label descriptorBottomText;
        VisualElement descriptorCostBox;
        VisualElement descriptorUsageCost;
        VisualElement descriptorResourceCost;
        VisualElement descriptorLockbox;
        VisualElement descriptorLockNames;

        VisualElement cancelButton;
        VisualElement returnButton;
        VisualElement levelButton;
        VisualElement shopButton;

        VisualElement processesUI;
        Label processTimer;

        VisualElement transportUI;

        // MiniMap
        VisualElement miniMap;
        List<MiniMapPinger> pingElements = new List<MiniMapPinger>();

        // Chat
        private bool allyChat = false;
        [HideInInspector] public bool chatON = false;
        TextField msgInput;
        VisualElement chatBox;
        float currentChatTime;
        float chatTime = 10; // Chat will be shown for this amount of time

        // Resources
        VisualElement resourceTab;

        VisualElement debugWindow;

        VisualElement statusWindow;

        // Unit stats
        VisualElement attackElement;
        Label attackValue;
        VisualElement armorElement;
        Label armorValue;
        VisualElement attributesElement;
        ProgressBar XpBar;

        // Commands
        VisualElement moveCommand;
        VisualElement stopCommand;
        VisualElement holdCommand;
        VisualElement attackCommand;

        // Inventory
        int currentItemIndex = 0;
        [HideInInspector] public bool isDraggingItem = false;

        VisualElement inventoryUI;
        VisualElement inventoryView;
        VisualElement inventoryMenu;
        VisualElement inventoryDragIcon;

        // Msg
        Label notifyMsg;
        float currentMsgTime;
        float msgTime = 3; // Messages will be shown for 3 seconds

        // Level up
        [HideInInspector] public bool isLeveling = false; // When true it means player is selecting ability to level or learn

        // Open containers ability index. To make sure back button works correctly.
        List<int> openContainersIndex = new List<int>();

        // [Interflow fix 2026-06-26 путь1] Готовность UI: становится true в КОНЦЕ Start (после построения rootVisualElement).
        // На headless-сервере Start выходит по init-гейту раньше → остаётся false, серверо-достижимые методы no-op по своему состоянию.
        private bool presentationReady;

    }
}
