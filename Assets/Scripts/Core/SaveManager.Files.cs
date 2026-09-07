using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using SimpleJSON;

namespace StrategyCore
{
    // SaveManager.Files.cs — файлы сейвов (SaveToFile/LoadSaveFile/список/утилиты). Вырезано 1:1 из SaveManager.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class SaveManager
    {

        // FILE MANIPULATION ----------------------------------------------------------------------------------------------------------------------------------------------------

        // Save to a file
        public static string SaveToFile(bool returnData = false)
        {
            if (SceneHandler.Instance.sceneData == null || SceneHandler.Instance.sceneIndex >= SceneHandler.Instance.sceneData.Length) return null;
            SceneData sceneData = SceneHandler.Instance.sceneData[SceneHandler.Instance.sceneIndex];

            if (!returnData)
            {
                if (Presentation.MenuUI != null) Presentation.MenuUI?.ShowMenuLobby(2);
                Time.timeScale = 0f;
            }

            // Store content
            string content = SavePlayerData();
            content += delimiter;
            content += SaveResources();
            content += delimiter;
            content += SaveTechnology();
            content += delimiter;
            content += SaveUnitData();

            Debug.Log("Save file byte size " + System.Text.Encoding.UTF8.GetBytes(content).Length);

            if (returnData) return content;

            // File name
            System.DateTime currentTime = System.DateTime.Now;
            string formattedTime = currentTime.ToString("yyyy-MM-dd_HH-mm-ss");

            string fileName = sceneData.sceneName + "_" + formattedTime;
            string path = savePath + fileName + saveExtension;

            // Save file
            if (!System.IO.Directory.Exists(savePath)) // Ensure the directory exists
            {
                System.IO.Directory.CreateDirectory(savePath);
            }

            System.IO.File.WriteAllText(path, content);

            // Done
            Time.timeScale = 1f;

            if (Presentation.MenuUI != null) Presentation.MenuUI?.ShowMenuLobby(4);
            Debug.Log("Saved at " + path);
            return path;
        }

        // Load specified save file
        public static void LoadSaveFile(string fileName, bool isPath = false)
        {
            if (!isPath) fileName = savePath + fileName + saveExtension;

            // Pause the game
            // Delete all units
            // Clear scene before loading - delet all units
            // Clear NetworkHandler.instance.unitNetID

            // load resources
            // load technology
            // load units
            // load shadowcasters
            // string path = savePath + fileName + saveExtension;

            if (File.Exists(fileName))
            {
                // Retrieve content
                string content = System.IO.File.ReadAllText(fileName);

                GameManager.Instance.StartCoroutine(SaveManager.LoadSave_Internal(content));
            }
            else
            {
                Debug.Log("No save file found! At " + fileName);
            }
        }

        // Loads the save file`s content
        public static IEnumerator LoadSave_Internal(string content)
        {
            // Wait for 1 frame for game to initialize everything
            yield return null;

            // Блоков стало ЧЕТЫРЕ: блок теневых кастеров снесён вместе с умениями-каналами (блок Б6, 2026-09-04).
            // Старый пятиблочный файл отвергаем ЯВНО: молча читать его нельзя — блоки съедут на один и
            // данные юнитов были бы разобраны как теневые кастеры.
            // Предел разбиения оставлен пятёркой НАМЕРЕННО: только так старый файл даст 5 частей и будет опознан;
            // с пределом 4 его хвост слился бы в последнюю часть и файл прошёл бы проверку как «четырёхблочный».
            // Проверка стоит ДО паузы и очистки сцены (по ревью 04.09): отказ не должен оставлять игрока
            // в стёртой сцене на паузе.
            string[] parts = content.Split(new string[] { delimiter }, 5, StringSplitOptions.None);
            if (parts.Length != 4)
            {
                Debug.LogError("[SaveManager] Файл сейва не читается: блоков " + parts.Length + " вместо 4. " +
                               "Файлы, сохранённые до сноса умений-каналов (2026-09-04), несовместимы — загрузка остановлена");
                yield break;
            }

            // Pause the game
            Time.timeScale = 0f;
            // Clear the scene
            GameManager.Instance.ClearScene();

            // Wait for 2 frames for scene to be cleared
            yield return null;
            yield return null;

            // LoadPlayerData(parts[0]); // LOADED IN THE LOBBY
            LoadResources(parts[1]);
            LoadTechnology(parts[2]);
            LoadUnitData(parts[3]);

            FinishedLoadingSaveFile();
        }

        // Callback when save file is finished loading
        public static void FinishedLoadingSaveFile()
        {
            // Send information that loading is finished
            NetworkDataSync.Instance.ClientFinishedLoadingSaveServerRpc();

            return;
        }

        // Replaces current slotManager data with the one from save file
        public static bool LoadSlotManagerData(string fileName, bool isPath = false)
        {
            if (!isPath) fileName = savePath + fileName + saveExtension;

            if (File.Exists(fileName))
            {
                // Retrieve content
                string content = System.IO.File.ReadAllText(fileName);

                string[] parts = content.Split(new string[] { delimiter }, 5, StringSplitOptions.None);

                LoadPlayerData(parts[0]);

                Presentation.MenuUI?.FillPlayerList(); // Refresh

                return true;
            }
            else
            {
                Debug.Log("No save file found! At " + fileName);
                return false;
            }
        }

        // Delete specified save file
        public static bool DeleteSaveFile(string fileName, bool isPath = false)
        {
            if (!isPath) fileName = savePath + fileName + saveExtension;

            if (File.Exists(fileName))
            {
                try
                {
                    File.Delete(fileName);
                    return true;
                }
                catch (IOException ex)
                {
                    Debug.LogError($"An error occurred while trying to delete the file: {ex.Message}");
                }
            }
            return false;
        }

        // Get all save files
        public static string[] GetAvailableSaveFiles()
        {
            string fileExtension = "*" + SaveManager.saveExtension;

            if (Directory.Exists(SaveManager.savePath))
            {
                return Directory.GetFiles(SaveManager.savePath, fileExtension);
            }

            return null;
        }

        // Retrieves file name from path
        public static string GetFileName(string filePath)
        {
            return Path.GetFileNameWithoutExtension(filePath);
        }

        // HELPERS ------------------------------------------------------------------------------------------------------------------------------------------

        // Returns string between two markers
        public static string ExtractInBetween(string input, string startMarker, string endMarker)
        {
            int startIndex = input.IndexOf(startMarker);
            int endIndex = input.LastIndexOf(endMarker);

            if (startIndex != -1 && endIndex != -1 && startIndex < endIndex)
            {
                // Calculate the substring
                string result = input.Substring(startIndex + startMarker.Length, endIndex - (startIndex + startMarker.Length));
                return result;
            }
            else
            {
                return null;
            }
        }

        // Extract ability levels - only hero levelable (уровень −1 снесён блоком Б9: улучшаемое умение всегда ≥ 0)
        public static void RecursiveLevelExtract(Unit unit, Ability[] recursiveAbilities, ref int abilityGlobalIndex, ref string abilityIndex, ref string levels)
        {
            for (int i = 0; i < recursiveAbilities.Length; i++)
            {
                abilityGlobalIndex++;

                if (recursiveAbilities[i].heroLevelable == true)
                {
                    abilityIndex += "-" + abilityGlobalIndex;
                    levels += "-" + unit.abilityLevel[abilityGlobalIndex];
                }

                if (recursiveAbilities[i].type == AbilityType.Container)
                {
                    Container container = (Container)recursiveAbilities[i];
                    RecursiveLevelExtract(unit, container.abilities, ref abilityGlobalIndex, ref abilityIndex, ref levels);
                }
            }
        }
    }
}
