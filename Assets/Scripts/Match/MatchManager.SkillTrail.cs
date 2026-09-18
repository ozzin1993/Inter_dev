using System.Collections;
using UnityEngine;

namespace StrategyCore
{
    // ============================= ШЛЕЙФ ПО ПУТИ РЫВКА (партиал MatchManager) ==
    // [Interflow 2026-09-18, шаг 4 слияния, решение Artsiom 56]
    //
    // Режим блока 17 «зона на земле»: зоны выкладываются по прямой от точки, откуда кастер стартовал,
    // до точки его приземления (блок 18), с шагом в метрах и ВО ВРЕМЕНИ — за время полёта блока 18.
    // Вложенное умение и поля trailSkill/trailSpacing проекта отвергнуты решением 56: шлейф вложенным
    // умением молча не сыграл бы у рывка, который сам исполнен вложенным (глубина вложения ≤ 1).
    //
    // Почему процесс живёт здесь, а не в ассете умения: ScriptableObject корутин не запускает, а образец
    // серверного процесса во времени в проекте один — корутина менеджера матча (MatchManager.MeteorStorm).
    // Партиал вместо нового менеджера или компонента на префабе (правила 5 и 22).
    //
    // Серверо-авторитетно (правило 6): зоны создаёт и регистрирует только сервер, клиент узнаёт о каждой
    // зоне штатным фактом из серверного реестра (MatchManager.GroundZones).
    public partial class MatchManager
    {
        /// <summary>
        /// Сервер: выложить шлейф зон по прямой «старт → приземление». Зовётся из блока 17 умения
        /// сразу после состоявшегося переноса кастера (CompositeSkill.ApplyGroundZone).
        ///
        /// Число зон = длина пути ÷ шаг, плюс одна (зона в самой точке старта); минимум одна.
        /// Интервал между зонами = время полёта ÷ число зон: последняя ложится к моменту, когда модель
        /// кастера у клиента дорисовала полёт. Нулевое время полёта — весь шлейф встаёт одним кадром,
        /// корутина не запускается.
        /// </summary>
        /// <param name="caster">Кастер. Его гибель ПРЕРЫВАЕТ выкладку (решение 56).</param>
        /// <param name="castingPlayer">Слот игрока-владельца зон.</param>
        /// <param name="level">Уровень умения — уезжает клиенту вместе с фактом зоны.</param>
        /// <param name="skill">Умение-источник: у него лежит префаб зоны и он же её регистрирует.</param>
        /// <param name="start">Точка, откуда кастер стартовал.</param>
        /// <param name="landing">Точка приземления кастера.</param>
        /// <param name="spacing">Шаг между зонами, метры (считается по горизонтали).</param>
        /// <param name="travelSeconds">Время полёта блока 18, секунды. 0 — выложить всё сразу.</param>
        public void StartSkillTrail(Unit caster, int castingPlayer, int level, CompositeSkill skill,
                                    Vector3 start, Vector3 landing, float spacing, float travelSeconds)
        {
            if (NetworkConnectionHandler.isClient) return;   // зоны — только сервер (правило 6)
            if (skill == null) return;
            if (spacing <= 0f) return;

            int count = TrailZoneCount(start, landing, spacing);

            // Мгновенный шлейф: ждать нечего, корутина только отложила бы всё на кадр.
            if (travelSeconds <= 0f)
            {
                for (int i = 0; i < count; i++)
                    skill.SpawnGroundZone(TrailZonePosition(start, landing, spacing, i), null, castingPlayer, level);

                return;
            }

            StartCoroutine(SkillTrailRoutine(caster, castingPlayer, level, skill, start, landing, spacing,
                                             count, travelSeconds / count));
        }

        /// <summary>
        /// Сколько зон ложится на путь: по одной на каждый шаг плюс зона в точке старта. Чистая функция —
        /// проверяется в режиме редактора (Tests/EditMode/MovementKnockbackTests).
        /// Длина считается ПО ГОРИЗОНТАЛИ: шаг задан в метрах по земле, перепад высот его не удлиняет.
        /// </summary>
        public static int TrailZoneCount(Vector3 start, Vector3 landing, float spacing)
        {
            if (spacing <= 0f) return 0;

            Vector3 flat = landing - start;
            flat.y = 0f;

            return Mathf.Max(1, Mathf.FloorToInt(flat.magnitude / spacing) + 1);
        }

        /// <summary>
        /// Точка i-й зоны шлейфа: старт плюс i шагов по прямой, не дальше точки приземления.
        /// Высота берётся линейной интерполяцией между стартом и приземлением — обе точки уже сидят
        /// на навигационной сетке, отдельного опроса террейна здесь нет. Чистая функция.
        /// </summary>
        public static Vector3 TrailZonePosition(Vector3 start, Vector3 landing, float spacing, int index)
        {
            Vector3 flat = landing - start;
            flat.y = 0f;

            float length = flat.magnitude;
            if (length <= 0.0001f) return start;   // кастер приземлился там же, где стартовал

            float t = Mathf.Clamp01(index * spacing / length);
            return Vector3.Lerp(start, landing, t);
        }

        IEnumerator SkillTrailRoutine(Unit caster, int castingPlayer, int level, CompositeSkill skill,
                                      Vector3 start, Vector3 landing, float spacing,
                                      int count, float interval)
        {
            for (int i = 0; i < count; i++)
            {
                // Умение могло быть выгружено вместе со сценой (конец матча) — корутина живёт дольше каста.
                // Проверка ПЕРВОЙ: ниже имя умения печатается в лог, и на выгруженном ассете это был бы краш.
                if (skill == null) yield break;

                // Гибель кастера прерывает выкладку (решение Artsiom 56): дорожка огня за героем,
                // которого уже нет, дальше не растёт. Первую зону это тоже проверяет — кастер мог
                // погибнуть от собственной стоимости в здоровье в этом же касте.
                if (caster == null || caster.dead)
                {
                    Debug.Log($"[{skill.name}] Шлейф по пути: кастер погиб — выкладка прервана " +
                              $"на зоне {i + 1} из {count}.");
                    yield break;
                }

                skill.SpawnGroundZone(TrailZonePosition(start, landing, spacing, i), null, castingPlayer, level);

                yield return new WaitForSeconds(interval);
            }
        }
    }
}
