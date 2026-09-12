using NUnit.Framework;
using UnityEngine;
namespace StrategyCore.Tests
{
    public class PassiveDefenseTests
    {
        GameObject victimGO, attackerGO;
        Unit victim, attacker;
        DamageType physical, magic;
        [SetUp] public void Setup()
        {
            InterflowCombat.ResetAll();
            victimGO = new GameObject("defense victim"); attackerGO = new GameObject("attacker");
            victim = victimGO.AddComponent<Unit>(); attacker = attackerGO.AddComponent<Unit>();
            physical = ScriptableObject.CreateInstance<DamageType>(); magic = ScriptableObject.CreateInstance<DamageType>();
            attacker.transform.position = Vector3.forward;
        }
        [TearDown] public void Cleanup()
        {
            InterflowCombat.ResetAll(); Object.DestroyImmediate(victimGO); Object.DestroyImmediate(attackerGO);
            Object.DestroyImmediate(physical); Object.DestroyImmediate(magic);
        }
        [Test] public void FrontDefenseRespectsDirectionAndDamageType()
        {
            InterflowCombat.IncomingRuleAdd(victim, new InterflowCombat.IncomingRule
                { onlyType = physical, frontArc = 180, multiplier = .8f });
            Assert.That(InterflowCombat.ModifyIncomingDamage(victim,attacker,physical,100,true),Is.EqualTo(80).Within(.01));
            Assert.That(InterflowCombat.ModifyIncomingDamage(victim,attacker,magic,100,true),Is.EqualTo(100));
            attacker.transform.position = Vector3.back;
            Assert.That(InterflowCombat.ModifyIncomingDamage(victim,attacker,physical,100,true),Is.EqualTo(100));
        }
        [Test] public void ParryDoesNotReactToCounterDamageAndCanBeRemoved()
        {
            int reactions = 0;
            var rule = InterflowCombat.IncomingRuleAdd(victim,new InterflowCombat.IncomingRule
                { onlyDirectAttack = true, multiplier = 0, chance = 1, onApplied = (v,a) => reactions++ });
            Assert.That(InterflowCombat.ModifyIncomingDamage(victim,attacker,physical,100,true),Is.Zero);
            Assert.That(reactions,Is.EqualTo(1));
            Assert.That(InterflowCombat.ModifyIncomingDamage(victim,attacker,physical,100,false),Is.EqualTo(100));
            InterflowCombat.IncomingRuleRemove(victim,rule);
            Assert.That(InterflowCombat.ModifyIncomingDamage(victim,attacker,physical,100,true),Is.EqualTo(100));
        }
        [Test] public void CounterReactionsCannotRecursivelyTriggerThemselves()
        {
            int reactions = 0;
            InterflowCombat.IncomingRuleAdd(victim,new InterflowCombat.IncomingRule
                { onApplied = (v,a) => { reactions++; InterflowCombat.ModifyIncomingDamage(a,v,physical,10,false); } });
            InterflowCombat.IncomingRuleAdd(attacker,new InterflowCombat.IncomingRule
                { onApplied = (v,a) => { reactions++; InterflowCombat.ModifyIncomingDamage(a,v,physical,10,false); } });
            InterflowCombat.ModifyIncomingDamage(victim,attacker,physical,100,true);
            Assert.That(reactions,Is.EqualTo(2));
        }
        [Test] public void RemovingReactionDoesNotSkipTheNextDefenseRule()
        {
            InterflowCombat.IncomingRule rule = null;
            rule = InterflowCombat.IncomingRuleAdd(victim,new InterflowCombat.IncomingRule
                { onApplied = (v,a) => InterflowCombat.IncomingRuleRemove(v,rule) });
            InterflowCombat.IncomingRuleAdd(victim,new InterflowCombat.IncomingRule { multiplier = .5f });
            Assert.That(InterflowCombat.ModifyIncomingDamage(victim,attacker,physical,100,true),Is.EqualTo(50));
        }
        [Test] public void ZeroChanceNeverProcs()
        {
            InterflowCombat.IncomingRuleAdd(victim,new InterflowCombat.IncomingRule { multiplier = 0, chance = 0 });
            for(int i=0;i<20;i++)Assert.That(InterflowCombat.ModifyIncomingDamage(victim,attacker,physical,100,true),Is.EqualTo(100));
        }
    }
}
