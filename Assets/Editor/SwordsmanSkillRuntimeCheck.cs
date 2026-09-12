#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using UnityEngine.AI;using StrategyCore;
[InitializeOnLoad]public static class SwordsmanSkillRuntimeCheck
{
 const string W=@"C:\Users\aleks\Documents\Codex\2026-09-11\v\work\skills-28\";
 const string Scene="Assets/Scenes/Archive/Artsiom_SkillChecks.unity";
 static Unit sword,enemy;static CompositePassive wall,parry;static float start;static int phase;static double boot;static List<string> results=new List<string>(),errors=new List<string>();static Camera camera;
 static SwordsmanSkillRuntimeCheck(){boot=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;Application.logMessageReceived+=Log;}
 static void Log(string msg,string stack,LogType t){if(SessionState.GetBool("SwordsmanSkillCheck",false)&&(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)){errors.Add(msg+"\n"+stack);File.WriteAllLines(W+"swordsman-runtime-errors.txt",errors);}}
 static void Tick(){try{
  if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
  if(!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists(W+"run-swordsman.txt")){try{File.Delete(W+"run-swordsman.txt");}catch(IOException){return;}Begin();return;}
  if(!SessionState.GetBool("SwordsmanSkillCheck",false)||!EditorApplication.isPlaying||EditorApplication.isPaused)return;
  if(phase==0){if(!SlotManager.instance||!SlotManager.instance.gameOn){if(EditorApplication.timeSinceStartup-boot>90)throw new Exception("Game initialization timeout");return;}Setup();phase=1;start=Time.time;return;}
  if(phase==1&&Time.time-start>1){DefenseTests();phase=2;start=Time.time;return;}
  if(phase==2&&Time.time-start>.12f){Capture("swordsman-shield.png");phase=3;return;}
  if(phase==3&&Time.time-start>1){ParryTests();phase=4;start=Time.time;return;}
  if(phase==4&&Time.time-start>.8f){for(int i=0;i<100;i++)if(Hit()==0)break;phase=8;start=Time.time;return;}
  if(phase==8&&Time.time-start>.12f){Capture("swordsman-parry.png");phase=5;return;}
  if(phase==5&&Time.time-start>1){sword.health=10000;enemy.health=10000;sword.canAttack=true;enemy.canAttack=true;sword.Attack(enemy);enemy.Attack(sword);phase=6;start=Time.time;return;}
  if(phase==6&&Time.time-start>8){Check(sword.health<10000&&enemy.health<10000,"Both units continue ordinary attacks after defense reactions");Capture("swordsman-combat.png");DeathTests();phase=7;start=Time.time;return;}
  if(phase==7&&Time.time-start>2){Check(!UnityEngine.Object.FindObjectsByType<VFXReferencer>(FindObjectsSortMode.None).Any(v=>v.name.StartsWith("ShieldContact")||v.name.StartsWith("ParryContact")),"Contact VFX finish after caster death");Finish();}
 }catch(Exception e){errors.Add(e.ToString());Finish();}}
 static void Begin(){if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!="Assets/Scenes/Artsiom.unity"&&UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=Scene)throw new Exception("Open basic Artsiom before this check");if(!AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene)){if(!AssetDatabase.CopyAsset("Assets/Scenes/Artsiom.unity",Scene))throw new Exception("Cannot copy Artsiom");}EditorSceneManager.OpenScene(Scene);foreach(var m in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(m.GetType().Name=="MatchManager")m.enabled=false;foreach(var f in UnityEngine.Object.FindObjectsByType<FogOfWar>(FindObjectsSortMode.None))f.TurnOff=true;
 EditorSceneManager.sceneSaved-=SceneCustomData.OnSceneSaved;try{EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());}finally{EditorSceneManager.sceneSaved+=SceneCustomData.OnSceneSaved;}
 SessionState.SetBool("SwordsmanSkillCheck",true);EditorApplication.isPaused=false;EditorApplication.isPlaying=true;}
 static void Setup(){foreach(var u in UnityEngine.Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)){u.canAttack=false;u.doNotLookForTargets=true;u.Hold();var a=u.GetComponent<AutoAbilityUser>();if(a)a.enabled=false;}
  if(FogOfWar.instance)FogOfWar.instance.TurnOff=true;
  var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UnitPrefabs/Units/Humans/Swordsman.prefab").GetComponent<Unit>();
  if(!NavMesh.SamplePosition(new Vector3(113,0,33),out var hit,10,NavMesh.AllAreas))throw new Exception("No navmesh");
  int foe=Enumerable.Range(1,SlotManager.instance.playerTeam.Length-2).First(i=>SlotManager.instance.playerTeam[i]!=SlotManager.instance.playerTeam[0]);
  sword=Unit.SpawnInternal(prefab,hit.position,0,0);enemy=Unit.SpawnInternal(prefab,hit.position+Vector3.forward*1.5f,0,foe);
  foreach(var u in new[]{sword,enemy}){u.maxHealth=100000;u.health=100000;u.armor=0;u.canAttack=false;u.doNotLookForTargets=true;u.Hold();u.FoWVisible=true;u.ShowRenderers();}
  sword.LookAtInstant(new Vector2(enemy.transform.position.x,enemy.transform.position.z));enemy.LookAtInstant(new Vector2(sword.transform.position.x,sword.transform.position.z));
  wall=(CompositePassive)sword.abilities.First(a=>a.name=="Swordsman_ShieldWall");parry=(CompositePassive)sword.abilities.First(a=>a.name=="Swordsman_Parry");
  Check(sword.abilityLocked[Array.IndexOf(sword.abilities,wall)]&&sword.abilityLocked[Array.IndexOf(sword.abilities,parry)],"Specializations initially locked");
  foreach(var m in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(m.GetType().Name=="Camera_TopDown"||m.GetType().Name=="CameraAxisLock")m.enabled=false;
  camera=Camera.main;camera.transform.SetParent(null,true);camera.transform.position=hit.position+new Vector3(5,5,-4);camera.transform.LookAt(hit.position+new Vector3(0,.7f,.6f));camera.orthographic=true;camera.orthographicSize=3;
  File.WriteAllText(W+"swordsman-runtime-started.txt","Ready");}
 static void Check(bool ok,string text){results.Add((ok?"PASS ":"FAIL ")+text);if(!ok)errors.Add(text);File.WriteAllLines(W+"swordsman-runtime-results.txt",results);}
 static float Hit(){sword.GetDamage(100,enemy.damageType,enemy.owner,enemy,true,out float dealt);return dealt;}
 static void DefenseTests(){float baseline=Hit();TechnologyManager.instance.UnlockTech(wall.requiredTech[0].data[0],sword.owner);Check(!sword.abilityLocked[Array.IndexOf(sword.abilities,wall)],"Technology unlock enables shield wall");float front=Hit();foreach(var extra in wall.defense.additionalDamageTypes){sword.GetDamage(100,extra,enemy.owner,enemy,true,out float value);float factor=GameManager.instance.damageToArmor[sword.armorType.index*GameManager.instance.DTAWidth+extra.index];Check(Mathf.Abs(value-80*factor)<.01f,"Shield wall covers physical type "+extra.name);}
 var magic=AssetDatabase.LoadAssetAtPath<DamageType>("Assets/Resources/DamageTypes/Magic.asset");if(magic){sword.GetDamage(100,magic,enemy.owner,enemy,true,out float magical);Check(Mathf.Abs(magical-100)<.01f,"Shield wall excludes magic");}
 Check(Mathf.Abs(front-baseline*.8f)<.01f,"Front physical reduction =20% ("+front+" / "+baseline+")");
 var old=enemy.transform.position;enemy.transform.position=sword.transform.position-Vector3.forward*1.5f;float rear=Hit();Check(Mathf.Abs(rear-baseline)<.01f,"Rear attack is not reduced");enemy.transform.position=old;Hit();}
 static void ParryTests(){TechnologyManager.instance.LockTech(wall.requiredTech[0].data[0],sword.owner);TechnologyManager.instance.UnlockTech(parry.requiredTech[0].data[0],sword.owner);Check(sword.abilityLocked[Array.IndexOf(sword.abilities,wall)]&&!sword.abilityLocked[Array.IndexOf(sword.abilities,parry)],"Technology switch removes wall and enables parry");float hp=enemy.health;int blocked=0;for(int i=0;i<400;i++)if(Hit()==0)blocked++;Check(blocked>30&&blocked<100,"Parry chance 15%: "+blocked+"/400 blocked");Check(enemy.health<hp,"Parry deals counter damage");
 var before=enemy.health;sword.GetDamage(10,enemy.damageType,enemy.owner,enemy,false,out float indirect);Check(indirect>0&&enemy.health==before,"Indirect damage cannot trigger parry counter");
 // Get a real random proc for its visual; no asset chances are changed.
 for(int i=0;i<100;i++)if(Hit()==0)break;}
 static void DeathTests(){sword.canAttack=false;enemy.canAttack=false;sword.Hold();enemy.Hold();enemy.Die(sword.owner,sword,false);Check(enemy.dead,"Target can die after counterattacks");sword.Die(enemy.owner,enemy,false);Check(sword.dead,"Caster death clears defense state");}
 static void Capture(string name){var fx=UnityEngine.Object.FindObjectsByType<VFXReferencer>(FindObjectsSortMode.None).Where(v=>v.name.StartsWith("ShieldContact")||v.name.StartsWith("ParryContact")).ToArray();File.WriteAllLines(W+name+".txt",fx.Select(v=>v.name+" at "+v.transform.position+" particles="+v.GetComponentsInChildren<ParticleSystem>().Sum(ps=>ps.particleCount)));if(name!="swordsman-combat.png")Check(fx.Length>0,"Contact VFX instances present: "+name);var rt=new RenderTexture(1280,800,24);var prior=camera.targetTexture;var active=RenderTexture.active;try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var t=new Texture2D(1280,800,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,1280,800),0,0);t.Apply();File.WriteAllBytes(W+name,t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);}finally{camera.targetTexture=prior;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(rt);}}
 static void Finish(){File.WriteAllLines(W+"swordsman-runtime-results.txt",results);File.WriteAllLines(W+"swordsman-runtime-errors.txt",errors);File.WriteAllText(W+"swordsman-runtime-finished.txt","Checks="+results.Count+" errors="+errors.Count);SessionState.SetBool("SwordsmanSkillCheck",false);EditorApplication.isPaused=true;}
}
#endif


