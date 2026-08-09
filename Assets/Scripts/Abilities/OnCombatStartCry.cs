using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Клич при вступлении в бой: первый удар после затишья бафает союзников вокруг.
    /// Потребитель: Тир 5 [Б] «Фанатичный клич» (+скорость атаки союзникам 1–2 тира рядом).
    ///
    /// «Вступление в бой» определяется по факту первой атаки после паузы: отдельного события
    /// «начал бой» в ассете нет, а состояние <c>unitState</c> меняется и при простом марше.
    /// Пауза и откат настраиваются в Inspector.
    /// </summary>
    public class OnCombatStartCry : InterflowAbility
    {
        public override AbilityType type { get { return AbilityType.Passive; } }

        [Header("Клич при вступлении в бой")]
        [Tooltip("Эффекторы союзникам вокруг (например +15% скорости атаки на 6 сек).")]
        public Effector[] cryEffectors;

        [Tooltip("Сколько секунд без атак считается «затишьем»: следующий удар после такой паузы засчитывается как вступление в бой.")]
        public float calmSeconds = 5f;

        [Tooltip("Собственный откат клича, сек. Считается отдельно от паузы: клич не сработает чаще, чем раз в это время.")]
        public float cryCooldown = 12f;

        [Header("Кого бафать")]
        [Tooltip("Минимальный тир союзника (поле tier на юните). Союзники ниже — не получают клич.")]
        public int minAllyTier = 1;

        [Tooltip("Максимальный тир союзника. Например 1–2 — только младшие юниты.")]
        public int maxAllyTier = 2;

        [Tooltip("Бафать и самого носителя.")]
        public bool includeSelf = false;

        class CryState
        {
            public float sinceLastAttack;
            public float cooldownLeft;
        }

        readonly Dictionary<Unit, CryState> states = new Dictionary<Unit, CryState>();
        readonly List<Unit> tickBuffer = new List<Unit>();
        bool tickWired;

        public override void Init()
        {
            base.Init();

            states.Clear();
            if (tickWired && GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            tickWired = false;
        }

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null || states.ContainsKey(unit)) return;

            states[unit] = new CryState { sinceLastAttack = calmSeconds }; // первый бой сразу считается вступлением

            unit.OnAfterDamageDealCallbacks.Add(new AfterDamageDealCallback
            {
                Callback = OnAttack,
                Ability = this,
                Level = level
            });

            if (!tickWired && GameManager.instance != null)
            {
                GameManager.instance.Tick += OnTick;
                tickWired = true;
            }
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null) return;

            states.Remove(unit);

            for (int i = 0; i < unit.OnAfterDamageDealCallbacks.Count; i++)
            {
                var c = unit.OnAfterDamageDealCallbacks[i];
                if (c.Ability == this && c.Level == level)
                {
                    unit.OnAfterDamageDealCallbacks.RemoveAt(i);
                    break;
                }
            }

            if (states.Count == 0 && tickWired && GameManager.instance != null)
            {
                GameManager.instance.Tick -= OnTick;
                tickWired = false;
            }
        }

        void OnAttack(Unit targetUnit, Vector3 targetPosition, Effector[] effectors, float dmg, bool directAttack,
                      DamageType damageType, Unit byUnit, Projectile byProjectile, int byOwner, int level)
        {
            if (NetworkConnectionHandler.isClient) return; // правило 6
            if (byUnit == null || !directAttack) return;
            if (!states.TryGetValue(byUnit, out CryState st)) return;

            bool enteringCombat = st.sinceLastAttack >= calmSeconds && st.cooldownLeft <= 0f;
            st.sinceLastAttack = 0f;

            if (!enteringCombat) return;

            st.cooldownLeft = cryCooldown;
            Cry(byUnit, level);
        }

        void Cry(Unit crier, int level)
        {
            if (cryEffectors == null || cryEffectors.Length == 0) return;

            float r = LevelValue(radius, level, 0f);
            if (r <= 0f) return;

            Vector2 pos = new Vector2(crier.transform.position.x, crier.transform.position.z);
            Unit[] allies = Utils.GetUnitsInRadius(pos, r, crier.owner, unitSelector, -1, includeSelf ? null : crier);
            if (allies == null) return;

            for (int i = 0; i < allies.Length; i++)
            {
                Unit a = allies[i];
                if (a == null || a.dead) continue;
                if (a.tier < minAllyTier || a.tier > maxAllyTier) continue;

                Effector.EffectorAdd(crier, a, cryEffectors);
            }
        }

        void OnTick()
        {
            if (NetworkConnectionHandler.isClient || GameManager.instance == null) return;
            if (states.Count == 0) return;

            float dt = GameManager.instance.currentDeltaTime;

            tickBuffer.Clear();
            tickBuffer.AddRange(states.Keys);

            for (int i = 0; i < tickBuffer.Count; i++)
            {
                Unit u = tickBuffer[i];
                if (u == null || u.dead)
                {
                    states.Remove(u);
                    continue;
                }

                CryState st = states[u];
                st.sinceLastAttack += dt;
                if (st.cooldownLeft > 0f) st.cooldownLeft -= dt;
            }
        }

    }
}
