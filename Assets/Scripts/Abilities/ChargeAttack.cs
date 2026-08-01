using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Чардж: заметив врага, всадник разгоняется на него, и ПЕРВЫЙ удар после разгона бьёт сильнее
    /// (с оглушением и/или бафом на себя).
    /// Потребитель: Тир 5 [А] Латный Рыцарь Альянса (+ специализации «Смятие рядов» и «Стальной натиск»).
    ///
    /// Разгон сделан ускорением, а не мгновенным броском: в ассете перемещение серверо-авторитетное
    /// и идёт через NavMeshAgent, телепорт (Warp) пришлось бы синхронизировать отдельно.
    /// Поэтому чардж = временный буст скорости + приказ атаковать цель; бонус срабатывает на первом попадании.
    ///
    /// Штатные поля Ability: <c>cooldown</c> — откат чарджа, <c>castRange</c> — с какой дистанции начинается разгон,
    /// <c>unitSelector</c> — на кого можно чарджить.
    /// </summary>
    [CreateAssetMenu(fileName = "ChargeAttack", menuName = "StrategyCore/Abilities/Interflow/ChargeAttack (Чардж)")]
    public class ChargeAttack : InterflowAbility
    {
        public override AbilityType type { get { return AbilityType.Passive; } }

        [Header("Разгон")]
        [Tooltip("Прибавка к скорости передвижения во время разгона, в долях. 0.8 = +80% скорости.")]
        public float speedBonusPercent = 0.8f;

        [Tooltip("Сколько секунд длится разгон, если цель не достигнута. По истечении чардж считается сорванным.")]
        public float chargeDuration = 3f;

        [Tooltip("Минимальная дистанция до цели, с которой имеет смысл разгоняться. Ближе — чардж не начинается.")]
        public float minChargeDistance = 4f;

        [Header("Удар с разгона")]
        [Tooltip("Во сколько раз сильнее первый удар после разгона, по уровням. 3 = тройной урон.")]
        public float[] impactDamageMultiplier = new float[1] { 3f };

        [Tooltip("Оглушение цели при ударе с разгона, сек. 0 — не оглушать. Уважает иммунитет к контролю.")]
        public float impactStunSeconds = 0f;

        [Tooltip("Оглушать только цели этой категории (например Боец/Стрелок — «пехота»). Снимите галку «Только категория», чтобы оглушать всех.")]
        public Unit.UnitCategory stunOnlyCategory = Unit.UnitCategory.Fighter;

        [Tooltip("Оглушать только выбранную категорию. ВЫКЛ — оглушение применяется к любой цели.")]
        public bool stunOnlySelectedCategory = true;

        [Tooltip("Эффекторы на СЕБЯ после удара с разгона (например +30% брони на 5 сек). Пусто — без бафа.")]
        public Effector[] selfEffectorsAfterImpact;

        [Header("Гейты специализаций (чтобы не плодить второй чардж)")]
        [Tooltip("Технология, включающая ОГЛУШЕНИЕ при ударе с разгона («Смятие рядов»). Пусто — оглушение работает сразу, без условия.")]
        public Technology stunRequiredTech;

        [Tooltip("Технология, включающая БАФ на себя после удара («Стальной натиск»). Пусто — баф работает сразу, без условия.")]
        public Technology selfBuffRequiredTech;

        [Tooltip("Эффекторы на ЦЕЛЬ удара с разгона. Пусто — только урон и оглушение.")]
        public Effector[] targetEffectorsOnImpact;

        // ---- состояние по юниту (SO один на всех носителей) ----
        class ChargeState
        {
            public bool active;
            public float timeLeft;
            public float cooldownLeft;
            public float appliedSpeedBonus;
            public int level;
        }

        readonly Dictionary<Unit, ChargeState> states = new Dictionary<Unit, ChargeState>();
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

            states[unit] = new ChargeState { level = level };

            bool has = false;
            for (int i = 0; i < unit.OnAfterDamageDealCallbacks.Count; i++)
            {
                var c = unit.OnAfterDamageDealCallbacks[i];
                if (c.Ability == this && c.Level == level) { has = true; break; }
            }

            if (!has)
            {
                unit.OnAfterDamageDealCallbacks.Add(new AfterDamageDealCallback
                {
                    Callback = ImpactApply,
                    Ability = this,
                    Level = level
                });
            }

            if (!tickWired && GameManager.instance != null)
            {
                GameManager.instance.Tick += OnTick;
                tickWired = true;
            }
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null) return;

            if (states.TryGetValue(unit, out ChargeState st))
            {
                if (st.active) StopCharge(unit, st);
                states.Remove(unit);
            }

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

        void OnTick()
        {
            if (NetworkConnectionHandler.isClient || GameManager.instance == null) return;
            if (states.Count == 0) return;

            float dt = GameManager.instance.currentDeltaTime;
            List<Unit> dead = null;

            foreach (var pair in states)
            {
                Unit u = pair.Key;
                ChargeState st = pair.Value;

                if (u == null || u.dead)
                {
                    if (dead == null) dead = new List<Unit>();
                    dead.Add(u);
                    continue;
                }

                if (st.cooldownLeft > 0f) st.cooldownLeft -= dt;

                if (st.active)
                {
                    st.timeLeft -= dt;
                    if (st.timeLeft <= 0f)
                    {
                        // Разгон сорван (цель не достигнута) — уходим в откат, иначе юнит
                        // разгонялся бы каждый тик и перебивал приказы игрока
                        StopCharge(u, st);
                        st.cooldownLeft = LevelValue(cooldown, st.level, 0f);
                    }
                    continue;
                }

                if (st.cooldownLeft > 0f) continue;
                TryStartCharge(u, st);
            }

            if (dead != null)
            {
                for (int i = 0; i < dead.Count; i++) states.Remove(dead[i]);
            }
        }

        void TryStartCharge(Unit unit, ChargeState st)
        {
            if (!unit.canMove || unit.stunned) return;

            float triggerRange = LevelValue(castRange, st.level, 0f);
            if (triggerRange <= 0f) return;

            Vector2 pos = new Vector2(unit.transform.position.x, unit.transform.position.z);
            Unit[] enemies = Utils.GetUnitsInRadius(pos, triggerRange, unit.owner, unitSelector, -1, unit);
            if (enemies == null || enemies.Length == 0) return;

            Unit best = null;
            float bestSqr = float.MaxValue;
            float minSqr = minChargeDistance * minChargeDistance;

            for (int i = 0; i < enemies.Length; i++)
            {
                Unit e = enemies[i];
                if (e == null || e.dead) continue;

                float sqr = (e.transform.position - unit.transform.position).sqrMagnitude;
                if (sqr < minSqr) continue; // слишком близко — разгоняться незачем

                if (sqr < bestSqr) { bestSqr = sqr; best = e; }
            }

            if (best == null) return;

            st.active = true;
            st.timeLeft = chargeDuration;

            if (speedBonusPercent != 0f)
            {
                unit.ChangeMoveSpeed(speedBonusPercent, true);
                st.appliedSpeedBonus = speedBonusPercent;
            }

            unit.Attack(best);
        }

        void StopCharge(Unit unit, ChargeState st)
        {
            st.active = false;
            st.timeLeft = 0f;

            if (Mathf.Abs(st.appliedSpeedBonus) > 0.0001f && unit != null)
            {
                unit.ChangeMoveSpeed(-st.appliedSpeedBonus, true);
                st.appliedSpeedBonus = 0f;
            }
        }

        // Первый удар после разгона: усиление, оглушение, бафы
        void ImpactApply(Unit targetUnit, Vector3 targetPosition, Effector[] effectors, float dmg, bool directAttack,
                         DamageType damageType, Unit byUnit, Projectile byProjectile, int byOwner, int level)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (byUnit == null || targetUnit == null || !directAttack) return;
            if (!states.TryGetValue(byUnit, out ChargeState st) || !st.active) return;

            float mult = LevelValue(impactDamageMultiplier, level, 1f);
            if (mult > 1f)
            {
                float extra = dmg * (mult - 1f);
                targetUnit.GetDamage(extra, damageType, byOwner, byUnit, false, out float _);
            }

            bool stunAllowed = !stunOnlySelectedCategory || targetUnit.unitCategory == stunOnlyCategory;
            if (impactStunSeconds > 0f && stunAllowed && TechReady(stunRequiredTech, byOwner)) targetUnit.Stun(impactStunSeconds);

            if (targetEffectorsOnImpact != null && targetEffectorsOnImpact.Length > 0)
                Effector.EffectorAdd(byUnit, targetUnit, targetEffectorsOnImpact);

            if (selfEffectorsAfterImpact != null && selfEffectorsAfterImpact.Length > 0 && TechReady(selfBuffRequiredTech, byOwner))
                Effector.EffectorAdd(byUnit, byUnit, selfEffectorsAfterImpact);

            StopCharge(byUnit, st);
            st.cooldownLeft = LevelValue(cooldown, level, 0f);

            RequestForceSync();
        }

        /// <summary>Технология не задана — условия нет; задана — проверяем через безопасную обёртку (гарды по игроку и словарю).</summary>
        static bool TechReady(Technology tech, int owner)
        {
            return InterflowCombat.IsTechUnlockedSafe(tech, owner);
        }

    }
}
