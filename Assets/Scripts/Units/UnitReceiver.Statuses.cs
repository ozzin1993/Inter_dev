using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Приёмник юнита — приём НАЛОЖЕНИЯ СОСТОЯНИЙ (шаг 2 схемы «пакет и приёмник», §12).
    ///
    /// Сюда переехала приёмная часть основной <c>Effector.EffectorAdd</c> — ОДИН В ОДИН, без единого
    /// изменения чисел и порядка вызовов: расчёт длительности и стакинга, слипание одинаковых
    /// наложений, создание держателя, применение и отправка статуса. Воронка
    /// <c>Effector.EffectorAdd</c> осталась тонкой обёрткой: отсев «есть ли кому адресовать пакет»
    /// (здание, труп) и сбор пакета.
    ///
    /// Отдельный файл — правило 22: новая фича кладётся партиалом, <c>UnitReceiver.cs</c> не раздувается.
    ///
    /// С 2026-09-03 здесь же — ЕДИНСТВЕННАЯ точка применения СОПРОТИВЛЕНИЙ И СЛАБОСТЕЙ к категориям
    /// состояний (дизайн решён после переезда, как и требовал §12 схемы; решения Artsiom 29.08–03.09.2026):
    /// действующую долю отдаёт носитель <see cref="UnitResistances"/> через кеш приёмника, а приёмник
    /// режет силу (числовые категории) или время (контроль) ДО слипания и до создания держателя.
    ///
    /// С 2026-09-03 здесь же живёт ОТКАЗ ПО ИММУНИТЕТУ к контролю: контроль стал состоянием,
    /// и единственная точка, где решается «пускать ли», — наложение состояния. Прежний
    /// <c>UnitReceiver.Receive(in ControlPacket)</c> снесён вместе с пакетом.
    ///
    /// Клиентского запрета здесь НЕТ и на этом шаге быть не должно: сегодня наложение считается
    /// на том пире, который его позвал (локальные ауры клиента), а гейт «только сервер» живёт внутри
    /// <c>NetworkDataSync.UnitStatusEffectorSend</c>. Как на шагах 0 и 1.
    /// </summary>
    public partial class UnitReceiver
    {
        /// <summary>
        /// Принять пакет наложения состояния. Ничего не возвращает — сегодняшняя
        /// <c>Effector.EffectorAdd</c> тоже не возвращает ничего, а шаг 2 переносит поведение
        /// один в один.
        /// </summary>
        public void Receive(in EffectorPacket p)
        {
            // Лог полного уровня. Загрузка сохранения молчит (решение Artsiom 07.09.2026).
            bool log = InterflowDebug.FullOn && !p.restoring;

            // [Interflow fix 2026-09-03 control-as-effectors] Иммунитет к контролю отбивает НАЛОЖЕНИЕ
            // целиком, если состояние несёт оглушение, немоту или обезоруживание. Точка проверки одна
            // на все виды — прежде она жила в снесённом UnitReceiver.Receive(in ControlPacket).
            // Ослепление иммунитет НЕ отбивает: он не отбивал его и до перестройки (решение 28.08.2026
            // распространило иммунитет на три вида, слепота в них не входила).
            // Смешанный ассет «контроль + статы» у иммунного отбивается ЦЕЛИКОМ — правило промта;
            // в контенте таких ассетов нет.
            // Загрузка сохранения проходит мимо: она возвращает «как было», а не накладывает заново
            // (решение Artsiom 28.08.2026 «сейв — чинить»).
            if (!p.restoring && (p.effector.stuns || p.effector.mutes || p.effector.disarms) && ControlImmune)
            {
                // Факт презентации «состояние отбито иммунитетом» (§15, точка 5).
                if (ShowStatusFact(in p)) RaiseBattleFact(BattleFactReason.StatusImmune);

                if (log)
                    InterflowDebug.Full("ПРИЁМНИК СОСТОЯНИЕ: отбито иммунитетом к контролю | " + InterflowDebug.Name(unit) +
                                        " | состояние=" + p.effector.name);
                return;
            }

            // [Interflow fix 2026-08-02 effector-unify]
            // Раньше здесь правились поля САМОГО ассета (общего для всех носителей): наложение на одного юнита
            // молча меняло эффектор всем остальным и переживало выход из Play Mode. Теперь фактические
            // параметры наложения считаются локально и живут в EffectorHolder.

            // Длительность: переопределение умения, иначе значение ассета. У бессрочного смысла не имеет.
            float duration = (!p.effector.permanent && p.durationOverride > 0f) ? p.durationOverride : p.effector.duration;
            // Сила наложения. Локальная копия: ниже её может изменить сопротивление.
            float power = p.powerMultiplier;

            // [Interflow fix 2026-09-03 status-resistances] Сопротивления и слабости носителя к категории
            // состояния (решения Artsiom 29.08–03.09.2026). Одна точка на все источники наложений.
            // r ≥ 1 (100 % и больше) — наложение отбивается ЦЕЛИКОМ: ни держателя, ни статуса клиенту
            // (так выражается «игнорирует любые замедления»). С 07.09.2026 отказ НЕ молчит: поднимается
            // факт презентации «отбито сопротивлением» (§15). Иначе категория контроля режет ВРЕМЯ, числовая — СИЛУ; слабость
            // (r < 0) даёт множитель больше единицы, предела нет (MVP). Порядок: сопротивление считается
            // ДО минимума длительности и ДО слипания — слипание сравнивает уже фактические силу и длительность,
            // поэтому два наложения одного ассета на одном юните при одном и том же r слипаются как раньше;
            // если r изменился между ними (появился баф-слабость), второе не слипнется с первым и они
            // сосуществуют — названо в логе, не чинится (промт §9).
            // Ассет без категории («Нет») сопротивлениями не задевается; загрузка сохранения проходит мимо
            // (p.restoring): она возвращает уже посчитанные при сохранении силу и длительность.
            if (!p.restoring && p.effector.category != EffectorCategory.None)
            {
                float r = Resistance(p.effector.category);
                if (r >= 1f)
                {
                    // Факт презентации «состояние отбито сопротивлением» (§15, точка 6).
                    if (ShowStatusFact(in p)) RaiseBattleFact(BattleFactReason.StatusResisted);

                    if (log)
                        InterflowDebug.Full("ПРИЁМНИК СОСТОЯНИЕ: отбито сопротивлением | " + InterflowDebug.Name(unit) +
                                            " | состояние=" + p.effector.name +
                                            " | категория=" + p.effector.category +
                                            " | доля=" + r.ToString("0.##"));
                    return;
                }

                bool cutsTime = UnitResistances.CutsTime(p.effector.category);
                float beforeCut = cutsTime ? duration : power;

                if (cutsTime) duration *= 1f - r;
                else power *= 1f - r;

                if (log && r != 0f)
                    InterflowDebug.Full("ПРИЁМНИК СОСТОЯНИЕ: сопротивление | " + InterflowDebug.Name(unit) +
                                        " | состояние=" + p.effector.name +
                                        " | категория=" + p.effector.category +
                                        " | доля=" + r.ToString("0.##") +
                                        " | режет=" + (cutsTime ? "время" : "силу") +
                                        " | " + beforeCut.ToString("0.##") + " → " +
                                        (cutsTime ? duration : power).ToString("0.##"));
            }

            // Just a check of duration, it should not be less than GameManager.everyFrameAbilityTickRate * 2
            if (duration < GameManager.tickRate * 2)
            {
                float beforeMinimum = duration;
                duration = GameManager.tickRate * 2;

                // Бессрочные молчат: у них длительность в ассете обычно 0, и минимум срабатывал бы всегда,
                // хотя на бессрочное состояние он не влияет.
                if (log && !p.effector.permanent)
                    InterflowDebug.Full("ПРИЁМНИК СОСТОЯНИЕ: длительность поднята до минимума | " + InterflowDebug.Name(unit) +
                                        " | состояние=" + p.effector.name +
                                        " | " + beforeMinimum.ToString("0.##") + " → " + duration.ToString("0.##"));
            }

            // Make sure invisibility should not stack, will cause a bug
            bool stacks = p.effector.stacks && !p.effector.makeInvisible;

            if (!stacks)
            {
                // If similar effector is already added to the unit by the same team, just reset the currentTime
                for (int i = 0; i < unit.effectors.Count; i++)
                {
                    EffectorHolder existing = unit.effectors[i];
                    if (existing.effector.id != p.effector.id) continue;
                    if (SlotManager.Instance.playerTeam[existing.owner] != SlotManager.Instance.playerTeam[p.owner]) continue;

                    // Слипаются только ПОЛНОСТЬЮ одинаковые наложения. Разная сила или разная длительность —
                    // разные эффекты: они сосуществуют и суммируются. Так было и до схлопывания ассетов,
                    // когда «замедление на 40 %» и «замедление на 50 %» были разными эффекторами с разными id.
                    if (!Mathf.Approximately(existing.powerMultiplier, power)) continue;
                    if (!Mathf.Approximately(existing.duration, duration)) continue;

                    existing.currentTime = 0;

                    if (log)
                        InterflowDebug.Full("ПРИЁМНИК СОСТОЯНИЕ: продлено | " + InterflowDebug.Name(unit) +
                                            " | состояние=" + p.effector.name +
                                            " | сила=" + power.ToString("0.##") +
                                            " | длительность=" + duration.ToString("0.#"));
                    // [Interflow fix 2026-08-05 unit-status-sync] Продление наложения — сообщить клиентам
                    // (единый канал статусов; внутри гейт «только сервер» — локальные ауры клиента не шлют).
                    if ((p.effector.icon != null || p.effector.VFX != null) && NetworkDataSync.Instance != null)
                        NetworkDataSync.Instance.UnitStatusEffectorSend(unit, p.effector.id, duration);
                    return;
                }
            }

            // Add new effector to the unit
            EffectorHolder newEH = new EffectorHolder(p.effector, p.unitOwner, p.owner, duration, stacks, power);
            newEH.currentTime = p.currentTime;
            unit.effectors.Add(newEH);

            if (log)
                InterflowDebug.Full("ПРИЁМНИК СОСТОЯНИЕ: наложено | " + InterflowDebug.Name(unit) +
                                    " | состояние=" + newEH.effector.name +
                                    " | категория=" + newEH.effector.category +
                                    " | сила=" + power.ToString("0.##") +
                                    " | длительность=" + duration.ToString("0.#") +
                                    " | стакается=" + (stacks ? "да" : "нет") +
                                    " | всего состояний=" + unit.effectors.Count);

            if (newEH.effector.VFX != null) unit.AddVFX(newEH.effector.VFX, newEH.effector.aboveHead);

            // Add passive effects
            if (newEH.effector.passiveEffectsOn) newEH.effector.passiveEffects.AddEffect(unit, power);

            // Make Invisible
            if (newEH.effector.makeInvisible) unit.SetInvisibility(true);

            // If can be seen when invisible, it is used to not hide the renderers
            // Should be applied only by the current player`s team
            if (newEH.effector.revealInvisible) unit.CanBeSeen(true, p.owner);

            if (!newEH.stacks) unit.OnStatusUpdate?.Invoke();

            // [Interflow fix 2026-08-05 unit-status-sync] Единый канал статусов: отправка «эффектор
            // появился» из ОДНОЙ точки — покрывает атаки, ауры и скиллы одинаково (решение Artsiom
            // 2026-08-05). Шлём только то, что клиенту есть чем показать (значок или VFX);
            // гейт «только сервер» живёт внутри UnitStatusEffectorSend.
            // [Interflow fix 2026-09-03 control-as-effectors] Держатель добавлен — пересобрать контроль.
            // До отправки статуса: к моменту, когда клиент увидит значок, сервер уже заморожен.
            //
            // ЗАГРУЗКА СОХРАНЕНИЯ — исключение (p.restoring): она восстанавливает состояния РАНЬШЕ,
            // чем маршрут, цель и активное умение (SaveManager.UnitData.cs: эффекторы :858, состояние
            // :892, атака :1022, умение :1050). Заморозь юнита здесь — и сейв тут же вернёт ему всё
            // отнятое, а он останется stunned с целью и маршрутом. До перестройки контроль
            // восстанавливался ПОСЛЕДНИМ, и этот порядок сохранён: путь загрузки зовёт
            // RecalculateControl один раз в конце, когда юнит собран целиком.
            if (!p.restoring) unit.RecalculateControl();

            if ((newEH.effector.icon != null || newEH.effector.VFX != null) && NetworkDataSync.Instance != null)
                NetworkDataSync.Instance.UnitStatusEffectorSend(unit, newEH.effector.id, duration);
        }

        /// <summary>
        /// Показывать ли факт презентации по этому наложению (§15, точки 5 и 6).
        ///
        /// ТОЛЬКО СЕРВЕР (правило 6): клиентского запрета в приёме наложений нет и быть не должно
        /// (см. заголовок файла) — наложение считается на том пире, который его позвал, и без этой
        /// проверки чистый клиент нарисовал бы надпись сам, мимо сервера.
        ///
        /// Правило частоты §15 в редакции решения Artsiom от 07.09.2026 (вариант А — буквально §5.2 промта):
        /// молчит только источник с НЕПРЕРЫВНОЙ атакой — он вешает состояния своей автоатаки каждый кадр
        /// (UnitReceiver.cs, состояния атаки бьющего). Признака источника в EffectorPacket нет, и заводить
        /// его нельзя (правило 7), поэтому периодические источники наложений — ауры и зоны на тике 0,1 с
        /// (MovementAura, CompositePassive блок «аура», GroundDamageZone, LineCompositionAura) — под правило
        /// НЕ попадают и дают до десяти надписей в секунду на юнита. Цена принята решением Artsiom 07.09.2026.
        /// </summary>
        bool ShowStatusFact(in EffectorPacket p)
        {
            return !NetworkConnectionHandler.isClient && NotContinuous(p.unitOwner);
        }
    }
}
