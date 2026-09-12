using UnityEngine;
namespace StrategyCore {
 public sealed class SkillActionLock:MonoBehaviour {
  Unit unit;int count;bool move,attack;
  public static bool Active(Unit u){var c=u?u.GetComponent<SkillActionLock>():null;return c&&c.count>0;}
  public static void Add(Unit u){var c=u.GetComponent<SkillActionLock>();if(!c)c=u.gameObject.AddComponent<SkillActionLock>();if(c.count++==0){c.unit=u;c.move=u.canMove;c.attack=u.canAttack;u.Hold();u.canMove=false;u.canAttack=false;}}
  public static void Remove(Unit u){var c=u.GetComponent<SkillActionLock>();if(!c||c.count==0)return;if(--c.count==0){u.canMove=c.move;u.canAttack=c.attack;}}
 }
}
