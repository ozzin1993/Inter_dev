#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using UnityEditor;using UnityEngine;using StrategyCore;
[InitializeOnLoad]public static class InquisitionArcherSkillSetup
{
 const string W=@"C:\Users\aleks\Documents\Codex\2026-09-11\v\work\skills-28\";
 const string F="Assets/Resources/Ability/Roster/Humans/InquisitionArcher/";
 static InquisitionArcherSkillSetup(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(W+"configure-inquisitionarcher.txt"))return;try{File.Delete(W+"configure-inquisitionarcher.txt");}catch(IOException){return;}try{Configure();File.WriteAllText(W+"inquisitionarcher-configured.txt","OK");}catch(Exception e){File.WriteAllText(W+"inquisitionarcher-configured.txt",e.ToString());Debug.LogException(e);}}
 static T Asset<T>(string name)where T:Ability{var a=AssetDatabase.LoadAssetAtPath<T>(F+name+".asset");if(a)return a;a=ScriptableObject.CreateInstance<T>();a.id=InterflowEditorUI.NextFreeId("t:Ability",o=>((Ability)o).id);a.abilityName=new[]{name};AssetDatabase.CreateAsset(a,F+name+".asset");return a;}
 static VFXReferencer Vfx(string name,string sourcePath,float scale)
 {
  string path="Assets/VFX/Skills/InquisitionArcher/"+name+".prefab";Directory.CreateDirectory("Assets/VFX/Skills/InquisitionArcher");var old=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(old){if(name=="PiercingHit"){var edit=PrefabUtility.LoadPrefabContents(path);try{TunePierce(edit);PrefabUtility.SaveAsPrefabAsset(edit,path);}finally{PrefabUtility.UnloadPrefabContents(edit);}}return old.GetComponent<VFXReferencer>();}
  var source=AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);if(!source)throw new Exception("Missing VFX: "+sourcePath);
  var root=new GameObject(name);try{root.AddComponent<VFXReferencer>();var child=(GameObject)PrefabUtility.InstantiatePrefab(source);child.transform.SetParent(root.transform,false);child.transform.localScale=Vector3.one*scale;foreach(var ps in child.GetComponentsInChildren<ParticleSystem>(true)){var m=ps.main;m.loop=true;m.playOnAwake=true;}if(name=="PiercingHit")TunePierce(root);return PrefabUtility.SaveAsPrefabAsset(root,path).GetComponent<VFXReferencer>();}finally{UnityEngine.Object.DestroyImmediate(root);}
 }
 static void TunePierce(GameObject root){foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true)){ps.transform.localScale=Vector3.one;var m=ps.main;m.loop=true;m.startLifetime=new ParticleSystem.MinMaxCurve(.25f,.4f);m.startSpeed=new ParticleSystem.MinMaxCurve(2f,3f);m.startSize=new ParticleSystem.MinMaxCurve(.08f,.14f);m.startColor=new Color(.55f,.85f,1f);var e=ps.emission;e.rateOverTime=0;e.SetBursts(new[]{new ParticleSystem.Burst(0,10)});var sh=ps.shape;sh.shapeType=ParticleSystemShapeType.Sphere;sh.radius=.08f;}}
 static Effector Effect(string name){string path="Assets/Resources/Effectors/Roster/Humans/InquisitionArcher/"+name+".asset";InterflowEditorUI.EnsureFolder(Path.GetDirectoryName(path));var e=AssetDatabase.LoadAssetAtPath<Effector>(path);if(e)return e;e=ScriptableObject.CreateInstance<Effector>();e.id=InterflowEditorUI.NextFreeId("t:Effector",o=>((Effector)o).id);AssetDatabase.CreateAsset(e,path);return e;}
 static void Configure()
 {
  Directory.CreateDirectory(F);AssetDatabase.Refresh();
  var burn=Asset<CompositePassive>("InquisitionArcher_CleansingFire");var curse=Asset<CompositePassive>("InquisitionArcher_CausticJudgment");
  var fire=Effect("InquisitionArcher_Burning");var vulnerability=Effect("InquisitionArcher_MagicVulnerability");
  var magic=AssetDatabase.LoadAssetAtPath<DamageType>("Assets/Resources/DamageTypes/Magic.asset");
  fire.displayName="Очищающий огонь";fire.description="Горение: 10 магического урона в секунду.";fire.duration=3;fire.damageAmount=10;fire.damageType=magic;fire.stacks=false;
  fire.VFX=Vfx("CleansingBurn","Assets/Lana Studio/Casual RPG VFX/Prefabs/Fire/Fire_cartoon_gold.prefab",.55f);
  vulnerability.displayName="Едкая Кара";vulnerability.description="Получает на 15% больше магического урона.";vulnerability.duration=5;vulnerability.stacks=false;vulnerability.incomingDamageMultiplier=1.15f;vulnerability.incomingDamageType=magic;
  vulnerability.VFX=Vfx("CausticJudgment","Assets/Lana Studio/Casual RPG VFX/Prefabs/Fire/Fire_cartoon_poison.prefab",.35f);
  burn.abilityName=new[]{"Очищающий огонь"};burn.description=new[]{"Атаки поджигают цель на 3 секунды. Повторное попадание продлевает горение."};burn.maxLevels=1;burn.attackEffectors=new PassiveAttackEffectorsBlock{enabled=true,effectors=new[]{fire}};
  curse.abilityName=new[]{"Едкая Кара"};curse.description=new[]{"Попадания ослабляют защиту цели от магии на 15% на 5 секунд. Повторное попадание продлевает эффект."};curse.maxLevels=1;curse.attackEffectors=new PassiveAttackEffectorsBlock{enabled=true,effectors=new[]{vulnerability}};
  burn.requiredTech=new[]{new MultiLevel<Technology>{data=new[]{AssetDatabase.LoadAssetAtPath<Technology>("Assets/Resources/Technology/Lyudi_T1_B1.asset")}}};
  curse.requiredTech=new[]{new MultiLevel<Technology>{data=new[]{AssetDatabase.LoadAssetAtPath<Technology>("Assets/Resources/Technology/Lyudi_T1_B2.asset")}}};
  foreach(var a in new UnityEngine.Object[]{burn,curse,fire,vulnerability})EditorUtility.SetDirty(a);
  const string prefab="Assets/Resources/UnitPrefabs/Units/Humans/InquisitionArcher.prefab";var go=PrefabUtility.LoadPrefabContents(prefab);try{var u=go.GetComponent<Unit>();u.abilities=(u.abilities??Array.Empty<Ability>()).Where(a=>a&&a!=burn&&a!=curse&&AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(a))!="745d7ba2997971a49a5ae87f4199a234").Concat(new Ability[]{burn,curse}).ToArray();u.abilityLevel=new int[u.abilities.Length];var auto=go.GetComponent<AutoAbilityUser>();if(auto){var so=new SerializedObject(auto);var list=so.FindProperty("autoAbilities");for(int i=list.arraySize-1;i>=0;i--){var a=list.GetArrayElementAtIndex(i).objectReferenceValue as Ability;if(!a||!u.abilities.Contains(a)){list.GetArrayElementAtIndex(i).objectReferenceValue=null;list.DeleteArrayElementAtIndex(i);}}so.ApplyModifiedPropertiesWithoutUndo();}PrefabUtility.SaveAsPrefabAsset(go,prefab);}finally{PrefabUtility.UnloadPrefabContents(go);}
  AssetDatabase.SaveAssets();InterflowAbilityUsage.InvalidateCache();InterflowAbilityGroups.InvalidateCache();
 }
}
#endif
