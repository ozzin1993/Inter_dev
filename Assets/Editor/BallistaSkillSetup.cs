#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using UnityEditor;using UnityEngine;using StrategyCore;
[InitializeOnLoad]public static class BallistaSkillSetup
{
 const string W=@"C:\Users\aleks\Documents\Codex\2026-09-11\v\work\skills-28\";
 const string F="Assets/Resources/Ability/Roster/Humans/Ballista/";
 static BallistaSkillSetup(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(W+"configure-ballista.txt"))return;try{File.Delete(W+"configure-ballista.txt");}catch(IOException){return;}try{Configure();File.WriteAllText(W+"ballista-configured.txt","OK");}catch(Exception e){File.WriteAllText(W+"ballista-configured.txt",e.ToString());Debug.LogException(e);}}
 static T Asset<T>(string name)where T:Ability{var a=AssetDatabase.LoadAssetAtPath<T>(F+name+".asset");if(a)return a;a=ScriptableObject.CreateInstance<T>();a.id=InterflowEditorUI.NextFreeId("t:Ability",o=>((Ability)o).id);a.abilityName=new[]{name};AssetDatabase.CreateAsset(a,F+name+".asset");return a;}
 static VFXReferencer Vfx(string name,string sourcePath,float scale)
 {
  string path="Assets/VFX/Skills/Ballista/"+name+".prefab";Directory.CreateDirectory("Assets/VFX/Skills/Ballista");var old=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(old){if(name=="PiercingHit"){var edit=PrefabUtility.LoadPrefabContents(path);try{TunePierce(edit);PrefabUtility.SaveAsPrefabAsset(edit,path);}finally{PrefabUtility.UnloadPrefabContents(edit);}}return old.GetComponent<VFXReferencer>();}
  var source=AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);if(!source)throw new Exception("Missing VFX: "+sourcePath);
  var root=new GameObject(name);try{root.AddComponent<VFXReferencer>();var child=(GameObject)PrefabUtility.InstantiatePrefab(source);child.transform.SetParent(root.transform,false);child.transform.localScale=Vector3.one*scale;foreach(var ps in child.GetComponentsInChildren<ParticleSystem>(true)){var m=ps.main;m.loop=false;m.playOnAwake=true;}if(name=="PiercingHit")TunePierce(root);return PrefabUtility.SaveAsPrefabAsset(root,path).GetComponent<VFXReferencer>();}finally{UnityEngine.Object.DestroyImmediate(root);}
 }
 static void TunePierce(GameObject root){foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true)){ps.transform.localScale=Vector3.one;var m=ps.main;m.loop=false;m.startLifetime=new ParticleSystem.MinMaxCurve(.25f,.4f);m.startSpeed=new ParticleSystem.MinMaxCurve(2f,3f);m.startSize=new ParticleSystem.MinMaxCurve(.08f,.14f);m.startColor=new Color(.55f,.85f,1f);var e=ps.emission;e.rateOverTime=0;e.SetBursts(new[]{new ParticleSystem.Burst(0,10)});var sh=ps.shape;sh.shapeType=ParticleSystemShapeType.Sphere;sh.radius=.08f;}}
 static void Configure()
 {
  Directory.CreateDirectory(F);AssetDatabase.Refresh();
  var pierce=Asset<PiercingLine>("Ballista_PiercingShot");var range=Asset<BonusDamageVsUnitType>("Ballista_StructureDemolition");
  var pierceV=Asset<CompositeSkill>("Ballista_PiercingShot_Visual");var rangeV=Asset<CompositeSkill>("Ballista_StructureDemolition_Visual");
  pierceV.targetMode=SkillTargetMode.SmartUnit;pierceV.impactVFX=Vfx("PiercingHit","Assets/Synty/PolygonDungeonRealms/Prefabs/FX/FX_Sparks_01.prefab",.35f);pierceV.impactVfxLifetime=.7f;pierceV.procAnimationState="";
  rangeV.targetMode=SkillTargetMode.SmartUnit;rangeV.impactVFX=Vfx("DemolitionHit","Assets/Lana Studio/Casual RPG VFX/Prefabs/Range_attack/Hit_wind.prefab",.4f);rangeV.impactVfxLifetime=.9f;rangeV.procAnimationState="";
  pierce.abilityName=new[]{"Сквозной выстрел"};pierce.description=new[]{"Болт поражает всех врагов на линии до основной цели. Ширина коридора 1.5 м, полный урон атаки."};pierce.maxLevels=1;pierce.corridorWidth=1.5f;pierce.extraDistanceBeyondTarget=0;pierce.damagePercent=new[]{1f};pierce.maxExtraTargets=0;pierce.onlyDirectAttack=true;pierce.applyAttackEffectors=false;pierce.unitSelector=new UnitSelector(false,false,true,true,true,false,false,true,true,true,false,false);pierce.hitPresentation=pierceV;
  range.abilityName=new[]{"Снос башен"};range.description=new[]{"Атаки наносят зданиям на 50% больше урона."};range.maxLevels=1;range.targetUnitType=UnitType.Building;range.damageMultiplier=new[]{1.5f};range.onlyDirectAttack=true;range.hitPresentation=rangeV;
  pierce.requiredTech=new[]{new MultiLevel<Technology>{data=new[]{AssetDatabase.LoadAssetAtPath<Technology>("Assets/Resources/Technology/Lyudi_T4_A1.asset")}}};range.requiredTech=new[]{new MultiLevel<Technology>{data=new[]{AssetDatabase.LoadAssetAtPath<Technology>("Assets/Resources/Technology/Lyudi_T4_A2.asset")}}};
  pierce.icon=new[]{AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Icons/icons/humans_tech_heavy_bolts.png")};range.icon=new[]{AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Icons/icons/humans_tech_heavy_bolts.png")};
  foreach(var a in new Ability[]{pierce,range,pierceV,rangeV})EditorUtility.SetDirty(a);
  const string prefab="Assets/Resources/UnitPrefabs/Units/Humans/Ballista.prefab";var go=PrefabUtility.LoadPrefabContents(prefab);try{var u=go.GetComponent<Unit>();u.abilities=(u.abilities??Array.Empty<Ability>()).Where(a=>a&&a!=pierce&&a!=range&&AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(a))!="745d7ba2997971a49a5ae87f4199a234").Concat(new Ability[]{pierce,range}).ToArray();u.abilityLevel=new int[u.abilities.Length];var auto=go.GetComponent<AutoAbilityUser>();if(auto){var so=new SerializedObject(auto);var list=so.FindProperty("autoAbilities");for(int i=list.arraySize-1;i>=0;i--){var a=list.GetArrayElementAtIndex(i).objectReferenceValue as Ability;if(!a||!u.abilities.Contains(a)){list.GetArrayElementAtIndex(i).objectReferenceValue=null;list.DeleteArrayElementAtIndex(i);}}so.ApplyModifiedPropertiesWithoutUndo();}PrefabUtility.SaveAsPrefabAsset(go,prefab);}finally{PrefabUtility.UnloadPrefabContents(go);}
  AssetDatabase.SaveAssets();InterflowAbilityUsage.InvalidateCache();InterflowAbilityGroups.InvalidateCache();
 }
}
#endif
