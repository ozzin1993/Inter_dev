using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Active-способность «массовый огненный плащ» (от замка через центральную таблицу).
    // Use() на сервере берёт юнитов команды кастера из MatchManager (тот же список, что для команд
    // Атака/Защита) и навешивает на каждого временный FlameCloakBuff. FlameCloak.cs (Toggle) не трогается (правило 1).
    [CreateAssetMenu(fileName = "FlameCloakActive", menuName = "StrategyCore/Abilities/FlameCloakActive")]
    public class FlameCloakActive : Ability
    {
        public override AbilityType type { get { return AbilityType.Active; } }

        [Header("Ability specific")]
        [Tooltip("Урон в секунду плаща каждого союзника по врагам вокруг него.")]
        public float[] damagePerSecond;
        [Tooltip("Тип урона плаща.")]
        public DamageType damageType;
        [Tooltip("Кого жжёт плащ союзника (обычно враги: Enemy + Unit + Ground/Water/Air).")]
        public UnitSelector damageSelector;
        [Tooltip("VFX плаща; добавляется каждому союзнику на время действия.")]
        public VFXReferencer VFX;

        // Базовые поля Ability используются так:
        //  unitSelector   — НЕ используется для получателей: список юнитов берётся из MatchManager
        //                   (тот же, что для команд Атака/Защита). Кого жжёт плащ — damageSelector;
        //  radius[level]  — добавка к радиусу ауры каждого союзника (как у FlameCloak);
        //  duration[level]— сколько секунд держится плащ. Пусто/0 — способность ничего не делает.

        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            Debug.Log($"[FlameCloakActive] Use вызван: кастер={(castingUnit != null ? castingUnit.name : "null")}, игрок={castingPlayer}, уровень={level}.");

            // [Interflow fix 2026-06-26] Use идёт на ВСЕХ пирах: клиент тоже накладывает FlameCloakBuff ради VFX
            // (AddVFX локален, по сети не реплицируется). Урон серверо-авторитетен и гейтится ВНУТРИ бафа (FlameCloakBuff.OnTick: !isClient).
            // Ранний `if (isClient) return` здесь был регрессом потери файлов — из-за него плащ не показывался у клиентов.
            MatchManager mm = MatchManager.instance;
            if (mm == null) { Debug.LogWarning("[FlameCloakActive] MatchManager.instance == null — нет списка юнитов команды."); return; }

            float dps = (damagePerSecond != null && damagePerSecond.Length > level) ? damagePerSecond[level] : 0f;
            float auraRadius = (radius != null && radius.Length > level) ? radius[level] : 0f;
            float dur = (duration != null && duration.Length > level) ? duration[level] : 0f;
            if (dur <= 0f)
            {
                Debug.LogWarning($"[FlameCloakActive] duration[{level}]={dur} ≤ 0 — плащ не накладывается. Задай длительность в .asset.");
                return;
            }

            // Список тот же, что MatchManager использует для команд Атака/Защита (боевые юниты команды кастера).
            List<Unit> teamUnits = mm.GetCommandUnitsForPlayer(castingPlayer);
            List<string> allies = new List<string>();
            for (int i = 0; i < teamUnits.Count; i++)
            {
                Unit u = teamUnits[i];
                if (u == null || u.dead) continue;
                FlameCloakBuff.Apply(u, dps, damageType, auraRadius, dur, damageSelector, VFX);
                allies.Add($"{u.name}(owner={u.owner})");
            }
            Debug.Log($"[FlameCloakActive] Плащ наложен на юнитов команды: {allies.Count} [{string.Join(", ", allies)}] " +
                      $"(dps={dps}, радиус+={auraRadius}, длит={dur}с).");
        }
    }
}
