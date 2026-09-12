using UnityEngine;
namespace StrategyCore {
 public partial class Effector {
  [Header("Ограниченные стаки")]
  [Min(0),Tooltip("0 — без ограничения. При достижении лимита новый hit обновляет длительность стаков.")] public int maxStacks;
  [Range(0,1),Tooltip("Снижение входящего лечения на один стак. Складывается, минимум лечения — 0.")] public float healingReduction;
  public Technology maximumStackTechnology;
  public AbilityPassiveEffects maximumStackEffects;
  public Technology deathSpreadTechnology;
  [Min(0)] public float deathSpreadRadius;
  [Min(0)] public int deathSpreadStacks;
  public CompositeSkill deathSpreadPresentation;
  public static float ModifyHealing(Unit unit,float amount) {
   if(amount<=0||unit==null)return amount;
   float reduction=0;foreach(var h in unit.effectors)if(h?.effector)reduction+=h.effector.healingReduction*h.powerMultiplier;
   return amount*Mathf.Clamp01(1-reduction);
  }
 }
}
