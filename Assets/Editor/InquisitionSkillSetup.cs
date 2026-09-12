#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using UnityEditor;using UnityEngine;using StrategyCore;
[InitializeOnLoad]public static class InquisitionSkillSetup
{
 const string W=@"C:\Users\aleks\Documents\Codex\2026-09-11\v\work\skills-28\";
 const string F="Assets/Resources/Ability/Roster/Humans/Inquisition/";
 static InquisitionSkillSetup(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(W+"configure-inquisition.txt"))return;try{File.Delete(W+"configure-inquisition.txt");}catch(IOException){return;}try{Configure();File.WriteAllText(W+"inquisition-configured.txt","OK");}catch(Exception e){File.WriteAllText(W+"inquisition-configured.txt",e.ToString());Debug.LogException(e);}}
 static T Asset<T>(string name)where T:Ability{var a=AssetDatabase.LoadAssetAtPath<T>(F+name+".asset");if(a)return a;a=ScriptableObject.CreateInstance<T>();a.id=InterflowEditorUI.NextFreeId("t:Ability",o=>((Ability)o).id);a.abilityName=new[]{name};AssetDatabase.CreateAsset(a,F+name+".asset");return a;}
 static VFXReferencer Vfx(string name,string sourcePath,float scale)
 {
  string path="Assets/VFX/Skills/Inquisition/"+name+".prefab";Directory.CreateDirectory("Assets/VFX/Skills/Inquisition");var old=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(old)return old.GetComponent<VFXReferencer>();
  var source=AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);if(!source)throw new Exception("Missing VFX: "+sourcePath);
  var root=new GameObject(name);try{root.AddComponent<VFXReferencer>();var child=(GameObject)PrefabUtility.InstantiatePrefab(source);child.transform.SetParent(root.transform,false);child.transform.localScale=Vector3.one*scale;foreach(var ps in child.GetComponentsInChildren<ParticleSystem>(true)){var m=ps.main;m.loop=false;m.playOnAwake=true;}return PrefabUtility.SaveAsPrefabAsset(root,path).GetComponent<VFXReferencer>();}finally{UnityEngine.Object.DestroyImmediate(root);}
 }
 static void Configure()
 {
  Directory.CreateDirectory(F);AssetDatabase.Refresh();
  const string magicPath="Assets/Resources/DamageTypes/Magic.asset";var magic=AssetDatabase.LoadAssetAtPath<DamageType>(magicPath);if(!magic){magic=ScriptableObject.CreateInstance<DamageType>();magic.id=InterflowEditorUI.NextFreeId("t:DamageType",o=>((DamageType)o).id);magic.displayName="Магический";magic.damageEffectivness=Array.Empty<DamageToArmor>();AssetDatabase.CreateAsset(magic,magicPath);}magic.ignoresArmor=true;EditorUtility.SetDirty(magic);
  var burst=Asset<CompositePassive>("Inquisition_DyingFlame");var blessing=Asset<CompositePassive>("Inquisition_FallenBlessing");
  var burstVisual=Asset<CompositeSkill>("Inquisition_DyingFlame_Visual");var healVisual=Asset<CompositeSkill>("Inquisition_FallenBlessing_Visual");
  burstVisual.impactVFX=Vfx("DyingFlame","Assets/Lana Studio/Casual RPG VFX/Prefabs/Fire/Fire_explosion_earth.prefab",1.5f);burstVisual.targetMode=SkillTargetMode.SmartPoint;burstVisual.radius=new[]{3f};burstVisual.duration=new[]{.6f};burstVisual.impactVfxLifetime=3;burstVisual.procAnimationState="";
  healVisual.impactVFX=Vfx("FallenBlessing","Assets/Lana Studio/Casual RPG VFX/Prefabs/Regeneration/Regeneration_health.prefab",1);healVisual.targetMode=SkillTargetMode.Self;healVisual.impactVfxLifetime=3;healVisual.procAnimationState="";
  burst.abilityName=new[]{"Взрыв при угасании"};burst.description=new[]{"При смерти наносит 60 магического урона врагам в радиусе 3."};burst.maxLevels=1;burst.deathEffect=new PassiveDeathBlock{enabled=true,radius=3,enemyDamage=60,damageType=magic,deathPresentation=burstVisual};
  blessing.abilityName=new[]{"Благословение Падшего"};blessing.description=new[]{"При смерти исцеляет ближайшего союзника на 25% его максимального здоровья."};blessing.maxLevels=1;blessing.deathEffect=new PassiveDeathBlock{enabled=true,allyHealMaxHpFraction=.25f,nearestAllyOnly=true,healPresentation=healVisual};
  burst.requiredTech=new[]{new MultiLevel<Technology>{data=new[]{AssetDatabase.LoadAssetAtPath<Technology>("Assets/Resources/Technology/Lyudi_T2_B2.asset")}}};
  blessing.requiredTech=new[]{new MultiLevel<Technology>{data=new[]{AssetDatabase.LoadAssetAtPath<Technology>("Assets/Resources/Technology/Lyudi_T2_B1.asset")}}};
  foreach(var a in new Ability[]{burst,blessing,burstVisual,healVisual})EditorUtility.SetDirty(a);
  const string prefab="Assets/Resources/UnitPrefabs/Units/Humans/Swordsman_Inquisition.prefab";var go=PrefabUtility.LoadPrefabContents(prefab);try{var u=go.GetComponent<Unit>();u.abilities=(u.abilities??Array.Empty<Ability>()).Where(a=>a&&a!=burst&&a!=blessing&&AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(a))!="745d7ba2997971a49a5ae87f4199a234").Concat(new Ability[]{burst,blessing}).ToArray();u.abilityLevel=new int[u.abilities.Length];var auto=go.GetComponent<AutoAbilityUser>();if(auto){var so=new SerializedObject(auto);var list=so.FindProperty("autoAbilities");for(int i=list.arraySize-1;i>=0;i--){var a=list.GetArrayElementAtIndex(i).objectReferenceValue as Ability;if(!a||!u.abilities.Contains(a)){list.GetArrayElementAtIndex(i).objectReferenceValue=null;list.DeleteArrayElementAtIndex(i);}}so.ApplyModifiedPropertiesWithoutUndo();}PrefabUtility.SaveAsPrefabAsset(go,prefab);}finally{PrefabUtility.UnloadPrefabContents(go);}
  AssetDatabase.SaveAssets();var watch=System.Diagnostics.Stopwatch.StartNew();var indexed=InterflowUnitPrefabIndex.LoadUnits().Select(AssetDatabase.GetAssetPath).ToArray();File.WriteAllLines(W+"unit-index-results.txt",new[]{"ElapsedMs="+watch.ElapsedMilliseconds,"Count="+indexed.Length}.Concat(indexed));InterflowAbilityUsage.InvalidateCache();InterflowAbilityGroups.InvalidateCache();
 }
}
#endif
