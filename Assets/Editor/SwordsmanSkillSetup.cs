#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using UnityEditor;using UnityEngine;using StrategyCore;
[InitializeOnLoad] public static class SwordsmanSkillSetup
{
 const string Work=@"C:\Users\aleks\Documents\Codex\2026-09-11\v\work\skills-28\";
 const string Folder="Assets/Resources/Ability/Roster/Humans/Swordsman/";
 static SwordsmanSkillSetup(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(Work+"configure-swordsman.txt"))return;File.Delete(Work+"configure-swordsman.txt");try{Configure();File.WriteAllText(Work+"swordsman-configured.txt","OK");}catch(Exception e){File.WriteAllText(Work+"swordsman-configured.txt",e.ToString());Debug.LogException(e);}}
 static T Asset<T>(string name) where T:Ability
 {
  var a=AssetDatabase.LoadAssetAtPath<T>(Folder+name+".asset");if(a)return a;
  a=ScriptableObject.CreateInstance<T>();a.id=InterflowEditorUI.NextFreeId("t:Ability",o=>((Ability)o).id);a.abilityName=new[]{name};AssetDatabase.CreateAsset(a,Folder+name+".asset");return a;
 }
 static VFXReferencer Sparks(string name,Color color)
 {
  const string folder="Assets/VFX/Skills/Swordsman/";Directory.CreateDirectory(folder);
  var old=AssetDatabase.LoadAssetAtPath<GameObject>(folder+name+".prefab");
  var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Synty/PolygonDungeonRealms/Prefabs/FX/FX_Sparks_01.prefab");
  if(!source)throw new Exception("Missing existing Synty sparks");
  var root=old?PrefabUtility.LoadPrefabContents(folder+name+".prefab"):new GameObject(name);try{
   if(!root.GetComponent<VFXReferencer>())root.AddComponent<VFXReferencer>();var child=root.transform.childCount>0?root.transform.GetChild(0).gameObject:(GameObject)PrefabUtility.InstantiatePrefab(source);child.transform.SetParent(root.transform,false);child.transform.localScale=Vector3.one;
   foreach(var ps in child.GetComponentsInChildren<ParticleSystem>(true)){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var m=ps.main;m.loop=false;m.duration=.15f;m.startLifetime=new ParticleSystem.MinMaxCurve(.3f,.45f);m.startSpeed=new ParticleSystem.MinMaxCurve(2f,3f);m.startSize=new ParticleSystem.MinMaxCurve(.08f,.15f);m.startColor=color;m.maxParticles=16;m.playOnAwake=true;var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=.08f;var e=ps.emission;e.rateOverTime=0;e.rateOverDistance=0;e.SetBursts(new[]{new ParticleSystem.Burst(0,12)});}
   return PrefabUtility.SaveAsPrefabAsset(root,folder+name+".prefab").GetComponent<VFXReferencer>();
  }finally{if(old)PrefabUtility.UnloadPrefabContents(root);else UnityEngine.Object.DestroyImmediate(root);}
 }
 static void Configure()
 {
  var wall=AssetDatabase.LoadAssetAtPath<CompositePassive>(Folder+"Swordsman_ShieldWall.asset");if(!wall)throw new Exception("Create ShieldWall in Interflow Editor first");
  var parry=Asset<CompositePassive>("Swordsman_Parry");
  var physical=AssetDatabase.LoadAssetAtPath<DamageType>("Assets/Resources/DamageTypes/DontRemove/DamageType.asset");if(!physical)throw new Exception("Missing standard damage type");
  var extraPhysical=AssetDatabase.FindAssets("t:DamageType").Select(g=>AssetDatabase.LoadAssetAtPath<DamageType>(AssetDatabase.GUIDToAssetPath(g))).Where(t=>t!=physical&&!t.ignoresArmor).ToArray();
  var blockV=Asset<CompositeSkill>("Swordsman_ShieldContact");var parryV=Asset<CompositeSkill>("Swordsman_ParryContact");
  blockV.abilityName=new[]{"Стена щитов — попадание"};parryV.abilityName=new[]{"Парирование — контакт"};
  foreach(var v in new[]{blockV,parryV}){v.targetMode=SkillTargetMode.Self;v.spawnSocket=SkillSocketType.LeftHand;v.castVfxLifetime=.6f;v.impactVFX=null;v.procAnimationState="";EditorUtility.SetDirty(v);}
  blockV.castVFX=Sparks("ShieldContact",new Color(.55f,.8f,1));parryV.castVFX=Sparks("ParryContact",new Color(1,.85f,.4f));
  wall.abilityName=new[]{"Стена щитов"};wall.description=new[]{"Блокирует 20% входящего физического урона спереди."};wall.maxLevels=1;
  wall.defense=new PassiveDefenseBlock{enabled=true,damageType=physical,additionalDamageTypes=extraPhysical,onlyDirectAttack=false,frontArc=180,chance=1,incomingMultiplier=.8f,presentation=blockV};
  parry.abilityName=new[]{"Парирование"};parry.description=new[]{"15% шанс отразить физическую атаку и нанести встречный удар."};parry.maxLevels=1;
  parry.defense=new PassiveDefenseBlock{enabled=true,damageType=physical,additionalDamageTypes=extraPhysical,onlyDirectAttack=true,frontArc=360,chance=.15f,incomingMultiplier=0,counterAttackFraction=1,presentation=parryV};
  wall.requiredTech=new[]{new MultiLevel<Technology>{data=new[]{AssetDatabase.LoadAssetAtPath<Technology>("Assets/Resources/Technology/Lyudi_T2_A1.asset")}}};
  parry.requiredTech=new[]{new MultiLevel<Technology>{data=new[]{AssetDatabase.LoadAssetAtPath<Technology>("Assets/Resources/Technology/Lyudi_T2_A2.asset")}}};
  if(wall.requiredTech[0].data[0]==null||parry.requiredTech[0].data[0]==null)throw new Exception("Missing existing melee specialization technologies");
  EditorUtility.SetDirty(wall);EditorUtility.SetDirty(parry);
  const string path="Assets/Resources/UnitPrefabs/Units/Humans/Swordsman.prefab";var go=PrefabUtility.LoadPrefabContents(path);
  try{
   var u=go.GetComponent<Unit>();var old=u.abilities??Array.Empty<Ability>();
   var keep=old.Where(a=>a&&a!=wall&&a!=parry&&AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(a))!="745d7ba2997971a49a5ae87f4199a234").ToList();keep.Add(wall);keep.Add(parry);u.abilities=keep.ToArray();u.abilityLevel=new int[u.abilities.Length];
   var auto=go.GetComponent<AutoAbilityUser>();if(auto){var aso=new SerializedObject(auto);var list=aso.FindProperty("autoAbilities");for(int i=list.arraySize-1;i>=0;i--){var a=list.GetArrayElementAtIndex(i).objectReferenceValue as Ability;if(!a||!u.abilities.Contains(a)){list.GetArrayElementAtIndex(i).objectReferenceValue=null;list.DeleteArrayElementAtIndex(i);}}aso.ApplyModifiedPropertiesWithoutUndo();}
   var sockets=go.GetComponentInChildren<CharacterSockets>(true);if(!sockets)sockets=go.AddComponent<CharacterSockets>();
   var bones=go.GetComponentsInChildren<Transform>(true);var left=bones.FirstOrDefault(t=>t.name=="Hand_L"||t.name=="LeftHand"||t.name=="hand_l");
   if(!left)left=bones.FirstOrDefault(t=>t.name.IndexOf("Hand",StringComparison.OrdinalIgnoreCase)>=0&&(t.name.EndsWith("_L")||t.name.Contains("Left")));
   if(!left)throw new Exception("No left hand bone; inspect sockets before continuing");
   var so=new SerializedObject(sockets);so.FindProperty("leftHand").objectReferenceValue=left;so.ApplyModifiedPropertiesWithoutUndo();
   PrefabUtility.SaveAsPrefabAsset(go,path);
  }finally{PrefabUtility.UnloadPrefabContents(go);}
  AssetDatabase.SaveAssets();InterflowAbilityUsage.InvalidateCache();InterflowAbilityGroups.InvalidateCache();
 }
}
#endif


