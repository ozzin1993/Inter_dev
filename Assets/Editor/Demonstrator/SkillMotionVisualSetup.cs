#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using StrategyCore;

[InitializeOnLoad] public static class SkillMotionVisualSetup
{
    const string W=@"C:\Users\aleks\Documents\Codex\2026-09-11\v\work\skills-28\";
    static SkillMotionVisualSetup(){EditorApplication.update+=Poll;}
    static void Poll(){
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(W+"fix-skill-motion.txt"))return;
        try{File.Delete(W+"fix-skill-motion.txt");}catch(IOException){return;}
        try{Configure();File.WriteAllText(W+"skill-motion-configured.txt","OK");}catch(Exception e){File.WriteAllText(W+"skill-motion-configured.txt",e.ToString());}
    }
    static CompositeSkill Skill(string name)=>Resources.LoadAll<CompositeSkill>("Ability").Single(s=>s.name==name);
    static void Configure(){
        const string projectilePath="Assets/Resources/Projectiles/VanguardMage_IceArrow.prefab";
        var root=PrefabUtility.LoadPrefabContents(projectilePath);
        try{root.GetComponent<Projectile>().renderObject.transform.localRotation=Quaternion.Euler(0,-90,0);PrefabUtility.SaveAsPrefabAsset(root,projectilePath);}finally{PrefabUtility.UnloadPrefabContents(root);}
        foreach(var name in new[]{"Inquisitor_Ascension","Avatar_RighteousDash","Gurtag_FelRam"}){
            var skill=Skill(name);skill.casterMove.travelAnimationState="skillTravel";
            if(skill.knockback.enabled)skill.knockback.travelSeconds=.38f;
            EditorUtility.SetDirty(skill);
        }
        ConfigureUnit("Humans/Inquisitor_Base",true);
        ConfigureUnit("Humans/Inquisitor_Avatar",false);
        ConfigureUnit("Orcs/Gurtag",false);
        AssetDatabase.SaveAssets();InterflowAbilityUsage.InvalidateCache();
    }
    static void ConfigureUnit(string relative,bool blend){
        string path="Assets/Resources/UnitPrefabs/Units/Heroes/"+relative+".prefab";
        var root=PrefabUtility.LoadPrefabContents(path);
        try{
            var unit=root.GetComponent<Unit>();
            if(blend&&!root.GetComponent<UnitMorphPoseBlend>())root.AddComponent<UnitMorphPoseBlend>();
            var animator=root.GetComponentInChildren<Animator>(true);
            var controller=(AnimatorController)animator.runtimeAnimatorController;
            var sm=controller.layers[0].stateMachine;
            var walk=sm.states.Select(s=>s.state).First(s=>s.name=="walk"||s.name=="walk0");
            var travel=sm.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="skillTravel")??sm.AddState("skillTravel");
            travel.motion=walk.motion;travel.speed=1.5f;travel.speedParameterActive=false;
            // Existing cast was a complete weapon swing compressed into a short spell wind-up.
            // A shield-ready gesture keeps Gurtag's axe and shield in their intended grips.
            if(relative=="Orcs/Gurtag"){
                var source=AssetDatabase.LoadAllAssetsAtPath("Assets/DoubleL/FBX_Animations/One Hand Base/Shield/Idle/InPlace/1Hand_Base_Shield_Block_Start_1_InPlace.fbx").OfType<AnimationClip>().FirstOrDefault(c=>!c.name.StartsWith("__preview__"));
                if(source&&source.humanMotion){
                    const string cp="Assets/Models/Units/Heroes/Orcs/Gurtag/Animations/Gurtag_SkillReady.anim";
                    var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(cp);
                    if(!clip){clip=UnityEngine.Object.Instantiate(source);AssetDatabase.CreateAsset(clip,cp);}
                    AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>());
                    var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=false;settings.loopBlendPositionXZ=true;settings.keepOriginalPositionXZ=true;AnimationUtility.SetAnimationClipSettings(clip,settings);
                    var cast=sm.states.Select(s=>s.state).First(s=>s.name=="cast");cast.motion=clip;cast.speed=1;
                    foreach(var transition in cast.transitions)transition.duration=.16f;
                    EditorUtility.SetDirty(clip);
                }else throw new Exception("Expected humanoid shield preparation clip was not found.");
            }
            EditorUtility.SetDirty(controller);PrefabUtility.SaveAsPrefabAsset(root,path);
        }finally{PrefabUtility.UnloadPrefabContents(root);}
    }
}
#endif
