using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Иммунитет к замедлениям: эффекторы, снижающие скорость передвижения, снимаются с носителя
    /// в тот же тик, что и наложены.
    /// Потребитель: Тир 5 [Б] Флагеллант-Всадник («игнорирует любые замедления»).
    ///
    /// Почему снятие, а не блокировка: наложение эффекторов идёт через статический
    /// <c>Effector.EffectorAdd</c> — точки перехвата в ассете нет, а править её пришлось бы в ядре.
    /// Снятие на тике даёт тот же результат: замедление не успевает подействовать дольше одного тика (0.1 с).
    ///
    /// Оглушение и обездвиживание сюда НЕ входят — это отдельная механика (<see cref="ControlImmunity"/>).
    /// </summary>
    [CreateAssetMenu(fileName = "SlowImmunity", menuName = "StrategyCore/Abilities/Interflow/SlowImmunity (Иммунитет к замедлениям)")]
    public class SlowImmunity : InterflowAbility
    {
        public override AbilityType type { get { return AbilityType.Passive; } }

        [Header("Иммунитет к замедлениям")]
        [Tooltip("Снимать эффекторы, у которых снижена скорость передвижения. ВЫКЛ — способность ничего не делает.")]
        public bool removeSlowEffectors = true;

        [Tooltip("Также снимать эффекторы, снижающие скорость АТАКИ.")]
        public bool alsoRemoveAttackSlow = false;

        readonly HashSet<Unit> carriers = new HashSet<Unit>();
        readonly List<Unit> tickBuffer = new List<Unit>();
        bool tickWired;

        public override void Init()
        {
            base.Init();

            carriers.Clear();
            if (tickWired && GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            tickWired = false;
        }

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null) return;

            carriers.Add(unit);

            if (!tickWired && GameManager.instance != null)
            {
                GameManager.instance.Tick += OnTick;
                tickWired = true;
            }
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null) return;

            carriers.Remove(unit);

            if (carriers.Count == 0 && tickWired && GameManager.instance != null)
            {
                GameManager.instance.Tick -= OnTick;
                tickWired = false;
            }
        }

        void OnTick()
        {
            if (NetworkConnectionHandler.isClient) return; // снятие эффектов — сервер (правило 6)
            if (!removeSlowEffectors || carriers.Count == 0) return;

            tickBuffer.Clear();
            tickBuffer.AddRange(carriers);

            for (int i = 0; i < tickBuffer.Count; i++)
            {
                Unit u = tickBuffer[i];
                if (u == null || u.dead)
                {
                    carriers.Remove(u);
                    continue;
                }

                if (u.effectors == null || u.effectors.Count == 0) continue;

                for (int e = u.effectors.Count - 1; e >= 0; e--)
                {
                    EffectorHolder eh = u.effectors[e];
                    if (eh == null || eh.effector == null || !eh.effector.passiveEffectsOn) continue;

                    AbilityPassiveEffects pe = eh.effector.passiveEffects;
                    if (pe == null) continue;

                    bool slowsMovement = pe.moveSpeedChange < 0f || pe.moveSpeedPercentageChange < 0f;
                    bool slowsAttack = alsoRemoveAttackSlow && (pe.attackSpeedChange < 0f || pe.attackSpeedPercentageChange < 0f);

                    if (!slowsMovement && !slowsAttack) continue;

                    Effector.EffectorRemove(u, eh);
                }
            }
        }
    }
}
