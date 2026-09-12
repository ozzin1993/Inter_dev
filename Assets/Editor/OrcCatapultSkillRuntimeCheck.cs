#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using UnityEngine.AI;using StrategyCore;
[InitializeOnLoad]public static class OrcCatapultSkillRuntimeCheck {
 const string W=@"C:\Users\aleks\Documents\Codex\2026-09-11\v\work\skills-28\";
 const string Scene="Assets/Scenes/Archive/Artsiom_SkillChecks.unity";
 static Unit archer,foe,foe2,ally,far,second;static EveryNthAttack pool;static BonusDamageVsUnitType breaker;static Technology tar;static float start,hp,speed;static int phase,foeOwner,impacts;static double boot;static Vector3 center,point;static Camera camera;static List<string> results=new List<string>(),errors=new List<string>();
 static OrcCatapultSkillRuntimeCheck(){boot=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;Application.logMessageReceived+=Log;}
 static void Log(string msg,string stack,LogType t){if(SessionState.GetBool("OrcCatapultSkillCheck",false)&&(t==LogType.Error||t==LogType.Exception||t==LogType.Assert))errors.Add(msg+"\n"+stack);}
 static void OnFired(Unit u,int id,int level,Unit target,Vector3 point){if(id==pool.onProcSkill.id)impacts++;}
 static void Tick(){try{
 if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
 if(!EditorApplication.isPlaying&&!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists(W+"run-orccatapult.txt")){try{File.Delete(W+"run-orccatapult.txt");}catch(IOException){return;}Begin();return;}
 if(!SessionState.GetBool("OrcCatapultSkillCheck",false)||!EditorApplication.isPlaying||EditorApplication.isPaused)return;
 if(phase==0){if(!SlotManager.instance||!SlotManager.instance.gameOn){if(EditorApplication.timeSinceStartup-boot>90)throw new Exception("Game initialization timeout");return;}Setup();phase=1;start=Time.time;return;}


 if(phase==1&&Time.time-start>.5f){Check(InterflowTargeting.PickWithPriority(archer,10,archer.splashUnitSelector,false)==foe,"Attack targeting selects dense pair instead of isolated enemy");hp=foe2.health;impacts=0;Shoot(archer,foe);phase=2;start=Time.time;return;}
 if(phase==2&&Time.time-start>.25f){Capture("orccatapult-flight.png");phase=3;return;}
 if(phase==3&&Time.time-start>1.8f){Check(impacts==1&&Zones().Length==1,"Multi-victim impact creates exactly one fire pool");Check(Vector3.Distance(Zones()[0].transform.position,point)<.2f,"Pool centered at landing");Check(foe2.moveSpeed==speed,"Locked tar does not slow");Check(ally.health==10000&&far.health==10000,"Splash and zone exclude allies and distant enemies");Capture("orccatapult-pool.png");phase=4;return;}
 if(phase==4&&Time.time-start>5.8f){Check(Mathf.Abs(hp-foe2.health-50)<.15f,"One ten-damage hit plus exactly forty fire damage over four seconds");Check(Zones().Length==0,"Four-second zone expires");TechnologyManager.instance.UnlockTech(tar,0);Shoot(archer,foe);phase=5;start=Time.time;return;}
 if(phase==5&&Time.time-start>1.5f){Check(Mathf.Abs(foe2.moveSpeed-speed*.65f)<.001f,"Tar slows movement exactly thirty-five percent");Check(ally.moveSpeed==speed&&far.moveSpeed==speed,"Tar only affects enemies inside pool");Capture("orccatapult-tar.png");foe2.agent.Warp(point+Vector3.right*5);phase=6;start=Time.time;return;}
 if(phase==6&&Time.time-start>.6f){Check(Mathf.Abs(foe2.moveSpeed-speed)<.001f,"Leaving pool removes slow");foe2.agent.Warp(point+Vector3.right*.7f);phase=7;start=Time.time;return;}
 if(phase==7&&Time.time-start>.5f){Check(Mathf.Abs(foe2.moveSpeed-speed*.65f)<.001f,"Reentering pool reapplies slow");TechnologyManager.instance.LockTech(tar,0);phase=8;start=Time.time;return;}
 if(phase==8&&Time.time-start>.6f){Check(Mathf.Abs(foe2.moveSpeed-speed)<.001f,"Tech lock stops refreshing tar");phase=9;start=Time.time;return;}
 if(phase==9&&Time.time-start>4){KnockTests();BuildingTests();phase=10;start=Time.time;return;}
 if(phase==10&&Time.time-start>5){impacts=0;ShootPoint(archer,center+Vector3.back*5);phase=11;start=Time.time;return;}
 if(phase==11&&Time.time-start>2){Check(impacts==1&&Zones().Length==1,"Landing on empty ground still creates pool");phase=12;start=Time.time;return;}
 if(phase==12&&Time.time-start>5){second=Spawn("Assets/Resources/UnitPrefabs/Units/Orcs/OrcCatapult.prefab",center+Vector3.left*3,0);second.damageType=archer.damageType;impacts=0;Shoot(archer,foe);Shoot(second,foe);phase=13;start=Time.time;return;}
 if(phase==13&&Time.time-start>2){Check(impacts==2&&Zones().Length==2,"Concurrent catapults create independent pools");phase=14;start=Time.time;return;}
 if(phase==14&&Time.time-start>5){impacts=0;archer.canAttack=true;archer.Attack(foe);phase=15;start=Time.time;return;}
 if(phase==15&&Time.time-start>7){archer.canAttack=false;archer.Hold();Check(impacts>=2,"Native attack animation repeatedly launches burning boulders");Capture("orccatapult-animated.png");phase=16;start=Time.time;return;}
 if(phase==16&&Time.time-start>6){impacts=0;Shoot(archer,foe);archer.Die(foeOwner,foe,false);foe.Die(0,second,false);phase=17;start=Time.time;return;}
 if(phase==17&&Time.time-start>2){Check(impacts==1&&Zones().Length==1,"Launched ground attack survives death of caster and target");Check(!archer.UseAbilityItem(0,false,foe2,Vector3.zero),"Dead caster rejected");second.Die(foeOwner,foe2,false);phase=18;start=Time.time;return;}
 if(phase==18&&Time.time-start>5){Check(Zones().Length==0,"Posthumous and concurrent pools clean up");Check(!UnityEngine.Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None).Any(p=>p.name.StartsWith("OrcCatapult_BurningBoulder")),"Projectiles cleaned up");Check(!UnityEngine.Object.FindObjectsByType<VFXReferencer>(FindObjectsSortMode.None).Any(v=>v.name.StartsWith("BoulderExplosion")),"Impact VFX cleaned up");Finish();}
 }catch(Exception e){errors.Add(e.ToString());Finish();}}
 static GroundDamageZone[] Zones()=>UnityEngine.Object.FindObjectsByType<GroundDamageZone>(FindObjectsSortMode.None).Where(z=>z.name.StartsWith("BurningGround")).ToArray();
 static void ShootPoint(Unit u,Vector3 point){Projectile.SpawnAttack(u,u.projectileGO.GetComponent<Projectile>(),u.transform.position+Vector3.up,Quaternion.identity,point,u.splashUnitSelector,u.splashUnitSelector,10,true,false);}
 static void KnockTests(){foe.tier=1;foe2.tier=3;var p=foe.transform.position;var q=foe2.transform.position;pool.onProcSkill.Use(archer,0,0,point);Check(Vector3.Distance(p,foe.transform.position)>.8f,"Tier-one infantry knocked back");Check(Vector3.Distance(q,foe2.transform.position)<.01f,"Tier-three infantry unaffected by knockback");foe.agent.Warp(p);var ci=foe.GetComponent<ControlImmunity>()??foe.gameObject.AddComponent<ControlImmunity>();ci.Add();pool.onProcSkill.Use(archer,0,0,point);Check(Vector3.Distance(p,foe.transform.position)<.01f,"Control immunity prevents knockback");ci.Remove();foe.tier=3;}
 static void BuildingTests(){var type=far.unitType;far.unitType=UnitType.Building;float h=far.health;archer.DealDamage(far,100,archer.damageType,true,far.transform.position);Check(Mathf.Abs(h-far.health-100)<.01f,"Locked wallbreaker leaves building damage unchanged");TechnologyManager.instance.UnlockTech(breaker.requiredTech[0].data[0],0);h=far.health;archer.DealDamage(far,100,archer.damageType,true,far.transform.position);Check(Mathf.Abs(h-far.health-160)<.01f,"Wallbreaker increases building attack damage sixty percent");far.unitType=type;h=far.health;archer.DealDamage(far,100,archer.damageType,true,far.transform.position);Check(Mathf.Abs(h-far.health-100)<.01f,"Wallbreaker does not amplify unit damage");}
 static void Shoot(Unit u,Unit target){Projectile.SpawnAttack(u,u.projectileGO.GetComponent<Projectile>(),u.transform.position+Vector3.up,Quaternion.identity,target,u.splashUnitSelector,u.splashUnitSelector,10,true,false);}
 static Unit Spawn(string path,Vector3 position,int owner){var u=Unit.SpawnInternal(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Unit>(),position,0,owner);u.maxHealth=10000;u.health=10000;u.armor=0;u.healthRegen=0;u.canAttack=false;u.doNotLookForTargets=true;u.Hold();u.FoWVisible=true;u.ShowRenderers();return u;}
 static void Begin(){if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!="Assets/Scenes/Artsiom.unity"&&UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=Scene)throw new Exception("Open basic Artsiom before this check");if(!AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene)){if(!AssetDatabase.CopyAsset("Assets/Scenes/Artsiom.unity",Scene))throw new Exception("Cannot copy Artsiom");}EditorSceneManager.OpenScene(Scene);foreach(var m in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(m.GetType().Name=="MatchManager")m.enabled=false;foreach(var f in UnityEngine.Object.FindObjectsByType<FogOfWar>(FindObjectsSortMode.None))f.TurnOff=true;
 EditorSceneManager.sceneSaved-=SceneCustomData.OnSceneSaved;try{EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());}finally{EditorSceneManager.sceneSaved+=SceneCustomData.OnSceneSaved;}
 SessionState.SetBool("OrcCatapultSkillCheck",true);EditorApplication.isPaused=false;EditorApplication.isPlaying=true;}
 static void Setup(){foreach(var u in UnityEngine.Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)){u.canAttack=false;u.doNotLookForTargets=true;u.Hold();var a=u.GetComponent<AutoAbilityUser>();if(a)a.enabled=false;}
  if(FogOfWar.instance)FogOfWar.instance.TurnOff=true;
  if(!NavMesh.SamplePosition(new Vector3(113,0,33),out var hit,10,NavMesh.AllAreas))throw new Exception("No navmesh");center=hit.position;
  foeOwner=Enumerable.Range(1,SlotManager.instance.playerTeam.Length-2).First(i=>SlotManager.instance.playerTeam[i]!=SlotManager.instance.playerTeam[0]);

  const string sample="Assets/Resources/UnitPrefabs/Units/Humans/Swordsman.prefab";
  archer=Spawn("Assets/Resources/UnitPrefabs/Units/Orcs/OrcCatapult.prefab",center,0);foe=Spawn(sample,center+Vector3.forward*6,foeOwner);foe2=Spawn(sample,center+new Vector3(.7f,0,6),foeOwner);ally=Spawn(sample,center+new Vector3(-.8f,0,6),0);far=Spawn(sample,center+new Vector3(3.5f,0,6),foeOwner);
  archer.LookAtInstant(new Vector2(foe.transform.position.x,foe.transform.position.z));pool=archer.abilities.OfType<EveryNthAttack>().Single(a=>a.name=="OrcCatapult_BurningBoulder");breaker=archer.abilities.OfType<BonusDamageVsUnitType>().Single();tar=AssetDatabase.LoadAssetAtPath<Technology>("Assets/Resources/Technology/Orki_T3_A1.asset");point=foe.transform.position;speed=foe2.moveSpeed;foe.tier=3;foe2.tier=3;archer.damageType=AssetDatabase.LoadAssetAtPath<DamageType>("Assets/Resources/DamageTypes/Magic.asset");
  Check(!archer.abilityLocked[Array.IndexOf(archer.abilities,pool)]&&archer.abilityLocked[Array.IndexOf(archer.abilities,breaker)],"Base boulder ready, wallbreaker tech locked");
  Check(GameManager.instance.gameProjectiles.TryGetValue(archer.projectileGO.GetComponent<Projectile>().id,out var registered)&&registered.gameObject==archer.projectileGO,"Burning projectile registered by native ID");
  SkillPresentationEvents.SkillFired+=OnFired;
  foreach(var m in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(m.GetType().Name=="Camera_TopDown"||m.GetType().Name=="CameraAxisLock")m.enabled=false;
  camera=Camera.main;camera.transform.SetParent(null,true);camera.transform.position=center+new Vector3(7,9,-5);camera.transform.LookAt(center+new Vector3(0,.5f,3));camera.orthographic=true;camera.orthographicSize=6;}
 static void Check(bool ok,string text){results.Add((ok?"PASS ":"FAIL ")+text);if(!ok)errors.Add(text);File.WriteAllLines(W+"orccatapult-runtime-results.txt",results);}
 static void Capture(string name){var rt=new RenderTexture(1280,800,24);var prior=camera.targetTexture;var active=RenderTexture.active;try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var t=new Texture2D(1280,800,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,1280,800),0,0);t.Apply();File.WriteAllBytes(W+name,t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);}finally{camera.targetTexture=prior;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(rt);}}
 static void Finish(){File.WriteAllLines(W+"orccatapult-runtime-results.txt",results);File.WriteAllLines(W+"orccatapult-runtime-errors.txt",errors);File.WriteAllText(W+"orccatapult-runtime-finished.txt","Checks="+results.Count+" errors="+errors.Count);SessionState.SetBool("OrcCatapultSkillCheck",false);EditorApplication.isPaused=true;}
}
#endif


