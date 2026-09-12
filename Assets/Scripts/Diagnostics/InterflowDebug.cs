using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Единая точка отладочных логов Interflow. Все сообщения идут с префиксом [IF],
    /// чтобы их можно было отфильтровать в консоли Unity строкой поиска.
    ///
    /// Уровни:
    ///   Off      — тишина (для релизных прогонов);
    ///   Events   — только события: способность включилась/выключилась, эффект впервые лёг на юнита;
    ///   Verbose  — плюс каждое срабатывание (каждый удар, каждый перенос огня). Строк много.
    ///   Full     — плюс полный разбор: состав каждого пакета, работа приёмника по ступеням,
    ///              сопротивления, лечение, очередь и щит. Строк ОЧЕНЬ много — для точечного разбора.
    ///
    /// Уровень переключается в инспекторе на компоненте <see cref="InterflowUnitWatcher"/>
    /// или из кода: <c>InterflowDebug.level = InterflowDebug.Level.Verbose;</c>
    /// </summary>
    public static class InterflowDebug
    {
        public enum Level { Off = 0, Events = 1, Verbose = 2, Full = 3 }

        /// <summary>Текущий уровень подробности. По умолчанию — события без спама по ударам.</summary>
        public static Level level = Level.Events;

        public static bool EventsOn { get { return level >= Level.Events; } }
        public static bool VerboseOn { get { return level >= Level.Verbose; } }
        public static bool FullOn { get { return level >= Level.Full; } }

        /// <summary>
        /// [Interflow 2026-09-09 passive-facts] Показывать срабатывания пассивных умений НА ЭКРАНЕ:
        /// надписью над юнитом и строкой в ленте. Это проверочный показ, поэтому у него свой
        /// выключатель, а не уровень логов: смотреть визуал обычно надо без потока строк в консоли,
        /// и наоборот. Выключение гасит и надписи, и ленту — сервер просто перестаёт отправлять факты.
        ///
        /// Переключается из кода: <c>InterflowDebug.showPassiveFacts = false;</c>
        /// Решает СЕРВЕР: факты рождаются у него, клиент их только показывает.
        /// </summary>
        public static bool showPassiveFacts = true;

        /// <summary>
        /// [Interflow 2026-09-11] За сколько секунд копится одна свёрнутая строка периодического урона
        /// (горение, аура, зона на земле). Решение Artsiom 11.09.2026: периодика сворачивается на СЕРВЕРЕ,
        /// иначе тик 0,1 секунды давал бы до десяти сообщений в секунду на юнита.
        ///
        /// Здесь, а не в Inspector: рядом с выключателем показа, по той же причине — это проверочный
        /// параметр показа, у которого нет своего объекта в сцене. Меньше 0,5 секунды не берётся
        /// (нижняя граница стоит в накопителе).
        ///
        /// Переключается из кода: <c>InterflowDebug.periodicRollupSeconds = 3f;</c>
        /// </summary>
        public static float periodicRollupSeconds = 2f;

        /// <summary>Событие: включение/выключение способности, первое наложение эффекта.</summary>
        public static void Event(string message)
        {
            if (level < Level.Events) return;

            Debug.Log("[IF] " + message);
        }

        /// <summary>Подробность: срабатывание на конкретном ударе.</summary>
        public static void Verbose(string message)
        {
            if (level < Level.Verbose) return;

            Debug.Log("[IF] " + message);
        }

        /// <summary>
        /// Полный разбор: состав пакета, ступени приёмника, сопротивления, лечение, очередь, щит.
        /// Строка ОБЯЗАНА собираться внутри <c>if (InterflowDebug.FullOn)</c> — этот путь горячий
        /// (приёмник проходит на каждом тике урона в секунду у каждого юнита), и склейка строк
        /// без гейта стоила бы кадра даже при выключенном уровне.
        /// </summary>
        public static void Full(string message)
        {
            if (level < Level.Full) return;

            Debug.Log("[IF] " + message);
        }

        /// <summary>Проблема в настройке данных (незаполненная ссылка и т.п.) — видно всегда, кроме Off.</summary>
        public static void Warn(string message)
        {
            if (level < Level.Events) return;

            Debug.LogWarning("[IF] " + message);
        }

        /// <summary>Читаемое имя юнита для логов: «Rifleman_Red#1234 (игрок 0)».</summary>
        public static string Name(Unit unit)
        {
            if (unit == null) return "null";

            return unit.name + "#" + unit.GetInstanceID() + " (игрок " + unit.owner + ")";
        }
    }
}
