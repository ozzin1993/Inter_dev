#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using UnityEngine.AI;using StrategyCore;
[InitializeOnLoad]public static class InquisitionSkillRuntimeCheck
{
 const string W=@"C:\Users\aleks\Documents\Codex\2026-09-11\v\work\skills-28\";
 const string Scene="Assets/Scenes/Archive/Artsiom_SkillChecks.unity";
 static Unit caster,near,far,foe,outside;static CompositePassive burst,blessing;static float start;static int phase;static double boot;static int foeOwner;static Vector3 center;static Camera camera;static List<string> results=new List<string>(),errors=new List<string>();
 static InquisitionSkillRuntimeCheck(){boot=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;Application.logMessageReceived+=Log;}
 static void Log(string msg,string stack,LogType t){if(SessionState.GetBool("InquisitionSkillCheck",false)&&(t==LogType.Error||t==LogType.Exception||t==LogType.Assert))errors.Add(msg+"\n"+stack);}
 static void Tick(){try{
  if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
  if(!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists(W+"run-inquisition.txt")){try{File.Delete(W+"run-inquisition.txt");}catch(IOException){return;}Begin();return;}
  if(!SessionState.GetBool("InquisitionSkillCheck",false)||!EditorApplication.isPlaying||EditorApplication.isPaused)return;
  if(phase==0){if(!SlotManager.instance||!SlotManager.instance.gameOn){if(EditorApplication.timeSinceStartup-boot>90)throw new Exception("Game initialization timeout");return;}Setup();phase=1;start=Time.time;return;}
  if(phase==1&&Time.time-start>1){BurstTest();phase=2;start=Time.time;return;}
  if(phase==2&&Time.time-start>.25f){Capture("inquisition-burst.png");phase=3;return;}
  if(phase==3&&Time.time-start>4){HealTest();phase=4;start=Time.time;return;}
  if(phase==4&&Time.time-start>.35f){Capture("inquisition-heal.png");phase=5;return;}
  if(phase==5&&Time.time-start>4){EdgeTests();phase=6;start=Time.time;return;}
  if(phase==6&&Time.time-start>4){Check(!UnityEngine.Object.FindObjectsByType<VFXReferencer>(FindObjectsSortMode.None).Any(v=>v.name.StartsWith("DyingFlame")||v.name.StartsWith("FallenBlessing")),"Death and heal VFX cleaned up");Finish();}
 }catch(Exception e){errors.Add(e.ToString());Finish();}}
 static void Begin(){if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!="Assets/Scenes/Artsiom.unity"&&UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=Scene)throw new Exception("Open basic Artsiom before this check");if(!AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene)){if(!AssetDatabase.CopyAsset("Assets/Scenes/Artsiom.unity",Scene))throw new Exception("Cannot copy Artsiom");}EditorSceneManager.OpenScene(Scene);foreach(var m in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(m.GetType().Name=="MatchManager")m.enabled=false;foreach(var f in UnityEngine.Object.FindObjectsByType<FogOfWar>(FindObjectsSortMode.None))f.TurnOff=true;
 EditorSceneManager.sceneSaved-=SceneCustomData.OnSceneSaved;try{EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());}finally{EditorSceneManager.sceneSaved+=SceneCustomData.OnSceneSaved;}
 SessionState.SetBool("InquisitionSkillCheck",true);EditorApplication.isPaused=false;EditorApplication.isPlaying=true;}
 static Unit Spawn(Vector3 p,int owner,bool inquisition=false){var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UnitPrefabs/Units/Humans/"+(inquisition?"Swordsman_Inquisition":"Swordsman")+".prefab").GetComponent<Unit>();var u=Unit.SpawnInternal(prefab,p,0,owner);u.maxHealth=1000;u.health=500;u.armor=100;u.canAttack=false;u.doNotLookForTargets=true;u.Hold();u.FoWVisible=true;u.ShowRenderers();return u;}
 static void Setup(){foreach(var u in UnityEngine.Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)){u.canAttack=false;u.doNotLookForTargets=true;u.Hold();var a=u.GetComponent<AutoAbilityUser>();if(a)a.enabled=false;}
  if(FogOfWar.instance)FogOfWar.instance.TurnOff=true;
  if(!NavMesh.SamplePosition(new Vector3(113,0,33),out var hit,10,NavMesh.AllAreas))throw new Exception("No navmesh");center=hit.position;
  foeOwner=Enumerable.Range(1,SlotManager.instance.playerTeam.Length-2).First(i=>SlotManager.instance.playerTeam[i]!=SlotManager.instance.playerTeam[0]);
  caster=Spawn(center,0,true);near=Spawn(center+Vector3.right,0);far=Spawn(center+Vector3.left*2,0);foe=Spawn(center+Vector3.forward*2,foeOwner);outside=Spawn(center+Vector3.forward*6,foeOwner);
  burst=(CompositePassive)caster.abilities.First(a=>a.name=="Inquisition_DyingFlame");blessing=(CompositePassive)caster.abilities.First(a=>a.name=="Inquisition_FallenBlessing");
  Check(caster.abilityLocked[Array.IndexOf(caster.abilities,burst)]&&caster.abilityLocked[Array.IndexOf(caster.abilities,blessing)],"Both skills initially tech locked");
  foreach(var m in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(m.GetType().Name=="Camera_TopDown"||m.GetType().Name=="CameraAxisLock")m.enabled=false;
  camera=Camera.main;camera.transform.SetParent(null,true);camera.transform.position=center+new Vector3(6,7,-5);camera.transform.LookAt(center+new Vector3(0,.5f,1));camera.orthographic=true;camera.orthographicSize=4.5f;}
 static void Check(bool ok,string text){results.Add((ok?"PASS ":"FAIL ")+text);if(!ok)errors.Add(text);File.WriteAllLines(W+"inquisition-runtime-results.txt",results);}
 static void BurstTest(){TechnologyManager.instance.UnlockTech(burst.requiredTech[0].data[0],0);Check(!caster.abilityLocked[Array.IndexOf(caster.abilities,burst)],"Death burst unlocked through technology");var hp=foe.health;var ally=near.health;var distant=outside.health;caster.Die(foeOwner,foe,false);Check(Mathf.Abs(foe.health-(hp-60))<.01f,"Death burst deals 60 magic through physical armor");Check(near.health==ally,"Death burst excludes allies");Check(outside.health==distant,"Death burst excludes outside radius");hp=foe.health;caster.Die(foeOwner,foe,false);Check(foe.health==hp,"Repeated death does not explode twice");}
 static void HealTest(){TechnologyManager.instance.LockTech(burst.requiredTech[0].data[0],0);TechnologyManager.instance.UnlockTech(blessing.requiredTech[0].data[0],0);caster=Spawn(center,0,true);var hp=near.health;var other=far.health;var enemyHp=foe.health;caster.Die(foeOwner,foe,false);Check(Mathf.Abs(near.health-hp-250)<.01f,"Nearest ally receives 25% max HP");Check(far.health==other,"Further ally is not healed");Check(foe.health==enemyHp,"Healing specialization deals no enemy damage");}
 static void EdgeTests(){near.Die(foeOwner,foe,false);far.health=900;caster=Spawn(center,0,true);caster.Die(foeOwner,foe,false);Check(far.health==1000,"Dead nearest ally skipped; next ally healing capped at max HP");
  TechnologyManager.instance.LockTech(blessing.requiredTech[0].data[0],0);caster=Spawn(center,0,true);far.health=500;caster.Die(foeOwner,foe,false);Check(far.health==500,"Locked skill does not heal");
  TechnologyManager.instance.UnlockTech(burst.requiredTech[0].data[0],0);var a=Spawn(center,0,true);var b=Spawn(center+Vector3.right*.2f,0,true);float hp=foe.health;a.Die(foeOwner,foe,false);b.Die(foeOwner,foe,false);Check(Mathf.Abs(hp-foe.health-120)<.01f,"Two simultaneous death bursts both apply once");
  // Remove all allies in this isolated test scene to exercise absence of heal targets.
  TechnologyManager.instance.LockTech(burst.requiredTech[0].data[0],0);TechnologyManager.instance.LockTech(blessing.requiredTech[0].data[0],0);
  foreach(var u in SlotManager.instance.unitNetID.Values.ToArray())if(u!=null&&!u.dead&&u.team==SlotManager.instance.playerTeam[0])u.Die(-1,null,false);
  TechnologyManager.instance.UnlockTech(blessing.requiredTech[0].data[0],0);caster=Spawn(center,0,true);caster.Die(-1,null,false);Check(caster.dead,"No ally target: death completes without exception");}
 static void Capture(string name){var fx=UnityEngine.Object.FindObjectsByType<VFXReferencer>(FindObjectsSortMode.None).Where(v=>v.name.StartsWith("DyingFlame")||v.name.StartsWith("FallenBlessing")).ToArray();Check(fx.Length>0,"Presentation spawned: "+name);var rt=new RenderTexture(1280,800,24);var prior=camera.targetTexture;var active=RenderTexture.active;try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var t=new Texture2D(1280,800,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,1280,800),0,0);t.Apply();File.WriteAllBytes(W+name,t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);}finally{camera.targetTexture=prior;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(rt);}}
 static void Finish(){File.WriteAllLines(W+"inquisition-runtime-results.txt",results);File.WriteAllLines(W+"inquisition-runtime-errors.txt",errors);File.WriteAllText(W+"inquisition-runtime-finished.txt","Checks="+results.Count+" errors="+errors.Count);SessionState.SetBool("InquisitionSkillCheck",false);EditorApplication.isPaused=true;}
}
#endif


