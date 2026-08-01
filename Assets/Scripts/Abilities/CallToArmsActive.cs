using UnityEngine;

namespace StrategyCore
{
    // Active-способность «Призыв к Оружию» (повторная волна ополчения, от замка через центральную таблицу).
    // Use() на сервере дублирует ПОСЛЕДНЮЮ фактически заспавненную волну команды кастера вне очереди.
    // Призванные юниты: живут заданное время (штатный LifetimeUnit), не тратят лидерство/золото
    // (компенсация в MatchManager), по флагу либо слушают общие приказы команды, либо держат фиксированную
    // команду (Защита/Атака). Вся логика спавна/жизни/приказов централизована в MatchManager (правило 5).
    // Ассет StrategyCore не трогается (правило 1).
    [CreateAssetMenu(fileName = "CallToArmsActive", menuName = "StrategyCore/Abilities/CallToArmsActive")]
    public class CallToArmsActive : Ability
    {
        public override AbilityType type { get { return AbilityType.Active; } }

        // Команда, которую держат призванные, если они НЕ слушают общих приказов. Идентификаторы ASCII,
        // подписи в Inspector — русские через [InspectorName] (как UnitCategory у AutoAbilityUser).
        public enum SummonedDefaultCommand
        {
            [InspectorName("Защита")] Defence,
            [InspectorName("Атака")]  Attack
        }

        [Header("Ability specific")]
        [Tooltip("Сколько секунд живут призванные юниты, затем умирают (штатный LifetimeUnit). По ТЗ — 120.")]
        public float summonedLifetime = 120f;

        [Tooltip("Слушаются ли призванные юниты общих приказов команды (Атака/Защита всей команде). " +
                 "ВКЛ — ведут себя как обычные юниты команды. ВЫКЛ — на них общие приказы не действуют, " +
                 "они держат фиксированную команду ниже.")]
        public bool summonedObeyCommonCommands = false;

        [Tooltip("Если общие приказы НЕ слушаются — какую команду держат призванные: " +
                 "Защита (передовая своя точка) или Атака (следующая вражеская точка). " +
                 "При слушании общих приказов это поле игнорируется.")]
        public SummonedDefaultCommand summonedCommand = SummonedDefaultCommand.Defence;

        [Tooltip("VFX призыва. Опционально (можно оставить пустым). Проигрывается один раз в точке спавна команды. " +
                 "По конвенции ассета VFX должен сам себя уничтожать после проигрывания.")]
        public VFXReferencer summonVFX;

        [Tooltip("Звук призыва. Опционально (можно оставить пустым). Проигрывается один раз в точке спавна команды.")]
        public AudioClip summonSound;

        [Tooltip("Громкость звука призыва.")]
        public float summonSoundVolume = 1f;

        // Базовые поля Ability:
        //  cooldown[level] — КД способности (по ТЗ — 50 с), задаётся в .asset;
        //  unitSelector/duration/radius — НЕ используются (состав берётся из последней волны MatchManager).

        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            Debug.Log($"[CallToArmsActive] Use вызван: кастер={(castingUnit != null ? castingUnit.name : "null")}, игрок={castingPlayer}, уровень={level}.");

            // Серверо-авторитетно: спавн/жизнь/команды — только на сервере (правило 6).
            if (NetworkConnectionHandler.isClient) { Debug.Log("[CallToArmsActive] Клиент — призыв пропущен (выполнит сервер)."); return; }

            MatchManager mm = MatchManager.instance;
            if (mm == null) { Debug.LogWarning("[CallToArmsActive] MatchManager.instance == null — призыв невозможен."); return; }

            // Команда призванных переводится в штатный BottomTableAction (если общие приказы не слушаются).
            BottomTableAction cmd = (summonedCommand == SummonedDefaultCommand.Attack)
                ? BottomTableAction.Attack
                : BottomTableAction.Defence;

            // Вся работа — в единой точке входа MatchManager (снимок последней волны, лидерство, жизнь, приказы).
            mm.SummonLastWave(castingPlayer, summonedLifetime, summonedObeyCommonCommands, cmd,
                              summonVFX, summonSound, summonSoundVolume);
        }
    }
}
