using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;

namespace StrategyCore
{
    /// <summary>Local presentation sandbox. All changes affect spawned instances in this scene only.</summary>
    public partial class SkillDemonstrator : MonoBehaviour
    {
        [Serializable] public class Entry { public Unit prefab; public Technology[] specializations; [TextArea] public string notes; }
        public Entry[] roster;
        public Unit humanTarget, orcTarget, allyMage, buildingTarget;
        public Vector3 stageCenter = new Vector3(113,0,33);
        public Camera stageCamera;
        public int selected = 4;
        public bool Ready { get; private set; }
        public Unit Caster { get; private set; }
        public IReadOnlyList<Unit> Targets => enemies;
        readonly List<Unit> actors = new List<Unit>(), allies = new List<Unit>(), enemies = new List<Unit>();
        readonly List<string> messages = new List<string>();
        readonly Dictionary<int,Ability> abilityMap = new Dictionary<int,Ability>();
        readonly HashSet<Technology> unlocked = new HashSet<Technology>();
        int foeOwner, specialization = -1, formation, pendingSelection = -1;
        bool pendingReset, attacks, retaliation, autoShow = true, film, paused, rebuilding;
        float nextAuto, nextOrder, nextUiCleanup, zoom = 7, speed = 1;
        string headline = "Подготовка полигона…";
        Vector2 rosterScroll, skillScroll;
        static readonly FieldInfo WaveUnits = typeof(MatchManager).GetField("teamUnits", BindingFlags.Instance|BindingFlags.NonPublic);

        NavMeshData demoNavData; NavMeshDataInstance demoNavInstance;
        IEnumerator Start()
        {
            SkillPresentationEvents.SkillFired += OnFired;
            foreach(var a in Resources.LoadAll<Ability>("Ability")) abilityMap[a.id]=a;
            while (!SlotManager.instance || !SlotManager.instance.gameOn) yield return null;
            if (NetworkConnectionHandler.isClient) { headline="Полигон запускается локально в режиме хоста."; yield break; }
            foeOwner=Enumerable.Range(1,SlotManager.instance.playerTeam.Length-2).First(i=>SlotManager.instance.playerTeam[i]!=SlotManager.instance.playerTeam[0]);
            if(FogOfWar.instance) FogOfWar.instance.TurnOff=true;
            foreach(var u in FindObjectsByType<Unit>(FindObjectsSortMode.None)) if(u.gameObject.activeInHierarchy) u.Disable();
            foreach(var m in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if(m.GetType().Name=="Camera_TopDown"||m.GetType().Name=="CameraAxisLock"||m.GetType().Name=="MatchManager") m.enabled=false;
            foreach(var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) if(c.renderMode!=RenderMode.WorldSpace)c.enabled=false;
            stageCamera=Camera.main; stageCamera.transform.SetParent(null,true); stageCamera.cullingMask=~LayerMask.GetMask("Icons","Healthbar");
            // A separate, flat navigation surface; never edit the game's baked NavMesh assets.
            var source=new NavMeshBuildSource{shape=NavMeshBuildSourceShape.Box,size=new Vector3(40,.1f,40),transform=Matrix4x4.TRS(stageCenter+Vector3.down*.05f,Quaternion.identity,Vector3.one),area=0};
            demoNavData=NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0),new List<NavMeshBuildSource>{source},new Bounds(stageCenter,new Vector3(42,8,42)),Vector3.zero,Quaternion.identity);
            demoNavInstance=NavMesh.AddNavMeshData(demoNavData);
            Ready=true;
            SelectDemo(selected);
        }

        public void SelectDemo(int index)
        {
            if(!Ready || rebuilding || roster==null || index<0 || index>=roster.Length)return;
            selected=index;
            specialization=roster[index].specializations.Length>0?0:-1;
            formation=roster[index].prefab.name.Contains("Catapult")||roster[index].prefab.name=="Ballista"||roster[index].prefab.name=="FelSiegeBehemoth"?3:0;
            StartCoroutine(Rebuild());
        }
        public void ResetDemo(){if(Ready&&!rebuilding)StartCoroutine(Rebuild());}
        IEnumerator Rebuild()
        {
            rebuilding=true; attacks=false; retaliation=false; nextAuto=Time.time+2;
            foreach(var u in actors)if(u){if(!u.dead)u.Die(-1,null,false,true,true);Destroy(u.gameObject);}
            actors.Clear();allies.Clear();enemies.Clear();Caster=null;RegisterTeams();
            foreach(var p in FindObjectsByType<Projectile>(FindObjectsSortMode.None))Destroy(p.gameObject);
            foreach(var z in FindObjectsByType<GroundDamageZone>(FindObjectsSortMode.None))Destroy(z.gameObject);
            foreach(var v in FindObjectsByType<VFXReferencer>(FindObjectsSortMode.None))if(!v.GetComponentInParent<Unit>())Destroy(v.gameObject);
            foreach(var t in unlocked)TechnologyManager.instance.LockTech(t,0);
            unlocked.Clear();
            yield return null;
            var entry=roster[selected];
            if(specialization>=0){var t=entry.specializations[specialization];TechnologyManager.instance.UnlockTech(t,0);unlocked.Add(t);}
            Caster=Spawn(entry.prefab,stageCenter+Vector3.back*2,0,false);
            bool orc=selected>=11&&selected<=22||entry.prefab.name=="Gurtag"||entry.prefab.name=="Korgal";
            allies.Add(Spawn(orc?orcTarget:humanTarget,stageCenter+new Vector3(-1.6f,0,.2f),0,false));
            allies.Add(Spawn(orc?orcTarget:humanTarget,stageCenter+new Vector3(1.6f,0,0),0,false));
            allies[0].SetHP(allies[0].maxHealth*.2f);
            if(entry.prefab.name=="Gurtag")allies.Add(Spawn(allyMage,stageCenter+new Vector3(0,0,-.5f),0,false));
            if(formation==3)enemies.Add(Spawn(buildingTarget,stageCenter+Vector3.forward*4,foeOwner,true));
            else {
                int count=formation==1?1:formation==2?4:5;
                for(int i=0;i<count;i++){
                    Vector3 delta=formation==2?new Vector3(0,0,.4f+i*1.5f):new Vector3((i%3-1)*1.35f,0,1.2f+(i/3)*1.5f);
                    enemies.Add(Spawn(orc?humanTarget:orcTarget,stageCenter+delta,foeOwner,true));
                }
            }
            Caster.LookAtInstant(new Vector2(stageCenter.x,stageCenter.z+5));
            foreach(var u in allies)u.LookAtInstant(new Vector2(stageCenter.x,stageCenter.z+5));
            foreach(var u in enemies)u.LookAtInstant(new Vector2(Caster.transform.position.x,Caster.transform.position.z));
            RegisterTeams();
            messages.Clear();headline="Выберите способность или включите обычную атаку";
            if(autoShow){attacks=true;retaliation=entry.prefab.name=="Swordsman"||entry.prefab.name=="OrcWarrior";}
            rebuilding=false;
        }
        Unit Spawn(Unit prefab,Vector3 point,int owner,bool target)
        {
            if(NavMesh.SamplePosition(point,out var hit,4,NavMesh.AllAreas))point=hit.position;
            var u=Unit.SpawnInternal(prefab,point,0,owner);
            actors.Add(u);u.doNotLookForTargets=true;u.canAttack=false;u.Hold();u.healthRegen=0;
            if(target){u.maxHealth=Mathf.Max(2000,u.maxHealth*5);u.SetHP(u.maxHealth);u.maxMana=Mathf.Max(200,u.maxMana);u.SetMP(u.maxMana);}
            u.FoWVisible=true;u.ShowRenderers();
            var au=u.GetComponent<AutoAbilityUser>();if(au)au.enabled=false;
            return u;
        }
        void RegisterTeams()
        {
            var mm=MatchManager.instance;if(!mm)return;
            mm.Team(0).ownerPlayer=0;mm.Team(1).ownerPlayer=foeOwner;
            // The wave timer is disabled in this sandbox; keep its existing targeting lists current.
            var lists=(List<Unit>[])WaveUnits.GetValue(mm);
            lists[0].Clear();lists[1].Clear();
            lists[0].AddRange(actors.Where(u=>u&&!u.dead&&u.owner==0));
            lists[1].AddRange(enemies.Where(u=>u&&!u.dead));
        }
        void Update()
        {
            if(!Ready||rebuilding)return;
            if(pendingSelection>=0){int n=pendingSelection;pendingSelection=-1;SelectDemo(n);return;}
            if(pendingReset){pendingReset=false;ResetDemo();return;}
            if(stageCamera){var focus=stageCenter+Vector3.forward*.8f;stageCamera.orthographic=true;stageCamera.orthographicSize=zoom;stageCamera.transform.position=focus+new Vector3(9,14,-12);stageCamera.transform.LookAt(focus);}
            RegisterTeams();
            if(Time.unscaledTime>=nextUiCleanup){
                nextUiCleanup=Time.unscaledTime+.2f;
                foreach(var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))if(c.renderMode!=RenderMode.WorldSpace)c.enabled=false;
                // Keep the native UI objects alive for presentation callbacks, but hide their panels.
                foreach(var d in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))if(d.rootVisualElement!=null)d.rootVisualElement.style.display=DisplayStyle.None;
            }
            if(!Caster||Caster.dead)return;
            if(autoShow&&Time.time>=nextAuto){nextAuto=Time.time+3;foreach(var a in Caster.abilities.OfType<CompositeSkill>())if(Cast(a))break;}
            if(Time.time<nextOrder)return;nextOrder=Time.time+.4f;
            if(attacks&&!SkillActionLock.Active(Caster)&&Caster.unitState!=UnitStates.AbilityCasting){var target=enemies.FirstOrDefault(u=>u&&!u.dead);if(target){Caster.canAttack=true;Caster.Attack(target);}}
            if(retaliation){var u=enemies.FirstOrDefault(e=>e&&!e.dead);if(u&&!SkillActionLock.Active(u)){u.canAttack=true;u.Attack(Caster);}}
        }
        public bool Cast(Ability ability)
        {
            if(!Caster||Caster.dead)return false;
            int i=Array.IndexOf(Caster.abilities,ability);if(i<0)return false;
            bool ok=Caster.UseAbilityItem(i,false,null,Vector3.zero,true);
            if(ok)Note("Применение: "+Name(ability));
            return ok;
        }
        void OnFired(Unit caster,int id,int level,Unit target,Vector3 point)
        {if(!rebuilding&&caster==Caster&&abilityMap.TryGetValue(id,out var ability))Note(Name(ability));}
        void Note(string s){headline=s;messages.Insert(0,s);if(messages.Count>4)messages.RemoveAt(4);}
        static string Name(Ability a)=>a.abilityName!=null&&a.abilityName.Length>0?a.abilityName[0]:a.name;
        float Cooldown(int i){int n=Caster.GetAbilityCooldownIndex(i,false);return n>=0?Caster.cooldownAbility[n]:0;}
        void StopOrders(){attacks=retaliation=false;foreach(var u in actors)if(u&&!u.dead){u.canAttack=false;u.Hold();}}
        void Damage(Unit u,float fraction){if(u&&!u.dead){var source=u==Caster?enemies.FirstOrDefault(x=>x&&!x.dead):Caster;if(source)source.DealDamage(u,u.maxHealth*fraction,source.damageType,true,u.transform.position);}}
        void OnDestroy(){SkillPresentationEvents.SkillFired-=OnFired;if(demoNavInstance.valid)demoNavInstance.Remove();if(demoNavData)Destroy(demoNavData);Time.timeScale=1;}
    }
}
