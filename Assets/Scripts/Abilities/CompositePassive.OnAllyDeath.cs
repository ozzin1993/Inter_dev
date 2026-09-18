using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ==== КОНСТРУКТОР ПАССИВКИ: ОСЬ «РЕАКЦИИ», СОБЫТИЕ 7 (правило 22 — партиал по фиче) ====
    // «Погиб союзник» — седьмое событие оси реакций (решение Artsiom 40 от 17.09.2026, ветка В1
    // проекта «Смерть и убийство» §3.3). Данные блока и подписка живут в одном партиале по образцу
    // реакции 6: разводить их по двум файлам ради одного блока дороже, чем держать рядом.
    //
    // Точка подключения — ХАБ СМЕРТЕЙ MatchManager.OnUnitDeathServer (Match/MatchManager.DeathEvents.cs:19):
    // только там одновременно видно жертву и убийцу, и только там смерть ЧУЖОГО юнита вообще доходит
    // до нашего носителя. Штатное Unit.OnDie здесь не годится: подписываться пришлось бы на каждого
    // союзника отдельно и переподписываться на каждого нового — ровно то, что делал мёртвый кирпич
    // SoulHarvest.cs (он не стоит ни на одном префабе; его судьба — отдельное решение, файл не тронут).
    //
    // ПОКРЫТИЕ ХАБА ограничено путями спавна (волна, герой, призванные); БАШНИ в него не входят
    // (DeathEvents.cs:9-10) — гибель башни эту реакцию не поднимет. Для орков линии этого достаточно.
    //
    // ТРИ ИНВАРИАНТА ТОЧКИ (проект §4.2), каждый легко нарушить в обратную сторону:
    //   1) жертву НЕЛЬЗЯ проверять на dead — Unit.Die ставит dead = true ДО вызова OnDie
    //      (Units/Unit.Combat.cs:345-348), и такая проверка выключила бы реакцию целиком;
    //   2) носитель на dead проверяется ОБЯЗАТЕЛЬНО — мёртвый герой не лечится;
    //   3) victim == carrier отсекается — иначе гибель самого носителя сработала бы как гибель союзника.
    //
    // Подписка ОДНА НА АССЕТ, а не на носителя (как у реакции 3 и у шкалы): событие хаба всё равно
    // общее, а своих носителей обработчик отбирает по словарю reactionCarriers.

    /// <summary>Реакция 7. Погиб союзник: носитель получает здоровье за чужую смерть.</summary>
    [System.Serializable]
    public class PassiveOnAllyDeathBlock
    {
        [Tooltip("Включить блок: когда рядом погибает союзник, носитель получает здоровье.")]
        public bool enabled;

        [Tooltip("Чьи смерти считаются, списком заготовок. Пусто — любой союзник, прошедший селектор ниже. " +
                 "Сверка идёт по типу юнита, а не по ссылке на префаб.")]
        public Unit[] victimPrefabs;

        [Tooltip("Кого считать союзником (обычно свой и союзник + юнит + земля/вода/воздух). " +
                 "Пустой селектор не пропускает НИКОГО — блок будет молчать.")]
        public UnitSelector victimSelector;

        [Tooltip("На каком расстоянии от носителя засчитывается гибель, метры, по уровням. " +
                 "0 или пусто — без ограничения расстоянием: считается любая смерть на карте.")]
        public float[] radius;

        [Tooltip("Лечение носителю числом, по уровням. 0 — не лечить числом.")]
        public float[] healFlat;

        [Tooltip("Лечение носителю долей от ЕГО максимального здоровья, по уровням. " +
                 "Складывается с лечением числом.")]
        public float[] healPercentOfMaxHp;

        [Tooltip("Визуал события «погиб союзник»: играется на НОСИТЕЛЕ, а не на погибшем. Пусто — без визуала.")]
        public EventPresentation presentation = new EventPresentation();
    }

    public partial class CompositePassive
    {
        [Header("Реакция 7 — погиб союзник")]
        public PassiveOnAllyDeathBlock onAllyDeath = new PassiveOnAllyDeathBlock();

        // Перебор носителей идёт по КОПИИ ключей: лечение носителя поднимает его событие здоровья,
        // а через него — реакцию 4, которая может закрыть умение и тронуть сам словарь.
        readonly List<Unit> allyDeathBuffer = new List<Unit>();

        /// <summary>Включена ли реакция 7 — отдельный предикат, чтобы читать его из соседнего партиала.</summary>
        bool IsAllyDeathEnabled() { return onAllyDeath != null && onAllyDeath.enabled; }

        /// <summary>Подписка на хаб смертей — одна на весь ассет, как у реакции 3.</summary>
        void WireAllyDeathHub()
        {
            if (!IsAllyDeathEnabled()) return;
            if (allyDeathHubWired || MatchManager.Instance == null) return;

            MatchManager.Instance.OnUnitDeathServer += HandleAllyDeathForReactions;
            allyDeathHubWired = true;
        }

        void UnwireAllyDeathHub()
        {
            if (!allyDeathHubWired) return;

            if (MatchManager.Instance != null) MatchManager.Instance.OnUnitDeathServer -= HandleAllyDeathForReactions;
            allyDeathHubWired = false;
        }

        /// <summary>
        /// Годится ли эта смерть для этого носителя. Чистая функция: ВСЕ отборы живут здесь,
        /// в проверяемом месте, а не в обработчике (тот же приём, что у GaugeHitDecision).
        /// Жертва на dead не проверяется НАМЕРЕННО — см. инвариант 1 в шапке файла.
        /// </summary>
        /// <param name="carrierDead">Мёртв ли носитель. Параметром, а не чтением поля: в режиме
        /// редактора убить юнита нечем, и гейт иначе остался бы без теста.</param>
        /// <param name="radiusValue">Радиус на уровне носителя. 0 и меньше — без ограничения расстоянием.</param>
        public static bool AllyDeathAccepted(PassiveOnAllyDeathBlock b, Unit victim, Unit carrier,
                                             bool carrierDead, float radiusValue)
        {
            if (b == null || !b.enabled) return false;
            if (victim == null || carrier == null) return false;
            if (carrierDead) return false;                       // инвариант 2
            if (victim == carrier) return false;                 // инвариант 3

            if (!CompositeSkill.PrefabAllowed(victim, b.victimPrefabs)) return false;
            if (!UnitSelector.IsUnitCompatible(carrier.owner, victim, b.victimSelector)) return false;

            if (radiusValue > 0f)
            {
                // Квадрат расстояния: корень здесь не нужен, а событие приходит на КАЖДУЮ смерть в матче.
                float sqr = (victim.transform.position - carrier.transform.position).sqrMagnitude;
                if (sqr > radiusValue * radiusValue) return false;
            }

            return true;
        }

        /// <summary>
        /// Погиб юнит (сервер). Событие приходит на КАЖДУЮ смерть в матче, поэтому своих носителей
        /// отбираем по словарю: чужая смерть — не отказ реакции, а чужое событие.
        /// </summary>
        void HandleAllyDeathForReactions(Unit victim, int killerPlayer, Unit killerUnit, bool rewards)
        {
            if (NetworkConnectionHandler.isClient) return;       // правило 6
            if (!IsAllyDeathEnabled()) return;
            if (victim == null || reactionCarriers.Count == 0) return;

            allyDeathBuffer.Clear();
            allyDeathBuffer.AddRange(reactionCarriers.Keys);

            int healedCarriers = 0;

            for (int i = 0; i < allyDeathBuffer.Count; i++)
            {
                Unit carrier = allyDeathBuffer[i];
                if (carrier == null) continue;
                if (!reactionCarriers.TryGetValue(carrier, out ReactionState st)) continue;

                int level = st.level;
                float radiusValue = LevelValue(onAllyDeath.radius, level);

                if (!AllyDeathAccepted(onAllyDeath, victim, carrier, carrier.dead, radiusValue)) continue;

                float flat = LevelValue(onAllyDeath.healFlat, level);
                float percent = LevelValue(onAllyDeath.healPercentOfMaxHp, level);
                if (flat > 0f || percent > 0f) HealUnit(carrier, flat, percent);

                healedCarriers++;

                // Хозяин набора — НОСИТЕЛЬ: погибшего к этому моменту уже нет в сетевом реестре,
                // и показ «на жертве» до клиента не дошёл бы (та же причина, что у реакции 2).
                EmitEventPresentation(this, (int)AbilityEventCode.PassiveAllyDeath, onAllyDeath.presentation,
                                      carrier, level, carrier, carrier.transform.position);

                InterflowDebug.Verbose("ГИБЕЛЬ СОЮЗНИКА: " + InterflowDebug.Name(carrier) + " получил +" +
                                       (flat + (percent > 0f ? carrier.maxHealth * percent : 0f)).ToString("0.#") +
                                       " здоровья за смерть " + InterflowDebug.Name(victim));
            }

            allyDeathBuffer.Clear();

            if (healedCarriers == 0) return;

            InterflowDebug.Event("РЕАКЦИЯ «ПОГИБ СОЮЗНИК» у " + InterflowDebug.Name(victim) +
                                 ": сработала у носителей: " + healedCarriers);

            RequestForceSync();
        }
    }
}
