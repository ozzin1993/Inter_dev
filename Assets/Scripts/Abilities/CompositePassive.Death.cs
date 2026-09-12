using System;
using System.Linq;
using UnityEngine;
namespace StrategyCore
{
    [Serializable]
    public class PassiveDeathBlock
    {
        public bool enabled;
        [Min(0)] public float radius = 3;
        [Min(0)] public float enemyDamage;
        public DamageType damageType;
        public UnitSelector enemySelector = new UnitSelector { isEnemy=true, isUnit=true, isGround=true, isWater=true, isAir=true, includeInvisible=true };
        [Min(0)] public float allyHeal;
        [Range(0,1)] public float allyHealMaxHpFraction;
        public bool nearestAllyOnly = true;
        [Tooltip("0 — искать ближайшего союзника на всей карте.")]
        [Min(0)] public float allySearchRadius;
        public UnitSelector allySelector = new UnitSelector { isOwn=true, isAlly=true, isUnit=true, isGround=true, isWater=true, isAir=true, includeInvisible=true, includeInvulnerable=true };
        public CompositeSkill deathPresentation;
        public CompositeSkill healPresentation;
    }
    public partial class CompositePassive
    {
        void ApplyDeathEffect(Unit unit, Carrier carrier)
        {
            if (deathEffect == null || !deathEffect.enabled) return;
            carrier.deathHandler = (dies, player, killer, rewards) =>
            {
                RemoveDeathEffect(dies, carrier); // Exactly once, including chained explosions.
                if (NetworkConnectionHandler.isClient || dies == null) return;
                var b = deathEffect;
                var point = dies.transform.position;
                if (b.deathPresentation != null) EmitSkillFired(dies,b.deathPresentation,carrier.level,null,point);
                if (b.enemyDamage > 0 && b.damageType != null)
                {
                    var victims=Utils.GetUnitsInRadius(new Vector2(point.x,point.z),b.radius,dies.owner,b.enemySelector,-1,dies);
                    foreach(var target in victims)
                        if(target != null && !target.dead)
                            target.GetDamage(b.enemyDamage,b.damageType,dies.owner,null,false,out _);
                }
                if ((b.allyHeal > 0 || b.allyHealMaxHpFraction > 0) && SlotManager.instance != null)
                {
                    // Snapshot before healing; callbacks may change the unit registry.
                    var allies=SlotManager.instance.unitNetID.Values.Where(u=>u!=null && u!=dies && !u.dead && UnitSelector.IsUnitCompatible(dies.owner,u,b.allySelector)
                        && (b.allySearchRadius<=0 || (u.transform.position-point).sqrMagnitude<=b.allySearchRadius*b.allySearchRadius))
                        .OrderBy(u=>(u.transform.position-point).sqrMagnitude).ToArray();
                    int count=b.nearestAllyOnly?Math.Min(1,allies.Length):allies.Length;
                    for(int i=0;i<count;i++)
                    {
                        var target=allies[i];if(target==null || target.dead)continue;
                        target.ChangeHP(b.allyHeal+target.maxHealth*b.allyHealMaxHpFraction);
                        if(b.healPresentation!=null)EmitSkillFired(dies,b.healPresentation,carrier.level,target,target.transform.position);
                    }
                }
            };
            unit.OnDie += carrier.deathHandler;
        }
        void RemoveDeathEffect(Unit unit,Carrier carrier)
        {
            if(unit!=null && carrier.deathHandler!=null)unit.OnDie-=carrier.deathHandler;
            carrier.deathHandler=null;
        }
    }
}
