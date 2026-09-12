#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using UnityEngine.AI;using StrategyCore;
[InitializeOnLoad]public static class BloodBerserkerSkillRuntimeCheck {
 const string W=@"C:\Users\aleks\Documents\Codex\2026-09-11\v\work\skills-28\";
 const string Scene="Assets/Scenes/Archive/Artsiom_SkillChecks.unity";
 static Unit ogre,primary,near,ally,outside;static RageFromMissingHp rage;static CompositePassive giant;static LifestealPassive thirst;static Effector slow;static DamageType physical;static Vector3 center;static Camera camera;static int foeOwner,phase;static float start,baseArmor,baseAS,baseMS;static double boot;static List<string> results=new List<string>(),errors=new List<string>();
 static BloodBerserkerSkillRuntimeCheck(){boot=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;Application.logMessageReceived+=Log;}
 static void Log(string msg,string stack,LogType t){if(SessionState.GetBool("BloodBerserkerSkillCheck",false)&&(t==LogType.Error||t==LogType.Exception||t==LogType.Assert))errors.Add(msg+"\n"+stack);}
 static void Tick(){try{
 if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
 if(!EditorApplication.isPlaying&&!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists(W+"run-berserker.txt")){File.Delete(W+"run-berserker.txt");Begin();return;}
 if(!SessionState.GetBool("BloodBerserkerSkillCheck",false)||!EditorApplication.isPlaying||EditorApplication.isPaused)return;
 if(phase==0){if(!SlotManager.instance||!SlotManager.instance.gameOn){if(EditorApplication.timeSinceStartup-boot>90)throw new Exception("Startup timeout");return;}Setup();phase=1;start=Time.time;return;}
 if(phase==1&&Time.time-start>.8f){Mechanics();phase=2;start=Time.time;return;}
 if(phase==2&&Time.time-start>.35f){Check(Mathf.Abs(ogre.moveSpeed-baseMS)<.001f,"Wrath removes movement slow");Check(!ogre.effectors.Any(e=>e.effector==slow),"Wrath removes attack slow and outgoing damage weakness");Capture("berserker-frenzy.png");Edges();ogre.canAttack=true;ogre.attackDamage=30;primary.SetHP(1000);ogre.SetHP(100);ogre.Attack(primary);phase=3;start=Time.time;return;}
 if(phase==3&&Time.time-start>5f){Check(primary.health<970,"Native attack animation repeats");Check(ogre.health>100,"Native attack events heal caster");Capture("berserker-native-lifesteal.png");ogre.canAttack=false;ogre.Hold();ogre.SetHP(1000);Check(Mathf.Abs(ogre.armor-baseArmor)<.001f&&Mathf.Abs(ogre.attackSpeed-baseAS)<.001f,"Full healing restores original stats");Check(!ogre.effectors.Any(e=>e.effector==rage.ragePresentation),"Full healing ends frenzy visual");ogre.SetHP(100);ogre.Die(-1,null,false);phase=4;start=Time.time;return;}
 if(phase==4&&Time.time-start>2f){Check(!UnityEngine.Object.FindObjectsByType<VFXReferencer>(FindObjectsSortMode.None).Any(v=>v.name.StartsWith("BloodFrenzy")||v.name.StartsWith("BloodSiphon")),"Death cleans all blood visuals");Finish();}
 }catch(Exception e){errors.Add(e.ToString());Finish();}}
 static void Begin(){if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!="Assets/Scenes/Artsiom.unity"&&UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=Scene)throw new Exception("Open basic Artsiom before this check");if(!AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene)){if(!AssetDatabase.CopyAsset("Assets/Scenes/Artsiom.unity",Scene))throw new Exception("Cannot copy Artsiom");}EditorSceneManager.OpenScene(Scene);foreach(var m in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(m.GetType().Name=="MatchManager")m.enabled=false;foreach(var f in UnityEngine.Object.FindObjectsByType<FogOfWar>(FindObjectsSortMode.None))f.TurnOff=true;
 EditorSceneManager.sceneSaved-=SceneCustomData.OnSceneSaved;try{EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());}finally{EditorSceneManager.sceneSaved+=SceneCustomData.OnSceneSaved;}
 SessionState.SetBool("BloodBerserkerSkillCheck",true);EditorApplication.isPaused=false;EditorApplication.isPlaying=true;}


 static Unit Spawn(Vector3 p,int owner,bool isBlood=false){var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UnitPrefabs/Units/"+(isBlood?"Orcs/BloodBerserker":"Humans/Swordsman")+".prefab").GetComponent<Unit>();var u=Unit.SpawnInternal(asset,p,0,owner);u.maxHealth=1000;u.SetHP(1000);if(!isBlood)u.armor=0;u.canAttack=false;u.doNotLookForTargets=true;u.Hold();u.FoWVisible=true;u.ShowRenderers();return u;}
 static void Setup(){foreach(var u in UnityEngine.Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)){u.canAttack=false;u.doNotLookForTargets=true;u.Hold();var a=u.GetComponent<AutoAbilityUser>();if(a)a.enabled=false;}if(FogOfWar.instance)FogOfWar.instance.TurnOff=true;
 if(!NavMesh.SamplePosition(new Vector3(113,0,33),out var hit,10,NavMesh.AllAreas))throw new Exception("No navmesh");center=hit.position;
 foeOwner=Enumerable.Range(1,SlotManager.instance.playerTeam.Length-2).First(i=>SlotManager.instance.playerTeam[i]!=SlotManager.instance.playerTeam[0]);
 ogre=Spawn(center,0,true);primary=Spawn(center+Vector3.forward*2,foeOwner);near=Spawn(center+Vector3.left*1.5f,foeOwner);ally=Spawn(center+Vector3.right*1.5f,0);outside=Spawn(center+Vector3.forward*6,foeOwner);
 rage=(RageFromMissingHp)ogre.abilities.First(a=>a.name=="BloodBerserker_DemonicRage");giant=(CompositePassive)ogre.abilities.First(a=>a.name=="BloodBerserker_UnstoppableWrath");thirst=(LifestealPassive)ogre.abilities.First(a=>a.name=="BloodBerserker_BloodThirst");physical=AssetDatabase.LoadAssetAtPath<DamageType>("Assets/Resources/DamageTypes/DontRemove/DamageType.asset");baseArmor=ogre.armor;baseAS=ogre.attackSpeed;baseMS=ogre.moveSpeed;
 foreach(var m in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))if(m.GetType().Name=="Camera_TopDown"||m.GetType().Name=="CameraAxisLock")m.enabled=false;
 camera=Camera.main;camera.transform.SetParent(null,true);camera.transform.position=center+new Vector3(6,7,-5);camera.transform.LookAt(center+new Vector3(0,.5f,1));camera.orthographic=true;camera.orthographicSize=5;}
 static void Check(bool ok,string text){results.Add((ok?"PASS ":"FAIL ")+text);if(!ok)errors.Add(text);File.WriteAllLines(W+"berserker-runtime-results.txt",results);}
 static void Hit(bool direct=true){ogre.DealDamage(primary,100,physical,direct,primary.transform.position);}
 static void Reset(){foreach(var u in new[]{primary,near,ally,outside})u.SetHP(1000);}
 static bool Immune()=>ogre.TryGetComponent<ControlImmunity>(out var ci)&&ci.Active;
 static void Mechanics(){
 Check(ogre.abilities.Contains(thirst)&&ogre.abilities.Contains(rage),"Both base passives assigned");
 Check(ogre.abilityLocked[Array.IndexOf(ogre.abilities,giant)],"Wrath specialization initially locked");
 ogre.SetHP(500);Check(Mathf.Abs(ogre.attackSpeed-baseAS/1.5f)<.001f,"Half HP gives 50 percent attack speed");
 ogre.SetHP(200);Check(Mathf.Abs(ogre.attackSpeed-baseAS/1.8f)<.001f,"20 percent HP gives 80 percent attack speed");
 ogre.SetHP(100);Check(Mathf.Abs(ogre.attackSpeed-baseAS/1.8f)<.001f,"Below 20 percent capped at 80 percent");
 Check(Mathf.Abs(ogre.armor-baseArmor)<.001f,"Rage does not change armor");
 rage.Unlock(ogre,0,0);Check(Mathf.Abs(ogre.attackSpeed-baseAS/1.8f)<.001f,"Repeated unlock does not stack rage");
 ogre.SetHP(300);Hit();Check(ogre.health==300,"Exactly 30 percent has no lifesteal");
 ogre.SetHP(299);Hit();Check(Mathf.Abs(ogre.health-329)<.001f,"Below threshold restores 30 percent of hit");
 ogre.SetHP(100);Hit(false);Check(ogre.health==100,"Indirect damage does not leech");
 ogre.DealDamage(ally,100,physical,true,ally.transform.position);Check(ogre.health==100,"Friendly attacks do not leech");
 thirst.Unlock(ogre,0,0);Hit();Check(Mathf.Abs(ogre.health-130)<.001f,"Repeated lifesteal unlock does not duplicate healing");
 TechnologyManager.instance.UnlockTech(thirst.upgradeTechnology,0);ogre.SetHP(100);Hit();Check(Mathf.Abs(ogre.health-150)<.001f,"Blood Thirst upgrade heals 50 percent");
 TechnologyManager.instance.LockTech(thirst.upgradeTechnology,0);ogre.SetHP(100);Hit();Check(Mathf.Abs(ogre.health-130)<.001f,"Lock returns base 30 percent");
 slow=ScriptableObject.CreateInstance<Effector>();slow.duration=10;slow.passiveEffectsOn=true;slow.passiveEffects=new AbilityPassiveEffects();slow.passiveEffects.moveSpeedPercentageChange=-.25f;slow.passiveEffects.attackSpeedPercentageChange=-.2f;slow.passiveEffects.damagePercentageChange=-.3f;
 Effector.EffectorAdd(ogre,slow,primary,foeOwner);Check(ogre.moveSpeed<baseMS,"Locked wrath allows slow");
 TechnologyManager.instance.UnlockTech(giant.requiredTech[0].data[0],0);
 }
 static void Edges(){
 ogre.Stun(.2f);Check(ogre.stunned,"Wrath does not invent stun immunity");
 ogre.SetHP(300);Effector.EffectorAdd(ogre,slow,primary,foeOwner);Check(ogre.moveSpeed<baseMS,"Above frenzy threshold remains vulnerable");foreach(var e in ogre.effectors.ToArray())if(e.effector==slow)Effector.EffectorRemove(ogre,e);
 TechnologyManager.instance.LockTech(giant.requiredTech[0].data[0],0);ogre.SetHP(100);Effector.EffectorAdd(ogre,slow,primary,foeOwner);Check(ogre.moveSpeed<baseMS,"Lock restores vulnerability at low HP");foreach(var e in ogre.effectors.ToArray())if(e.effector==slow)Effector.EffectorRemove(ogre,e);
 var other=Spawn(center+Vector3.back*4,0,true);other.SetHP(500);Check(Mathf.Abs(other.attackSpeed-baseAS/1.5f)<.001f&&Mathf.Abs(ogre.attackSpeed-baseAS/1.8f)<.001f,"Two berserkers maintain independent rage");other.SetHP(100);other.Die(-1,null,false);Check(other.dead,"Death during frenzy succeeds");
 ogre.SetHP(100);primary.SetHP(20);Hit();Check(primary.dead&&ogre.health==130,"Killing blow heals exactly once");primary=Spawn(center+Vector3.forward*2,foeOwner);
 }
 static void Capture(string name){var fx=UnityEngine.Object.FindObjectsByType<VFXReferencer>(FindObjectsSortMode.None).Where(v=>v.name.StartsWith("Blood")).ToArray();Check(fx.Length>0,"Presentation spawned: "+name);var rt=new RenderTexture(1280,800,24);var prior=camera.targetTexture;var active=RenderTexture.active;try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var t=new Texture2D(1280,800,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,1280,800),0,0);t.Apply();File.WriteAllBytes(W+name,t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);}finally{camera.targetTexture=prior;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(rt);}}


 static void Finish(){File.WriteAllLines(W+"berserker-runtime-results.txt",results);File.WriteAllLines(W+"berserker-runtime-errors.txt",errors);File.WriteAllText(W+"berserker-runtime-finished.txt","Checks="+results.Count+" errors="+errors.Count);SessionState.SetBool("BloodBerserkerSkillCheck",false);EditorApplication.isPaused=true;}
}
#endif
