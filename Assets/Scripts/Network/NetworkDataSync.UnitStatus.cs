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
            if (unit == null) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || !NetworkManager.Singleton.IsListening) return;
            UnitStatusEffectorClientRpc(unit.netID, effectorId, duration);
        }

        [Rpc(SendTo.NotServer)]
        private void UnitStatusEffectorClientRpc(UInt16 netID, int effectorId, float duration)
        {
            // Подключение в середине матча: принимаем только данные сцены (штатное правило RPC этого хаба).
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;
            if (GameManager.instance == null) return; // кадр выгрузки сцены
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
            if (unit == null) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || !NetworkManager.Singleton.IsListening) return;
            UnitStatusEffectorRemoveClientRpc(unit.netID, effectorId);
        }

        [Rpc(SendTo.NotServer)]
        private void UnitStatusEffectorRemoveClientRpc(UInt16 netID, int effectorId)
        {
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;
            if (!TryResolveUnit(netID, "UnitStatusEffectorRemoveSend", out Unit unit)) return;

            SkillVisualStatus.RemoveEffector(unit, effectorId);
        }

        // ============================== ФЛАГИ НАШИХ СОСТОЯНИЙ ==============================

        /// <summary>Сервер: включить/выключить у юнита флаг состояния без эффектора (например, слепоту).</summary>
        public void UnitStatusFlagSend(Unit unit, UnitStatusFlag flag, bool state)
        {
            if (unit == null) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || !NetworkManager.Singleton.IsListening) return;
            UnitStatusFlagClientRpc(unit.netID, (byte)flag, state);
        }

        [Rpc(SendTo.NotServer)]
        private void UnitStatusFlagClientRpc(UInt16 netID, byte flag, bool state)
        {
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;
            if (!TryResolveUnit(netID, "UnitStatusFlagSend", out Unit unit)) return;

            SkillVisualStatus.SetFlag(unit, (UnitStatusFlag)flag, state);
        }

        // ============================== VFX БАФА СКИЛЛА ==============================
        // Значка у бафа нет (значок даёт эффектор-статус), поэтому это отдельное сообщение канала.

        /// <summary>Сервер: показать клиентам VFX длящегося бафа скилла на юнитах. Одно сообщение на каст.</summary>
        public void SkillBuffVfxSend(UInt16[] netIDs, int buffAbilityId, int buffAbilityLevel, float buffDuration)
        {
            if (netIDs == null || netIDs.Length == 0 || buffAbilityId < 0) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || !NetworkManager.Singleton.IsListening) return;
            SkillBuffVfxClientRpc(netIDs, buffAbilityId, buffAbilityLevel, buffDuration);
        }

        [Rpc(SendTo.NotServer)]
        private void SkillBuffVfxClientRpc(UInt16[] netIDs, int buffAbilityId, int buffAbilityLevel, float buffDuration)
        {
            if (NetworkConnectionHandler.instance.connectionStage == 2) return;
            if (GameManager.instance == null) return;

            // Разбираем описание бафа один раз на всё сообщение, а не на каждую цель.
            VFXReferencer buffVfx = null;
            float auraRadius = 0f;
            if (GameManager.instance.gameAbilities.TryGetValue(buffAbilityId, out Ability ability)
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
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || !NetworkManager.Singleton.IsListening) return;

            UInt16 casterID = caster == null ? (UInt16)0 : caster.netID;
            UInt16 aimID = aimUnit == null ? (UInt16)0 : aimUnit.netID;
            SkillFiredClientRpc(casterID, abilityID, level, aimID, aimPoint);
        }

        [Rpc(SendTo.NotServer)]
        private void SkillFiredClientRpc(UInt16 casterID, int abilityID, int level, UInt16 aimID, Vector3 aimPoint)
        {
            if (NetworkConnectionHandler.instance != null && NetworkConnectionHandler.instance.connectionStage == 2) return;
            if (GameManager.instance == null) return; // кадр выгрузки сцены

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
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || !NetworkManager.Singleton.IsListening) return;
            GroundZoneSpawnClientRpc(zoneId, abilityID, level, position);
        }

        [Rpc(SendTo.NotServer)]
        private void GroundZoneSpawnClientRpc(int zoneId, int abilityID, int level, Vector3 position)
        {
            // Подключение в середине матча: принимаем только данные сцены (штатное правило RPC этого хаба).
            // Живые зоны опоздавшему клиенту досылаются отдельно, по завершении подключения.
            if (NetworkConnectionHandler.instance != null && NetworkConnectionHandler.instance.connectionStage == 2) return;
            if (GameManager.instance == null) return; // кадр выгрузки сцены

            SkillPresentationEvents.RaiseZoneSpawned(zoneId, abilityID, level, position);
        }

        /// <summary>Сервер: зона на земле закончила жизнь (истекла или снята).</summary>
        public void GroundZoneDespawnSend(int zoneId)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || !NetworkManager.Singleton.IsListening) return;
            GroundZoneDespawnClientRpc(zoneId);
        }

        [Rpc(SendTo.NotServer)]
        private void GroundZoneDespawnClientRpc(int zoneId)
        {
            if (NetworkConnectionHandler.instance != null && NetworkConnectionHandler.instance.connectionStage == 2) return;

            SkillPresentationEvents.RaiseZoneDespawned(zoneId);
        }

        /// <summary>
        /// Сервер: догнать ОДНОГО клиента живыми зонами. Обычные сообщения он отбросил, пока грузил сцену
        /// (`connectionStage == 2`), поэтому у досыла гейта стадии НЕТ — это и есть данные сцены.
        /// </summary>
        public void GroundZoneResendSend(ulong clientID, int zoneId, int abilityID, int level, Vector3 position)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || !NetworkManager.Singleton.IsListening) return;
            GroundZoneResendClientRpc(zoneId, abilityID, level, position, RpcTarget.Single(clientID, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void GroundZoneResendClientRpc(int zoneId, int abilityID, int level, Vector3 position, RpcParams rpcParams = default)
        {
            if (GameManager.instance == null) return;

            SkillPresentationEvents.RaiseZoneSpawned(zoneId, abilityID, level, position);
        }

        // ============================== ОБЩЕЕ ==============================

        static bool TryResolveUnit(UInt16 netID, string source, out Unit unit)
        {
            if (!SlotManager.instance.unitNetID.TryGetValue(netID, out unit) || unit == null)
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
