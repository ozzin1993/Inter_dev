using System;
using UnityEngine;
namespace StrategyCore
{
    [Serializable]
    public class PassiveKillBlock
    {
        public bool enabled;
        [Range(0,1)] public float healMaxHpFraction;
        [Min(0), Tooltip("Прибавка к исходному урону за каждое убийство. 0.1 = +10%, два убийства = +20%.")]
        public float damageFractionPerKill;
        public CompositeSkill presentation;
    }
    public partial class CompositePassive
    {
        MatchManager killHub;
        void WireKillEffect()
        {
            if(NetworkConnectionHandler.isClient || (killEffect==null || !killEffect.enabled) && (allyDeath==null || !allyDeath.enabled) || MatchManager.instance==null)return;
            if(killHub==MatchManager.instance)return;
            UnwireKillEffect();killHub=MatchManager.instance;killHub.OnUnitDeathServer+=OnKillEffect;
        }
        void UnwireKillEffect()
        {
            if(killHub!=null)killHub.OnUnitDeathServer-=OnKillEffect;
            killHub=null;
        }
        void OnKillEffect(Unit victim,int player,Unit killer,bool rewards)
        {
            ApplyAllyDeath(victim);
            if(NetworkConnectionHandler.isClient||victim==null||killer==null||killer.dead||killEffect==null||!killEffect.enabled)return;
            if(victim.unitType!=UnitType.Unit||victim==killer||SlotManager.instance==null||victim.team==killer.team)return;
            if(!carriers.TryGetValue(killer,out var carrier))return;
            var next=carrier.killDamageFraction+Mathf.Max(0,killEffect.damageFractionPerKill);
            if(next>carrier.killDamageFraction)killer.ChangeDamage((1+next)/(1+carrier.killDamageFraction)-1,false);
            carrier.killDamageFraction=next;
            RequestForceSync();
            if(killEffect.healMaxHpFraction>0)killer.ChangeHP(killer.maxHealth*killEffect.healMaxHpFraction);
            if(killEffect.presentation!=null)EmitSkillFired(killer,killEffect.presentation,carrier.level,killer,killer.transform.position);
        }
        void RemoveKillEffect(Unit unit,Carrier carrier)
        {
            if(unit!=null&&carrier.killDamageFraction>0)unit.ChangeDamage(-carrier.killDamageFraction,true);
            carrier.killDamageFraction=0;
        }
    }
}
