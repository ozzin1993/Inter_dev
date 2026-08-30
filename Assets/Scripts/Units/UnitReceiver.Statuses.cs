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
    /// Сопротивлений и сокращения длительности здесь НЕТ и на этом шаге быть не должно: это новая
    /// механика, её дизайн решается отдельно и ПОСЛЕ переезда (§12 схемы: переезд отдельно от смены
    /// баланса; решение Artsiom 28.08.2026 «сначала переезд, дизайн потом»).
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
            // [Interflow fix 2026-08-02 effector-unify]
            // Раньше здесь правились поля САМОГО ассета (общего для всех носителей): наложение на одного юнита
            // молча меняло эффектор всем остальным и переживало выход из Play Mode. Теперь фактические
            // параметры наложения считаются локально и живут в EffectorHolder.

            // Длительность: переопределение умения, иначе значение ассета. У бессрочного смысла не имеет.
            float duration = (!p.effector.permanent && p.durationOverride > 0f) ? p.durationOverride : p.effector.duration;
            // Just a check of duration, it should not be less than GameManager.everyFrameAbilityTickRate * 2
            if (duration < GameManager.tickRate * 2) duration = GameManager.tickRate * 2;
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
                    if (!Mathf.Approximately(existing.powerMultiplier, p.powerMultiplier)) continue;
                    if (!Mathf.Approximately(existing.duration, duration)) continue;

                    existing.currentTime = 0;
                    // [Interflow fix 2026-08-05 unit-status-sync] Продление наложения — сообщить клиентам
                    // (единый канал статусов; внутри гейт «только сервер» — локальные ауры клиента не шлют).
                    if ((p.effector.icon != null || p.effector.VFX != null) && NetworkDataSync.Instance != null)
                        NetworkDataSync.Instance.UnitStatusEffectorSend(unit, p.effector.id, duration);
                    return;
                }
            }

            // Add new effector to the unit
            EffectorHolder newEH = new EffectorHolder(p.effector, p.unitOwner, p.owner, duration, stacks, p.powerMultiplier);
            newEH.currentTime = p.currentTime;
            unit.effectors.Add(newEH);
            if (newEH.effector.VFX != null) unit.AddVFX(newEH.effector.VFX, newEH.effector.aboveHead);

            // Add passive effects
            if (newEH.effector.passiveEffectsOn) newEH.effector.passiveEffects.AddEffect(unit, p.powerMultiplier);

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
            if ((newEH.effector.icon != null || newEH.effector.VFX != null) && NetworkDataSync.Instance != null)
                NetworkDataSync.Instance.UnitStatusEffectorSend(unit, newEH.effector.id, duration);
        }
    }
}
