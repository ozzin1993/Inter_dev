using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Active-способность «Благословение Небес» (массовое лечение, от замка через центральную таблицу).
    // Use() на сервере берёт юнитов команды кастера из MatchManager (тот же список, что для команд
    // Атака/Защита) и каждому: мгновенно лечит долю maxHP, затем навешивает HeavensBlessingBuff
    // (лечение-во-времени). Сделано по аналогии с FlameCloakActive; Unit.cs не трогается (правило 1).
    [CreateAssetMenu(fileName = "HeavensBlessingActive", menuName = "StrategyCore/Abilities/HeavensBlessingActive")]
    public class HeavensBlessingActive : Ability
    {
        public override AbilityType type { get { return AbilityType.Active; } }

        [Header("Ability specific")]
        [Tooltip("Мгновенное лечение в ПРОЦЕНТАХ от макс. ХП, целое число (25 = 25%). По уровням способности.")]
        public float[] instantHealPercent;
        [Tooltip("Лечение-во-времени в ПРОЦЕНТАХ от макс. ХП в секунду, целое число (2 = 2%/сек). По уровням способности.")]
        public float[] healPerSecondPercent;
        [Tooltip("VFX лечения; добавляется каждому союзнику на время действия HoT. Можно оставить пустым.")]
        public VFXReferencer VFX;

        // Базовые поля Ability используются так:
        //  unitSelector   — НЕ используется для получателей: список юнитов берётся из MatchManager
        //                   (тот же, что для команд Атака/Защита);
        //  duration[level]— сколько секунд длится лечение-во-времени (HoT). 0/пусто — HoT не запускается,
        //                   мгновенная часть всё равно отрабатывает.

        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            Debug.Log($"[HeavensBlessingActive] Use вызван: кастер={(castingUnit != null ? castingUnit.name : "null")}, игрок={castingPlayer}, уровень={level}.");

            // Серверо-авторитетно: лечение — только на сервере (правило 6). HP синкается клиентам штатно (ChangeHP).
            if (NetworkConnectionHandler.isClient) { Debug.Log("[HeavensBlessingActive] Клиент — лечение пропущено (выполнит сервер)."); return; }
            MatchManager mm = MatchManager.instance;
            if (mm == null) { Debug.LogWarning("[HeavensBlessingActive] MatchManager.instance == null — нет списка юнитов команды."); return; }

            float instant = (instantHealPercent != null && instantHealPercent.Length > level) ? instantHealPercent[level] : 0f;
            float perSec = (healPerSecondPercent != null && healPerSecondPercent.Length > level) ? healPerSecondPercent[level] : 0f;
            float dur = (duration != null && duration.Length > level) ? duration[level] : 0f;

            if (instant <= 0f && (perSec <= 0f || dur <= 0f))
            {
                Debug.LogWarning($"[HeavensBlessingActive] Нечего лечить: instant={instant}, perSec={perSec}, duration={dur}. Задай значения в .asset.");
                return;
            }

            // Список тот же, что MatchManager использует для команд Атака/Защита (боевые юниты команды кастера).
            List<Unit> teamUnits = mm.GetCommandUnitsForPlayer(castingPlayer);
            List<string> allies = new List<string>();
            for (int i = 0; i < teamUnits.Count; i++)
            {
                Unit u = teamUnits[i];
                if (u == null || u.dead) continue;

                // Мгновенная часть — штатный ChangeHP (сам клампит до maxHealth и синкает HP клиентам).
                // instant — проценты (25 = 25%), переводим в долю делением на 100.
                if (instant > 0f) u.ChangeHP(instant / 100f * u.maxHealth);

                // Лечение-во-времени.
                if (perSec > 0f && dur > 0f) HeavensBlessingBuff.Apply(u, perSec, dur, VFX);

                allies.Add($"{u.name}(owner={u.owner})");
            }
            Debug.Log($"[HeavensBlessingActive] Благословение наложено на юнитов команды: {allies.Count} [{string.Join(", ", allies)}] " +
                      $"(instant={instant}% maxHP, perSec={perSec}%/с, длит={dur}с).");
        }
    }
}
