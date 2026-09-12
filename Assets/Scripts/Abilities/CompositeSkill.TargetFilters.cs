using UnityEngine;
namespace StrategyCore {
 public partial class CompositeSkill {
  [Tooltip("Допустимые типы юнитов по основным префабам. Пусто — все типы.")]
  public Unit[] targetPrefabs;
  [Tooltip("В режиме умной точки: отдельный набор целей области. Основные фильтры выбирают только центр взрыва.")]
  public bool independentAreaTargets;
  public UnitSelector areaSelector;
  [Range(0,360),Tooltip("Цель должна быть впереди кастера внутри этого угла. 0 — без ограничения.")]
  public float targetFrontAngle;
  bool PassesPositionFilter(Unit target,Unit caster) {
   if(targetFrontAngle<=0)return true;
   if(!caster||!target||target==caster)return false;
   var direction=target.transform.position-caster.transform.position;direction.y=0;
   return direction.sqrMagnitude<.0001f||Vector3.Angle(caster.LookDirection,direction)<=targetFrontAngle*.5f;
  }
 }
}
