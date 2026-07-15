using UnityEngine;

namespace StrategyCore
{
    // ============================= ПОСМЕРТНЫЕ ЭФФЕКТЫ (кирпич B15) ==
    // Компонент на префабе юнита. При смерти (Unit.OnDie, сервер) по включённым блокам, в месте смерти:
    //   1) урон врагам в радиусе;  2) лечение союзников (ChangeHP);  3) зона урона на земле (B8 GroundDamageZone);
    //   4) призыв юнита (reuse MatchManager.SummonFromUnit — B16, net-zero лидерство).
    // Каждый блок независим — включается заданием своих полей (урон/хил > 0 или назначенный префаб). Числа —
    // только в Inspector (правило 3). Серверо-авторитетно (правило 6). Ассет StrategyCore не трогаем (правило 1).
    // Позицию смерти фиксируем сразу (не держим ссылку на умирающего для спавна — тайминг отложенного Destroy, §риски).
    [RequireComponent(typeof(Unit))]
    public class DeathEffects : MonoBehaviour
    {
        [Header("Общее (B15)")]
        [Tooltip("Радиус эффектов урона/лечения вокруг места смерти, мировые единицы. [БАЛАНС — Влад]")]
        [SerializeField] private float radius = 3f;

        [Header("Урон врагам при смерти")]
        [Tooltip("Урон врагам в радиусе (0 = блок выключен). [БАЛАНС — Влад]")]
        [SerializeField] private float enemyDamage = 0f;
        [Tooltip("Тип урона по врагам (обязателен, если урон > 0).")]
        [SerializeField] private DamageType enemyDamageType;
        [Tooltip("Селектор врагов (обычно Enemy + Unit + Ground/Water/Air).")]
        [SerializeField] private UnitSelector enemySelector;

        [Header("Лечение союзников при смерти")]
        [Tooltip("Лечение союзникам в радиусе, ХП (0 = блок выключен). [БАЛАНС — Влад]")]
        [SerializeField] private float allyHeal = 0f;
        [Tooltip("Селектор союзников (обычно Ally + Unit + Ground/Water/Air).")]
        [SerializeField] private UnitSelector allySelector;

        [Header("Зона урона на земле (B8)")]
        [Tooltip("Префаб зоны (компонент GroundDamageZone) в месте смерти (пусто = блок выключен). Владелец = владелец носителя.")]
        [SerializeField] private GameObject zonePrefab;

        [Header("Призыв юнита при смерти (B16)")]
        [Tooltip("Префаб призываемого юнита в месте смерти (пусто = блок выключен). Через SummonFromUnit (net-zero лидерство).")]
        [SerializeField] private Unit summonPrefab;
        [Tooltip("Сколько юнитов призвать при смерти. [БАЛАНС — Влад]")]
        [SerializeField] private int summonCount = 1;
        [Tooltip("Время жизни призванных, сек (0 = постоянные). [БАЛАНС — Влад]")]
        [SerializeField] private float summonLifetime = 0f;
        [Tooltip("Призванные слушают общие приказы Атака/Защита команды.")]
        [SerializeField] private bool summonObeyCommands = true;
        [Tooltip("Команда призванных, если общие приказы НЕ слушают (Атака/Защита).")]
        [SerializeField] private BottomTableAction summonCommand = BottomTableAction.Attack;
        [Tooltip("Разнос точек спавна при count > 1, мировые единицы.")]
        [SerializeField] private float summonSpawnSpread = 1f;

        private Unit unit;
        private bool subscribed;

        private void Awake()
        {
            unit = GetComponent<Unit>();
            if (unit == null) { enabled = false; return; }
            unit.OnDie += OnUnitDie;
            subscribed = true;
        }

        // Смерть носителя. OnDie летит и на клиенте (DieClientRpc) → гейт isClient (правило 6).
        private void OnUnitDie(Unit dies, int killerPlayer, Unit killerUnit, bool rewards)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (unit == null) return;

            Vector3 deathPos = unit.transform.position;                       // фиксируем место смерти сразу
            Vector2 center = new Vector2(deathPos.x, deathPos.z);
            int owner = unit.owner;

            // 1) Урон врагам в радиусе. Источник МЁРТВ (unit.dead=true в OnDie) → бьём через target.GetDamage
            //    (byUnit=null, killerPlayer=owner — корректный автор для наград/баунти; без рекурсии AfterDamageDeal),
            //    как «средовой» урон GroundDamageZone. SkvernaExplosion.Detonate здесь неприменим: он наносит урон
            //    через dealer.DealDamage от умирающего юнита.
            if (enemyDamage > 0f && enemyDamageType == null)
                Debug.LogWarning("[B15 DeathEffects] Урон врагам задан (enemyDamage>0), но не задан enemyDamageType — блок урона выключен.");
            if (enemyDamage > 0f && enemyDamageType != null)
            {
                Unit[] enemies = Utils.GetUnitsInRadius(center, radius, owner, enemySelector, -1, unit);
                if (enemies != null)
                    for (int i = 0; i < enemies.Length; i++)
                        if (enemies[i] != null && !enemies[i].dead)
                            enemies[i].GetDamage(enemyDamage, enemyDamageType, owner, null, false, out _);
            }

            // 2) Лечение союзников (ChangeHP клампит до maxHealth и синкает). Себя-мёртвого не лечим (exclude = unit).
            if (allyHeal > 0f)
            {
                Unit[] allies = Utils.GetUnitsInRadius(center, radius, owner, allySelector, -1, unit);
                if (allies != null)
                    for (int i = 0; i < allies.Length; i++)
                        if (allies[i] != null && !allies[i].dead)
                            allies[i].ChangeHP(allyHeal);
            }

            // 3) Зона урона на земле (B8): инстанс префаба в месте смерти, владелец = владелец носителя.
            if (zonePrefab != null)
            {
                GameObject zoneGo = Instantiate(zonePrefab, deathPos, Quaternion.identity);
                GroundDamageZone zone = zoneGo.GetComponent<GroundDamageZone>();
                if (zone != null) zone.SetOwner(owner);
                else Debug.LogWarning("[B15 DeathEffects] zonePrefab без компонента GroundDamageZone — зона не активна.");
            }

            // 4) Призыв юнита от места смерти (reuse SummonFromUnit — B16; net-zero). killSummonsOnOwnerDeath=false
            //    (носитель уже мёртв), лимит не применяем (0). Носитель как caster даёт позицию/владельца/команду.
            if (summonPrefab != null && summonCount > 0 && MatchManager.instance != null)
                MatchManager.instance.SummonFromUnit(unit, summonPrefab, summonCount, summonLifetime,
                    summonObeyCommands, summonCommand, 0, false, summonSpawnSpread);
        }

        private void OnDestroy()
        {
            if (unit != null && subscribed) unit.OnDie -= OnUnitDie;
            subscribed = false;
        }
    }
}
