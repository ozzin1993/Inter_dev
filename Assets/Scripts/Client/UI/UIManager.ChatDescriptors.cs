using Camera_TopDownNS;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // UIManager.ChatDescriptors.cs — чат + общие дескрипторы. Вырезано 1:1 из UIManager.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class UIManager
    {
        // Handle the hotkey press on the abilities and inventory
        private void AnyKeyPressed(InputAction.CallbackContext ctx)
        {
            if (pc.activeUnit.owner != SlotManager.Instance.currentPlayer && !SlotManager.Instance.debugMode) return;
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
                    AddChatMsg(msg, SlotManager.Instance.currentPlayer, allyChat);
                    // Send info to other players
                    if (NetworkDataSync.Instance) NetworkDataSync.Instance.MsgSend(msg, allyChat);
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
            if (allyChat) playerName.text = "[TEAM]" + SlotManager.Instance.playerName[owner] + ": ";
            else playerName.text = SlotManager.Instance.playerName[owner] + ": ";
            playerName.style.color = SlotManager.Instance.playerColors[owner];
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

            if (SlotManager.Instance.debugMode) Cheats.MsgAdded(msg, owner);
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
            UIManager.Instance.ShowChatBox();

            // [Interflow fix 2026-08-01 ADR-005] Серверный релей ServerMsgSend перенесён в Presentation.ChatServerMsg (хаб).
        }

        void ChatTimerUpdate()
        {
            if (currentChatTime < chatTime)
            {
                currentChatTime += GameManager.Instance.currentDeltaTime;
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
    }
}
