using System;
using UnityEngine;

namespace StrategyCore
{
    // ==== КОНСТРУКТОР ПАССИВКИ: ЗДОРОВЬЕ НОСИТЕЛЯ (правило 22 — партиал по фиче) ====
    // Семья «здоровье носителя и иммунитеты», проект Документы/Механики/Здоровье_и_Иммунитеты_Проект.md §4,
    // решения Artsiom 41–46 от 17.09.2026. В одном партиале лежат три вещи, потому что они считаются
    // ОДНИМ И ТЕМ ЖЕ числом (доля здоровья носителя) в ОДНОМ И ТОМ ЖЕ тике (правило 5):
    //
    //   1) УСЛОВИЕ «блок работает, пока здоровья меньше доли» для блоков 2 и 3 (решение 41 = ветка А3).
    //      Считается ТИКОМ, как условие ауры, а не подпиской на изменение здоровья: подписка тянет
    //      рекурсию (ChangeMaxHP сам дёргает OnHPChange) и несимметричное снятие у блоков 4, 6 и 7.
    //      ПРИНЯТАЯ ЦЕНА: включение и выключение с задержкой до одного тика (0,1 с).
    //
    //   2) БЛОК 10 «характеристики от нехватки здоровья» (решение 44 = ветка Г2). Старый класс
    //      Abilities/RageFromMissingHp.cs НЕ трогается — он остаётся под своими двумя ассетами.
    //
    //   3) ДЛЯЩИЙСЯ ВИЗУАЛ (решение 46 = ветка Е2): состояние-эффектор, который висит, пока условие
    //      выполнено. Держатели лежат в СВОЁМ хранилище блока (healthStates), а не в Carrier,
    //      и это хранилище чистится на старте матча — см. ResetHealth и CompositePassive.Init.
    //
    //   4) СНЯТИЕ ПО КАТЕГОРИИ при включении блока 3 (решение 42 = ветка Б2). Лежит здесь, а не
    //      рядом с выдачей сопротивлений: CompositePassive.Properties.cs уже у порога крупного
    //      файла (правило 22), а сам механизм — часть этой семьи.
    //
    // ВЕСЬ СЧЁТ — СЕРВЕРНЫЙ (правило 6, §4.3 проекта): единственный вход — OnTick конструктора,
    // а он выходит на клиенте первой строкой. Своих подписок на OnHPChange здесь НЕТ намеренно:
    // это прямо отвергнутые решением 41 ветки А1 и А2. Клиент видит результат штатной синхронизацией
    // характеристик и канала статусов, своего решения не принимает.
    //
    // Новых сетевых сообщений семья не заводит: длящийся визуал едет штатным каналом статусов
    // (решения Artsiom 05.08 и 20).

    /// <summary>
    /// Когда блок работает. Общее перечисление для блоков 2, 3 и для вампиризма реакции 5 —
    /// одно правило порога на всю пассивку (правило 5). Сравнение порога делает готовая чистая
    /// функция <see cref="SkillCastConditionsBlock.HpAllowed"/>, своей копии здесь нет (правило 1).
    /// </summary>
    public enum PassiveBlockCondition
    {
        [InspectorName("Всегда")]                      Always,
        [InspectorName("Пока здоровья меньше доли")]   WhileHpBelow
    }

    /// <summary>
    /// Что тик обязан сделать с блоком: ничего, выдать или снять. Отдельным перечислением, а не
    /// парой булевых: «повторная выдача без смены состояния запрещена» — это правило, и оно должно
    /// читаться в одном месте и проверяться тестом.
    /// </summary>
    public enum HealthGateStep
    {
        None,     // состояние не сменилось — не трогать
        Grant,    // условие стало выполнено — выдать блок
        Revoke    // условие перестало выполняться — снять блок
    }

    /// <summary>
    /// 10. Характеристики носителя растут по мере НЕХВАТКИ его здоровья («ярость»).
    /// Пересчитывается тем же тиком, что и условие по здоровью, — отдельной подписки у блока нет.
    /// </summary>
    [Serializable]
    public class PassiveMissingHpStatsBlock
    {
        [Tooltip("Включить блок: чем меньше здоровья у носителя, тем выше его характеристики. " +
                 "Пересчёт идёт штатным тиком 0,1 с — прибавка появляется и уходит с задержкой до одного тика.")]
        public bool enabled;

        [Tooltip("Кривая: доля НЕДОСТАЮЩЕГО здоровья (0..1) → МНОЖИТЕЛЬ урона носителя. " +
                 "1 — без прибавки, 1.5 — урон в полтора раза. Постоянная единица — урон не трогается.")]
        public AnimationCurve damageByMissingHp = AnimationCurve.Constant(0f, 1f, 1f);

        [Tooltip("Кривая: доля НЕДОСТАЮЩЕГО здоровья (0..1) → прибавка брони ЧИСЛОМ. " +
                 "0 — без прибавки, 20 — плюс двадцать брони. Постоянный ноль — броня не трогается.")]
        public AnimationCurve armorByMissingHp = AnimationCurve.Constant(0f, 1f, 0f);

        [Tooltip("Кривая: доля НЕДОСТАЮЩЕГО здоровья (0..1) → прибавка СКОРОСТИ АТАКИ в долях. " +
                 "0 — без прибавки, 0.8 — бьёт на 80 % чаще. Постоянный ноль — скорость атаки не трогается.")]
        public AnimationCurve attackSpeedByMissingHp = AnimationCurve.Constant(0f, 1f, 0f);

        [Tooltip("Шаг квантования доли нехватки: прибавка меняется СТУПЕНЯМИ, по полным шагам. " +
                 "0.1 — пересчёт на каждые полные десять процентов потерянного здоровья. " +
                 "0 — шага нет, кривые читаются плавно.")]
        [Range(0f, 1f)]
        public float missingHpStep;

        [Tooltip("Доля НЕДОСТАЮЩЕГО здоровья, с которой на носителе показывается длящийся визуал. " +
                 "0.7 — видно, когда потеряно больше семидесяти процентов здоровья. " +
                 "0 — порога нет: визуал висит, пока блок включён. Читается от НЕквантованной нехватки: " +
                 "показ не должен зависеть от шага прибавок.")]
        [Range(0f, 1f)]
        public float presentationMissingHpThreshold;

        [Tooltip("Длящийся визуал: состояние, которое висит на носителе, пока нехватка здоровья выше порога. " +
                 "Ассет обязан быть бессрочным, без значка и без геймплея — это показ, а не состояние. " +
                 "Пусто — без визуала.")]
        public Effector visualEffector;
    }

    public partial class CompositePassive
    {
        [Header("Блок 10 — характеристики от нехватки здоровья")]
        public PassiveMissingHpStatsBlock missingHpStats = new PassiveMissingHpStatsBlock();

        // ==================================================== СОСТОЯНИЕ НОСИТЕЛЕЙ ==

        /// <summary>
        /// Пер-юнит состояние семьи. Решение Artsiom 46 (ветка Е2): держатели длящегося визуала
        /// живут в СВОЁМ хранилище блока, а не в <c>Carrier</c>. Плата за это — обязанность чистить
        /// хранилище между матчами: ассет-ScriptableObject переживает выход из Play вместе со всем,
        /// что в нём накопилось. Точка чистки — <see cref="ResetHealth"/>, её зовёт <c>Init()</c>.
        ///
        /// Здесь же лежат УЖЕ ВЫДАННЫЕ прибавки блока 10: снимать надо ровно свой вклад, иначе
        /// характеристики юнита разъедутся навсегда (тот же приём, что у старого RageFromMissingHp).
        /// </summary>
        class HealthState
        {
            public EffectorHolder controlImmunityVisual;   // держатель визуала блока 2
            public EffectorHolder resistancesVisual;       // держатель визуала блока 3
            public EffectorHolder missingHpVisual;         // держатель визуала блока 10

            public float appliedArmor;                     // выданная прибавка брони, числом
            public float appliedAttackSpeed;               // выданная прибавка скорости атаки, долей
            public float appliedDamage;                    // выданная прибавка урона, долей (множитель минус единица)
        }

        readonly UnitStateMap<HealthState> healthStates = new UnitStateMap<HealthState>();

        /// <summary>Порог «значение изменилось»: тот же, что у старого носителя механики.</summary>
        const float StatEpsilon = 0.0001f;

        // ======================================================= ЧИСТЫЕ ФУНКЦИИ ==
        // Вынесены статикой ради тестов режима редактора: обработчик тика из теста недостижим.

        /// <summary>
        /// Работает ли блок при этом здоровье носителя. Условие «Всегда» не смотрит на здоровье вовсе;
        /// условие по здоровью считается ГОТОВОЙ чистой функцией блока 21 условий каста (правило 1):
        /// сравнение строгое, нулевой максимум — отказ, доля ≤ 0 — условия нет.
        /// </summary>
        public static bool BlockActive(PassiveBlockCondition condition, float hpBelow, float health, float maxHealth)
        {
            if (condition != PassiveBlockCondition.WhileHpBelow) return true;

            return SkillCastConditionsBlock.HpAllowed(health, maxHealth, hpBelow);
        }

        /// <summary>
        /// Что делать с блоком на этом тике. Выдача и снятие идут ТОЛЬКО на переходе: повторная
        /// выдача накрутила бы счётчик источников иммунитета к контролю десять раз в секунду,
        /// и снятие после этого не сработало бы никогда.
        /// </summary>
        /// <param name="granted">Блок сейчас выдан носителю.</param>
        /// <param name="conditionMet">Условие по здоровью сейчас выполнено.</param>
        public static HealthGateStep GateStep(bool granted, bool conditionMet)
        {
            if (granted == conditionMet) return HealthGateStep.None;

            return conditionMet ? HealthGateStep.Grant : HealthGateStep.Revoke;
        }

        /// <summary>
        /// Доля НЕДОСТАЮЩЕГО здоровья, 0..1. Нулевой максимум — ноль: доли от нуля не существует,
        /// а считать «потеряно всё» на юните без максимума нельзя (у него и характеристик нет).
        /// </summary>
        public static float MissingHpFraction(float health, float maxHealth)
        {
            if (maxHealth <= 0f) return 0f;

            return Mathf.Clamp01(1f - health / maxHealth);
        }

        /// <summary>
        /// Квантование доли нехватки шагом: округление ВНИЗ по полным шагам (0.37 при шаге 0.1 → 0.3).
        /// Шаг ≤ 0 — квантования нет, кривая читается плавно.
        ///
        /// Добавка перед округлением нужна против двоичной погрешности: 0.2/0.1 в float даёт
        /// 1.9999999, и честный Floor вернул бы одну ступень вместо двух. Добавка задана В ШАГАХ,
        /// поэтому от величины самого шага не зависит.
        /// </summary>
        public static float QuantizeMissingHp(float missing, float step)
        {
            if (step <= 0f) return missing;

            return Mathf.Floor(missing / step + 1e-4f) * step;
        }

        // ======================================================= ЖИЗНЕННЫЙ ЦИКЛ ==

        /// <summary>
        /// Между матчами состояние не переносим (решение Artsiom 46: хранилище держателей ОБЯЗАНО
        /// сбрасываться на старте матча). Зовётся из <c>CompositePassive.Init()</c> — той же строкой,
        /// что и сброс реакций, попаданий и шкалы.
        /// </summary>
        void ResetHealth()
        {
            healthStates.Clear();
        }

        /// <summary>
        /// Открытие умения у юнита: посчитать условие по здоровью ДО выдачи блоков 2 и 3 и решить,
        /// нужен ли носителю тик. Сами блоки выдаются штатными <c>ApplyControlImmunity</c>
        /// и <c>ApplyResistances</c> — они читают посчитанные здесь флаги.
        ///
        /// Длящийся визуал и прибавки блока 10 ЗДЕСЬ НЕ ВЫДАЮТСЯ: они появляются первым же тиком.
        /// Так у них одна точка выдачи вместо двух (правило 5) и нет клиентской ветки — открытие
        /// умения идёт на обоих пирах, а тик только на сервере.
        /// </summary>
        void InitHealthGates(Unit unit, Carrier c)
        {
            healthStates.PruneDead();   // Unit.Die не зовёт Lock — чистим лениво, как onHitStates

            bool ciOn = controlImmunity != null && controlImmunity.enabled;
            bool resOn = resistances != null && resistances.enabled;

            c.controlImmunityGate = ciOn && BlockActive(controlImmunity.carrierCondition, controlImmunity.carrierHpBelow,
                                                       unit.health, unit.maxHealth);

            c.resistancesGate = resOn && BlockActive(resistances.carrierCondition, resistances.carrierHpBelow,
                                                    unit.health, unit.maxHealth);

            c.missingHpStats = missingHpStats != null && missingHpStats.enabled;

            // Тик нужен, только если есть что пересчитывать: условие, которое может смениться,
            // длящийся визуал, который надо навесить, или прибавки блока 10. Пассивка без всего
            // этого не тикает вовсе — как и до семьи.
            c.healthTick = (ciOn && (controlImmunity.carrierCondition == PassiveBlockCondition.WhileHpBelow
                                     || controlImmunity.visualEffector != null))
                           || (resOn && (resistances.carrierCondition == PassiveBlockCondition.WhileHpBelow
                                         || resistances.visualEffector != null))
                           || c.missingHpStats;

            if (InterflowDebug.FullOn)
            {
                if (ciOn && controlImmunity.carrierCondition == PassiveBlockCondition.WhileHpBelow)
                    LogHealthCondition(2, unit, c.controlImmunityGate, controlImmunity.carrierHpBelow, "открытие умения");

                if (resOn && resistances.carrierCondition == PassiveBlockCondition.WhileHpBelow)
                    LogHealthCondition(3, unit, c.resistancesGate, resistances.carrierHpBelow, "открытие умения");
            }
        }

        /// <summary>
        /// Закрытие умения у юнита: снять длящийся визуал по ДЕРЖАТЕЛЮ и вернуть прибавки блока 10.
        /// Зовётся из <c>Lock</c> ДО того, как носитель уйдёт из carriers.
        ///
        /// Снятие блоков 2 и 3 сюда не входит — его делают штатные <c>RemoveControlImmunity</c>
        /// и <c>RemoveResistances</c> той же строкой, что и всегда.
        /// </summary>
        void RemoveHealthState(Unit unit, Carrier c)
        {
            c.controlImmunityGate = false;
            c.resistancesGate = false;
            c.missingHpStats = false;
            c.healthTick = false;

            HealthState st;
            if (!healthStates.TryGet(unit, out st)) return;

            DropVisual(unit, ref st.controlImmunityVisual, 2);
            DropVisual(unit, ref st.resistancesVisual, 3);
            DropVisual(unit, ref st.missingHpVisual, 10);

            RevertMissingHpStats(unit, st);

            healthStates.Remove(unit);
        }

        // ================================================================== ТИК ==

        /// <summary>
        /// Один тик семьи «здоровье носителя». ТОЛЬКО СЕРВЕР: зовётся из <c>OnTick</c>, а тот выходит
        /// на клиенте первой строкой (правило 6, §4.3 проекта).
        ///
        /// Порядок: сперва условия блоков 2 и 3 (они могут выдать или снять сами блоки), потом
        /// прибавки блока 10, потом длящийся визуал — визуал показывает уже случившееся состояние.
        /// </summary>
        void TickHealth(Unit unit, Carrier c)
        {
            TickBlockGate(unit, c, 2);
            TickBlockGate(unit, c, 3);
            TickMissingHpStats(unit, c);
            TickHealthVisuals(unit, c);
        }

        /// <summary>
        /// Пересечение порога здоровья у блока 2 или 3. Выдача и снятие идут ТЕМИ ЖЕ методами,
        /// что зовут открытие и закрытие умения, — второго пути выдачи в проекте нет (правило 5).
        ///
        /// Повторная выдача без смены состояния запрещена: работа делается только на ПЕРЕХОДЕ
        /// (было выдано — стало не выдано и наоборот). Иначе счётчик источников иммунитета к контролю
        /// рос бы десять раз в секунду и снятие уже никогда не сработало бы.
        /// </summary>
        void TickBlockGate(Unit unit, Carrier c, int block)
        {
            bool enabled = block == 2
                ? controlImmunity != null && controlImmunity.enabled
                : resistances != null && resistances.enabled;

            if (!enabled) return;

            PassiveBlockCondition condition = block == 2 ? controlImmunity.carrierCondition : resistances.carrierCondition;
            if (condition != PassiveBlockCondition.WhileHpBelow) return;   // «Всегда» пересчитывать нечего

            float hpBelow = block == 2 ? controlImmunity.carrierHpBelow : resistances.carrierHpBelow;
            bool want = BlockActive(condition, hpBelow, unit.health, unit.maxHealth);
            bool had = block == 2 ? c.controlImmunityGate : c.resistancesGate;

            HealthGateStep step = GateStep(had, want);
            if (step == HealthGateStep.None) return;                       // состояние не сменилось — молчим

            if (block == 2) c.controlImmunityGate = want; else c.resistancesGate = want;

            if (step == HealthGateStep.Grant)
            {
                if (block == 2) ApplyControlImmunity(unit, c); else ApplyResistances(unit, c);
            }
            else
            {
                if (block == 2) RemoveControlImmunity(unit, c); else RemoveResistances(unit, c);
            }

            if (InterflowDebug.FullOn) LogHealthCondition(block, unit, want, hpBelow, "тик");
        }

        /// <summary>
        /// Пересчёт прибавок блока 10. Снимаем СВОЙ прошлый вклад и ставим новый — ровно так,
        /// как это делает старый носитель механики: иначе прибавки копились бы каждым тиком.
        ///
        /// Броня идёт ЧИСЛОМ, скорость атаки и урон — ДОЛЕЙ. Возврат доли — та же операция
        /// с обратным знаком: ядро устроено так, что «+p» и «−p» взаимно обратны
        /// (Units/Unit.Parameters.cs, обе процентные перегрузки Change*).
        ///
        /// Рекурсии по здоровью нет: ни одна из трёх характеристик здоровье не меняет.
        /// </summary>
        void TickMissingHpStats(Unit unit, Carrier c)
        {
            if (!c.missingHpStats) return;

            HealthState st = HealthStateOf(unit);

            float missing = QuantizeMissingHp(MissingHpFraction(unit.health, unit.maxHealth), missingHpStats.missingHpStep);

            float wantArmor = missingHpStats.armorByMissingHp != null ? missingHpStats.armorByMissingHp.Evaluate(missing) : 0f;
            float wantAttackSpeed = missingHpStats.attackSpeedByMissingHp != null ? missingHpStats.attackSpeedByMissingHp.Evaluate(missing) : 0f;

            // Кривая урона даёт МНОЖИТЕЛЬ (1 — без прибавки), а ядро принимает ДОЛЮ прибавки.
            float wantDamage = (missingHpStats.damageByMissingHp != null ? missingHpStats.damageByMissingHp.Evaluate(missing) : 1f) - 1f;

            bool changed = false;

            if (Mathf.Abs(wantArmor - st.appliedArmor) > StatEpsilon)
            {
                unit.ChangeArmor(wantArmor - st.appliedArmor);       // броня аддитивна: хватает дельты
                st.appliedArmor = wantArmor;
                changed = true;
            }

            if (Mathf.Abs(wantAttackSpeed - st.appliedAttackSpeed) > StatEpsilon)
            {
                if (Mathf.Abs(st.appliedAttackSpeed) > StatEpsilon) unit.ChangeAttackSpeed(-st.appliedAttackSpeed, true);
                if (Mathf.Abs(wantAttackSpeed) > StatEpsilon) unit.ChangeAttackSpeed(wantAttackSpeed, true);
                st.appliedAttackSpeed = wantAttackSpeed;
                changed = true;
            }

            if (Mathf.Abs(wantDamage - st.appliedDamage) > StatEpsilon)
            {
                if (Mathf.Abs(st.appliedDamage) > StatEpsilon) unit.ChangeDamage(-st.appliedDamage, true);
                if (Mathf.Abs(wantDamage) > StatEpsilon) unit.ChangeDamage(wantDamage, true);
                st.appliedDamage = wantDamage;
                changed = true;
            }

            // Строка ТОЛЬКО на смену ступени: тик идёт десять раз в секунду у каждого носителя,
            // и строка на каждый тик забила бы лог (то же правило, что у тика ауры).
            if (changed && InterflowDebug.FullOn)
                LogMissingHpStats(unit, missing, st.appliedDamage, st.appliedArmor, st.appliedAttackSpeed);
        }

        /// <summary>Вернуть носителю всё, что блок 10 ему выдал. Идемпотентно: нулевой вклад молчит.</summary>
        void RevertMissingHpStats(Unit unit, HealthState st)
        {
            if (unit == null || st == null) return;

            if (Mathf.Abs(st.appliedArmor) > StatEpsilon) unit.ChangeArmor(-st.appliedArmor);
            if (Mathf.Abs(st.appliedAttackSpeed) > StatEpsilon) unit.ChangeAttackSpeed(-st.appliedAttackSpeed, true);
            if (Mathf.Abs(st.appliedDamage) > StatEpsilon) unit.ChangeDamage(-st.appliedDamage, true);

            st.appliedArmor = 0f;
            st.appliedAttackSpeed = 0f;
            st.appliedDamage = 0f;
        }

        // ================================================ СНЯТИЕ ПО КАТЕГОРИИ ==

        /// <summary>
        /// Снять с носителя уже висящие наложения тех категорий, которым он теперь сопротивляется
        /// ПОЛНОСТЬЮ (решение Artsiom 42 = ветка Б2).
        ///
        /// Условие снятия — ровно то же, по которому приёмник отбил бы новое наложение:
        /// ДЕЙСТВУЮЩАЯ доля носителя от единицы и выше (Units/UnitReceiver.Statuses.cs). Поэтому
        /// читается не число из строки блока, а <c>Effective</c> носителя: слабость от другого
        /// источника перебивает сопротивление, и тогда состояние этой категории на юнита проходит —
        /// снимать его было бы враньём.
        ///
        /// ПРИНЯТАЯ ЦЕНА (карта развилок §3.2): снимается ЛЮБОЕ наложение категории, включая
        /// союзное. У замедлений бафов сегодня нет, но категория числовая и завтра может нести
        /// ускорение. Правила слипания и стакинга (UnitReceiver.Statuses.cs) при этом не трогаются.
        /// </summary>
        void DispelFullyResisted(Unit unit, UnitResistances holder)
        {
            if (unit == null || holder == null || resistances.entries == null) return;

            for (int i = 0; i < resistances.entries.Length; i++)
            {
                ResistanceEntry entry = resistances.entries[i];
                if (entry == null || entry.category == EffectorCategory.None) continue;

                if (entry.value < 1f) continue;                        // эта строка сопротивляется не полностью
                if (holder.Effective(entry.category) < 1f) continue;   // слабость другого источника перебила

                // Повтор категории в списке безвреден: со второго раза снимать уже нечего.
                int removed = Effector.EffectorRemoveByCategory(unit, entry.category);

                if (removed > 0 && InterflowDebug.FullOn) LogResistanceDispel(unit, entry.category, removed);
            }
        }

        // ====================================================== ДЛЯЩИЙСЯ ВИЗУАЛ ==

        /// <summary>
        /// Длящийся визуал трёх хозяев: блоков 2, 3 и 10 (решения Artsiom 20 и 46).
        /// Пара «наложить — снять по держателю» взята с визуала щита (CompositeSkill.ApplyShield.cs):
        /// накладываем штатным <c>Effector.EffectorAdd</c> и запоминаем ОТДАННЫЙ им держатель,
        /// снимаем штатным <c>Effector.EffectorRemove</c> ровно по нему.
        ///
        /// Искать своё наложение в общем списке юнита по ссылке на ассет нельзя: там лежат наложения
        /// всех источников и обеих команд, и снялось бы чужое.
        /// </summary>
        void TickHealthVisuals(Unit unit, Carrier c)
        {
            bool anyVisual = (controlImmunity != null && controlImmunity.enabled && controlImmunity.visualEffector != null)
                             || (resistances != null && resistances.enabled && resistances.visualEffector != null)
                             || (c.missingHpStats && missingHpStats.visualEffector != null);

            if (!anyVisual) return;

            HealthState st = HealthStateOf(unit);

            if (controlImmunity != null && controlImmunity.enabled)
                SyncVisual(unit, ref st.controlImmunityVisual, controlImmunity.visualEffector, c.controlImmunityGate, 2);

            if (resistances != null && resistances.enabled)
                SyncVisual(unit, ref st.resistancesVisual, resistances.visualEffector, c.resistancesGate, 3);

            if (c.missingHpStats)
            {
                // Показ читается от НЕквантованной нехватки: порог показа — про картинку, а не про
                // ступени прибавок, и не должен ездить вслед за шагом квантования.
                float missing = MissingHpFraction(unit.health, unit.maxHealth);
                bool want = missing >= missingHpStats.presentationMissingHpThreshold;

                SyncVisual(unit, ref st.missingHpVisual, missingHpStats.visualEffector, want, 10);
            }
        }

        /// <summary>Привести визуал к нужному состоянию. Работа делается только на ПЕРЕХОДЕ.</summary>
        void SyncVisual(Unit unit, ref EffectorHolder holder, Effector visual, bool want, int block)
        {
            if (visual == null)
            {
                DropVisual(unit, ref holder, block);   // ассет убрали из блока на ходу — снимаем висящее
                return;
            }

            if (want)
            {
                if (holder != null) return;            // уже висит — повторно не накладываем

                // Держатель нулевой — наложения не было вовсе (труп, здание, отказ приёмника):
                // тогда и снимать потом нечего, попробуем следующим тиком.
                holder = Effector.EffectorAdd(unit, visual, null, unit.owner);

                if (holder != null && InterflowDebug.FullOn) LogHealthVisual(block, unit, true, visual);
                return;
            }

            DropVisual(unit, ref holder, block);
        }

        /// <summary>Снять висящий визуал по держателю. Держателя нет — снимать нечего.</summary>
        void DropVisual(Unit unit, ref EffectorHolder holder, int block)
        {
            if (holder == null) return;

            Effector visual = holder.effector;
            if (unit != null) Effector.EffectorRemove(unit, holder);
            holder = null;

            if (InterflowDebug.FullOn) LogHealthVisual(block, unit, false, visual);
        }

        HealthState HealthStateOf(Unit unit)
        {
            HealthState st;
            if (healthStates.TryGet(unit, out st)) return st;

            st = new HealthState();
            healthStates.Set(unit, st);
            return st;
        }
    }
}
