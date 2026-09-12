using UnityEngine;
namespace StrategyCore {
 [System.Serializable] public class PassiveCombatResourceBlock {
  public bool enabled;
  public string resourceName="Вера";
  public float hitGain=5, killGain=10, combatGainPerSecond=1;
  public float combatMemorySeconds=5;
  public bool onlyRangedHits=true, suspendWhileMorphed=true;
 }
 // Per-carrier state; the native mana bar, costs and network snapshot remain authoritative.
 public sealed class CombatResource : MonoBehaviour {
  Unit unit; CompositePassive source; PassiveCombatResourceBlock settings; MatchManager hub;
  float combatUntil;
  public void Configure(CompositePassive ability,PassiveCombatResourceBlock block) {
   if(source==ability)return;
   unit=GetComponent<Unit>();source=ability;settings=block;
   unit.ChangeMP(-unit.mana);hub=MatchManager.instance;
   if(hub)hub.OnUnitDeathServer+=Killed;
  }
  public void Remove(CompositePassive ability){if(source==ability)Destroy(this);}
  bool Available=>unit&&!unit.dead&&settings!=null&&(!settings.suspendWhileMorphed||!unit.polymorphed);
  public void Hit(Unit victim,float amount,bool direct) {
   if(!Available||!victim||victim.team==unit.team||amount<=0)return;
   combatUntil=Time.time+settings.combatMemorySeconds;
   if(direct&&(!settings.onlyRangedHits||!unit.melee))unit.ChangeMP(settings.hitGain);
  }
  public void Threatened(Unit attacker){if(Available&&attacker&&attacker.team!=unit.team)combatUntil=Time.time+settings.combatMemorySeconds;}
  void Killed(Unit victim,int owner,Unit killer,bool rewards){if(Available&&killer==unit&&victim&&victim.unitType==UnitType.Unit&&victim.team!=unit.team){combatUntil=Time.time+settings.combatMemorySeconds;unit.ChangeMP(settings.killGain);}}
  void Update(){if(NetworkConnectionHandler.isClient||!Available)return;if(Time.time<combatUntil)unit.ChangeMP(settings.combatGainPerSecond*Time.deltaTime);}
  void OnDestroy(){if(hub)hub.OnUnitDeathServer-=Killed;}
 }
}
