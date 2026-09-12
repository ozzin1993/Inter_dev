#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using UnityEngine.AI;using StrategyCore;
[InitializeOnLoad]public static class FireAltarSkillRuntimeCheck {
 const string W=@"C:\Users\aleks\Documents\Codex\2026-09-11\v\work\skills-28\";
 const string Scene="Assets/Scenes/Archive/Artsiom_SkillChecks.unity";
 static Unit archer,foe,foe2,ally,far,second;static EveryNthAttack pool,charge;static float start,hp,outerHp;static int phase,foeOwner,impacts;static double boot;static Vector3 center;static Camera camera;static List<string> results=new List<string>(),errors=new List<string>();
 static FireAltarSkillRuntimeCheck(){boot=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;Application.logMessageReceived+=Log;}
 static void Log(string msg,string stack,LogType t){if(SessionState.GetBool("FireAltarSkillCheck",false)&&(t==LogType.Error||t==LogType.Exception||t==LogType.Assert))errors.Add(msg+"\n"+stack);}
 static void OnFired(Unit u,int id,int level,Unit target,Vector3 point){if(id==pool.onProcSkill.id)impacts++;}
 static void Tick(){try{
 if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
 if(!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists(W+"run-firealtar.txt")){try{File.Delete(W+"run-firealtar.txt");}catch(IOException){return;}Begin();return;}
 if(!SessionState.GetBool("FireAltarSkillCheck",false)||!EditorApplication.isPlaying||EditorApplication.isPaused)return;
 if(phase==0){if(!SlotManager.instance||!SlotManager.instance.gameOn){if(EditorApplication.timeSinceStartup-boot>90)throw new Exception("Game initialization timeout");return;}Setup();phase=1;start=Time.time;return;}


 if(phase==1&&Time.time-start>.5f){Check(!archer.GetComponent<WeaponDeployment>().ReadyToFire(),"Deployment starts with a delay");phase=2;start=Time.time;return;}
 if(phase==2&&Time.time-start>1){Check(!archer.GetComponent<WeaponDeployment>().ReadyToFire(),"Cannot fire after only one second");Capture("firealtar-deployment.png");phase=3;return;}
 if(phase==3&&Time.time-start>1.9f){Check(archer.GetComponent<WeaponDeployment>().ReadyToFire(),"Ready after 1.75 seconds");Check(archer.GetComponent<WeaponDeployment>().ReadyToFire(),"Stationary follow-up does not redeploy");archer.transform.position+=Vector3.left;phase=4;start=Time.time;return;}
 if(phase==4&&Time.time-start>.2f){Check(!archer.GetComponent<WeaponDeployment>().ReadyToFire(),"Movement resets deployment");archer.transform.position=center;hp=foe.health;archer.canAttack=true;archer.Attack(foe);phase=5;start=Time.time;return;}
 if(phase==5&&Time.time-start>1.4f){Check(foe.health==hp,"Actual attack waits for deployment");phase=6;return;}
 if(phase==6&&Time.time-start>5){archer.canAttack=false;archer.Hold();Check(foe.health<hp,"Actual animated attack fires after deployment");Check(Zones().Length==0,"Locked pool never appears");phase=7;start=Time.time;return;}
 if(phase==7&&Time.time-start>2){TechnologyManager.instance.UnlockTech(pool.requiredTech[0].data[0],0);TechnologyManager.instance.UnlockTech(pool.requiredTech[0].data[0],0);Check(archer.OnAfterDamageDealCallbacks.Count(c=>c.Ability==pool)==1,"Repeated tech unlock does not stack");hp=foe2.health;archer.DealDamage(foe,10,archer.damageType,true,foe.transform.position);phase=8;start=Time.time;return;}
 if(phase==8&&Time.time-start>.3f){Check(Zones().Length==1,"One hit creates exactly one pool");Check(Vector3.Distance(Zones()[0].transform.position,foe.transform.position)<.1f,"Pool centered at impact");Capture("firealtar-pool.png");phase=9;return;}
 if(phase==9&&Time.time-start>4.7f){Check(Mathf.Abs(hp-foe2.health-48)<.2f,"Pool deals exactly 12 DPS for 4 seconds");Check(ally.health==10000&&far.health==10000,"Pool excludes allies and outside targets");Check(Zones().Length==0,"Pool expires and removes visual");TechnologyManager.instance.LockTech(pool.requiredTech[0].data[0],0);archer.DealDamage(foe,10,archer.damageType,true,foe.transform.position);Check(Zones().Length==0,"Tech lock disables pool");TechnologyManager.instance.UnlockTech(charge.requiredTech[0].data[0],0);hp=foe2.health;outerHp=far.health;archer.DealDamage(foe,10,archer.damageType,true,foe.transform.position);phase=10;start=Time.time;return;}
 if(phase==10&&Time.time-start>.15f){Check(Mathf.Abs(hp-foe2.health-100)<.1f,"Empowered epicenter deals 100 magic damage");Check(far.health==outerHp&&ally.health==10000,"Empowered blast excludes outside and allied targets");Capture("firealtar-charge.png");hp=foe2.health;archer.DealDamage(foe,10,archer.damageType,false,foe.transform.position);Check(foe2.health==hp,"Indirect damage does not trigger attack payload");TechnologyManager.instance.LockTech(charge.requiredTech[0].data[0],0);TechnologyManager.instance.UnlockTech(pool.requiredTech[0].data[0],0);impacts=0;Shoot(archer,foe);phase=11;start=Time.time;return;}
 if(phase==11&&Time.time-start>2){Check(impacts==1&&Zones().Length==1,"Real fire projectile creates one pool");Capture("firealtar-projectile-pool.png");phase=12;start=Time.time;return;}
 if(phase==12&&Time.time-start>4.5f){second=Spawn("Assets/Resources/UnitPrefabs/Units/Humans/FireAltar.prefab",center+Vector3.left*3,0);impacts=0;Shoot(archer,foe);Shoot(second,foe);phase=13;start=Time.time;return;}
 if(phase==13&&Time.time-start>2){Check(impacts==2&&Zones().Length==2,"Concurrent altars create independent pools");Capture("firealtar-concurrent.png");phase=14;start=Time.time;return;}
 if(phase==14&&Time.time-start>4.5f){impacts=0;Shoot(archer,foe);archer.Die(foeOwner,foe,false);phase=15;start=Time.time;return;}
 if(phase==15&&Time.time-start>2){Check(impacts==1&&Zones().Length==1,"Launched fire payload survives caster death");Check(!archer.UseAbilityItem(0,false,foe,Vector3.zero),"Dead altar cannot cast");second.GetComponent<WeaponDeployment>().ReadyToFire();second.Die(foeOwner,foe,false);Check(!second.effectors.Any(e=>e.effector==second.GetComponent<WeaponDeployment>().deploymentVisual),"Death clears deployment effector");phase=16;start=Time.time;return;}
 if(phase==16&&Time.time-start>5){Check(Zones().Length==0,"All concurrent and posthumous pools clean up");var p=Projectile.Spawn(0,null,AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Projectiles/fireProjectile.prefab").GetComponent<Projectile>(),center+Vector3.up,Quaternion.identity,foe,true,10,archer.damageType,true,false);foe.Die(0,null,false);phase=17;start=Time.time;return;}
 if(phase==17&&Time.time-start>3){Check(!UnityEngine.Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None).Any(p=>p.name.StartsWith("fireProjectile")),"Dead target projectile cleaned up");Check(!UnityEngine.Object.FindObjectsByType<VFXReferencer>(FindObjectsSortMode.None).Any(v=>v.name.StartsWith("PoolIgnition")||v.name.StartsWith("EmpoweredImpact")||v.name.StartsWith("DeploymentGlow")),"Skill VFX clean up after death and duration");Finish();}
 }catch(Exception e){errors.Add(e.ToString());Finish();}}
 static GroundDamageZone[] Zones()=>UnityEngine.Object.FindObjectsByType<GroundDamageZone>(FindObjectsSortMode.None).Where(z=>z.name.StartsWith("CleansingGround")).ToArray();
 static void Shoot(Unit u,Unit target){Projectile.SpawnAttack(u,u.projectileGO.GetComponent<Projectile>(),u.transform.position+Vector3.up,Quaternion.identity,target,u.splashUnitSelector,u.splashUnitSelector,10,true,false);}
 static Unit Spawn(string path,Vector3 position,int owner){var u=Unit.SpawnInternal(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Unit>(),position,0,owner);u.maxHealth=10000;u.health=10000;u.armor=0;u.healthRegen=0;u.canAttack=false;u.doNotLookForTargets=true;u.Hold();u.FoWVisible=true;u.ShowRenderers();return u;}
 static void Begin(){if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!="Assets/Scenes/Artsiom.unity"&&UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=Scene)throw new Exception("Open basic Artsiom before this check");if(!AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene)){if(!AssetDatabase.CopyAsset("Assets/Scenes/Artsiom.unity",Scene))throw new Exception("Cannot copy Artsiom");}EditorSceneManager.OpenScene(Scene);foreach(var m in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(m.GetType().Name=="MatchManager")m.enabled=false;foreach(var f in UnityEngine.Object.FindObjectsByType<FogOfWar>(FindObjectsSortMode.None))f.TurnOff=true;
 EditorSceneManager.sceneSaved-=SceneCustomData.OnSceneSaved;try{EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());}finally{EditorSceneManager.sceneSaved+=SceneCustomData.OnSceneSaved;}
 SessionState.SetBool("FireAltarSkillCheck",true);EditorApplication.isPaused=false;EditorApplication.isPlaying=true;}
 static void Setup(){foreach(var u in UnityEngine.Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)){u.canAttack=false;u.doNotLookForTargets=true;u.Hold();var a=u.GetComponent<AutoAbilityUser>();if(a)a.enabled=false;}
  if(FogOfWar.instance)FogOfWar.instance.TurnOff=true;
  if(!NavMesh.SamplePosition(new Vector3(113,0,33),out var hit,10,NavMesh.AllAreas))throw new Exception("No navmesh");center=hit.position;
  foeOwner=Enumerable.Range(1,SlotManager.instance.playerTeam.Length-2).First(i=>SlotManager.instance.playerTeam[i]!=SlotManager.instance.playerTeam[0]);

  const string sample="Assets/Resources/UnitPrefabs/Units/Humans/Swordsman.prefab";
  archer=Spawn("Assets/Resources/UnitPrefabs/Units/Humans/FireAltar.prefab",center,0);foe=Spawn(sample,center+Vector3.forward*6,foeOwner);foe2=Spawn(sample,center+new Vector3(.7f,0,6),foeOwner);ally=Spawn(sample,center+new Vector3(-.8f,0,6),0);far=Spawn(sample,center+new Vector3(3.5f,0,6),foeOwner);
  archer.LookAtInstant(new Vector2(foe.transform.position.x,foe.transform.position.z));pool=archer.abilities.OfType<EveryNthAttack>().Single(a=>a.name=="FireAltar_CleansingPool");charge=archer.abilities.OfType<EveryNthAttack>().Single(a=>a.name=="FireAltar_EmpoweredCharge");
  Check(archer.abilityLocked[Array.IndexOf(archer.abilities,pool)]&&archer.abilityLocked[Array.IndexOf(archer.abilities,charge)],"Both specializations start tech locked");
  SkillPresentationEvents.SkillFired+=OnFired;
  foreach(var m in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(m.GetType().Name=="Camera_TopDown"||m.GetType().Name=="CameraAxisLock")m.enabled=false;
  camera=Camera.main;camera.transform.SetParent(null,true);camera.transform.position=center+new Vector3(7,9,-5);camera.transform.LookAt(center+new Vector3(0,.5f,3));camera.orthographic=true;camera.orthographicSize=6;}
 static void Check(bool ok,string text){results.Add((ok?"PASS ":"FAIL ")+text);if(!ok)errors.Add(text);File.WriteAllLines(W+"firealtar-runtime-results.txt",results);}
 static void Capture(string name){var rt=new RenderTexture(1280,800,24);var prior=camera.targetTexture;var active=RenderTexture.active;try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var t=new Texture2D(1280,800,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,1280,800),0,0);t.Apply();File.WriteAllBytes(W+name,t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);}finally{camera.targetTexture=prior;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(rt);}}
 static void Finish(){File.WriteAllLines(W+"firealtar-runtime-results.txt",results);File.WriteAllLines(W+"firealtar-runtime-errors.txt",errors);File.WriteAllText(W+"firealtar-runtime-finished.txt","Checks="+results.Count+" errors="+errors.Count);SessionState.SetBool("FireAltarSkillCheck",false);EditorApplication.isPaused=true;}
}
#endif


