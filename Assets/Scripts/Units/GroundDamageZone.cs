using UnityEngine;

namespace StrategyCore
{
    // Кирпич B8 — длящаяся зона урона на земле. ПРОСТОЙ префаб (НЕ Unit → нет NavMeshObstacle/carving, путей не
    // перекрывает; прецедент graves). Каждый штатный тик бьёт врагов владельца в радиусе; живёт duration, затем
    // самоудаляется. Цикл — образец FlameCloakBuff; лайфтайм — свой (как graves/CallToArms). Урон — только сервер
    // (правило 6). Клиентский синк визуала СДЕЛАН 2026-08-06: сервер держит реестр (MatchManager.GroundZones),
    // клиент получает факт и спавнит ТОТ ЖЕ префаб — на клиенте Start выходит сразу, остаётся чистый визуал.
    // Числа — в Inspector (правило 3).
    // [Interflow 2026-09-05, блок Б7] Зона с НОСИТЕЛЕМ — это «аура на время» целевой модели (§9): та же зона,
    // но каждый тик переставляется в позицию носителя и гаснет вместе с ним (решение Artsiom 05.09). Носителя задаёт
    // серверный спавнер (SetCarrier); клиентская копия следует за юнитом сама — презентер вешает её на его transform.
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

        // Носитель («аура на время»): зона идёт за ним и гаснет с ним. hasCarrier отдельно от ссылки:
        // уничтоженный юнит Unity сравнивает с null как null, и зона без флага молча осталась бы стоять в точке.
        private Unit carrier;
        private bool hasCarrier;

        // Умение-источник для диагностики очереди пакетов (решение Artsiom 05.09.2026); null — без умения.
        private Ability sourceAbility;

        /// <summary>Задать владельца до старта (серверный спавнер зовёт сразу после Instantiate).</summary>
        public void SetOwner(int owner) { ownerPlayer = owner; }

        /// <summary>Задать умение-источник до старта (серверный спавнер, для диагностики очереди пакетов). null — без умения.</summary>
        public void SetSource(Ability source) { sourceAbility = source; }

        /// <summary>
        /// Задать носителя до старта (серверный спавнер, блок «зона на земле» с галкой «идёт за кастером»).
        /// Зона каждый тик встаёт в позицию носителя; носитель погиб или пропал — зона гаснет тем же тиком.
        /// </summary>
        public void SetCarrier(Unit unit)
        {
            carrier = unit;
            hasCarrier = unit != null;
        }

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

            // Зона с носителем: сначала догоняем его, а если он погиб или пропал — гаснем, не сканируя
            // (решение Artsiom 05.09: аура на время умирает вместе с носителем).
            if (hasCarrier)
            {
                if (carrier == null || carrier.dead) { Cleanup(); return; }
                transform.position = carrier.transform.position;
            }

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

                        if (damagePerSecond != 0f)
                        {
                            DamagePacket packet = DamagePacket.Create(damagePerSecond * dt, damageType, ownerPlayer, null, false, sourceAbility, null, true);   // [Interflow fix 2026-09-04 damage-full-packet] пакет одной записи   // [Interflow 2026-09-11] periodic: тик по времени — в ленту идёт свёрнутой строкой, не строкой на тик
                            t.GetDamage(in packet, out _);
                        }
                        if (t.dead) continue; // цель могла погибнуть от этого же урона — по трупу не работаем

                        if (zoneEffectors != null && zoneEffectors.Length > 0) Effector.EffectorAdd(ownerPlayer, t, zoneEffectors);

                        // Оглушение — один раз на вход в зону (иначе цель стояла бы в стане вечно)
                        if (stunOnEnterSeconds > 0f && !stunnedOnEnter.Contains(t))
                        {
                            bool allowed = !stunOnlySelectedCategory || t.unitCategory == stunOnlyCategory;
                            if (allowed)
                            {
                                // Юнита-источника у зоны нет (урон она наносит так же — byUnit=null),
                                // владельцем идёт её игрок. Иммунитет к контролю спросит наложение состояния.
                                t.Stun(stunOnEnterSeconds, null, ownerPlayer);
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
