using System;
using System.Collections.Generic;
using UnityEngine;
namespace StrategyCore {
 [Serializable] public class PassiveSpellCastBlock {
  public bool enabled;
  [Range(0,1)] public float currentHpCost;
  [Min(0)] public float cooldownMultiplier=1;
  public Technology ritualTechnology;
  [Min(0),Tooltip("Прибавка к урону СЛЕДУЮЩЕГО каста за 1% максимального HP, потраченный этим кастом.")] public float nextDamagePerHpPercent;
 }
 public sealed class SpellCastModifiers : MonoBehaviour {
  sealed class Rule {public PassiveSpellCastBlock block;public float pending;}
  readonly Dictionary<CompositePassive,Rule> rules=new Dictionary<CompositePassive,Rule>();
  public float CurrentPower {get;private set;}=1;
  public int HealthCostExemptions;
  public void Set(CompositePassive key,PassiveSpellCastBlock block){if(!rules.ContainsKey(key))rules.Add(key,new Rule{block=block});}
  public void Remove(CompositePassive key)=>rules.Remove(key);
  public static float Cooldown(Unit u){var m=u?u.GetComponent<SpellCastModifiers>():null;if(!m)return 1;float v=1;foreach(var r in m.rules.Values)v*=Mathf.Max(0,r.block.cooldownMultiplier);return v;}
  public static float Power(Unit u){var m=u?u.GetComponent<SpellCastModifiers>():null;return m?m.CurrentPower:1;}
  public static IDisposable Begin(Unit u,bool enabled){if(!enabled||NetworkConnectionHandler.isClient||!u||u.dead)return null;var m=u.GetComponent<SpellCastModifiers>();return m?new CastScope(m,u):null;}
  sealed class CastScope:IDisposable {
   SpellCastModifiers owner;float prior;
   public CastScope(SpellCastModifiers m,Unit u){owner=m;prior=m.CurrentPower;float power=1;
    foreach(var r in m.rules.Values){bool ritual=r.block.ritualTechnology&&InterflowAbility.OptionalTechUnlocked(r.block.ritualTechnology,u.owner);if(ritual)power+=r.pending;r.pending=0;float before=u.health;if(m.HealthCostExemptions<=0&&r.block.currentHpCost>0)PercentHpCost.PayFromCaster(u,r.block.currentHpCost);float lost=Mathf.Max(0,before-u.health);if(ritual&&u.maxHealth>0)r.pending=lost/u.maxHealth*100*r.block.nextDamagePerHpPercent;}
    m.CurrentPower=power;
   }
   public void Dispose(){if(owner)owner.CurrentPower=prior;}
  }
 }
}
