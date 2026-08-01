using UnityEngine;

namespace StrategyCore
{
    // Active-способность ГЗ орков «Врата Бездны» (демонический портал, центральная таблица замка).
    // Use() на сервере открывает портал у ближайшей вражеской точки интереса и призывает фикс. набор
    // орков-воинов: постоянные, «бесплатные» (лидерство компенсируется), держат фикс. команду Атака.
    // Вся логика спавна/жизни/приказов — в MatchManager (правило 5). Ассет StrategyCore не трогается (правило 1).
    [CreateAssetMenu(fileName = "GatesOfAbyssActive", menuName = "StrategyCore/Abilities/GatesOfAbyssActive")]
    public class GatesOfAbyssActive : Ability
    {
        public override AbilityType type { get { return AbilityType.Active; } }

        [Header("Ability specific")]
        [Tooltip("Префаб призываемого орка-воина (Tier-1). Обязателен — без него призыв пропускается.")]
        public Unit warriorPrefab;

        [Tooltip("Сколько орков призывать за один каст. По ТЗ — 3.")]
        public int spawnCount = 3;

        [Tooltip("Максимум одновременно живых орков от портала на игрока (защита от снежного кома). " +
                 "Сверх лимита каст новых не добавляет. 0 = без лимита. Задай значение в .asset.")]
        public int maxAlivePerPlayer = 0;

        [Tooltip("Разброс точки спавна у вражеской точки (радиус, юниты Unity). Штатный CircleCheck сместит, " +
                 "если место занято. 0 — строго в точке.")]
        public float spawnSpread = 0.5f;

        [Tooltip("VFX портала Скверны. Опционально (можно оставить пустым). Проигрывается один раз в точке спавна. " +
                 "По конвенции ассета VFX сам себя уничтожает после проигрывания.")]
        public VFXReferencer portalVFX;

        [Tooltip("Звук открытия портала. Опционально (можно оставить пустым).")]
        public AudioClip summonSound;

        [Tooltip("Громкость звука портала.")]
        public float summonSoundVolume = 1f;

        // Базовые поля Ability:
        //  cooldown[level] — КД способности (по ТЗ — 35 с), задаётся в .asset;
        //  unitSelector/duration/radius — НЕ используются (точка/состав определяются в MatchManager). Маны нет (по ТЗ).

        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            Debug.Log($"[GatesOfAbyssActive] Use вызван: кастер={(castingUnit != null ? castingUnit.name : "null")}, игрок={castingPlayer}, уровень={level}.");

            // Серверо-авторитетно: спавн/жизнь/команды — только на сервере (правило 6).
            if (NetworkConnectionHandler.isClient) { Debug.Log("[GatesOfAbyssActive] Клиент — призыв пропущен (выполнит сервер)."); return; }

            MatchManager mm = MatchManager.instance;
            if (mm == null) { Debug.LogWarning("[GatesOfAbyssActive] MatchManager.instance == null — призыв невозможен."); return; }

            // Единая точка входа призыва (портал у ближайшей вражеской точки; постоянные, бесплатные, фикс. Атака).
            mm.SummonFixedSquadAtEnemyPoint(castingPlayer, warriorPrefab, spawnCount, maxAlivePerPlayer,
                                            spawnSpread, portalVFX, summonSound, summonSoundVolume);
        }
    }
}
