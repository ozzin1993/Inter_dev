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
        public static UIManager instance;

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
                float height = 2f * Camera.main.transform.position.y * Mathf.Tan(Camera.main.fieldOfView * 0.5f * Mathf.Deg2Rad);

                minimapRect.size = new Vector2(height * Camera.main.aspect, height);
            }
            else
            {
                Debug.Log("ViewRect of camera is not assigned!");
            }
        }

        // When active unit changes subscribe to that unit parameter changes and show icon/abilities
        public void SubscribeToUnit()
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            ResetUnitUI();

            // AbilityView and Inventory
            abilityView.style.display = DisplayStyle.Flex;
            ShowCommands();
            AbilityInventoryDisplay();

            pc.activeUnit.OnRedrawAbilityView += AbilityInventoryDisplay;
            pc.activeUnit.OnInventoryChange += InventoryDisplay;
            GameManager.instance.Tick += CooldownTimerUpdate;

            // Subscribe AbilityDisplay to Technology Lock and Unlock. 
            TechnologyManager.instance.OnTechUnlock[pc.activeUnit.owner] += AbilityInventoryDisplay;
            TechnologyManager.instance.OnTechLock[pc.activeUnit.owner] += AbilityInventoryDisplay;

            // Process
            if (pc.activeUnit.team == SlotManager.instance.currentTeam)
            {
                // Processing unit
                if (pc.activeUnit.canProcess)
                {
                    ProcessDisplay();
                    //ShowProcesses();
                    pc.activeUnit.OnProcessUpdate += ProcessDisplay;
                }
                // Transport Unit
                if (pc.activeUnit.transportUnit)
                {
                    TransportDisplay();
                    pc.activeUnit.transportUnit.transportChange += TransportDisplay;
                }
            }

            // Hero ability leveling
            if (pc.activeUnit.levelingUnit && pc.activeUnit.levelingUnit.abilityPoints != 0)
            {
                ShowLevelButton();
            }

            // Status
            DisplayStatusTab();
            pc.activeUnit.OnStatusUpdate += DisplayStatusTab;

            // Unit Box
            UnitBoxDisplay();

            HealthDisplay();
            ManaDisplay();
            XpDisplay();

            // Subscribe to changes
            pc.activeUnit.OnHPChange += HealthDisplay;
            pc.activeUnit.OnMPChange += ManaDisplay;
            if (pc.activeUnit.levelingUnit) pc.activeUnit.levelingUnit.OnXPChange += XpDisplay;

            // Unit stats
            DisplayUnitStats();

            pc.activeUnit.OnCharacteristicsChange += UpdateDamageInfo;
            pc.activeUnit.OnCharacteristicsChange += UpdateArmorInfo;

            // LifeTime
            if (pc.activeUnit.lifetimeUnit)
            {
                lifetimeBar.style.display = DisplayStyle.Flex;
                GameManager.instance.Tick += LifetimeUpdate;
            }
            else if (pc.activeUnit.isBeingBuilt)
            {
                lifetimeBar.style.display = DisplayStyle.Flex;
                GameManager.instance.Tick += ConstructionUpdate;
            }
            else if (pc.activeUnit.polymorphed)
            {
                lifetimeBar.style.display = DisplayStyle.Flex;
                GameManager.instance.Tick += PolymorphUpdate;
            }
            else lifetimeBar.style.display = DisplayStyle.None;

            // Hotkey
            PlayerControl.coreInput.Main.AnyKey.performed += AnyKeyPressed;

            // Waypoint
            pc.activeUnit.ShowWaypoint();
            pc.activeUnit.WaypointUpdate += pc.activeUnit.ShowWaypoint;

            // Debug
            // GameManager.instance.Tick += SetUnitInfo;
        }

        // Active unit is no longer there, unsubscribe and hide the icon/abilities
        public void UnsubscribeToUnit(Unit unit)
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            // Subscribe AbilityDisplay to Technology Lock and Unlock.
            pc.activeUnit.OnRedrawAbilityView -= AbilityInventoryDisplay;
            pc.activeUnit.OnInventoryChange -= InventoryDisplay;
            TechnologyManager.instance.OnTechUnlock[pc.activeUnit.owner] -= AbilityInventoryDisplay;
            TechnologyManager.instance.OnTechLock[pc.activeUnit.owner] -= AbilityInventoryDisplay;

            // Cooldown
            GameManager.instance.Tick -= CooldownTimerUpdate;
            cooldownElements.Clear();
            cooldownTimers.Clear();
            cooldownIndex.Clear();

            pc.activeUnit.OnStatusUpdate -= DisplayStatusTab;

            pc.activeUnit.OnHPChange -= HealthDisplay;
            pc.activeUnit.OnMPChange -= ManaDisplay;
            if (pc.activeUnit.levelingUnit) pc.activeUnit.levelingUnit.OnXPChange -= XpDisplay;

            if (pc.activeUnit.canProcess) pc.activeUnit.OnProcessUpdate -= ProcessDisplay;
            if (pc.activeUnit.transportUnit) pc.activeUnit.transportUnit.transportChange -= TransportDisplay;

            // Unit stats
            //DisplayUnitStats();
            //HideCommands();

            pc.activeUnit.OnCharacteristicsChange -= UpdateDamageInfo;
            pc.activeUnit.OnCharacteristicsChange -= UpdateArmorInfo;

            // LifeTime
            if (pc.activeUnit.lifetimeUnit) GameManager.instance.Tick -= LifetimeUpdate;
            else if (pc.activeUnit.constructionUnit) GameManager.instance.Tick -= ConstructionUpdate;
            GameManager.instance.Tick -= PolymorphUpdate;

            //HideStatusTab();

            //RemoveIconDisplay();
            //RemoveUnitName();
            //HideProcesses();
            ResetUnitUI();
            //HideLevelButton(true);

            // Hotkey
            PlayerControl.coreInput.Main.AnyKey.performed -= AnyKeyPressed;

            // Waypoint
            ReferenceManager.instance.HideWaypoint();
            pc.activeUnit.WaypointUpdate -= pc.activeUnit.ShowWaypoint;

            // Debug
            // GameManager.instance.Tick -= SetUnitInfo;
        }

        // Redraw the active unit info by resubscribing to it
        public void Resubscribe()
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            if (pc.activeUnit != null)
            {
                UnsubscribeToUnit(pc.activeUnit);
                SubscribeToUnit();
            }
        }

        public void AbilityInventoryDisplay()
        {
            if (pc.activeUnit != null)
            {
                RedrawAbilityView();
                InventoryDisplay();
            }
        }

        // Clears the UI as if no unit is selected
        public void ResetUnitUI()
        {
            //HideReturnButton();

            // Ability view
            // abilityView.style.display = DisplayStyle.None;
            RebuildAbilityView();

            // Inventory box
            RemoveInventoryDisplay();

            // UnitBox
            unitBox.style.display = DisplayStyle.None;

            // RemoveHealthDisplay();
            // RemoveManaDisplay();
            // RemoveIconDisplay();
            // RemoveXpDisplay();

            HideProcesses();
            HideTransport();
            HideDescriptor();

            HideStatusTab();
            // HideUnitStats();

            HideLevelButton(true);
            HideCommands();

            openContainersIndex.Clear();
        }

        // Health display
        private void HealthDisplay()
        {
            healthBar.title = Mathf.Ceil(pc.activeUnit.health) + " / " + Mathf.Ceil(pc.activeUnit.maxHealth);
            healthBar.value = pc.activeUnit.health / pc.activeUnit.maxHealth;
            healthRegen.text = (pc.activeUnit.healthRegen > 0) ? "+" + pc.activeUnit.healthRegen.ToString("0.0") : pc.activeUnit.healthRegen.ToString("0.0");
        }

        private void RemoveHealthDisplay()
        {
            healthBar.title = "";
            healthBar.value = 0;
            healthRegen.text = "";
        }

        // Mana display
        private void ManaDisplay()
        {
            if (pc.activeUnit.maxMana != 0)
            {
                manaBar.style.display = DisplayStyle.Flex;
                manaBar.title = Mathf.Ceil(pc.activeUnit.mana) + " / " + Mathf.Ceil(pc.activeUnit.maxMana);
                manaBar.value = pc.activeUnit.mana / pc.activeUnit.maxMana;
                manaRegen.text = (pc.activeUnit.manaRegen > 0) ? "+" + pc.activeUnit.manaRegen.ToString("0.0") : pc.activeUnit.manaRegen.ToString("0.0");
            }
            else
            {
                manaBar.style.display = DisplayStyle.None;
            }
        }

        // Unit icon display
        private void UnitBoxDisplay()
        {
            unitBox.style.display = DisplayStyle.Flex;

            unitName.text = pc.activeUnit.unitName;
            unitIcon.style.backgroundImage = pc.activeUnit.icon;
        }

        private void IconDisplay()
        {
            unitIcon.style.backgroundImage = pc.activeUnit.icon;
        }

        private void RemoveIconDisplay()
        {
            unitIcon.style.backgroundImage = null;
        }

        private void UnitNameDisplay()
        {
            unitName.style.display = DisplayStyle.Flex;
            unitName.text = pc.activeUnit.unitName;
        }

        private void RemoveUnitName()
        {
            unitName.style.display = DisplayStyle.None;
        }

        // When hovering over unit name we show its description
        private void UnitDescriptor(MouseEnterEvent evt)
        {
            string description = "";
            if (pc.activeUnit.description != "") description = pc.activeUnit.description;

            // Move speed
            if (pc.activeUnit.canMove)
            {
                if (description.Length > 0) description += "\n";
                description += "Movement speed: " + pc.activeUnit.moveSpeed;
            }

            // Resource unit
            if (pc.activeUnit.resourceUnit)
            {
                // Collectible
                if (pc.activeUnit.resourceUnit.isCollectible)
                {
                    string collectible = "";
                    for (int i = 0; i < pc.activeUnit.resourceUnit.collectibleResources.Length; i++)
                    {
                        collectible += "\n" + pc.activeUnit.resourceUnit.collectibleResources[i].type.displayName + ": " + pc.activeUnit.resourceUnit.collectibleResources[i].value;
                    }
                    if (collectible.Length > 0) description += "\n\nCollectible resources:" + collectible;
                }
                // Collector
                else if (pc.activeUnit.resourceUnit.isCollector)
                {
                    string collector = "";
                    for (int i = 0; i < pc.activeUnit.resourceUnit.currentHeldResources.Count; i++)
                    {
                        collector += "\n" + pc.activeUnit.resourceUnit.currentHeldResources[i].type.displayName + ": " + pc.activeUnit.resourceUnit.currentHeldResources[i].value;

                        int collectorResourceIndex = ResourceUnit.GetResourceIndex(pc.activeUnit.resourceUnit.currentHeldResources[i].type, pc.activeUnit.resourceUnit.collectorResources);
                        if (collectorResourceIndex != -1)
                        {
                            collector += "\\" + pc.activeUnit.resourceUnit.collectorResources[collectorResourceIndex].value;
                        }
                    }
                    if (collector.Length > 0) description += "\n\nCurrent resources:" + collector;
                }
            }

            // Leveling unit
            if (pc.activeUnit.levelingUnit)
            {
                FillDescriptor(pc.activeUnit.unitName, description, pc.activeUnit.levelingUnit.level, pc.activeUnit.levelingUnit.maxLevel);
            }
            else
            {
                FillDescriptor(pc.activeUnit.unitName, description);
            }
        }

        // XP display
        private void XpDisplay()
        {
            if (pc.activeUnit.levelingUnit && pc.activeUnit.levelingUnit.maxLevel != 1)
            {
                XpBar.style.display = DisplayStyle.Flex;
                XpBar.title = pc.activeUnit.levelingUnit.level.ToString();
                XpBar.value = (float)(pc.activeUnit.levelingUnit.currentExp - pc.activeUnit.levelingUnit.previousExpRequired) / (pc.activeUnit.levelingUnit.expRequired - pc.activeUnit.levelingUnit.previousExpRequired);

                // Ability Points
                if (pc.activeUnit.levelingUnit.abilityPoints != 0)
                {
                    ShowLevelButton();
                }
            }
            else
            {
                XpBar.style.display = DisplayStyle.None;
            }
        }

        private void RemoveXpDisplay()
        {
            XpBar.style.display = DisplayStyle.None;
        }

        private void LifetimeUpdate()
        {
            if (pc.activeUnit != null)
            {
                lifetimeBar.title = Mathf.Ceil(pc.activeUnit.lifetimeUnit.currentLifeSpan) + "s";
                lifetimeBar.value = pc.activeUnit.lifetimeUnit.currentLifeSpan / pc.activeUnit.lifetimeUnit.lifespan;
            }
        }

        private void ConstructionUpdate()
        {
            if (pc.activeUnit != null)
            {
                if (pc.activeUnit.constructionUnit.upgradeBuildingRef)
                {
                    // Upgrade time
                    lifetimeBar.title = (pc.activeUnit.constructionUnit.upgradeTime * (1 - pc.activeUnit.constructionUnit.currentConstructionPercentage)).ToString("F1") + "s";
                }
                else
                {
                    // Construction time
                    lifetimeBar.title = (pc.activeUnit.constructionUnit.constructionTime * (1 - pc.activeUnit.constructionUnit.currentConstructionPercentage)).ToString("F1") + "s";
                }

                lifetimeBar.value = pc.activeUnit.constructionUnit.currentConstructionPercentage;
            }
        }

        private void PolymorphUpdate()
        {
            if (pc.activeUnit != null)
            {
                if (pc.activeUnit.polymorphed)
                {
                    lifetimeBar.value = pc.activeUnit.polymorphTime / pc.activeUnit.polymorphTotalTime;
                    lifetimeBar.title = (pc.activeUnit.polymorphTime).ToString("F1") + "s";
                }
                else
                {
                    lifetimeBar.style.display = DisplayStyle.None;
                    GameManager.instance.Tick -= PolymorphUpdate;
                }
            }
        }

        private void DisplayUnitStats()
        {
            // Damage
            if (pc.activeUnit.canAttack)
            {
                attackElement.style.display = DisplayStyle.Flex;
                UpdateDamageInfo();
            }
            else
            {
                attackElement.style.display = DisplayStyle.None;
            }

            // Armor
            UpdateArmorInfo();

            // Attributes
            if (pc.activeUnit.attributeUnit) attributesElement.style.display = DisplayStyle.Flex;
            else attributesElement.style.display = DisplayStyle.None;
        }

        private void HideUnitStats()
        {
            attackElement.style.display = DisplayStyle.None;
            armorElement.style.display = DisplayStyle.None;
            attributesElement.style.display = DisplayStyle.None;
            XpBar.style.display = DisplayStyle.None;
            unitName.style.display = DisplayStyle.None;
        }

        private void ShowCommands()
        {
            // Check if unit belongs to the current player
            if (!SlotManager.instance.debugMode && pc.activeUnit.owner != SlotManager.instance.currentPlayer) return;

            // Construction cancel button
            if (pc.activeUnit.isBeingBuilt)
            {
                ShowCancelButton();
                return;
            }

            if (pc.activeUnit.canAttack)
            {
                attackCommand.style.display = DisplayStyle.Flex;
            }
            // Skip move command - not enough space in UI
            // if (pc.activeUnit.canMove)
            // {
            //     moveCommand.style.display = DisplayStyle.Flex;
            //     holdCommand.style.display = DisplayStyle.Flex;
            // }
            stopCommand.style.display = DisplayStyle.Flex;
            holdCommand.style.display = DisplayStyle.Flex;

            if (pc.activeUnit.isShop) shopButton.style.display = DisplayStyle.Flex;

            //CommandUpdate();
            // Subscribe
            //if (pc.activeUnit) pc.activeUnit.OnCommand += CommandUpdate;
        }

        private void HideCommands()
        {
            moveCommand.style.display = DisplayStyle.None;
            stopCommand.style.display = DisplayStyle.None;
            holdCommand.style.display = DisplayStyle.None;
            attackCommand.style.display = DisplayStyle.None;

            shopButton.style.display = DisplayStyle.None;
            returnButton.style.display = DisplayStyle.None;
            levelButton.style.display = DisplayStyle.None;
            cancelButton.style.display = DisplayStyle.None;

            // Unsub
            //if (pc.activeUnit) pc.activeUnit.OnCommand -= CommandUpdate;
        }

        // To display the state of the unit
        private void CommandUpdate()
        {
            if (pc.activeUnit.unitState == UnitStates.Hold) holdCommand.AddToClassList("commandButtonActive");
            else holdCommand.RemoveFromClassList("commandButtonActive");

            if (pc.activeUnit.unitState == UnitStates.Attack) attackCommand.AddToClassList("commandButtonActive");
            else attackCommand.RemoveFromClassList("commandButtonActive");
        }

        private void UpdateDamageInfo()
        {
            attackValue.text = pc.activeUnit.attackDamage.ToString("F0");
        }

        private void UpdateArmorInfo()
        {
            // Armor
            armorElement.style.display = DisplayStyle.Flex;
            armorValue.text = pc.activeUnit.armor.ToString();

            // Invulnerability
            if (pc.activeUnit.isInvulnerable) invulnerable.style.display = DisplayStyle.Flex;
            else invulnerable.style.display = DisplayStyle.None;
        }

        private void DamageDescriptor(MouseEnterEvent evt)
        {
            if (pc.activeUnit)
            {
                if (!pc.activeUnit.damageType)
                {
                    Debug.LogWarning("Damage type of " + pc.activeUnit.unitName + " is not set!");
                    return;
                }

                if (pc.activeUnit.melee)
                {
                    FillDescriptor("Type: " + pc.activeUnit.damageType.displayName + "",
                                   "Attack Speed: " + pc.activeUnit.attackSpeed.ToString("F2") + "\n" + pc.activeUnit.damageType.description,
                                   "Melee");
                }
                else
                {
                    FillDescriptor("Type: " + pc.activeUnit.damageType.displayName + "",
                                   "Attack Speed: " + pc.activeUnit.attackSpeed.ToString("F2") + "\n" + pc.activeUnit.damageType.description,
                                   "Range: " + pc.activeUnit.attackRange);
                }

                // Range projector
                if (!pc.activeUnit.melee) pc.CreateRangeProjector();
            }
        }

        private void ArmorDescriptor(MouseEnterEvent evt)
        {
            if (pc.activeUnit)
            {
                if (!pc.activeUnit.armorType)
                {
                    Debug.LogWarning("Armor type of " + pc.activeUnit.unitName + " is not set!");
                    return;
                }

                FillDescriptor("Type: " + pc.activeUnit.armorType.displayName, "Current damage reduction: " + (100 * ((0.06f * pc.activeUnit.armor) / (1 + 0.06f * pc.activeUnit.armor))).ToString("F2") + "%\n" + pc.activeUnit.armorType.description);
            }
        }

        private void XPDescriptor(MouseEnterEvent evt)
        {
            if (pc.activeUnit)
            {
                FillDescriptor(
                "Level " + pc.activeUnit.levelingUnit.level + " / " + pc.activeUnit.levelingUnit.maxLevel,
                "Level can be obtained by killing enemy units",
                "XP: " + pc.activeUnit.levelingUnit.currentExp + " / " + pc.activeUnit.levelingUnit.expRequired
                );
            }
        }

        private void AttributeDescriptor(MouseEnterEvent evt)
        {
            FillDescriptor("Main attribute: " + pc.activeUnit.attributeUnit.mainAttribute.displayName, "");

            for (int i = 0; i < pc.activeUnit.attributeUnit.unitAttributes.Length; i++)
            {
                if (i != 0) descriptorDescription.text += "\n";
                descriptorDescription.text += pc.activeUnit.attributeUnit.unitAttributes[i].attribute.displayName + ": " + pc.activeUnit.attributeUnit.unitAttributes[i].value;
            }
        }

        // ABILITIES ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // To show current state of ability view call this
        public void RedrawAbilityView()
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            RebuildAbilityView();
            HideDescriptor(); // Descriptor is called hidden because when abilityView is changed, descriptor might get stuck when the ability is removed

            if (openContainersIndex.Count == 0)
            {
                // Main ability view
                AbilityDisplay(pc.activeUnit.abilities, pc.activeUnit.abilities);
                openContainersIndex.Clear();
            }
            else
            {
                // Last open container ability view
                Container container = (Container)Utils.GetAbilityByIndexName(pc.activeUnit, openContainersIndex[openContainersIndex.Count - 1].ToString(), out int abilityIndex);
                AbilityDisplay(container.abilities, pc.activeUnit.abilities);
            }
        }

        // Ability view ScrollView element (parent of the ability elements) should be rebuilt completely because of the bug that does not change its size dynamically
        public void RebuildAbilityView()
        {
            var parent = abilityScrollView.parent;
            abilityScrollView.UnregisterCallback<ClickEvent>(AbilityHandler);
            parent.Remove(abilityScrollView);

            abilityScrollView = new ScrollView();
            abilityScrollView.AddToClassList("AbilityScrollView");
            abilityScrollView.name = "ScrollView";
            abilityScrollView.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
            abilityScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            parent.Add(abilityScrollView);

            //abilityView = abilityScrollView; // abilityScrollView.Query("unity-content-container").First();
            abilityScrollView.RegisterCallback<ClickEvent>(AbilityHandler);

            // Fill with Empty elements
            for (int i = 0; i < 16; i++)
            {
                EmptyElementCreate(abilityScrollView, i);
            }
        }

        // Add ability elements to AbilityView
        // pathPrefix is used for the naming of the ability elements that should indicate how to reach said ability in case of containerAbility usage
        // abilities - abilities that are to be displayed now. mainAbilities - unit`s main abilities parameter. In case of containers abilities will be abilities of the container, and mainAbilities will be abilities of the unit that has that container.
        public void AbilityDisplay(Ability[] abilities, Ability[] mainAbilities, string pathPrefix = "")
        {
            abilityScrollView.Clear();

            // Abilities
            int nextEmptySlot = 0;
            int totalSlotSize = 16; // 4x4. 4 columns 4 rows.
            List<int> slotToAbilityIndex = new List<int>(); // Slot to unit.abilities[] index
            List<int> slotToGlobalAbilityIndex = new List<int>();// Currently viewed abilities by slots to ability index
            slotToAbilityIndex.Populate(-1, totalSlotSize); // -1 means this slot is empty.
            slotToGlobalAbilityIndex.Populate(-1, totalSlotSize); // -1 means this slot is empty.

            // Cooldown reset
            if (cooldownElements.Count > 0)
            {
                cooldownElements.Clear();
                cooldownTimers.Clear();
                cooldownIndex.Clear();
            }

            // Ability arrangement in UI
            for (int i = 0; i < abilities.Length; i++)
            {
                int abilityIndex = Utils.GetAbilityIndex(mainAbilities, abilities[i]);

                // If does not belong to player, we show only items and containers containing items
                if (pc.activeUnit.isBeingBuilt) continue;
                if (pc.activeUnit.owner != SlotManager.instance.currentPlayer && !SlotManager.instance.debugMode)
                {
                    // Only ally team
                    if (SlotManager.instance.IsAlly(pc.activeUnit.owner, SlotManager.instance.currentPlayer))
                    {
                        if (abilities[i].type == AbilityType.Container)
                        {
                            Container container = (Container)abilities[i];
                            if (!container.isShop) continue;
                        }
                        else if (abilities[i].isItem == false) continue;
                    }
                    else continue;
                }

                // If this ability is a research and already was learnt, skip it
                if (abilities[i] is Research)
                {
                    Research research = (Research)abilities[i];

                    if (research.unlockTech.Length > 0 && TechnologyManager.instance.isUnlocked(research.unlockTech[research.unlockTech.Length - 1], pc.activeUnit.owner))
                    {
                        continue;
                    }
                }

                // When leveling ability, show only heroLevelable abilities that are not max level AND containers
                if (isLeveling == true)
                {
                    if (abilities[i].type != AbilityType.Container)
                    {
                        if (!abilities[i].heroLevelable || pc.activeUnit.abilityLevel[abilityIndex] + 1 >= abilities[i].maxLevels)
                        {
                            continue;
                        }
                    }
                }
                else
                {
                    // If ability level is -1, it means it is levelable ability that was not learnt, skip it
                    if (pc.activeUnit.abilityLevel[abilityIndex] == -1)
                    {
                        continue;
                    }
                }

                // If slot number is higher than current slotSize, increase slot size.
                if (abilities[i].slotNumber >= totalSlotSize)
                {
                    int newSlotSize = (int)(Mathf.Ceil((abilities[i].slotNumber + 1f) / 4f) * 4f); // Round up to be divisible by 4. 4 Abilities per row.
                    slotToAbilityIndex.Populate(-1, newSlotSize - totalSlotSize);
                    slotToGlobalAbilityIndex.Populate(-1, newSlotSize - totalSlotSize);
                    totalSlotSize = newSlotSize;
                }

                // If slot number is -1, it means we should display the ability sequentially. Otherwise slotToAbilityIndex should point to the index of the ability of the unit
                if (abilities[i].slotNumber == -1)
                {
                    // Find the next empty slot
                    while (nextEmptySlot < slotToAbilityIndex.Count && slotToAbilityIndex[nextEmptySlot] != -1)
                    {
                        nextEmptySlot++;
                    }

                    // Ensure we expand the list if we've run out of space
                    if (nextEmptySlot >= slotToAbilityIndex.Count)
                    {
                        slotToAbilityIndex.Populate(-1, 4); // Add 4 new slots
                        slotToGlobalAbilityIndex.Populate(-1, 4);
                        totalSlotSize += 4;
                    }

                    // Assign the ability to the next available slot
                    slotToAbilityIndex[nextEmptySlot] = i;
                    slotToGlobalAbilityIndex[nextEmptySlot] = abilityIndex;

                    // Increment nextEmptySlot for the next sequentially added ability
                    nextEmptySlot++;
                }
                else
                {
                    // If slot is empty add the ability index
                    if (slotToAbilityIndex[abilities[i].slotNumber] == -1)
                    {
                        slotToAbilityIndex[abilities[i].slotNumber] = i;
                        slotToGlobalAbilityIndex[abilities[i].slotNumber] = abilityIndex;
                    }
                    // If slot is not empty, put ability at the current slot to the empty slot, put current ability at current slot
                    else
                    {
                        // Save the ability currently in the slot
                        int abilityAtSlot = slotToAbilityIndex[abilities[i].slotNumber];
                        int abilityAtSlotGlobal = slotToGlobalAbilityIndex[abilities[i].slotNumber];

                        // Assign the new ability to the current slot
                        slotToAbilityIndex[abilities[i].slotNumber] = i;
                        slotToGlobalAbilityIndex[abilities[i].slotNumber] = abilityIndex;

                        // Find the next empty slot for the displaced ability
                        while (nextEmptySlot < slotToAbilityIndex.Count && slotToAbilityIndex[nextEmptySlot] != -1)
                        {
                            nextEmptySlot++;
                        }

                        // Ensure we expand the list if we've run out of space
                        if (nextEmptySlot >= slotToAbilityIndex.Count)
                        {
                            slotToAbilityIndex.Populate(-1, 4); // Add 4 new slots
                            slotToGlobalAbilityIndex.Populate(-1, 4);
                            totalSlotSize += 4;
                        }

                        // Move the displaced ability to the next available slot
                        slotToAbilityIndex[nextEmptySlot] = abilityAtSlot;
                        slotToGlobalAbilityIndex[nextEmptySlot] = abilityAtSlotGlobal;

                        // Increment nextEmptySlot for the next sequentially added ability
                        nextEmptySlot++;
                    }
                }
            }

            // Add UI elements according to slotToAbility arrangement
            for (int i = 0; i < slotToAbilityIndex.Count; i++)
            {
                if (slotToAbilityIndex[i] == -1)
                {
                    // Empty slot
                    // Display empty 
                    EmptyElementCreate(abilityScrollView, i);
                }
                else
                {
                    // unit.abilities[slotToAbilityIndex[i]] - the ability at the current slot
                    GroupBox element;
                    int iconLevel;
                    if (abilities[slotToAbilityIndex[i]].icon.Length > pc.activeUnit.abilityLevel[slotToGlobalAbilityIndex[i]]) iconLevel = pc.activeUnit.abilityLevel[slotToGlobalAbilityIndex[i]];
                    else iconLevel = abilities[slotToAbilityIndex[i]].icon.Length - 1;
                    if (iconLevel == -1) iconLevel = 0;

                    // Display ability
                    if (((isLeveling || !abilities[slotToAbilityIndex[i]].heroLevelable) && pc.activeUnit.abilityLocked[slotToGlobalAbilityIndex[i]])
                        || (abilities[slotToAbilityIndex[i]] is UpgradeBuilding && (pc.activeUnit.activeProcess[0] != null || (pc.activeUnit.transportUnit && pc.activeUnit.transportUnit.units.Count > 0))))
                    {
                        // Ability is locked
                        element = ElementCreate(abilityScrollView, slotToGlobalAbilityIndex[i], abilities[slotToAbilityIndex[i]].icon[iconLevel], true, false, pathPrefix);
                    }
                    else
                    {
                        // If this ability is of Research type and is being processed currently, should be locked
                        if (abilities[slotToAbilityIndex[i]] is Research)
                        {
                            Research research = (Research)abilities[slotToAbilityIndex[i]];

                            if (research.unlockTech.Length > pc.activeUnit.abilityLevel[slotToGlobalAbilityIndex[i]] && TechnologyManager.instance.IsTechBeingProcessed(research.unlockTech[pc.activeUnit.abilityLevel[slotToGlobalAbilityIndex[i]]], pc.activeUnit.owner))
                            {
                                // Locked
                                element = ElementCreate(abilityScrollView, slotToGlobalAbilityIndex[i], abilities[slotToAbilityIndex[i]].icon[iconLevel], true, false, pathPrefix);
                            }
                            else
                            {
                                // Unlocked
                                element = ElementCreate(abilityScrollView, slotToGlobalAbilityIndex[i], abilities[slotToAbilityIndex[i]].icon[iconLevel], false, false, pathPrefix);
                            }
                        }
                        else
                        {
                            // Ability is unlocked
                            element = ElementCreate(abilityScrollView, slotToGlobalAbilityIndex[i], abilities[slotToAbilityIndex[i]].icon[iconLevel], false, false, pathPrefix);
                        }
                    }

                    // If toggle is active, add indicator
                    if (abilities[slotToAbilityIndex[i]].type == AbilityType.Toggle && pc.activeUnit.IndexOfEveryFrameAbility(slotToGlobalAbilityIndex[i], false) != -1)
                    {
                        element.AddToClassList("activeAbility");
                    }

                    // If ability has cooldown, show it
                    if (!isLeveling)
                    {
                        int abilityCooldownIndex = pc.activeUnit.GetAbilityCooldownIndex(slotToGlobalAbilityIndex[i], false);
                        if (abilityCooldownIndex != -1) AddCooldownElement(element, abilityCooldownIndex);
                    }
                }
            }
        }

        public void InventoryDisplay()
        {
            if (pc.activeUnit.InventorySize > 0)
            {
                // Show and Refresh UI
                inventoryUI.style.display = DisplayStyle.Flex;
                inventoryView.Clear();

                // Display items
                for (int i = 0; i < GameManager.maxInventorySize; i++)
                {
                    GroupBox element = null;

                    if (i < pc.activeUnit.InventorySize)
                    {
                        if (pc.activeUnit.items[i] == null)
                        {
                            // Empty slot
                            // Display empty 
                            EmptyElementCreate(inventoryView, i, true);
                        }
                        else
                        {
                            // unit.items[slotToItemIndex[i]] - the item at the current slot
                            // Display item
                            element = ElementCreate(inventoryView, i, pc.activeUnit.items[i].icon[0], false, true);
                        }
                    }
                    else
                    {
                        // Unavailable slot
                        EmptyElementCreate(inventoryView, -1, true, true);
                    }

                    if (element != null)
                    {
                        // If toggle is active, add indicator
                        Ability itemAbility = (Ability)pc.activeUnit.items[i];
                        if (itemAbility.type == AbilityType.Toggle && pc.activeUnit.IndexOfEveryFrameAbility(itemAbility, true) != -1)
                        {
                            element.AddToClassList("activeAbility");
                        }

                        // If ability has cooldown, show it
                        int abilityCooldownIndex = pc.activeUnit.GetAbilityCooldownIndex(i, true);
                        if (abilityCooldownIndex != -1)
                        {
                            AddCooldownElement(element, abilityCooldownIndex);
                        }

                        // Charges
                        if (pc.activeUnit.itemCharges[i] > 0)
                        {
                            Label charges = new Label();
                            charges.AddToClassList("charges");
                            charges.text = pc.activeUnit.itemCharges[i].ToString();
                            element.Add(charges);
                        }
                    }
                }
            }

            // We update cooldown timers to properly show currently added timers since the inventory display is called after the ability display
            CooldownTimerUpdate();
        }

        private void RemoveInventoryDisplay()
        {
            inventoryUI.style.display = DisplayStyle.None;
        }

        // Creates ability or item UI element. pathPrefix is used in container abilities, to find which ability exactly is being utilized.
        private GroupBox ElementCreate(VisualElement parent, int abilityIndex, Texture2D iconImage, bool locked = false, bool item = false, string pathPrefix = "") // Ability or inventory element creator
        {
            GroupBox button = new GroupBox();
            button.name = pathPrefix + abilityIndex.ToString();
            if (item)
            {
                button.AddToClassList("ItemButton");
                button.RegisterCallback<MouseEnterEvent>(InventoryDescriptor);
                button.RegisterCallback<MouseLeaveEvent>(HideDescriptor);

                button.style.backgroundImage = iconImage;
            }
            else
            {
                button.AddToClassList("AbilityButton");
                button.RegisterCallback<MouseEnterEvent>(AbilityDescriptor);
                button.RegisterCallback<MouseLeaveEvent>(HideDescriptor);
                if (locked) button.AddToClassList("emptySlot");

                GroupBox icon = new GroupBox();
                icon.name = pathPrefix + abilityIndex.ToString();
                icon.style.backgroundImage = iconImage;
                icon.AddToClassList("AbilityButtonIcon");
                if (locked) icon.AddToClassList("locked");

                button.Add(icon);
            }
            parent.Add(button);

            return button;
        }

        private void EmptyElementCreate(VisualElement parent, int abilityIndex, bool item = false, bool unavailableSlot = false) // Ability or inventory element creator
        {
            GroupBox button = new GroupBox();
            if (item)
            {
                button.name = abilityIndex.ToString();
                button.AddToClassList("ItemButton");

                if (unavailableSlot) button.AddToClassList("emptySlot");
            }
            else
            {
                button.name = "null";
                button.AddToClassList("AbilityButton");
            }

            parent.Add(button);
        }

        // Handles clicks of abilities
        void AbilityHandler(ClickEvent evt)
        {
            VisualElement clickedElement = evt.target as VisualElement;

            if (clickedElement.name != "null" && !clickedElement.ClassListContains("locked"))
            {
                Ability clickedAbility = Utils.GetAbilityByIndexName(pc.activeUnit, clickedElement.name, out int abilityIndex);

                if (clickedAbility != null)
                {
                    UseAbility(clickedAbility, abilityIndex, false);
                }
            }
        }

        // Uses the given available ability by the active unit
        private void UseAbility(Ability clickedAbility, int abilityIndex, bool isItem)
        {
            // Handle ability control

            // When leveling any click on ability will cause its levelup
            if (isLeveling && !isItem)
            {
                if (clickedAbility.heroLevelable)
                {
                    if (pc.activeUnit.LevelUpAbilityCommand(clickedAbility, abilityIndex))
                    {
                        // Successful ability level increase
                        ShowLevelButton();
                        RedrawAbilityView();
                        if (pc.activeUnit.levelingUnit.abilityPoints == 0)
                        {
                            HideLevelButton(true);
                        }
                    }
                }
                else if (clickedAbility.type == AbilityType.Container)
                {
                    // Add container index, redraw ability view and show return button
                    openContainersIndex.Add(abilityIndex);
                    RedrawAbilityView();
                    ShowReturnButton();
                }
            }
            else
            {
                // Check if ability unlocked - abilities inside containers will inherit the lock state from container itself
                if (isItem || clickedAbility.heroLevelable || !pc.activeUnit.abilityLocked[abilityIndex])
                {
                    // Check if is cooldown ok
                    if (!pc.activeUnit.IsCooldownGood(abilityIndex, isItem)) return;

                    // Container
                    if (!isItem && clickedAbility.type == AbilityType.Container)
                    {
                        // Add container index, redraw ability view and show return button
                        openContainersIndex.Add(abilityIndex);
                        RedrawAbilityView();
                        ShowReturnButton();
                    }

                    // Item 
                    else if (!isItem && clickedAbility.isItem)
                    {
                        pc.activeUnit.BuyItem(abilityIndex);
                    }

                    // Area ability
                    else if (clickedAbility.type == AbilityType.Area)
                    {
                        int lvl = (isItem) ? 0 : pc.activeUnit.abilityLevel[abilityIndex];
                        pc.ChangeMode(PCMode.Area, clickedAbility.radius[lvl], abilityIndex, isItem, clickedAbility);
                        ShowCancelButton();
                    }

                    // Location ability
                    else if (clickedAbility.type == AbilityType.Location)
                    {
                        pc.ChangeMode(PCMode.Position, 0, abilityIndex, isItem, clickedAbility);
                        ShowCancelButton();
                    }

                    // Unit ability
                    else if (clickedAbility.type == AbilityType.Unit)
                    {
                        pc.ChangeMode(PCMode.Unit, 0, abilityIndex, isItem, clickedAbility);
                        ShowCancelButton();
                    }

                    // Processes
                    else if (clickedAbility.type == AbilityType.Process)
                    {
                        if (clickedAbility is Research)
                        {
                            // Research
                            if (pc.activeUnit.AddProcess(abilityIndex, isItem))
                            {
                                // Redraw ability display
                                RedrawAbilityView();
                            }
                        }
                        else
                        {
                            // Process
                            pc.activeUnit.AddProcess(abilityIndex, isItem);
                        }
                    }

                    // Construction
                    else if (clickedAbility.type == AbilityType.Construction)
                    {
                        int lvl = (isItem) ? 0 : pc.activeUnit.abilityLevel[abilityIndex];
                        pc.ChangeMode(PCMode.Placement, clickedAbility.radius[lvl], abilityIndex, isItem, clickedAbility);
                        ShowCancelButton();
                    }

                    // Toggle
                    else if (clickedAbility.type == AbilityType.Toggle)
                    {
                        pc.activeUnit.UseAbilityItem(abilityIndex, isItem, null, Vector3.zero, true);
                    }

                    // Active
                    else if (clickedAbility.type == AbilityType.Active)
                    {
                        // Last check for UpgradeBuilding
                        if (clickedAbility is UpgradeBuilding && (pc.activeUnit.activeProcess[0] != null || (pc.activeUnit.transportUnit && pc.activeUnit.transportUnit.units.Count > 0))) return;

                        pc.activeUnit.UseAbilityItem(abilityIndex, isItem, null, Vector3.zero, true);
                    }
                }
                else
                {
                    ShowNotifyMsg("This ability is locked", pc.activeUnit.owner, false);
                }
            }
        }

        // COOLDOWNS
        List<VisualElement> cooldownElements = new List<VisualElement>();
        List<Label> cooldownTimers = new List<Label>();
        List<int> cooldownIndex = new List<int>();

        private void CooldownTimerUpdate()
        {
            for (int i = 0; i < cooldownIndex.Count; i++)
            {
                int abilityIndex = cooldownIndex[i];
                if (pc.activeUnit.cooldownAbilityIsItem[abilityIndex])
                {
                    cooldownElements[i].style.scale = new StyleScale(new Vector2(1, pc.activeUnit.cooldownAbility[abilityIndex] / pc.activeUnit.items[pc.activeUnit.cooldownAbilityIndex[abilityIndex]].cooldown[0]));
                }
                else
                {
                    cooldownElements[i].style.scale = new StyleScale(new Vector2(1, pc.activeUnit.cooldownAbility[abilityIndex] / pc.activeUnit.abilities[pc.activeUnit.cooldownAbilityIndex[abilityIndex]].cooldown[pc.activeUnit.abilityLevel[pc.activeUnit.cooldownAbilityIndex[abilityIndex]]]));
                }
                cooldownTimers[i].text = pc.activeUnit.cooldownAbility[abilityIndex].ToString("F2");
            }
        }

        private void AddCooldownElement(VisualElement parent, int abilityCooldownIndex)
        {
            // Cooldown element
            VisualElement cooldownElement = new VisualElement();
            cooldownElement.pickingMode = PickingMode.Ignore;
            cooldownElement.AddToClassList("buttonCD");
            parent.Add(cooldownElement);
            cooldownElements.Add(cooldownElement);

            // Cooldown timer
            Label cdTimer = new Label();
            cdTimer.pickingMode = PickingMode.Ignore;
            cdTimer.AddToClassList("process-timer");
            parent.Add(cdTimer);
            cooldownTimers.Add(cdTimer);

            // Index
            cooldownIndex.Add(abilityCooldownIndex);
        }

        void AbilityDescriptor(MouseEnterEvent evt)
        {
            VisualElement hoveredElement = evt.target as VisualElement;

            if (hoveredElement.name != "null")
            {
                Ability hoveredAbility = Utils.GetAbilityByIndexName(pc.activeUnit, hoveredElement.name, out int abilityIndex);

                if (hoveredAbility != null)
                {
                    // Name set
                    if (pc.activeUnit.abilityLevel[abilityIndex] == -1) descriptorName.text = hoveredAbility.abilityName[0];
                    else if (hoveredAbility.abilityName.Length > pc.activeUnit.abilityLevel[abilityIndex]) descriptorName.text = hoveredAbility.abilityName[pc.activeUnit.abilityLevel[abilityIndex]];
                    else descriptorName.text = hoveredAbility.abilityName[hoveredAbility.abilityName.Length - 1];

                    // Hotkey
                    int elemIndex = abilityScrollView.IndexOf(hoveredElement);
                    if (elemIndex != -1 && hoveredAbility.type != AbilityType.Aura && hoveredAbility.type != AbilityType.Passive)
                    {
                        descriptorHotkey.style.display = DisplayStyle.Flex;

                        if (elemIndex == 0) descriptorHotkey.text = "[Q]";
                        else if (elemIndex == 1) descriptorHotkey.text = "[W]";
                        else if (elemIndex == 2) descriptorHotkey.text = "[E]";
                        else if (elemIndex == 3) descriptorHotkey.text = "[R]";

                        else if (elemIndex == 4) descriptorHotkey.text = "[Z]";
                        else if (elemIndex == 5) descriptorHotkey.text = "[X]";
                        else if (elemIndex == 6) descriptorHotkey.text = "[C]";
                        else if (elemIndex == 7) descriptorHotkey.text = "[V]";

                        else if (elemIndex == 8) descriptorHotkey.text = "[T]";
                        else if (elemIndex == 9) descriptorHotkey.text = "[Y]";
                        else if (elemIndex == 10) descriptorHotkey.text = "[U]";
                        else if (elemIndex == 11) descriptorHotkey.text = "[I]";

                        else if (elemIndex == 12) descriptorHotkey.text = "[B]";
                        else if (elemIndex == 13) descriptorHotkey.text = "[N]";
                        else if (elemIndex == 14) descriptorHotkey.text = "[M]";
                        else if (elemIndex == 15) descriptorHotkey.text = "[G]";

                        else descriptorHotkey.style.display = DisplayStyle.None;
                    }
                    else
                    {
                        descriptorHotkey.style.display = DisplayStyle.None;
                    }

                    // If we are learning an ability we show the next level parameters
                    int abilityLevel = pc.activeUnit.abilityLevel[abilityIndex];
                    if (isLeveling) abilityLevel += 1;

                    // Indicate that this is an item
                    if (hoveredAbility.isItem)
                    {
                        descriptorLevel.style.display = DisplayStyle.Flex;
                        descriptorLevel.text = "[Item]";
                    }
                    // Level
                    else if (hoveredAbility.maxLevels > 0)
                    {
                        descriptorLevel.style.display = DisplayStyle.Flex;
                        descriptorLevel.text = "Level " + (abilityLevel + 1) + "/" + hoveredAbility.maxLevels;
                    }
                    else descriptorLevel.style.display = DisplayStyle.None;

                    // Lock state
                    if (((isLeveling || !hoveredAbility.heroLevelable) && pc.activeUnit.abilityLocked[abilityIndex]))
                    {
                        descriptorLockbox.style.display = DisplayStyle.Flex;
                        descriptorLockNames.Clear();

                        // Display tech that is required for this ability
                        // Level requirements
                        if (hoveredAbility.requiredLevel.Length > abilityLevel)
                        {
                            LockNameCreate("Level " + hoveredAbility.requiredLevel[abilityLevel], "Gain more XP to increase your level.");
                        }
                        // Tech requirements
                        if (hoveredAbility.requiredTech.Length > abilityLevel)
                        {
                            for (int i = 0; i < hoveredAbility.requiredTech[abilityLevel].data.Length; i++)
                            {
                                if (!TechnologyManager.instance.isUnlocked(hoveredAbility.requiredTech[abilityLevel].data[i], pc.activeUnit.owner))
                                {
                                    // Not unlocked, display the name of the tech
                                    LockNameCreate(hoveredAbility.requiredTech[abilityLevel].data[i].displayName, hoveredAbility.requiredTech[abilityLevel].data[i].description);
                                }
                            }
                        }
                    }
                    else
                    {
                        descriptorLockbox.style.display = DisplayStyle.None;
                    }

                    // Cost
                    descriptorUsageCost.Clear();
                    descriptorResourceCost.Clear();
                    descriptorCostBox.style.display = DisplayStyle.None;

                    if (hoveredAbility.manaCost.Length > abilityLevel && hoveredAbility.manaCost[abilityLevel] != 0)
                    {
                        // Mana
                        CostElementCreate(hoveredAbility.manaCost[abilityLevel], true, false);
                        descriptorCostBox.style.display = DisplayStyle.Flex;
                    }
                    if (hoveredAbility.manaCostPerSecond.Length > abilityLevel && hoveredAbility.manaCostPerSecond[abilityLevel] != 0)
                    {
                        // Mana cost per second
                        CostElementCreate(hoveredAbility.manaCostPerSecond[abilityLevel], true, false, null, true);
                        descriptorCostBox.style.display = DisplayStyle.Flex;
                    }

                    if (hoveredAbility.cooldown.Length > abilityLevel && hoveredAbility.cooldown[abilityLevel] != 0)
                    {
                        // Cooldown
                        CostElementCreate(hoveredAbility.cooldown[abilityLevel], false, true);
                        descriptorCostBox.style.display = DisplayStyle.Flex;
                    }

                    // Resource cost
                    if (hoveredAbility.cost.Length > abilityLevel)
                    {
                        for (int i = 0; i < hoveredAbility.cost[abilityLevel].data.Length; i++)
                        {
                            CostElementCreate(hoveredAbility.cost[abilityLevel].data[i].value, false, false, hoveredAbility.cost[abilityLevel].data[i].type.icon);
                            descriptorCostBox.style.display = DisplayStyle.Flex;
                        }
                    }

                    // Description
                    if (pc.activeUnit.abilityLevel[abilityIndex] == -1) descriptorDescription.text = hoveredAbility.description[0];
                    else if (hoveredAbility.description.Length > pc.activeUnit.abilityLevel[abilityIndex]) descriptorDescription.text = hoveredAbility.description[pc.activeUnit.abilityLevel[abilityIndex]];
                    else descriptorDescription.text = hoveredAbility.description[hoveredAbility.abilityName.Length - 1];

                    // Bottom Descriptor
                    descriptorBottomText.style.display = DisplayStyle.Flex;
                    descriptorBottomText.text = "";
                    if (hoveredAbility.castTime.Length > abilityLevel && hoveredAbility.type != AbilityType.Aura && hoveredAbility.type != AbilityType.Passive)
                    {
                        descriptorBottomText.text += "CAST TIME: " + hoveredAbility.castTime[abilityLevel] + "s";
                    }
                    if (hoveredAbility.castRange.Length > abilityLevel && hoveredAbility.type != AbilityType.Aura && hoveredAbility.type != AbilityType.Passive)
                    {
                        if (descriptorBottomText.text != "") descriptorBottomText.text += "\n";
                        descriptorBottomText.text += "CAST RANGE: " + hoveredAbility.castRange[abilityLevel];
                    }
                    if (hoveredAbility.duration.Length > abilityLevel && hoveredAbility.type != AbilityType.Aura && hoveredAbility.type != AbilityType.Passive)
                    {
                        if (descriptorBottomText.text != "") descriptorBottomText.text += "\n";
                        descriptorBottomText.text += "DURATION: " + hoveredAbility.duration[abilityLevel];
                    }
                    if (hoveredAbility.radius.Length > abilityLevel && hoveredAbility.radius[abilityLevel] != 0)
                    {
                        if (descriptorBottomText.text != "") descriptorBottomText.text += "\n";
                        descriptorBottomText.text += "RADIUS: " + hoveredAbility.radius[abilityLevel];
                    }
                    if (descriptorBottomText.text != "") descriptorBottomText.text += "\n";
                    descriptorBottomText.text += "TYPE: " + hoveredAbility.type;

                    if (hoveredAbility.continuous) descriptorBottomText.text += " / CONTINUOUS";

                    descriptor.style.display = DisplayStyle.Flex;
                }
            }
        }

        // INVENTORY -----------------------------------------------------------------------------------------------------------------------------------------------------------------

        void InventoryHandler(PointerUpEvent evt)
        {
            if (pc.activeUnit != null && pc.activeUnit.owner != SlotManager.instance.currentPlayer && !SlotManager.instance.debugMode) return;

            VisualElement clickedElement = evt.target as VisualElement;

            // Names of the UI button are the index of the items in the inventory
            if (int.TryParse(clickedElement.name, out int itemIndex))
            {
                if (isDraggingItem)
                {
                    if (itemIndex == -1 || itemIndex == currentItemIndex)
                    {
                        pc.ChangeMode(PCMode.Default);
                        //ItemDragCancel();
                        return;
                    }

                    // Replace with selected item, item that is being dragged
                    pc.activeUnit.SwapItem(currentItemIndex, itemIndex);

                    pc.ChangeMode(PCMode.Default);
                    //ItemDragCancel();
                }
                else
                {
                    if (itemIndex == -1) return;

                    if (evt.button == 0)
                    {
                        // Left click - Item use
                        if (pc.activeUnit.items[itemIndex] != null) UseAbility(pc.activeUnit.items[itemIndex], itemIndex, true);
                    }
                    else if (evt.button == 1)
                    {
                        // Right click - Inventory Menu
                        if (pc.activeUnit.items[itemIndex] != null)
                        {
                            currentItemIndex = itemIndex;

                            // ItemDragStart(itemIndex);
                            Vector2 mousePositionCorrected = CursorToUIposition();

                            inventoryMenu.style.left = mousePositionCorrected.x;
                            inventoryMenu.style.top = mousePositionCorrected.y;

                            inventoryMenu.style.display = DisplayStyle.Flex;
                            PlayerControl.coreInput.Main.Select.performed += InventoryMenuHide;
                            PlayerControl.coreInput.Main.Command.performed += InventoryMenuHide;
                            PlayerControl.coreInput.Main.Cancel.performed += InventoryMenuHide;
                        }
                    }
                }
            }
        }

        void InventoryDescriptor(MouseEnterEvent evt)
        {
            VisualElement hoveredElement = evt.target as VisualElement;

            if (hoveredElement.name != "null")
            {
                // Names of the buttons are the index of the items of the unit
                if (int.TryParse(hoveredElement.name, out int index))
                {
                    descriptorName.text = pc.activeUnit.items[index].abilityName[0];
                    descriptorDescription.text = pc.activeUnit.items[index].description[0];
                    descriptorLevel.style.display = DisplayStyle.None;
                    descriptorHotkey.style.display = DisplayStyle.None;
                    descriptorLockbox.style.display = DisplayStyle.None;

                    // Cost
                    descriptorUsageCost.Clear();
                    descriptorResourceCost.Clear();
                    descriptorCostBox.style.display = DisplayStyle.None;
                    if (pc.activeUnit.items[index].manaCost.Length > 0 && pc.activeUnit.items[index].manaCost[0] != 0)
                    {
                        // Mana
                        CostElementCreate(pc.activeUnit.items[index].manaCost[0], true, false);
                        descriptorCostBox.style.display = DisplayStyle.Flex;
                    }
                    if (pc.activeUnit.items[index].manaCostPerSecond.Length > 0 && pc.activeUnit.items[index].manaCostPerSecond[0] != 0)
                    {
                        // Mana per second
                        CostElementCreate(pc.activeUnit.items[index].manaCostPerSecond[0], true, false, null, true);
                        descriptorCostBox.style.display = DisplayStyle.Flex;
                    }

                    if (pc.activeUnit.items[index].cooldown.Length > 0 && pc.activeUnit.items[index].cooldown[0] != 0)
                    {
                        // Cooldown
                        CostElementCreate(pc.activeUnit.items[index].cooldown[0], false, true);
                        descriptorCostBox.style.display = DisplayStyle.Flex;
                    }

                    // Resource cost
                    if (pc.activeUnit.items[index].cost.Length > 0)
                    {
                        for (int i = 0; i < pc.activeUnit.items[index].cost[0].data.Length; i++)
                        {
                            CostElementCreate(pc.activeUnit.items[index].cost[0].data[i].value * GameManager.instance.sellPriceReduction, false, false, pc.activeUnit.items[index].cost[0].data[i].type.icon);
                            descriptorCostBox.style.display = DisplayStyle.Flex;
                        }
                    }

                    // Bottom Descriptor
                    descriptorBottomText.style.display = DisplayStyle.Flex;
                    descriptorBottomText.text = "";
                    if (pc.activeUnit.items[index].castTime.Length > 0)
                    {
                        descriptorBottomText.text += "CAST TIME: " + pc.activeUnit.items[index].castTime[0] + "s";
                    }
                    if (pc.activeUnit.items[index].castRange.Length > 0)
                    {
                        descriptorBottomText.text += "\nCAST RANGE: " + pc.activeUnit.items[index].castRange[0];
                    }
                    if (pc.activeUnit.items[index].duration.Length > 0)
                    {
                        descriptorBottomText.text += "\nDURATION: " + pc.activeUnit.items[index].duration[0];
                    }
                    descriptorBottomText.text += "\nTYPE: " + pc.activeUnit.items[index].type;

                    if (pc.activeUnit.items[index].continuous) descriptorBottomText.text += " / CONTINUOUS";

                    descriptor.style.display = DisplayStyle.Flex;
                }
            }
        }

        void InventoryDragStart(InputAction.CallbackContext context)
        {
            if (pc.activeUnit != null && pc.activeUnit.owner != SlotManager.instance.currentPlayer && !SlotManager.instance.debugMode) return;

            // Check if inventory item was clicked
            Vector2 mousePositionCorrected = CursorToUIposition();
            VisualElement picked = uiDocument.rootVisualElement.panel.Pick(mousePositionCorrected);

            // Check if the cursor is over inventory
            if (picked != null && (picked.parent != null && picked.parent.name == "Items"))
            {
                // Names of the UI button are the index of the items in the inventory
                if (int.TryParse(picked.name, out int itemIndex))
                {
                    if (itemIndex == -1) return;
                    if (pc.activeUnit.items[itemIndex] == null) return;

                    ItemDragStart(itemIndex);

                    inventoryDragIcon.style.display = DisplayStyle.Flex;
                    inventoryDragIcon.style.backgroundImage = picked.style.backgroundImage;
                }
            }
        }

        void InventoryDragUpdate()
        {
            Vector2 mousePositionCorrected = CursorToUIposition();

            inventoryDragIcon.style.left = mousePositionCorrected.x;
            inventoryDragIcon.style.top = mousePositionCorrected.y;
        }

        // When we release the mouse button during the drag this function is called. If we did not release on inventory items, we should cancel the drag.
        // Handling release on inventory items is done in InventoryHandler(PointerUpEvent evt)
        void InventoryDragFinish()
        {
            // If cursor is not over UI we try dropping the item
            if (!pc.IsOverUI(Camera_TopDown.instance.GetCursorPosition()))
            {
                Unit unit = Utils.GetUnitAtCursor();
                if (unit == null)
                {
                    // Drop item at position
                    Vector3 point = Utils.TerrainScreenRaycast(Camera_TopDown.instance.GetCursorPosition());
                    if (point != Vector3.zero)
                    {
                        pc.activeUnit.DropItem(currentItemIndex, point);
                    }
                    else
                    {
                        ShowNotifyMsg("Can`t drop item at this location");
                    }
                }
                else
                {
                    // Drop item on unit
                    pc.activeUnit.DropItem(currentItemIndex, unit);
                }

                // if (!pc.activeUnit.DropItem(currentItemIndex))
                // {
                //     ShowNotifyMsg("Can`t drop item at the unit location");
                // }
            }
            else
            {
                // Check if inventoryMenu was clicked
                Vector2 mousePositionCorrected = CursorToUIposition();
                VisualElement picked = uiDocument.rootVisualElement.panel.Pick(mousePositionCorrected);

                if (picked != null && (picked.parent != null && picked.parent.name == "Items")) return;
            }

            pc.ChangeMode(PCMode.Default);
            //ItemDragCancel();
        }

        // If inventory menu does not fit into the screen we change its Y position
        void InventoryMenuReposition(GeometryChangedEvent evt)
        {
            if (uiDocument.rootVisualElement.worldBound.size.y < inventoryMenu.resolvedStyle.top + inventoryMenu.resolvedStyle.height)
            {
                inventoryMenu.style.top = inventoryMenu.resolvedStyle.top - inventoryMenu.resolvedStyle.height;
            }
        }

        // We hide inventoryMenu if any mouse buttons were clicked, we do this after 1 frame so inventory menu click has time to process its logic
        void InventoryMenuHide(InputAction.CallbackContext context)
        {
            // Check if inventoryMenu was clicked
            Vector2 mousePositionCorrected = CursorToUIposition();
            VisualElement picked = uiDocument.rootVisualElement.panel.Pick(mousePositionCorrected);

            if (picked != null && (picked.name == "sellItemButton" || picked.name == "dropItemButton")) return;

            inventoryMenu.style.display = DisplayStyle.None;
            PlayerControl.coreInput.Main.Select.performed -= InventoryMenuHide;
            PlayerControl.coreInput.Main.Command.performed -= InventoryMenuHide;
            PlayerControl.coreInput.Main.Cancel.performed -= InventoryMenuHide;
        }

        void inventoryMenuClick(PointerUpEvent evt, int actionType)
        {
            // 0 is drop
            if (actionType == 0)
            {
                pc.activeUnit.DropItem(currentItemIndex);
            }
            // 1 is sell
            else if (actionType == 1)
            {
                pc.activeUnit.SellItem(currentItemIndex);
            }

            currentItemIndex = 0;
            inventoryMenu.style.display = DisplayStyle.None;
            PlayerControl.coreInput.Main.Select.performed -= InventoryMenuHide;
            PlayerControl.coreInput.Main.Command.performed -= InventoryMenuHide;
            PlayerControl.coreInput.Main.Cancel.performed -= InventoryMenuHide;
        }

        // Hotkey ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // Handle the hotkey press on the abilities and inventory
        private void AnyKeyPressed(InputAction.CallbackContext ctx)
        {
            if (pc.activeUnit.owner != SlotManager.instance.currentPlayer && !SlotManager.instance.debugMode) return;
            if (pc.activeUnit.isBeingBuilt) return;
            if (chatON) return;

            string elemName = "null";
            int inventoryIndex = -1;

            // Command hotkeys
            if (Keyboard.current.aKey.isPressed) AttackMoveButton(); // Attack
            else if (Keyboard.current.hKey.isPressed) HoldButton(); // Hold
            else if (Keyboard.current.sKey.isPressed) StopButton(); // Idle(Stop)
            else if (Keyboard.current.backspaceKey.isPressed) ReturnButton(new ClickEvent()); // One container back/close
            else if (Keyboard.current.lKey.isPressed) LevelButton(new ClickEvent()); // Levelling
            else if (Keyboard.current.kKey.isPressed) ShopButton(new ClickEvent()); // Shoping unit change

            // Ability hotkeys
            else if (Keyboard.current.qKey.isPressed) elemName = abilityScrollView.ElementAt(0).name;
            else if (Keyboard.current.wKey.isPressed) elemName = abilityScrollView.ElementAt(1).name;
            else if (Keyboard.current.eKey.isPressed) elemName = abilityScrollView.ElementAt(2).name;
            else if (Keyboard.current.rKey.isPressed) elemName = abilityScrollView.ElementAt(3).name;

            else if (Keyboard.current.zKey.isPressed) elemName = abilityScrollView.ElementAt(4).name;
            else if (Keyboard.current.xKey.isPressed) elemName = abilityScrollView.ElementAt(5).name;
            else if (Keyboard.current.cKey.isPressed) elemName = abilityScrollView.ElementAt(6).name;
            else if (Keyboard.current.vKey.isPressed) elemName = abilityScrollView.ElementAt(7).name;

            else if (Keyboard.current.tKey.isPressed) elemName = abilityScrollView.ElementAt(8).name;
            else if (Keyboard.current.yKey.isPressed) elemName = abilityScrollView.ElementAt(9).name;
            else if (Keyboard.current.uKey.isPressed) elemName = abilityScrollView.ElementAt(10).name;
            else if (Keyboard.current.iKey.isPressed) elemName = abilityScrollView.ElementAt(11).name;

            else if (Keyboard.current.bKey.isPressed) elemName = abilityScrollView.ElementAt(12).name;
            else if (Keyboard.current.nKey.isPressed) elemName = abilityScrollView.ElementAt(13).name;
            else if (Keyboard.current.mKey.isPressed) elemName = abilityScrollView.ElementAt(14).name;
            else if (Keyboard.current.gKey.isPressed) elemName = abilityScrollView.ElementAt(15).name;

            // Inventory hotkeys - you can add more here by changing inventory index and key pressed
            else if (Keyboard.current.fKey.isPressed) inventoryIndex = 0;

            // Use ability
            if (inventoryIndex == -1)
            {
                Ability ability = Utils.GetAbilityByIndexName(pc.activeUnit, elemName, out int abilityIndex);
                if (ability != null) UseAbility(ability, abilityIndex, false);
            }
            // Use inventory
            else if (pc.activeUnit.items[inventoryIndex] != null)
            {
                UseAbility((Ability)pc.activeUnit.items[inventoryIndex], inventoryIndex, true);
            }
        }

        // CHAT ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

        public void ChatButtonPressed(InputAction.CallbackContext ctx)
        {
            if (chatON)
            {
                string msg = msgInput.value.Trim();
                if (msg != "")
                {
                    msg = msg.Length > msgInput.maxLength ? msg.Substring(0, msgInput.maxLength) : msg;
                    AddChatMsg(msg, SlotManager.instance.currentPlayer, allyChat);
                    // Send info to other players
                    if (NetworkDataSync.instance) NetworkDataSync.instance.MsgSend(msg, allyChat);
                }
            }
            else
            {
                currentChatTime = 0;
                chatBox.style.display = DisplayStyle.Flex;
                msgInput.value = "";
                msgInput.style.display = DisplayStyle.Flex;
                chatON = true;
                if (Keyboard.current.shiftKey.isPressed)
                {
                    allyChat = false;
                    msgInput.label = "All:";
                }
                else
                {
                    allyChat = true;
                    msgInput.label = "Allies:";
                }
                StartCoroutine(ChatDisplayDelay());
            }
        }

        public void ShowChatBox()
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            currentChatTime = 0;
            chatBox.style.display = DisplayStyle.Flex;
        }

        // Focus on msgInput with a 1 frame delay
        IEnumerator ChatDisplayDelay()
        {
            // Wait for 1 frame
            yield return null;
            if (chatON)
            {
                msgInput.Focus();
            }
        }

        // On blur of the chat we hide next frame, so ChatButtonPressed() has time to do its logic
        private void HideChat(BlurEvent evt)
        {
            StartCoroutine(HideChatDelay());
        }

        IEnumerator HideChatDelay()
        {
            // Wait for 1 frame
            yield return null;
            if (chatON)
            {
                msgInput.style.display = DisplayStyle.None;
                chatON = false;
            }
        }

        // CHAT BOX
        public void AddChatMsg(string msg, int owner, bool allyChat)
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            VisualElement wrapper1 = new VisualElement();
            wrapper1.style.flexDirection = FlexDirection.Row;
            chatBox.Add(wrapper1);

            Label playerName = new Label();
            if (allyChat) playerName.text = "[TEAM]" + SlotManager.instance.playerName[owner] + ": ";
            else playerName.text = SlotManager.instance.playerName[owner] + ": ";
            playerName.style.color = SlotManager.instance.playerColors[owner];
            wrapper1.Add(playerName);

            VisualElement wrapper2 = new VisualElement();
            wrapper1.Add(wrapper2);

            Label msgLabel = new Label();
            msgLabel.text = msg;
            wrapper2.Add(msgLabel);

            if (chatBox.childCount > 10)
            {
                chatBox.RemoveAt(0);
            }

            if (SlotManager.instance.debugMode) Cheats.MsgAdded(msg, owner);
        }

        public void AddChatServerMsg(string msg)
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            VisualElement wrapper1 = new VisualElement();
            wrapper1.style.flexDirection = FlexDirection.Row;
            chatBox.Add(wrapper1);

            Label playerName = new Label();
            playerName.text = "Server: ";
            playerName.style.color = Color.gray;
            wrapper1.Add(playerName);

            VisualElement wrapper2 = new VisualElement();
            wrapper1.Add(wrapper2);

            Label msgLabel = new Label();
            msgLabel.text = msg;
            wrapper2.Add(msgLabel);

            if (chatBox.childCount > 10)
            {
                chatBox.RemoveAt(0);
            }
            UIManager.instance.ShowChatBox();

            // Sync
            if (NetworkDataSync.instance && NetworkManager.Singleton.IsServer) NetworkDataSync.instance.ServerMsgSend(msg);
        }

        void ChatTimerUpdate()
        {
            if (currentChatTime < chatTime)
            {
                currentChatTime += GameManager.instance.currentDeltaTime;
                if (currentChatTime > chatTime)
                {
                    chatBox.style.display = DisplayStyle.None;
                }
            }
        }

        // Descriptor ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

        void ShowDescriptor(MouseEnterEvent evt, int type)
        {
            if (type == 0)
            {
                FillDescriptorHotkey("Cancel", "Cancel the current command", "ESC");
            }
            else if (type == 1)
            {
                FillDescriptorHotkey("Return", "Return to main ability view", "Backspace");
            }
            else if (type == 2)
            {
                FillDescriptorHotkey("Learn ability", "Level up unit`s abilities or learn a new skill", "L");
            }
            else if (type == 7)
            {
                FillDescriptorHotkey("Change shopping unit", "Change the unit that is currently using this shop", "K");
            }

            // Commands
            else if (type == 3)
            {
                FillDescriptorHotkey("Move/Follow", "Make a right click on the ground to command selected unit to move to that position, click on a unit will make selected one to follow it, if enemy to attack it.", "Right Click");
            }
            else if (type == 4)
            {
                FillDescriptorHotkey("Stop", "Make currently selected unit stop doing its current objective", "S");
            }
            else if (type == 5)
            {
                FillDescriptorHotkey("Hold", "The unit will hold its position and not move, but attack if another unit comes in its attack range", "H");
            }
            else if (type == 6)
            {
                FillDescriptorHotkey("Attack", "Command to attack selected unit. Command on the ground will make the selected unit move to that position attacking enemy along the way. \nCTRL+Right click on the ground will force the unit attack the ground if it is capable of splash attack", "A");
            }
        }

        void HideDescriptor(PointerLeaveEvent evt)
        {
            descriptor.style.display = DisplayStyle.None;

            // If damage range was being displayed
            if (pc.activeUnit != null)
            {
                pc.HideRangeProjector();
            }
        }

        void HideDescriptor(MouseLeaveEvent evt)
        {
            descriptor.style.display = DisplayStyle.None;

            if (viewingEffectorDescriptor) viewingEffectorDescriptor = false;

            // If damage range was being displayed
            if (pc.activeUnit != null)
            {
                pc.HideRangeProjector();
            }
        }

        void HideDescriptor()
        {
            descriptor.style.display = DisplayStyle.None;
        }

        // Fill the descriptor
        void FillDescriptor(string name, string description)
        {
            descriptorName.text = name;
            descriptorDescription.text = description;
            descriptorBottomText.style.display = DisplayStyle.None;

            descriptorCostBox.style.display = DisplayStyle.None;
            descriptorLockbox.style.display = DisplayStyle.None;
            descriptorHotkey.style.display = DisplayStyle.None;
            descriptorLevel.style.display = DisplayStyle.None;

            descriptor.style.display = DisplayStyle.Flex;
        }

        void FillDescriptor(string name, string description, string level)
        {
            descriptorName.text = name;
            descriptorDescription.text = description;
            descriptorBottomText.style.display = DisplayStyle.None;

            descriptorLevel.text = level;

            descriptorLevel.style.display = DisplayStyle.Flex;

            descriptorCostBox.style.display = DisplayStyle.None;
            descriptorLockbox.style.display = DisplayStyle.None;
            descriptorHotkey.style.display = DisplayStyle.None;

            descriptor.style.display = DisplayStyle.Flex;
        }

        void FillDescriptor(string name, string description, int currentLevel, int maxLevel)
        {
            descriptorName.text = name;
            descriptorDescription.text = description;
            descriptorBottomText.style.display = DisplayStyle.None;

            descriptorLevel.text = "Level " + currentLevel + " / " + maxLevel;

            descriptorLevel.style.display = DisplayStyle.Flex;

            descriptorCostBox.style.display = DisplayStyle.None;
            descriptorLockbox.style.display = DisplayStyle.None;
            descriptorHotkey.style.display = DisplayStyle.None;

            descriptor.style.display = DisplayStyle.Flex;
        }

        void FillDescriptorHotkey(string name, string description, string hotkey)
        {
            descriptorName.text = name;
            descriptorDescription.text = description;
            descriptorHotkey.text = "[" + hotkey.ToUpper() + "]";
            descriptorHotkey.style.display = DisplayStyle.Flex;
            descriptorBottomText.style.display = DisplayStyle.None;

            descriptorCostBox.style.display = DisplayStyle.None;
            descriptorLockbox.style.display = DisplayStyle.None;
            descriptorLevel.style.display = DisplayStyle.None;

            descriptor.style.display = DisplayStyle.Flex;
        }

        void LockNameCreate(string name, string description)
        {
            GroupBox lockNameBox = new GroupBox();
            lockNameBox.AddToClassList("lockName");

            Label techName = new Label();
            techName.AddToClassList("techName");
            techName.AddToClassList("yellow-label");
            techName.text = name;
            lockNameBox.Add(techName);

            Label techDesc = new Label();
            techDesc.AddToClassList("techDescription");
            techDesc.text = description;
            lockNameBox.Add(techDesc);

            descriptorLockNames.Add(lockNameBox);
        }

        void CostElementCreate(float value, bool manaCost = false, bool cooldown = false, Texture2D icon = null, bool perSecond = false)
        {
            GroupBox costBox = new GroupBox();
            costBox.AddToClassList("costGroup");

            GroupBox costIcon = new GroupBox();
            costIcon.AddToClassList("costIcon");
            if (manaCost) costIcon.AddToClassList("manaIcon");
            else if (cooldown) costIcon.AddToClassList("cooldownIcon");
            else costIcon.style.backgroundImage = icon;

            Label costValue = new Label();
            int val = (int)value;
            costValue.text = val.ToString();
            if (perSecond) costValue.text += "/s";
            if (manaCost || cooldown) descriptorUsageCost.Add(costBox);
            else descriptorResourceCost.Add(costBox);
            costBox.Add(costIcon);
            costBox.Add(costValue);
        }

        // Buttons ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

        void StopButton()
        {
            for (int i = 0; i < pc.selectedUnits.Count; i++)
            {
                if (pc.selectedUnits[i].owner == SlotManager.instance.currentPlayer || SlotManager.instance.debugMode)
                {
                    if (!pc.selectedUnits[i].isBeingBuilt)
                    {
                        pc.selectedUnits[i].Idle(true);
                    }
                }
            }
        }

        void HoldButton()
        {
            for (int i = 0; i < pc.selectedUnits.Count; i++)
            {
                if (pc.selectedUnits[i].owner == SlotManager.instance.currentPlayer || SlotManager.instance.debugMode)
                {
                    if (!pc.selectedUnits[i].isBeingBuilt)
                    {
                        pc.selectedUnits[i].Hold(true);
                    }
                }
            }
        }

        void AttackMoveButton()
        {
            pc.ChangeMode(PCMode.AttackMove);
            ShowCancelButton();
        }

        // When you are casting a spell it will cancel that action and go back to the default state
        void CancelButton(ClickEvent evt)
        {
            // PlayerControl mode reset
            pc.ChangeMode(PCMode.Default);

            // Ability leveling off
            LevellingToggle(false);
            RedrawAbilityView();

            // If a building being built, cancel the construction
            if (pc.activeUnit.isBeingBuilt)
            {
                pc.activeUnit.constructionUnit.CancelConstruction(false);
            }
        }

        public void ShowCancelButton()
        {
            cancelButton.style.display = DisplayStyle.Flex;
        }

        public void HideCancelButton()
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            cancelButton.style.display = DisplayStyle.None;
        }

        // Return button, will reset ability view
        void ReturnButton(ClickEvent evt)
        {
            // Remove last container opened container
            if (openContainersIndex.Count != 0)
            {
                openContainersIndex.RemoveAt(openContainersIndex.Count - 1);
                RedrawAbilityView();
            }

            if (openContainersIndex.Count == 0) HideReturnButton();
        }

        public void ShowReturnButton()
        {
            returnButton.style.display = DisplayStyle.Flex;
        }

        public void HideReturnButton()
        {
            returnButton.style.display = DisplayStyle.None;
        }

        // Level button, will allow to choose skill to learn or level up
        void LevelButton(ClickEvent evt)
        {
            if (!pc.activeUnit.levelingUnit || pc.activeUnit.levelingUnit.abilityPoints == 0) return;

            if (isLeveling) LevellingToggle(false);
            else LevellingToggle(true);

            RedrawAbilityView();
        }

        public void LevellingToggle(bool on)
        {
            if (on)
            {
                levelButton.RemoveFromClassList("commandButtonActive");
                isLeveling = true;
            }
            else
            {
                levelButton.AddToClassList("commandButtonActive");
                isLeveling = false;
            }
        }

        public void ShowLevelButton()
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            // Check if unit belongs to the current player
            if (!SlotManager.instance.debugMode && pc.activeUnit.owner != SlotManager.instance.currentPlayer) return;

            Label levelLabel = (Label)levelButton.ElementAt(1);
            levelLabel.text = pc.activeUnit.levelingUnit.abilityPoints.ToString();
            levelButton.style.display = DisplayStyle.Flex;
        }

        public void HideLevelButton(bool levelingOff = false)
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            if (levelingOff) LevellingToggle(false);
            levelButton.style.display = DisplayStyle.None;
        }

        public void ShopButton(ClickEvent evt)
        {
            if (!pc.activeUnit.isShop) return;
            pc.ChangeMode(PCMode.ShopUnit); // Shop unit change
            ShowCancelButton();
        }

        // Transport ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

        public void TransportDisplay()
        {
            if (pc.activeUnit.transportUnit && pc.activeUnit.transportUnit.units.Count > 0)
            {
                processesUI.style.display = DisplayStyle.None;
                transportUI.style.display = DisplayStyle.Flex;
                transportUI.Clear();

                for (int i = 0; i < pc.activeUnit.transportUnit.units.Count; i++)
                {
                    for (int c = 0; c < pc.activeUnit.transportUnit.units[i].transportWeight; c++)
                    {
                        // Main display
                        if (c == 0) ProcessElementCreate(transportUI, false, i, pc.activeUnit.transportUnit.units[i].icon, -1);
                        // Display of transport weight
                        else ProcessElementCreate(transportUI, true, i, pc.activeUnit.transportUnit.units[i].icon);
                    }
                }

                // Add empty slots
                for (int c = 0; c < pc.activeUnit.transportUnit.capacity - pc.activeUnit.transportUnit.currentCapacity; c++)
                {
                    ProcessElementCreate(transportUI, true);
                }
            }
            else
            {
                transportUI.style.display = DisplayStyle.None;

                if (pc.activeUnit.canProcess && pc.activeUnit.activeProcess[0] != null && processesUI.style.display == DisplayStyle.None) ProcessDisplay();
            }
        }

        // When process is clicked in processUI, it is canceled
        private void TransportClick(ClickEvent evt)
        {
            VisualElement clickedElement = evt.target as VisualElement;

            // Name is the index of the process
            if (int.TryParse(clickedElement.name, out int index))
            {
                if (pc.activeUnit.owner == SlotManager.instance.currentPlayer || SlotManager.instance.debugMode)
                {
                    pc.activeUnit.transportUnit.Disembark(index, pc.activeUnit.transform.position);
                }
                else
                {
                    ShowNotifyMsg("Very funny! This unit is not yours to control!");
                }
            }
        }

        public void HideTransport()
        {
            transportUI.style.display = DisplayStyle.None;
        }

        // Processes ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

        // If unit has any processes it will show Processes UI
        public void ProcessDisplay()
        {
            if (pc.activeUnit.transportUnit && pc.activeUnit.transportUnit.units.Count > 0) return;

            transportUI.style.display = DisplayStyle.None;
            processesUI.style.display = DisplayStyle.Flex;
            processesUI.Clear();

            for (int i = 0; i < GameManager.maxProcessCount; i++)
            {
                if (pc.activeUnit.activeProcess[i] != null)
                {
                    if (pc.activeUnit.activeProcess[i].icon.Length > pc.activeUnit.processLevel[i]) ProcessElementCreate(processesUI, false, i, pc.activeUnit.activeProcess[i].icon[pc.activeUnit.processLevel[i]], pc.activeUnit.activeProcess[i].castTime[pc.activeUnit.processLevel[i]]);
                    else ProcessElementCreate(processesUI, false, i, pc.activeUnit.activeProcess[i].icon[pc.activeUnit.activeProcess[i].icon.Length - 1], pc.activeUnit.activeProcess[i].castTime[pc.activeUnit.processLevel[i]]);
                }
                else
                {
                    ProcessElementCreate(processesUI, true);
                }
            }
        }

        // When process is clicked in processUI, it is canceled
        private void ProcessClick(ClickEvent evt)
        {
            VisualElement clickedElement = evt.target as VisualElement;

            // Name is the index of the process
            if (int.TryParse(clickedElement.name, out int index))
            {
                if (pc.activeUnit.owner == SlotManager.instance.currentPlayer)
                {
                    if (pc.activeUnit.CancelProcess(index))
                    {
                        RedrawAbilityView();
                    }
                }
                else
                {
                    ShowNotifyMsg("Very funny! This unit is not yours to control!");
                }
            }
        }

        private void ProcessTimerUpdate()
        {
            if (pc.activeUnit.activeProcess[0] != null && processesUI.style.display == DisplayStyle.Flex)
            {
                // Process exists
                processTimer = (Label)processesUI.ElementAt(0).ElementAt(1);
                processTimer.text = pc.activeUnit.currentProcessTimer.ToString("F2");
            }
        }

        public void ShowProcesses()
        {
            processesUI.style.display = DisplayStyle.Flex;
        }

        public void HideProcesses()
        {
            processesUI.style.display = DisplayStyle.None;
        }

        void ProcessElementCreate(VisualElement parent, bool empty, int index = 0, Texture2D icon2D = null, float timer = 0)
        {
            if (empty)
            {
                GroupBox button = new GroupBox();
                button.AddToClassList("ItemButton");
                button.AddToClassList("emptySlot");
                if (icon2D != null)
                {
                    button.name = index.ToString();
                    button.style.backgroundImage = icon2D;
                }
                else button.name = "empty";
                parent.Add(button);
            }
            else
            {
                GroupBox button = new GroupBox();
                button.AddToClassList("ItemButton");
                button.name = index.ToString();
                parent.Add(button);

                GroupBox icon = new GroupBox();
                icon.AddToClassList("AbilityButtonIcon");
                icon.style.backgroundImage = icon2D;
                icon.pickingMode = PickingMode.Ignore;
                button.Add(icon);

                if (timer != -1)
                {
                    Label timerLabel = new Label();
                    timerLabel.AddToClassList("process-timer");
                    timerLabel.text = timer.ToString("F2");
                    timerLabel.pickingMode = PickingMode.Ignore;
                    button.Add(timerLabel);
                }
            }
        }

        // Inventory ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

        public void ItemDragStart(int index)
        {
            currentItemIndex = index;
            isDraggingItem = true;
            pc.ChangeMode(PCMode.DragDrop);
            ShowCancelButton();
        }

        public void ItemDragCancel()
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            inventoryDragIcon.style.display = DisplayStyle.None;
            isDraggingItem = false;
        }

        // EFFECTORS ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

        private bool viewingEffectorDescriptor = false; // To make sure we hide the descriptor if player was hovering over the effector when it was removed

        public void DisplayStatusTab()
        {
            statusWindow.Clear();
            statusWindow.style.display = DisplayStyle.Flex;

            if (viewingEffectorDescriptor)
            {
                HideDescriptor();
                viewingEffectorDescriptor = false;
            }

            for (int i = 0; i < pc.activeUnit.effectors.Count; i++)
            {
                if (pc.activeUnit.effectors[i].effector.stacks) continue; // We skip stackable effectors
                StatusEffectorCreate(pc.activeUnit.effectors[i].effector.id, pc.activeUnit.effectors[i].effector.icon);
            }
        }

        public void HideStatusTab()
        {
            statusWindow.style.display = DisplayStyle.None;
        }

        void StatusEffectorCreate(int effectorID, Texture2D icon)
        {
            GroupBox button = new GroupBox();
            button.name = effectorID.ToString();

            button.AddToClassList("statusElement");
            button.RegisterCallback<MouseEnterEvent>(EffectorDescriptor);
            button.RegisterCallback<MouseLeaveEvent>(HideDescriptor);

            button.style.backgroundImage = icon;

            statusWindow.Add(button);
        }

        void EffectorDescriptor(MouseEnterEvent evt)
        {
            VisualElement hoveredElement = evt.target as VisualElement;

            // Names of the groupBox are the index of the resourceTypes
            if (int.TryParse(hoveredElement.name, out int index))
            {
                Effector hoveredEffector = Effector.GetEffectorByID(index);
                if (hoveredEffector)
                {
                    FillDescriptor(hoveredEffector.displayName, hoveredEffector.description);
                    viewingEffectorDescriptor = true;
                }
            }
        }

        // MiniMap ---------------------------------------------------------------------------------------------------------------------------------------------------------------------
        void MiniMapClick(PointerDownEvent evt)
        {
            if (PlayerControl.coreInput.Main.MiniMapPing.IsPressed()) return;

            float x = Mathf.Clamp01(evt.localPosition.x / miniMap.resolvedStyle.width);
            float y = Mathf.Clamp01(evt.localPosition.y / miniMap.resolvedStyle.height);

            cameraRig.SetPosition(new Vector3(x * Grid.instance.width, 0, (1 - y) * Grid.instance.height));
        }

        void MiniMapMoveEvent(PointerMoveEvent evt)
        {
            if (PlayerControl.coreInput.Main.MiniMapPing.IsPressed()) return;

            if (PlayerControl.coreInput.Main.Select.IsPressed())
            {
                float x = Mathf.Clamp01(evt.localPosition.x / miniMap.resolvedStyle.width);
                float y = Mathf.Clamp01(evt.localPosition.y / miniMap.resolvedStyle.height);

                cameraRig.SetPosition(new Vector3(x * Grid.instance.width, 0, (1 - y) * Grid.instance.height));
            }
        }

        // Alt+Click on minimap will trigger ping
        void MiniMapPing(ClickEvent evt)
        {
            if (PlayerControl.coreInput.Main.MiniMapPing.IsPressed())
            {
                CreatePinger(SlotManager.instance.currentPlayer, new Vector2(evt.localPosition.x - 17.5f, evt.localPosition.y - 17.5f));

                // Send info to network team players
                if (NetworkDataSync.instance) NetworkDataSync.instance.MiniMapPingSend(SlotManager.instance.currentPlayer, new Vector2(evt.localPosition.x - 17.5f, evt.localPosition.y - 17.5f));
            }
        }

        void MinimapPingUpdate()
        {
            for (int i = pingElements.Count - 1; i >= 0; i--)
            {
                // Change opacity
                Color tintColor = pingElements[i].pingElement.style.unityBackgroundImageTintColor.value;
                tintColor.a += 0.5f * Utils.minimapPingDuration * Time.deltaTime * pingElements[i].opacityMultiplier;

                if (tintColor.a > 0.98f) pingElements[i].opacityMultiplier = -1;
                else if (tintColor.a < 0.3f) pingElements[i].opacityMultiplier = 1;

                pingElements[i].pingElement.style.unityBackgroundImageTintColor = tintColor;

                // Duration
                pingElements[i].currentTime += Time.deltaTime;

                // Remove
                if (pingElements[i].currentTime > Utils.minimapPingDuration)
                {
                    pingElements[i].pingElement.parent.Remove(pingElements[i].pingElement);
                    pingElements.RemoveAt(i);
                }
            }
        }

        public void CreatePinger(int owner, Vector2 pos)
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            VisualElement pinger = new VisualElement();
            pinger.pickingMode = PickingMode.Ignore;

            pinger.AddToClassList("pingMiniMap");
            pinger.style.left = pos.x;
            pinger.style.top = pos.y;

            pinger.style.unityBackgroundImageTintColor = SlotManager.instance.playerColors[owner];

            miniMap.parent.Add(pinger);

            pingElements.Add(new MiniMapPinger(pinger, owner));
        }

        public void WorldToMiniMapPing(float x, float y)
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            CreatePinger(SlotManager.instance.currentPlayer, new Vector2(miniMap.resolvedStyle.width * x - 17.5f, miniMap.resolvedStyle.height - miniMap.resolvedStyle.height * y - 17.5f)); // 17.5f is half the size of ping ui element

            // Send info to network team players
            if (NetworkDataSync.instance) NetworkDataSync.instance.MiniMapPingSend(SlotManager.instance.currentPlayer, new Vector2(miniMap.resolvedStyle.width * x - 17.5f, miniMap.resolvedStyle.height - miniMap.resolvedStyle.height * y - 17.5f));
        }

        // ResourcesTab ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

        private void ResourceTabCreate()
        {
            for (int i = 0; i < GameResources.instance.gameResources.Length; i++)
            {
                ResourceGroupBoxCreate(i);
            }
        }

        private void ResourceGroupBoxCreate(int index)
        {
            //GroupBox resourceWrapper = new GroupBox();
            //resourceWrapper.name = index.ToString();
            //resourceWrapper.AddToClassList("resource-wrapper");
            //
            //resourceWrapper.RegisterCallback<PointerEnterEvent>(ResourceDescriptor);
            //resourceWrapper.RegisterCallback<PointerLeaveEvent>(HideDescriptor);

            GroupBox resourceGroupBox = new GroupBox();
            resourceGroupBox.name = index.ToString();
            resourceGroupBox.AddToClassList("resourceBox");

            resourceGroupBox.RegisterCallback<PointerEnterEvent>(ResourceDescriptor);
            resourceGroupBox.RegisterCallback<PointerLeaveEvent>(HideDescriptor);

            Label count = new Label();
            count.name = "Count";
            count.AddToClassList("resourceCount");
            resourceGroupBox.Add(count);

            Label icon = new Label();
            icon.name = "Icon";
            icon.AddToClassList("resourceIcon");
            icon.style.backgroundImage = GameResources.instance.gameResources[index].type.icon; ;
            resourceGroupBox.Add(icon);

            resourceTab.Add(resourceGroupBox);
        }

        private void ResourceDescriptor(PointerEnterEvent evt)
        {
            VisualElement hoveredElement = evt.target as VisualElement;

            if (hoveredElement.name != "null")
            {
                // Names of the groupBox are the index of the resourceTypes
                if (int.TryParse(hoveredElement.name, out int index))
                {
                    FillDescriptor(GameResources.instance.gameResources[index].type.displayName, GameResources.instance.gameResources[index].type.description);
                }
            }
        }

        public void UpdateResourceTab(int resourceIndex)
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            if (resourceTab == null) return; // GameResource calls it in Awake, it is defined in start of UIManager

            var countLabel = (Label)resourceTab.ElementAt(resourceIndex).ElementAt(0);
            if (GameResources.instance.gameResources[resourceIndex].type.limited)
            {
                // Limited resource
                countLabel.text = GameResources.instance.playerResources[resourceIndex + SlotManager.instance.currentPlayer * GameResources.instance.gameResources.Length].ToString() + " / " + GameResources.instance.playerResourceLimits[resourceIndex + SlotManager.instance.currentPlayer * GameResources.instance.gameResources.Length];
            }
            else
            {
                // Standard resource
                countLabel.text = GameResources.instance.playerResources[resourceIndex + SlotManager.instance.currentPlayer * GameResources.instance.gameResources.Length].ToString();
            }
        }

        // Refreshes the resource tab to match actual resource values
        public void RefreshResourceTab()
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            for (int i = 0; i < GameResources.instance.gameResources.Length; i++)
            {
                UpdateResourceTab(i);
            }
        }

        // MESSAGES ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Show the message to local player, usually for messages that are direct feedbacks to UI/Player actions.
        /// </summary>
        /// <param name="msg">Message to show.</param>
        public void ShowNotifyMsg(string msg)
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            notifyMsg.text = msg;
            notifyMsg.style.display = DisplayStyle.Flex;
            currentMsgTime = 0;
        }

        /// <summary>
        /// Show the message to specified player.
        /// </summary>
        /// <param name="msg">Message to show.</param>
        /// <param name="player">Player to show.</param>
        /// <param name="calledByServer">If called by the server, it will send the message to specified client player.</param>
        public void ShowNotifyMsg(string msg, int player, bool calledByServer = false)
        {
            if (SlotManager.instance.currentPlayer != player)
            {
                if (calledByServer)
                {
                    // Send the message to client
                    if (NetworkManager.Singleton.IsServer) NetworkDataSync.instance.GameMsgSend(msg, player);
                }
                else return;
            }

            // [Interflow fix 2026-06-26 путь1] Гейт ПОСЛЕ релая GameMsgSend: сервер уже отправил сообщение клиенту; локальный UI рисуем только когда презентация готова.
            if (!presentationReady) return;

            notifyMsg.text = msg;
            notifyMsg.style.display = DisplayStyle.Flex;
            currentMsgTime = 0;
        }

        // Hide message after specified period of time
        void MsgTimerUpdate()
        {
            if (currentMsgTime < msgTime)
            {
                currentMsgTime += GameManager.instance.currentDeltaTime;
                if (currentMsgTime > msgTime)
                {
                    notifyMsg.style.display = DisplayStyle.None;
                }
            }
        }

        // Debug ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

        private void SetUnitInfo()
        {
            Label name = (Label)debugWindow.ElementAt(0);
            name.text = pc.activeUnit.unitName;

            Label info = (Label)debugWindow.ElementAt(1);

            info.text = "netID " + pc.activeUnit.netID + "\n";

            info.text += "State " + pc.activeUnit.unitState.ToString() + "\n";

            info.text += "IsMoving " + pc.activeUnit.isMoving + "\n";

            info.text += "Grid chunk " + pc.activeUnit.currentCell.x + ", " + pc.activeUnit.currentCell.y + "\n";

            info.text += "FoWCell " + pc.activeUnit.FoWCell.x + ", " + pc.activeUnit.FoWCell.y + "\n";

            info.text += "FowVisibile " + pc.activeUnit.FoWVisible + "\n";

            // info.text += "Units in cell \n";
            // int cellIndex = pc.activeUnit.currentCell.x + pc.activeUnit.currentCell.y * Grid.chunkCountX;
            // foreach (Unit u in Grid.chunkUnits[cellIndex])
            // {
            //     info.text += u.unitName + ", ";
            // }
            // info.text += "\n";

            // Resource collection
            if (pc.activeUnit.resourceUnit)
            {
                info.text += "isCollecting " + pc.activeUnit.resourceUnit.isCollecting + "\n";

                info.text += "CurrentlyHas: \n";
                for (int i = 0; i < pc.activeUnit.resourceUnit.currentHeldResources.Count; i++)
                {
                    info.text += pc.activeUnit.resourceUnit.currentHeldResources[i].type.displayName + " = " + pc.activeUnit.resourceUnit.currentHeldResources[i].value + "\n";
                }
            }

            if (pc.activeUnit.canBeSeenCount.Length > 0)
            {
                info.text += "canBeSeenCount: + " + pc.activeUnit.canBeSeenCount[SlotManager.instance.currentTeam] + "\n";
            }
        }

        // UTILS ---------------------------------------------------------------------------------------------------------------------------------------------------------------------

        public void ResetMiniMap()
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            if (miniMap == null) miniMap = uiDocument.rootVisualElement.Q("MiniMap").Q("Overlay");
            miniMap.style.backgroundImage = FogOfWar.instance.visionMask;
        }

        // Returns cursors position in UI space
        public Vector2 CursorToUIposition()
        {
            if (!presentationReady) return default;   // [Interflow fix 2026-06-26 путь1]
            Vector2 mousePosition = Camera_TopDown.instance.GetCursorPosition(); // Mouse.current.position.ReadValue();
            Vector2 mousePositionCorrected = new Vector2(mousePosition.x + 10, Screen.height - mousePosition.y + 10);
            mousePositionCorrected = RuntimePanelUtils.ScreenToPanel(uiDocument.rootVisualElement.panel, mousePositionCorrected);
            return mousePositionCorrected;
        }
    }
}
