#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using StrategyCore;

[InitializeOnLoad] public static class SkillMotionRuntimeCheck
{
    const string W=@"C:\Users\aleks\Documents\Codex\2026-09-11\v\work\skills-28\";
    static readonly List<string> results=new List<string>(),errors=new List<string>();
    static int phase, travelFrames, pushFrames, arrowFrames;
    static float next;static Vector3 last;static bool sampled;static float maxStep;
    static SkillMotionRuntimeCheck(){EditorApplication.update+=Tick;Application.logMessageReceived+=Log;}
    static void Log(string s,string stack,LogType t){if(SessionState.GetBool("MotionCheck",false)&&(t==LogType.Error||t==LogType.Exception)&&errors.Count<25)errors.Add(s+"\n"+stack);}
    static void Set(SkillDemonstrator demo,string name,object value)=>typeof(SkillDemonstrator).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(demo,value);
    static void Manual(SkillDemonstrator demo){Set(demo,"autoShow",false);Set(demo,"attacks",false);Set(demo,"retaliation",false);}
    static bool Cast(SkillDemonstrator d,string name){var a=d.Caster.abilities.OfType<CompositeSkill>().Single(s=>s.name==name);int i=Array.IndexOf(d.Caster.abilities,a);d.Caster.ChangeAbilityCooldown(-1,i,false);return d.Cast(a);}
    static void Check(bool ok,string message){results.Add((ok?"PASS ":"FAIL ")+message);File.WriteAllLines(W+"skill-motion-results.txt",results);}
    static void Delay(int step,float seconds){phase=step;next=Time.unscaledTime+seconds;}
    static void ResetSamples(){travelFrames=pushFrames=arrowFrames=0;sampled=false;maxStep=0;}
    static void Tick(){
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists(W+"run-skill-motion.txt")){
            try{File.Delete(W+"run-skill-motion.txt");}catch(IOException){return;}
            if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=SkillDemonstratorSetup.ScenePath)return;
            SessionState.SetBool("MotionCheck",true);phase=0;results.Clear();errors.Clear();EditorApplication.isPlaying=true;return;
        }
        if(!SessionState.GetBool("MotionCheck",false)||!EditorApplication.isPlaying||EditorApplication.isPaused)return;
        try{
            var d=UnityEngine.Object.FindFirstObjectByType<SkillDemonstrator>();if(!d||!d.Ready||(!d.Caster&&phase!=12))return;
            var travel=d.Caster?d.Caster.GetComponent<SkillTravel>():null;
            if(travel&&travel.Running){travelFrames++;if(sampled)maxStep=Mathf.Max(maxStep,Vector3.Distance(last,d.Caster.transform.position));last=d.Caster.transform.position;sampled=true;}
            if(d.Targets.Any(t=>t&&t.GetComponent<SkillDisplacement>()&&t.GetComponent<SkillDisplacement>().Running))pushFrames++;
            if(travelFrames==8||travelFrames==18)ScreenCapture.CaptureScreenshot(W+"motion-phase-"+phase+"-travel-"+travelFrames+".png");
            if(pushFrames==6)ScreenCapture.CaptureScreenshot(W+"motion-phase-"+phase+"-push.png");
            if(phase==3){var p=UnityEngine.Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None).FirstOrDefault(x=>x.name.StartsWith("VanguardMage_IceArrow"));if(p){arrowFrames++;if(arrowFrames==2){Check(Vector3.Dot(p.renderObject.transform.right,p.transform.forward)>.99f,"Ice arrow visual axis follows projectile direction");ScreenCapture.CaptureScreenshot(W+"motion-ice-arrow.png");}}}
            if(Time.unscaledTime<next)return;
            switch(phase){
                case 0: Manual(d);Time.timeScale=.35f;d.SelectDemo(4);Delay(1,1);break;
                case 1: Manual(d);Set(d,"specialization",1);d.ResetDemo();Delay(2,1);break;
                case 2: ResetSamples();Check(Cast(d,"VanguardMage_IceArrow"),"Ice arrow cast accepted");Delay(3,3);break;
                case 3: Check(arrowFrames>0,"Ice arrow observed during flight");d.SelectDemo(24);Delay(4,1);break;
                case 4: Manual(d);d.Caster.SetMP(d.Caster.maxMana);ResetSamples();Check(Cast(d,"Inquisitor_Ascension"),"Ascension cast accepted");Delay(5,5);break;
                case 5:
                    Check(travelFrames>4&&maxStep<1,"Ascension has intermediate motion frames, max step "+maxStep);
                    Check(pushFrames>2,"Ascension knockback has intermediate frames");
                    Check(d.Caster.polymorphed&&d.Caster.melee,"Ascension finishes in melee Avatar form");
                    Check(!SkillActionLock.Active(d.Caster),"Ascension releases action lock");
                    Check(d.Caster.GetComponent<UnitMorphPoseBlend>(),"Morph pose blending component present");
                    ScreenCapture.CaptureScreenshot(W+"motion-avatar-arrival.png");
                    Delay(50,.3f);break;
                case 50: d.SelectDemo(25);Delay(6,1);break;
                case 6: Manual(d);ResetSamples();Check(Cast(d,"Avatar_RighteousDash"),"Avatar dash accepted");Delay(7,4);break;
                case 7:
                    Check(travelFrames>4&&maxStep<1,"Avatar dash has intermediate motion frames, max step "+maxStep);
                    Check(!SkillActionLock.Active(d.Caster),"Avatar dash releases action lock");
                    d.SelectDemo(27);Delay(8,1);break;
                case 8: Manual(d);ResetSamples();Check(Cast(d,"Gurtag_FelRam"),"Gurtag ram accepted");Delay(9,4);break;
                case 9:
                    Check(travelFrames>4&&maxStep<1,"Gurtag ram has intermediate motion frames, max step "+maxStep);
                    Check(pushFrames>2,"Gurtag targets move over multiple frames");
                    Check(d.Targets.All(t=>!t||!SkillActionLock.Active(t)),"Targets released after knockback");
                    Check(!SkillActionLock.Active(d.Caster),"Gurtag released after ram");
                    ScreenCapture.CaptureScreenshot(W+"motion-gurtag-arrival.png");
                    Delay(90,.3f);break;
                case 90: d.ResetDemo();Delay(10,1);break;
                case 10: Check(Cast(d,"Gurtag_FelRam"),"Gurtag ram reusable");Delay(11,1.25f);break;
                case 11: d.Caster.Die(-1,null,false);Delay(12,1);break;
                case 12:
                    Check(!d.Caster||!d.Caster.GetComponent<SkillTravel>().Running,"Caster death stops travel");
                    Check(!d.Caster||!SkillActionLock.Active(d.Caster),"Caster death releases travel lock");
                    Check(errors.Count==0,"No exceptions during motion checks");
                    File.WriteAllLines(W+"skill-motion-results.txt",results);File.WriteAllLines(W+"skill-motion-errors.txt",errors);
                    File.WriteAllText(W+"skill-motion-finished.txt",results.Count+" checks; "+results.Count(s=>s.StartsWith("FAIL"))+" failures; "+errors.Count+" errors");
                    SessionState.SetBool("MotionCheck",false);Time.timeScale=1;d.ResetDemo();break;
            }
        }catch(Exception e){errors.Add(e.ToString());File.WriteAllLines(W+"skill-motion-errors.txt",errors);SessionState.SetBool("MotionCheck",false);Time.timeScale=1;}
    }
}
#endif
