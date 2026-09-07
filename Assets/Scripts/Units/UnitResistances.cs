using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Одна строка выдачи сопротивления: категория состояний и доля. Общий тип для блока
    /// «Сопротивления» конструктора пассивок и для длящегося бафа умения — оба входа выдают
    /// одно и то же (решение Artsiom 03.09.2026 «выдача — оба входа»).
    /// </summary>
    [Serializable]
    public class ResistanceEntry
    {
        [Tooltip("Категория состояний, к которой относится сопротивление или слабость. «Нет» — строка не действует")]
        public EffectorCategory category = EffectorCategory.None;

        [Tooltip("Доля: 0,3 — сопротивление 30 % (сила или время состояния меньше на треть); " +
                 "−0,5 — слабость 50 % (сила или время больше в полтора раза); 1 и больше — состояния этой категории " +
                 "не действуют вовсе. Потолка у слабости нет: −2 даст тройной эффект")]
        public float value = 0f;
    }

    /// <summary>
    /// Носитель сопротивлений и слабостей юнита к категориям состояний (решения Artsiom 29.08–03.09.2026).
    /// Компонент на юните по образцу <see cref="ControlImmunity"/>: ставят его пассивки и бафы кодом
    /// (навешивается лениво при первой выдаче), приёмник кеширует найденный (<c>UnitReceiver.Resistance</c>).
    ///
    /// Хранит только ВКЛАДЫ — кто (источник) и сколько (доля) по каждой категории. Никакого своего
    /// состояния помимо вкладов, никакого <c>Update</c>, ничего сериализуемого: сопротивление — это
    /// следствие открытых пассивок и висящих бафов, а не самостоятельная величина. Поэтому и в сохранение
    /// оно не попадает: пассивки и бафы восстанавливаются сами и выдают вклады заново.
    ///
    /// Правило сложения (решение Artsiom 03.09.2026 «сильнейший источник, слабость главнее»):
    /// есть хоть одна слабость (отрицательный вклад) — действует САМАЯ СИЛЬНАЯ слабость, сопротивления
    /// не учитываются; слабостей нет — действует самое сильное сопротивление. Перемножения нет.
    /// Единое с лечением правило «худшее для получателя главнее».
    ///
    /// Значение в долях: 0,3 = сопротивление 30 %, −0,5 = слабость 50 %. Как именно доля применяется
    /// (к силе или ко времени), знает приёмник — по виду категории (<see cref="CutsTime"/>).
    ///
    /// Горячий путь — <see cref="Effective"/>: зовётся на каждое наложение состояния, поэтому без
    /// поиска компонентов, без выделений памяти и без LINQ (руководство Unity, programming best practices).
    /// Вклады лежат в массиве списков по номеру категории.
    /// </summary>
    public class UnitResistances : MonoBehaviour
    {
        /// <summary>Один вклад: кто выдал и сколько. Источник — стабильный объект (ассет пассивки,
        /// компонент бафа), по нему же вклад и снимается.</summary>
        struct Contribution
        {
            public object source;
            public float value;
        }

        // Индекс — числовое значение EffectorCategory. Список создаётся при первом вкладе категории.
        readonly List<Contribution>[] byCategory =
            new List<Contribution>[Enum.GetValues(typeof(EffectorCategory)).Length];

        /// <summary>
        /// Выдать вклад. Категория «Нет» игнорируется — ей нечего сопротивляться.
        /// Один источник может выдать несколько вкладов (по разным категориям или по одной);
        /// <see cref="Remove"/> снимает их все разом.
        /// </summary>
        public void Add(object source, EffectorCategory category, float value)
        {
            if (source == null || category == EffectorCategory.None) return;

            int index = (int)category;
            if (index < 0 || index >= byCategory.Length) return;

            if (byCategory[index] == null) byCategory[index] = new List<Contribution>();
            byCategory[index].Add(new Contribution { source = source, value = value });
        }

        /// <summary>
        /// Снять ВСЕ вклады источника по всем категориям — ровно то, что он выдал. Повторный вызов
        /// и вызов без вкладов безвредны. Сравнение источников — по ссылке.
        /// </summary>
        public void Remove(object source)
        {
            if (source == null) return;

            for (int c = 0; c < byCategory.Length; c++)
            {
                List<Contribution> list = byCategory[c];
                if (list == null) continue;

                for (int i = list.Count - 1; i >= 0; i--)
                    if (ReferenceEquals(list[i].source, source)) list.RemoveAt(i);
            }
        }

        /// <summary>
        /// Действующее сопротивление категории, в долях. Есть слабости — минимальное значение
        /// (самая сильная слабость); иначе максимальное (сильнейшее сопротивление); вкладов нет — 0.
        /// Наборы из приёмки промта: {0,3; 0,5} → 0,5; {0,3; −0,5} → −0,5; {−0,2; −0,5} → −0,5; {} → 0.
        /// </summary>
        public float Effective(EffectorCategory category)
        {
            int index = (int)category;
            if (category == EffectorCategory.None || index < 0 || index >= byCategory.Length) return 0f;

            List<Contribution> list = byCategory[index];
            if (list == null || list.Count == 0) return 0f;

            bool hasWeakness = false;
            float strongestWeakness = 0f;
            float strongestResistance = 0f;

            for (int i = 0; i < list.Count; i++)
            {
                float v = list[i].value;
                if (v < 0f)
                {
                    if (!hasWeakness || v < strongestWeakness) strongestWeakness = v;
                    hasWeakness = true;
                }
                else if (v > strongestResistance)
                {
                    strongestResistance = v;
                }
            }

            return hasWeakness ? strongestWeakness : strongestResistance;
        }

        /// <summary>
        /// Что режет сопротивление у этой категории: у контроля (оглушение, немота, безоружие, слепота) —
        /// ВРЕМЯ наложения, у числовых категорий (замедления, периодический урон) — СИЛУ
        /// (каталог категорий, решение Artsiom 30.08.2026). Единственное место, где вид категории задан.
        /// </summary>
        public static bool CutsTime(EffectorCategory category)
        {
            switch (category)
            {
                case EffectorCategory.Stun:
                case EffectorCategory.Mute:
                case EffectorCategory.Disarm:
                case EffectorCategory.Blind:
                    return true;
                default:
                    return false;
            }
        }
    }
}
