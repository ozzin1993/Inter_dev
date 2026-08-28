using UnityEngine;

namespace StrategyCore
{
    // Effectors are temporary effects that modify a unit�s parameters, either buffing or nerfing them. They can be applied through attacks or abilities and are commonly used in auras.
    // Effectors appear in the unit�s Status UI and last for a specified duration.

    public class Effector : ScriptableObject
    {
        [EffectorID]
        public int id;

        [Header("Text")]
        public string displayName;
        public Texture2D icon;
        [TextArea(5, 10)]
        public string description;

        [Space(10)]
        [Tooltip("This VFX transform will be added as a child of the unit`s effects holder")]
        public VFXReferencer VFX;
        [Tooltip("Should the VFX appear above the unit`s head")]
        public bool aboveHead;

        [Header("Effects")]
        [Tooltip("Should this effector be permanent and have no duration")]
        public bool permanent = false;
        [Tooltip("After how many seconds this effector should wear off. For aura effectors it should be twice the gameManagerTickRate (0.2s)")]
        public float duration = 0.2f;
        [Tooltip("When the effector stacks, its effects will increase each time it is applied. \nFor example, the Damage Over Time effect caused by an attack will damage twice as much if the unit was attacked twice. \nWhen false, a similar effector can be added only once by the same team")]
        public bool stacks = false; // Abilities that stack are not shown in status

        [Header("Damage Over Time")]
        [Tooltip("How much damage per second is applied to the unit")]
        public int damageAmount;
        [Tooltip("Damage type that is used in damage over time")]
        public DamageType damageType;

        [Header("Invisibility")]
        [Tooltip("Should this unit upon acquiring this effector become invisible")]
        public bool makeInvisible;
        [Tooltip("Carrier of this effector will be revealed by the team that applied the effect")]
        public bool revealInvisible;

        [Header("Passive changes")]
        [Tooltip("Turns on passive effects of the effector")]
        public bool passiveEffectsOn = false;
        [Tooltip("What unit parameters should change when this effector is applied")]
        public AbilityPassiveEffects passiveEffects;

        // Technical
        // private Unit thisUnit; // Current holder of the effector
        // [HideInInspector] public Unit unitOwner; // Which player`s effector is this. If damaging one this player will be seen as a killer
        // [HideInInspector] public int owner = -1; // Which player`s effector is this. If damaging one this player will be seen as a killer
        // float currentTime;
        // 
        // // currentChanges
        // private int currentDamageChange; // Holds current percentage damage change

        // This function is called by the unit that holds this effector currently
        public static void EffectorUpdate(EffectorHolder EH, Unit unitHolder)
        {
            // if (unitHolder == null) return; // Means unit was destroyed

            // Damage
            if (EH.effector.damageAmount != 0)
            {
                // [Interflow fix 2026-08-02 effector-unify] Урон в секунду масштабируется множителем силы наложения.
                unitHolder.GetDamage(EH.effector.damageAmount * EH.powerMultiplier * GameManager.Instance.currentDeltaTime, EH.effector.damageType, EH.owner, EH.unitOwner, false, out float _);
            }

            // If it is a permanent effector, we do not handle removal logic
            if (EH.effector.permanent) return;

            EH.currentTime += GameManager.Instance.currentDeltaTime;

            // REMOVAL OF EFFECTOR
            // [Interflow fix 2026-08-02 effector-unify] Длительность берётся у наложения, а не у общего ассета.
            if (EH.currentTime > EH.duration)
            {
                // Remove the passive effects
                // Снимаем ТЕМ ЖЕ множителем, каким накладывали, иначе статы юнита уплывут.
                if (EH.effector.passiveEffectsOn) EH.effector.passiveEffects.RemoveEffect(unitHolder, EH.powerMultiplier);

                // Remove tha effector
                bool invisibilityAbilityPresent = false;

                for (int i = 0; i < unitHolder.effectors.Count; i++)
                {
                    // Remove the effector
                    if (unitHolder.effectors[i] == EH && SlotManager.Instance.playerTeam[unitHolder.effectors[i].owner] == SlotManager.Instance.playerTeam[EH.owner])
                    {
                        if (EH.effector.VFX != null) unitHolder.RemoveVFX(EH.effector.VFX);
                        unitHolder.effectors.RemoveAt(i);
                    }
                    else
                    {
                        // Check if other abilities are making this unit invisible
                        if (unitHolder.effectors[i].effector.makeInvisible) invisibilityAbilityPresent = true;
                    }
                }

                // Remove invisibility only if there is no other effectors making this unit invisible
                if (!invisibilityAbilityPresent && EH.effector.makeInvisible) unitHolder.SetInvisibility(false);

                // Remove visibility of the unit
                if (EH.effector.revealInvisible) unitHolder.CanBeSeen(false, EH.owner);

                if (!EH.stacks) unitHolder.OnStatusUpdate?.Invoke();

                // [Interflow fix 2026-08-05 unit-status-sync] Эффектор истёк — сообщить клиентам «снят»
                // (фикс §8.6), но только если на юните не осталось других наложений того же эффектора
                // (разная сила/длительность сосуществуют — значок ещё заслужен).
                if ((EH.effector.icon != null || EH.effector.VFX != null) && NetworkDataSync.Instance != null
                    && !HasEffectorWithId(unitHolder, EH.effector.id))
                    NetworkDataSync.Instance.UnitStatusEffectorRemoveSend(unitHolder, EH.effector.id);
            }
        }

        // This function adds the effects of this effector to the unit
        /// <summary>Накладывает эффектор на юнита.</summary>
        /// <param name="powerMultiplier">Множитель силы наложения: масштабирует пассивные изменения статов
        /// и урон в секунду. 1 — ровно то, что записано в ассете.</param>
        /// <param name="durationOverride">Длительность этого наложения, секунды. Значение ≤ 0 — брать из ассета.
        /// У бессрочных эффекторов игнорируется.</param>
        public static void EffectorAdd(Unit unit, Effector effector, Unit unitOwner, int owner, float currentTime = 0,
                                       float powerMultiplier = 1f, float durationOverride = -1f)
        {
            // [Interflow fix 2026-08-06 no-effectors-on-buildings] Решение Artsiom: на здания эффекты
            // не вешаются. Заодно закрывает краш ядра: slow-эффектор звал ChangeMoveSpeed, а у зданий
            // нет NavMeshAgent — EffectorAdd обрывался исключением на полпути (воспроизведено 2026-08-06).
            if (unit.unitType == UnitType.Building) return;

            // [Interflow fix 2026-08-23 no-effectors-on-dead] Решение Artsiom: на мёртвых эффекты
            // не вешаются. Удар, убивший цель, тут же вешал на неё эффекторы атаки (GetDamage убивает
            // синхронно, Die уже снял netID из реестра на всех пирах) — клиентам уходило сообщение
            // канала статусов по снятому netID: «Desync! Unit netID… should exist on client».
            // Одна точка покрывает все боевые вызовы, включая будущие.
            // Разбор: Документы/Аудиты/Анализ_Рассинхрон_Состояние_На_Трупе.md.
            if (unit.dead) return;

            // [Interflow fix 2026-08-02 effector-unify]
            // Раньше здесь правились поля САМОГО ассета (общего для всех носителей): наложение на одного юнита
            // молча меняло эффектор всем остальным и переживало выход из Play Mode. Теперь фактические
            // параметры наложения считаются локально и живут в EffectorHolder.

            // Длительность: переопределение умения, иначе значение ассета. У бессрочного смысла не имеет.
            float duration = (!effector.permanent && durationOverride > 0f) ? durationOverride : effector.duration;
            // Just a check of duration, it should not be less than GameManager.everyFrameAbilityTickRate * 2
            if (duration < GameManager.tickRate * 2) duration = GameManager.tickRate * 2;
            // Make sure invisibility should not stack, will cause a bug
            bool stacks = effector.stacks && !effector.makeInvisible;

            if (!stacks)
            {
                // If similar effector is already added to the unit by the same team, just reset the currentTime
                for (int i = 0; i < unit.effectors.Count; i++)
                {
                    EffectorHolder existing = unit.effectors[i];
                    if (existing.effector.id != effector.id) continue;
                    if (SlotManager.Instance.playerTeam[existing.owner] != SlotManager.Instance.playerTeam[owner]) continue;

                    // Слипаются только ПОЛНОСТЬЮ одинаковые наложения. Разная сила или разная длительность —
                    // разные эффекты: они сосуществуют и суммируются. Так было и до схлопывания ассетов,
                    // когда «замедление на 40 %» и «замедление на 50 %» были разными эффекторами с разными id.
                    if (!Mathf.Approximately(existing.powerMultiplier, powerMultiplier)) continue;
                    if (!Mathf.Approximately(existing.duration, duration)) continue;

                    existing.currentTime = 0;
                    // [Interflow fix 2026-08-05 unit-status-sync] Продление наложения — сообщить клиентам
                    // (единый канал статусов; внутри гейт «только сервер» — локальные ауры клиента не шлют).
                    if ((effector.icon != null || effector.VFX != null) && NetworkDataSync.Instance != null)
                        NetworkDataSync.Instance.UnitStatusEffectorSend(unit, effector.id, duration);
                    return;
                }
            }

            // Add new effector to the unit
            EffectorHolder newEH = new EffectorHolder(effector, unitOwner, owner, duration, stacks, powerMultiplier);
            newEH.currentTime = currentTime;
            unit.effectors.Add(newEH);
            if (newEH.effector.VFX != null) unit.AddVFX(newEH.effector.VFX, newEH.effector.aboveHead);

            // Add passive effects
            if (newEH.effector.passiveEffectsOn) newEH.effector.passiveEffects.AddEffect(unit, powerMultiplier);

            // Make Invisible
            if (newEH.effector.makeInvisible) unit.SetInvisibility(true);

            // If can be seen when invisible, it is used to not hide the renderers
            // Should be applied only by the current player`s team
            if (newEH.effector.revealInvisible) unit.CanBeSeen(true, owner);

            if (!newEH.stacks) unit.OnStatusUpdate?.Invoke();

            // [Interflow fix 2026-08-05 unit-status-sync] Единый канал статусов: отправка «эффектор
            // появился» из ОДНОЙ точки — покрывает атаки, ауры и скиллы одинаково (решение Artsiom
            // 2026-08-05). Шлём только то, что клиенту есть чем показать (значок или VFX);
            // гейт «только сервер» живёт внутри UnitStatusEffectorSend.
            if ((newEH.effector.icon != null || newEH.effector.VFX != null) && NetworkDataSync.Instance != null)
                NetworkDataSync.Instance.UnitStatusEffectorSend(unit, newEH.effector.id, duration);
        }

        // Add Effector[] by unitOwner
        public static void EffectorAdd(Unit ownerUnit, Unit targetUnit, Effector[] effector)
        {
            for (int i = 0; i < effector.Length; i++)
            {
                EffectorAdd(targetUnit, effector[i], ownerUnit, ownerUnit.owner);
            }
        }

        // Add Effector[] by owner only
        public static void EffectorAdd(int owner, Unit targetUnit, Effector[] effector)
        {
            for (int i = 0; i < effector.Length; i++)
            {
                EffectorAdd(targetUnit, effector[i], null, owner);
            }
        }

        // Removes specified effector from a unit
        public static void EffectorRemove(Unit unitHolder, EffectorHolder EH)
        {
            // Remove the passive effects
            // [Interflow fix 2026-08-02 effector-unify] Снимаем тем же множителем, каким накладывали.
            if (EH.effector.passiveEffectsOn) EH.effector.passiveEffects.RemoveEffect(unitHolder, EH.powerMultiplier);

            // Remove tha effector
            bool invisibilityAbilityPresent = false;

            for (int i = 0; i < unitHolder.effectors.Count; i++)
            {
                // Remove the effector
                if (unitHolder.effectors[i] == EH && SlotManager.Instance.playerTeam[unitHolder.effectors[i].owner] == SlotManager.Instance.playerTeam[EH.owner])
                {
                    if (EH.effector.VFX != null) unitHolder.RemoveVFX(EH.effector.VFX);
                    unitHolder.effectors.RemoveAt(i);
                }
                else
                {
                    // Check if other abilities are making this unit invisible
                    if (unitHolder.effectors[i].effector.makeInvisible) invisibilityAbilityPresent = true;
                }
            }

            // Remove invisibility only if there is no other effectors making this unit invisible
            if (!invisibilityAbilityPresent && EH.effector.makeInvisible) unitHolder.SetInvisibility(false);

            // Remove visibility of the unit
            if (EH.effector.revealInvisible) unitHolder.CanBeSeen(false, EH.owner);

            if (!EH.stacks) unitHolder.OnStatusUpdate?.Invoke();

            // [Interflow fix 2026-08-05 unit-status-sync] Досрочное снятие (диспел) — сообщить клиентам
            // «снят» (фикс §8.6: раньше досрочное снятие и permanent-эффекторы висели у клиента вечно).
            if ((EH.effector.icon != null || EH.effector.VFX != null) && NetworkDataSync.Instance != null
                && !HasEffectorWithId(unitHolder, EH.effector.id))
                NetworkDataSync.Instance.UnitStatusEffectorRemoveSend(unitHolder, EH.effector.id);
        }

        // [Interflow fix 2026-08-05 unit-status-sync] Остались ли на юните наложения эффектора с этим id
        // (для решения, можно ли слать клиентам «статус снят»).
        private static bool HasEffectorWithId(Unit unit, int effectorId)
        {
            for (int i = 0; i < unit.effectors.Count; i++)
                if (unit.effectors[i].effector != null && unit.effectors[i].effector.id == effectorId) return true;
            return false;
        }

        // Returns the effector by its ID
        public static Effector GetEffectorByID(int effectorID)
        {
            if (GameManager.Instance.gameEffectors.TryGetValue(effectorID, out Effector effector))
            {
                return effector;
            }

            return null;
        }
    }
}
