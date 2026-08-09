using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Добавляет эффекторы к атакам носителя (поверх тех, что уже стоят на префабе).
    /// Потребители: Тир 1 [Б] «Психологическое подавление» (замедление горящей цели),
    /// Тир 4 [А] «Осколочные снаряды» (замедление пехоты в зоне взрыва).
    ///
    /// Зачем: специализация должна ДОБАВИТЬ эффект к атаке уже открытого юнита, не подменяя префаб.
    /// Штатное поле <c>Unit.attackEffectors</c> — обычный массив, применяется и к цели, и к целям сплеша,
    /// и к целям снаряда, поэтому достаточно дописать в него запись при разблокировке технологии.
    ///
    /// Открывается технологией: положить в <c>abilities[]</c> префаба, заполнить <c>Required Tech</c>.
    /// </summary>
    public class ExtraAttackEffectors : InterflowAbility
    {
        public override AbilityType type { get { return AbilityType.Passive; } }

        [Header("Эффекторы к атакам")]
        [Tooltip("Эффекторы, которые добавятся к атакам носителя. Накладываются на цель удара, на цели сплеша и на цели снаряда — как штатные Attack Effectors.")]
        public Effector[] effectors;

        // Исходный набор эффекторов юнита, чтобы корректно вернуть его при Lock
        readonly Dictionary<Unit, Effector[]> originalEffectors = new Dictionary<Unit, Effector[]>();

        public override void Init()
        {
            base.Init();
            originalEffectors.Clear();
        }

        // Unit.Die не зовёт Lock — мёртвые ключи убираем лениво, при очередной разблокировке
        void PruneDead()
        {
            if (originalEffectors.Count == 0) return;

            List<Unit> dead = null;
            foreach (Unit u in originalEffectors.Keys)
            {
                if (u == null || u.dead) (dead ?? (dead = new List<Unit>())).Add(u);
            }

            if (dead == null) return;
            for (int i = 0; i < dead.Count; i++) originalEffectors.Remove(dead[i]);
        }

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            PruneDead();

            if (unit == null || effectors == null || effectors.Length == 0) return;
            if (originalEffectors.ContainsKey(unit)) return; // уже добавлено

            Effector[] current = unit.attackEffectors != null ? unit.attackEffectors : new Effector[0];
            originalEffectors[unit] = current;

            List<Effector> merged = new List<Effector>(current);
            for (int i = 0; i < effectors.Length; i++)
            {
                if (effectors[i] == null) continue;
                if (merged.Contains(effectors[i])) continue;

                merged.Add(effectors[i]);
            }

            unit.attackEffectors = merged.ToArray();

            InterflowDebug.Event("ЭФФЕКТЫ АТАКИ расширены у " + InterflowDebug.Name(unit) + ": было " +
                                 current.Length + ", стало " + unit.attackEffectors.Length +
                                 " (способность " + name + ")");
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null) return;
            if (!originalEffectors.TryGetValue(unit, out Effector[] original)) return;

            unit.attackEffectors = original;

            InterflowDebug.Event("ЭФФЕКТЫ АТАКИ возвращены к исходным у " + InterflowDebug.Name(unit) +
                                 " (способность " + name + " выключена)");
            originalEffectors.Remove(unit);
        }
    }
}
