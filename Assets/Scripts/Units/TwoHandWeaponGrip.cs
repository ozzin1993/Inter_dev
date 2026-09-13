using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Хват двуручного оружия: поддерживающая (левая) кисть притягивается к точке на оружии
    /// средствами инверсной кинематики аниматора. Анимация остаётся одна на все виды оружия,
    /// а место хвата задаётся точкой в префабе модели.
    ///
    /// Компонент висит на МОДЕЛИ юнита — на объекте с <see cref="Animator"/> (это
    /// <see cref="Unit.mainRenderer"/>), а не на корне юнита: инверсную кинематику считает тот
    /// аниматор, который проигрывает анимацию. Сам <see cref="Unit"/> берётся из родителей и нужен
    /// ровно для одного — отпустить руку на смерти, иначе кисть выворачивается на анимации падения.
    ///
    /// Чистая презентация: игровых величин не трогает и на выделенном сервере не работает
    /// (гейт <see cref="Utils.Headless"/> — та же конвенция, что у полосок здоровья и маны, значков
    /// состояний и самого аниматора в Unit.Init).
    ///
    /// ВАЖНО про настройку: Unity зовёт OnAnimatorIK только у слоя контроллера с включённой галкой
    /// IK Pass. Точка хвата не задана или аватар не гуманоидный — компонент молчит и весов не трогает.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public class TwoHandWeaponGrip : MonoBehaviour
    {
        [Tooltip("Точка хвата на оружии, к которой притягивается поддерживающая (левая) кисть. " +
                 "Обычно пустой объект на модели оружия. Не задана — инверсная кинематика не работает.")]
        [SerializeField] private Transform supportHandTarget;

        [Tooltip("Сила притягивания кисти к точке хвата: 0 — рука идёт как в анимации, " +
                 "1 — полностью встаёт в точку хвата.")]
        [Range(0f, 1f)]
        [SerializeField] private float weight = 1f;

        /// <summary>Поддерживающая рука двуручного хвата — левая. Правая держит рукоять и идёт из анимации.</summary>
        private const AvatarIKGoal SupportHand = AvatarIKGoal.LeftHand;

        /// <summary>Вес инверсной кинематики на смерти: хват отпущен полностью.</summary>
        private const float DeathWeight = 0f;

        // Аниматор этой же модели. За время жизни объекта не меняется: смена облика юнита
        // создаёт НОВУЮ модель со своим компонентом, а не подменяет аниматор у старой.
        private Animator animator;

        // Юнит-носитель. Может не найтись (модель показывают отдельно от юнита, статическая копия) —
        // тогда хват просто работает всегда.
        private Unit unit;

        // Юнит умер — руку отпускаем.
        // Флаг по событию, а не опрос состояния аниматора: ядро проигрывает СЛУЧАЙНУЮ анимацию
        // из death0..deathN (Unit.Combat.cs:465), публичного хэша состояния смерти не отдаёт,
        // а компонент Unit к моменту анимации падения уже снят с трупа (Unit.Combat.cs:481) —
        // опрашивать было бы нечего. Событие приходит до всего этого (Unit.Combat.cs:348).
        private bool dying;

        void Awake()
        {
            animator = GetComponent<Animator>();

            // Выделенному серверу инверсная кинематика не нужна: показывать некому
            // (там же ядро гасит и сам аниматор — Unit.Init).
            if (Utils.Headless) { enabled = false; return; }
        }

        void Start()
        {
            // Юнит ищется в Start, а не в Awake, намеренно: при смене облика (Unit.ReplaceRenderers)
            // модель рождается под ВРЕМЕННЫМ юнитом и лишь потом переносится к настоящему — в Awake
            // родитель ещё не тот. Так же устроены полоски здоровья и маны.
            unit = GetComponentInParent<Unit>();
            if (unit != null) unit.OnDie += OnUnitDeath;
        }

        void OnDestroy()
        {
            if (unit != null) unit.OnDie -= OnUnitDeath;
        }

        // Сигнатура под Unit.OnDie: кто умер, игрок-убийца, юнит-убийца, начисляются ли награды.
        void OnUnitDeath(Unit unitThatDies, int playerKiller, Unit unitKiller, bool rewards)
        {
            dying = true;
        }

        // Параметр слоя не используется намеренно: цели и веса одни и те же на любом слое
        // с включённым IK Pass, повторная запись идемпотентна.
        void OnAnimatorIK(int layerIndex)
        {
            if (supportHandTarget == null || !animator.isHuman) return;

            float activeWeight = dying ? DeathWeight : weight;

            animator.SetIKPositionWeight(SupportHand, activeWeight);
            animator.SetIKRotationWeight(SupportHand, activeWeight);

            // Цель задаётся и при нулевом весе — это безвредно и избавляет от лишней ветки.
            animator.SetIKPosition(SupportHand, supportHandTarget.position);
            animator.SetIKRotation(SupportHand, supportHandTarget.rotation);
        }
    }
}
