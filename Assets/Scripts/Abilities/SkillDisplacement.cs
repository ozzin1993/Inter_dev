using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    // Server motion uses the ordinary position replication, with no teleport at the endpoint.
    public sealed class SkillDisplacement : MonoBehaviour
    {
        Unit unit;
        Vector3 start, end;
        float elapsed, duration;
        bool running;
        SkillMotionPosition position;
        public bool Running => running;
        public static bool Begin(Unit target, Vector3 destination, float seconds, bool respectImmunity)
        {
            if(NetworkConnectionHandler.isClient||!target||target.dead||!target.canMove||!target.agent)return false;
            if(respectImmunity&&target.TryGetComponent<ControlImmunity>(out var immunity)&&immunity.Active)return false;
            if(!NavMesh.SamplePosition(destination,out var hit,2,target.agent.areaMask))return false;
            var motion=target.GetComponent<SkillDisplacement>()??target.gameObject.AddComponent<SkillDisplacement>();
            motion.Finish();motion.unit=target;motion.start=target.transform.position;motion.end=hit.position;
            motion.duration=Mathf.Max(.05f,seconds);motion.elapsed=0;motion.running=true;
            SkillActionLock.Add(target);motion.position=new SkillMotionPosition(target);return true;
        }
        void LateUpdate()
        {
            if(!running)return;
            if(!unit||unit.dead||!unit.agent){Finish();return;}
            elapsed+=Time.deltaTime;float t=Mathf.Clamp01(elapsed/duration);
            float progress=1-(1-t)*(1-t);
            if(!position.Move(Vector3.Lerp(start,end,progress))||t>=1)Finish();
        }
        void Finish(){if(!running)return;running=false;position?.Finish();if(unit)SkillActionLock.Remove(unit);}
        void OnDisable()=>Finish();
    }
}
