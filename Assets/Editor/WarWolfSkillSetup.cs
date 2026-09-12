#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using UnityEditor;using UnityEditor.Animations;using UnityEngine;using StrategyCore;
[InitializeOnLoad] public static class WarWolfSkillSetup {
 const string W=@"C:\Users\aleks\Documents\Codex\2026-09-11\v\work\skills-28\";
 const string F="Assets/Resources/Ability/Roster/Orcs/WarWolf/";
 const string V="Assets/VFX/Skills/WarWolf/";
 const string A="Assets/Models/Units/Orcs/WarWolf/Animations/";
 static WarWolfSkillSetup(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(W+"configure-warwolf.txt"))return;try{File.Delete(W+"configure-warwolf.txt");}catch(IOException){return;}try{Configure();File.WriteAllText(W+"warwolf-configured.txt","OK");}catch(Exception e){File.WriteAllText(W+"warwolf-configured.txt",e.ToString());Debug.LogException(e);}}
 static T Asset<T>(string name)where T:Ability{var a=AssetDatabase.LoadAssetAtPath<T>(F+name+".asset");if(a)return a;a=ScriptableObject.CreateInstance<T>();a.id=InterflowEditorUI.NextFreeId("t:Ability",o=>((Ability)o).id);a.abilityName=new[]{name};AssetDatabase.CreateAsset(a,F+name+".asset");return a;}
 static VFXReferencer Vfx(string name,string source,float scale,bool loop=false){var old=AssetDatabase.LoadAssetAtPath<GameObject>(V+name+".prefab");if(old)return old.GetComponent<VFXReferencer>();var root=new GameObject(name);try{root.AddComponent<VFXReferencer>();var visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Lana Studio/Casual RPG VFX/Prefabs/"+source+".prefab"));visual.transform.SetParent(root.transform,false);visual.transform.localScale=Vector3.one*scale;foreach(var ps in visual.GetComponentsInChildren<ParticleSystem>(true)){var m=ps.main;m.loop=loop;m.playOnAwake=true;}return PrefabUtility.SaveAsPrefabAsset(root,V+name+".prefab").GetComponent<VFXReferencer>();}finally{UnityEngine.Object.DestroyImmediate(root);}}



 static Technology Tech(string name,string label){var path="Assets/Resources/Technology/"+name+".asset";var t=AssetDatabase.LoadAssetAtPath<Technology>(path);if(!t){t=ScriptableObject.CreateInstance<Technology>();t.id=InterflowEditorUI.NextFreeId("t:Technology",o=>((Technology)o).id);t.displayName=label;AssetDatabase.CreateAsset(t,path);}return t;}
 static void Configure(){InterflowEditorUI.EnsureFolder(F);InterflowEditorUI.EnsureFolder(V);
 var cleave=Asset<CompositePassive>("WarWolf_Cleave");var grip=Asset<EveryNthAttack>("WarWolf_Grip");var hit=Asset<CompositeSkill>("WarWolf_CleaveVisual");var stun=Asset<CompositeSkill>("WarWolf_GripHit");
 hit.targetMode=SkillTargetMode.SmartUnit;hit.procAnimationState="";hit.spawnSocket=SkillSocketType.None;hit.impactVFX=Vfx("WolfCleaveImpact","Slash/Hit_stone",.32f);hit.impactVfxLifetime=.45f;
 stun.targetMode=SkillTargetMode.SmartUnit;stun.procAnimationState="";stun.spawnSocket=SkillSocketType.None;stun.unitSelector=new UnitSelector{isEnemy=true,isOwn=true,isAlly=true,isUnit=true,isGround=true,isWater=true,isAir=true};stun.castTime=new[]{0f};stun.cooldown=new[]{0f};stun.manaCost=new[]{0f};stun.castRange=new[]{100f};stun.status=new SkillStatusBlock{enabled=true,stunSeconds=new[]{.8f}};stun.impactVFX=Vfx("WolfGripImpact","States/Stun",.5f);stun.impactVfxLifetime=.8f;
 cleave.abilityName=new[]{"Клив"};cleave.description=new[]{"Каждый удар наносит 40% урона соседним противникам. Кровавый Широкий Замах увеличивает урон до 60% и радиус. Радиус для альфы: 1.5 / 2 м."};cleave.cleave=new PassiveCleaveBlock{enabled=true,radius=1.5f,damageFraction=.4f,upgradeTechnology=Tech("Orki_T5_A1","Кровавый Широкий Замах"),upgradedRadius=2f,upgradedDamageFraction=.6f,hitPresentation=hit};
 grip.abilityName=new[]{"Хватка Волка"};grip.description=new[]{"Каждая атака с вероятностью 15% оглушает основную цель на 0.8 секунды."};grip.everyN=1;grip.procChance=.15f;grip.onlyDirectAttack=true;grip.onProcSkill=stun;grip.requiredTech=new[]{new MultiLevel<Technology>{data=new[]{Tech("Orki_T5_A2","Хватка Волка")}}};
 foreach(var v in new[]{hit.impactVFX,stun.impactVFX}){var path=AssetDatabase.GetAssetPath(v);var fx=PrefabUtility.LoadPrefabContents(path);try{fx.transform.GetChild(0).localPosition=Vector3.up*(v==hit.impactVFX?.65f:1.6f);PrefabUtility.SaveAsPrefabAsset(fx,path);}finally{PrefabUtility.UnloadPrefabContents(fx);}}
 foreach(var a in new Ability[]{cleave,grip,hit,stun}){a.maxLevels=1;a.icon=new[]{AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Icons/icons/ui_stat_damage.png")};EditorUtility.SetDirty(a);}
 const string prefab="Assets/Resources/UnitPrefabs/Units/Orcs/WarWolf.prefab";var root=PrefabUtility.LoadPrefabContents(prefab);try{var u=root.GetComponent<Unit>();u.abilities=(u.abilities??Array.Empty<Ability>()).Where(a=>a&&a!=cleave&&a!=grip).Concat(new Ability[]{cleave,grip}).ToArray();u.abilityLevel=new int[u.abilities.Length];PrefabUtility.SaveAsPrefabAsset(root,prefab);}finally{PrefabUtility.UnloadPrefabContents(root);}AssetDatabase.SaveAssets();InterflowAbilityUsage.InvalidateCache();InterflowAbilityGroups.InvalidateCache();
 }
}
#endif
