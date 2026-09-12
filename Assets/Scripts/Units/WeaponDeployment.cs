using UnityEngine;
namespace StrategyCore
{
    /// <summary>Optional stationary setup before firing; moving resets deployment.</summary>
    public sealed class WeaponDeployment : MonoBehaviour
    {
        [Min(0)] public float seconds = 1.75f;
        [Tooltip("Значок и VFX подготовки, снимаемые при движении или завершении.")]
        public Effector deploymentVisual;
        Unit unit;
        Vector3 lastPosition;
        bool preparing, ready;
        float finishAt;
        EffectorHolder visual;
        void Awake() { unit=GetComponent<Unit>();lastPosition=transform.position;if(unit)unit.OnDie+=OnUnitDeath; }
        void OnUnitDeath(Unit who,int killer,Unit killerUnit,bool rewards){ResetPreparation();}
        void OnDestroy(){if(unit)unit.OnDie-=OnUnitDeath;}
        void Update()
        {
            if (unit == null || unit.dead) { ResetPreparation();return; }
            Vector3 delta=transform.position-lastPosition;delta.y=0;
            if(delta.sqrMagnitude>.0001f) ResetPreparation();
            lastPosition=transform.position;
        }
        public bool ReadyToFire()
        {
            if(unit==null||unit.dead){ResetPreparation();return false;}
            Vector3 delta=transform.position-lastPosition;delta.y=0;
            if(delta.sqrMagnitude>.0001f){ResetPreparation();lastPosition=transform.position;}
            if (seconds <= 0 || ready) return true;
            if (!preparing)
            {
                preparing=true;finishAt=Time.time+seconds;
                if(deploymentVisual!=null)
                {
                    Effector.EffectorAdd(unit,deploymentVisual,unit,unit.owner,durationOverride:seconds);
                    visual=unit.effectors.Find(e=>e.effector==deploymentVisual);
                }
            }
            if(Time.time<finishAt)return false;
            ready=true;ClearVisual();return true;
        }
        void ClearVisual(){if(unit!=null&&visual!=null)Effector.EffectorRemove(unit,visual);visual=null;}
        void ResetPreparation(){preparing=false;ready=false;ClearVisual();}
        void OnDisable(){ResetPreparation();}
    }
}
