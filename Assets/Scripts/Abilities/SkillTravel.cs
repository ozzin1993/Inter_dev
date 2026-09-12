using UnityEngine;
using UnityEngine.AI;
namespace StrategyCore {
 public sealed class SkillTravel:MonoBehaviour {
  Unit unit;CompositeSkill skill;int level;Vector3 start,end,modelPosition,lastTrail;Transform model;float elapsed;bool running,animate;
  public bool Running=>running;
  SkillMotionPosition motion;
  public void Begin(Unit caster,CompositeSkill ability,int lvl,Vector3 point){
   if(running||NetworkConnectionHandler.isClient||!caster||caster.dead||!caster.canMove||!caster.agent)return;
   if(ability.casterMove.respectControlImmunity&&caster.TryGetComponent<ControlImmunity>(out var ci)&&ci.Active)return;
   Vector3 direction=point-caster.transform.position;direction.y=0;point+=direction.normalized*ability.casterMove.passThroughDistance;
   if(!NavMesh.SamplePosition(point,out var hit,2,caster.agent.areaMask))return;
   unit=caster;skill=ability;level=lvl;start=unit.transform.position;end=hit.position;lastTrail=start;elapsed=0;running=true;
   model=unit.mainRenderer?unit.mainRenderer.transform:null;if(model)modelPosition=model.localPosition;
   SkillActionLock.Add(unit);motion=new SkillMotionPosition(unit);unit.LookAtInstant(end);
   animate=unit.animator&&!string.IsNullOrEmpty(skill.casterMove.travelAnimationState)&&unit.animator.HasState(0,Animator.StringToHash(skill.casterMove.travelAnimationState));
   if(animate){unit.AnimatorSetBool(AnimationState.Reset,false);unit.AnimatorSetBool(AnimationState.Idle,false);unit.animator.CrossFadeInFixedTime(skill.casterMove.travelAnimationState,.12f,0,0);}
  }
  void LateUpdate(){
   if(!running)return;if(!unit||unit.dead||!unit.agent){Finish(false);return;}
   elapsed+=Time.deltaTime;float t=Mathf.Clamp01(elapsed/Mathf.Max(.01f,skill.casterMove.travelSeconds));
   Vector3 desired=Vector3.Lerp(start,end,t*t*(3-2*t));
   // Each segment stays on navigable ground; airborne height is presentation only.
   if(!motion.Move(desired)){Finish(false);return;}
   if(model)model.localPosition=modelPosition+Vector3.up*(Mathf.Sin(t*Mathf.PI)*skill.casterMove.leapHeight);
   if(skill.casterMove.trailSkill&&Vector3.Distance(lastTrail,unit.transform.position)>=Mathf.Max(.3f,skill.casterMove.trailSpacing)){lastTrail=unit.transform.position;skill.casterMove.trailSkill.ExecuteImpact(unit,unit.owner,level,null,lastTrail);}
   if(t>=1)Finish(true);
  }
  void Finish(bool impact){if(!running)return;running=false;if(model)model.localPosition=modelPosition;
   motion?.Finish();if(unit)SkillActionLock.Remove(unit);
   if(animate&&unit&&!unit.dead&&unit.animator){unit.AnimatorSetBool(AnimationState.Reset,false);unit.animator.CrossFadeInFixedTime("idleReady",.15f,0,0);}
   if(impact&&unit&&!unit.dead)skill.ExecuteImpact(unit,unit.owner,level,null,end);
  }
  void OnDisable(){if(running)Finish(false);}
 }
}
