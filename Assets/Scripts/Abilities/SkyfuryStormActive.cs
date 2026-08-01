using UnityEngine;

namespace StrategyCore
{
    // Active-способность ГЗ орков «Ярость Небосвода» (метеоритный дождь, центральная таблица замка).
    // Use() на сервере запускает метеоритный дождь через MatchManager (правило 5): авто-таргет по врагам,
    // урон физ+огонь + оглушение всем в зоне удара. Ассет StrategyCore не трогается (правило 1). Маны нет (по ТЗ).
    [CreateAssetMenu(fileName = "SkyfuryStormActive", menuName = "StrategyCore/Abilities/SkyfuryStormActive")]
    public class SkyfuryStormActive : Ability
    {
        public override AbilityType type { get { return AbilityType.Active; } }

        [Header("Ability specific")]
        [Tooltip("Длительность дождя, секунд. По ТЗ — 4.")]
        public float durationSeconds = 4f;

        [Tooltip("Сколько метеоров падает каждую секунду. По ТЗ — 4 (итого 4с × 4 = 16).")]
        public int meteorsPerSecond = 4;

        [Tooltip("Радиус зоны поражения одного метеора (юниты Unity). Обязателен — задай в .asset.")]
        public float impactRadius = 0f;

        [Tooltip("Физический урон метеора всем в зоне. Задай в .asset (пусто = не наносится).")]
        public float physicalDamage = 0f;

        [Tooltip("Тип физического урона (ассет из Resources/DamageType). Пусто → физ. компонент пропускается.")]
        public DamageType physicalDamageType;

        [Tooltip("Огненный урон метеора всем в зоне. Задай в .asset (пусто = не наносится).")]
        public float fireDamage = 0f;

        [Tooltip("Тип огненного урона (ассет из Resources/DamageType). Пусто → огненный компонент пропускается.")]
        public DamageType fireDamageType;

        [Tooltip("Длительность оглушения всех в зоне, секунд. По ТЗ — 0.5.")]
        public float stunSeconds = 0.5f;

        [Tooltip("ВКЛ — цель выбирается взвешенно по плотности врагов (плотные участки бьются чаще). " +
                 "ВЫКЛ — равномерно случайный вражеский юнит.")]
        public bool useDensityTargeting = true;

        [Tooltip("Селектор ЦЕЛИ: кого считать врагами при выборе точки удара (обычно враги, наземные/воздушные).")]
        public UnitSelector targetSelector;

        [Tooltip("Селектор ЗОНЫ: кого накрывает урон и оглушение в радиусе (по ТЗ — ВСЕ юниты, включая союзников).")]
        public UnitSelector splashSelector;

        [Tooltip("VFX метеора в точке удара. Опционально (можно оставить пустым). Сам себя уничтожает по конвенции ассета.")]
        public VFXReferencer meteorVFX;

        [Tooltip("Звук удара метеора. Опционально (можно оставить пустым).")]
        public AudioClip impactSound;

        [Tooltip("Громкость звука удара.")]
        public float impactVolume = 1f;

        // Базовые поля Ability:
        //  cooldown[level] — КД способности (по ТЗ — 60 с), задаётся в .asset;
        //  unitSelector/duration/radius — НЕ используются (селекторы/радиус/длительность — явные поля выше). Маны нет.

        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            Debug.Log($"[SkyfuryStormActive] Use вызван: кастер={(castingUnit != null ? castingUnit.name : "null")}, игрок={castingPlayer}, уровень={level}.");

            if (NetworkConnectionHandler.isClient) { Debug.Log("[SkyfuryStormActive] Клиент — дождь пропущен (выполнит сервер)."); return; }

            MatchManager mm = MatchManager.instance;
            if (mm == null) { Debug.LogWarning("[SkyfuryStormActive] MatchManager.instance == null — дождь невозможен."); return; }

            mm.StartMeteorStorm(castingUnit, castingPlayer, durationSeconds, meteorsPerSecond, impactRadius,
                                physicalDamage, physicalDamageType, fireDamage, fireDamageType, stunSeconds,
                                useDensityTargeting, targetSelector, splashSelector, meteorVFX, impactSound, impactVolume);
        }
    }
}
