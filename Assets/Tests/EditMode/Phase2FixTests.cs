using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StrategyCore.Tests
{
    public class Phase2FixTests
    {
        readonly List<Object> objects = new List<Object>();
        Unit Make(float x, float health = 100f)
        {
            var go = new GameObject("Проверка фазы 2") { hideFlags = HideFlags.HideAndDontSave };
            objects.Add(go);
            var unit = go.AddComponent<Unit>();
            unit.transform.position = new Vector3(x, 0, 0);
            unit.health = health;
            unit.maxHealth = 100;
            return unit;
        }
        T Asset<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            objects.Add(asset);
            return asset;
        }
        [TearDown] public void Cleanup()
        {
            foreach (var obj in objects) if (obj != null) Object.DestroyImmediate(obj);
            objects.Clear();
        }
        [Test] public void БлижайшийНижеПорога_НеСамыйРаненый_ИНеРовноПорог()
        {
            Unit boundary = Make(1, 30), nearby = Make(2, 29), wounded = Make(10, 1);
            Assert.AreSame(nearby, SkillTargeting.Pick(SkillTargetStrategy.NearestBelowThreshold,
                new[] { boundary, wounded, nearby }, null,
                new SkillTargeting.Options { origin = Vector3.zero, hpThreshold = .3f }));
            Assert.IsNull(SkillTargeting.NearestBelowThreshold(new[] { boundary }, null, Vector3.zero, .3f));
        }
        [Test] public void УронВСекунду_УчитываетИнтервал_НеВыбираетОтключённуюАтаку()
        {
            Unit slow = Make(1), fast = Make(2), disabled = Make(3);
            slow.canAttack = fast.canAttack = true;
            slow.attackDamage = 100; slow.attackSpeed = 10;
            fast.attackDamage = 30; fast.attackSpeed = 1;
            disabled.canAttack = false; disabled.attackDamage = 10000;
            Assert.AreSame(fast, SkillTargeting.HighestDps(new[] { slow, disabled, fast }, null));
            Assert.IsNull(SkillTargeting.HighestDps(null, null));
        }
        [Test] public void Уязвимость_ТолькоЗаданныйТип_СнятиеСразуУбираетВклад()
        {
            Unit unit = Make(0);
            DamageType magic = Asset<DamageType>(), physical = Asset<DamageType>();
            Effector effector = Asset<Effector>();
            effector.incomingDamageType = magic; effector.incomingDamageMultiplier = 1.15f;
            unit.effectors.Add(new EffectorHolder(effector, null, 0));
            Assert.AreEqual(1.15f, UnitReceiver.EffectorDamageMultiplier(unit, magic), .0001f);
            Assert.AreEqual(1f, UnitReceiver.EffectorDamageMultiplier(unit, physical));
            unit.effectors.Clear();
            Assert.AreEqual(1f, UnitReceiver.EffectorDamageMultiplier(unit, magic));
        }
        [Test] public void Уязвимость_ПовторноеНаложениеНеПеремножаетОдинаковыйМножитель()
        {
            Unit unit = Make(0);
            Effector effector = Asset<Effector>(); effector.incomingDamageMultiplier = 1.15f;
            unit.effectors.Add(new EffectorHolder(effector, null, 0));
            unit.effectors.Add(new EffectorHolder(effector, null, 1));
            Assert.AreEqual(1.15f, UnitReceiver.EffectorDamageMultiplier(unit, null), .0001f);
        }
    }
}
