using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Приёмник юнита — ОЧЕРЕДЬ ПАКЕТОВ УРОНА (схема «пакет и приёмник» §6; шаг 4, решение Artsiom Р5, 03.09.2026).
    ///
    /// Реакции (ответный удар, встречный удар при не достигшем цели ударе, посмертный взрыв) порождают новые
    /// пакеты ПОСРЕДИ обработки текущего. Вложенно они больше не исполняются: пакет, пришедший во время
    /// обработки другого, кладётся в очередь и разбирается после того, как текущий доработан до конца —
    /// в том же вызове, не на тике. Следствия приняты: жертва отвечает уже оглушённой, визуал ответа
    /// отыгрывает после исходного удара (§6.2, §15.5 схемы).
    ///
    /// Очередь одна на всех (статическая): порядок обработки общий для всех юнитов, а не по одному на приёмник.
    /// Партиал — правило 22: <c>UnitReceiver.cs</c> не раздувается.
    ///
    /// Ограничитель глубины — <see cref="GameManager.damageQueueMaxDepth"/> (в Inspector, правило 3).
    /// Пакет с поколением выше предела отбрасывается с предупреждением в консоль (решение Artsiom Р5а):
    /// цепочка длиннее нескольких звеньев почти наверняка означает ошибку настройки умений.
    ///
    /// Только сервер: у клиента с шага 4 приёмник ничего не считает (решение Р1 В), очередь ему не нужна —
    /// пакет уходит в <see cref="Receive"/> сразу, там только анимация удара.
    /// </summary>
    public partial class UnitReceiver
    {
        struct QueuedPacket
        {
            public Unit target;
            public DamagePacket packet;
        }

        static readonly Queue<QueuedPacket> queue = new Queue<QueuedPacket>();

        /// <summary>0 — никто не обрабатывает пакет; 1 — идёт обработка (вложенные вызовы — в очередь).</summary>
        static int processingDepth;

        /// <summary>Поколение обрабатываемого сейчас пакета: порождённые им получают на единицу больше.</summary>
        static int currentGeneration;

        /// <summary>
        /// Отдать пакет в обработку с учётом очереди. Зовёт воронка <c>Unit.GetDamage(in DamagePacket)</c>.
        /// Пакет, пришедший во время обработки другого (из реакции), уходит в очередь; для него
        /// «погиб» и <paramref name="damageDealt"/> неизвестны в момент вызова — возвращаются false и 0
        /// (единственный читатель этих значений — <c>Unit.DealDamage</c> для <c>OnDamageDeal</c>;
        /// реакции их не читают).
        /// </summary>
        public static bool Dispatch(Unit target, in DamagePacket packet, out float damageDealt)
        {
            damageDealt = 0f;
            if (target == null) return false;

            // Клиент: только анимация, без очереди (решение Р1 В).
            if (NetworkConnectionHandler.isClient)
                return target.ReceiverEnsure().Receive(in packet, out damageDealt);

            if (processingDepth > 0)
            {
                Enqueue(target, in packet);
                return false;
            }

            processingDepth = 1;
            currentGeneration = 0;
            bool died;
            try
            {
                died = target.ReceiverEnsure().Receive(in packet, out damageDealt);
                Drain();
            }
            finally
            {
                // Исключение в приёмнике не должно навсегда «занять» очередь: следующий пакет обязан обработаться.
                processingDepth = 0;
                currentGeneration = 0;
                queue.Clear();
            }

            return died;
        }

        static void Enqueue(Unit target, in DamagePacket packet)
        {
            int generation = currentGeneration + 1;
            int maxDepth = GameManager.Instance != null ? GameManager.Instance.damageQueueMaxDepth : 0;

            if (generation > maxDepth)
            {
                // Умение называется по имени ассета (решение Artsiom 05.09.2026); у автоатаки, состояния
                // и отложенного урона без умения ссылки нет — так и пишем.
                string abilityName = packet.sourceAbility != null ? packet.sourceAbility.name : "нет";
                Debug.LogWarning("[Приёмник] Очередь пакетов урона: пакет поколения " + generation +
                                 " по «" + target.unitName + "» от «" + InterflowDebug.Name(packet.attackingUnit) +
                                 "», умение: " + abilityName + " — отброшен, предел глубины " + maxDepth +
                                 " (GameManager.damageQueueMaxDepth). " +
                                 "Цепочка ответов длиннее предела — проверь настройку умений с реакциями.");
                return;
            }

            QueuedPacket q;
            q.target = target;
            q.packet = packet;
            q.packet.generation = generation;
            queue.Enqueue(q);
        }

        /// <summary>
        /// Разобрать очередь до пустоты. Пакеты из очереди идут в приёмник напрямую, минуя воронку:
        /// провокация в воронке действует только на прямые атаки, а очередь наполняют реакции —
        /// их пакеты прямыми не бывают. Цель, погибшая пока ждала, пропускается (до приёмника только живой).
        /// </summary>
        static void Drain()
        {
            while (queue.Count > 0)
            {
                QueuedPacket q = queue.Dequeue();
                if (q.target == null || q.target.dead) continue;

                currentGeneration = q.packet.generation;
                q.target.ReceiverEnsure().Receive(in q.packet, out float _);
            }
        }
    }
}
