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
            Debug.Log("NetID " + PlayerControl.Instance.activeUnit.netID);
            Debug.Log("State " + PlayerControl.Instance.activeUnit.unitState);
            Debug.Log("IsMoving " + PlayerControl.Instance.activeUnit.isMoving);
            Debug.Log("Target destination ");
            Debug.Log(PlayerControl.Instance.activeUnit.targetPosition);
            Debug.Log("Agent destination ");
            Debug.Log(PlayerControl.Instance.activeUnit.agent.destination);
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
            if (Camera_TopDown.Instance.edgeMoveSensitivity == 0)
                Camera_TopDown.Instance.edgeMoveSensitivity = 0.7f;
            else 
                Camera_TopDown.Instance.edgeMoveSensitivity = 0;
        }

        public static void Sub(string msg)
        {
            if (ushort.TryParse(msg.Replace("sub ", ""), out ushort netID))
            {
                if (SlotManager.Instance.unitNetID.TryGetValue(netID, out Unit unit))
                {
                    PlayerControl.Instance.activeUnit = unit;
                    UIManager.Instance.SubscribeToUnit();
                }
            }
        }

        public static void Unsub()
        {
            if (PlayerControl.Instance.activeUnit != null)
                UIManager.Instance.UnsubscribeToUnit(PlayerControl.Instance.activeUnit);
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
            Debug.Log(PlayerControl.Instance.activeUnit.currentCell.x + PlayerControl.Instance.activeUnit.currentCell.y * Grid.chunkCountX);
        }

        public static void SetPosition()
        {
            Vector2 pos2D = new Vector2(PlayerControl.Instance.activeUnit.transform.position.x + 1f, PlayerControl.Instance.activeUnit.transform.position.z + 1f);
            PlayerControl.Instance.activeUnit.transform.position = new Vector3(pos2D.x, Utils.GetTerrainHeight(pos2D), pos2D.y);
        }

        public static void Greed()
        {
            for (int i = 0; i < GameResources.Instance.gameResources.Length; i++)
            {
                for (int p = 0; p < Enum.GetNames(typeof(Players)).Length; p++)
                {
                    // Initialize playerResourceLimits
                    if (GameResources.Instance.gameResources[i].type.limited)
                    {
                        // Change limit
                        GameResources.Instance.playerResourceLimits[i + p * GameResources.Instance.gameResources.Length] = 9999999;
                    }
                    else
                    {
                        // Regular resource
                        GameResources.Instance.ChangeAmount(p, new ResourceWrapper(GameResources.Instance.gameResources[i].type, 9999999), 1, false, true);
                    }

                    if (SlotManager.Instance.currentPlayer == p) UIManager.Instance.UpdateResourceTab(i);
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

            GameManager.Instance.ClearScene();
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
            NetworkConnectionHandler.Instance.StartHost(SlotManager.Instance.currentName);

        }

        public static void Join()
        {
            NetworkConnectionHandler.Instance.StartClient(SlotManager.Instance.currentName);
        }

        public static void StartScene()
        {
            SceneHandler.Instance.LoadScene();
        }
    }
}