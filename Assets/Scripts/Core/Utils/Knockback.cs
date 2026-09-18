using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    // Кирпич B9 — нокбэк: мгновенно отбросить цель от источника на distance (юниты Unity) вдоль луча.
    // Статик-хелпер (переиспользуют B2 и абилки, правило 5). Путь: точка = pos + dir*distance → NavMesh.SamplePosition
    // → MoveToSynced; опц. Stun. ТОЛЬКО сервер (правило 6).
    //
    // [Interflow 2026-09-18, шаг 4 слияния, семья «движение кастера, отброс целей, облик»]
    // ЕДИНЫЙ СИНХРОНИЗИРОВАННЫЙ ВХОД ПЕРЕМЕЩЕНИЯ — MoveToSynced. До этой правки шапка обещала
    // «позиция расходится штатным синком NetworkDataSync» — этого НЕ ПРОИСХОДИЛО: agent.Warp двигает
    // агент и не сообщает об этом никому. Пока юнит стоит, его координата клиенту не уходит вовсе
    // (в список позиций юнит попадает только когда поехал — Units/Unit.Motion.cs), поэтому отброс
    // и телепорт кастера чистому клиенту были НЕ ВИДНЫ, а клетка сетки (Grid) и клетка тумана (FogOfWar)
    // у сдвинутого юнита оставались от старой точки. Полный образец правильного переноса в проекте был
    // один — Abilities/Samples/Blink.cs. Теперь он стоит здесь, и через него идут оба хелпера,
    // то есть все четыре потребителя: блок 15 «отброс», блок 18 «перемещение кастера»,
    // усиленная атака EveryNthAttack и реакция 5 пассивки.
    // ⚠️ Воздушные (airReplica) после починки синка в Unity не проверялись.
    public static class Knockback
    {
        // Насколько далеко искать валидную точку NavMesh около расчётной (не выкинуть за меш).
        const float NavSampleMaxDistance = 2f;

        /// <summary>
        /// СИНХРОНИЗИРОВАННЫЙ ВХОД ПЕРЕМЕЩЕНИЯ: посадить точку на навигационную сетку, перенести туда
        /// агент и сообщить об этом всему, что зависит от позиции, — клетке сетки, клетке тумана
        /// и клиентам (прямой канал позиции). Единственное место, откуда кодом переставляют юнита;
        /// кто двигает юнита мимо него, оставляет клиента, сетку и туман в старой точке.
        ///
        /// Проверок «жив ли», «может ли двигаться» и «иммунитет» здесь НЕТ: их делают вызывающие
        /// <see cref="Apply"/> и <see cref="MoveTo"/> — у них разные правила отказа, и слить их нельзя.
        /// Серверного гейта здесь тоже нет по той же причине (правило 6 соблюдают вызывающие).
        /// </summary>
        /// <param name="unit">Кого переставляем. Обязан иметь агента — это проверяет вызывающий.</param>
        /// <param name="destination">Желаемая точка; фактическая садится на навигационную сетку.</param>
        /// <returns>false — валидной точки на сетке рядом нет, юнит НЕ сдвинут и ничего не разослано.</returns>
        public static bool MoveToSynced(Unit unit, Vector3 destination)
        {
            if (!NavMesh.SamplePosition(destination, out NavMeshHit hit, NavSampleMaxDistance, NavMesh.AllAreas))
                return false;

            unit.agent.Warp(hit.position);

            // Порядок как в Blink: сначала мир (сетка и туман), потом клиенты. Проверки на существование —
            // менеджеры живут только внутри матча, а хелпер зовут и вне него (тесты, загрузка сцены).
            if (Grid.Instance != null) Grid.AssignToChunk(unit);
            if (FogOfWar.Instance != null) FogOfWar.Instance.CellAssignment(unit);

            // Прямой канал позиции: у клиента он гасит интерполяцию ходьбы, доворачивает юнита,
            // ставит позицию и обновляет там сетку с туманом (Network/NetworkDataSync.PositionSync.cs).
            // Координата пакуется в UInt16 × 100 — предел карты 655.34 по оси (там же).
            if (NetworkDataSync.Instance != null) NetworkDataSync.Instance.SetPositionDirect(unit);

            return true;
        }

        /// <summary>Отбросить target от sourcePos на distance; опц. заморозка stunTime. respectControlImmunity —
        /// уважать иммунитет к контролю (B12) для отброса (заморозку гейтит наложение состояния, куда уходит Unit.Stun).
        /// sourceUnit/sourceOwner — кто отбрасывает: с 2026-09-03 оглушение стало состоянием, а у состояния
        /// владелец обязателен, поэтому источник протаскивается сюда из вызывающего кода. Только сервер.</summary>
        /// <returns>[Interflow 2026-09-18] true — цель ФАКТИЧЕСКИ сдвинулась. Нужно блоку 15: состояние
        /// «в полёте» и визуал полёта цепляются только к состоявшемуся отбросу. Прежние потребители
        /// (EveryNthAttack, реакция 5) результат игнорируют — их поведение не изменилось.</returns>
        public static bool Apply(Unit target, Vector3 sourcePos, float distance, float stunTime, bool respectControlImmunity,
                                 Unit sourceUnit, int sourceOwner)
        {
            if (NetworkConnectionHandler.isClient) return false;           // отброс/стан — только сервер (правило 6)
            if (target == null || target.dead) return false;
            if (!target.canMove || target.agent == null) return false;     // неподвижных/строения/без агента не двигаем

            // Иммунитет к контролю (B12): по флагу пропустить отброс (заморозку Stun гейтит приёмник всегда).
            // Шаг 1 схемы «пакет и приёмник» (§3.1): спрашиваем приёмник цели, а не ищем компонент
            // на каждый отброс — у приёмника найденный ControlImmunity закеширован.
            // Флаг проверяется ПЕРВЫМ: при false приёмник не запрашивается и не создаётся.
            // Проверки выше уже отсеяли мёртвых, поэтому обращение к приёмнику здесь безопасно.
            if (respectControlImmunity && target.ReceiverEnsure().ControlImmune)
                return false;

            Vector3 tp = target.transform.position;
            Vector3 dir = tp - sourcePos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = target.transform.forward;   // позиции совпали → толкаем «вперёд»
            dir.Normalize();

            // [Interflow 2026-09-18] Перенос идёт синхронизированным входом: нет валидной точки на меше —
            // не двигаем (§6 как раньше), но теперь удавшийся перенос доезжает до клиента, сетки и тумана.
            bool moved = MoveToSynced(target, tp + dir * distance);

            if (stunTime > 0f) target.Stun(stunTime, sourceUnit, sourceOwner);   // иммунитет спросит наложение состояния

            return moved;
        }

        /// <summary>
        /// Переставить target В ЗАДАННУЮ точку (рывок к кастеру, телепорт). Возвращает true, только если
        /// юнит действительно сдвинулся: неподвижные, без агента, с иммунитетом к контролю и случай
        /// «нет валидной точки на NavMesh» дают false — вызывающая сторона решает, что делать дальше.
        /// Только сервер (правило 6); удавшийся перенос уезжает клиенту прямым каналом позиции
        /// и обновляет клетку сетки с клеткой тумана (см. <see cref="MoveToSynced"/>).
        /// </summary>
        public static bool MoveTo(Unit target, Vector3 destination, bool respectControlImmunity)
        {
            if (NetworkConnectionHandler.isClient) return false;
            if (target == null || target.dead) return false;
            if (!target.canMove || target.agent == null) return false;

            // Тот же вопрос приёмнику, что и в Apply (§3.1 схемы): флаг — первым, мёртвые отсеяны выше.
            if (respectControlImmunity && target.ReceiverEnsure().ControlImmune)
                return false;

            return MoveToSynced(target, destination);
        }

        /// <summary>
        /// Точка вплотную к кастеру со стороны цели: от кастера в сторону цели на сумму габаритов
        /// обоих агентов плюс заданный зазор. Радиус берём у NavMeshAgent — это тот же габарит,
        /// которым навигация разводит юнитов, поэтому притянутый не окажется внутри кастера.
        /// </summary>
        public static Vector3 PointNextTo(Unit caster, Unit target, float gap)
        {
            if (caster == null) return target != null ? target.transform.position : Vector3.zero;

            Vector3 cp = caster.transform.position;
            if (target == null) return cp;

            Vector3 dir = target.transform.position - cp;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = caster.transform.forward;  // стоят в одной точке
            dir.Normalize();

            float casterRadius = caster.agent != null ? caster.agent.radius : 0f;
            float targetRadius = target.agent != null ? target.agent.radius : 0f;

            return cp + dir * (casterRadius + targetRadius + Mathf.Max(0f, gap));
        }

        /// <summary>
        /// [Interflow 2026-09-18] Точка ЗА точкой приложения по направлению движения — «пролёт сквозь строй»
        /// блока 18 (passThroughDistance). Направление берётся от кастера к точке приложения; кастер уже
        /// стоит в точке приложения — тогда направления нет и берётся его взгляд.
        /// Чистая функция: сетку не трогает, юнита не двигает (посадку на сетку делает MoveToSynced).
        /// </summary>
        public static Vector3 PointBeyond(Vector3 from, Vector3 aimPoint, Vector3 fallbackForward, float extraDistance)
        {
            if (extraDistance <= 0f) return aimPoint;

            Vector3 dir = aimPoint - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = fallbackForward;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f) return aimPoint;   // ни направления, ни взгляда — пролёта нет
            }
            dir.Normalize();

            return aimPoint + dir * extraDistance;
        }
    }
}
