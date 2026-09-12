using System;
using System.Linq;
using UnityEngine;
namespace StrategyCore {
 [Serializable] public class PassiveManaBurnBlock {
  public bool enabled;
  [Min(0)] public float amount=15;
  public DamageType damageType;
  public Technology shieldTechnology;
  [Range(0,1)] public float shieldFraction=.5f;
  [Min(0),Tooltip("0 — щит живёт до поглощения урона.")] public float shieldDuration;
  public Effector shieldPresentation;
  public CompositeSkill hitPresentation;
 }
 public static class ManaBurnEffect {
  public static float Apply(Unit source,Unit target,float requested,DamageType damageType) {
   if(NetworkConnectionHandler.isClient||!source||source.dead||!target||target.dead||requested<=0)return 0;
   float before=Mathf.Max(0,target.mana);
   target.ChangeMP(-Mathf.Min(before,requested));
   float burned=Mathf.Max(0,before-target.mana);
   if(burned>0&&damageType)target.GetDamage(burned,damageType,source.owner,source,false,out _);
   return burned;
  }
 }
 public partial class CompositePassive {
  void ApplyManaBurn(Unit u,Carrier c) {
   if(manaBurn==null||!manaBurn.enabled)return;
   c.manaBurnCallback=new AfterDamageDealCallback{Ability=this,Level=c.level,Callback=ManaBurnHit};
   u.OnAfterDamageDealCallbacks.Add(c.manaBurnCallback);
  }
  void RemoveManaBurn(Unit u,Carrier c) {
   if(c.manaBurnCallback.Callback!=null)u.OnAfterDamageDealCallbacks.Remove(c.manaBurnCallback);
   c.manaBurnCallback=default;
  }
  void ManaBurnHit(Unit target,Vector3 point,Effector[] effects,float damage,bool direct,DamageType type,Unit source,Projectile projectile,int owner,int level) {
   if(!direct||!source||source.dead||!carriers.ContainsKey(source))return;
   var b=manaBurn;if(b==null||!b.enabled)return;
   float burned=ManaBurnEffect.Apply(source,target,b.amount,b.damageType);
   if(burned<=0)return;
   if(b.hitPresentation)EmitSkillFired(source,b.hitPresentation,level,target,point);
   if(b.shieldFraction>0&&b.shieldTechnology&&OptionalTechUnlocked(b.shieldTechnology,owner)) {
    var visual=b.shieldPresentation;
    AbsorbShield.Add(source,burned*b.shieldFraction,b.shieldDuration,u=>{if(u&&visual)foreach(var h in u.effectors.Where(h=>h.effector==visual).ToArray())Effector.EffectorRemove(u,h);});
    if(visual)Effector.EffectorAdd(source,visual,source,owner);
   }
   RequestForceSync();
  }
 }
}
