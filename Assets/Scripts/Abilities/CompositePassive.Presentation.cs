using UnityEngine;
namespace StrategyCore
{
    public partial class CompositePassive
    {
        void ApplyAttackPresentation(Unit unit,Carrier carrier)
        {
            if(attackPresentation==null)return;
            carrier.attackVisualCallback=new AfterDamageDealCallback{Ability=this,Level=carrier.level,Callback=PresentAttack};
            unit.OnAfterDamageDealCallbacks.Add(carrier.attackVisualCallback);
        }
        void RemoveAttackPresentation(Unit unit,Carrier carrier)
        {
            if(carrier.attackVisualCallback.Callback!=null)unit.OnAfterDamageDealCallbacks.Remove(carrier.attackVisualCallback);
            carrier.attackVisualCallback=default;
        }
        void PresentAttack(Unit target,Vector3 point,Effector[] effectors,float damage,bool direct,
            DamageType type,Unit source,Projectile projectile,int owner,int level)
        {
            if(NetworkConnectionHandler.isClient||!direct||damage<=0||source==null||attackPresentation==null)return;
            if(!carriers.ContainsKey(source))return;
            EmitSkillFired(source,attackPresentation,level,target,point);
        }
    }
}
