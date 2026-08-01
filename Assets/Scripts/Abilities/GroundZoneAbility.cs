using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Выставить на землю зону-ловушку (кирпич B8 <see cref="GroundDamageZone"/>): колья, огненный ковёр, шипы.
    /// Потребитель: Тир 4 [Б] «Защитный частокол» (колья вокруг баллисты — урон и остановка кавалерии).
    ///
    /// Тип способности выбирается настройкой: «Вокруг себя» (Active — автокаст по откату)
    /// или «В точку» (Location — по указанной позиции).
    /// Штатные поля Ability: <c>cooldown</c> — как часто выставляется, <c>castRange</c> — дальность для варианта «В точку».
    /// </summary>
    [CreateAssetMenu(fileName = "GroundZoneAbility", menuName = "StrategyCore/Abilities/Interflow/GroundZoneAbility (Зона на земле)")]
    public class GroundZoneAbility : Ability
    {
        [Header("Куда ставится зона")]
        [Tooltip("ВКЛ — зона появляется вокруг носителя (тип Active, удобно для автокаста по откату). ВЫКЛ — в указанную точку (тип Location).")]
        public bool aroundSelf = true;

        public override AbilityType type { get { return aroundSelf ? AbilityType.Active : AbilityType.Location; } }

        [Header("Зона")]
        [Tooltip("Префаб зоны. Должен нести компонент GroundDamageZone (радиус, урон, эффекторы, оглушение — настраиваются на самом префабе).")]
        public GameObject zonePrefab;

        [Tooltip("Смещение зоны вперёд от носителя, метры. Для кольев перед орудием. Работает в режиме «вокруг себя».")]
        public float forwardOffset = 0f;

        [Tooltip("Сколько зон ставится за один каст (например частокол из нескольких участков).")]
        [Min(1)]
        public int zoneCount = 1;

        [Tooltip("Разброс зон вокруг точки установки, метры. Имеет смысл при количестве больше одной.")]
        public float spread = 1.5f;

        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            if (!aroundSelf) return;
            if (castingUnit == null) return;

            Vector3 center = castingUnit.transform.position + castingUnit.transform.forward * forwardOffset;
            SpawnZones(center, castingPlayer);
        }

        public override void Use(Unit castingUnit, int castingPlayer, int level, Vector3 location)
        {
            if (aroundSelf) return;

            SpawnZones(location, castingPlayer);
        }

        void SpawnZones(Vector3 center, int owner)
        {
            if (NetworkConnectionHandler.isClient) return; // правило 6
            if (zonePrefab == null)
            {
                Debug.LogWarning("[GroundZoneAbility] Не задан префаб зоны — каст пропущен.");
                return;
            }

            for (int i = 0; i < zoneCount; i++)
            {
                Vector3 pos = center;
                if (zoneCount > 1 && spread > 0f)
                {
                    Vector2 offset = Random.insideUnitCircle * spread;
                    pos += new Vector3(offset.x, 0f, offset.y);
                }

                GameObject go = Instantiate(zonePrefab, pos, Quaternion.identity);

                GroundDamageZone zone = go.GetComponent<GroundDamageZone>();
                if (zone != null) zone.SetOwner(owner);
                else Debug.LogWarning("[GroundZoneAbility] На префабе зоны нет компонента GroundDamageZone — зона не будет действовать.");
            }
        }
    }
}
