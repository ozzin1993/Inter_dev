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

        [Header("Щит на полоске здоровья")]
        [Tooltip("Материал серого сегмента поглощающего щита (шейдер StrategyCore/HealthBarShield). " +
                 "Цвет и долю высоты сегмента настраивать на самом материале. " +
                 "Пусто — материал создастся в рантайме (подстраховка для редактора; для билда заполнить).")]
        public Material shieldBarMaterial;
    }
}
