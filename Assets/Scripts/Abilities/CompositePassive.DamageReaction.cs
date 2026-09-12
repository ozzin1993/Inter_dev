using System;
using UnityEngine;
namespace StrategyCore
{
 [Serializable] public class PassiveDamageReactionBlock
 {
  public bool enabled;
  [Tooltip("Пустой список — любой тип входящего урона.")] public DamageType[] damageTypes;
  public bool onlyDirectAttack;
  [Min(0)] public float cooldown;
  [Range(0,1)] public float healMaxHpFraction;
  public CompositeSkill presentation;
 }
 public partial class CompositePassive
 {
  void ApplyDamageReaction(Unit unit,Carrier carrier)
  {
   var block=damageReaction;if(block==null||!block.enabled)return;
   carrier.damagedHandler=(victim,attacker,type,damage,direct)=>
   {
    if(NetworkConnectionHandler.isClient||victim==null||victim.dead||damage<=0)return;
    if(block.onlyDirectAttack&&!direct)return;
    if(block.damageTypes!=null&&block.damageTypes.Length>0&&Array.IndexOf(block.damageTypes,type)<0)return;
    if(Time.time<carrier.reactionReadyAt)return;
    carrier.reactionReadyAt=Time.time+Mathf.Max(0,block.cooldown);
    if(block.healMaxHpFraction>0)victim.ChangeHP(victim.maxHealth*block.healMaxHpFraction);
    if(block.presentation!=null)EmitSkillFired(victim,block.presentation,carrier.level,victim,victim.transform.position);
            RequestForceSync();
   };
   InterflowCombat.DamagedListenerAdd(unit,carrier.damagedHandler);
  }
  void RemoveDamageReaction(Unit unit,Carrier carrier)
  {
   if(carrier.damagedHandler!=null)InterflowCombat.DamagedListenerRemove(unit,carrier.damagedHandler);
   carrier.damagedHandler=null;
  }
 }
}
