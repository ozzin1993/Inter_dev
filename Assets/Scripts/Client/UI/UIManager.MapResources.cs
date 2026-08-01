using Camera_TopDownNS;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // UIManager.MapResources.cs — миникарта + ресурсы + уведомления. Вырезано 1:1 из UIManager.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class UIManager
    {

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
            // [Interflow fix 2026-08-01 ADR-005] Серверный релей GameMsgSend перенесён в Presentation.NotifyMsg (хаб) — здесь только локальная отрисовка; фильтр адресата сохранён 1:1.
            if (SlotManager.instance.currentPlayer != player)
            {
                if (!calledByServer) return;
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
