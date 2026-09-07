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
        // Числа урона выводятся ЧИСЛОМ без слов, поэтому у них не строка, а формат числа.

        [Tooltip("Надпись «удар не достиг цели». Одна на промах бьющего и на уход жертвы — " +
                 "после броска эти случаи неразличимы (решение Artsiom Р4)")]
        public string hitMissedText = "Мимо";

        [Tooltip("Цвет надписи «удар не достиг цели»")]
        public Color hitMissedColor = new Color(0.80f, 0.80f, 0.80f);

        [Tooltip("Надпись «поглощающий щит принял удар на себя». " +
                 "Появляется и тогда, когда часть урона всё же дошла до здоровья")]
        public string shieldAbsorbedText = "Щит";

        [Tooltip("Цвет надписи о поглощении щитом")]
        public Color shieldAbsorbedColor = new Color(0.45f, 0.75f, 1f);

        [Tooltip("Надпись «цель неуязвима» — весь урон пакета отбит")]
        public string invulnerableText = "Неуязвим";

        [Tooltip("Цвет надписи о неуязвимости")]
        public Color invulnerableColor = new Color(1f, 0.85f, 0.30f);

        [Tooltip("Надпись «состояние отбито иммунитетом к контролю» " +
                 "(оглушение, немота, безоружие; слепоту иммунитет не отбивает)")]
        public string statusImmuneText = "Иммунитет";

        [Tooltip("Цвет надписи об иммунитете к контролю")]
        public Color statusImmuneColor = new Color(0.75f, 0.55f, 1f);

        [Tooltip("Надпись «состояние отбито сопротивлением» — сопротивление категории 100 % и выше")]
        public string statusResistedText = "Сопротивление";

        [Tooltip("Цвет надписи о сопротивлении состоянию")]
        public Color statusResistedColor = new Color(0.40f, 0.90f, 0.75f);

        [Tooltip("Формат числа урона (правила форматирования чисел .NET). " +
                 "«0» — целое без дробной части, «0.#» — с одним знаком после запятой при необходимости")]
        public string damageNumberFormat = "0";

        [Tooltip("Цвет числа урона")]
        public Color damageDealtColor = new Color(1f, 0.35f, 0.30f);

        [Header("Щит на полоске здоровья")]
        [Tooltip("Материал серого сегмента поглощающего щита (шейдер StrategyCore/HealthBarShield). " +
                 "Цвет и долю высоты сегмента настраивать на самом материале. " +
                 "Пусто — материал создастся в рантайме (подстраховка для редактора; для билда заполнить).")]
        public Material shieldBarMaterial;
    }
}
