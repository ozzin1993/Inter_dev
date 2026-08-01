using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Контрудар: получив урон, носитель бьёт по кругу вокруг себя.
    /// Потребитель: Тир 2 [А] «Контрудар» (круговая контратака после получения урона).
    ///
    /// Реализация: реакция через <see cref="InterflowCombat"/> (штатный хук жертвы не сообщает, кто ударил,
    /// но для круговой атаки атакующий и не нужен). Ответный урон наносится как НЕ прямая атака —
    /// это и логично (не удар оружием по цели), и исключает бесконечный обмен контрударами между двумя носителями.
    ///
    /// Штатные поля Ability, которые здесь работают: <c>radius</c> — радиус круга, <c>cooldown</c> — откат,
    /// <c>unitSelector</c> — кого задевает (обычно враги).
    /// </summary>
    [CreateAssetMenu(fileName = "Counterattack", menuName = "StrategyCore/Abilities/Interflow/Counterattack (Контрудар)")]
    public class Counterattack : InterflowAbility
    {
        public override AbilityType type { get { return AbilityType.Passive; } }

        [Header("Контрудар")]
        [Tooltip("Урон контрудара как доля от урона атаки носителя, по уровням. 0.5 = половина обычного удара. Работает вместе с «фиксированным уроном» ниже (складываются).")]
        public float[] damagePercentOfAttack = new float[1] { 0.5f };

        [Tooltip("Фиксированный урон контрудара, по уровням. 0 — только доля от атаки.")]
        public float[] flatDamage = new float[1] { 0f };

        [Tooltip("Тип урона контрудара. Пусто — берётся тип атаки носителя.")]
        public DamageType counterDamageType;

        [Tooltip("Отвечать только на прямые атаки (удары юнитов). ВЫКЛ — контрудар пойдёт и на урон от эффекторов, зон и способностей.")]
        public bool onlyOnDirectAttack = true;

        [Tooltip("Эффекторы, накладываемые задетым врагам (например замедление). Пусто — только урон.")]
        public Effector[] counterEffectors;

        [Tooltip("Максимум задетых целей за один контрудар. 0 — без ограничения.")]
        [Min(0)]
        public int maxTargets = 0;

        // Состояние: SO один на всех носителей, поэтому откат хранится по юниту
        readonly Dictionary<Unit, float> cooldownLeft = new Dictionary<Unit, float>();
        readonly Dictionary<Unit, InterflowCombat.DamagedHandler> handlers = new Dictionary<Unit, InterflowCombat.DamagedHandler>();
        bool tickWired;

        public override void Init()
        {
            base.Init();

            // Между матчами состояние не переносим
            cooldownLeft.Clear();
            handlers.Clear();
            UnwireTick();
        }

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null || handlers.ContainsKey(unit)) return;

            int lvl = level;
            InterflowCombat.DamagedHandler h = (victim, attacker, damageType, damageDealt, directAttack) =>
                OnDamaged(victim, attacker, directAttack, lvl);

            handlers[unit] = h;
            InterflowCombat.DamagedListenerAdd(unit, h);
            WireTick();
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null) return;

            if (handlers.TryGetValue(unit, out var h))
            {
                InterflowCombat.DamagedListenerRemove(unit, h);
                handlers.Remove(unit);
            }

            cooldownLeft.Remove(unit);
            if (handlers.Count == 0) UnwireTick();
        }

        void OnDamaged(Unit victim, Unit attacker, bool directAttack, int level)
        {
            if (NetworkConnectionHandler.isClient) return; // правило 6
            if (victim == null || victim.dead) return;
            if (onlyOnDirectAttack && !directAttack) return;
            if (victim.stunned) return;

            // Проверяем НАЛИЧИЕ записи, а не её величину: при cooldown = 0 значение 0 не защитило бы
            // от взаимной рекурсии двух носителей. Запись живёт минимум до следующего тика.
            if (cooldownLeft.ContainsKey(victim)) return;

            float radiusValue = LevelValue(radius, level, 0f);
            if (radiusValue <= 0f) return;

            float damage = LevelValue(flatDamage, level, 0f) + victim.attackDamage * LevelValue(damagePercentOfAttack, level, 0f);
            if (damage <= 0f && (counterEffectors == null || counterEffectors.Length == 0)) return;

            DamageType dt = counterDamageType != null ? counterDamageType : victim.damageType;
            if (dt == null) return;

            // Откат ставим ДО нанесения урона: иначе два носителя контрудара рядом отвечали бы
            // друг другу рекурсивно (наш ответ → его ответ → наш ответ …) до переполнения стека.
            cooldownLeft[victim] = LevelValue(cooldown, level, 0f);

            Vector2 center = new Vector2(victim.transform.position.x, victim.transform.position.z);
            Unit[] targets = Utils.GetUnitsInRadius(center, radiusValue, victim.owner, unitSelector, -1, victim);

            int hit = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null || targets[i].dead) continue;

                // directAttack = false: это не удар оружием, поэтому чужие контрудары в ответ не срабатывают
                if (damage > 0f) targets[i].GetDamage(damage, dt, victim.owner, victim, false, out float _);
                if (counterEffectors != null && counterEffectors.Length > 0) Effector.EffectorAdd(victim, targets[i], counterEffectors);

                hit++;
                if (maxTargets > 0 && hit >= maxTargets) break;
            }

            if (hit > 0)
                InterflowDebug.Verbose("КОНТРУДАР: " + InterflowDebug.Name(victim) + " ответил по " + hit +
                                       " целям на " + damage.ToString("0.#") + " урона (откат " +
                                       LevelValue(cooldown, level, 0f).ToString("0.#") + " с)");

            if (hit > 0) RequestForceSync();
        }

        // ============================= ОТКАТ ==

        void WireTick()
        {
            if (tickWired || GameManager.instance == null) return;

            GameManager.instance.Tick += CooldownTick;
            tickWired = true;
        }

        void UnwireTick()
        {
            if (!tickWired || GameManager.instance == null)
            {
                tickWired = false;
                return;
            }

            GameManager.instance.Tick -= CooldownTick;
            tickWired = false;
        }

        readonly List<Unit> tickBuffer = new List<Unit>();

        void CooldownTick()
        {
            // Чистка мёртвых носителей — ДО раннего выхода: иначе носитель, который ни разу
            // не контратаковал, никогда бы не вычистился, и подписка на Tick жила бы до конца матча
            PruneDeadCarriers();

            if (cooldownLeft.Count == 0) return;

            float dt = GameManager.instance.currentDeltaTime;

            // Ключи копируем: словарь нельзя менять во время обхода
            tickBuffer.Clear();
            tickBuffer.AddRange(cooldownLeft.Keys);

            for (int i = 0; i < tickBuffer.Count; i++)
            {
                Unit u = tickBuffer[i];
                float left = cooldownLeft[u] - dt;

                if (u == null || u.dead || left <= 0f) cooldownLeft.Remove(u);
                else cooldownLeft[u] = left;
            }
        }

        // Unit.Die не зовёт Lock, поэтому мёртвые носители копились бы в handlers,
        // а подписка на Tick никогда не снималась. Чистим лениво, на том же тике.
        void PruneDeadCarriers()
        {
            if (handlers.Count == 0) return;

            tickBuffer.Clear();
            foreach (Unit u in handlers.Keys)
            {
                if (u == null || u.dead) tickBuffer.Add(u);
            }

            for (int i = 0; i < tickBuffer.Count; i++)
            {
                Unit u = tickBuffer[i];
                if (u != null && handlers.TryGetValue(u, out var h)) InterflowCombat.DamagedListenerRemove(u, h);
                handlers.Remove(u);
            }

            if (handlers.Count == 0) UnwireTick();
        }

    }
}
