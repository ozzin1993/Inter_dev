using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Партиал Unit: КОНТРОЛЬ КАК СОСТОЯНИЕ (решение Artsiom 30.08.2026, промт
    /// «Промт_Приёмник_Контроль_Эффекторами.md»).
    ///
    /// Единственный владелец истины про контроль — список наложенных состояний. «Юнит оглушён» =
    /// «на нём висит хоть один эффектор с признаком оглушения». Собственных таймеров у оглушения,
    /// немоты, обезоруживания и слепоты больше нет: время отсчитывает жизненный цикл наложения,
    /// поэтому диспел состояния снимает и контроль, а несколько наложений держат контроль
    /// до истечения ПОСЛЕДНЕГО из них.
    ///
    /// Здесь же живут вход и выход каждого вида контроля — тела, переехавшие из бывших
    /// <c>StunApply</c>/<c>MuteApply</c>/<c>DisarmApply</c> (вход) и из бывших
    /// <c>StunUpdate</c>/<c>MuteUpdate</c>/<c>DisarmUpdate</c> (выход) БЕЗ изменений,
    /// минус таймеры и минус рассылки: клиенту контроль теперь едет единым каналом статусов.
    ///
    /// Отдельный партиал — правило 22: <c>Unit.State.cs</c> и без того на две тысячи строк.
    /// </summary>
    public partial class Unit
    {
        // ============================================ НАЛОЖЕНИЕ ==

        /// <summary>
        /// Наложить служебное состояние контроля. Общая часть всех четырёх воронок
        /// (<see cref="Stun"/>, <see cref="Mute"/>, <see cref="Disarm"/>, <see cref="Blind"/>):
        /// проверить, что ассет назначен в справочнике, и отдать его штатному наложению состояний —
        /// дальше работают приёмник (иммунитет) и пересчёт (переходы).
        /// </summary>
        /// <param name="controlEffector">Служебный ассет из StatusIconCatalog. null — контент не настроен.</param>
        /// <param name="kind">Вид контроля по-русски, для сообщения о ненастроенном справочнике.</param>
        /// <param name="time">Длительность наложения, секунды. Меньше двух тиков приёмник поднимет до них.</param>
        /// <param name="sourceUnit">Юнит-источник, может быть null.</param>
        /// <param name="sourceOwner">Слот игрока-источника.</param>
        /// <param name="powerMultiplier">Сила наложения. У ослепления ею едет шанс промаха, у остальных 1.</param>
        void ControlEffectorApply(Effector controlEffector, string kind, float time,
                                  Unit sourceUnit, int sourceOwner, float powerMultiplier)
        {
            if (controlEffector == null)
            {
                Debug.LogWarning("[Контроль] В справочнике Resources/Catalogs/StatusIconCatalog не назначено " +
                                 "служебное состояние " + kind + " — контроль на «" + unitName + "» не наложен.");
                return;
            }

            Effector.EffectorAdd(this, controlEffector, sourceUnit, sourceOwner, 0f, powerMultiplier, time);
        }

        /// <summary>
        /// [Interflow 2026-09-18, решение Artsiom 50] Отметить юнита летящим на <paramref name="time"/> секунд.
        /// Воронка ровно того же вида, что <see cref="Stun"/>/<see cref="Mute"/>/<see cref="Disarm"/>:
        /// ассет берётся из справочника (правило 3 — ссылок в коде не держим) и уходит штатному
        /// наложению состояний. Запрет активных действий задают галки САМОГО ассета, а не этот метод.
        ///
        /// Мимо сопротивлений и иммунитета к контролю — исключение живёт в приёмнике
        /// (Units/UnitReceiver.Statuses.cs), здесь ничего особенного не делается.
        /// Повторное наложение штатно продлевает висящее (слипание по id, команде, силе и длительности).
        /// </summary>
        /// <param name="time">Время полёта, секунды (travelSeconds блока 15).</param>
        /// <param name="sourceUnit">Кто отбросил. null допустим.</param>
        /// <param name="sourceOwner">Слот игрока-источника.</param>
        public void InFlightStart(float time, Unit sourceUnit, int sourceOwner)
        {
            if (staticObject) return;
            if (dead) return;
            if (time <= 0f) return;

            StatusIconCatalog catalog = StatusIconCatalog.Get();
            ControlEffectorApply(catalog != null ? catalog.inFlightEffector : null, "полёта (отброс)",
                                 time, sourceUnit, sourceOwner, 1f);
        }

        // ==================================================== ПЕРЕСЧЁТ ==

        /// <summary>
        /// Пересобрать контроль по висящим состояниям и применить переходы. ИДЕМПОТЕНТЕН:
        /// повторный вызов без изменений в списке не делает ничего. Звать обязаны все, кто меняет
        /// набор состояний, — наложение, истечение, диспел, приход и уход статуса у клиента.
        ///
        /// Вход и выход дёргаются РОВНО на смене факта, а не на каждом наложении: иначе второе
        /// оглушение поверх первого заморозило бы юнита дважды, а истечение одного из двух
        /// разморозило бы его при живом втором.
        /// </summary>
        public void RecalculateControl()
        {
            bool wantStunned = false;
            bool wantMuted = false;
            bool wantDisarmed = false;
            bool wantInFlight = false;
            float wantMissChance = 0f;

            // Источник 1 — наложения в локальной симуляции этого пира. На сервере это весь контроль.
            // У клиента сюда попадают его собственные ауры и то, что восстановила загрузка сцены
            // при входе в середине матча (сейв несёт строку состояний).
            for (int i = 0; i < effectors.Count; i++)
            {
                EffectorHolder eh = effectors[i];
                if (eh == null || eh.effector == null) continue;

                CollectControl(eh.effector, eh.powerMultiplier,
                               ref wantStunned, ref wantMuted, ref wantDisarmed, ref wantInFlight, ref wantMissChance);
            }

            // Источник 2 — только у клиента: состояния, присланные сервером единым каналом статусов.
            // Сами эффекторы по сети не ходят (состояние живёт на сервере), клиенту приезжает
            // «покажи вот это» и он разворачивает ассет по id — признаки контроля читаем оттуда же.
            if (NetworkConnectionHandler.isClient)
            {
                SkillVisualStatus visual = GetComponent<SkillVisualStatus>();
                if (visual != null)
                {
                    for (int i = 0; i < visual.EntryCount; i++)
                    {
                        if (!visual.TryGetEffector(i, out Effector shown)) continue;

                        // Силу наложения канал не несёт, и она клиенту не нужна: бросок промаха
                        // делает только сервер, а флаги контроля от силы не зависят.
                        CollectControl(shown, 1f,
                                       ref wantStunned, ref wantMuted, ref wantDisarmed, ref wantInFlight, ref wantMissChance);
                    }
                }
            }

            ApplyControlTransitions(wantStunned, wantMuted, wantDisarmed, wantInFlight, wantMissChance);
        }

        /// <summary>Сложить признаки одного состояния в собираемые факты. Шанс промаха — СУММА висящих
        /// ослеплений с зажимом до единицы (решение Artsiom Р3, 03.09.2026, шаг 4 схемы: «проценты
        /// складываются, как везде в игре»); до шага 4 бралось наибольшее.</summary>
        static void CollectControl(Effector effector, float powerMultiplier,
                                   ref bool stun, ref bool mute, ref bool disarm, ref bool inFlightFact,
                                   ref float missChance)
        {
            if (effector.stuns) stun = true;
            if (effector.mutes) mute = true;
            if (effector.disarms) disarm = true;

            // [Interflow 2026-09-18, решение Artsiom 50] Факт «юнит прямо сейчас летит от отброса»
            // выводится ЗДЕСЬ, из списка состояний, тем же способом, что оглушение, немота и безоружие
            // (решение 03.09.2026 — прямой записи флага нет). Признак — СЛУЖЕБНАЯ КАТЕГОРИЯ состояния,
            // а не отдельная галка ассета: категория у состояния и так одна, и «в полёте» — её смысл.
            // Сам запрет активных действий на время полёта задаёт КОНТЕНТ ассета (галки «Обезоруживает»
            // и «Накладывает немоту» у Eff_Status_InFlight): ни одной новой точки запрета не заведено,
            // работают те, что уже разведены по автомату состояний и по входу применения умения.
            if (effector.category == EffectorCategory.InFlight) inFlightFact = true;

            if (effector.blinds && effector.blindMissChance > 0f)
            {
                float chance = Mathf.Clamp01(effector.blindMissChance * powerMultiplier);
                missChance = Mathf.Clamp01(missChance + chance);
            }
        }

        /// <summary>Применить собранные факты: вход на false→true, выход на true→false, иначе ничего.</summary>
        void ApplyControlTransitions(bool wantStunned, bool wantMuted, bool wantDisarmed, bool wantInFlight,
                                     float wantMissChance)
        {
            bool onClient = NetworkConnectionHandler.isClient;

            // [Interflow 2026-09-18] «В полёте» — ЧИСТЫЙ ФАКТ, без входа и выхода: никакого своего
            // перехода у него нет и заводить его нечем. Остановку текущего действия делают переходы
            // безоружия и немоты (AttackStop, EndActiveAbility) — их поднимает тот же ассет
            // состояния, поэтому второй раз то же самое делать нельзя (двойная остановка).
            // Читает флаг только блок 15: повторный отброс летящей цели пропускается (решение 50).
            inFlight = wantInFlight;

            if (wantStunned != stunned)
            {
                if (wantStunned) { if (onClient) StunEnterClient(); else StunEnter(); }
                else { if (onClient) StunExitClient(); else StunExit(); }
            }

            if (wantMuted != muted)
            {
                if (wantMuted) { if (onClient) MuteEnterClient(); else MuteEnter(); }
                else { if (onClient) MuteExitClient(); else MuteExit(); }
            }

            if (wantDisarmed != disarmed)
            {
                if (wantDisarmed) { if (onClient) DisarmEnterClient(); else DisarmEnter(); }
                else { if (onClient) DisarmExitClient(); else DisarmExit(); }
            }

            // Слепота: бросок промаха делает только сервер (как штатное уклонение), у клиента
            // от неё остаётся один значок — его рисует канал статусов.
            if (onClient) return;

            // [Interflow fix 2026-09-04 damage-full-packet] Писатель шанса промаха ОДИН — этот пересчёт:
            // он уже сложил шансы всех висящих ослеплений (решение Artsiom Р3: сумма, не наибольшее).
            // Сначала чистим, потом ставим то, что осталось висеть, — иначе снятое ослепление не убрать.
            // Повтор с тем же числом даёт то же состояние словаря — вызов идемпотентен.
            InterflowCombat.MissChanceClear(this);
            if (wantMissChance > 0f) InterflowCombat.MissChanceSet(this, wantMissChance);
        }

        // ============================================ СЕРВЕРНЫЕ ПЕРЕХОДЫ ==
        // Тела перенесены из прежних StunApply/MuteApply/DisarmApply (вход) и из хвостов
        // StunUpdate/MuteUpdate/DisarmUpdate (выход) построчно. Ушли только работа с таймерами
        // (полей больше нет) и рассылки StunSetSend/MuteSetSend/DisarmSetSend (канал статусов
        // уже везёт служебное состояние клиенту сам).

        /// <summary>Оглушение началось: заморозить юнита.</summary>
        void StunEnter()
        {
            // Флаг ставится ПЕРВЫМ, а не последним, как в прежнем StunApply. Поведение то же — ни одно
            // из тел ниже флаги контроля не читает (проверено: Unit.Commands.Idle, Unit.State.AttackStop,
            // Unit.Ability.EndActiveAbility, Unit.Motion.MakeAgent), — но вложенный пересчёт теперь
            // невозможен: если какое-то из них однажды тронет список состояний, RecalculateControl
            // увидит факт уже выставленным и второй заморозки не сделает.
            stunned = true;

            // Freeze the unit; (ветка «умение исполняется» снесена блоком Б6 — каналов больше нет)
            if (!firstAttack) AttackStop();
            MakeAgent(false);
            EndActiveAbility(false, true);
        }

        /// <summary>Оглушение кончилось: разморозить и прибрать за собой.</summary>
        void StunExit()
        {
            stunned = false;

            // Ability cast
            if (unitState == UnitStates.AbilityCasting)
            {
                currentActionTime = 0;
                sendToClients = false;
                playCast = false;
            }

            // Construction animation
            if (constructionUnit && !constructionUnit.isBuilding && constructionUnit.isWorking)
            {
                AnimatorSetBool(AnimationState.Building, true);
            }

            if (stunnedVFX != null) Destroy(stunnedVFX.gameObject);
        }

        /// <summary>Немота началась: оборвать применяемое умение.</summary>
        void MuteEnter()
        {
            // Флаг первым — по той же причине, что и у оглушения (защита от вложенного пересчёта).
            muted = true;

            // Mute the unit (ветка «умение исполняется» снесена блоком Б6 — каналов больше нет)
            EndActiveAbility(false, true);
        }

        /// <summary>Немота кончилась.</summary>
        void MuteExit()
        {
            muted = false;

            // Ability cast
            if (unitState == UnitStates.AbilityCasting)
            {
                currentActionTime = 0;
                sendToClients = false;
                playCast = false;
            }

            if (mutedVFX != null) Destroy(mutedVFX.gameObject);
        }

        /// <summary>Обезоруживание началось: остановить атаку.</summary>
        void DisarmEnter()
        {
            // Флаг первым — по той же причине, что и у оглушения (защита от вложенного пересчёта).
            disarmed = true;

            // Stop attack
            if (!firstAttack) AttackStop();
        }

        /// <summary>Обезоруживание кончилось.</summary>
        void DisarmExit()
        {
            disarmed = false;

            if (disarmedVFX != null) Destroy(disarmedVFX.gameObject);
        }

        // ============================================ КЛИЕНТСКИЕ ПЕРЕХОДЫ ==
        // Тела перенесены из прежних Unit.Stun(bool)/Mute(bool)/Disarm(bool) построчно — это были
        // приёмные концы рассылок контроля. Рассылок больше нет, зовёт их теперь пересчёт по
        // статусам канала. Клиент только отражает состояние: агент, атака и умения — на сервере.

        /// <summary>Клиент: оглушение появилось.</summary>
        void StunEnterClient()
        {
            stunned = true;

            // Turn off move animation
            if (m_walkAnimationPlaying)
            {
                m_walkAnimationPlaying = false;
                AnimatorSetBool(AnimationState.Walk, false);
            }

            // Construction animation
            if (constructionUnit && !constructionUnit.isBuilding && constructionUnit.isWorking)
            {
                AnimatorSetBool(AnimationState.Building, false);
            }
        }

        /// <summary>Клиент: оглушение снято.</summary>
        void StunExitClient()
        {
            stunned = false;
            if (stunnedVFX != null) Destroy(stunnedVFX.gameObject);

            // Construction animation
            if (constructionUnit && !constructionUnit.isBuilding && constructionUnit.isWorking)
            {
                AnimatorSetBool(AnimationState.Building, true);
            }
        }

        /// <summary>Клиент: немота появилась.</summary>
        void MuteEnterClient()
        {
            muted = true;
        }

        /// <summary>Клиент: немота снята.</summary>
        void MuteExitClient()
        {
            muted = false;
            if (mutedVFX != null) Destroy(mutedVFX.gameObject);
        }

        /// <summary>Клиент: обезоруживание появилось.</summary>
        void DisarmEnterClient()
        {
            disarmed = true;
        }

        /// <summary>Клиент: обезоруживание снято.</summary>
        void DisarmExitClient()
        {
            disarmed = false;
            if (disarmedVFX != null) Destroy(disarmedVFX.gameObject);
        }
    }
}
