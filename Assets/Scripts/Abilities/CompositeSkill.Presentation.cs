using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StrategyCore
{
    // CompositeSkill.Presentation.cs — кеш эффекторов и презентация целей. Вырезано 1:1 из CompositeSkill.cs (разрезка на partial-ы, правило 22).
    public partial class CompositeSkill
    {
        // Кэш набора эффекторов «блок + значок состояния»: собирается один раз, дальше переиспользуется.
        Effector[] cachedEffectorSet;
        bool effectorSetBuilt;

        /// <summary>Эффекторы, которые получает цель: из блока эффекторов плюс отдельный значок состояния.</summary>
        public Effector[] EffectorsForTargets()
        {
            if (effectorSetBuilt) return cachedEffectorSet;

            List<Effector> list = new List<Effector>();
            if (effectors != null && effectors.enabled && effectors.records != null)
                for (int i = 0; i < effectors.records.Length; i++)
                    if (effectors.records[i] != null && effectors.records[i].effector != null)
                        list.Add(effectors.records[i].effector);

            if (statusEffector != null) list.Add(statusEffector);

            cachedEffectorSet = list.Count > 0 ? list.ToArray() : null;
            effectorSetBuilt = true;
            return cachedEffectorSet;
        }
        // ==================================================== НАБОРЫ ВИЗУАЛА СОБЫТИЙ ==

        /// <summary>
        /// Наборы визуала этого умения по кодам событий. Клиентский презентер спрашивает НАБОР, а не блок:
        /// про устройство блоков он не знает (правило 5). Кода нет в списке — набора нет, показывать нечего.
        /// </summary>
        public override EventPresentation PresentationFor(int eventCode)
        {
            switch ((AbilityEventCode)eventCode)
            {
                case AbilityEventCode.SkillCast:             return presentation;
                case AbilityEventCode.SkillHeal:             return heal != null ? heal.presentation : null;
                case AbilityEventCode.SkillProjectileImpact: return projectileImpact != null ? projectileImpact.presentation : null;
                case AbilityEventCode.SkillSecondaryHit:     return secondary != null ? secondary.presentation : null;
                case AbilityEventCode.SkillSecondaryCast:    return secondary != null ? secondary.castPresentation : null;

                // [Interflow 2026-09-18, решение Artsiom 57] Пять наборов семьи «движение, отброс, облик».
                case AbilityEventCode.SkillCasterMoveStart:  return casterMove != null ? casterMove.startPresentation : null;
                case AbilityEventCode.SkillCasterMoveArrive: return casterMove != null ? casterMove.arrivePresentation : null;
                case AbilityEventCode.SkillKnockbackTarget:  return knockback != null ? knockback.presentation : null;
                case AbilityEventCode.SkillMorphOn:          return morph != null ? morph.onPresentation : null;
                case AbilityEventCode.SkillMorphOff:         return morph != null ? morph.offPresentation : null;
            }

            return null;
        }

        // ============================================================ ПРЕЗЕНТАЦИЯ ЦЕЛЕЙ ==

        /// <summary>
        /// На сколько растягивать визуал бафа. Ноль — аура не настроена, значит визуал остаётся
        /// авторского размера. Считается и на сервере, и на клиенте (по уровню из сообщения).
        /// </summary>
        public float BuffVfxScaleRadius(int level)
        {
            if (buff == null || !buff.enabled) return 0f;

            float aura = LevelValue(buff.auraRadius, level);
            bool hasAura = aura > 0f || LevelValue(buff.auraDamagePerSecond, level) > 0f;

            return hasAura ? aura : 0f;
        }

        /// <summary>
        /// Сервер: показать VFX длящегося бафа задетым целям. Хосту рисуем напрямую (сообщение
        /// до него не доходит), клиентам уходит ОДНО сообщение на весь каст. Значки и VFX
        /// ЭФФЕКТОРОВ с 2026-08-05 шлёт ядро в точке наложения (единый канал статусов) —
        /// здесь только баф. Геймплейное состояние клиенту не передаётся.
        /// </summary>
        void SendPresentation(List<Unit> targets, int level)
        {
            if (targets == null || targets.Count == 0) return;

            float buffDuration = (buff != null && buff.enabled) ? LevelValue(buff.duration, level) : 0f;
            bool hasBuffVfx = buff != null && buff.enabled && buff.buffVFX != null && buffDuration > 0f;

            if (!hasBuffVfx) return;

            float auraRadius = BuffVfxScaleRadius(level);

            List<UInt16> netIDs = new List<UInt16>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                Unit t = targets[i];
                if (t == null || t.dead) continue;

                // Хост своего же сообщения не получает, но эффекторы у него УЖЕ настоящие (их наложил
                // сервер), поэтому значок и VFX эффектора он видит штатно — дублировать нельзя.
                // Не хватает ему только визуала бафа: SkillBuff теперь чисто геймплейный и ничего не рисует.
                if (hasBuffVfx) SkillVisualStatus.ShowBuffVfx(t, id, buff.buffVFX, buffDuration, auraRadius);

                netIDs.Add(t.netID);
            }

            if (netIDs.Count == 0 || NetworkDataSync.Instance == null) return;

            // [2026-08-05 единый канал статусов] Значки и VFX эффекторов скилл больше НЕ шлёт сам:
            // их отправляет ядро в момент наложения (Effector.EffectorAdd → UnitStatusEffectorSend) —
            // одинаково для атак, аур и скиллов (решение Artsiom 2026-08-05). Здесь остался только
            // VFX длящегося бафа — он не эффектор и в ядре точки наложения не имеет.
            if (hasBuffVfx) NetworkDataSync.Instance.SkillBuffVfxSend(netIDs.ToArray(), id, level, buffDuration);
        }
    }
}
