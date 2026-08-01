using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Тип точки привязки на модели персонажа. Используется скиллами: откуда вылетает снаряд,
    /// где появляется VFX замаха и т.п. Значение выбирается в ассете скилла.
    /// </summary>
    public enum SkillSocketType
    {
        [InspectorName("Не задана (центр объекта)")] None,
        [InspectorName("Правая рука")]               RightHand,
        [InspectorName("Левая рука")]                LeftHand,
        [InspectorName("Оружие")]                    Weapon,
        [InspectorName("Грудь")]                     Chest,
        [InspectorName("Центр")]                     Center,
        [InspectorName("Голова")]                    Head,
        [InspectorName("Под ногами")]                Ground
    }

    /// <summary>
    /// Точки привязки на префабе персонажа. Пустые дочерние объекты под нужными костями
    /// (например «RightHandPoint» под костью правой руки), масштаб 1, позиция правится вручную
    /// в редакторе — фаербол удобно ставить чуть перед ладонью.
    ///
    /// Компонент ОПЦИОНАЛЕН: если его нет на префабе или сокет не заполнен, вызывающая сторона
    /// получает фолбэк — сам transform объекта. Поэтому старые префабы без сокетов продолжают работать.
    /// </summary>
    // Без [RequireComponent(typeof(Unit))]: ассет снимает «лишние» компоненты юнита через
    // Utils.UnitRemoveComponents (апгрейд/трансформация/статик-копия), а зависимость блокировала бы
    // удаление Unit. Тот же приём, что у AutoAbilityUser.
    public class CharacterSockets : MonoBehaviour
    {
        [Header("Точки привязки")]
        [Tooltip("Правая рука — основная точка для снарядов и VFX замаха у большинства кастеров.")]
        [SerializeField] private Transform rightHand;

        [Tooltip("Левая рука — для двуручных замахов и вторых снарядов.")]
        [SerializeField] private Transform leftHand;

        [Tooltip("Оружие — точка на клинке/наконечнике (например свечение меча перед ударом).")]
        [SerializeField] private Transform weapon;

        [Tooltip("Грудь — для щитов, аур и эффектов «изнутри» персонажа.")]
        [SerializeField] private Transform chest;

        [Tooltip("Центр — геометрический центр модели (примерно на уровне пояса).")]
        [SerializeField] private Transform center;

        [Tooltip("Голова — для нимбов, значков и эффектов над лицом.")]
        [SerializeField] private Transform head;

        [Tooltip("Под ногами — точка на земле у основания модели (круги, зоны, печати).")]
        [SerializeField] private Transform ground;

        /// <summary>
        /// Точка привязки по типу. Если тип не задан или конкретная точка не заполнена на префабе —
        /// возвращается transform самого объекта (фолбэк, никогда не null).
        /// </summary>
        public Transform GetSocket(SkillSocketType type)
        {
            Transform socket;
            switch (type)
            {
                case SkillSocketType.RightHand: socket = rightHand; break;
                case SkillSocketType.LeftHand:  socket = leftHand;  break;
                case SkillSocketType.Weapon:    socket = weapon;    break;
                case SkillSocketType.Chest:     socket = chest;     break;
                case SkillSocketType.Center:    socket = center;    break;
                case SkillSocketType.Head:      socket = head;      break;
                case SkillSocketType.Ground:    socket = ground;    break;
                default:                        socket = null;      break; // None
            }

            return socket != null ? socket : transform;
        }
    }
}
