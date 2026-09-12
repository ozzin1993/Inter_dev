#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using StrategyCore;
[InitializeOnLoad]public static class RosterFinalAudit {
 const string W=@"C:\Users\aleks\Documents\Codex\2026-09-11\v\work\skills-28\";
 static RosterFinalAudit(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
  if(File.Exists(W+"final-repair.txt")){File.Delete(W+"final-repair.txt");try{Repair();}catch(Exception e){File.WriteAllText(W+"final-repair-error.txt",e.ToString());}}
  if(File.Exists(W+"final-detail.txt")){File.Delete(W+"final-detail.txt");Details();}
  if(File.Exists(W+"final-audit.txt")){File.Delete(W+"final-audit.txt");try{Audit();}catch(Exception e){File.WriteAllText(W+"final-audit-error.txt",e.ToString());}}
  if(File.Exists(W+"restore-artsiom-skills.txt")){File.Delete(W+"restore-artsiom-skills.txt");try{Restore();}catch(Exception e){File.WriteAllText(W+"restore-artsiom-error.txt",e.ToString());}}
 }
 static Unit[] Roster()=>AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Resources/UnitPrefabs/Units"}).Select(g=>AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).Where(g=>g).Select(g=>g.GetComponent<Unit>()).Where(u=>u && !AssetDatabase.GetAssetPath(u).Contains("/Templates/")).ToArray();
 static void Audit(){var rows=new List<string>();var roster=Roster();rows.Add("Roster="+roster.Length);foreach(var u in roster){int broken=0,missing=0;foreach(var tr in u.GetComponentsInChildren<Transform>(true))missing+=GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(tr.gameObject);foreach(var c in u.GetComponentsInChildren<Component>(true)){if(!c)continue;var so=new SerializedObject(c);var p=so.GetIterator();while(p.NextVisible(true))if(p.propertyType==SerializedPropertyType.ObjectReference&&p.objectReferenceValue==null&&p.objectReferenceInstanceIDValue!=0){broken++;rows.Add("BROKEN "+u.name+" "+c.GetType().Name+" "+p.propertyPath);}}
  rows.Add(u.name+" | ID="+u.unitTypeID+" | abilities="+string.Join(",",u.abilities.Select(a=>a?a.name:"MISSING"))+" | missingScripts="+missing+" | brokenRefs="+broken);
  var au=u.GetComponent<AutoAbilityUser>();if(au){var so=new SerializedObject(au);var p=so.FindProperty("autoAbilities");if(p!=null)for(int i=0;i<p.arraySize;i++){var item=p.GetArrayElementAtIndex(i);var ability=item.propertyType==SerializedPropertyType.ObjectReference?item.objectReferenceValue:item.FindPropertyRelative("ability")?.objectReferenceValue;if(ability&&!u.abilities.Contains(ability))rows.Add("AUTO_NOT_ASSIGNED "+u.name+" "+ability.name);}}
 }
 foreach(var g in roster.GroupBy(u=>u.unitTypeID).Where(g=>g.Count()>1))rows.Add("DUPLICATE_UNIT_ID "+g.Key);
 foreach(var g in Resources.LoadAll<Ability>("Ability").GroupBy(a=>a.id).Where(g=>g.Count()>1))rows.Add("DUPLICATE_ABILITY_ID "+g.Key+" "+string.Join(",",g.Select(a=>a.name)));
 foreach(var g in Resources.LoadAll<Effector>("Effectors").GroupBy(a=>a.id).Where(g=>g.Count()>1))rows.Add("DUPLICATE_EFFECTOR_ID "+g.Key+" "+string.Join(",",g.Select(a=>a.name)));
 File.WriteAllLines(W+"final-prefab-audit.txt",rows);
 var issues=InterflowValidator.RunAll();File.WriteAllLines(W+"final-interflow-validator.txt",issues.Select(i=>i.severity+" | "+i.message+" | "+(i.target?AssetDatabase.GetAssetPath(i.target):"")+" | "+i.source));
 File.WriteAllText(W+"final-audit-finished.txt","Roster="+roster.Length+" ValidatorErrors="+issues.Count(i=>i.severity==InterflowIssueSeverity.Error));
 }
 static void Repair(){var rows=new List<string>();foreach(var source in Roster()){
 var path=AssetDatabase.GetAssetPath(source);var root=PrefabUtility.LoadPrefabContents(path);bool dirty=false;try{
 foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true)){if(ps.textureSheetAnimation.enabled)continue;var so=new SerializedObject(ps);var it=so.GetIterator();while(it.NextVisible(true))if(it.propertyType==SerializedPropertyType.ObjectReference&&it.propertyPath.StartsWith("UVModule.sprites.")&&it.objectReferenceValue==null&&it.objectReferenceInstanceIDValue!=0){it.objectReferenceInstanceIDValue=0;dirty=true;rows.Add("Cleared unused sprite: "+source.name+"/"+ps.name);}so.ApplyModifiedPropertiesWithoutUndo();}
 var u=root.GetComponent<Unit>();if(source.name=="Ballista"&&!u.icon){u.icon=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Icons/icons/humans_tower_ballista.png");if(!u.icon)throw new Exception("Ballista icon missing");dirty=true;rows.Add("Ballista icon restored");}
 if(source.name=="Inquisitor_Base"||source.name=="Korgal"){var launch=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="LaunchPoint");var sockets=root.GetComponent<CharacterSockets>();if(!sockets)sockets=root.AddComponent<CharacterSockets>();var so=new SerializedObject(sockets);so.FindProperty("weapon").objectReferenceValue=launch;so.ApplyModifiedPropertiesWithoutUndo();dirty=true;rows.Add(source.name+" weapon socket="+AnimationUtility.CalculateTransformPath(launch,root.transform));}
 if(dirty)PrefabUtility.SaveAsPrefabAsset(root,path);
 }finally{PrefabUtility.UnloadPrefabContents(root);}}
 AssetDatabase.SaveAssets();File.WriteAllLines(W+"final-repair-results.txt",rows);}
 static void Details(){var rows=new List<string>();foreach(var u in Roster()){
 foreach(var ps in u.GetComponentsInChildren<ParticleSystem>(true)){var so=new SerializedObject(ps);var a=so.FindProperty("UVModule.sprites");if(a!=null&&a.arraySize>0)rows.Add(u.name+" | "+AnimationUtility.CalculateTransformPath(ps.transform,u.transform)+" | UV enabled="+ps.textureSheetAnimation.enabled+" mode="+ps.textureSheetAnimation.mode+" sprites="+a.arraySize);}
 if(u.name=="Korgal"||u.name=="Inquisitor_Base")foreach(var t in u.GetComponentsInChildren<Transform>(true))rows.Add(u.name+" TREE "+AnimationUtility.CalculateTransformPath(t,u.transform));
 var anim=u.GetComponentInChildren<Animator>(true);rows.Add(u.name+" controller="+(anim?AssetDatabase.GetAssetPath(anim.runtimeAnimatorController):"NONE"));
 }File.WriteAllLines(W+"final-detail-results.txt",rows);}
 static void Restore(){var active=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(active.path!="Assets/Scenes/Archive/Artsiom_SkillChecks.unity"&&active.path!="Assets/Scenes/Artsiom.unity")throw new Exception("Unexpected scene: "+active.path);if(active.path!="Assets/Scenes/Artsiom.unity")EditorSceneManager.OpenScene("Assets/Scenes/Artsiom.unity");var rows=new List<string>();foreach(var u in UnityEngine.Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)){var original=PrefabUtility.GetCorrespondingObjectFromSource(u);if(!original||!AssetDatabase.GetAssetPath(original).StartsWith("Assets/Resources/UnitPrefabs/Units/"))continue;var so=new SerializedObject(u);foreach(var name in new[]{"abilities","abilityLevel"}){var p=so.FindProperty(name);if(p!=null&&p.prefabOverride)PrefabUtility.RevertPropertyOverride(p,InteractionMode.AutomatedAction);}if(original.name=="Inquisitor_Base"){var p=so.FindProperty("maxManaBase");if(p!=null&&p.prefabOverride)PrefabUtility.RevertPropertyOverride(p,InteractionMode.AutomatedAction);}var au=u.GetComponent<AutoAbilityUser>();if(au&&PrefabUtility.GetCorrespondingObjectFromSource(au)){var aps=new SerializedObject(au);var p=aps.FindProperty("autoAbilities");if(p!=null&&p.prefabOverride)PrefabUtility.RevertPropertyOverride(p,InteractionMode.AutomatedAction);}rows.Add(u.name+" | "+string.Join(",",u.abilities.Select(a=>a?a.name:"MISSING"))+" | poolMatches="+u.abilities.SequenceEqual(original.abilities));}
 EditorSceneManager.sceneSaved-=SceneCustomData.OnSceneSaved;try{EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());}finally{EditorSceneManager.sceneSaved+=SceneCustomData.OnSceneSaved;}File.WriteAllLines(W+"artsiom-restored.txt",rows);
 }
}
#endif
