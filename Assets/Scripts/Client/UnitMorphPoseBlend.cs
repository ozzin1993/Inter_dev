using UnityEngine;

namespace StrategyCore
{
    // Blend the last visible humanoid pose into the new form; gameplay changes remain immediate.
    [DefaultExecutionOrder(1000)]
    public sealed class UnitMorphPoseBlend : MonoBehaviour
    {
        public float seconds=.22f;
        Unit unit;GameObject shape;Animator animator;float elapsed=1;bool initialized;
        readonly Quaternion[] previous=new Quaternion[(int)HumanBodyBones.LastBone];
        readonly Quaternion[] from=new Quaternion[(int)HumanBodyBones.LastBone];
        readonly Quaternion[] desired=new Quaternion[(int)HumanBodyBones.LastBone];
        readonly Transform[] bones=new Transform[(int)HumanBodyBones.LastBone];
        readonly bool[] valid=new bool[(int)HumanBodyBones.LastBone];
        void Awake(){unit=GetComponent<Unit>();}
        void LateUpdate()
        {
            if(!unit||unit.dead)return;
            if(shape!=unit.mainRenderer){
                shape=unit.mainRenderer;animator=unit.animator;
                System.Array.Copy(previous,from,previous.Length);elapsed=initialized?0:seconds;initialized=true;
                for(int i=0;i<bones.Length;i++)bones[i]=animator&&animator.isHuman?animator.GetBoneTransform((HumanBodyBones)i):null;
            }
            elapsed+=Time.deltaTime;float t=Mathf.SmoothStep(0,1,Mathf.Clamp01(elapsed/Mathf.Max(.01f,seconds)));
            for(int i=0;i<bones.Length;i++)if(bones[i])desired[i]=bones[i].rotation;
            for(int i=0;i<bones.Length;i++)if(bones[i]){
                if(t<1&&valid[i])bones[i].rotation=Quaternion.Slerp(from[i],desired[i],t);
                previous[i]=bones[i].rotation;valid[i]=true;
            }
        }
    }
}
