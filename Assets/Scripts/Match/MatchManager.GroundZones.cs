using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ============================= ЗОНЫ НА ЗЕМЛЕ (партиал MatchManager) ==
    // Серверный реестр живых зон (кирпич B8, GroundDamageZone). Серверо-авторитетно (правило 6):
    // зону создаёт и убивает только сервер, клиенту уезжает ФАКТ (NetworkDataSync.UnitStatus →
    // SkillPresentationEvents), а копию префаба рисует клиентский презентер.
    // Образец — MatchManager.Graves (реестр + спавн/деспавн визуала по id).
    // Реестр нужен и сам по себе: по нему досылаются живые зоны опоздавшему клиенту (late-join).
    // Партиал вместо нового менеджера (правила 5, 22).
    public partial class MatchManager
    {
        // Запись живой зоны: ровно тот минимум, которого клиенту хватает, чтобы её нарисовать
        // (в том числе при досыле). Ссылку на сам объект не держим — зона самоуничтожается
        // и сообщает об этом сама (GroundDamageZone.Cleanup → UnregisterGroundZone).
        class GroundZoneRecord
        {
            public int zoneId;
            public int abilityID;   // умение-источник: по нему клиент берёт префаб зоны из ассета
            public int level;       // уровень умения на момент каста — клиент им считает размер визуала
            public Vector3 position;
        }

        // Реестр живых зон (единый источник, правило 5). Заполняется только на сервере.
        readonly List<GroundZoneRecord> groundZones = new List<GroundZoneRecord>();
        int nextGroundZoneId = 1;   // возрастающий id зоны (0 — «не зарегистрирована»)

        /// <summary>
        /// Сервер: зарегистрировать только что созданную зону и разослать факт клиентам.
        /// Зовётся из CompositeSkill.ApplyGroundZone сразу после Instantiate.
        /// Позиция передаётся ФАКТИЧЕСКАЯ — разброс zoneCount/spread рандомится здесь же, на сервере,
        /// и клиент его не воспроизвёл бы.
        /// </summary>
        public void RegisterGroundZone(GameObject zoneObject, int abilityID, int level, Vector3 position)
        {
            if (NetworkConnectionHandler.isClient) return;   // реестр и рассылка — только сервер (правило 6)
            if (zoneObject == null) return;

            GroundZoneRecord rec = new GroundZoneRecord
            {
                zoneId = nextGroundZoneId++,
                abilityID = abilityID,
                level = level,
                position = position
            };
            groundZones.Add(rec);

            // Зона сообщит о своей смерти по этому id.
            GroundDamageZone zone = zoneObject.GetComponent<GroundDamageZone>();
            if (zone != null) zone.SetZoneId(rec.zoneId);

            if (NetworkDataSync.Instance != null)
                NetworkDataSync.Instance.GroundZoneSpawnSend(rec.zoneId, rec.abilityID, rec.level, rec.position);
        }

        /// <summary>
        /// Сервер: догнать подключившегося по ходу матча клиента всеми живыми зонами.
        /// Обычные сообщения о спавне он не принимал, пока грузил сцену, — поэтому реестр и нужен.
        /// </summary>
        public void ResendGroundZonesTo(ulong clientID)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (groundZones.Count == 0 || NetworkDataSync.Instance == null) return;

            for (int i = 0; i < groundZones.Count; i++)
            {
                GroundZoneRecord rec = groundZones[i];
                NetworkDataSync.Instance.GroundZoneResendSend(clientID, rec.zoneId, rec.abilityID, rec.level, rec.position);
            }
        }

        /// <summary>
        /// Сервер: зона закончила жизнь — убрать из реестра и погасить копию у клиентов.
        /// Единственный вызыватель — сама зона из Cleanup (её единственная точка смерти).
        /// </summary>
        public void UnregisterGroundZone(int zoneId)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (zoneId == 0) return;

            for (int i = groundZones.Count - 1; i >= 0; i--)
            {
                if (groundZones[i].zoneId != zoneId) continue;
                groundZones.RemoveAt(i);
                if (NetworkDataSync.Instance != null) NetworkDataSync.Instance.GroundZoneDespawnSend(zoneId);
                return;
            }
        }
    }
}
