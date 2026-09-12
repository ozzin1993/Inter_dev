using UnityEngine;
namespace StrategyCore {
 public sealed class MorphAttackMode:MonoBehaviour {
  Unit unit;bool saved,melee;AttackType type;float range;GameObject projectile;
  public void Apply(Unit carrier,Unit shape){if(saved)Restore();unit=carrier;melee=unit.melee;type=unit.attackType;range=unit.attackRange;projectile=unit.projectileGO;saved=true;unit.melee=shape.melee;unit.attackType=shape.attackType;unit.attackRange=shape.attackRange;unit.projectileGO=shape.projectileGO;unit.projectileVFX=shape.projectileGO?shape.projectileGO.GetComponent<Projectile>():null;}
  public void Restore(){if(!saved||!unit)return;unit.melee=melee;unit.attackType=type;unit.attackRange=range;unit.projectileGO=projectile;unit.projectileVFX=projectile?projectile.GetComponent<Projectile>():null;saved=false;}
 }
}
