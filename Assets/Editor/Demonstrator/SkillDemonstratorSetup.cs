#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using StrategyCore;

[InitializeOnLoad] public static class SkillDemonstratorSetup
{
    public const string ScenePath="Assets/Scenes/Skill_Demonstrator.unity";
    const string W=@"C:\Users\aleks\Documents\Codex\2026-09-11\v\work\skills-28\";
    static SkillDemonstratorSetup(){EditorApplication.update+=Poll;}
    static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        if(File.Exists(W+"build-skill-demo.txt")){File.Delete(W+"build-skill-demo.txt");try{Build();File.WriteAllText(W+"skill-demo-built.txt","OK");}catch(Exception e){File.WriteAllText(W+"skill-demo-build-error.txt",e.ToString());}}
        if(File.Exists(W+"finish-skill-demo.txt")){File.Delete(W+"finish-skill-demo.txt");try{Finish();File.WriteAllText(W+"skill-demo-finalized.txt","OK");}catch(Exception e){File.WriteAllText(W+"skill-demo-finalized.txt",e.ToString());}}
    }
    [MenuItem("Tools/Interflow/Skill Demonstrator/Open")]
    public static void Open(){if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())EditorSceneManager.OpenScene(ScenePath);}
    static Unit Prefab(string path)=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UnitPrefabs/"+path+".prefab").GetComponent<Unit>();
    static void Build()
    {
        if(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))throw new Exception("Demonstrator already exists; edit it explicitly instead of overwriting.");
        var active=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(active.isDirty){
            string backup="Assets/Scenes/Archive/BeforeDemonstrator_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".unity";
            EditorSceneManager.sceneSaved-=SceneCustomData.OnSceneSaved;
            try{EditorSceneManager.SaveScene(active,backup,true);}finally{EditorSceneManager.sceneSaved+=SceneCustomData.OnSceneSaved;}
            File.WriteAllText(W+"skill-demo-unsaved-backup.txt",backup);
        }
        if(!AssetDatabase.CopyAsset("Assets/Scenes/Artsiom.unity",ScenePath))throw new Exception("Scene copy failed");
        if(!AssetDatabase.IsValidFolder("Assets/Scenes/Skill_Demonstrator"))AssetDatabase.CreateFolder("Assets/Scenes","Skill_Demonstrator");
        var team="Assets/Scenes/Artsiom/TeamData.json";
        if(File.Exists(team))AssetDatabase.CopyAsset(team,"Assets/Scenes/Skill_Demonstrator/TeamData.json");
        EditorSceneManager.OpenScene(ScenePath);
        foreach(var u in UnityEngine.Object.FindObjectsByType<Unit>(FindObjectsSortMode.None))u.gameObject.SetActive(false);
        foreach(var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))r.enabled=false;
        foreach(var p in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))p.gameObject.SetActive(false);
        foreach(var c in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))c.enabled=false;
        foreach(var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))l.enabled=false;
        foreach(var m in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(m.GetType().Name=="MatchManager"||m.GetType().Name=="Volume")m.enabled=false;
        foreach(var n in UnityEngine.Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None))n.NetworkConfig.EnableSceneManagement=false;
        var root=new GameObject("Skill Demonstrator");var demo=root.AddComponent<SkillDemonstrator>();
        if(NavMesh.SamplePosition(demo.stageCenter,out var hit,8,NavMesh.AllAreas))demo.stageCenter=hit.position;
        var unitPaths=AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Resources/UnitPrefabs/Units"}).Select(AssetDatabase.GUIDToAssetPath).Where(p=>!p.Contains("/Templates/")).ToArray();
        var names=new[]{"Swordsman","Swordsman_Inquisition","RoyalArcher","InquisitionArcher","VanguardMage","PriestOfLight","Ballista","FireAltar","Cannon","SteelPaladin","SupremePunisher","OrcWarrior","OrcWarlock","OrcCatapult","OrcSlaver","WarWolf","OgreGladiator","FelFireHurler","FelStalker","FelWarlock","FelSiegeBehemoth","BloodBerserker","FelLord","Haldor","Inquisitor_Base","Inquisitor_Avatar","Korgal","Gurtag"};
        var plan=File.ReadAllLines(W+"roster-plan.md");
        demo.roster=names.Select(name=>{
            var path=unitPaths.Single(p=>Path.GetFileNameWithoutExtension(p)==name);var unit=AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Unit>();
            var techs=new HashSet<Technology>();var visited=new HashSet<UnityEngine.Object>();
            foreach(var a in unit.abilities.Where(a=>a&&!a.name.Contains("UpgradePassive")))FindTechs(a,techs,visited);
            var row=plan.FirstOrDefault(l=>l.StartsWith("| "+name+" |"));
            return new SkillDemonstrator.Entry{prefab=unit,specializations=techs.OrderBy(t=>t.name).ToArray(),notes=(row!=null?row.Split('|')[2].Trim():"")+"\n\nДля пассивных эффектов включите обычную атаку / ответный огонь. Порог HP, гибель и запас ресурса задаются кнопками внизу."};
        }).ToArray();
        demo.humanTarget=Prefab("Units/Humans/Swordsman");demo.orcTarget=Prefab("Units/Orcs/OrcWarrior");demo.allyMage=Prefab("Units/Orcs/FelWarlock");demo.buildingTarget=Prefab("Buildings/ArrowTower");
        var light=new GameObject("Demo Key Light").AddComponent<Light>();light.transform.SetParent(root.transform);light.type=LightType.Directional;light.intensity=1.5f;light.color=new Color(1,.95f,.87f);light.shadows=LightShadows.Soft;light.transform.rotation=Quaternion.Euler(48,-35,0);
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.55f,.6f,.68f);RenderSettings.fog=false;
        if(!AssetDatabase.IsValidFolder("Assets/Art/Demonstrator"))AssetDatabase.CreateFolder("Assets/Art","Demonstrator");
        var mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.SetColor("_BaseColor",new Color(.18f,.22f,.25f));mat.SetFloat("_Smoothness",.08f);AssetDatabase.CreateAsset(mat,"Assets/Art/Demonstrator/Stage.mat");
        var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="Demonstration Floor";floor.transform.SetParent(root.transform);floor.transform.position=demo.stageCenter+Vector3.down*.07f;floor.transform.localScale=Vector3.one*10;UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());floor.GetComponent<Renderer>().sharedMaterial=mat;
        var gridmat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));gridmat.SetColor("_BaseColor",new Color(.26f,.33f,.38f));AssetDatabase.CreateAsset(gridmat,"Assets/Art/Demonstrator/Grid.mat");
        for(int i=-12;i<=12;i+=2){Line(root.transform,gridmat,demo.stageCenter+new Vector3(i,-.055f,-12),demo.stageCenter+new Vector3(i,-.055f,12));Line(root.transform,gridmat,demo.stageCenter+new Vector3(-12,-.055f,i),demo.stageCenter+new Vector3(12,-.055f,i));}
        var camera=Camera.main;if(camera){camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.11f,.14f);camera.orthographic=true;camera.orthographicSize=8;camera.transform.position=demo.stageCenter+new Vector3(9,14,-12);camera.transform.LookAt(demo.stageCenter);}
        EditorSceneManager.sceneSaved-=SceneCustomData.OnSceneSaved;
        try{EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());}finally{EditorSceneManager.sceneSaved+=SceneCustomData.OnSceneSaved;}
        AssetDatabase.SaveAssets();
    }
    static void FindTechs(UnityEngine.Object value,HashSet<Technology> found,HashSet<UnityEngine.Object> visited)
    {
        if(!value||!visited.Add(value))return;
        if(value is Technology t){found.Add(t);return;}
        if(value is GameObject go){foreach(var zone in go.GetComponentsInChildren<GroundDamageZone>(true))FindTechs(zone,found,visited);return;}
        if(!(value is Ability)&&!(value is Effector)&&!(value is GroundDamageZone))return;
        var so=new SerializedObject(value);var p=so.GetIterator();while(p.NextVisible(true))if(p.propertyType==SerializedPropertyType.ObjectReference){var o=p.objectReferenceValue;if(o is Technology tech)found.Add(tech);else if(o is Ability||o is Effector||o is GameObject||o is GroundDamageZone)FindTechs(o,found,visited);}
    }
    static void Finish()
    {
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(scene.path!=ScenePath)throw new Exception("Only the demonstrator may be changed.");
        var demo=UnityEngine.Object.FindFirstObjectByType<SkillDemonstrator>();
        foreach(var entry in demo.roster){var techs=new HashSet<Technology>();var visited=new HashSet<UnityEngine.Object>();foreach(var a in entry.prefab.abilities.Where(a=>a&&!a.name.Contains("UpgradePassive")))FindTechs(a,techs,visited);entry.specializations=techs.OrderBy(t=>t.name).ToArray();}
        EditorUtility.SetDirty(demo);
        // Terrain is not a Renderer; the copied landscape must not cover the presentation floor.
        foreach(var terrain in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None)){terrain.drawHeightmap=false;terrain.drawTreesAndFoliage=false;EditorUtility.SetDirty(terrain);}
        foreach(var n in UnityEngine.Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None)){n.NetworkConfig.EnableSceneManagement=false;PrefabUtility.RecordPrefabInstancePropertyModifications(n);EditorUtility.SetDirty(n);}
        var camera=Camera.main;if(camera){camera.cullingMask=~LayerMask.GetMask("Icons","Healthbar");PrefabUtility.RecordPrefabInstancePropertyModifications(camera);EditorUtility.SetDirty(camera);}
        File.WriteAllLines(W+"skill-demo-specializations.txt",demo.roster.Select(e=>e.prefab.name+" | "+string.Join("; ",e.specializations.Select(t=>t.displayName))));
        EditorSceneManager.sceneSaved-=SceneCustomData.OnSceneSaved;
        try{EditorSceneManager.SaveScene(scene);}finally{EditorSceneManager.sceneSaved+=SceneCustomData.OnSceneSaved;}
    }
    static void Line(Transform root,Material mat,Vector3 a,Vector3 b){var line=new GameObject("Floor Grid").AddComponent<LineRenderer>();line.transform.SetParent(root);line.sharedMaterial=mat;line.positionCount=2;line.SetPositions(new[]{a,b});line.startWidth=line.endWidth=.018f;line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;}
}
#endif
