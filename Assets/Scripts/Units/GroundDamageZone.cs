using UnityEngine;

namespace StrategyCore
{
    // Кирпич B8 — длящаяся зона урона на земле. ПРОСТОЙ префаб (НЕ Unit → нет NavMeshObstacle/carving, путей не
    // перекрывает; прецедент graves). Каждый штатный тик бьёт врагов владельца в радиусе; живёт duration, затем
    // самоудаляется. Цикл — образец FlameCloakBuff; лайфтайм — свой (как graves/CallToArms). Урон — только сервер
    // (правило 6). Клиентский синк визуала СДЕЛАН 2026-08-06: сервер держит реестр (MatchManager.GroundZones),
    // клиент получает факт и спавнит ТОТ ЖЕ префаб — на клиенте Start выходит сразу, остаётся чистый визуал.
    // Числа — в Inspector (правило 3).
    public class GroundDamageZone : MonoBehaviour
    {
        [Header("Зона урона (B8)")]
        [Tooltip("Радиус зоны (клетки → юниты Unity). [БАЛАНС — Влад]")]
        [SerializeField] private float radius = 3f;
        [Tooltip("Урон в секунду по целям в зоне. [БАЛАНС — Влад]")]
        [SerializeField] private float damagePerSecond = 10f;
        [Tooltip("Тип урона зоны")]
        [SerializeField] private DamageType damageType;
        [Tooltip("Кого задевает зона (обычно враги владельца: Enemy + Unit + Ground/Water/Air)")]
        [SerializeField] private UnitSelector targetSelector;
        [Tooltip("Длительность жизни зоны, сек. [БАЛАНС — Влад]")]
        [SerializeField] private float duration = 5f;
        [Tooltip("Игрок-владелец зоны (для определения врагов). Спавнер задаёт через SetOwner; для теста — здесь.")]
        [SerializeField] private int ownerPlayer = 0;

        [Header("Эффекты по входящим (добавлено 2026-07-24 — «Защитный частокол»)")]
        [Tooltip("Эффекторы, накладываемые целям в зоне (замедление, кровотечение). Обновляются каждый тик — длительность у эффектора должна быть короткой.")]
        [SerializeField] private Effector[] zoneEffectors;
        [Tooltip("Оглушение цели при ПЕРВОМ попадании в зону, сек. 0 — не оглушать. Уважает иммунитет к контролю. Нужно для «колья останавливают кавалерию».")]
        [SerializeField] private float stunOnEnterSeconds = 0f;
        [Tooltip("Оглушать только цели этой категории (например Боец — кавалерия помечается им же). Снимите галку ниже, чтобы оглушать всех.")]
        [SerializeField] private Unit.UnitCategory stunOnlyCategory = Unit.UnitCategory.Fighter;
        [Tooltip("Оглушать только выбранную категорию. ВЫКЛ — оглушение для всех, кто вошёл.")]
        [SerializeField] private bool stunOnlySelectedCategory = false;

        // Кого уже оглушили при входе — чтобы стан не повторялся каждый тик, пока цель стоит в зоне
        private readonly System.Collections.Generic.HashSet<Unit> stunnedOnEnter = new System.Collections.Generic.HashSet<Unit>();

        private float remaining;
        private bool subscribed;
        private bool started;

        // id зоны в серверном реестре (MatchManager.GroundZones); 0 — не зарегистрирована.
        // На клиентской копии остаётся нулём: реестр ведётся только на сервере.
        private int zoneId;

        /// <summary>Задать владельца до старта (серверный спавнер зовёт сразу после Instantiate).</summary>
        public void SetOwner(int owner) { ownerPlayer = owner; }

        /// <summary>Задать id реестра. Зовёт серверный реестр сразу после регистрации зоны.</summary>
        public void SetZoneId(int id) { zoneId = id; }

        // Старт отсчёта/тика — только сервер (правило 6). Зона спавнится по ходу матча (GameManager готов).
        private void Start()
        {
            if (started || NetworkConnectionHandler.isClient || GameManager.Instance == null) return;
            started = true;
            remaining = duration;
            GameManager.Instance.Tick += OnTick;
            subscribed = true;
        }

        private void OnTick()
        {
            if (GameManager.Instance == null) return;
            float dt = GameManager.Instance.currentDeltaTime;

            bool needScan = damagePerSecond != 0f
                            || (zoneEffectors != null && zoneEffectors.Length > 0)
                            || stunOnEnterSeconds > 0f;

            if (needScan)
            {
                Vector2 pos = new Vector2(transform.position.x, transform.position.z);
                // Урон как «средовой»: byUnit=null, автор = ownerPlayer (для killerPlayer в OnDie). directAttack=false.
                Unit[] targets = Utils.GetUnitsInRadius(pos, radius, ownerPlayer, targetSelector, -1, null);
                if (targets != null)
                {
                    for (int i = 0; i < targets.Length; i++)
                    {
                        Unit t = targets[i];
                        if (t == null || t.dead) continue;

                        if (damagePerSecond != 0f) t.GetDamage(damagePerSecond * dt, damageType, ownerPlayer, null, false, out _);
                        if (t.dead) continue; // цель могла погибнуть от этого же урона — по трупу не работаем

                        if (zoneEffectors != null && zoneEffectors.Length > 0) Effector.EffectorAdd(ownerPlayer, t, zoneEffectors);

                        // Оглушение — один раз на вход в зону (иначе цель стояла бы в стане вечно)
                        if (stunOnEnterSeconds > 0f && !stunnedOnEnter.Contains(t))
                        {
                            bool allowed = !stunOnlySelectedCategory || t.unitCategory == stunOnlyCategory;
                            if (allowed)
                            {
                                t.Stun(stunOnEnterSeconds); // штатный стан уважает ControlImmunity
                                stunnedOnEnter.Add(t);
                            }
                        }
                    }
                }
            }

            remaining -= dt;
            if (remaining <= 0f) Cleanup();
        }

        // Деспавн зоны один раз: отписать тик (урон прекращается ДО Destroy) + уничтожить префаб.
        private void Cleanup()
        {
            if (subscribed && GameManager.Instance != null) GameManager.Instance.Tick -= OnTick;
            subscribed = false;

            // Единственная точка смерти зоны — здесь же гасим её копию у клиентов.
            // Cleanup достижим только на сервере (на клиенте тик не подписан), отдельный гейт не нужен.
            if (zoneId != 0)
            {
                if (MatchManager.Instance != null) MatchManager.Instance.UnregisterGroundZone(zoneId);
                zoneId = 0;
            }

            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (subscribed && GameManager.Instance != null) GameManager.Instance.Tick -= OnTick;
            subscribed = false;
        }
    }
}
