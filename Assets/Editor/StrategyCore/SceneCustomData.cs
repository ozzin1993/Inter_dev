using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Save custom data of the scene, for fetching it from lobby
namespace StrategyCore
{
    [UnityEditor.InitializeOnLoad]
    public class SceneCustomData
    {
        static SceneCustomData()
        {
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved += OnSceneSaved;
        }

        public static void OnSceneSaved(Scene scene)
        {
            // Assign unique net ids
            DuplicateWatcher.AssignUniqueNetID();

            // Find SlotManager
            GameObject[] rootObjects = scene.GetRootGameObjects();
            GameManager gm = null;
            GameObject navmeshHolder = null;
            int spawnPointsLength = 0;

            foreach (var obj in rootObjects)
            {
                if (obj.name == "GameManager")
                {
                    if (obj.GetComponent<GameManager>() != null) gm = obj.GetComponent<GameManager>();
                }
                else if (obj.name == "Navmesh") navmeshHolder = obj;
                else if (obj.name == "LevelData")
                {
                    Transform SpawnPoints = obj.transform.Find("SpawnPoints");
                    spawnPointsLength = SpawnPoints.childCount;
                }

                if (navmeshHolder != null && gm != null && spawnPointsLength != 0) break;
            }

            if (gm != null)
            {
                // [Interflow 2026-08-17 ревью «долг редактора»] Пересборка 4 навмешей УБРАНА из каждого сохранения сцены:
                // сохранение документа её не требует. Навмеши пересобираются на входе в Play Mode (DuplicateWatcher)
                // и вручную: Tools → Interflow → «Пересобрать навмеши сцены».

                // Save data
                string content = JsonHelper.ToJson<TeamsAndPlayers>(gm.teamsAndPlayers);
                content += SaveManager.delimiter;
                content += (gm.chooseTeams == true) ? 1 : 0;
                content += SaveManager.delimiter;
                content += JsonHelper.ToJson(FactionData.GetNames(gm));
                content += SaveManager.delimiter;
                content += spawnPointsLength;

                string fileName = "TeamData";
                string folderPath = scene.path.Replace(".unity", ""); // Get the folder path
                string path = folderPath + "/" + fileName + ".json";

                // Ensure the directory exists
                if (!System.IO.Directory.Exists(folderPath))
                {
                    System.IO.Directory.CreateDirectory(folderPath);
                }

                System.IO.File.WriteAllText(path, content);
                Debug.Log("Custom Scene Data Saved!");
                AssetDatabase.ImportAsset(path); // [Interflow 2026-08-17 ревью] точечный импорт одного файла вместо полного Refresh (сканировал весь проект)
            }
            else
            {
                return;
            }
        }
    }
}