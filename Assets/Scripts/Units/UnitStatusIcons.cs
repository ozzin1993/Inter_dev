using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// ЕДИНЫЙ перечислитель значков статусов юнита (правило 5, единый источник истины):
    /// его читают и Status-таб панели выбранного юнита, и шкала над полоской здоровья —
    /// состав значков в обоих местах совпадает по построению.
    ///
    /// Источники (порядок ряда — решение Artsiom 2026-08-05: «порядок наложения»):
    ///  1) локальные эффекторы юнита (хост/оффлайн; порядок списка = порядок наложения);
    ///  2) присланное сервером по единому каналу статусов (SkillVisualStatus, клиент);
    ///  3) полиморф — единственный оставшийся статус-поле юнита; идёт фиксированным хвостом.
    /// Контроль (стан/немота/безоружие/слепота) с 2026-09-03 приходит источниками 1 и 2 как
    /// обычное состояние — отдельной ветки у него больше нет.
    /// Дедуп по ключу закрывает §8.7 отчёта приёмки (двойной значок «аура + блок эффекторов»).
    /// </summary>
    public static class UnitStatusIcons
    {
        public struct Entry
        {
            public int key;        // id эффектора ≥ 0; статусы ядра и флаги — своя отрицательная зона
            public Texture2D icon;
        }

        // Ключ статуса-поля юнита. Глубже BuffKey скиллов (-abilityId-1), чтобы не пересечься.
        // [Interflow fix 2026-09-03 control-as-effectors] KeyStun/KeyMuted/KeyDisarmed/KeyBlind сняты
        // вместе со своими ветками — контроль теперь обычное состояние со своим id.
        public const int KeyPolymorph = -100003;

        static readonly HashSet<int> seen = new HashSet<int>();

        /// <summary>Собрать значки юнита в buffer (очищается внутри). Возвращает число значков.</summary>
        public static int Collect(Unit unit, List<Entry> buffer)
        {
            buffer.Clear();
            if (unit == null || unit.dead) return 0;
            seen.Clear();

            // 1) Локальные эффекторы. Стакающие значка не дают (правило ядра, как в Status-табе).
            for (int i = 0; i < unit.effectors.Count; i++)
            {
                EffectorHolder eh = unit.effectors[i];
                if (eh == null || eh.stacks || eh.effector == null || eh.effector.icon == null) continue;
                if (!seen.Add(eh.effector.id)) continue; // несколько наложений одного id — один значок
                buffer.Add(new Entry { key = eh.effector.id, icon = eh.effector.icon });
            }

            // 2) Присланное сервером (клиент). Дедуп по id с локальными: ауры накладывают эффектор
            //    и в локальной симуляции клиента — без дедупа значок двоился бы (§8.7).
            SkillVisualStatus svs = unit.GetComponent<SkillVisualStatus>();
            if (svs != null)
            {
                for (int i = 0; i < svs.IconCount; i++)
                    if (svs.TryGetIcon(i, out int id, out Texture2D icon) && icon != null && seen.Add(id))
                        buffer.Add(new Entry { key = id, icon = icon });
            }

            // 3) Полиморф — единственный оставшийся статус-поле юнита.
            //    [Interflow fix 2026-09-03 control-as-effectors] Ветки оглушения, немоты, безоружия
            //    и слепоты СНЕСЕНЫ: контроль стал состоянием-эффектором и приходит источниками 1 и 2
            //    со СВОИМ значком. Оставить их значило бы рисовать два одинаковых значка на юните —
            //    ключи у эффектора (id ≥ 0) и у статуса ядра разные, дедуп бы их не свёл.
            //    Спрайты контроля в справочнике остались: из них сделаны значки служебных состояний.
            //    Полиморф ядром НЕ реплицируется — иконка видна только хосту (облик юнита меняется у всех).
            StatusIconCatalog cat = StatusIconCatalog.Get();
            if (cat != null && unit.polymorphed && cat.polymorphedIcon != null)
                buffer.Add(new Entry { key = KeyPolymorph, icon = cat.polymorphedIcon });

            return buffer.Count;
        }

        /// <summary>
        /// Дешёвый слепок состава статусов-ПОЛЕЙ юнита — для опроса шкалой раз в тик: они меняются
        /// без вызова OnStatusUpdate, события у них нет.
        /// [Interflow fix 2026-09-03 control-as-effectors] От пяти бит остался один: контроль стал
        /// состоянием, а наложение и снятие состояния OnStatusUpdate дёргают штатно.
        /// </summary>
        public static int CoreStatusHash(Unit unit)
        {
            if (unit == null || unit.dead) return 0;

            return unit.polymorphed ? 8 : 0;
        }
    }
}
