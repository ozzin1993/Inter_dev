using UnityEngine;

namespace StrategyCore
{
    // ============================= ПРИЗЫВ ОТ НОСИТЕЛЯ — SO-обёртка (кирпич B16) ==
    // Active/авто-способность: кастер-юнит призывает prefab × count в СВОЕЙ позиции (Призыв духа Друида,
    // тотем Рунного мага). Вся логика спавна/жизни/лидерства/команд — в MatchManager.SummonFromUnit (правило 5).
    // Периодичность / «в бою» — штатный AutoAbilityUser (авто-каст по стратегии) + cooldown[] как период (правило 2);
    // отдельного механизма периодичности не заводим. Антирекурсия: призванный (маркер SummonedUnit) сам не призывает.
    // Серверо-авторитетно (правило 6). Ассет StrategyCore не трогается (правило 1). Числа — в .asset (правило 3).
    [CreateAssetMenu(fileName = "SummonAbility", menuName = "StrategyCore/Abilities/Interflow/SummonFromUnit (B16)")]
    public class SummonAbility : Ability
    {
        public override AbilityType type { get { return AbilityType.Active; } }

        // Команда призванных, если они НЕ слушают общих приказов. ASCII-идентификаторы, русские подписи (как CallToArms).
        public enum SummonedDefaultCommand
        {
            [InspectorName("Защита")] Defence,
            [InspectorName("Атака")]  Attack
        }

        [Header("Призыв от носителя (B16)")]
        [Tooltip("Префаб призываемого юнита. Обязателен — без него каст пропускается.")]
        public Unit summonPrefab;

        [Tooltip("Сколько юнитов призвать за один каст. [БАЛАНС — Влад]")]
        public int spawnCount = 1;

        [Tooltip("Время жизни призванных, сек (0 = постоянные, штатный LifetimeUnit). [БАЛАНС — Влад]")]
        public float summonedLifetime = 0f;

        [Tooltip("Максимум одновременно живых призванных ОТ ЭТОГО носителя (0 = без лимита). Сверх лимита каст не добавляет.")]
        public int maxAlivePerCaster = 0;

        [Tooltip("Убивать призванных при смерти носителя. ВЫКЛ — живут дальше (по своему времени).")]
        public bool killSummonsOnOwnerDeath = false;

        [Tooltip("Слушают ли призванные общие приказы Атака/Защита команды. ВКЛ — как обычные юниты; ВЫКЛ — держат фикс. команду ниже.")]
        public bool summonedObeyCommonCommands = true;

        [Tooltip("Команда призванных, если общие приказы НЕ слушают (Атака/Защита). При слушании общих — игнорируется.")]
        public SummonedDefaultCommand summonedCommand = SummonedDefaultCommand.Attack;

        [Tooltip("Разброс точки спавна при count > 1, мировые единицы. 0 — строго в точке носителя.")]
        public float spawnSpread = 1f;

        // Базовые поля Ability: cooldown[level] — период авто-каста (задаётся в .asset); unitSelector/radius —
        // НЕ используются (призыв от себя, без цели). Требования (requiredTech/Level) / мана — штатные.

        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            // Серверо-авторитетно: спавн/жизнь/команды — только сервер (правило 6).
            if (NetworkConnectionHandler.isClient) return;
            if (castingUnit == null) { Debug.LogWarning("[SummonAbility] Нет кастера — призыв пропущен."); return; }

            // Антирекурсия (§риски): призванный юнит сам не призывает (иначе лавина авто-каста «дух призывает духа»).
            if (castingUnit.GetComponent<SummonedUnit>() != null)
            {
                Debug.Log($"[SummonAbility] Кастер '{castingUnit.name}' сам призван — вложенный призыв запрещён.");
                return;
            }

            MatchManager mm = MatchManager.instance;
            if (mm == null) { Debug.LogWarning("[SummonAbility] MatchManager.instance == null — призыв невозможен."); return; }

            BottomTableAction cmd = (summonedCommand == SummonedDefaultCommand.Attack)
                ? BottomTableAction.Attack
                : BottomTableAction.Defence;

            // Единая точка входа призыва от носителя (позиция/владелец/команда берутся из castingUnit).
            mm.SummonFromUnit(castingUnit, summonPrefab, spawnCount, summonedLifetime,
                              summonedObeyCommonCommands, cmd, maxAlivePerCaster, killSummonsOnOwnerDeath, spawnSpread);
        }
    }
}
