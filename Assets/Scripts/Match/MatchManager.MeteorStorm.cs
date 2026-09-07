using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Партиал MatchManager: серверная механика «Ярость Небосвода» (метеоритный дождь ГЗ орков).
    // Централизовано здесь (правило 5): способность SkyfuryStormActive — тонкая обёртка, зовёт StartMeteorStorm.
    // Авто-таргет по врагам (плотность/рандом), урон физ+огонь + оглушение ВСЕМ в зоне (friendly fire).
    // Штатные Utils.GetUnitsInRadius / Unit.DealDamage / Unit.Stun / PlaySummonFx. Серверо-авторитетно (правило 6).
    // Новый partial-файл — ассет StrategyCore не трогается (правило 1).
    public partial class MatchManager
    {
        /// <summary>
        /// Сервер: запустить метеоритный дождь. Каждую секунду × durationSeconds падает meteorsPerSecond метеоров:
        /// цель — вражеский юнит (взвешенно по плотности или равномерно случайно), зона impactRadius; всем в зоне
        /// (вкл. союзников, splashSelector) — физ+огонь урон и оглушение stunSeconds. Точка входа — SkyfuryStormActive.
        /// </summary>
        public void StartMeteorStorm(Unit caster, int castingPlayer,
                                     float durationSeconds, int meteorsPerSecond, float impactRadius,
                                     float physicalDamage, DamageType physicalDamageType,
                                     float fireDamage, DamageType fireDamageType,
                                     float stunSeconds, bool useDensityTargeting,
                                     UnitSelector targetSelector, UnitSelector splashSelector,
                                     VFXReferencer meteorVFX, AudioClip impactSound, float impactVolume,
                                     Ability sourceAbility) // умение-источник для диагностики очереди пакетов (решение Artsiom 05.09.2026); null — без умения
        {
            if (NetworkConnectionHandler.isClient) return; // урон/стан/презентация — только сервер (правило 6)
            if (caster == null) { Debug.LogWarning("[Ярость Небосвода] Нет кастера — дождь пропущен."); return; }
            if (durationSeconds <= 0f || meteorsPerSecond <= 0) { Debug.LogWarning("[Ярость Небосвода] duration/meteorsPerSecond ≤ 0 — дождь пропущен."); return; }
            if (impactRadius <= 0f) { Debug.LogWarning("[Ярость Небосвода] impactRadius ≤ 0 — задай радиус зоны в .asset."); return; }
            if (physicalDamageType == null && fireDamageType == null) { Debug.LogWarning("[Ярость Небосвода] Не задан ни один DamageType — дождь пропущен."); return; }

            int teamIndex = TeamIndexOfOwner(castingPlayer);
            if (teamIndex < 0) { Debug.LogWarning($"[Ярость Небосвода] Игрок {castingPlayer} не владеет командой."); return; }

            StartCoroutine(MeteorStormRoutine(caster, teamIndex, durationSeconds, meteorsPerSecond, impactRadius,
                physicalDamage, physicalDamageType, fireDamage, fireDamageType, stunSeconds, useDensityTargeting,
                targetSelector, splashSelector, meteorVFX, impactSound, impactVolume, sourceAbility));
        }

        IEnumerator MeteorStormRoutine(Unit caster, int teamIndex,
                                       float durationSeconds, int meteorsPerSecond, float impactRadius,
                                       float physicalDamage, DamageType physicalDamageType,
                                       float fireDamage, DamageType fireDamageType,
                                       float stunSeconds, bool useDensityTargeting,
                                       UnitSelector targetSelector, UnitSelector splashSelector,
                                       VFXReferencer meteorVFX, AudioClip impactSound, float impactVolume,
                                       Ability sourceAbility) // умение-источник, протаскивается в DropMeteor
        {
            int enemyTeam = OpponentTeam(teamIndex);
            int batches = Mathf.Max(1, Mathf.RoundToInt(durationSeconds)); // «каждую секунду» × длительность
            int total = 0;
            for (int b = 0; b < batches; b++)
            {
                if (caster == null || caster.dead) { Debug.Log("[Ярость Небосвода] Кастер погиб — дождь прерван."); yield break; }
                for (int m = 0; m < meteorsPerSecond; m++)
                {
                    if (DropMeteor(caster, enemyTeam, impactRadius, physicalDamage, physicalDamageType,
                                   fireDamage, fireDamageType, stunSeconds, useDensityTargeting,
                                   targetSelector, splashSelector, meteorVFX, impactSound, impactVolume, sourceAbility))
                        total++;
                }
                yield return new WaitForSeconds(1f);
            }
            Debug.Log($"[Ярость Небосвода] Завершено: team {teamIndex}, метеоров с целью {total} из {batches * meteorsPerSecond}.");
        }

        // Один метеор: выбрать цель среди живых врагов (взвешенно по плотности / равномерно) и накрыть зону.
        // Возвращает true, если была цель (метеор упал). Нет врагов → метеор пропускается.
        bool DropMeteor(Unit caster, int enemyTeam, float impactRadius,
                        float physicalDamage, DamageType physicalDamageType,
                        float fireDamage, DamageType fireDamageType,
                        float stunSeconds, bool useDensityTargeting,
                        UnitSelector targetSelector, UnitSelector splashSelector,
                        VFXReferencer meteorVFX, AudioClip impactSound, float impactVolume,
                        Ability sourceAbility) // умение-источник, уходит в пакет через DealDamage
        {
            List<Unit> alive = new List<Unit>();
            List<Unit> enemies = GetGroupUnits(enemyTeam);
            for (int i = 0; i < enemies.Count; i++)
                if (enemies[i] != null && !enemies[i].dead) alive.Add(enemies[i]);
            if (alive.Count == 0) return false;

            Unit target;
            if (useDensityTargeting)
            {
                // Вес = число врагов в радиусе зоны вокруг кандидата (плотность). Взвешенно-случайный выбор:
                // плотные участки бьются чаще, но 4 метеора/сек не падают в одну точку (не вырождается).
                float[] weights = new float[alive.Count];
                for (int i = 0; i < alive.Count; i++)
                {
                    Vector3 p = alive[i].transform.position;
                    Unit[] near = Utils.GetUnitsInRadius(new Vector2(p.x, p.z), impactRadius, caster.owner, targetSelector);
                    weights[i] = (near != null && near.Length > 0) ? near.Length : 1f;
                }
                target = alive[WeightedPick(weights)];
            }
            else
            {
                target = alive[Random.Range(0, alive.Count)];
            }

            Vector3 center = target.transform.position;

            // Презентация метеора (VFX+звук) в точке удара — штатный PlaySummonFx (серверо-онли, как VFX CallToArms/FlameCloak).
            PlaySummonFx(target.transform, meteorVFX, impactSound, impactVolume);

            // Зона поражения: урон физ+огонь и оглушение ВСЕМ в зоне (splashSelector = все, friendly fire).
            Unit[] inZone = Utils.GetUnitsInRadius(new Vector2(center.x, center.z), impactRadius, caster.owner, splashSelector);
            if (inZone != null)
            {
                for (int i = 0; i < inZone.Length; i++)
                {
                    Unit u = inZone[i];
                    if (u == null || u.dead) continue;
                    if (physicalDamageType != null && physicalDamage > 0f) caster.DealDamage(u, physicalDamage, physicalDamageType, false, center, sourceAbility);
                    if (fireDamageType != null && fireDamage > 0f)         caster.DealDamage(u, fireDamage, fireDamageType, false, center, sourceAbility);
                    if (stunSeconds > 0f) u.Stun(stunSeconds, caster, caster.owner);
                }
            }
            return true;
        }

        // Взвешенно-случайный индекс по массиву весов (сумма > 0). Фоллбэк — равномерно.
        static int WeightedPick(float[] weights)
        {
            float sum = 0f;
            for (int i = 0; i < weights.Length; i++) sum += weights[i];
            if (sum <= 0f) return Random.Range(0, weights.Length);
            float r = Random.value * sum;
            for (int i = 0; i < weights.Length; i++) { r -= weights[i]; if (r <= 0f) return i; }
            return weights.Length - 1;
        }
    }
}
