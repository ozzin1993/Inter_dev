using UnityEngine;

namespace StrategyCore
{
    public partial class UnitReceiver
    {
        /// <summary>
        /// Вклад активных состояний в приёмник урона, по аналогии с получаемым лечением.
        /// Своего реестра нет: диспел, истечение и смерть используют штатный список состояний.
        /// Сила наложения не масштабирует множитель, как и в UnitReceiver.Heal.
        /// </summary>
        public static float EffectorDamageMultiplier(Unit target, DamageType type)
        {
            if (target == null || target.effectors == null) return 1f;
            float weakest = 1f;
            float strongest = 1f;
            foreach (EffectorHolder holder in target.effectors)
            {
                Effector effector = holder?.effector;
                if (effector == null || (effector.incomingDamageType != null && effector.incomingDamageType != type)) continue;
                float multiplier = Mathf.Max(0f, effector.incomingDamageMultiplier);
                if (multiplier < weakest) weakest = multiplier;
                else if (multiplier > strongest) strongest = multiplier;
            }
            return weakest < 1f ? weakest : strongest;
        }
    }
}
