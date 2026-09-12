using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace StrategyCore {
 [Serializable] public class SkillDamageLinkBlock {
  public bool enabled;
  [Min(0)] public float duration=5;
  [Min(0)] public float sharedFraction=.4f;
  [Tooltip("Тип для копии фактически прошедшего урона: обычно чистый, чтобы не считать броню второй раз.")] public DamageType sharedDamageType;
  public Technology deathBurstTechnology;
  [Min(0)] public float deathBurstDamage;
  public DamageType deathBurstDamageType;
  public CompositeSkill deathPresentation;
  public Material chainMaterial;
  public Color chainColor=new Color(.25f,1,.1f,.8f);
  [Min(.01f)] public float chainWidth=.065f;
 }
 public sealed class DamageLinkGroup:MonoBehaviour {
  static readonly List<DamageLinkGroup> groups=new List<DamageLinkGroup>();
  static int nextId,propagationDepth;
  readonly List<Unit> members=new List<Unit>();
  CompositeSkill skill;Unit source;int owner,linkId;float endTime,power;bool ended;
  public int MemberCount=>members.Count(u=>u&&!u.dead);
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void Reset(){groups.Clear();nextId=0;propagationDepth=0;}
  public static DamageLinkGroup Create(CompositeSkill skill,Unit source,int owner,List<Unit> targets,float power){
   if(NetworkConnectionHandler.isClient||!source||source.dead||skill.damageLink==null||!skill.damageLink.enabled||skill.damageLink.duration<=0)return null;
   var live=targets.Where(u=>u&&!u.dead).Distinct().ToList();if(live.Count<2)return null;
   foreach(var prior in groups.ToArray())if(prior&&prior.source==source&&prior.skill==skill)prior.End();
   var g=new GameObject("DamageLinkGroup").AddComponent<DamageLinkGroup>();g.skill=skill;g.source=source;g.owner=owner;g.power=power;g.linkId=++nextId;g.endTime=Time.time+skill.damageLink.duration;g.members.AddRange(live);groups.Add(g);
   foreach(var u in live)u.OnDie+=g.OnMemberDeath;source.OnDie+=g.OnCasterDeath;
   SkillPresentationEvents.RaiseDamageLink(g.linkId,skill.id,source,live.ToArray(),skill.damageLink.duration);
   if(NetworkDataSync.instance)NetworkDataSync.instance.DamageLinkSend(g.linkId,skill.id,source.netID,live.Select(u=>u.netID).ToArray(),skill.damageLink.duration);
   return g;
  }
  public static void NotifyDamage(Unit target,float amount){if(NetworkConnectionHandler.isClient||propagationDepth>0||amount<=0)return;propagationDepth++;try{foreach(var g in groups.ToArray())if(g&&!g.ended&&Time.time<g.endTime&&g.members.Contains(target))g.Share(target,amount);}finally{propagationDepth--;}}
  void Share(Unit target,float damage){var b=skill.damageLink;if(!b.sharedDamageType)return;foreach(var u in members.ToArray())if(u&&u!=target&&!u.dead&&u.health>0)u.GetDamage(damage*b.sharedFraction*power,b.sharedDamageType,owner,source,false,out _);}
  void OnMemberDeath(Unit unit,int killer,Unit killerUnit,bool rewards){if(ended)return;unit.OnDie-=OnMemberDeath;members.Remove(unit);var b=skill.damageLink;
   if(Time.time<endTime&&b.deathBurstTechnology&&InterflowAbility.OptionalTechUnlocked(b.deathBurstTechnology,owner)&&b.deathBurstDamageType&&b.deathBurstDamage>0){propagationDepth++;try{foreach(var u in members.ToArray())if(u&&!u.dead&&u.health>0){if(b.deathPresentation)InterflowAbility.EmitSkillFired(source,b.deathPresentation,0,u,u.transform.position);u.GetDamage(b.deathBurstDamage*power,b.deathBurstDamageType,owner,source,false,out _);}}finally{propagationDepth--;}}
   if(MemberCount<2)End();
  }
  void OnCasterDeath(Unit u,int p,Unit killer,bool rewards)=>End();
  void Update(){if(!source||source.dead||Time.time>=endTime)End();}
  public void End(){if(ended)return;ended=true;groups.Remove(this);foreach(var u in members)if(u)u.OnDie-=OnMemberDeath;if(source)source.OnDie-=OnCasterDeath;members.Clear();SkillPresentationEvents.RaiseDamageLink(linkId,-1,null,Array.Empty<Unit>(),0);if(NetworkDataSync.instance)NetworkDataSync.instance.DamageLinkSend(linkId,-1,0,Array.Empty<ushort>(),0);Destroy(gameObject);}
  void OnDestroy(){if(!ended)End();}
 }
}
