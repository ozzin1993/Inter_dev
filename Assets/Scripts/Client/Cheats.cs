using System;
using Camera_TopDownNS;
using UnityEngine;
namespace StrategyCore
{
    public class Cheats
    {
        public static GameObject textGO;

        // CHEATS CAN BE DELETED, THEY ARE FOR DEBUGGING ONLY

        public static void MsgAdded(string msg, int owner)
        {
            // [Interflow fix 2026-08-07 status-cheats] Читы проверки шкалы статусов (status/skill/nostatus/dummy/bld/rows).
            // Вся логика — в нашем Diagnostics/InterflowStatusCheats.cs; здесь только точка входа.
            if (InterflowStatusCheats.Handle(msg, NetworkConnectionHandler.isClient)) return;

            if (msg == "stats") Stats();
            else if (msg == "edge") EdgeScroll();
            else if (msg.StartsWith("sub ")) Sub(msg);
            else if (msg == "unsub") Unsub();

            if (!NetworkConnectionHandler.isClient)
            {
                if (msg == "pause") GamePause();
                else if (msg.StartsWith("speed ")) Speed(msg);
                else if (msg == "play") GamePlay();
                else if (msg == "warpten") Warpten();
                else if (msg == "clear") Clear();
                else if (msg == "save") Save();
                else if (msg == "load") Load();
                else if (msg == "host") Host();
                else if (msg == "join") Join();
                else if (msg == "start") StartScene();
                else if (msg == "pos") SetPosition();
                else if (msg == "greed") Greed();
                else if (msg == "chunk") Chunk();
                else if (msg == "hide") Hide();
            }
        }

        public static void Stats()
        {
            Debug.Log("NetID " + PlayerControl.instance.activeUnit.netID);
            Debug.Log("State " + PlayerControl.instance.activeUnit.unitState);
            Debug.Log("IsMoving " + PlayerControl.instance.activeUnit.isMoving);
            Debug.Log("Target destination ");
            Debug.Log(PlayerControl.instance.activeUnit.targetPosition);
            Debug.Log("Agent destination ");
            Debug.Log(PlayerControl.instance.activeUnit.agent.destination);
        }

        public static void Speed(string msg)
        {
            if (float.TryParse(msg.Replace("speed ", ""), out float s))
            {
                Time.timeScale = s;
            }
        }

        public static void EdgeScroll()
        {
            if (Camera_TopDown.instance.edgeMoveSensitivity == 0)
                Camera_TopDown.instance.edgeMoveSensitivity = 0.7f;
            else 
                Camera_TopDown.instance.edgeMoveSensitivity = 0;
        }

        public static void Sub(string msg)
        {
            if (ushort.TryParse(msg.Replace("sub ", ""), out ushort netID))
            {
                if (SlotManager.instance.unitNetID.TryGetValue(netID, out Unit unit))
                {
                    PlayerControl.instance.activeUnit = unit;
                    UIManager.instance.SubscribeToUnit();
                }
            }
        }

        public static void Unsub()
        {
            if (PlayerControl.instance.activeUnit != null)
                UIManager.instance.UnsubscribeToUnit(PlayerControl.instance.activeUnit);
        }

        public static void Hide()
        {
            if (textGO == null) textGO = GameObject.Find("Texts");
            if (textGO != null)
            {
                if (textGO.activeSelf) textGO.SetActive(false);
                else textGO.SetActive(true);
            }
        }

        public static void Chunk()
        {
            Debug.Log(PlayerControl.instance.activeUnit.currentCell.x + PlayerControl.instance.activeUnit.currentCell.y * Grid.chunkCountX);
        }

        public static void SetPosition()
        {
            Vector2 pos2D = new Vector2(PlayerControl.instance.activeUnit.transform.position.x + 1f, PlayerControl.instance.activeUnit.transform.position.z + 1f);
            PlayerControl.instance.activeUnit.transform.position = new Vector3(pos2D.x, Utils.GetTerrainHeight(pos2D), pos2D.y);
        }

        public static void Greed()
        {
            for (int i = 0; i < GameResources.instance.gameResources.Length; i++)
            {
                for (int p = 0; p < Enum.GetNames(typeof(Players)).Length; p++)
                {
                    // Initialize playerResourceLimits
                    if (GameResources.instance.gameResources[i].type.limited)
                    {
                        // Change limit
                        GameResources.instance.playerResourceLimits[i + p * GameResources.instance.gameResources.Length] = 9999999;
                    }
                    else
                    {
                        // Regular resource
                        GameResources.instance.ChangeAmount(p, new ResourceWrapper(GameResources.instance.gameResources[i].type, 9999999), 1, false, true);
                    }

                    if (SlotManager.instance.currentPlayer == p) UIManager.instance.UpdateResourceTab(i);
                }
            }
        }

        public static void GamePause()
        {
            Time.timeScale = 0f;
        }

        public static void GamePlay()
        {
            Time.timeScale = 1f;
        }

        public static void Warpten()
        {
            Time.timeScale = 10f;
        }

        public static void Clear()
        {

            GameManager.instance.ClearScene();
        }

        public static void Save()
        {
            SaveManager.SaveToFile();
        }

        public static void Load()
        {
            SaveManager.SaveUnitData();
            // SaveManager.LoadSaveFile();
        }

        public static void Host()
        {
            NetworkConnectionHandler.instance.StartHost(SlotManager.instance.currentName);

        }

        public static void Join()
        {
            NetworkConnectionHandler.instance.StartClient(SlotManager.instance.currentName);
        }

        public static void StartScene()
        {
            SceneHandler.instance.LoadScene();
        }
    }
}