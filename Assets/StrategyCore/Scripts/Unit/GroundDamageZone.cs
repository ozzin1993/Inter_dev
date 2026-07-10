using UnityEngine;

namespace StrategyCore
{
    // Кирпич B8 — длящаяся зона урона на земле. ПРОСТОЙ префаб (НЕ Unit → нет NavMeshObstacle/carving, путей не
    // перекрывает; прецедент graves). Каждый штатный тик бьёт врагов владельца в радиусе; живёт duration, затем
    // самоудаляется. Цикл — образец FlameCloakBuff; лайфтайм — свой (как graves/CallToArms). Урон — только сервер
    // (правило 6). Клиентский синк визуала ОТЛОЖЕН (§6.3): визуал = сам префаб (виден на хосте), RPC — будущее.
    // Ассет StrategyCore не трогаем (правило 1). Числа — в Inspector (правило 3).
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

        private float remaining;
        private bool subscribed;
        private bool started;

        /// <summary>Задать владельца до старта (серверный спавнер зовёт сразу после Instantiate).</summary>
        public void SetOwner(int owner) { ownerPlayer = owner; }

        // Старт отсчёта/тика — только сервер (правило 6). Зона спавнится по ходу матча (GameManager готов).
        private void Start()
        {
            if (started || NetworkConnectionHandler.isClient || GameManager.instance == null) return;
            started = true;
            remaining = duration;
            GameManager.instance.Tick += OnTick;
            subscribed = true;
        }

        private void OnTick()
        {
            if (GameManager.instance == null) return;
            float dt = GameManager.instance.currentDeltaTime;

            if (damagePerSecond != 0f)
            {
                Vector2 pos = new Vector2(transform.position.x, transform.position.z);
                // Урон как «средовой»: byUnit=null, автор = ownerPlayer (для killerPlayer в OnDie). directAttack=false.
                Unit[] targets = Utils.GetUnitsInRadius(pos, radius, ownerPlayer, targetSelector, -1, null);
                if (targets != null)
                    for (int i = 0; i < targets.Length; i++)
                        if (targets[i] != null && !targets[i].dead)
                            targets[i].GetDamage(damagePerSecond * dt, damageType, ownerPlayer, null, false, out _);
            }

            remaining -= dt;
            if (remaining <= 0f) Cleanup();
        }

        // Деспавн зоны один раз: отписать тик (урон прекращается ДО Destroy) + уничтожить префаб.
        private void Cleanup()
        {
            if (subscribed && GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            subscribed = false;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (subscribed && GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            subscribed = false;
        }
    }
}
