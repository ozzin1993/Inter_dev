using System;
using UnityEngine;
namespace StrategyCore {
 [Serializable] public class PassiveCleaveBlock {
  public bool enabled;
  public bool centerOnCaster;
  [Tooltip("Пусто — постоянно. Иначе круговой удар работает только пока на носителе есть этот эффектор.")] public Effector requiredEffector;
  [Min(0)] public float radius=1.5f;
  [Range(0,1)] public float damageFraction=.4f;
  [Range(0,1),Tooltip("0 — без ограничения здоровья носителя.")] public float casterHpBelow;
  public UnitSelector targets=new UnitSelector{isEnemy=true,isUnit=true,isGround=true,isWater=true};
  public Technology upgradeTechnology;
  [Min(0)] public float upgradedRadius=2f;
  [Range(0,1)] public float upgradedDamageFraction=.6f;
  public CompositeSkill hitPresentation;
 }
 public partial class CompositePassive {
  void ApplyCleave(Unit unit,Carrier carrier) {
   if(cleave==null||!cleave.enabled)return;
   carrier.cleaveCallback=new AfterDamageDealCallback{Ability=this,Level=carrier.level,Callback=CleaveHit};
   unit.OnAfterDamageDealCallbacks.Add(carrier.cleaveCallback);
  }
  void RemoveCleave(Unit unit,Carrier carrier) {
   if(carrier.cleaveCallback.Callback!=null)unit.OnAfterDamageDealCallbacks.Remove(carrier.cleaveCallback);
   carrier.cleaveCallback=default;
  }
  void CleaveHit(Unit primary,Vector3 point,Effector[] effects,float amount,bool direct,
   DamageType type,Unit source,Projectile projectile,int owner,int level) {
   if(NetworkConnectionHandler.isClient||!direct||amount<=0||!source||source.dead||!carriers.ContainsKey(source))return;
   var b=cleave;if(b==null||!b.enabled)return;
   if(b.requiredEffector&&!source.effectors.Exists(e=>e.effector==b.requiredEffector))return;
   if(b.casterHpBelow>0&&!SkillTargeting.IsBelowHealthThreshold(source,b.casterHpBelow))return;
   bool upgraded=b.upgradeTechnology&&OptionalTechUnlocked(b.upgradeTechnology,owner);
   float radius=upgraded?b.upgradedRadius:b.radius;
   float fraction=upgraded?b.upgradedDamageFraction:b.damageFraction;
   if(radius<=0||fraction<=0)return;
   if(b.centerOnCaster)point=source.transform.position;
   var targets=Utils.GetUnitsInRadius(new Vector2(point.x,point.z),radius,owner,b.targets,-1,source);
   if(targets==null)return;
   foreach(var target in targets) {
    if(!target||target.dead||target==primary)continue;
    target.GetDamage(amount*fraction,type,owner,source,false,out _);
    if(b.hitPresentation)EmitSkillFired(source,b.hitPresentation,level,target,target.transform.position);
   }
   RequestForceSync();
  }
 }
}
