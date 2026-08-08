using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Interflow-партиал Unit: класс (роль) юнита и реакция на общие приказы команды.
    /// Наш файл — сериализуемые поля добавляются на Unit БЕЗ правки ассета (правило 1, без форк-долга).
    /// Перенос из временного UnitClass.cs (план §4.3). В Inspector поля видны внизу компонента Unit.
    /// Используется AutoAbilityUser (приоритет цели) и MatchManager.DefenceSlots (фильтр кнопок Атака/Защита).
    /// </summary>
    public partial class Unit
    {
        /// <summary>Боевая роль юнита.</summary>
        public enum UnitCategory
        {
            [InspectorName("Боец")]    Fighter,
            [InspectorName("Танк")]    Tank,
            [InspectorName("Стрелок")] Shooter,
            [InspectorName("Маг")]     Mage,
            [InspectorName("Герой")]   Hero
        }

        [Header("Класс (Interflow)")]
        [Tooltip("Боевая роль юнита. Используется AutoAbilityUser для приоритета цели и кнопками Атака/Защита для фильтрации.")]
        public UnitCategory unitCategory = UnitCategory.Fighter;

        [Header("Приоритет цели атаки (Interflow)")]
        [Tooltip("Упорядоченный список категорий целей. При выборе НОВОЙ цели юнит предпочитает ближайшего из первой непустой категории (сначала элемент [0], затем [1] и т.д.). " +
                 "Пусто — берётся командный дефолт из MatchManager; если и он пуст — штатный выбор (ближайший). Уже начатый бой не прерывается.")]
        public UnitCategory[] targetPriority;

        [Header("Реакция на общие приказы (Interflow)")]
        [Tooltip("Реагирует на кнопку АТАКА (весь отряд в атаку). ВЫКЛ — юнит игнорирует этот приказ.")]
        public bool respondsToAttackCommand = true;
        [Tooltip("Реагирует на кнопку ЗАЩИТА (весь отряд в защиту). ВЫКЛ — юнит игнорирует этот приказ.")]
        public bool respondsToDefenceCommand = true;

        [Header("Могилка (Interflow)")]
        [Tooltip("Оставляет ли юнит могилку при смерти. ВЫКЛ — могилка не создаётся. Дефолт ВЫКЛ (задаётся на префабе юнита).")]
        public bool leavesGrave = false;
        [Tooltip("Тир юнита (например, 1–5). Копируется в могилку для будущих механик (воскрешение/поиск).")]
        public int tier = 1;

        // ================== НАПРАВЛЕНИЕ ВЗГЛЯДА (Interflow) ==================
        // Корневой transform юнита НЕ вращается: весь поворот пишется в horizontalPart
        // (Unit.Transform.cs) с компенсацией на horizontalPartForward — начальный разворот модели.
        // Поэтому у юнита с отдельной моделью transform.forward — константа, читать его нельзя.
        // Формула ниже — та же, по которой проверяется завершённость доворота в Unit.State.cs.

        /// <summary>
        /// Куда юнит смотрит СЕЙЧАС: горизонтальный вектор единичной длины (y = 0).
        /// Нет horizontalPart или юнит ещё не инициализирован — падаем на forward самого объекта.
        /// </summary>
        public Vector3 LookDirection
        {
            get
            {
                Vector3 dir = (horizontalPart != null && horizontalPartSet)
                    ? Quaternion.Euler(horizontalPart.rotation.eulerAngles - horizontalPartForward.eulerAngles) * Vector3.forward
                    : transform.forward;

                dir.y = 0f;
                return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
            }
        }

        /// <summary>
        /// Стоит ли текущая цель атаки в пределах удара — то есть юнит уже дерётся с ней, а не идёт к ней.
        /// Цель назначается движком ещё на дистанции обнаружения (reactionRange), поэтому «цель есть»
        /// само по себе не значит «цель достаётся».
        ///
        /// Формула — копия боевой проверки движка (Unit.State.cs:676), намеренно с той же логикой «или»:
        /// ближний бой меряет сумму радиусов, дальний — attackRange, причём attackRange проверяется всегда.
        /// Своей формулы не заводим: разъедься она с движком, умение срабатывало бы вне боя или молчало в бою.
        /// </summary>
        public bool IsAttackTargetInStrikeRange()
        {
            if (target == null || target.dead) return false;

            float distance = Vector2.Distance(new Vector2(transform.position.x, transform.position.z),
                                              new Vector2(target.transform.position.x, target.transform.position.z));

            return (melee && distance < target.unitRadius + unitRadius + Utils.stopDistanceOffset)
                   || distance < attackRange;
        }
    }
}
