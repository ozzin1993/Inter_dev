using System;
using UnityEngine;
namespace StrategyCore {
 [Serializable] public class SkillProjectileImpactBlock {
  public bool enabled;
  [Tooltip("Мгновенный скилл, исполняемый в точке прилёта. Стоимость и откат второй раз не списываются.")] public CompositeSkill skill;
  public Technology primaryStunTechnology;
  [Min(0)] public float primaryStunSeconds;
  public CompositeSkill stunPresentation;
 }
 public partial class CompositeSkill {
  internal void ExecuteImpact(Unit caster,int owner,int level,Unit primary,Vector3 point) {
   if(NetworkConnectionHandler.isClient)return;
   // A delivered payload never starts another projectile or pays another cast cost.
   EmitSkillFired(caster,this,level,primary,point);
   var targets=CollectTargets(caster,owner,level,primary,point);
   ApplyEffects(caster,owner,level,targets,point,false);
   SendPresentation(targets,level);RequestForceSync();
  }
 }
}
