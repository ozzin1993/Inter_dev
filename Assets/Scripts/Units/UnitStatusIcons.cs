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
    ///  3) статусы ядра (стан/немота/безоружие/полиморф — поля юнита, реплицируются ядром);
    ///  4) флаги наших состояний (слепота).
    /// У статусов ядра и флагов на клиенте нет времени наложения, поэтому они идут
    /// фиксированным хвостом после эффекторов.
    /// Дедуп по ключу закрывает §8.7 отчёта приёмки (двойной значок «аура + блок эффекторов»).
    /// </summary>
    public static class UnitStatusIcons
    {
        public struct Entry
        {
            public int key;        // id эффектора ≥ 0; статусы ядра и флаги — своя отрицательная зона
            public Texture2D icon;
        }

        // Ключи статусов ядра и флагов. Глубже BuffKey скиллов (-abilityId-1), чтобы не пересечься.
        public const int KeyStun = -100000;
        public const int KeyMuted = -100001;
        public const int KeyDisarmed = -100002;
        public const int KeyPolymorph = -100003;
        public const int KeyBlind = -100010;

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
                if (eh == null || eh.effector == null || (eh.stacks && eh.effector.maxStacks<=0) || eh.effector.icon == null) continue;
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

            // 3) Статусы ядра — поля юнита (ядро реплицирует их штатно: Stun/Mute/DisarmSetSend).
            //    Полиморф ядром НЕ реплицируется — иконка видна только хосту (облик юнита меняется у всех).
            StatusIconCatalog cat = StatusIconCatalog.Get();
            if (cat != null)
            {
                if (unit.stunned && cat.stunIcon != null) buffer.Add(new Entry { key = KeyStun, icon = cat.stunIcon });
                if (unit.muted && cat.mutedIcon != null) buffer.Add(new Entry { key = KeyMuted, icon = cat.mutedIcon });
                if (unit.disarmed && cat.disarmedIcon != null) buffer.Add(new Entry { key = KeyDisarmed, icon = cat.disarmedIcon });
                if (unit.polymorphed && cat.polymorphedIcon != null) buffer.Add(new Entry { key = KeyPolymorph, icon = cat.polymorphedIcon });

                // 4) Слепота: хост читает серверное состояние напрямую, клиент — присланный флаг.
                bool blind = NetworkConnectionHandler.isClient
                    ? (svs != null && svs.HasFlag(UnitStatusFlag.Blind))
                    : InterflowCombat.IsBlinded(unit);
                if (blind && cat.blindIcon != null) buffer.Add(new Entry { key = KeyBlind, icon = cat.blindIcon });
            }

            return buffer.Count;
        }

        /// <summary>
        /// Дешёвый слепок состава статусов ЯДРА и флагов (5 бит) — для опроса шкалой раз в тик:
        /// эти статусы меняются без вызова OnStatusUpdate, события у них нет.
        /// </summary>
        public static int CoreStatusHash(Unit unit)
        {
            if (unit == null || unit.dead) return 0;

            int h = 0;
            if (unit.stunned) h |= 1;
            if (unit.muted) h |= 2;
            if (unit.disarmed) h |= 4;
            if (unit.polymorphed) h |= 8;

            SkillVisualStatus svs;
            bool blind = NetworkConnectionHandler.isClient
                ? ((svs = unit.GetComponent<SkillVisualStatus>()) != null && svs.HasFlag(UnitStatusFlag.Blind))
                : InterflowCombat.IsBlinded(unit);
            if (blind) h |= 16;

            return h;
        }
    }
}
