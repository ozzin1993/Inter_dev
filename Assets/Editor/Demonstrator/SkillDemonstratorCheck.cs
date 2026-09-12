#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using StrategyCore;

[InitializeOnLoad] public static class SkillDemonstratorCheck
{
    const string W=@"C:\Users\aleks\Documents\Codex\2026-09-11\v\work\skills-28\";
    static readonly List<string> results=new List<string>(), errors=new List<string>();
    static int step=-1;static double next;
    static SkillDemonstratorCheck(){EditorApplication.update+=Tick;Application.logMessageReceived+=Log;}
    static void Log(string text,string stack,LogType type){if(SessionState.GetBool("DemoChecking",false)&&(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)){if(errors.Count<30&&!errors.Any(e=>e.StartsWith(text)))errors.Add(text+"\n"+stack);}}
    static void Tick()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(File.Exists(W+"inspect-skill-demo.txt")){
            File.Delete(W+"inspect-skill-demo.txt");
            var d=UnityEngine.Object.FindFirstObjectByType<SkillDemonstrator>();
            if(d){var lines=new List<string>{"Stage "+d.stageCenter,"Caster "+(d.Caster?d.Caster.transform.position.ToString():"none")};
                foreach(var r in d.GetComponentsInChildren<Renderer>(true))lines.Add(r.name+" pos="+r.transform.position+" active="+r.gameObject.activeInHierarchy+" enabled="+r.enabled+" layer="+r.gameObject.layer+" material="+(r.sharedMaterial?r.sharedMaterial.shader.name:"null"));
                foreach(var c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))lines.Add("Camera "+c.name+" mask="+c.cullingMask+" pos="+c.transform.position+" near="+c.nearClipPlane+" far="+c.farClipPlane);
                File.WriteAllLines(W+"skill-demo-inspection.txt",lines);
            }
        }
        if(!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists(W+"run-skill-demo.txt")){
            File.Delete(W+"run-skill-demo.txt");if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=SkillDemonstratorSetup.ScenePath)return;
            step=-1;results.Clear();errors.Clear();SessionState.SetBool("DemoChecking",true);EditorApplication.isPaused=false;EditorApplication.isPlaying=true;return;
        }
        if(!SessionState.GetBool("DemoChecking",false)||!EditorApplication.isPlaying||EditorApplication.isPaused)return;
        try{
            var demo=UnityEngine.Object.FindFirstObjectByType<SkillDemonstrator>();if(!demo||!demo.Ready||!demo.Caster)return;
            if(EditorApplication.timeSinceStartup<next)return;
            if(step==-1){results.Clear();errors.Clear();Check(demo.roster.Length==28,"Roster has 28 prefabs");step=0;demo.SelectDemo(0);next=EditorApplication.timeSinceStartup+2;return;}
            if(step<28){Check(demo.Caster&&demo.Caster.unitTypeID==demo.roster[step].prefab.unitTypeID,"Spawn/selection "+demo.roster[step].prefab.name);Check(demo.Targets.Count>0&&demo.Targets.All(t=>t&&t.owner!=demo.Caster.owner),"Enemy setup "+demo.roster[step].prefab.name);step++;demo.SelectDemo(step<28?step:4);next=EditorApplication.timeSinceStartup+2;return;}
            if(step==28){var skill=demo.Caster.abilities.OfType<CompositeSkill>().FirstOrDefault(a=>!demo.Caster.abilityLocked[Array.IndexOf(demo.Caster.abilities,a)]);if(skill){demo.Caster.ChangeAbilityCooldown(-1,Array.IndexOf(demo.Caster.abilities,skill),false);Check(demo.Cast(skill),"Manual ability accepted through Unit.UseAbilityItem");}else Check(false,"No unlocked demonstration skill");step=29;next=EditorApplication.timeSinceStartup+2;return;}
            if(step==29){Check(demo.Targets.Any(t=>t&&t.health<t.maxHealth),"Live skill changes target health");ScreenCapture.CaptureScreenshot(W+"skill-demo-overview.png");Check(errors.Count==0,"No runtime exceptions during all selections");SessionState.SetBool("DemoChecking",false);File.WriteAllLines(W+"skill-demo-results.txt",results);File.WriteAllLines(W+"skill-demo-errors.txt",errors);File.WriteAllText(W+"skill-demo-finished.txt",results.Count+" checks, "+errors.Count+" errors");}
        }catch(Exception e){errors.Add(e.ToString());File.WriteAllLines(W+"skill-demo-errors.txt",errors);SessionState.SetBool("DemoChecking",false);}
    }
    static void Check(bool ok,string text){results.Add((ok?"PASS ":"FAIL ")+text);File.WriteAllLines(W+"skill-demo-results.txt",results);}
}
#endif
