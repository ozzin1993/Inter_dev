using UnityEngine;

namespace StrategyCore
{
    // ============================= АУРА ПО СОСТАВУ ЛИНИИ (кирпич B22) ==
    // Компонент на префабе-носителе. Пока у команды носителя ≥ threshold живых юнитов заданной категории —
    // носитель раздаёт эффектор(ы) в радиусе вокруг себя (обновляется тиком, как EffectorAura). Подсчёт живых —
    // через MatchManager.LineComposition (событийный счётчик per-team, правило 5). Порог не достигнут — раздачи
    // нет, короткий эффектор истекает сам (≈2×tickRate) → «снятие». Тайминг — GameManager.Tick (образец
    // FlameCloakBuff). Серверо-авторитетно (правило 6). Ассет StrategyCore не трогается (правило 1). Числа — Inspector.
    [RequireComponent(typeof(Unit))]
    public class LineCompositionAura : MonoBehaviour
    {
        [Header("Аура по составу линии (B22)")]
        [Tooltip("Категория юнитов, которых считаем в команде носителя для порога.")]
        [SerializeField] private Unit.UnitCategory countedCategory = Unit.UnitCategory.Fighter;
        [Tooltip("Порог: сколько живых юнитов категории у команды нужно, чтобы аура включилась. [БАЛАНС — Влад]")]
        [SerializeField] private int threshold = 3;
        [Tooltip("Радиус раздачи эффектора вокруг носителя, мировые единицы. [БАЛАНС — Влад]")]
        [SerializeField] private float radius = 4f;
        [Tooltip("Кому раздаём эффектор (обычно союзникам: Ally + Unit + Ground/Water/Air).")]
        [SerializeField] private UnitSelector targetSelector;
        [Tooltip("Эффекторы ауры (Resources/Effectors). ВАЖНО: короткая длительность — обновляются каждый тик; " +
                 "постоянный/долгий эффектор не «спадёт» при падении состава ниже порога.")]
        [SerializeField] private Effector[] effectors;

        private Unit unit;
        private bool subscribed;

        private void Awake()
        {
            unit = GetComponent<Unit>();
            if (unit == null) { enabled = false; return; }
        }

        // Подписка на тик — в Start (GameManager.instance гарантированно готов; образец GroundDamageZone).
        private void Start()
        {
            if (unit == null) return;
            if (!subscribed && GameManager.instance != null) { GameManager.instance.Tick += OnTick; subscribed = true; }
        }

        private void OnTick()
        {
            if (NetworkConnectionHandler.isClient) return;               // раздача — только сервер (правило 6)
            if (unit == null || unit.dead) { Cleanup(); return; }

            MatchManager mm = MatchManager.instance;
            if (mm == null || effectors == null || effectors.Length == 0) return;

            mm.EnsureLineCompositionWired();                            // ленивая инициализация счётчика (идемпотентно)
            if (mm.AliveOfCategoryForOwner(unit.owner, countedCategory) < threshold) return;  // порог не достигнут — не раздаём (истечёт)

            Vector2 pos = new Vector2(unit.transform.position.x, unit.transform.position.z);
            Unit[] targets = Utils.GetUnitsInRadius(pos, radius, unit.owner, targetSelector);
            if (targets == null) return;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null || targets[i].dead) continue;
                for (int j = 0; j < effectors.Length; j++)
                    if (effectors[j] != null)
                        Effector.EffectorAdd(targets[i], effectors[j], unit, unit.owner);
            }
        }

        private void Cleanup()
        {
            if (subscribed && GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            subscribed = false;
        }

        private void OnDestroy()
        {
            if (subscribed && GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            subscribed = false;
        }
    }
}
