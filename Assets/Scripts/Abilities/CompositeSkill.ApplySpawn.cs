using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // CompositeSkill.ApplySpawn.cs — призыв, зона на земле, серверный сервис (блоки 9-11). Вырезано 1:1 из CompositeSkill.Blocks.cs (разрезка на partial-ы, правило 22).
    public partial class CompositeSkill
    {
        // ----------------------------------------------------------------- 9. ПРИЗЫВ --
        void ApplySummon(Unit castingUnit, int castingPlayer, int level)
        {
            MatchManager mm = MatchManager.Instance;
            if (mm == null)
            {
                Debug.LogWarning($"[{name}] Призыв: MatchManager.Instance == null — пропуск.");
                if (InterflowDebug.FullOn) LogSummonSkipped(castingUnit, "нет менеджера матча");
                return;
            }

            int count = Mathf.RoundToInt(LevelValue(summon.count, level));
            float lifetime = LevelValue(summon.lifetime, level);

            // Фактически призванное число: два режима из трёх его возвращают (лимит живых, пустой префаб).
            // −1 — режим числа не отдаёт («дубль последней волны»), в лог оно тогда не идёт.
            int spawned = -1;

            switch (summon.mode)
            {
                case SkillSummonMode.FromCaster:
                    if (castingUnit == null)
                    {
                        Debug.LogWarning($"[{name}] Призыв от кастера: нет кастера — пропуск.");
                        if (InterflowDebug.FullOn) LogSummonSkipped(castingUnit, "режим «от кастера», а кастера нет");
                        return;
                    }
                    spawned = mm.SummonFromUnit(castingUnit, summon.prefab, count, lifetime,
                                      summon.obeyCommands, summon.command,
                                      summon.maxAlivePerCaster, summon.killSummonsOnOwnerDeath, summon.spawnSpread);
                    break;

                case SkillSummonMode.FixedSquadAtEnemyPoint:
                    spawned = mm.SummonFixedSquadAtEnemyPoint(castingPlayer, summon.prefab, count, summon.maxAlivePerPlayer,
                                                    summon.spawnSpread, summon.summonVFX,
                                                    summon.summonSound, summon.summonSoundVolume);
                    break;

                case SkillSummonMode.LastWave:
                    mm.SummonLastWave(castingPlayer, lifetime, summon.obeyCommands, summon.command,
                                      summon.summonVFX, summon.summonSound, summon.summonSoundVolume);
                    break;
            }

            if (InterflowDebug.FullOn) LogSummon(castingUnit, count, lifetime, spawned);
        }

        // -------------------------------------------------------------------- 10. ЗОНА --
        /// <param name="casterMoved">[Interflow 2026-09-18] Состоялся ли перенос кастера блоком 18.
        /// Режим «шлейф по пути» (решение Artsiom 56) без него не работает: прямой не существует.</param>
        /// <param name="moveStart">Точка, откуда кастер стартовал — начало прямой шлейфа.</param>
        /// <param name="moveLanding">Точка приземления — конец прямой шлейфа.</param>
        void ApplyGroundZone(Unit castingUnit, int castingPlayer, int level, Vector3 origin,
                             bool casterMoved, Vector3 moveStart, Vector3 moveLanding)
        {
            if (groundZone.zonePrefab == null)
            {
                Debug.LogWarning($"[{name}] Зона на земле: не задан префаб — каст пропущен.");
                return;
            }

            // [Interflow 2026-09-18, решение Artsiom 56] ШЛЕЙФ ПО ПУТИ РЫВКА — режим этого блока.
            // Зоны ложатся по прямой «старт → приземление» с шагом trailSpacing, во времени, за время
            // полёта блока 18. Точка прицела, количество и разброс в этом режиме не участвуют.
            if (groundZone.trailAlongCasterPath)
            {
                if (!casterMoved)
                {
                    Debug.LogWarning($"[{name}] Шлейф по пути: кастер не переместился (блок 18 выключен, " +
                                     "нет места на навигационной сетке или иммунитет) — шлейфу негде лечь.");
                    return;
                }

                if (groundZone.trailSpacing <= 0f)
                {
                    Debug.LogWarning($"[{name}] Шлейф по пути: шаг между зонами ≤ 0 — шлейф не выложен.");
                    return;
                }

                MatchManager mmTrail = MatchManager.Instance;
                if (mmTrail == null)
                {
                    Debug.LogWarning($"[{name}] Шлейф по пути: MatchManager.Instance == null — пропуск.");
                    return;
                }

                // Интервал считается из времени полёта блока 18 (решение 56): последняя зона ложится
                // ровно к приземлению модели. Нулевое время полёта — весь шлейф встаёт одним кадром.
                float travel = casterMove != null ? casterMove.travelSeconds : 0f;

                mmTrail.StartSkillTrail(castingUnit, castingPlayer, level, this,
                                        moveStart, moveLanding, groundZone.trailSpacing, travel);
                return;
            }

            // Аура на время (блок Б7, 2026-09-05): одна зона в позиции кастера, дальше она идёт за ним сама
            // (GroundDamageZone.SetCarrier). Точка прицела, разброс и смещение здесь не участвуют.
            if (groundZone.followCaster)
            {
                if (castingUnit == null)
                {
                    Debug.LogWarning($"[{name}] Зона идёт за кастером, но кастера нет — каст пропущен.");
                    return;
                }

                SpawnGroundZone(castingUnit.transform.position, castingUnit, castingPlayer, level);
                return;
            }

            Vector3 center = origin;
            if (castingUnit != null && groundZone.forwardOffset != 0f)
                center += castingUnit.transform.forward * groundZone.forwardOffset;

            for (int i = 0; i < groundZone.zoneCount; i++)
            {
                Vector3 pos = center;
                if (groundZone.zoneCount > 1 && groundZone.spread > 0f)
                {
                    Vector2 offset = UnityEngine.Random.insideUnitCircle * groundZone.spread;
                    pos += new Vector3(offset.x, 0f, offset.y);
                }

                SpawnGroundZone(pos, null, castingPlayer, level);
            }
        }

        /// <summary>Одна зона: префаб, владелец, носитель (null — зона стоит в точке), запись в серверный реестр.
        /// [Interflow 2026-09-18] internal, а не private: этим же методом выкладывает зоны шлейф
        /// (Match/MatchManager.SkillTrail.cs) — второй точки спавна зоны в проекте быть не должно (правило 5).</summary>
        internal void SpawnGroundZone(Vector3 pos, Unit carrier, int castingPlayer, int level)
        {
            GameObject go = Instantiate(groundZone.zonePrefab, pos, Quaternion.identity);

            GroundDamageZone zone = go.GetComponent<GroundDamageZone>();
            if (zone != null)
            {
                zone.SetOwner(castingPlayer);
                zone.SetSource(this);
                if (carrier != null) zone.SetCarrier(carrier);
            }
            else Debug.LogWarning($"[{name}] На префабе зоны нет компонента GroundDamageZone — зона не будет действовать.");

            // Клиенты узнают о зоне фактом из серверного реестра: позиция уже с учётом разброса,
            // префаб клиент берёт из этого же ассета по id умения. Реестр сам разошлёт сообщение;
            // носителя он же передаст клиенту, и тот повесит копию зоны на юнит.
            if (MatchManager.Instance != null) MatchManager.Instance.RegisterGroundZone(go, id, level, pos, carrier);
        }

        // -------------------------------------------------------- 11. СЕРВЕРНЫЙ СЕРВИС --
        void ApplyDelegate(Unit castingUnit, int castingPlayer, Vector3 origin)
        {
            MatchManager mm = MatchManager.Instance;
            if (mm == null) { Debug.LogWarning($"[{name}] Серверный сервис: MatchManager.Instance == null — пропуск."); return; }

            switch (delegateService.service)
            {
                case SkillServerService.MeteorStorm:
                    mm.StartMeteorStorm(castingUnit, castingPlayer,
                        delegateService.meteorDurationSeconds, delegateService.meteorsPerSecond,
                        delegateService.meteorImpactRadius,
                        delegateService.meteorPhysicalDamage, delegateService.meteorPhysicalDamageType,
                        delegateService.meteorFireDamage, delegateService.meteorFireDamageType,
                        delegateService.meteorStunSeconds, delegateService.meteorUseDensityTargeting,
                        delegateService.meteorTargetSelector, delegateService.meteorSplashSelector,
                        delegateService.meteorVFX, delegateService.meteorImpactSound, delegateService.meteorImpactVolume, this);
                    break;

                case SkillServerService.ResurrectFromGraves:
                {
                    int team = TeamIndexOfPlayer(castingPlayer);
                    if (team < 0)
                    {
                        Debug.LogWarning($"[{name}] Подъём павших: игрок {castingPlayer} не владеет командой — пропуск.");
                        return;
                    }
                    mm.ResurrectFromGraves(team, origin, delegateService.resurrectRadius, delegateService.resurrectCount,
                                           delegateService.resurrectHighestTierFirst, delegateService.resurrectTemporary,
                                           delegateService.resurrectLifetime, delegateService.resurrectObeyCommands);
                    break;
                }
            }
        }

        /// <summary>
        /// Индекс команды (0 = A, 1 = B) по игроку-владельцу; -1 — игрок не владеет ни одной командой.
        /// Считается через публичный MatchManager.Team(index).ownerPlayer — приватных полей менеджера не трогаем (правило 9).
        /// </summary>
        internal static int TeamIndexOfPlayer(int player)
        {
            MatchManager mm = MatchManager.Instance;
            if (mm == null) return -1;

            if (mm.Team(0) != null && mm.Team(0).ownerPlayer == player) return 0;
            if (mm.Team(1) != null && mm.Team(1).ownerPlayer == player) return 1;
            return -1;
        }
    }
}
