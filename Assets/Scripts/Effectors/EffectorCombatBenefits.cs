using UnityEngine;
namespace StrategyCore {
 // Benefits follow actual holders. Refresh/removal is synchronous, copied projectile hooks re-check expiry.
 public sealed class EffectorCombatBenefits:MonoBehaviour {
  Unit unit;AfterDamageDealCallback callback;bool exemption,hooked;
  public static void Refresh(Unit u){if(NetworkConnectionHandler.isClient||!u)return;var c=u.GetComponent<EffectorCombatBenefits>();if(!c){if(!u.effectors.Exists(h=>h.effector&&(h.effector.lifestealFraction>0||h.effector.waiveSpellHealthCost)))return;c=u.gameObject.AddComponent<EffectorCombatBenefits>();}c.unit=u;c.UpdateBenefits();}
  void UpdateBenefits(){if(!unit)return;if(!hooked){hooked=true;callback=new AfterDamageDealCallback{Callback=Heal};unit.OnAfterDamageDealCallbacks.Add(callback);}bool free=false;foreach(var h in unit.effectors)if(h.effector&&h.effector.waiveSpellHealthCost&&(h.effector.freeCostPrefabs==null||h.effector.freeCostPrefabs.Length==0||System.Array.Exists(h.effector.freeCostPrefabs,p=>p&&p.unitTypeID==unit.unitTypeID)))free=true;
   if(free!=exemption){var m=unit.GetComponent<SpellCastModifiers>();if(!m)m=unit.gameObject.AddComponent<SpellCastModifiers>();m.HealthCostExemptions+=free?1:-1;exemption=free;}}
  void Heal(Unit t,Vector3 pos,Effector[] e,float damage,bool direct,DamageType type,Unit by,Projectile projectile,int owner,int level){if(!unit||unit.dead||!direct||by!=unit||!t||!UnitSelector.IsUnitCompatible(unit.owner,t,new UnitSelector{isEnemy=true,isUnit=true,isGround=true,isAir=true,isWater=true}))return;float f=0;foreach(var h in unit.effectors)if(h.effector)f=Mathf.Max(f,h.effector.lifestealFraction);if(f>0)unit.ChangeHP(damage*f);}
  void OnDestroy(){if(!unit)return;if(hooked)unit.OnAfterDamageDealCallbacks.Remove(callback);if(exemption){var m=unit.GetComponent<SpellCastModifiers>();if(m)m.HealthCostExemptions--;}}
 }
}
