using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// [Interflow 2026-09-18, шаг 4 слияния] Презентер: ПОЛЁТ МОДЕЛИ и ОБЛИК НА КЛИЕНТЕ.
    ///
    /// Семья «движение кастера, отброс целей, облик» (решения Artsiom 47, 49, 54). Геймплей здесь
    /// не считается вовсе: сервер УЖЕ перенёс юнита в конечную точку и уже разослал его позицию
    /// прямым каналом. Здесь только показ — модель едет из точки события в свою сетевую позицию.
    ///
    /// Почему начальная точка берётся ИЗ ФАКТА, а не с экрана: снимок позиции (прямой канал) и факт
    /// события (SkillFired) — ДВА РАЗНЫХ сообщения, и порядок их прихода не гарантирован. Придёт
    /// позиция первой — с экрана мы прочитали бы уже конечную точку и полёта не увидели бы вовсе.
    ///
    /// Куда лететь, наоборот, берётся С ЭКРАНА, и КАЖДЫЙ КАДР: цель полёта — сетевая позиция юнита.
    /// Придёт снимок позиции позже факта — полёт просто перенацелится, а не разойдётся с игрой.
    ///
    /// Высота (дуга) пишется прямо в transform и работает ровно до тех пор, пока клиент не считает
    /// юнита ДВИЖУЩИМСЯ: при isMoving интерполяция позиции (Units/Unit.Transform.cs, PositionUpdateDirect)
    /// сама переписывает и X/Z, и Y (садит на террейн) каждый кадр. Поэтому отданная игроком в этот
    /// момент команда движения обрывает показ полёта: модель мгновенно подхватывается сетевым путём
    /// от своей настоящей (уже конечной) позиции. Это ПРИНЯТОЕ поведение, а не дефект: настоящая
    /// позиция юнита всё время полёта — конечная (решение Artsiom 49).
    ///
    /// Отдельный партиал — правило 22.
    /// </summary>
    public partial class SkillPresenter
    {
        /// <summary>Летящая модель: что показываем и сколько ещё.</summary>
        class FlightView
        {
            public Unit unit;             // чью модель ведём
            public Vector3 from;          // точка из ФАКТА события — откуда полетели
            public Vector3 to;            // куда: сетевая позиция юнита, снятая при последнем ЧУЖОМ изменении
            public Vector3 lastWritten;   // что мы сами записали в transform на прошлом кадре
            public bool written;          // писали ли уже хоть раз (до первой записи «чужого» изменения нет)
            public float total;           // всё время полёта, сек
            public float elapsed;         // сколько прошло, сек
            public float leapHeight;      // высота дуги в середине, м; 0 — по земле
            public EventPresentation onArrive;   // набор «прибыл»: играется в конце полёта; null — нечего играть
            public int abilityID;         // умение-хозяин — только для диагностики
        }

        // По одному полёту на юнита: второй полёт того же юнита перебивает первый (повторный отброс
        // геймплейно отсекается состоянием «в полёте», но телепорт кастера в полёте цели возможен).
        readonly Dictionary<Unit, FlightView> flights = new Dictionary<Unit, FlightView>();

        // Буфер завершённых полётов: словарь нельзя менять, пока по нему идёт перебор.
        readonly List<Unit> flightsDone = new List<Unit>();

        // ============================== ПРИЁМ ФАКТОВ ==============================

        /// <summary>
        /// Разбор пяти кодов семьи. Зовётся из общего приёмника факта ДО проигрывателя набора.
        /// </summary>
        /// <returns>
        /// false — набор этого факта играть СЕЙЧАС не нужно (отложен до конца полёта).
        /// true — общий путь продолжается как у любого другого набора.
        /// </returns>
        bool HandleFamilyEvent(InterflowAbility ability, Unit carrier, int level, Unit aimUnit,
                               Vector3 aimPoint, int eventCode)
        {
            switch ((AbilityEventCode)eventCode)
            {
                case AbilityEventCode.SkillCasterMoveStart:
                    HandleCasterMoveStart(ability as CompositeSkill, carrier, level, aimPoint);
                    return true;

                case AbilityEventCode.SkillCasterMoveArrive:
                    return !DeferCasterMoveArrive(ability as CompositeSkill, carrier);

                case AbilityEventCode.SkillKnockbackTarget:
                    HandleKnockbackTarget(ability as CompositeSkill, carrier, aimPoint);
                    return true;

                case AbilityEventCode.SkillMorphOn:
                    HandleMorphOn(ability as CompositeSkill, carrier);
                    return true;

                case AbilityEventCode.SkillMorphOff:
                    HandleMorphOff(carrier);
                    return true;
            }

            return true;
        }

        /// <summary>
        /// Кастер начал перемещение (код 15). Набор «старт» уже сыграл общий проигрыватель;
        /// здесь начинается ведение модели. Точка события — старая позиция кастера.
        /// </summary>
        void HandleCasterMoveStart(CompositeSkill skill, Unit caster, int level, Vector3 from)
        {
            if (skill == null || skill.casterMove == null || caster == null) return;
            if (skill.casterMove.travelSeconds <= 0f) return;   // телепорт — вести нечего

            StartFlight(caster, from, skill.casterMove.travelSeconds, skill.casterMove.leapHeight,
                        skill.casterMove.arrivePresentation, skill.id);
        }

        /// <summary>
        /// Кастер прибыл (код 16). Показ этого набора при заданном времени полёта ждёт КОНЦА полёта —
        /// единственный набор семьи, чей момент показа решает клиент (сервер признал событие сразу).
        /// </summary>
        /// <returns>true — набор отложен до конца полёта и играть его сейчас НЕ нужно.</returns>
        bool DeferCasterMoveArrive(CompositeSkill skill, Unit caster)
        {
            if (skill == null || skill.casterMove == null || caster == null) return false;
            if (skill.casterMove.travelSeconds <= 0f) return false;   // телепорт — играем сразу

            // Полёт заводит факт «старт», и он приходит РАНЬШЕ: оба факта поднимает один вызов
            // ApplyCasterMove, старт первым. Полёта нет (набор старта пуст и сообщения не породил) —
            // откладывать некуда, играем сразу, иначе набор «прибыл» потерялся бы совсем.
            return flights.ContainsKey(caster);
        }

        /// <summary>
        /// Цель отброшена (код 17). Точка события — позиция цели ДО отброса; носитель — сама цель.
        /// Набора «прибыла» у отброса нет (решение Artsiom 57 — пять событий, прибытия цели среди них нет).
        /// </summary>
        void HandleKnockbackTarget(CompositeSkill skill, Unit target, Vector3 from)
        {
            if (skill == null || skill.knockback == null || target == null) return;
            if (skill.knockback.travelSeconds <= 0f) return;

            StartFlight(target, from, skill.knockback.travelSeconds, 0f, null, skill.id);
        }

        /// <summary>
        /// Облик надет (код 18) — ТОЛЬКО подмена внешности (решение Artsiom 54). Геймплейного полиморфа
        /// у клиента не появляется: ни Unit.Polymorph, ни таймера, ни статов.
        ///
        /// Только ЧИСТЫЙ КЛИЕНТ: на хосте внешность уже подменил сервер настоящим полиморфом,
        /// второй вызов уничтожил бы подменённый рендерер и оставил юнита без модели.
        /// </summary>
        static void HandleMorphOn(CompositeSkill skill, Unit carrier)
        {
            if (!NetworkConnectionHandler.isClient) return;
            if (skill == null || skill.morph == null || skill.morph.shapeUnit == null) return;
            if (carrier == null || carrier.dead) return;

            // false — облик временный: ядро прячет исходный рендерер, а не уничтожает его,
            // и RestoreRenderers сможет его вернуть.
            carrier.ReplaceRenderers(skill.morph.shapeUnit, false);
        }

        /// <summary>
        /// Облик снят (код 19) — парный возврат внешности. Потерянный факт оставит модель превращённой
        /// до конца матча: цена принята решением Artsiom 54.
        /// </summary>
        static void HandleMorphOff(Unit carrier)
        {
            if (!NetworkConnectionHandler.isClient) return;
            if (carrier == null || carrier.dead) return;

            carrier.RestoreRenderers();
        }

        // ============================== ВЕДЕНИЕ МОДЕЛИ ==============================

        void StartFlight(Unit unit, Vector3 from, float seconds, float leapHeight,
                         EventPresentation onArrive, int abilityID)
        {
            // ТОЛЬКО ЧИСТЫЙ КЛИЕНТ (решение Artsiom 47: «полёт рисует ТОЛЬКО клиент»). На сервере
            // и на хосте transform юнита — это его ИГРОВАЯ позиция: по ней считаются клетка сетки,
            // клетка тумана, дальности и выборки в радиусе. Вести её показом значило бы двигать юнита
            // на сервере на всё время полёта (правило 6). Цена, принятая вместе с решением 47:
            // хост видит мгновенный перенос без дуги, дугу видят подключённые клиенты.
            if (!NetworkConnectionHandler.isClient) return;

            flights[unit] = new FlightView
            {
                unit = unit,
                from = from,
                to = unit.transform.position,
                written = false,
                total = seconds,
                elapsed = 0f,
                leapHeight = Mathf.Max(0f, leapHeight),
                onArrive = onArrive != null && onArrive.Any ? onArrive : null,
                abilityID = abilityID
            };
        }

        /// <summary>Покадровое ведение летящих моделей. Зовётся из Update презентера.</summary>
        void UpdateFlights()
        {
            if (flights.Count == 0) return;

            flightsDone.Clear();

            foreach (KeyValuePair<Unit, FlightView> pair in flights)
            {
                FlightView view = pair.Value;
                Unit unit = view.unit;

                // Юнит погиб или исчез — вести нечего, набор «прибыл» тоже не играем: прибыл не он.
                if (unit == null || unit.dead)
                {
                    flightsDone.Add(pair.Key);
                    continue;
                }

                // Игрок отдал команду движения: сетевая интерполяция с этого кадра сама пишет позицию
                // (и садит Y на террейн) — наш показ ей только мешал бы. Полёт обрывается, прибытие
                // считается состоявшимся: юнит геймплейно давно в конечной точке.
                if (unit.isMoving)
                {
                    PlayArrive(view);
                    flightsDone.Add(pair.Key);
                    continue;
                }

                view.elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(view.elapsed / view.total);

                // ПЕРЕНАЦЕЛИВАНИЕ. Цель полёта — сетевая позиция юнита, но читать transform каждый кадр
                // нельзя: мы сами в него пишем, и со второго кадра прочитали бы собственную точку показа.
                // Поэтому цель обновляется только когда transform изменил КТО-ТО ДРУГОЙ — то есть когда
                // пришёл снимок позиции прямым каналом (он мог прийти позже факта события, порядок
                // сообщений не гарантирован). Своё значение мы знаем точно, сравнение точное.
                Vector3 current = unit.transform.position;
                if (!view.written || (current - view.lastWritten).sqrMagnitude > 1e-6f) view.to = current;

                Vector3 shown = Vector3.Lerp(view.from, view.to, t);

                // Дуга: парабола 4·t·(1−t) даёт ноль на концах и leapHeight в середине.
                if (view.leapHeight > 0f) shown.y += view.leapHeight * 4f * t * (1f - t);

                unit.transform.position = shown;
                view.lastWritten = shown;
                view.written = true;

                if (t >= 1f)
                {
                    // Долетели: ставим модель точно в сетевую позицию — без этого остался бы остаток
                    // дуги, если последний кадр был длинным.
                    unit.transform.position = view.to;

                    PlayArrive(view);
                    flightsDone.Add(pair.Key);
                }
            }

            for (int i = 0; i < flightsDone.Count; i++) flights.Remove(flightsDone[i]);
        }

        /// <summary>Набор «прибыл», отложенный на конец полёта. Пустого набора здесь уже не бывает.</summary>
        static void PlayArrive(FlightView view)
        {
            if (view.onArrive == null || view.unit == null) return;

            PlayPresentation(view.onArrive, view.unit, view.unit, view.unit.transform.position);
        }

        /// <summary>Смена сцены: летящих моделей больше нет, ссылки на юнитов прошлой сцены не держим.</summary>
        void ClearFlights()
        {
            flights.Clear();
            flightsDone.Clear();
        }
    }
}
