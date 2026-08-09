using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Аура, работающая ТОЛЬКО пока носитель движется: каждый тик задевает врагов вокруг
    /// микро-уроном и эффекторами.
    /// Потребитель: Тир 6 [А] «Железная поступь» (каждый шаг Титана бьёт и замедляет пехоту рядом).
    ///
    /// Отличие от штатного <c>EffectorAura</c>: тот раздаёт эффекты постоянно. Здесь условие — движение,
    /// поэтому нужен свой тик (движение определяем по смещению позиции между тиками, а не по состоянию,
    /// чтобы работало и в бою, и в марше).
    /// </summary>
    public class MovementAura : InterflowAbility
    {
        public override AbilityType type { get { return AbilityType.Passive; } }

        [Header("Аура в движении")]
        [Tooltip("Урон в СЕКУНДУ врагам вокруг, пока носитель движется. 0 — только эффекторы.")]
        public float damagePerSecond = 5f;

        [Tooltip("Тип урона ауры. Обязателен, если урон больше нуля.")]
        public DamageType damageType;

        [Tooltip("Эффекторы, накладываемые задетым (например замедление). Длительность эффектора должна быть короткой — он обновляется каждый тик.")]
        public Effector[] effectors;

        [Tooltip("Задевать только юнитов ближнего боя. ВЫКЛ — всех, кто проходит по Unit Selector.")]
        public bool onlyMeleeUnits = false;

        [Tooltip("Минимальное смещение за тик, которое считается движением (мировые единицы). Отсекает дрожание на месте.")]
        public float movementEpsilon = 0.02f;

        readonly Dictionary<Unit, Vector3> lastPositions = new Dictionary<Unit, Vector3>();
        readonly Dictionary<Unit, int> levels = new Dictionary<Unit, int>();
        readonly List<Unit> tickBuffer = new List<Unit>();
        bool tickWired;
        bool damageTypeWarned; // предупреждение о незаданном типе урона — один раз за матч

        public override void Init()
        {
            base.Init();

            lastPositions.Clear();
            levels.Clear();
            damageTypeWarned = false;
            if (tickWired && GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            tickWired = false;
        }

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null || lastPositions.ContainsKey(unit)) return;

            lastPositions[unit] = unit.transform.position;
            levels[unit] = level;

            if (!tickWired && GameManager.instance != null)
            {
                GameManager.instance.Tick += OnTick;
                tickWired = true;
            }
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null) return;

            lastPositions.Remove(unit);
            levels.Remove(unit);

            if (lastPositions.Count == 0 && tickWired && GameManager.instance != null)
            {
                GameManager.instance.Tick -= OnTick;
                tickWired = false;
            }
        }

        void OnTick()
        {
            if (NetworkConnectionHandler.isClient || GameManager.instance == null) return;
            if (lastPositions.Count == 0) return;

            float dt = GameManager.instance.currentDeltaTime;

            tickBuffer.Clear();
            tickBuffer.AddRange(lastPositions.Keys);

            for (int i = 0; i < tickBuffer.Count; i++)
            {
                Unit u = tickBuffer[i];
                if (u == null || u.dead)
                {
                    lastPositions.Remove(u);
                    levels.Remove(u);
                    continue;
                }

                Vector3 now = u.transform.position;
                float moved = (now - lastPositions[u]).magnitude;
                lastPositions[u] = now;

                if (moved < movementEpsilon) continue; // стоит — аура молчит

                int level = levels.TryGetValue(u, out int l) ? l : 0;
                float r = LevelValue(radius, level, 0f);
                if (r <= 0f) continue;

                // Урон без типа молча не наносится — предупреждаем один раз за матч, а не глотаем
                if (damagePerSecond > 0f && damageType == null && !damageTypeWarned)
                {
                    damageTypeWarned = true;
                    Debug.LogWarning("[MovementAura] '" + name + "': задан Damage Per Second, но не задан Damage Type — урон ауры не наносится.");
                }

                Unit[] targets = Utils.GetUnitsInRadius(new Vector2(now.x, now.z), r, u.owner, unitSelector, -1, u);
                if (targets == null) continue;

                for (int t = 0; t < targets.Length; t++)
                {
                    Unit target = targets[t];
                    if (target == null || target.dead) continue;
                    if (onlyMeleeUnits && !target.melee) continue;

                    if (damagePerSecond > 0f && damageType != null)
                        target.GetDamage(damagePerSecond * dt, damageType, u.owner, u, false, out float _);

                    if (target.dead) continue; // погибла от этого же урона — эффекторы на труп не вешаем

                    if (effectors != null && effectors.Length > 0) Effector.EffectorAdd(u, target, effectors);
                }
            }
        }

    }
}
