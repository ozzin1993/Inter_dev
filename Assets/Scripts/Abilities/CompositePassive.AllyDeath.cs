using System;
using UnityEngine;
namespace StrategyCore {
 [Serializable] public class PassiveAllyDeathBlock {
  public bool enabled;
  [Min(0)] public float healFlat;
  [Tooltip("Типы погибших союзников. Пусто — любые боевые юниты команды.")] public Unit[] victimPrefabs;
  public CompositeSkill presentation;
 }
 public partial class CompositePassive {
  void ApplyAllyDeath(Unit victim) {
   var block=allyDeath;
   if(NetworkConnectionHandler.isClient||!victim||victim.unitType!=UnitType.Unit||block==null||!block.enabled)return;
   if(block.victimPrefabs!=null&&block.victimPrefabs.Length>0){bool allowed=false;foreach(var p in block.victimPrefabs)if(p&&p.unitTypeID==victim.unitTypeID){allowed=true;break;}if(!allowed)return;}
   // The match death hub covers the single battle line; no range limit is imposed.
   foreach(var entry in carriers){var unit=entry.Key;if(!unit||unit.dead||unit==victim||unit.team!=victim.team)continue;
    if(block.healFlat>0)unit.ChangeHP(block.healFlat);
    if(block.presentation)EmitSkillFired(unit,block.presentation,entry.Value.level,unit,unit.transform.position);
   }
   RequestForceSync();
  }
 }
}
