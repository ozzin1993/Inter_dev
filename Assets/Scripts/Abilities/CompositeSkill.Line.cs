using System;using System.Linq;using System.Collections.Generic;using UnityEngine;
namespace StrategyCore {
 [Serializable]public class SkillLineBlock {
  public bool enabled;[Min(.1f)]public float length=8;[Min(.1f)]public float width=2;
  public Technology pullTechnology;public VFXReferencer presentation;public float visualLifetime=1.2f;
 }
 [Serializable]public class SkillDisruptionBlock {
  public bool enabled;public Unit.UnitCategory[] categories;public Unit[] additionalPrefabs;
  public float silenceSeconds=3;public bool purgeSpeedBuffs;
  public Technology manaBurnTechnology;[Range(0,1)]public float currentManaFraction=.3f;public DamageType manaDamageType;public CompositeSkill presentation;
 }
 public partial class CompositeSkill {
  public SkillLineBlock line=new SkillLineBlock();
  public SkillDisruptionBlock disruption=new SkillDisruptionBlock();
  void CollectLine(Unit caster,int owner,List<Unit> gathered){
   var origin=caster.transform.position;var forward=caster.LookDirection;forward.y=0;forward.Normalize();
   var middle=origin+forward*line.length*.5f;
   var found=Utils.GetUnitsInRadius(new Vector2(middle.x,middle.z),line.length*.5f+line.width,owner,unitSelector,-1,caster);
   if(found==null)return;
   foreach(var u in found){if(!u||u.dead)continue;var delta=u.transform.position-origin;delta.y=0;float along=Vector3.Dot(delta,forward);float side=(delta-forward*along).magnitude;if(along>=0&&along<=line.length&&side<=line.width*.5f)AddCandidate(u,caster,gathered);}
  }
  void ApplyDisruption(Unit caster,Unit target,int level){
   var b=disruption;if(b==null||!b.enabled||!caster||!target||target.dead)return;
   bool matches=CategoryAllowed(target,b.categories);
   if(!matches&&b.additionalPrefabs!=null)matches=b.additionalPrefabs.Any(p=>p&&p.unitTypeID==target.unitTypeID);
   if(!matches)return;
   if(b.silenceSeconds>0)target.Mute(b.silenceSeconds);
   if(b.purgeSpeedBuffs)foreach(var h in target.effectors.ToArray()){var e=h.effector;if(!e||!e.passiveEffectsOn||e.passiveEffects==null)continue;var p=e.passiveEffects;if(p.moveSpeedChange>0||p.moveSpeedPercentageChange>0||p.attackSpeedChange>0||p.attackSpeedPercentageChange>0)Effector.EffectorRemove(target,h);}
   if(target.muted&&b.manaBurnTechnology&&OptionalTechUnlocked(b.manaBurnTechnology,caster.owner))ManaBurnEffect.Apply(caster,target,Mathf.Max(0,target.mana)*b.currentManaFraction,b.manaDamageType);
   if(b.presentation)EmitSkillFired(caster,b.presentation,level,target,target.transform.position);
  }
  void ApplyLinePull(Unit caster,Unit target){if(line==null||!line.enabled||!line.pullTechnology||!OptionalTechUnlocked(line.pullTechnology,caster.owner)||!target||target.dead)return;var forward=caster.LookDirection;forward.y=0;forward.Normalize();Knockback.MoveTo(target,caster.transform.position+forward*line.length*.5f,true);}
 }
}
