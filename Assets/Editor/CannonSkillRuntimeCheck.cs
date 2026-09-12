#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using UnityEngine.AI;using StrategyCore;
[InitializeOnLoad]public static class CannonSkillRuntimeCheck {
 const string W=@"C:\Users\aleks\Documents\Codex\2026-09-11\v\work\skills-28\";
 const string Scene="Assets/Scenes/Archive/Artsiom_SkillChecks.unity";
 static Unit archer,foe,foe2,ally,far,second;static CompositePassive splash;static float start,hp,nearHp,impactAt=-1;static bool captured;static int phase,foeOwner;static double boot;static Vector3 center;static Camera camera;static List<string> results=new List<string>(),errors=new List<string>();
 static CannonSkillRuntimeCheck(){boot=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;Application.logMessageReceived+=Log;}
 static void Log(string msg,string stack,LogType t){if(SessionState.GetBool("CannonSkillCheck",false)&&(t==LogType.Error||t==LogType.Exception||t==LogType.Assert))errors.Add(msg+"\n"+stack);}
 static void Tick(){try{
 if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
 if(!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists(W+"run-cannon.txt")){try{File.Delete(W+"run-cannon.txt");}catch(IOException){return;}Begin();return;}
 if(!SessionState.GetBool("CannonSkillCheck",false)||!EditorApplication.isPlaying||EditorApplication.isPaused)return;
 if(phase==3&&foe.health<hp&&!captured){if(impactAt<0)impactAt=Time.time;if(Time.time-impactAt>.08f){Check(UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Any(p=>p.name=="Explosion(Clone)"),"Explosion VFX appears with fog disabled");Capture("cannon-explosion.png");captured=true;}}
 if(phase==0){if(!SlotManager.instance||!SlotManager.instance.gameOn){if(EditorApplication.timeSinceStartup-boot>90)throw new Exception("Game initialization timeout");return;}Setup();phase=1;start=Time.time;return;}



 if(phase==1&&Time.time-start>.5f){Check(archer.isSplash&&Mathf.Abs(archer.splashRadius-2)<.01f,"Base cannon splash enabled at radius 2");splash.Unlock(archer,0,0);Check(Mathf.Abs(archer.splashRadius-2)<.01f,"Repeated unlock does not enlarge splash");hp=foe.health;nearHp=foe2.health;Shoot(archer,foe);phase=2;start=Time.time;return;}
 if(phase==2&&Time.time-start>.45f){Capture("cannon-flight.png");phase=3;return;}
 if(phase==3&&Time.time-start>1.5f){Check(Mathf.Abs(hp-foe.health-45)<.1f,"Primary target takes one hit");Check(Mathf.Abs(nearHp-foe2.health-45)<.1f,"Nearby enemy takes full splash damage");Check(ally.health==10000&&far.health==10000,"Splash excludes allies and outside targets");Capture("cannon-impact.png");hp=foe2.health;archer.canAttack=true;archer.Attack(foe);phase=4;start=Time.time;return;}
 if(phase==4&&Time.time-start>4){archer.canAttack=false;archer.Hold();Check(hp-foe2.health>=90,"Repeated animated shots apply splash");second=Spawn("Assets/Resources/UnitPrefabs/Units/Humans/Cannon.prefab",center+Vector3.left*2,0);hp=foe2.health;Shoot(archer,foe);Shoot(second,foe);phase=5;start=Time.time;return;}
 if(phase==5&&Time.time-start>2){Check(Mathf.Abs(hp-foe2.health-90)<.1f,"Concurrent cannons apply one splash each");hp=foe2.health;Shoot(archer,foe);archer.Die(foeOwner,foe,false);phase=6;start=Time.time;return;}
 if(phase==6&&Time.time-start>2){Check(Mathf.Abs(hp-foe2.health-45)<.1f,"Launched splash survives caster death");Shoot(second,foe);foe.Die(0,null,false);phase=7;start=Time.time;return;}
 if(phase==7&&Time.time-start>3){Check(!UnityEngine.Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None).Any(p=>p.name.StartsWith("CannonProjectile")),"Dead target projectile cleaned up");second.canAttack=true;second.Attack(foe2);foe2.Die(0,null,false);phase=8;start=Time.time;return;}
 if(phase==8&&Time.time-start>3){Check(!UnityEngine.Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None).Any(p=>p.name.StartsWith("CannonProjectile")),"No living target leaves no projectiles");Check(second.unitState!=UnitStates.AbilityCasting,"Cannon never remains in casting state");Check(!UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Any(p=>p.name=="Explosion(Clone)"),"Explosion particle roots clean up");Finish();}
 }catch(Exception e){errors.Add(e.ToString());Finish();}}
 static void Shoot(Unit u,Unit target){Projectile.SpawnAttack(u,u.projectileGO.GetComponent<Projectile>(),u.transform.position+Vector3.up,Quaternion.identity,target,u.splashUnitSelector,u.splashUnitSelector,45,true,false);}
 static Unit Spawn(string path,Vector3 position,int owner){var u=Unit.SpawnInternal(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Unit>(),position,0,owner);u.maxHealth=10000;u.health=10000;u.armor=0;u.healthRegen=0;u.canAttack=false;u.doNotLookForTargets=true;u.Hold();u.FoWVisible=true;u.ShowRenderers();return u;}
 static void Begin(){if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!="Assets/Scenes/Artsiom.unity"&&UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=Scene)throw new Exception("Open basic Artsiom before this check");if(!AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene)){if(!AssetDatabase.CopyAsset("Assets/Scenes/Artsiom.unity",Scene))throw new Exception("Cannot copy Artsiom");}EditorSceneManager.OpenScene(Scene);foreach(var m in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(m.GetType().Name=="MatchManager")m.enabled=false;foreach(var f in UnityEngine.Object.FindObjectsByType<FogOfWar>(FindObjectsSortMode.None))f.TurnOff=true;
 EditorSceneManager.sceneSaved-=SceneCustomData.OnSceneSaved;try{EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());}finally{EditorSceneManager.sceneSaved+=SceneCustomData.OnSceneSaved;}
 SessionState.SetBool("CannonSkillCheck",true);EditorApplication.isPaused=false;EditorApplication.isPlaying=true;}
 static void Setup(){foreach(var u in UnityEngine.Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)){u.canAttack=false;u.doNotLookForTargets=true;u.Hold();var a=u.GetComponent<AutoAbilityUser>();if(a)a.enabled=false;}
  if(FogOfWar.instance)FogOfWar.instance.TurnOff=true;
  if(!NavMesh.SamplePosition(new Vector3(113,0,33),out var hit,10,NavMesh.AllAreas))throw new Exception("No navmesh");center=hit.position;
  foeOwner=Enumerable.Range(1,SlotManager.instance.playerTeam.Length-2).First(i=>SlotManager.instance.playerTeam[i]!=SlotManager.instance.playerTeam[0]);

  const string sample="Assets/Resources/UnitPrefabs/Units/Humans/Swordsman.prefab";
  archer=Spawn("Assets/Resources/UnitPrefabs/Units/Humans/Cannon.prefab",center,0);foe=Spawn(sample,center+Vector3.forward*6,foeOwner);foe2=Spawn(sample,center+new Vector3(.7f,0,6),foeOwner);ally=Spawn(sample,center+new Vector3(-.8f,0,6),0);far=Spawn(sample,center+new Vector3(3.5f,0,6),foeOwner);
  archer.LookAtInstant(new Vector2(foe.transform.position.x,foe.transform.position.z));splash=archer.abilities.OfType<CompositePassive>().Single(a=>a.name=="Cannon_ExplosiveShell");
  foreach(var m in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(m.GetType().Name=="Camera_TopDown"||m.GetType().Name=="CameraAxisLock")m.enabled=false;
  camera=Camera.main;camera.transform.SetParent(null,true);camera.transform.position=center+new Vector3(7,9,-5);camera.transform.LookAt(center+new Vector3(0,.5f,3));camera.orthographic=true;camera.orthographicSize=6;}
 static void Check(bool ok,string text){results.Add((ok?"PASS ":"FAIL ")+text);if(!ok)errors.Add(text);File.WriteAllLines(W+"cannon-runtime-results.txt",results);}
 static void Capture(string name){var rt=new RenderTexture(1280,800,24);var prior=camera.targetTexture;var active=RenderTexture.active;try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var t=new Texture2D(1280,800,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,1280,800),0,0);t.Apply();File.WriteAllBytes(W+name,t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);}finally{camera.targetTexture=prior;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(rt);}}
 static void Finish(){File.WriteAllLines(W+"cannon-runtime-results.txt",results);File.WriteAllLines(W+"cannon-runtime-errors.txt",errors);File.WriteAllText(W+"cannon-runtime-finished.txt","Checks="+results.Count+" errors="+errors.Count);SessionState.SetBool("CannonSkillCheck",false);EditorApplication.isPaused=true;}
}
#endif


