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
        /// <param name="effectorIds">Эффекторы скилла — по ним клиент возьмёт значок и VFX.</param>
        /// <param name="effectorDurations">Фактическая длительность каждого эффектора из effectorIds, тот же порядок.
        /// Значение ≤ 0 — брать из ассета. С 2026-08-02 числа эффектора задаёт умение, поэтому клиентский
        /// таймер значка нельзя считать по ассету: у другого умения тот же эффектор живёт другое время.</param>
        /// <param name="buffAbilityId">Скилл, чей VFX длящегося бафа показать. −1 — бафа нет.</param>
        /// <param name="buffDuration">Сколько секунд держать VFX бафа.</param>
        /// <param name="level">Уровень скилла — нужен, чтобы клиент посчитал размер визуала ауры.</param>
        public void SkillPresentationSend(UInt16[] netIDs, int[] effectorIds, float[] effectorDurations, int buffAbilityId, float buffDuration, int level)
        {
            if (netIDs == null || netIDs.Length == 0) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return; // одиночный прогон — RPC не шлём

            bool hasEffectors = effectorIds != null && effectorIds.Length > 0;
            if (!hasEffectors && buffAbilityId < 0) return; // показывать нечего — сообщение не шлём

            // Массивы обязаны быть одной длины: клиент читает их по одному индексу. Рассогласование —
            // ошибка вызывающего кода, а не контента, поэтому падаем шумно, а не подгоняем молча.
            float[] durations = effectorDurations ?? Array.Empty<float>();
            if (hasEffectors && durations.Length != effectorIds.Length)
            {
                Debug.LogError("SkillPresentationSend: длины массивов не совпадают — id " + effectorIds.Length +
                               ", длительностей " + durations.Length + ". Презентация не отправлена.");
                return;
            }

            SkillPresentationClientRpc(netIDs, effectorIds ?? Array.Empty<int>(), durations, buffAbilityId, buffDuration, level);
        }

        [Rpc(SendTo.NotServer)]
        private void SkillPresentationClientRpc(UInt16[] netIDs, int[] effectorIds, float[] effectorDurations, int buffAbilityId, float buffDuration, int level)
        {
            // Подключение в середине матча: принимаем только данные сцены (штатное правило всех RPC этого хаба).
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;

            // Без GameManager показывать нечего и некуда: по нему идёт и поиск эффектора (`GetEffectorByID`),
            // и подписка на тик у `SkillVisualStatus`. Сообщение может прийти в кадр выгрузки сцены.
            if (GameManager.instance == null) return;

            // Разбираем описание бафа один раз на всё сообщение, а не на каждую цель.
            VFXReferencer buffVfx = null;
            float auraRadius = 0f;
            if (buffAbilityId >= 0
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
                    if (effector != null)
                    {
                        // Длительности может не быть только у старого отправителя — тогда −1 и ассетное число.
                        float shown = e < effectorDurations.Length ? effectorDurations[e] : -1f;
                        SkillVisualStatus.ShowEffector(unit, effector, shown);
                        continue;
                    }

                    // Молча пропускать нельзя: у сервера значок есть, у клиента нет, и без сообщения
                    // расхождение не диагностируется. Тот же шаблон, что у рассинхрона по netID выше.
                    Debug.LogError("Desync! Effector id:" + effectorIds[e] +
                                   " should exist on client, but does not! (SkillPresentationSend NetworkDataSync)");
                }

                if (buffVfx != null) SkillVisualStatus.ShowBuffVfx(unit, buffAbilityId, buffVfx, buffDuration, auraRadius);
            }
        }
    }
}
