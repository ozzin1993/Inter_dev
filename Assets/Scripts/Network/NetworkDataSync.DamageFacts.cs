using System;
using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// [Interflow 2026-09-11] Партиал NetworkDataSync: РАЗБОР ЗАПИСИ УРОНА. Решение Artsiom 11.09.2026 —
    /// детали удара едут клиенту одним сводным сообщением на запись, а не россыпью отдельных причин факта.
    ///
    /// Зачем: по одному итоговому числу не видно, из чего оно сложилось. Заявленное число, правила жертвы,
    /// подписки (поглощающий щит), броня с таблицей типов — четыре ступени, и в ленте боя нужна каждая.
    /// Отдельными причинами это стоило бы четырёх сообщений на запись; здесь одно.
    ///
    /// Этим же сообщением приезжает СВЁРНУТАЯ периодика (горение, аура, зона на земле): у неё число тиков
    /// больше нуля, а ступени расчёта не заполнены — копить их по тикам смысла нет (см. PeriodicDamageRollup).
    ///
    /// Новый partial-файл, а не строки в NetworkDataSync.UnitStatus.cs — правило 22: тот файл и так
    /// держит пять механик канала статусов.
    /// </summary>
    public partial class NetworkDataSync
    {
        /// <summary>
        /// Сервер: разобрана одна запись урона (или готова свёртка периодического источника).
        /// Шлёт клиентам И поднимает факт локально: сообщение идёт SendTo.NotServer и до хоста не доходит,
        /// а показывать разбор хосту надо так же (образец — разовый факт боя).
        /// </summary>
        public void UnitDamageStepSend(in DamageStepInfo info)
        {
            if (!CanSendAbout(info.victim)) return;

            // Бьющий необязателен: зона на земле и отложенный урон юнита-источника не имеют,
            // а живой бьющий мог погибнуть в этом же вызове (конвенция «0 — нет ссылки»).
            UInt16 attackerID = StillRegistered(info.attacker) ? info.attacker.netID : (UInt16)0;

            DamageStepMessage m;
            m.victimID = info.victim.netID;
            m.attackerID = attackerID;
            m.abilityID = info.abilityID;
            m.hitId = info.hitId;
            m.recordIndex = (byte)Mathf.Clamp(info.recordIndex, 0, 255);
            m.recordCount = (byte)Mathf.Clamp(info.recordCount, 0, 255);
            m.declared = info.declared;
            m.afterRules = info.afterRules;
            m.afterCallbacks = info.afterCallbacks;
            m.afterArmor = info.afterArmor;
            m.dealt = info.dealt;
            m.healthBefore = info.healthBefore;
            m.healthAfter = info.healthAfter;
            m.healthMax = info.healthMax;
            m.ticks = info.ticks;
            m.died = info.died;

            UnitDamageStepClientRpc(m);
            SkillPresentationEvents.RaiseDamageStep(in info);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void UnitDamageStepClientRpc(DamageStepMessage m)
        {
            // Подключение в середине матча: принимаем только данные сцены (штатное правило RPC этого хаба).
            if (NetworkConnectionHandler.Instance != null && NetworkConnectionHandler.Instance.connectionStage == 2) return;
            if (GameManager.Instance == null) return; // кадр выгрузки сцены
            // Мёртвую цель принимаем: смертельная строка отправляется ДО Die, но у клиента смерть
            // могла уже примениться своим путём, и штатный резолв отбросил бы самую важную строку удара.
            if (!TryResolveUnit(m.victimID, "UnitDamageStepSend", out Unit victim, true)) return;

            // Бьющего резолвим МОЛЧА: он мог погибнуть между отправкой и приёмом, и его номер у клиента
            // уже снят — это штатный случай, а не рассинхрон. Без него строка лишится имени слева.
            Unit attacker = m.attackerID != 0 ? ResolveUnitQuiet(m.attackerID) : null;

            DamageStepInfo info;
            info.victim = victim;
            info.attacker = attacker;
            info.abilityID = m.abilityID;
            info.hitId = m.hitId;
            info.recordIndex = m.recordIndex;
            info.recordCount = m.recordCount;
            info.declared = m.declared;
            info.afterRules = m.afterRules;
            info.afterCallbacks = m.afterCallbacks;
            info.afterArmor = m.afterArmor;
            info.dealt = m.dealt;
            info.healthBefore = m.healthBefore;
            info.healthAfter = m.healthAfter;
            info.healthMax = m.healthMax;
            info.ticks = m.ticks;
            info.died = m.died;

            SkillPresentationEvents.RaiseDamageStep(in info);
        }
    }

    /// <summary>
    /// Сообщение разбора записи урона. Отдельный сериализуемый тип, а не восемнадцать параметров вызова:
    /// столько аргументов у сообщения читать невозможно, а состав ступеней ещё и меняется по ходу работы
    /// над показом боя. Ссылки на юнитов едут сетевыми номерами (конвенция ядра «0 — нет ссылки»).
    /// </summary>
    public struct DamageStepMessage : INetworkSerializable
    {
        /// <summary>Сетевой номер цели.</summary>
        public UInt16 victimID;

        /// <summary>Сетевой номер бьющего. 0 — источника-юнита нет.</summary>
        public UInt16 attackerID;

        /// <summary>Умение-источник. −1 — умения нет.</summary>
        public int abilityID;

        /// <summary>Номер удара. 0 — свёрнутая периодика.</summary>
        public int hitId;

        /// <summary>Номер записи в пакете, с нуля.</summary>
        public byte recordIndex;

        /// <summary>Сколько записей урона в пакете.</summary>
        public byte recordCount;

        /// <summary>Заявлено записью до расчётов.</summary>
        public float declared;

        /// <summary>После правил входящего урона жертвы.</summary>
        public float afterRules;

        /// <summary>После подписок (щит и прочие снижения).</summary>
        public float afterCallbacks;

        /// <summary>После брони и таблицы типов.</summary>
        public float afterArmor;

        /// <summary>Снято здоровья на самом деле.</summary>
        public float dealt;

        /// <summary>Здоровье цели до записи.</summary>
        public float healthBefore;

        /// <summary>Здоровье цели после записи.</summary>
        public float healthAfter;

        /// <summary>Предельное здоровье цели.</summary>
        public float healthMax;

        /// <summary>Сколько тиков свёрнуто. 0 — разовый удар.</summary>
        public int ticks;

        /// <summary>Погибла ли цель от этой записи.</summary>
        public bool died;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref victimID);
            serializer.SerializeValue(ref attackerID);
            serializer.SerializeValue(ref abilityID);
            serializer.SerializeValue(ref hitId);
            serializer.SerializeValue(ref recordIndex);
            serializer.SerializeValue(ref recordCount);
            serializer.SerializeValue(ref declared);
            serializer.SerializeValue(ref afterRules);
            serializer.SerializeValue(ref afterCallbacks);
            serializer.SerializeValue(ref afterArmor);
            serializer.SerializeValue(ref dealt);
            serializer.SerializeValue(ref healthBefore);
            serializer.SerializeValue(ref healthAfter);
            serializer.SerializeValue(ref healthMax);
            serializer.SerializeValue(ref ticks);
            serializer.SerializeValue(ref died);
        }
    }
}
