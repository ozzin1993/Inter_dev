using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Ульта героя «Железный Приговор» (Active): на duration секунд всем боевым юнитам ЛИНИИ кастера —
    // снижение ВСЕГО входящего урона на damageReductionPercent% (штатный OnBeforeGetDamageCallbacks) и
    // иммунитет к контролю (ControlImmunity). Список — как у команд Атака/Защита (GetCommandUnitsForPlayer;
    // включает самого героя). Серверо-авторитетно (правило 6). Логика в нашем бафе IronVerdictBuff (ассет не тронут).
    [CreateAssetMenu(fileName = "IronVerdictActive", menuName = "StrategyCore/Abilities/IronVerdictActive")]
    public class IronVerdictActive : Ability
    {
        public override AbilityType type { get { return AbilityType.Active; } }

        [Header("Ability specific")]
        [Tooltip("На сколько процентов снижается ВЕСЬ входящий урон (любой источник: башни/чума/атаки). По ТЗ — 35.")]
        public float damageReductionPercent = 35f;

        [Tooltip("Давать ли иммунитет к контролю (стан; в будущем заморозка/страх/чума) на время действия. По ТЗ — да.")]
        public bool controlImmunity = true;

        // База Ability: duration[level] — длительность режима (по ТЗ 6с); cooldown[level] — КД (по ТЗ 40с).

        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            if (NetworkConnectionHandler.isClient) return; // эффекты — только сервер (правило 6)
            MatchManager mm = MatchManager.instance;
            if (mm == null) { Debug.LogWarning("[IronVerdict] MatchManager.instance == null — нет списка юнитов линии."); return; }

            float dur = (duration != null && duration.Length > level) ? duration[level] : 0f;
            if (dur <= 0f) { Debug.LogWarning($"[IronVerdict] duration не задан (level {level}) — эффект пропущен."); return; }

            List<Unit> lineUnits = mm.GetCommandUnitsForPlayer(castingPlayer);
            int affected = 0;
            for (int i = 0; i < lineUnits.Count; i++)
            {
                Unit u = lineUnits[i];
                if (u == null || u.dead) continue;
                IronVerdictBuff.Apply(u, this, level, damageReductionPercent, controlImmunity, dur);
                affected++;
            }
            Debug.Log($"[IronVerdict] player={castingPlayer}: -{damageReductionPercent}% урона + иммунитет={controlImmunity} на {dur}с, юнитов {affected}.");
        }
    }
}
