using System;
using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Партиал NetworkDataSync: ЕДИНЫЙ канал синка статусов юнитов.
    /// Решение Artsiom 2026-08-05: «слепота и все остальные эффекты должны идти одинаково» —
    /// один механизм доставки статусов клиенту, без спец-путей.
    ///
    /// Сервер сообщает клиентам «статус появился» / «статус снят», клиент только показывает
    /// (SkillVisualStatus — правило 6, состояние живёт на сервере). Через канал идут:
    ///  - ЭФФЕКТОРЫ (значок и VFX): отправка из ядра Effector.EffectorAdd/Update/Remove —
    ///    одинаково покрывает атаки, ауры и скиллы; сообщение «снят» чинит §8.6 отчёта приёмки
    ///    (диспел и permanent-эффекторы раньше висели у клиента до конца матча);
    ///  - ФЛАГИ наших состояний без эффектора (слепота BlindDebuff);
    ///  - VFX длящегося БАФА скилла (перенос из бывшего партиала SkillPresentation, который
    ///    этим файлом ЗАМЕНЁН: эффекторные значки скиллов теперь едут общим путём из ядра);
    ///  - ФАКТЫ ПРЕЗЕНТАЦИИ УМЕНИЙ (2026-08-06): срабатывание умения и жизнь зон. Эти сообщения ничего не рисуют
    ///    сами — они поднимают событие в SkillPresentationEvents, а рисует клиентский презентер.
    ///    Сетевой слой про визуал не знает (событийная инверсия, решение Artsiom 2026-08-06).
    /// Статусы ядра (стан/немота/безоружие) канала не требуют: ядро уже реплицирует их штатно
    /// (StunSetSend / MuteSetSend / DisarmSetSend), клиентские поля юнита актуальны.
    ///
    /// Новый partial-файл: сам NetworkDataSync.cs не правится.
    /// </summary>
    public partial class NetworkDataSync
    {
        // ============================== ЭФФЕКТОРЫ ==============================

        /// <summary>Сервер: на юните появился или продлился эффектор со значком/VFX.</summary>
        /// <param name="duration">Фактическая длительность ЭТОГО наложения, сек. Клиент держит значок
        /// до сообщения «снят»; длительность — страховка на случай потери снятия при смене сцены.</param>
        public void UnitStatusEffectorSend(Unit unit, int effectorId, float duration)
        {
            if (!CanSendAbout(unit)) return;
            UnitStatusEffectorClientRpc(unit.netID, effectorId, duration);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void UnitStatusEffectorClientRpc(UInt16 netID, int effectorId, float duration)
        {
            // Подключение в середине матча: принимаем только данные сцены (штатное правило RPC этого хаба).
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;
            if (GameManager.Instance == null) return; // кадр выгрузки сцены
            if (!TryResolveUnit(netID, "UnitStatusEffectorSend", out Unit unit)) return;

            Effector effector = Effector.GetEffectorByID(effectorId);
            if (effector == null)
            {
                // Молча пропускать нельзя: у сервера значок есть, у клиента нет — рассинхрон должен быть виден.
                Debug.LogError("Desync! Effector id:" + effectorId +
                               " should exist on client, but does not! (UnitStatusEffectorSend NetworkDataSync)");
                return;
            }

            SkillVisualStatus.ShowEffector(unit, effector, duration);
        }

        /// <summary>Сервер: эффектора с этим id на юните больше нет (истёк или снят досрочно — фикс §8.6).</summary>
        public void UnitStatusEffectorRemoveSend(Unit unit, int effectorId)
        {
            if (!CanSendAbout(unit)) return;
            UnitStatusEffectorRemoveClientRpc(unit.netID, effectorId);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void UnitStatusEffectorRemoveClientRpc(UInt16 netID, int effectorId)
        {
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;
            if (!TryResolveUnit(netID, "UnitStatusEffectorRemoveSend", out Unit unit)) return;

            SkillVisualStatus.RemoveEffector(unit, effectorId);
        }

        // ============================== ФЛАГИ НАШИХ СОСТОЯНИЙ ==============================

        /// <summary>Сервер: включить/выключить у юнита флаг состояния без эффектора (например, слепоту).</summary>
        public void UnitStatusFlagSend(Unit unit, UnitStatusFlag flag, bool state)
        {
            if (!CanSendAbout(unit)) return;
            UnitStatusFlagClientRpc(unit.netID, (byte)flag, state);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void UnitStatusFlagClientRpc(UInt16 netID, byte flag, bool state)
        {
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;
            if (!TryResolveUnit(netID, "UnitStatusFlagSend", out Unit unit)) return;

            SkillVisualStatus.SetFlag(unit, (UnitStatusFlag)flag, state);
        }

        // ============================== VFX БАФА СКИЛЛА ==============================
        // Значка у бафа нет (значок даёт эффектор-статус), поэтому это отдельное сообщение канала.

        /// <summary>Сервер: показать клиентам VFX длящегося бафа скилла на юнитах. Одно сообщение на каст.</summary>
        public void SkillBuffVfxSend(UInt16[] netIDs, int buffAbilityId, int buffAbilityLevel, float buffDuration)
        {
            if (netIDs == null || netIDs.Length == 0 || buffAbilityId < 0) return;
            if (!ServerCanSend()) return;

            // [Interflow fix 2026-08-23 status-send-gate] Мёртвые к моменту отправки выпадают из
            // массива: их netID уже снят из реестра, и клиент напечатал бы «Desync!» на каждый номер.
            int alive = 0;
            for (int i = 0; i < netIDs.Length; i++)
                if (SlotManager.Instance.unitNetID.ContainsKey(netIDs[i])) alive++;
            if (alive == 0) return;
            if (alive != netIDs.Length)
            {
                UInt16[] filtered = new UInt16[alive];
                int k = 0;
                for (int i = 0; i < netIDs.Length; i++)
                    if (SlotManager.Instance.unitNetID.ContainsKey(netIDs[i])) filtered[k++] = netIDs[i];
                netIDs = filtered;
            }

            SkillBuffVfxClientRpc(netIDs, buffAbilityId, buffAbilityLevel, buffDuration);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void SkillBuffVfxClientRpc(UInt16[] netIDs, int buffAbilityId, int buffAbilityLevel, float buffDuration)
        {
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;
            if (GameManager.Instance == null) return;

            // Разбираем описание бафа один раз на всё сообщение, а не на каждую цель.
            VFXReferencer buffVfx = null;
            float auraRadius = 0f;
            if (GameManager.Instance.gameAbilities.TryGetValue(buffAbilityId, out Ability ability)
                && ability is CompositeSkill skill && skill.buff != null)
            {
                buffVfx = skill.buff.buffVFX;
                auraRadius = skill.BuffVfxScaleRadius(buffAbilityLevel);
            }
            if (buffVfx == null) return;

            for (int i = 0; i < netIDs.Length; i++)
            {
                if (!TryResolveUnit(netIDs[i], "SkillBuffVfxSend", out Unit unit)) continue;
                SkillVisualStatus.ShowBuffVfx(unit, buffAbilityId, buffVfx, buffDuration, auraRadius);
            }
        }

        // ============================== СРАБАТЫВАНИЕ УМЕНИЯ ==============================
        // Сервер шлёт ФАКТ срабатывания с фактическими целью/точкой (клиент их не вычисляет — правило 6).
        // Что рисовать, презентер берёт из ассета умения по abilityID. Конвенция netID «0 == null» — из ядра.

        /// <summary>Сервер: умение сработало. Одно сообщение на любой каст, включая «умный выбор» с кнопки.</summary>
        public void SkillFiredSend(Unit caster, int abilityID, int level, Unit aimUnit, Vector3 aimPoint)
        {
            if (!ServerCanSend()) return;

            // [Interflow fix 2026-08-23 status-send-gate] Ссылки необязательны (конвенция «0 — нет
            // ссылки»): кастер или цель могли умереть в этом же вызове — шлём 0, а не снятый netID,
            // на который клиент ответил бы ложным «Desync!».
            UInt16 casterID = StillRegistered(caster) ? caster.netID : (UInt16)0;
            UInt16 aimID = StillRegistered(aimUnit) ? aimUnit.netID : (UInt16)0;
            SkillFiredClientRpc(casterID, abilityID, level, aimID, aimPoint);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void SkillFiredClientRpc(UInt16 casterID, int abilityID, int level, UInt16 aimID, Vector3 aimPoint)
        {
            if (NetworkConnectionHandler.Instance != null && NetworkConnectionHandler.Instance.connectionStage == 2) return;
            if (GameManager.Instance == null) return; // кадр выгрузки сцены

            // Ссылки не обязательны: умение могло сработать без кастера или без конкретной цели,
            // а цель — умереть по дороге. Презентацию из-за этого не отменяем, точка приложения известна.
            Unit caster = null;
            if (casterID != 0) TryResolveUnit(casterID, "SkillFiredSend", out caster);

            Unit aimUnit = null;
            if (aimID != 0) TryResolveUnit(aimID, "SkillFiredSend", out aimUnit);

            SkillPresentationEvents.RaiseSkillFired(caster, abilityID, level, aimUnit, aimPoint);
        }

        // ============================== ЗОНЫ НА ЗЕМЛЕ ==============================
        // Сервер шлёт только ФАКТ жизни зоны; чем её рисовать — знает клиентский презентер
        // (префаб лежит в ассете умения). Реестр и время жизни зоны — на сервере (MatchManager.GroundZones).

        /// <summary>Сервер: на земле появилась зона умения. Позиция ФАКТИЧЕСКАЯ — разброс рандомится сервером.</summary>
        public void GroundZoneSpawnSend(int zoneId, int abilityID, int level, Vector3 position)
        {
            if (!ServerCanSend()) return;
            GroundZoneSpawnClientRpc(zoneId, abilityID, level, position);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void GroundZoneSpawnClientRpc(int zoneId, int abilityID, int level, Vector3 position)
        {
            // Подключение в середине матча: принимаем только данные сцены (штатное правило RPC этого хаба).
            // Живые зоны опоздавшему клиенту досылаются отдельно, по завершении подключения.
            if (NetworkConnectionHandler.Instance != null && NetworkConnectionHandler.Instance.connectionStage == 2) return;
            if (GameManager.Instance == null) return; // кадр выгрузки сцены

            SkillPresentationEvents.RaiseZoneSpawned(zoneId, abilityID, level, position);
        }

        /// <summary>Сервер: зона на земле закончила жизнь (истекла или снята).</summary>
        public void GroundZoneDespawnSend(int zoneId)
        {
            if (!ServerCanSend()) return;
            GroundZoneDespawnClientRpc(zoneId);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void GroundZoneDespawnClientRpc(int zoneId)
        {
            if (NetworkConnectionHandler.Instance != null && NetworkConnectionHandler.Instance.connectionStage == 2) return;

            SkillPresentationEvents.RaiseZoneDespawned(zoneId);
        }

        /// <summary>
        /// Сервер: догнать ОДНОГО клиента живыми зонами. Обычные сообщения он отбросил, пока грузил сцену
        /// (`connectionStage == 2`), поэтому у досыла гейта стадии НЕТ — это и есть данные сцены.
        /// </summary>
        public void GroundZoneResendSend(ulong clientID, int zoneId, int abilityID, int level, Vector3 position)
        {
            if (!ServerCanSend()) return;
            GroundZoneResendClientRpc(zoneId, abilityID, level, position, RpcTarget.Single(clientID, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void GroundZoneResendClientRpc(int zoneId, int abilityID, int level, Vector3 position, RpcParams rpcParams = default)
        {
            if (GameManager.Instance == null) return;

            SkillPresentationEvents.RaiseZoneSpawned(zoneId, abilityID, level, position);
        }

        // ============================== ПОГЛОЩАЮЩИЙ ЩИТ ==============================
        // Величина щита живёт только на сервере (AbsorbShield); клиенту она нужна ровно для одного —
        // серого сегмента на полоске здоровья (ShieldBarDisplay). Смерть носителя отдельно не шлём:
        // сегмент живёт на полоске юнита и снимается его смертью на каждом пире сам.

        /// <summary>Сервер: изменился объём поглощающего щита юнита (0 — щит снят).</summary>
        public void UnitShieldSend(Unit unit, float amount)
        {
            if (!CanSendAbout(unit)) return;
            UnitShieldClientRpc(unit.netID, amount);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void UnitShieldClientRpc(UInt16 netID, float amount)
        {
            if (NetworkConnectionHandler.Instance != null && NetworkConnectionHandler.Instance.connectionStage == 2) return;
            if (GameManager.Instance == null) return; // кадр выгрузки сцены
            if (!TryResolveUnit(netID, "UnitShieldSend", out Unit unit)) return;

            SkillPresentationEvents.RaiseShieldChanged(unit, amount);
        }

        // ============================== ОБЩЕЕ ==============================

        // [Interflow fix 2026-08-23 status-send-gate] Общая преамбула отправки канала (правило 5):
        // раньше четыре метода повторяли одну и ту же пару проверок, а про юнита, умершего в этом же
        // вызове (удар убил цель — Die уже снял netID из реестра на всех пирах), сообщение всё равно
        // уходило: клиент печатал «Desync! Unit netID… should exist on client». Разбор:
        // Документы/Аудиты/Анализ_Рассинхрон_Состояние_На_Трупе.md. Парно к запрету
        // «эффектор на мёртвого» в Effector.EffectorAdd.

        /// <summary>Сервер запущен и слушает — базовое условие любой отправки канала.</summary>
        static bool ServerCanSend()
        {
            return NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && NetworkManager.Singleton.IsListening;
        }

        /// <summary>Юнит всё ещё числится в реестре netID — зеркало клиентского TryResolveUnit:
        /// смерть снимает номер на всех пирах (SlotManager.RemoveNetID из Unit.Die), про снятого
        /// с учёта клиентам не рассказываем. Сравнение экземпляра — номер мог быть перезаписан.</summary>
        static bool StillRegistered(Unit unit)
        {
            return unit != null
                && SlotManager.Instance.unitNetID.TryGetValue(unit.netID, out Unit registered)
                && registered == unit;
        }

        /// <summary>Полная преамбула сообщения «про конкретного юнита»: сервер слушает, юнит на учёте.</summary>
        static bool CanSendAbout(Unit unit)
        {
            return ServerCanSend() && StillRegistered(unit);
        }

        static bool TryResolveUnit(UInt16 netID, string source, out Unit unit)
        {
            if (!SlotManager.Instance.unitNetID.TryGetValue(netID, out unit) || unit == null)
            {
                Debug.LogError("Desync! Unit netID:" + netID + " should exist on client, but does not! (" + source + " NetworkDataSync)");
                unit = null;
                return false;
            }
            return !unit.dead;
        }
    }

    /// <summary>Флаги наших состояний юнита, идущие единым каналом статусов (не эффекторы и не статусы ядра).</summary>
    public enum UnitStatusFlag : byte
    {
        /// <summary>Слепота: носитель промахивается атаками (BlindDebuff + InterflowCombat).</summary>
        Blind = 0,
    }
}
