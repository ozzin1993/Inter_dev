using System;
using UnityEngine;
namespace StrategyCore
{
    [Serializable]
    public class PassiveDefenseBlock
    {
        public bool enabled;
        [Tooltip("Тип входящего урона. Пусто — любой; для физической защиты укажите физический тип.")]
        public DamageType damageType;
        [Tooltip("Дополнительные типы той же категории, например пушечный физический урон.")]
        public DamageType[] additionalDamageTypes;
        public bool onlyDirectAttack = true;
        [Range(0, 360)] public float frontArc = 360f;
        [Range(0, 1)] public float chance = 1f;
        [Range(0, 1)] public float incomingMultiplier = 0.8f;
        [Tooltip("Контрудар долей от базовой атаки. 0 — без контрудара.")]
        public float counterAttackFraction;
        public float counterAttackFlat;
        public DamageType counterDamageType;
        public bool onlyEnemyMelee;
        [Tooltip("Необязательный скилл презентации с выключенными механическими блоками.")]
        public CompositeSkill presentation;
    }
    public partial class CompositePassive
    {
        void ApplyDefense(Unit unit, Carrier carrier)
        {
            if (defense == null || !defense.enabled) return;
            var block = defense;
            carrier.defenseRule = InterflowCombat.IncomingRuleAdd(unit, new InterflowCombat.IncomingRule
            {
                multiplier = Mathf.Clamp01(block.incomingMultiplier), onlyType = block.damageType,
                additionalTypes = block.additionalDamageTypes, onlyDirectAttack = block.onlyDirectAttack, frontArc = Mathf.Clamp(block.frontArc, 0, 360),
                chance = Mathf.Clamp01(block.chance), onlyEnemyMelee=block.onlyEnemyMelee,
                onApplied = (victim, attacker) =>
                {
                    if (NetworkConnectionHandler.isClient || victim == null || victim.dead) return;
                    if (block.presentation != null)
                        EmitSkillFired(victim, block.presentation, carrier.level, attacker, victim.transform.position);
                    if (attacker == null || attacker.dead || block.counterAttackFraction <= 0f && block.counterAttackFlat <= 0f) return;
                    // A reaction is not a direct attack; two parries cannot recurse.
                    var type = block.counterDamageType ? block.counterDamageType : (victim.damageType != null ? victim.damageType : block.damageType);
                    if (type != null) victim.DealDamage(attacker, victim.attackDamage * block.counterAttackFraction + block.counterAttackFlat,
                                                       type, false, attacker.transform.position);
                }
            });
        }
        void RemoveDefense(Unit unit, Carrier carrier)
        {
            if (carrier.defenseRule != null) InterflowCombat.IncomingRuleRemove(unit, carrier.defenseRule);
            carrier.defenseRule = null;
        }
    }
}
