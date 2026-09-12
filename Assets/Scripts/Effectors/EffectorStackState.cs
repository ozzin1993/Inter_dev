using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace StrategyCore {
 // Optional bounded-stack rules reuse native Effector holders, duration, DoT and status transport.
 public sealed class EffectorStackState : MonoBehaviour {
  readonly Dictionary<Effector,int> lastFrame=new Dictionary<Effector,int>();
  Unit unit;readonly HashSet<Effector> watched=new HashSet<Effector>();
  readonly Dictionary<Effector,AbilityPassiveEffects> applied=new Dictionary<Effector,AbilityPassiveEffects>();
  public static void Changed(Unit target,Effector effect,bool tick=false) {
   if(NetworkConnectionHandler.isClient||!target||target.dead||!effect||effect.maxStacks<=0)return;
   var state=target.GetComponent<EffectorStackState>();if(!state)state=target.gameObject.AddComponent<EffectorStackState>();
   if(tick&&state.lastFrame.TryGetValue(effect,out int frame)&&frame==Time.frameCount)return;state.lastFrame[effect]=Time.frameCount;
   state.watched.Add(effect);state.Refresh(effect);
  }
  void Awake(){unit=GetComponent<Unit>();unit.OnDie+=OnDeath;}
  void OnDestroy(){if(unit)unit.OnDie-=OnDeath;}
  void Refresh(Effector effect) {
   var holders=unit.effectors.Where(h=>h.effector==effect).ToArray();
   bool active=holders.Length>=effect.maxStacks&&effect.maximumStackEffects!=null&&holders.Any(h=>InterflowAbility.OptionalTechUnlocked(effect.maximumStackTechnology,h.owner));
   if(active&&!applied.ContainsKey(effect)){applied[effect]=effect.maximumStackEffects;effect.maximumStackEffects.AddEffect(unit);InterflowAbility.RequestForceSync();}
   else if(!active&&applied.TryGetValue(effect,out var previous)){previous.RemoveEffect(unit);applied.Remove(effect);InterflowAbility.RequestForceSync();}
  }
  void OnDeath(Unit victim,int killer,Unit killerUnit,bool rewards) {
   foreach(var effect in watched) {
    if(effect.deathSpreadRadius<=0||effect.deathSpreadStacks<=0)continue;
    var holders=victim.effectors.Where(h=>h.effector==effect).ToArray();if(holders.Length<effect.maxStacks)continue;
    var source=holders.LastOrDefault(h=>InterflowAbility.OptionalTechUnlocked(effect.deathSpreadTechnology,h.owner));if(source==null)continue;
    var pos=victim.transform.position;
    var targets=Utils.GetUnitsInRadius(new Vector2(pos.x,pos.z),effect.deathSpreadRadius,source.owner,new UnitSelector{isEnemy=true,isUnit=true,isGround=true,isWater=true},-1,victim);
    if(targets!=null)foreach(var target in targets){if(!target||target.dead)continue;for(int i=0;i<effect.deathSpreadStacks;i++)Effector.EffectorAdd(target,effect,source.unitOwner,source.owner);}
    if(effect.deathSpreadPresentation)InterflowAbility.EmitSkillFired(source.unitOwner,effect.deathSpreadPresentation,0,victim,pos);
   }
   foreach(var pair in applied)pair.Value.RemoveEffect(unit);applied.Clear();unit.OnDie-=OnDeath;
  }
 }
}
