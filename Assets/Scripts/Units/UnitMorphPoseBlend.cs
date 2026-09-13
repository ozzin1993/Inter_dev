using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Плавный переход позы скелета при смене облика юнита (полиморф и возврат): последняя видимая
    /// поза СТАРОГО тела перетекает в анимацию нового. Смешивание чисто визуальное — игровые величины
    /// меняются мгновенно, задерживается только картинка.
    ///
    /// Живёт на КОРНЕ юнита, там же, где <see cref="Unit"/>. Смена облика определяется по смене ссылки
    /// <see cref="Unit.mainRenderer"/>: её переставляют Unit.ReplaceRenderers и Unit.RestoreRenderers,
    /// и вместе с ней ядро переустанавливает <see cref="Unit.animator"/> (Unit.Init.CalculateVisuals).
    ///
    /// Чистая презентация: на выделенном сервере не работает (гейт <see cref="Utils.Headless"/> —
    /// там же ядро гасит и сам аниматор в Unit.Init).
    ///
    /// Трогает ТОЛЬКО мировые вращения гуманоидных костей. Позиции, масштабы и положение корня
    /// не меняются; у негуманоидного аватара компонент молчит.
    /// </summary>
    // Поздний порядок исполнения: позы костей пишутся ПОСЛЕ того, как аниматор и остальная визуальная
    // логика кадра уже отработали. Раньше — смешанную позу затёрло бы обычной анимацией.
    // Число — то же, что в исходном поведении; в Inspector его выносить некуда, атрибут требует константы.
    [DefaultExecutionOrder(1000)]
    public class UnitMorphPoseBlend : MonoBehaviour
    {
        [Header("Смешивание поз при смене облика")]
        [Tooltip("Длительность перехода от позы старого тела к анимации нового, секунды. " +
                 "Чем больше, тем мягче морф.")]
        [SerializeField] private float seconds = 0.22f;

        [Tooltip("Нижняя граница длительности перехода, секунды. Страховка от деления на ноль, " +
                 "если в поле выше выставлены ноль или отрицательное значение.")]
        [Min(0f)]
        [SerializeField] private float minSeconds = 0.01f;

        // Юнит-носитель. Кэшируется в Awake.
        private Unit unit;

        // Облик, под который собран кэш костей. Смена ссылки = юнит сменил тело.
        private GameObject shape;

        // Аниматор текущего облика. Берётся из юнита, не через GetComponent.
        private Animator animator;

        // Сколько секунд идёт текущее смешивание.
        private float elapsed;

        // Компонент уже видел хотя бы один облик. Нужен, чтобы САМОЕ первое назначение облика
        // (кадр рождения юнита) не считалось морфом.
        private bool initialized;

        // Мировые вращения костей. Длина массивов — по числу гуманоидных костей аватара.
        private static readonly int BoneCount = (int)HumanBodyBones.LastBone;

        private readonly Quaternion[] previous = new Quaternion[BoneCount]; // поза прошлого кадра
        private readonly Quaternion[] from = new Quaternion[BoneCount];     // поза «откуда», зафиксирована на морфе
        private readonly Quaternion[] desired = new Quaternion[BoneCount];  // поза «куда», снимается каждый кадр
        private readonly Transform[] bones = new Transform[BoneCount];      // кости текущего облика
        private readonly bool[] valid = new bool[BoneCount];                // по кости уже записана поза прошлого кадра

        void Awake()
        {
            unit = GetComponent<Unit>();

            // Выделенному серверу смешивать поз не нужно: показывать некому.
            if (Utils.Headless) { enabled = false; return; }
        }

        void LateUpdate()
        {
            // Компонент Unit ядро снимает с трупа (Unit.Combat): на смерти просто замираем,
            // облик при этом не пересобираем.
            if (unit == null || unit.dead) return;

            if (shape != unit.mainRenderer) SwitchShape();

            // Доля смешивания. Прибавление кадра идёт ДО расчёта: даже в кадре морфа доля уже
            // чуть больше нуля, а не ровно ноль. SmoothStep даёт мягкие старт и финиш.
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / Mathf.Max(minSeconds, seconds)));

            // Первый проход — ТОЛЬКО чтение: целевые позы снимаются со всех костей до того, как хоть
            // одна будет изменена. Иначе запись в родительскую кость сдвинула бы мировые вращения
            // ещё не прочитанных дочерних.
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null) desired[i] = bones[i].rotation;
            }

            // Второй проход — запись. Кость без записанной позы прошлого кадра не смешиваем:
            // смешивать не с чем, она встаёт сразу в целевую позу.
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;

                if (t < 1f && valid[i]) bones[i].rotation = Quaternion.Slerp(from[i], desired[i], t);

                previous[i] = bones[i].rotation;
                valid[i] = true;
            }
        }

        /// <summary>
        /// Юнит сменил тело: запомнить новый облик и его аниматор, зафиксировать позу «откуда»
        /// и пересобрать кэш костей.
        /// </summary>
        void SwitchShape()
        {
            shape = unit.mainRenderer;
            animator = unit.animator;

            // Поза прошлого кадра (ещё старого тела) становится позой «откуда».
            System.Array.Copy(previous, from, previous.Length);

            // САМОЕ первое назначение облика — не морф: массив поз прошлого кадра ещё пуст, и смешивание
            // дало бы рывок в мусорную позу. Стартовое значение подобрано так, чтобы доля сразу стала
            // единицей и смешивание пропустилось целиком. Это задумано, а не дефект.
            elapsed = initialized ? 0f : Mathf.Max(minSeconds, seconds);
            initialized = true;

            // Негуманоидный аватар или отсутствие аниматора обнуляют весь кэш — компонент замолкает.
            bool humanoid = animator != null && animator.isHuman;
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i] = humanoid ? animator.GetBoneTransform((HumanBodyBones)i) : null;
            }
        }
    }
}
