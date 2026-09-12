using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StrategyCore
{
    /// <summary>
    /// [Interflow 2026-08-06] ЕДИНЫЙ клиентский презентер умений. Решение Artsiom: событийная инверсия.
    ///
    /// Единственная точка, где факты презентации превращаются в визуал. Подписан на SkillPresentationEvents;
    /// ЧТО именно рисовать, берёт из ассета умения по abilityID. Разделение жёсткое:
    /// сервер не знает про визуал, презентер не знает про геймплей (правило 6).
    ///
    /// Живёт в сборке Interflow.Client (defineConstraints: !UNITY_SERVER) — в билде выделенного сервера
    /// его нет вовсе, поэтому сетевой слой на него не ссылается и не может сослаться.
    /// Создаёт себя сам при старте приложения (как PresentationBridge регистрирует сервисы):
    /// ни сцену, ни префаб GameManager править не нужно.
    /// </summary>
    public partial class SkillPresenter : MonoBehaviour
    {
        public static SkillPresenter Instance { get; private set; }

        // Настройки презентации. Ассета может не быть — тогда работаем на значениях по умолчанию
        // (недоступен окажется только сектор конуса, ему нужен материал).
        SkillPresentationSettings settings;
        SkillPresentationSettings Settings => settings != null
            ? settings
            : settings = Resources.Load<SkillPresentationSettings>(SkillPresentationSettings.ResourcePath)
                      ?? ScriptableObject.CreateInstance<SkillPresentationSettings>();

        // Клиентские копии зон на земле: id зоны → объект. Геймплея в них нет: GroundDamageZone.Start
        // сам выходит на клиенте (isClient), тик урона не подписывается — остаётся чистый визуал.
        readonly Dictionary<int, GameObject> zoneVisuals = new Dictionary<int, GameObject>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Create()
        {
            if (Utils.Headless) return;      // показывать некому
            if (Instance != null) return;    // уничтоженный объект Unity сравнивается с null как null — пересоздастся

            GameObject go = new GameObject("SkillPresenter");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SkillPresenter>();
        }

        void OnEnable()
        {
            SkillPresentationEvents.SkillFired += HandleSkillFired;
            SkillPresentationEvents.UnitReady += HandleUnitReady;
            SkillPresentationEvents.UnitGone += HandleUnitGone;
            SkillPresentationEvents.CastStarted += HandleCastStarted;
            SkillPresentationEvents.CastStopped += HandleCastStopped;
            SkillPresentationEvents.ZoneSpawned += HandleZoneSpawned;
            SkillPresentationEvents.ZoneDespawned += HandleZoneDespawned;
            SkillPresentationEvents.ShieldChanged += HandleShieldChanged;
            SkillPresentationEvents.BattleFact += HandleBattleFact;
            SkillPresentationEvents.BattleFactAt += HandleBattleFactAt;
            SkillPresentationEvents.DamageStep += HandleDamageStep;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        void OnDisable()
        {
            SkillPresentationEvents.SkillFired -= HandleSkillFired;
            SkillPresentationEvents.UnitReady -= HandleUnitReady;
            SkillPresentationEvents.UnitGone -= HandleUnitGone;
            SkillPresentationEvents.CastStarted -= HandleCastStarted;
            SkillPresentationEvents.CastStopped -= HandleCastStopped;
            SkillPresentationEvents.ZoneSpawned -= HandleZoneSpawned;
            SkillPresentationEvents.ZoneDespawned -= HandleZoneDespawned;
            SkillPresentationEvents.ShieldChanged -= HandleShieldChanged;
            SkillPresentationEvents.BattleFact -= HandleBattleFact;
            SkillPresentationEvents.BattleFactAt -= HandleBattleFactAt;
            SkillPresentationEvents.DamageStep -= HandleDamageStep;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        /// <summary>Поглощающий щит юнита изменился — серый сегмент на полоске здоровья рисует ShieldBarDisplay.</summary>
        static void HandleShieldChanged(Unit unit, float amount) => ShieldBarDisplay.LocalShow(unit, amount);

        // ============================== РАЗОВЫЕ НАДПИСИ В БОЮ ==============================
        // §15 схемы, решения Artsiom Р1–Р7 от 07.09.2026. Сервер прислал ФАКТ и ПРИЧИНУ, слова и цвета
        // берём здесь — боевой код про них не знает (решение Р6). Форма — готовый всплывающий текст (Р2),
        // видно всем, кто видит юнита (Р3): отсев делают собственные гейты текста (headless, кадр, туман).
        // [Interflow 2026-09-09 passive-facts] Этим же путём идут срабатывания пассивных умений,
        // и та же строка уходит в ленту на экране (BattleFactFeed).

        /// <summary>
        /// По юниту произошло разовое событие боя — пишем надпись над ним.
        /// <c>sync: false</c> ОБЯЗАТЕЛЕН: факт уже разослан сервером своим сообщением, а при <c>sync: true</c>
        /// сервер ретранслировал бы текст второй раз (<c>Core/FloatingText.cs</c>), и клиент увидел бы две надписи.
        /// </summary>
        // [Interflow 2026-09-10] Скобка «было → стало» идёт ТОЛЬКО в ленту. Над юнитом надписи
        // живут по несколько штук в кадре и разведены полуметром — со скобкой они налезают друг
        // на друга и не читаются ни одна. В ленте места хватает, и разбирают баланс именно по ней.
        void HandleBattleFact(BattleFactInfo info)
        {
            Unit unit = info.unit;
            if (unit == null) return;

            string text = FactText(info.reason, info.value, info.before, info.after, false);   // над юнитом — коротко
            if (string.IsNullOrEmpty(text)) return;

            Color color = FactColor(info.reason);

            // Разводим надписи по высоте (решение Artsiom 07.09.2026). За один кадр по одному юниту их
            // может быть несколько: число урона и «Щит» при частичном поглощении, число и отказ наложения
            // состояния атаки, два числа от кирпичей «каждая N-я атака» и «оглушающий удар». Всплывающий
            // текст стопку не умеет — без смещения надписи легли бы друг на друга. Образец — сбор ресурсов
            // (ResourceUnit: i * 0.5f). Счётчик живёт ровно один кадр, ссылки на юнитов в нём не копятся.
            if (factsFrame != Time.frameCount)
            {
                factsFrame = Time.frameCount;
                factsThisFrame.Clear();
            }
            factsThisFrame.TryGetValue(unit, out int shown);
            factsThisFrame[unit] = shown + 1;

            Vector3 above = unit.transform.position
                          + new Vector3(0f, unit.unitHeight + shown * Settings.factStackStep, 0f);
            FloatingText.Spawn(-1, above, text, color, false);

            FeedFact(in info, color);
        }

        /// <summary>
        /// [Interflow 2026-09-09 passive-facts] Разовое событие боя в ТОЧКЕ — носителя уже нет
        /// (реакция «носитель погиб»). Стопка по кадру здесь не нужна: ключом стопки служит юнит,
        /// а его нет, и двух надписей в одной точке за кадр этот путь не порождает.
        /// </summary>
        void HandleBattleFactAt(BattleFactInfo info)
        {
            string text = FactText(info.reason, info.value, info.before, info.after, false);   // над юнитом — коротко
            if (string.IsNullOrEmpty(text)) return;

            Color color = FactColor(info.reason);

            // Подъём над точкой: у юнита для этого есть своя высота, у точки её взять неоткуда —
            // берём тот же шаг разведения надписей, чтобы не заводить ещё одну настройку.
            Vector3 above = info.position + new Vector3(0f, Mathf.Max(1f, Settings.factStackStep), 0f);
            FloatingText.Spawn(-1, above, text, color, false);

            FeedFact(in info, color);
        }

        // ============================== ЛЕНТА: ГРУППА ПО УДАРУ ==============================
        // [Interflow 2026-09-11] Решение Artsiom: лента показывает удар группой — заголовок «кто, чем,
        // по кому, на сколько, здоровье было → стало из максимума» и детали расчёта отступом. Строки
        // одного удара лента узнаёт по номеру, который выдаёт приёмник; слова и цвета по-прежнему
        // выбираются здесь, а не в боевом коде (решение Р6).

        /// <summary>Как подписан источник, у которого нет ни юнита, ни умения: факт в точке гибели.</summary>
        const string PointName = "(точка)";

        /// <summary>
        /// Отдать факт в ленту. ИСХОД удара («мимо», «неуязвим», отказ наложения) становится заголовком
        /// группы: разбора записи у такого удара не будет, и без него группа осталась бы безымянной.
        /// Факт вне удара (номер 0) — тоже заголовок: это отдельная строка сама по себе.
        /// Остальное ложится деталью под заголовок.
        ///
        /// Число урона в ленту не идёт: его печатает заголовок разбора записи, и вторая строка с тем же
        /// числом только занимала бы место. Надпись над юнитом при этом остаётся прежней.
        /// </summary>
        void FeedFact(in BattleFactInfo info, Color color)
        {
            if (info.reason == BattleFactReason.DamageDealt) return;

            string line = FactText(info.reason, info.value, info.before, info.after, true);
            if (string.IsNullOrEmpty(line)) return;

            if (IsHitOutcome(info.reason) || info.hitId == 0)
            {
                string target = info.unit != null ? ShortName(info.unit) : PointName;
                BattleFactFeed.PushHeader(info.hitId, Head(info.attacker, info.abilityID, target) + "   " + line, color);
                return;
            }

            BattleFactFeed.PushDetail(info.hitId, line, color);
        }

        /// <summary>
        /// Разобрана запись урона — заголовок группы и две ступени расчёта под ним. Остальные ступени
        /// (снижение, уязвимость, поглощение щитом) приезжают своими фактами и ложатся в ту же группу.
        /// Свёрнутая периодика (число тиков больше нуля) ступеней не несёт — только заголовок.
        /// </summary>
        void HandleDamageStep(DamageStepInfo info)
        {
            if (info.victim == null) return;

            SkillPresentationSettings s = Settings;

            string head = Head(info.attacker, info.abilityID, ShortName(info.victim))
                        + "   −" + Num(info.dealt);

            if (info.ticks > 0) head += " " + s.feedTicksText + " " + info.ticks + " " + s.feedTicksSuffixText;

            // У свёртки скобки «было → стало» нет: между первым и последним тиком жертву мог бить
            // кто угодно ещё и лечить союзник, и пара чисел не сошлась бы с числом свёртки.
            head += "   " + (info.ticks > 0 ? string.Empty : Num(info.healthBefore) + " → ")
                  + Num(info.healthAfter) + " " + s.feedHealthOfText + " " + Num(info.healthMax);

            if (info.died) head += "   " + s.feedDiedText;

            BattleFactFeed.PushHeader(info.hitId, head, s.damageDealtColor);

            if (info.ticks > 0) return;

            // Заявленное число. У умения записей в пакете бывает несколько — подписываем, какая это.
            string declared = s.feedDeclaredText + " " + Num(info.declared)
                            + (info.recordCount > 1 ? " (" + (info.recordIndex + 1) + "/" + info.recordCount + ")" : "");
            BattleFactFeed.PushDetail(info.hitId, declared, s.damageDealtColor);

            // Броня и таблица «тип брони × тип урона» — одна ступень: отдельного числа брони в факте нет,
            // видна её работа «сколько было → сколько стало».
            if (!Mathf.Approximately(info.afterCallbacks, info.afterArmor))
                BattleFactFeed.PushDetail(info.hitId,
                                          s.feedArmorText + " " + Num(info.afterCallbacks) + " → " + Num(info.afterArmor),
                                          s.damageDealtColor);
        }

        /// <summary>Исход удара: такой факт заменяет собой заголовок группы — числа урона не будет.</summary>
        static bool IsHitOutcome(BattleFactReason reason)
        {
            return reason == BattleFactReason.HitMissed
                || reason == BattleFactReason.Invulnerable
                || reason == BattleFactReason.StatusImmune
                || reason == BattleFactReason.StatusResisted;
        }

        /// <summary>
        /// Левая часть заголовка: «кто [чем] → по кому». Источника может не быть вовсе (факт вне удара,
        /// зона на земле) — тогда в заголовке остаётся одна цель.
        /// </summary>
        string Head(Unit attacker, int abilityID, string target)
        {
            string source = attacker != null ? ShortName(attacker) : string.Empty;

            if (abilityID >= 0)
                source = (source.Length > 0 ? source + " " : string.Empty) + "[" + AbilityDisplayName(abilityID) + "]";

            return source.Length > 0 ? source + " " + Settings.feedArrowText + " " + target : target;
        }

        /// <summary>
        /// Короткое имя юнита для ленты: название плюс сетевой номер. Полное имя из логов
        /// (InterflowDebug.Name) сюда не годится — в заголовке их два, и строка уезжает за край.
        /// </summary>
        static string ShortName(Unit unit)
        {
            if (unit == null) return "?";

            string name = string.IsNullOrEmpty(unit.unitName) ? unit.name : unit.unitName;
            return name + "#" + unit.netID;
        }

        /// <summary>Число для ленты: целые без хвоста, дробные с одним знаком.</summary>
        static string Num(float value) => value.ToString("0.#");

        // Сколько надписей уже показано над юнитом в текущем кадре. Чистится сменой кадра.
        readonly Dictionary<Unit, int> factsThisFrame = new Dictionary<Unit, int>();
        int factsFrame = -1;

        /// <summary>
        /// Строка надписи по причине. Ассета настроек может не быть — тогда Settings отдаёт экземпляр
        /// со значениями по умолчанию, и надписи всё равно показываются (§5.5 промта).
        /// Число урона выводится ЧИСЛОМ без слов, целым: формат полем не выносится (решение Artsiom
        /// 07.09.2026) — свободная строка формата из Inspector при опечатке роняет расчёт урона
        /// исключением прямо посреди приёмника. У причин пассивных умений число приписывается тем же
        /// способом — склейкой, а не форматом, и по той же причине.
        /// </summary>
        string FactText(BattleFactReason reason, float value, float before, float after, bool withPair)
        {
            SkillPresentationSettings s = Settings;

            switch (reason)
            {
                case BattleFactReason.HitMissed: return s.hitMissedText;
                case BattleFactReason.ShieldAbsorbed: return s.shieldAbsorbedText + (withPair ? Pair(before, after) : "");
                case BattleFactReason.Invulnerable: return s.invulnerableText;
                case BattleFactReason.StatusImmune: return s.statusImmuneText;
                case BattleFactReason.StatusResisted: return s.statusResistedText;
                // Минимум «1» (решение Artsiom 07.09.2026): урон меньше половины единицы округлился бы
                // в «0», а ноль показывать нельзя — здоровье-то снялось. Сам факт до сюда доходит только
                // при снятом здоровье, гейт стоит в приёмнике (UnitReceiver: dealt > 0).
                case BattleFactReason.DamageDealt: return Mathf.Max(1, Mathf.RoundToInt(value)).ToString();

                // ===== Пассивные умения (2026-09-09) =====
                // Число блока — порядковый номер заголовка в ассете умения; слово берётся из настроек.
                case BattleFactReason.PassiveBlockGranted:
                    return s.passiveBlockGrantedPrefix + s.PassiveBlockName(Mathf.RoundToInt(value));
                case BattleFactReason.PassiveBlockRevoked:
                    return s.passiveBlockRevokedPrefix + s.PassiveBlockName(Mathf.RoundToInt(value));

                // Ноль не приписываем: «Ответ 0» читается как ошибка, хотя ответ мог быть чисто
                // состояниями, без урона, а клич — без задетых союзников.
                case BattleFactReason.PassiveCounter:
                    return WithCount(s.passiveCounterText, value);
                case BattleFactReason.PassiveCounterOnEvade:
                    return WithCount(s.passiveCounterOnEvadeText, value);
                case BattleFactReason.PassiveDeathBurst:
                    return WithCount(s.passiveDeathBurstText, value);
                case BattleFactReason.PassiveOnKill:
                    return WithCount(s.passiveOnKillText, value);
                case BattleFactReason.PassiveHpBelow:
                    return s.passiveHpBelowText + " " + Mathf.RoundToInt(value) + "%";
                case BattleFactReason.IncomingDamageReduced:
                    return WithCount(s.incomingDamageReducedText, value) + (withPair ? Pair(before, after) : "");

                // ===== Реакция 5 и пассивки, собранные кодом (2026-09-10) =====
                // Номер кирпича — как у блоков свойств: слово живёт в настройках.
                case BattleFactReason.PassiveOnHitBrick:
                    return s.passiveOnHitPrefix + s.PassiveOnHitBrickName(Mathf.RoundToInt(value));

                // Здесь число факта — id УМЕНИЯ, а не номер чего-либо: слово берётся из самого
                // ассета умения (правило 3 — слова не дублируются в настройках двадцать раз).
                case BattleFactReason.PassiveProc:
                    return s.passiveProcPrefix + AbilityDisplayName(Mathf.RoundToInt(value));
                case BattleFactReason.PassiveGranted:
                    return s.passiveGrantedPrefix + AbilityDisplayName(Mathf.RoundToInt(value));
                case BattleFactReason.PassiveRevoked:
                    return s.passiveRevokedPrefix + AbilityDisplayName(Mathf.RoundToInt(value));

                // ===== Числа боя и состояния (2026-09-10) =====
                case BattleFactReason.OutgoingDamageIncreased:
                    return WithCount(s.outgoingDamageIncreasedText, value) + (withPair ? Pair(before, after) : "");
                case BattleFactReason.IncomingDamageIncreased:
                    return WithCount(s.incomingDamageIncreasedText, value) + (withPair ? Pair(before, after) : "");
                case BattleFactReason.StatusApplied:
                    return s.statusAppliedPrefix + StatusDisplayName(Mathf.RoundToInt(value)) + (withPair ? Pair(before, after) : "");

                // Щит: число — объём, скобка показывает, был ли щит до этого и на сколько вырос.
                case BattleFactReason.ShieldGranted:
                    return s.shieldGrantedText + " " + Mathf.RoundToInt(value) + (withPair ? Pair(before, after) : "");
            }

            // Номер причины, которого у этого пира нет: сервер новее клиента. Молчать нельзя (правило 9) —
            // так же кричит канал статусов, когда у клиента нет присланного состояния.
            Debug.LogError("Desync! Неизвестная причина факта боя: " + (int)reason +
                           " (HandleBattleFact SkillPresenter)");
            return null;
        }
        /// <summary>
        /// [Interflow 2026-09-10] Скобка «было → стало». По одному числу баланс не проверишь:
        /// «Снижен 6» не говорит, из скольки, а «−40 % скорости атаки» — какой она была.
        /// Оба нуля означают «пары нет» — скобку не печатаем: у части причин второго числа
        /// не существует (ответный удар, номер блока свойств).
        /// Два знака после запятой: урон обычно целый, а промежуток между ударами — дробный.
        /// </summary>
        static string Pair(float before, float after)
        {
            if (before == 0f && after == 0f) return "";

            return " (" + before.ToString("0.##") + " \u2192 " + after.ToString("0.##") + ")";
        }


        /// <summary>Слово плюс число, если число есть. Ноль и меньше — только слово.</summary>
        static string WithCount(string text, float value)
        {
            int n = Mathf.RoundToInt(value);
            return n > 0 ? text + " " + n : text;
        }

        /// <summary>Цвет надписи по причине. Неизвестная причина сюда не доходит — строка уже отсеяна.</summary>
        Color FactColor(BattleFactReason reason)
        {
            SkillPresentationSettings s = Settings;

            switch (reason)
            {
                case BattleFactReason.HitMissed: return s.hitMissedColor;
                case BattleFactReason.ShieldAbsorbed: return s.shieldAbsorbedColor;
                case BattleFactReason.Invulnerable: return s.invulnerableColor;
                case BattleFactReason.StatusImmune: return s.statusImmuneColor;
                case BattleFactReason.StatusResisted: return s.statusResistedColor;

                case BattleFactReason.PassiveBlockGranted: return s.passiveBlockGrantedColor;
                case BattleFactReason.PassiveBlockRevoked: return s.passiveBlockRevokedColor;
                case BattleFactReason.PassiveCounter: return s.passiveCounterColor;
                case BattleFactReason.PassiveCounterOnEvade: return s.passiveCounterOnEvadeColor;
                case BattleFactReason.PassiveDeathBurst: return s.passiveDeathBurstColor;
                case BattleFactReason.PassiveOnKill: return s.passiveOnKillColor;
                case BattleFactReason.PassiveHpBelow: return s.passiveHpBelowColor;
                case BattleFactReason.IncomingDamageReduced: return s.incomingDamageReducedColor;

                case BattleFactReason.PassiveOnHitBrick: return s.passiveOnHitColor;
                case BattleFactReason.PassiveProc: return s.passiveProcColor;
                case BattleFactReason.PassiveGranted: return s.passiveGrantedColor;
                case BattleFactReason.PassiveRevoked: return s.passiveRevokedColor;

                case BattleFactReason.OutgoingDamageIncreased: return s.outgoingDamageIncreasedColor;
                case BattleFactReason.IncomingDamageIncreased: return s.incomingDamageIncreasedColor;
                case BattleFactReason.StatusApplied: return s.statusAppliedColor;
                case BattleFactReason.ShieldGranted: return s.shieldGrantedColor;

                default: return s.damageDealtColor;
            }
        }

        /// <summary>Настройки ленты: она создаёт себя отдельно и своего ассета не грузит (правило 5).</summary>
        public SkillPresentationSettings FeedSettings => Settings;

        // Сам презентер переживает смену сцены, а визуалы — нет: они уничтожаются вместе со сценой.
        // Без обнуления реестра там копились бы мёртвые ссылки, а id зон нового матча совпадали бы со старыми.
        void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            zoneVisuals.Clear();
            ClearAreas();
        }

        // Таймеры областей и гейт тумана войны — покадрово (см. SkillPresenter.Areas.cs).
        void Update() => UpdateAreas();

        // ============================== СРАБАТЫВАНИЕ УМЕНИЯ ==============================

        /// <summary>
        /// Умение сработало — проигрываем всё, что описано в его ассете. Гейты тумана войны те же,
        /// что были в CompositeSkill.Execute до переноса: у хелперов VFX они внутри, у звуков — здесь.
        /// </summary>
        void HandleSkillFired(Unit caster, int abilityID, int level, Unit aimUnit, Vector3 aimPoint)
        {
            CompositeSkill skill = ResolveSkill(abilityID);
            if (skill == null) return;

            PlayProcAnimation(caster, skill.procAnimationState);

            // Визуал и звук замаха — из точки привязки на модели кастера.
            InterflowAbility.PlaySocketVFX(caster, skill.spawnSocket, skill.localOffset, skill.castVFX, skill.castVfxLifetime);
            if (caster != null && InterflowAbility.VisibleForLocalViewer(caster.transform.position))
                InterflowAbility.PlaySound(skill.castSound, caster.transform, skill.soundVolume);

            // Визуал и звук попадания — в точке приложения (она же центр области).
            InterflowAbility.PlayPointVFX(aimPoint, skill.impactVFX, skill.impactVfxLifetime);
            if (InterflowAbility.VisibleForLocalViewer(aimPoint))
                InterflowAbility.PlaySound(skill.impactSound, aimUnit != null ? aimUnit.transform : null, skill.soundVolume);

            SpawnVisualProjectile(skill, caster, level, aimUnit);

            // Область действия: та, что висела на замахе, доживает свой срок; у «умного выбора точки»
            // область появляется только сейчас — раньше точки не существовало.
            AreaOnSkillFired(skill, caster, level, aimPoint);
        }

        /// <summary>
        /// Визуальная копия снаряда для ЧИСТОГО клиента. Настоящий снаряд несёт урон, поэтому живёт
        /// только на сервере (правило 6) — на хосте он уже летит, копия там дала бы два снаряда.
        /// До этого у кастов «умный выбор» с кнопки клиент не видел полёта вовсе: цель выбирает сервер,
        /// и клиент выходил из Use раньше. Теперь цель приезжает фактом.
        /// </summary>
        static void SpawnVisualProjectile(CompositeSkill skill, Unit caster, int level, Unit aimUnit)
        {
            if (!NetworkConnectionHandler.isClient) return;

            // Те же условия, при которых снаряд создаёт сервер: иначе копия появилась бы там, где настоящего нет.
            if (skill.delivery != SkillDelivery.Projectile || skill.projectilePrefab == null) return;
            if (caster == null || aimUnit == null || !skill.projectileFollowsTarget) return;

            Transform socket = InterflowAbility.ResolveSocket(caster, skill.spawnSocket);
            Vector3 spawnPos = InterflowAbility.SocketPosition(caster, skill.spawnSocket, skill.localOffset);
            Quaternion spawnRot = socket != null ? socket.rotation : Quaternion.identity;

            // Урон и оглушение — нули: состояние мира клиент не считает. Тип урона всё же передаём —
            // штатный снаряд по прилёте обращается к нему, и без типа расчёт брони упал бы.
            Projectile visual = Projectile.Spawn(caster.owner, caster, skill.projectilePrefab, spawnPos, spawnRot,
                                                 aimUnit, false, 0f, skill.ProjectileDamageType(level), true);
            if (visual == null) return;

            visual.stunTime = 0f;

            // Штатный спавн подставляет снаряду эффекторы АВТОАТАКИ кастера. Живому кастеру они не
            // применяются, но если он погибнет в полёте, снаряд наложил бы их сам — на клиенте, мимо сервера.
            visual.attackEffectors = null;
        }

        // Анимация срабатывания. Невидимого в тумане не анимируем — паттерн ядра (Unit.State: cast, Unit.Combat: hit).
        // CrossFade перебивает текущую анимацию, включая замах атаки, — принятая цена (План §3).
        static void PlayProcAnimation(Unit unit, string state)
        {
            if (unit == null || unit.dead || string.IsNullOrEmpty(state)) return;
            if (!unit.FoWVisible || unit.animator == null) return;

            unit.animator.CrossFade(state, unit.crossFadeTime, 0, 0f);
        }

        // ============================== ЗОНЫ НА ЗЕМЛЕ ==============================

        /// <param name="level">Уровень умения. Для зоны сейчас не используется (префаб от уровня не зависит),
        /// но едет в событии вместе с остальными фактами — как у прочих сообщений канала.</param>
        /// <param name="carrier">Носитель зоны («аура на время», блок Б7). Не null — копия ставится в КЛИЕНТСКУЮ позицию
        /// носителя и вешается на его transform, дальше едет с ним сама; мировой масштаб сохраняется (SetParent с
        /// worldPositionStays). Серверная `position` для неё не годится: клиентская позиция юнита отстаёт на интерполяцию,
        /// и разница осела бы постоянным смещением копии на весь срок зоны (замечание ревью Б7).
        /// Погиб носитель — сервер гасит зону обычным деспавном; исчез его объект раньше — копия ушла вместе с ним.</param>
        void HandleZoneSpawned(int zoneId, int abilityID, int level, Vector3 position, Unit carrier)
        {
            if (zoneVisuals.ContainsKey(zoneId)) return;   // повтор (в т.ч. досыл при подключении) — не дублируем

            GameObject prefab = ResolveZonePrefab(abilityID);
            if (prefab == null) return;

            Vector3 at = carrier != null ? carrier.transform.position : position;
            GameObject visual = Instantiate(prefab, at, Quaternion.identity);
            if (carrier != null) visual.transform.SetParent(carrier.transform, true);

            zoneVisuals[zoneId] = visual;
        }

        void HandleZoneDespawned(int zoneId)
        {
            if (!zoneVisuals.TryGetValue(zoneId, out GameObject go)) return;
            if (go != null) Destroy(go);
            zoneVisuals.Remove(zoneId);
        }

        // ============================== ОБЩЕЕ ==============================

        // Описание визуала по сети не гоняем: оно уже лежит в ассете умения, одинаковом на всех пирах.
        // Умения не-конструкторы (кодовые пассивки) презентации пока не описывают — для них null.
        /// <summary>
        /// Отображаемое название умения по его id. Нужно надписям пассивок, собранных кодом:
        /// канал факта везёт одно число, поэтому по сети едет id, а слово клиент берёт из ассета
        /// умения — того же самого на всех пирах. Русское имя, иначе имя ассета.
        /// Умения нет в справочнике (сервер новее клиента) — печатаем номер, но молчать нельзя.
        /// </summary>
        /// <summary>
        /// Отображаемое название состояния по его id. Как и у умений, по сети едет число, а слово
        /// клиент берёт из ассета состояния — одного и того же на всех пирах.
        /// </summary>
        static string StatusDisplayName(int effectorID)
        {
            Effector e = Effector.GetEffectorByID(effectorID);
            if (e == null)
            {
                Debug.LogError("Desync! Факт боя про неизвестное состояние id=" + effectorID +
                               " (StatusDisplayName SkillPresenter)");
                return "Состояние " + effectorID;
            }

            return string.IsNullOrEmpty(e.displayName) ? e.name : e.displayName;
        }

        static string AbilityDisplayName(int abilityID)
        {
            if (GameManager.Instance == null) return "Умение " + abilityID;

            if (!GameManager.Instance.gameAbilities.TryGetValue(abilityID, out Ability ability) || ability == null)
            {
                Debug.LogError("Desync! Факт боя про неизвестное умение id=" + abilityID +
                               " (AbilityDisplayName SkillPresenter)");
                return "Умение " + abilityID;
            }

            return (ability.abilityName != null && ability.abilityName.Length > 0 &&
                    !string.IsNullOrEmpty(ability.abilityName[0]))
                   ? ability.abilityName[0]
                   : ability.name;
        }

        static CompositeSkill ResolveSkill(int abilityID)
        {
            if (GameManager.Instance == null) return null;
            if (!GameManager.Instance.gameAbilities.TryGetValue(abilityID, out Ability ability)) return null;

            return ability as CompositeSkill;
        }

        static GameObject ResolveZonePrefab(int abilityID)
        {
            CompositeSkill skill = ResolveSkill(abilityID);
            if (skill == null || skill.groundZone == null || !skill.groundZone.enabled) return null;

            return skill.groundZone.zonePrefab;
        }
    }
}
