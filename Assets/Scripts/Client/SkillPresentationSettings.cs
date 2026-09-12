using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// [Interflow 2026-08-06] Настройки клиентской презентации умений. Отдельный ассет, потому что
    /// презентер создаёт себя в рантайме и объекта в сцене, где можно было бы заполнить поля, у него нет.
    /// Прецедент — справочник иконок статусов (`Resources/Catalogs/StatusIconCatalog.asset`).
    ///
    /// Ассет НЕОБЯЗАТЕЛЕН: без него презентер работает на значениях по умолчанию, недоступен окажется
    /// только сектор конуса (для него нужен материал, а выдумывать его в коде нельзя — правило 3).
    /// </summary>
    [CreateAssetMenu(fileName = "SkillPresentationSettings",
                     menuName = "StrategyCore/Interflow/Настройки презентации умений")]
    public class SkillPresentationSettings : ScriptableObject
    {
        /// <summary>Путь внутри Resources, по которому презентер ищет ассет.</summary>
        public const string ResourcePath = "Catalogs/SkillPresentationSettings";

        [Header("Область действия")]
        [Tooltip("Материал сектора конуса. Прозрачный Unlit, как у шкалы статусов. " +
                 "Круглые области его не используют — они рисуются штатной декалью-проектором. " +
                 "Пусто — конусы не показываются (в консоль уйдёт одно предупреждение).")]
        public Material coneMaterial;

        [Tooltip("На сколько сегментов бьётся дуга сектора. Больше — глаже край и больше треугольников.")]
        [Min(3)]
        public int coneSegments = 24;

        [Tooltip("Подъём сектора над землёй, метры. Нужен, чтобы плоский меш не мерцал сквозь террейн.")]
        public float groundLift = 0.05f;

        [Tooltip("Сколько секунд область висит после срабатывания МГНОВЕННОГО умения. " +
                 "0 — гаснет сразу в момент срабатывания. У длящихся умений вместо этого берётся их длительность.")]
        [Min(0f)]
        public float instantAreaFadeSeconds = 0f;

        [Header("Разовые надписи в бою (§15 схемы)")]
        // Шесть причин факта боя (BattleFactReason). Слова и цвета — контент, а не код: правило 3.
        // Значения ниже — подстраховка на случай ассета без ключей; рабочие значения лежат в
        // Resources/Catalogs/SkillPresentationSettings.asset (слова и цвета выбраны 08.09.2026 по слову Artsiom).
        // Числа урона выводятся ЧИСЛОМ без слов, поэтому у них не строка, а формат числа.

        [Tooltip("Надпись «удар не достиг цели». Одна на промах бьющего и на уход жертвы — " +
                 "после броска эти случаи неразличимы (решение Artsiom Р4)")]
        public string hitMissedText = "Мимо";

        [Tooltip("Цвет надписи «удар не достиг цели»")]
        public Color hitMissedColor = new Color(0.85f, 0.85f, 0.85f);

        [Tooltip("Надпись «поглощающий щит принял удар на себя». " +
                 "Появляется и тогда, когда часть урона всё же дошла до здоровья")]
        public string shieldAbsorbedText = "Щит";

        [Tooltip("Цвет надписи о поглощении щитом")]
        public Color shieldAbsorbedColor = new Color(0.40f, 0.78f, 1f);

        [Tooltip("Надпись «цель неуязвима» — весь урон пакета отбит")]
        public string invulnerableText = "Неуязвим";

        [Tooltip("Цвет надписи о неуязвимости")]
        public Color invulnerableColor = new Color(1f, 0.84f, 0.35f);

        [Tooltip("Надпись «состояние отбито иммунитетом к контролю» " +
                 "(оглушение, немота, безоружие; слепоту иммунитет не отбивает)")]
        public string statusImmuneText = "Иммунитет";

        [Tooltip("Цвет надписи об иммунитете к контролю")]
        public Color statusImmuneColor = new Color(0.78f, 0.55f, 1f);

        [Tooltip("Надпись «состояние отбито сопротивлением» — сопротивление категории 100 % и выше")]
        public string statusResistedText = "Сопротивление";

        [Tooltip("Цвет надписи о сопротивлении состоянию")]
        public Color statusResistedColor = new Color(0.95f, 0.62f, 0.98f);

        [Tooltip("Цвет числа урона. Само число выводится целым, без дробной части")]
        public Color damageDealtColor = new Color(1f, 0.35f, 0.30f);

        [Tooltip("На сколько метров поднимать каждую СЛЕДУЮЩУЮ надпись над тем же юнитом в одном кадре. " +
                 "Без этого число урона и «Щит» легли бы в одну точку и обе стали бы нечитаемы. " +
                 "0 — не разводить")]
        [Min(0f)]
        public float factStackStep = 0.5f;

        // ============================== ПАССИВНЫЕ УМЕНИЯ ==============================
        // [Interflow 2026-09-09 passive-facts] Слова и цвета срабатываний пассивных умений.
        // ВАЖНО: рабочий ассет создан ДО появления этих полей, и в его YAML их нет — Unity подставит
        // пустую строку, прозрачный чёрный и пустой массив, а не значения инициализаторов ниже
        // (инициализатор работает только для НОВОГО экземпляра). Поэтому пустые значения дозаполняются
        // в OnEnable, а единственная галка сформулирована ОТ ОБРАТНОГО («скрыть»): у выключателя
        // отсутствие поля и осознанное «нет» неразличимы, и умолчание должно совпадать с «поля нет».

        [Header("Пассивные умения: блоки свойств")]
        [Tooltip("Названия восьми блоков свойств по порядку заголовков в ассете умения. " +
                 "Номер блока приезжает числом факта, слово берётся отсюда.")]
        public string[] passiveBlockNames = new string[8]
        {
            "Характеристики",
            "Иммунитет к контролю",
            "Сопротивления",
            "Невидимость",
            "Пробитие брони",
            "Сплеш",
            "Эффекторы атаки",
            "Аура"
        };

        [Tooltip("Что ставится перед названием блока, когда блок ВЫДАН носителю")]
        public string passiveBlockGrantedPrefix = "+ ";

        [Tooltip("Цвет надписи о выдаче блока свойств")]
        public Color passiveBlockGrantedColor = new Color(0.55f, 0.95f, 0.55f);

        [Tooltip("Что ставится перед названием блока, когда блок СНЯТ с носителя")]
        public string passiveBlockRevokedPrefix = "- ";

        [Tooltip("Цвет надписи о снятии блока свойств")]
        public Color passiveBlockRevokedColor = new Color(0.65f, 0.65f, 0.65f);

        [Header("Пассивные умения: реакции")]
        [Tooltip("Надпись «сработал ответный удар по кругу». Рядом печатается урон одной цели")]
        public string passiveCounterText = "Ответ";

        [Tooltip("Цвет надписи об ответном ударе")]
        public Color passiveCounterColor = new Color(1f, 0.65f, 0.25f);

        [Tooltip("Надпись «сработал встречный удар при уходе». Отдельная от обычного ответа: " +
                 "при разборе надо видеть, какой из двух режимов сработал")]
        public string passiveCounterOnEvadeText = "Встречный";

        [Tooltip("Цвет надписи о встречном ударе при уходе")]
        public Color passiveCounterOnEvadeColor = new Color(1f, 0.80f, 0.35f);

        [Tooltip("Надпись «сработала реакция при гибели». Печатается в месте гибели, рядом — задетых врагов")]
        public string passiveDeathBurstText = "При гибели";

        [Tooltip("Цвет надписи о реакции при гибели")]
        public Color passiveDeathBurstColor = new Color(1f, 0.45f, 0.25f);

        [Tooltip("Надпись «сработала реакция при добивании». Рядом печатается, скольких союзников задел клич")]
        public string passiveOnKillText = "Добил";

        [Tooltip("Цвет надписи о реакции при добивании")]
        public Color passiveOnKillColor = new Color(0.95f, 0.95f, 0.45f);

        [Tooltip("Надпись «сработал порог здоровья». Рядом печатается доля здоровья в процентах")]
        public string passiveHpBelowText = "Порог";

        [Tooltip("Цвет надписи о пороге здоровья")]
        public Color passiveHpBelowColor = new Color(1f, 0.40f, 0.60f);

        [Tooltip("Надпись «правило жертвы срезало часть удара». Рядом печатается, сколько единиц срезано. " +
                 "Правило заводят и пассивное умение, и баф, и щит умения — надпись общая для всех")]
        public string incomingDamageReducedText = "Снижен";

        [Tooltip("Цвет надписи о снижении входящего урона")]
        public Color incomingDamageReducedColor = new Color(0.55f, 0.85f, 0.95f);

        [Header("Пассивные умения: реакция 5 «носитель попал по цели»")]
        [Tooltip("Названия десяти кирпичей реакции 5 по порядку исполнения в коде. " +
                 "Номер кирпича приезжает числом факта, слово берётся отсюда.")]
        public string[] passiveOnHitBrickNames = new string[10]
        {
            "Добавочный урон",
            "Состояния цели",
            "Уязвимость",
            "Вампиризм",
            "Оглушение",
            "Отброс",
            "Коридор",
            "Перенос состояний",
            "Клич",
            "Глубокая рана"
        };

        [Tooltip("Что ставится перед названием кирпича реакции 5")]
        public string passiveOnHitPrefix = "Удар: ";

        [Tooltip("Цвет надписи о срабатывании кирпича реакции 5")]
        public Color passiveOnHitColor = new Color(1f, 0.75f, 0.45f);

        [Header("Пассивные умения, собранные кодом")]
        // Название берётся из САМОГО умения (поле «abilityName», иначе имя ассета): своих полей
        // со словами здесь нет — классов два десятка, и заводить на каждый строку было бы
        // копированием того, что уже лежит в ассете умения.

        [Tooltip("Что ставится перед названием пассивки, когда она СРАБОТАЛА. " +
                 "Пусто — печатается одно название")]
        public string passiveProcPrefix = "";

        [Tooltip("Цвет надписи о срабатывании пассивки, собранной кодом")]
        public Color passiveProcColor = new Color(0.75f, 0.90f, 1f);

        [Tooltip("Что ставится перед названием пассивки, когда она ВЫДАНА носителю")]
        public string passiveGrantedPrefix = "+ ";

        [Tooltip("Цвет надписи о выдаче пассивки, собранной кодом")]
        public Color passiveGrantedColor = new Color(0.55f, 0.95f, 0.55f);

        [Tooltip("Что ставится перед названием пассивки, когда она СНЯТА с носителя")]
        public string passiveRevokedPrefix = "- ";

        [Tooltip("Цвет надписи о снятии пассивки, собранной кодом")]
        public Color passiveRevokedColor = new Color(0.65f, 0.65f, 0.65f);

        [Header("Рост урона и наложение состояний")]
        [Tooltip("Надпись «модификаторы носителя усилили его удар». Рядом печатается прибавка в единицах")]
        public string outgoingDamageIncreasedText = "Усилен";

        [Tooltip("Цвет надписи об усилении своего удара")]
        public Color outgoingDamageIncreasedColor = new Color(1f, 0.55f, 0.20f);

        [Tooltip("Надпись «правило жертвы увеличило входящий удар». Рядом печатается прибавка в единицах")]
        public string incomingDamageIncreasedText = "Уязвим";

        [Tooltip("Цвет надписи об уязвимости")]
        public Color incomingDamageIncreasedColor = new Color(1f, 0.45f, 0.75f);

        [Tooltip("Надпись «на юнита наложен поглощающий щит». Рядом печатается объём щита")]
        public string shieldGrantedText = "Щит выдан";

        [Tooltip("Цвет надписи о наложении щита")]
        public Color shieldGrantedColor = new Color(0.35f, 0.95f, 0.85f);

        [Tooltip("Что ставится перед названием наложенного состояния. Пусто — печатается одно название")]
        public string statusAppliedPrefix = "";

        [Tooltip("Цвет надписи о наложенном состоянии")]
        public Color statusAppliedColor = new Color(0.70f, 0.80f, 0.95f);

        [Header("Лента срабатываний на экране")]
        [Tooltip("Скрыть ленту при старте. Показ переключается в игре клавишей F8. " +
                 "Формулировка от обратного намеренная: в ассете, созданном до появления поля, " +
                 "галки нет, и Unity читает её как ВЫКЛ — а это и должно значить «лента видна»")]
        public bool factFeedHidden;

        [Tooltip("Сколько последних строк держать в ленте")]
        [Min(1)]
        public int factFeedLines = 14;

        [Tooltip("Сколько секунд строка живёт в ленте, прежде чем уйти")]
        [Min(1f)]
        public float factFeedSeconds = 10f;

        // [Interflow 2026-09-11] Лента собирает строки ОДНОГО удара в группу: заголовок «кто → кому,
        // чем, на сколько, здоровье было → стало из максимума» и детали расчёта отступом (решение
        // Artsiom 11.09.2026). Слова ниже — контент, а не код (правило 3); пустые дозаполняются в OnEnable.

        [Tooltip("Стрелка между источником и целью в заголовке группы")]
        public string feedArrowText = "→";

        [Tooltip("Отступ строк-деталей под заголовком группы")]
        public string feedDetailIndent = "        ";

        [Tooltip("Во сколько раз приглушить цвет строк-деталей: 1 — как заголовок, 0 — чёрный")]
        [Range(0.3f, 1f)]
        public float feedDetailDim = 0.75f;

        [Tooltip("Слово перед предельным здоровьем: «120 → 68 ИЗ 240»")]
        public string feedHealthOfText = "из";

        [Tooltip("Подпись заявленного числа в деталях удара")]
        public string feedDeclaredText = "заявлено";

        [Tooltip("Подпись ступени брони и таблицы типов в деталях удара: «броня и тип урона 60 → 52»")]
        public string feedArmorText = "броня и тип урона";

        [Tooltip("Слово о числе свёрнутых тиков периодического урона: «−12 ЗА 4 тика»")]
        public string feedTicksText = "за";

        [Tooltip("Слово «тиков» после их числа в свёрнутой строке")]
        public string feedTicksSuffixText = "тиков";

        [Tooltip("Пометка гибели цели в заголовке группы")]
        public string feedDiedText = "погиб";

        [Tooltip("Подпись наложенного состояния в деталях группы")]
        public string feedStatusText = "состояние:";

        [Header("Щит на полоске здоровья")]
        [Tooltip("Материал серого сегмента поглощающего щита (шейдер StrategyCore/HealthBarShield). " +
                 "Цвет и долю высоты сегмента настраивать на самом материале. " +
                 "Пусто — материал создастся в рантайме (подстраховка для редактора; для билда заполнить).")]
        public Material shieldBarMaterial;

        // ============================== ДОЗАПОЛНЕНИЕ ==============================

        /// <summary>
        /// Поля пассивных умений добавлены к уже существующему ассету, и в его YAML их нет: Unity
        /// подставляет пустую строку, прозрачный чёрный и пустой массив, а не значения инициализаторов
        /// выше (инициализатор работает только для НОВОГО экземпляра). Дозаполняем при загрузке —
        /// иначе на экране были бы пустые надписи невидимого цвета, и это выглядело бы как «не работает».
        /// Правка живёт только в памяти: ассет на диске не помечается изменённым.
        /// </summary>
        void OnEnable()
        {
            if (passiveBlockNames == null || passiveBlockNames.Length < 8)
                passiveBlockNames = new string[8]
                {
                    "Характеристики",
                    "Иммунитет к контролю",
                    "Сопротивления",
                    "Невидимость",
                    "Пробитие брони",
                    "Сплеш",
                    "Эффекторы атаки",
                    "Аура"
                };

            if (string.IsNullOrEmpty(passiveBlockGrantedPrefix)) passiveBlockGrantedPrefix = "+ ";
            if (string.IsNullOrEmpty(passiveBlockRevokedPrefix)) passiveBlockRevokedPrefix = "- ";
            if (string.IsNullOrEmpty(passiveCounterText)) passiveCounterText = "Ответ";
            if (string.IsNullOrEmpty(passiveCounterOnEvadeText)) passiveCounterOnEvadeText = "Встречный";
            if (string.IsNullOrEmpty(passiveDeathBurstText)) passiveDeathBurstText = "При гибели";
            if (string.IsNullOrEmpty(passiveOnKillText)) passiveOnKillText = "Добил";
            if (string.IsNullOrEmpty(passiveHpBelowText)) passiveHpBelowText = "Порог";
            if (string.IsNullOrEmpty(incomingDamageReducedText)) incomingDamageReducedText = "Снижен";

            // Прозрачность — признак «поля в ассете не было»: осмысленной надписи с нулевой альфой не бывает.
            if (passiveBlockGrantedColor.a <= 0f) passiveBlockGrantedColor = new Color(0.55f, 0.95f, 0.55f);
            if (passiveBlockRevokedColor.a <= 0f) passiveBlockRevokedColor = new Color(0.65f, 0.65f, 0.65f);
            if (passiveCounterColor.a <= 0f) passiveCounterColor = new Color(1f, 0.65f, 0.25f);
            if (passiveCounterOnEvadeColor.a <= 0f) passiveCounterOnEvadeColor = new Color(1f, 0.80f, 0.35f);
            if (passiveDeathBurstColor.a <= 0f) passiveDeathBurstColor = new Color(1f, 0.45f, 0.25f);
            if (passiveOnKillColor.a <= 0f) passiveOnKillColor = new Color(0.95f, 0.95f, 0.45f);
            if (passiveHpBelowColor.a <= 0f) passiveHpBelowColor = new Color(1f, 0.40f, 0.60f);
            if (incomingDamageReducedColor.a <= 0f) incomingDamageReducedColor = new Color(0.55f, 0.85f, 0.95f);

            // [2026-09-10] Поля реакции 5 и кодовых пассивок добавлены ещё позже — дозаполняем по той же причине.
            if (passiveOnHitBrickNames == null || passiveOnHitBrickNames.Length < 10)
                passiveOnHitBrickNames = new string[10]
                {
                    "Добавочный урон",
                    "Состояния цели",
                    "Уязвимость",
                    "Вампиризм",
                    "Оглушение",
                    "Отброс",
                    "Коридор",
                    "Перенос состояний",
                    "Клич",
                    "Глубокая рана"
                };

            if (string.IsNullOrEmpty(passiveOnHitPrefix)) passiveOnHitPrefix = "Удар: ";
            if (string.IsNullOrEmpty(passiveGrantedPrefix)) passiveGrantedPrefix = "+ ";
            if (string.IsNullOrEmpty(passiveRevokedPrefix)) passiveRevokedPrefix = "- ";
            // passiveProcPrefix намеренно НЕ дозаполняется: пустая строка — рабочее значение
            // (печатается одно название), и отличить её от «поля в ассете нет» нельзя.

            if (passiveOnHitColor.a <= 0f) passiveOnHitColor = new Color(1f, 0.75f, 0.45f);
            if (passiveProcColor.a <= 0f) passiveProcColor = new Color(0.75f, 0.90f, 1f);
            if (passiveGrantedColor.a <= 0f) passiveGrantedColor = new Color(0.55f, 0.95f, 0.55f);
            if (passiveRevokedColor.a <= 0f) passiveRevokedColor = new Color(0.65f, 0.65f, 0.65f);

            // [2026-09-10] Рост урона и наложение состояний — дозаполняем по той же причине.
            if (string.IsNullOrEmpty(outgoingDamageIncreasedText)) outgoingDamageIncreasedText = "Усилен";
            if (string.IsNullOrEmpty(incomingDamageIncreasedText)) incomingDamageIncreasedText = "Уязвим";
            if (outgoingDamageIncreasedColor.a <= 0f) outgoingDamageIncreasedColor = new Color(1f, 0.55f, 0.20f);
            if (incomingDamageIncreasedColor.a <= 0f) incomingDamageIncreasedColor = new Color(1f, 0.45f, 0.75f);
            if (statusAppliedColor.a <= 0f) statusAppliedColor = new Color(0.70f, 0.80f, 0.95f);

            // [2026-09-11] Наложение щита добавлено позже ассета — дозаполняем по той же причине.
            if (string.IsNullOrEmpty(shieldGrantedText)) shieldGrantedText = "Щит выдан";
            if (shieldGrantedColor.a <= 0f) shieldGrantedColor = new Color(0.35f, 0.95f, 0.85f);

            if (factFeedLines < 1) factFeedLines = 14;
            if (factFeedSeconds < 1f) factFeedSeconds = 10f;

            // [2026-09-11] Слова группы добавлены позже ассета — дозаполняем по той же причине,
            // что и слова пассивок выше: в YAML этих ключей нет, и Unity читает пустые строки.
            if (string.IsNullOrEmpty(feedArrowText)) feedArrowText = "→";
            if (string.IsNullOrEmpty(feedDetailIndent)) feedDetailIndent = "        ";
            if (feedDetailDim < 0.3f) feedDetailDim = 0.75f;
            if (string.IsNullOrEmpty(feedHealthOfText)) feedHealthOfText = "из";
            if (string.IsNullOrEmpty(feedDeclaredText)) feedDeclaredText = "заявлено";
            if (string.IsNullOrEmpty(feedArmorText)) feedArmorText = "броня и тип урона";
            if (string.IsNullOrEmpty(feedTicksText)) feedTicksText = "за";
            if (string.IsNullOrEmpty(feedTicksSuffixText)) feedTicksSuffixText = "тиков";
            if (string.IsNullOrEmpty(feedDiedText)) feedDiedText = "погиб";
            if (string.IsNullOrEmpty(feedStatusText)) feedStatusText = "состояние:";
        }

        // ============================== ЧТЕНИЕ ==============================

        /// <summary>
        /// Название блока свойств по его номеру (1–8). Номер приезжает числом факта, поэтому
        /// проверяем границы: причина могла прийти от сервера новее клиента.
        /// </summary>
        /// <summary>
        /// Название кирпича реакции 5 по его номеру (1–10). Границы проверяем по той же причине,
        /// что и у блоков свойств: номер мог прийти от сервера новее клиента.
        /// </summary>
        public string PassiveOnHitBrickName(int brickNumber)
        {
            if (passiveOnHitBrickNames == null) return "Кирпич " + brickNumber;
            if (brickNumber < 1 || brickNumber > passiveOnHitBrickNames.Length) return "Кирпич " + brickNumber;

            string name = passiveOnHitBrickNames[brickNumber - 1];
            return string.IsNullOrEmpty(name) ? "Кирпич " + brickNumber : name;
        }

        public string PassiveBlockName(int blockNumber)
        {
            if (passiveBlockNames == null) return "Блок " + blockNumber;
            if (blockNumber < 1 || blockNumber > passiveBlockNames.Length) return "Блок " + blockNumber;

            string name = passiveBlockNames[blockNumber - 1];
            return string.IsNullOrEmpty(name) ? "Блок " + blockNumber : name;
        }
    }
}
