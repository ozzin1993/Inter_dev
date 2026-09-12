using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
namespace StrategyCore
{
    // Prefilter serialized prefab references before loading models/materials into the Editor.
    // Native GetComponent remains the final authority. Binary/unknown assets are never excluded.
    public static class InterflowUnitPrefabIndex
    {
        static List<GameObject> cached;
        static InterflowUnitPrefabIndex(){EditorApplication.projectChanged += () => cached=null;}
        public static IEnumerable<GameObject> LoadUnits()
        {
            if(cached==null)cached=ScanUnits().ToList();
            return cached.Where(go=>go!=null);
        }
        static IEnumerable<GameObject> ScanUnits()
        {
            string scriptGuid=AssetDatabase.FindAssets("Unit t:MonoScript").FirstOrDefault(g=>AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(g))?.GetClass()==typeof(Unit));
            var possible=new Dictionary<string,bool>();
            foreach(string guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);
                if(!string.IsNullOrEmpty(scriptGuid)&&!MayContainUnit(path,scriptGuid,possible))continue;
                var go=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if(go!=null&&go.GetComponent<Unit>()!=null)yield return go;
            }
        }
        static bool MayContainUnit(string path,string unitGuid,Dictionary<string,bool> memo)
        {
            if(memo.TryGetValue(path,out bool result))return result;
            memo[path]=false; // Break malformed source-reference cycles.
            try
            {
                string text=File.ReadAllText(path);
                if(!text.StartsWith("%YAML",StringComparison.Ordinal)||text.Contains(unitGuid))return memo[path]=true;
                foreach(Match m in Regex.Matches(text,@"m_SourcePrefab:\s*\{[^}]*guid:\s*([a-fA-F0-9]{32})"))
                {
                    string source=AssetDatabase.GUIDToAssetPath(m.Groups[1].Value);
                    if(string.IsNullOrEmpty(source)||!source.EndsWith(".prefab",StringComparison.OrdinalIgnoreCase))continue;
                    if(MayContainUnit(source,unitGuid,memo))return memo[path]=true;
                }
                return false;
            }
            catch(IOException){return memo[path]=true;}
            catch(UnauthorizedAccessException){return memo[path]=true;}
        }
    }
}
