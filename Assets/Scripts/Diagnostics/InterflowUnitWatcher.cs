using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Отладочный наблюдатель: вешается на тестовый префаб и пишет в консоль всё, что происходит
    /// со статами и эффекторами юнита. Нужен, чтобы видеть работу пассивок, которые меняют
    /// характеристики штатным механизмом (например «Заградительный огонь» через Passive) —
    /// такие изменения нашим кодом не проходят и иначе остались бы невидимыми.
    ///
    /// Что пишет:
    ///   • стартовый снимок статов при появлении юнита;
    ///   • любое изменение скорости атаки / дальности / брони / урона / скорости бега — в виде «было → стало»;
    ///   • наложение и снятие эффектора (горение, замедление и т.п.) с именем эффектора и источником;
    ///   • разблокировку и блокировку способностей юнита (когда игрок покупает узел дерева).
    ///
    /// На бой не влияет: только читает состояние. Снимать с боевых префабов перед релизом.
    /// </summary>
    [DisallowMultipleComponent]
    public class InterflowUnitWatcher : MonoBehaviour
    {
        [Header("Логи")]
        [Tooltip("Уровень подробности логов Interflow. Задаётся глобально: последний включившийся наблюдатель выставляет уровень для всех.\n" +
                 "Events — события (включение способности, первое наложение эффекта).\n" +
                 "Verbose — плюс каждое срабатывание на каждом ударе.\n" +
                 "Full — плюс разбор пакетов и работы приёмника: состав пакета, ступени расчёта, " +
                 "сопротивления, лечение, очередь и щит. Строк очень много, уровень для точечного разбора.")]
        public InterflowDebug.Level logLevel = InterflowDebug.Level.Events;

        [Tooltip("Следить за изменением характеристик (скорость атаки, дальность, броня, урон, скорость бега).")]
        public bool watchStats = true;

        [Tooltip("Следить за наложением и снятием эффекторов.")]
        public bool watchEffectors = true;

        [Tooltip("Следить за разблокировкой способностей (покупка узла дерева).")]
        public bool watchAbilities = true;

        [Tooltip("Минимальное изменение характеристики, которое считается значимым (отсекает дрожание float).")]
        public float statEpsilon = 0.0001f;

        Unit unit;
        bool subscribed;

        // Последний известный снимок
        float lastAttackSpeed, lastAttackRange, lastArmor, lastAttackDamage, lastMoveSpeed;
        readonly List<Effector> lastEffectors = new List<Effector>();
        readonly List<Effector> effectorBuffer = new List<Effector>();
        bool[] lastLocked;

        void Start()
        {
            InterflowDebug.level = logLevel;

            unit = GetComponent<Unit>();
            if (unit == null)
            {
                InterflowDebug.Warn("InterflowUnitWatcher висит на объекте без Unit — снимаю: " + name);
                enabled = false;
                return;
            }

            Snapshot();

            InterflowDebug.Event("НАБЛЮДАТЕЛЬ включён: " + InterflowDebug.Name(unit) +
                                 " | урон " + unit.attackDamage +
                                 ", скорость атаки " + unit.attackSpeed +
                                 ", дальность " + unit.attackRange +
                                 ", броня " + unit.armor +
                                 ", скорость бега " + unit.moveSpeed +
                                 ", способностей в списке " + (unit.abilities != null ? unit.abilities.Length : 0));

            TrySubscribe();
        }

        void Update()
        {
            // GameManager мог ещё не проснуться в момент Start — дожидаемся его, иначе наблюдатель молчал бы весь матч
            if (!subscribed) TrySubscribe();
        }

        void TrySubscribe()
        {
            if (subscribed || GameManager.Instance == null) return;

            GameManager.Instance.Tick += OnTick;
            subscribed = true;
        }

        void OnDestroy()
        {
            if (subscribed && GameManager.Instance != null) GameManager.Instance.Tick -= OnTick;
            subscribed = false;
        }

        void Snapshot()
        {
            lastAttackSpeed = unit.attackSpeed;
            lastAttackRange = unit.attackRange;
            lastArmor = unit.armor;
            lastAttackDamage = unit.attackDamage;
            lastMoveSpeed = unit.moveSpeed;

            lastEffectors.Clear();
            if (unit.effectors != null)
                for (int i = 0; i < unit.effectors.Count; i++)
                    if (unit.effectors[i] != null) lastEffectors.Add(unit.effectors[i].effector);
        }

        void OnTick()
        {
            if (unit == null || unit.dead) return;
            if (InterflowDebug.level == InterflowDebug.Level.Off) return;

            if (watchStats) StatsCheck();
            if (watchEffectors) EffectorsCheck();
            if (watchAbilities) AbilitiesCheck();
        }

        void StatsCheck()
        {
            StatCompare("скорость атаки", ref lastAttackSpeed, unit.attackSpeed);
            StatCompare("дальность атаки", ref lastAttackRange, unit.attackRange);
            StatCompare("броня", ref lastArmor, unit.armor);
            StatCompare("урон атаки", ref lastAttackDamage, unit.attackDamage);
            StatCompare("скорость бега", ref lastMoveSpeed, unit.moveSpeed);
        }

        void StatCompare(string statName, ref float previous, float current)
        {
            if (Mathf.Abs(current - previous) <= statEpsilon) return;

            float delta = current - previous;
            string percent = Mathf.Abs(previous) > 0.0001f
                ? " (" + (delta / previous * 100f).ToString("+0.#;-0.#") + "%)"
                : "";

            InterflowDebug.Event("СТАТ " + InterflowDebug.Name(unit) + ": " + statName +
                                 " " + previous.ToString("0.###") + " → " + current.ToString("0.###") + percent);

            previous = current;
        }

        void EffectorsCheck()
        {
            effectorBuffer.Clear();
            if (unit.effectors != null)
                for (int i = 0; i < unit.effectors.Count; i++)
                    if (unit.effectors[i] != null) effectorBuffer.Add(unit.effectors[i].effector);

            // Появившиеся
            for (int i = 0; i < effectorBuffer.Count; i++)
            {
                Effector e = effectorBuffer[i];
                if (e == null) continue;

                int wasCount = CountOf(lastEffectors, e);
                int nowCount = CountOf(effectorBuffer, e);
                if (nowCount > wasCount && FirstSeenIndex(effectorBuffer, e) == i)
                {
                    InterflowDebug.Event("ЭФФЕКТ + на " + InterflowDebug.Name(unit) + ": «" + EffectorName(e) + "»" +
                                         (nowCount > 1 ? " (стаков: " + nowCount + ")" : "") +
                                         SourceOf(e));
                }
            }

            // Снятые
            for (int i = 0; i < lastEffectors.Count; i++)
            {
                Effector e = lastEffectors[i];
                if (e == null) continue;

                int wasCount = CountOf(lastEffectors, e);
                int nowCount = CountOf(effectorBuffer, e);
                if (nowCount < wasCount && FirstSeenIndex(lastEffectors, e) == i)
                {
                    InterflowDebug.Event("ЭФФЕКТ − с " + InterflowDebug.Name(unit) + ": «" + EffectorName(e) + "»" +
                                         (nowCount > 0 ? " (осталось стаков: " + nowCount + ")" : ""));
                }
            }

            lastEffectors.Clear();
            lastEffectors.AddRange(effectorBuffer);
        }

        void AbilitiesCheck()
        {
            if (unit.abilities == null || unit.abilityLocked == null) return;

            int count = Mathf.Min(unit.abilityLocked.Length, unit.abilities.Length);

            // Первый проход — запоминаем исходное состояние молча
            if (lastLocked == null || lastLocked.Length != count)
            {
                lastLocked = new bool[count];
                for (int i = 0; i < count; i++) lastLocked[i] = unit.abilityLocked[i];

                return;
            }

            // Сравниваем каждую способность отдельно: так видно и открытие, и закрытие,
            // даже если в одном тике одна открылась, а другая закрылась
            for (int i = 0; i < count; i++)
            {
                if (unit.abilityLocked[i] == lastLocked[i]) continue;

                Ability a = unit.abilities[i];
                lastLocked[i] = unit.abilityLocked[i];
                if (a == null) continue;

                InterflowDebug.Event("СПОСОБНОСТЬ " + (unit.abilityLocked[i] ? "закрыта" : "ОТКРЫТА") +
                                     " у " + InterflowDebug.Name(unit) + ": " + a.name);
            }
        }

        static string EffectorName(Effector e)
        {
            if (e == null) return "null";

            return string.IsNullOrEmpty(e.displayName) ? e.name : e.displayName + " [" + e.name + "]";
        }

        string SourceOf(Effector e)
        {
            if (unit.effectors == null) return "";

            for (int i = 0; i < unit.effectors.Count; i++)
            {
                EffectorHolder h = unit.effectors[i];
                if (h == null || h.effector != e) continue;

                if (h.unitOwner != null) return ", источник: " + h.unitOwner.name + " (игрок " + h.owner + ")";
                if (h.owner >= 0) return ", источник: игрок " + h.owner;

                break;
            }

            return "";
        }

        static int CountOf(List<Effector> list, Effector e)
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++) if (list[i] == e) n++;

            return n;
        }

        static int FirstSeenIndex(List<Effector> list, Effector e)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == e) return i;

            return -1;
        }
    }
}
