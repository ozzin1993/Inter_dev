using System;
using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Партиал NetworkDataSync: репликация ПРЕЗЕНТАЦИИ скиллов-конструкторов (значки состояний и VFX).
    ///
    /// Решение Artsiom 2026-08-01: строгое разделение — состояние живёт только на сервере, клиент только
    /// показывает. Эффекторы по сети не ходят и являются геймплейными объектами, поэтому клиенту отправляется
    /// не эффектор, а явное «покажи вот такой значок и VFX на этих юнитах». Модель скопирована с того, как
    /// ядро уже реплицирует стан (StunSetSend → StunSetClientRpc).
    ///
    /// Новый partial-файл: сам NetworkDataSync.cs не правится.
    /// </summary>
    public partial class NetworkDataSync
    {
        /// <summary>
        /// Сервер: показать клиентам презентацию одного каста. Одно сообщение на весь каст, а не на цель —
        /// у командных скиллов целей десятки. Хост это сообщение не получает (SendTo.NotServer) и рисует
        /// у себя напрямую, как обычный локальный зритель.
        /// </summary>
        /// <param name="netIDs">Юниты, которых задел скилл.</param>
        /// <param name="effectorIds">Эффекторы скилла — по ним клиент возьмёт значок и VFX; длительность у эффектора своя.</param>
        /// <param name="buffAbilityId">Скилл, чей VFX длящегося бафа показать. −1 — бафа нет.</param>
        /// <param name="buffDuration">Сколько секунд держать VFX бафа.</param>
        /// <param name="level">Уровень скилла — нужен, чтобы клиент посчитал размер визуала ауры.</param>
        public void SkillPresentationSend(UInt16[] netIDs, int[] effectorIds, int buffAbilityId, float buffDuration, int level)
        {
            if (netIDs == null || netIDs.Length == 0) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return; // одиночный прогон — RPC не шлём

            bool hasEffectors = effectorIds != null && effectorIds.Length > 0;
            if (!hasEffectors && buffAbilityId < 0) return; // показывать нечего — сообщение не шлём

            SkillPresentationClientRpc(netIDs, effectorIds ?? Array.Empty<int>(), buffAbilityId, buffDuration, level);
        }

        [Rpc(SendTo.NotServer)]
        private void SkillPresentationClientRpc(UInt16[] netIDs, int[] effectorIds, int buffAbilityId, float buffDuration, int level)
        {
            // Подключение в середине матча: принимаем только данные сцены (штатное правило всех RPC этого хаба).
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            // Разбираем описание бафа один раз на всё сообщение, а не на каждую цель.
            VFXReferencer buffVfx = null;
            float auraRadius = 0f;
            if (buffAbilityId >= 0 && GameManager.instance != null
                && GameManager.instance.gameAbilities.TryGetValue(buffAbilityId, out Ability ability)
                && ability is CompositeSkill skill && skill.buff != null)
            {
                buffVfx = skill.buff.buffVFX;
                auraRadius = skill.BuffVfxScaleRadius(level);
            }

            for (int i = 0; i < netIDs.Length; i++)
            {
                if (!SlotManager.instance.unitNetID.TryGetValue(netIDs[i], out Unit unit))
                {
                    Debug.LogError("Desync! Unit netID:" + netIDs[i] +
                                   " should exist on client, but does not! (SkillPresentationSend NetworkDataSync)");
                    continue;
                }
                if (unit == null || unit.dead) continue;

                for (int e = 0; e < effectorIds.Length; e++)
                {
                    Effector effector = Effector.GetEffectorByID(effectorIds[e]);
                    if (effector != null) SkillVisualStatus.ShowEffector(unit, effector);
                }

                if (buffVfx != null) SkillVisualStatus.ShowBuffVfx(unit, buffAbilityId, buffVfx, buffDuration, auraRadius);
            }
        }
    }
}
