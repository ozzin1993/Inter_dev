using UnityEngine;
namespace StrategyCore {
 // Presentation-only stance, attached to the buff VFX and consequently shown on every peer.
 public sealed class SkillBuffPose:MonoBehaviour {
  public string stateName="brace";Unit unit;Animator animator;
  void LateUpdate(){if(!unit)unit=GetComponentInParent<Unit>();if(!unit||unit.dead)return;if(!animator)animator=unit.GetComponentInChildren<Animator>();if(animator&&animator.HasState(0,Animator.StringToHash(stateName)))animator.Play(stateName,0,0);}
  void OnDestroy(){if(unit&&!unit.dead&&animator&&animator.GetCurrentAnimatorStateInfo(0).IsName(stateName))animator.Play("idleReady",0,0);}
 }
}
