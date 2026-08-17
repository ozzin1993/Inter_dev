using Camera_TopDownNS;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // UIManager.Lifecycle.cs — жизненный цикл (Awake/Start/Update/ViewRect). Вырезано 1:1 из UIManager.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class UIManager
    {
        void Awake()
        {
            if (instance == null)
            {
                instance = this;
            }
        }

        void Start()
        {
            if (ServerBootstrap.IsHeadlessServer) return;   // [Interflow fix 2026-06-20] init-гейт: на headless UI не строим (rootVisualElement не запрашиваем)
            // Components
            pc = PlayerControl.instance;

            // Inventory dragDrop
            PlayerControl.coreInput.Main.SelectHold.performed += InventoryDragStart;

            // ABILITY VIEW
            abilityView = uiDocument.rootVisualElement.Query("RightArea").First();
            abilityScrollView = (ScrollView)uiDocument.rootVisualElement.Query("RightArea").First().Query("ScrollView").First();

            // INVENTORY
            inventoryUI = uiDocument.rootVisualElement.Query("InventoryBox").First();
            inventoryView = uiDocument.rootVisualElement.Query("Items").First();
            inventoryMenu = uiDocument.rootVisualElement.Query("InventoryMenu").First();
            inventoryDragIcon = uiDocument.rootVisualElement.Query("InventoryDragIcon").First();
            inventoryDragIcon.pickingMode = PickingMode.Ignore;
            //inventoryView.RegisterCallback<PointerDownEvent>(InventoryDragStart);
            inventoryView.RegisterCallback<PointerUpEvent>(InventoryHandler);
            inventoryMenu.Query("dropItemButton").First().RegisterCallback<PointerUpEvent, int>(inventoryMenuClick, 0); // 0 Is drop
            inventoryMenu.Query("sellItemButton").First().RegisterCallback<PointerUpEvent, int>(inventoryMenuClick, 1); // 1 Is sell
            inventoryMenu.RegisterCallback<GeometryChangedEvent>(InventoryMenuReposition);

            // UI pointers
            processesUI = uiDocument.rootVisualElement.Query("Processes").First();
            processesUI.RegisterCallback<ClickEvent>(ProcessClick);

            transportUI = uiDocument.rootVisualElement.Query("Transport").First();
            transportUI.RegisterCallback<ClickEvent>(TransportClick);

            cancelButton = uiDocument.rootVisualElement.Query("RightArea").First().Query("CancelButton").First();
            cancelButton.RegisterCallback<ClickEvent>(CancelButton);
            cancelButton.RegisterCallback<MouseEnterEvent, int>(ShowDescriptor, 0); // 0 means it is a cancelButton
            cancelButton.RegisterCallback<MouseLeaveEvent>(HideDescriptor);

            returnButton = uiDocument.rootVisualElement.Query("RightArea").First().Query("ReturnButton").First();
            returnButton.RegisterCallback<ClickEvent>(ReturnButton);
            returnButton.RegisterCallback<MouseEnterEvent, int>(ShowDescriptor, 1); // 1 means it is a returnButton
            returnButton.RegisterCallback<MouseLeaveEvent>(HideDescriptor);

            levelButton = uiDocument.rootVisualElement.Query("RightArea").First().Query("LevelButton").First();
            levelButton.RegisterCallback<ClickEvent>(LevelButton);
            levelButton.RegisterCallback<MouseEnterEvent, int>(ShowDescriptor, 2); // 2 means it is a returnButton
            levelButton.RegisterCallback<MouseLeaveEvent>(HideDescriptor);

            shopButton = uiDocument.rootVisualElement.Query("RightArea").First().Query("ShopButton").First();
            shopButton.RegisterCallback<ClickEvent>(ShopButton);
            shopButton.RegisterCallback<MouseEnterEvent, int>(ShowDescriptor, 7); // 7 means it is a shopButton
            shopButton.RegisterCallback<MouseLeaveEvent>(HideDescriptor);

            // Descriptor
            descriptor = uiDocument.rootVisualElement.Query("Descriptor").First();
            descriptorName = (Label)uiDocument.rootVisualElement.Query("Descriptor").First().Query("Name").First();
            descriptorDescription = (Label)uiDocument.rootVisualElement.Query("Descriptor").First().Query("Description").First();
            descriptorBottomText = (Label)uiDocument.rootVisualElement.Query("Descriptor").First().Query("BottomText").First();
            descriptorHotkey = (Label)uiDocument.rootVisualElement.Query("Descriptor").First().Query("Hotkey").First();
            descriptorLevel = (Label)uiDocument.rootVisualElement.Query("Descriptor").First().Query("Level").First();
            descriptorCostBox = uiDocument.rootVisualElement.Query("Descriptor").First().Query("CostBox").First();
            descriptorUsageCost = uiDocument.rootVisualElement.Query("Descriptor").First().Query("UsageCost").First();
            descriptorResourceCost = uiDocument.rootVisualElement.Query("Descriptor").First().Query("ResourceCost").First();
            descriptorLockbox = uiDocument.rootVisualElement.Query("Descriptor").First().Query("LockBox").First();
            descriptorLockNames = uiDocument.rootVisualElement.Query("Descriptor").First().Query("LockNames").First();

            // Unit tab
            unitBox = uiDocument.rootVisualElement.Query("UnitBox").First();
            unitIcon = uiDocument.rootVisualElement.Query("UnitInfo").First().Query("UnitIcon").First();
            unitName = (Label)uiDocument.rootVisualElement.Query("UnitInfo").First().Query("UnitName").First();
            invulnerable = (Label)uiDocument.rootVisualElement.Query("UnitInfo").First().Query("Invulnerable").First();
            unitName.RegisterCallback<MouseEnterEvent>(UnitDescriptor);
            unitName.RegisterCallback<MouseLeaveEvent>(HideDescriptor);

            manaBar = (ProgressBar)uiDocument.rootVisualElement.Query("UnitInfo").First().Query("ManaBar").First();
            manaRegen = (Label)uiDocument.rootVisualElement.Query("UnitInfo").First().Query("ManaRegen").First();

            healthBar = (ProgressBar)uiDocument.rootVisualElement.Query("UnitInfo").First().Query("HealthBar").First();
            healthRegen = (Label)uiDocument.rootVisualElement.Query("UnitInfo").First().Query("HealthRegen").First();

            lifetimeBar = (ProgressBar)uiDocument.rootVisualElement.Query("UnitBox").First().Query("Lifetime").First();

            // Unit stats
            attackElement = uiDocument.rootVisualElement.Query("UnitInfo").First().Query("DamageInfo").First();
            attackValue = (Label)attackElement.Query("DamageValue").First();
            armorElement = uiDocument.rootVisualElement.Query("UnitInfo").First().Query("ArmorInfo").First();
            armorValue = (Label)armorElement.Query("ArmorValue").First();
            attributesElement = uiDocument.rootVisualElement.Query("UnitInfo").First().Query("StatsInfo").First();
            XpBar = (ProgressBar)uiDocument.rootVisualElement.Query("UnitInfo").First().Query("XPBar").First();

            attackElement.RegisterCallback<MouseEnterEvent>(DamageDescriptor);
            attackElement.RegisterCallback<MouseLeaveEvent>(HideDescriptor);
            armorElement.RegisterCallback<MouseEnterEvent>(ArmorDescriptor);
            armorElement.RegisterCallback<MouseLeaveEvent>(HideDescriptor);
            XpBar.RegisterCallback<MouseEnterEvent>(XPDescriptor);
            XpBar.RegisterCallback<MouseLeaveEvent>(HideDescriptor);
            attributesElement.RegisterCallback<MouseEnterEvent>(AttributeDescriptor);
            attributesElement.RegisterCallback<MouseLeaveEvent>(HideDescriptor);

            // Commands
            moveCommand = uiDocument.rootVisualElement.Query("Commands").First().Query("MoveCommand").First();
            stopCommand = uiDocument.rootVisualElement.Query("Commands").First().Query("StopCommand").First();
            holdCommand = uiDocument.rootVisualElement.Query("Commands").First().Query("HoldCommand").First();
            attackCommand = uiDocument.rootVisualElement.Query("Commands").First().Query("AttackCommand").First();

            moveCommand.RegisterCallback<MouseEnterEvent, int>(ShowDescriptor, 3);
            moveCommand.RegisterCallback<MouseLeaveEvent>(HideDescriptor);

            stopCommand.RegisterCallback<MouseEnterEvent, int>(ShowDescriptor, 4);
            stopCommand.RegisterCallback<MouseLeaveEvent>(HideDescriptor);
            stopCommand.RegisterCallback<ClickEvent>(evt => { StopButton(); });

            holdCommand.RegisterCallback<MouseEnterEvent, int>(ShowDescriptor, 5);
            holdCommand.RegisterCallback<MouseLeaveEvent>(HideDescriptor);
            holdCommand.RegisterCallback<ClickEvent>(evt => { HoldButton(); });

            attackCommand.RegisterCallback<MouseEnterEvent, int>(ShowDescriptor, 6);
            attackCommand.RegisterCallback<MouseLeaveEvent>(HideDescriptor);
            attackCommand.RegisterCallback<ClickEvent>(evt => { AttackMoveButton(); });

            // Status
            statusWindow = uiDocument.rootVisualElement.Query("Status").First();

            // DebugTab
            debugWindow = uiDocument.rootVisualElement.Query("Debug").First();

            // Mini map
            miniMap = uiDocument.rootVisualElement.Query("MiniMap").First().Query("Overlay").First();
            miniMap.RegisterCallback<PointerDownEvent>(MiniMapClick);
            miniMap.RegisterCallback<PointerMoveEvent>(MiniMapMoveEvent);
            miniMap.RegisterCallback<ClickEvent>(MiniMapPing);

            ResetMiniMap();

            // MSG and Chatbox
            notifyMsg = (Label)uiDocument.rootVisualElement.Query("MessageBox").First().Query("warningMsg").First();
            msgInput = (TextField)uiDocument.rootVisualElement.Query("MessageInput").First();
            chatBox = (VisualElement)uiDocument.rootVisualElement.Query("MessageBox").First().Query("ChatBox").First();
            msgInput.RegisterCallback<BlurEvent>(HideChat);

            chatBox.Clear();

            // Resources Tab
            resourceTab = uiDocument.rootVisualElement.Query("Resources").First();
            resourceTab.Clear();
            ResourceTabCreate();
            RefreshResourceTab();

            // [Interflow fix 2026-06-27] вызов InitTechPanel() удалён — партиал UIManager.TechPanel.cs удалён.
            // Причина: мёртвый UI (панель TechPanel и кнопка TechTreeButton скрыты hiddenHudElements, ToggleTechPanel без вызовов; техи теперь в CornerTables).
            InitBottomTables();
            InitCornerTables();

            // UI initialisation
ResetUnitUI();

            GameManager.instance.Tick += MsgTimerUpdate;
            GameManager.instance.Tick += ChatTimerUpdate;

            // [Interflow fix 2026-06-26 путь1] UI построен — презентация готова. На сервере сюда не доходим (init-гейт в начале Start).
            presentationReady = true;
        }

        // Update is called once per frame
        void Update()
        {
            if (ServerBootstrap.IsHeadlessServer) return;   // [Interflow fix 2026-06-20] init-гейт: pc/UI не построены на сервере
            if (pc.activeUnit != null)
            {
                // Process timer update
                if (pc.activeUnit.canProcess)
                {
                    ProcessTimerUpdate();
                }

                // Debug
                // SetUnitInfo();

                // Item dragDrop
                if (isDraggingItem)
                {
                    if (PlayerControl.coreInput.Main.SelectHold.WasReleasedThisFrame()) InventoryDragFinish();
                    InventoryDragUpdate();
                }
            }

            ViewRectUpdate();
            MinimapPingUpdate();
        }

        public void ViewRectUpdate()
        {
            if (minimapRect)
            {
                // Set Position and Rotation
                // viewRect.transform.position = new Vector3(viewRect.transform.position.x, 25f, viewRect.transform.position.z);
                // 
                // Vector3 sourceRotation = cameraRig.transform.rotation.eulerAngles;
                // Vector3 targetRotation = viewRect.transform.rotation.eulerAngles;
                // 
                // targetRotation.z = sourceRotation.y;
                // 
                // viewRect.transform.rotation = Quaternion.Euler(targetRotation);

                // Set Size
                float height = 2f * Utils.MainCamera.transform.position.y * Mathf.Tan(Utils.MainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);

                minimapRect.size = new Vector2(height * Utils.MainCamera.aspect, height);
            }
            else
            {
                Debug.Log("ViewRect of camera is not assigned!");
            }
        }
    }
}
