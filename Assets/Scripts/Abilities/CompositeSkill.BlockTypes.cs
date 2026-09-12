using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // CompositeSkill.BlockTypes.cs — типы данных блоков умения: 3 перечисления и 13 классов уровня namespace. Вырезано 1:1 из CompositeSkill.Blocks.cs (разрезка на partial-ы, правило 22).
    /// <summary>Кого задевает запись урона внутри одного скилла.</summary>
    public enum SkillDamageTargets
    {
        [InspectorName("Врагов")]   Enemies,
        [InspectorName("Союзников")] Allies,
        [InspectorName("Всех")]     All
    }

    /// <summary>Откуда берётся призываемый отряд.</summary>
    public enum SkillSummonMode
    {
        [InspectorName("От кастера")]                  FromCaster,
        [InspectorName("Фикс. отряд у вражеской точки")] FixedSquadAtEnemyPoint,
        [InspectorName("Дубль последней волны")]        LastWave
    }

    /// <summary>Именованный серверный сервис MatchManager — точка расширения для «процессов во времени».</summary>
    public enum SkillServerService
    {
        [InspectorName("Нет")]                   None,
        [InspectorName("Метеоритный дождь")]     MeteorStorm,
        [InspectorName("Подъём павших из могил")] ResurrectFromGraves
    }

    // =====================================================================================
    // Блоки эффектов. Каждый — обычный [Serializable] класс с галкой «включён»;
    // рендерятся штатным BuildGroupedFields по [Header]. Порядок исполнения задан кодом
    // (см. ApplyEffects) — так он одинаков на всех пирах и не зависит от порядка полей.
    // =====================================================================================

    /// <summary>1. Стоимость каста в здоровье самого кастера.</summary>
    [Serializable]
    public class SkillSelfCostBlock
    {
        [Tooltip("Включить блок: каст стоит кастеру здоровья.")]
        public bool enabled;

        [Tooltip("Сколько ХП списать сразу, числом. По уровням. Пример: 50 — минус 50 здоровья.")]
        public float[] flatHp;

        [Tooltip("Сколько ХП списать долей от ТЕКУЩЕГО здоровья. По уровням. Пример: 0.2 — минус 20% от того, что осталось.")]
        public float[] percentOfCurrentHp;

        [Tooltip("Запретить каст, если стоимость добьёт кастера. ВЫКЛ — кастер может убить себя своим же умением.")]
        public bool blockIfLethal = true;
    }

    /// <summary>Одна запись урона: сколько, чем и кому. Записей может быть несколько в одном скилле.</summary>
    [Serializable]
    public class SkillDamageEntry
    {
        [Tooltip("Урон по уровням. Пример: 45 — сорок пять единиц урона.")]
        public float[] amount;

        [Tooltip("Тип урона (ассет из Resources/DamageType). Обязателен, если урон больше нуля.")]
        public DamageType damageType;

        [Tooltip("Кому достаётся этот урон. Позволяет в одном умении бить врагов сильно, а своих слабо.")]
        public SkillDamageTargets targets = SkillDamageTargets.Enemies;
    }

    /// <summary>2. Разовый урон целям.</summary>
    [Serializable]
    public class SkillDamageBlock
    {
        [Tooltip("Включить блок: умение наносит разовый урон.")]
        public bool enabled;

        [Tooltip("Записи урона. Несколько записей = несколько порций урона за каст (например врагам 70 огнём и своим 25 физически).")]
        public SkillDamageEntry[] entries;
    }

    /// <summary>3. Оглушение / обезоруживание / немота.</summary>
    [Serializable]
    public class SkillStatusBlock
    {
        [Tooltip("Включить блок: умение вешает штатный контроль — оглушение, обезоруживание, немоту.")]
        public bool enabled;

        [Tooltip("Оглушение целей, секунды. По уровням. 0 — не оглушать. Иммунные к контролю юниты пропускаются штатно.")]
        public float[] stunSeconds;

        [Tooltip("Обезоруживание целей (нельзя атаковать), секунды. По уровням. 0 — не обезоруживать.")]
        public float[] disarmSeconds;

        [Tooltip("Немота целей (нельзя кастовать), секунды. По уровням. 0 — не заглушать.")]
        public float[] muteSeconds;
    }

    /// <summary>
    /// Одна запись блока эффекторов. Ассет говорит ЧТО происходит (какие статы, урон в секунду,
    /// VFX, иконка, стакинг), умение — СКОЛЬКО и КАК ДОЛГО. ADR-006 §5.2.
    /// </summary>
    [Serializable]
    public class SkillEffectorRecord
    {
        [Tooltip("Ассет состояния: ЧТО оно делает — какие статы меняет, урон в секунду, VFX, иконку, накопление (Stacks).")]
        public Effector effector;

        [Tooltip("Множитель силы по уровням. Масштабирует и пассивные изменения статов, и урон в секунду. " +
                 "Пусто или 0 — брать силу как в ассете. Отрицательные значения ЗАПРЕЩЕНЫ: разделитель " +
                 "записей в формате сохранения — дефис, минус ломает сейв (SaveManager.UnitData).")]
        public float[] power;

        [Tooltip("Длительность в секундах по уровням. Пусто или 0 — брать длительность из ассета. " +
                 "У бессрочных состояний не действует: длительности у них нет по определению.")]
        public float[] duration;
    }

    /// <summary>4. Эффекторы на цели — именно они дают значок в панели состояний.</summary>
    [Serializable]
    public class SkillEffectorsBlock
    {
        [Tooltip("Включить блок: умение вешает состояния.")]
        public bool enabled;

        [Tooltip("Состояния, накладываемые каждой цели (замедление, яд, бафы статов), у каждого своя " +
                 "сила и длительность по уровням. Значок в панели появляется только " +
                 "у ненакапливаемых состояний с иконкой.")]
        public SkillEffectorRecord[] records;

        // УСТАРЕЛО. Плоский список до 2026-08-02. Поле СПЕЦИАЛЬНО оставлено сериализуемым: пока оно есть,
        // Unity продолжает читать старые ассеты, и миграция в records выполняется скриптом без потери
        // данных. Удалить вместе с миграцией после её приёмки в игре.
        [HideInInspector]
        public Effector[] effectors;
    }

    /// <summary>5. Мгновенное лечение.</summary>
    [Serializable]
    public class SkillHealBlock
    {
        [Tooltip("Включить блок: умение мгновенно лечит цели.")]
        public bool enabled;

        [Tooltip("Лечение числом, по уровням. Пример: 120 — плюс сто двадцать здоровья.")]
        public float[] flat;

        [Tooltip("Лечение в ПРОЦЕНТАХ от максимального здоровья цели, целым числом, по уровням. " +
                 "Пример: 25 — вылечить на четверть максимума. Перелечить нельзя, штатный ChangeHP сам обрежет.")]
        public float[] percentOfMaxHp;
    }

    /// <summary>6. Длящийся баф на цели (аура урона, лечение во времени, самосожжение, защита, детонация).</summary>
    [Serializable]
    public class SkillBuffBlock
    {
        [Tooltip("Включить блок: на цели вешается длящийся баф (компонент SkillBuff).")]
        public bool enabled;

        [Tooltip("Сколько секунд держится баф, по уровням. Ноль — баф не вешается вовсе.")]
        public float[] duration;

        [Header("Аура урона вокруг носителя")]
        [Tooltip("Урон в секунду по тем, кто рядом с носителем бафа. По уровням. 0 — ауры нет. Пример «огненный плащ».")]
        public float[] auraDamagePerSecond;

        [Tooltip("Тип урона ауры. Обязателен, если урон ауры больше нуля.")]
        public DamageType auraDamageType;

        [Tooltip("Добавка к радиусу ауры сверх габарита носителя, по уровням.")]
        public float[] auraRadius;

        [Tooltip("Кого жжёт аура (обычно враги).")]
        public UnitSelector auraSelector;

        [Header("Лечение во времени")]
        [Tooltip("Лечение числом в секунду, по уровням. 0 — не лечит.")]
        public float[] healPerSecond;

        [Tooltip("Лечение в ПРОЦЕНТАХ от максимального здоровья в секунду, целым числом, по уровням. " +
                 "Пример: 2 — два процента максимума каждую секунду.")]
        public float[] healPercentOfMaxPerSecond;

        [Header("Самосожжение")]
        [Tooltip("Сколько здоровья носитель теряет в секунду от самого бафа, по уровням. 0 — не жжёт носителя.")]
        public float[] selfBurnPerSecond;

        [Header("Защита")]
        [Tooltip("Множитель ВХОДЯЩЕГО урона носителя, по уровням. 1 — без изменений, 0.65 — минус тридцать пять процентов. " +
                 "Пусто или 1 — блок защиты не ставится.")]
        public float[] incomingDamageMultiplier;

        [Tooltip("Дать носителю иммунитет к контролю (оглушение и т.п.) на время бафа.")]
        public bool controlImmunity;

        // [Interflow fix 2026-09-03 status-resistances] Второй вход выдачи сопротивлений (решение Artsiom
        // 03.09.2026 «оба входа»): те же строки, что в блоке 3 конструктора пассивок. ТОЛЬКО ДАННЫЕ —
        // выдаёт и снимает SkillBuff (вклады живут ровно столько, сколько баф).
        [Tooltip("Сопротивления и слабости носителя к категориям состояний на время бафа: строки «категория — доля». " +
                 "0,3 — сопротивление 30 %, −0,5 — слабость 50 %, 1 и больше — состояния категории не действуют вовсе. " +
                 "Контроль (оглушение, немота, безоружие, слепота) режется по времени, замедления и периодический урон — по силе. " +
                 "Из всех источников на юните действует одно значение: сильнейшая слабость, иначе сильнейшее сопротивление. " +
                 "Пусто — блок сопротивлений не ставится.")]
        public ResistanceEntry[] resistances;

        [Header("Визуал")]
        [Tooltip("VFX бафа на носителе. Вешается один раз, при продлении не дублируется. Пусто — без визуала.")]
        public VFXReferencer buffVFX;

        [Header("Детонация при смерти носителя")]
        [Tooltip("Взорвать носителя, если он погиб под бафом. Срабатывает один раз.")]
        public bool detonateOnDeath;

        [Tooltip("Радиус взрыва, по уровням.")]
        public float[] detonationRadius;

        [Tooltip("Урон взрыва, по уровням.")]
        public float[] detonationDamage;

        [Tooltip("Тип урона взрыва. Обязателен, если урон взрыва больше нуля.")]
        public DamageType detonationDamageType;

        [Tooltip("Кого задевает взрыв (обычно враги).")]
        public UnitSelector detonationSelector;
    }

    /// <summary>7. Поглощающий щит на целях.</summary>
    [Serializable]
    public class SkillShieldBlock
    {
        [Tooltip("Включить блок: цели получают поглощающий щит.")]
        public bool enabled;

        [Tooltip("Объём щита числом, по уровням.")]
        public float[] flat;

        [Tooltip("Объём щита в ДОЛЕ от максимального здоровья цели, по уровням. Пример: 0.3 — щит на 30% максимума.")]
        public float[] percentOfMaxHp;

        [Tooltip("Сколько секунд держится щит, по уровням. ВНИМАНИЕ: 0 или пусто — щит БЕССРОЧНЫЙ, " +
                 "он сойдёт только когда его пробьют или носитель погибнет.")]
        public float[] duration;

        [Tooltip("Состояния, которые получит носитель в момент ПРОБИТИЯ щита (объём исчерпан). Пусто — ничего не происходит.")]
        public Effector[] onDepletedEffectors;

        [Tooltip("Радиус ВСПЫШКИ при пробитии щита, метры. 0 — вспышки нет, реакция достаётся только носителю. " +
                 "Больше нуля — состояния и ослепление ниже получают все вокруг носителя по селектору ответа.")]
        public float onDepletedRadius;

        [Tooltip("ОТВЕТ НА УДАР, пока щит держится: радиус вокруг носителя, метры. 0 — ответа нет.")]
        public float retaliationRadius;

        [Tooltip("Ответ на удар: оглушение задетых, секунды. 0 — не оглушать. Штатный стан уважает иммунитет к контролю.")]
        public float retaliationStunSeconds;

        [Tooltip("Ответ на удар: состояния задетым (например заморозка). Пусто — только оглушение.")]
        public Effector[] retaliationEffectors;

        [Tooltip("Ответ на удар: задевать только юнитов ближнего боя. ВЫКЛ — всех подходящих в радиусе.")]
        public bool retaliationOnlyMelee = true;

        [Tooltip("Кого задевают ответ на удар и вспышка при пробитии (обычно враги носителя щита).")]
        public UnitSelector reactionSelector;

        [Tooltip("Ослепить носителя при пробитии щита: шанс промаха 0..1. 0 — не ослеплять.")]
        [Range(0f, 1f)]
        public float onDepletedBlindChance;

        [Tooltip("Длительность ослепления при пробитии щита, секунды.")]
        public float onDepletedBlindDuration;

        [Tooltip("Множитель ВХОДЯЩЕГО урона носителя, пока щит держится: 0.5 — минус половина. 1 — без изменений. " +
                 "Живёт ровно столько, сколько сам щит: пробили раньше срока — снижение снимается вместе с ним. " +
                 "Тем и отличается от такого же поля в блоке длящегося бафа, где оно идёт по своему таймеру.\n\n" +
                 "ТРЕБУЕТ ненулевой длительности щита: у бессрочного щита снижение не ставится.")]
        public float incomingDamageMultiplier = 1f;
    }

    /// <summary>8. Ослепление целей (шанс промаха).</summary>
    [Serializable]
    public class SkillBlindBlock
    {
        [Tooltip("Включить блок: цели получают шанс промахиваться.")]
        public bool enabled;

        [Tooltip("Шанс промаха 0..1, по уровням. Пример: 0.4 — сорок процентов ударов мимо.")]
        public float[] chance;

        [Tooltip("Длительность ослепления в секундах, по уровням.")]
        public float[] duration;
    }

    /// <summary>9. Призыв юнитов через готовые серверные сервисы матча.</summary>
    [Serializable]
    public class SkillSummonBlock
    {
        [Tooltip("Включить блок: умение призывает юнитов.")]
        public bool enabled;

        [Tooltip("Откуда берётся отряд: у кастера / фиксированный набор у вражеской точки / дубль последней волны.")]
        public SkillSummonMode mode = SkillSummonMode.FromCaster;

        [Tooltip("Префаб призываемого юнита. Не нужен только для режима «дубль последней волны».")]
        public Unit prefab;

        [Tooltip("Сколько юнитов призвать за каст, по уровням.")]
        public float[] count;

        [Tooltip("Сколько секунд живут призванные, по уровням. 0 — навсегда.")]
        public float[] lifetime;

        [Tooltip("Слушают ли призванные общие приказы Атака/Защита. ВЫКЛ — живут своей фиксированной командой.")]
        public bool obeyCommands;

        [Tooltip("Команда, которую призванные получают сразу (когда общие приказы они не слушают).")]
        public BottomTableAction command = BottomTableAction.Attack;

        [Tooltip("Режим «от кастера»: сколько призванных этого носителя может быть живо одновременно. 0 — без лимита.")]
        public int maxAlivePerCaster;

        [Tooltip("Режим «от кастера»: смерть носителя добивает его призванных. ВЫКЛ — живут дальше.")]
        public bool killSummonsOnOwnerDeath;

        [Tooltip("Режим «фикс. отряд»: сколько таких призванных может быть живо у игрока одновременно. 0 — без лимита.")]
        public int maxAlivePerPlayer;

        [Tooltip("Разброс точек появления, метры. Имеет смысл при количестве больше одного.")]
        public float spawnSpread = 1.5f;

        [Tooltip("VFX в точке появления. Пусто — без визуала.")]
        public VFXReferencer summonVFX;

        [Tooltip("Звук появления. Пусто — без звука.")]
        public AudioClip summonSound;

        [Tooltip("Громкость звука появления, 0..1.")]
        [Range(0f, 1f)]
        public float summonSoundVolume = 1f;
    }

    /// <summary>10. Зона-ловушка на земле (колья, огненный ковёр, шипы); с галкой «идёт за кастером» — аура на время.</summary>
    [Serializable]
    public class SkillGroundZoneBlock
    {
        [Tooltip("Включить блок: умение выставляет зону на землю.")]
        public bool enabled;

        [Tooltip("Зона идёт за кастером — это «аура на время»: живёт срок зоны (задан на префабе), каждый тик стоит " +
                 "в позиции кастера, задевает и тех, кто вошёл позже, и гаснет вместе с кастером. " +
                 "Ставится ровно ОДНА зона в позиции кастера: количество, разброс и смещение вперёд не применяются.")]
        public bool followCaster;

        [Tooltip("Префаб зоны. Обязан нести компонент GroundDamageZone — радиус, урон и состояния настраиваются на самом префабе.")]
        public GameObject zonePrefab;

        [Tooltip("Сколько зон ставится за один каст (например частокол из нескольких участков).")]
        [Min(1)]
        public int zoneCount = 1;

        [Tooltip("Разброс зон вокруг точки установки, метры. Имеет смысл при количестве больше одной.")]
        public float spread = 1.5f;

        [Tooltip("Смещение зоны вперёд от кастера, метры. Работает, когда зона ставится вокруг кастера.")]
        public float forwardOffset;
    }

    /// <summary>11. Вызов именованного серверного сервиса матча (процессы во времени).</summary>
    [Serializable]
    public class SkillDelegateBlock
    {
        [Tooltip("Включить блок: умение запускает серверный сервис матча.")]
        public bool enabled;

        [Tooltip("Какой сервис запустить.")]
        public SkillServerService service = SkillServerService.None;

        [Header("Метеоритный дождь")]
        [Tooltip("Сколько секунд идёт дождь.")]
        public float meteorDurationSeconds = 5f;

        [Tooltip("Сколько метеоров падает каждую секунду.")]
        public int meteorsPerSecond = 4;

        [Tooltip("Радиус зоны поражения одного метеора.")]
        public float meteorImpactRadius = 2f;

        [Tooltip("Физический урон в зоне метеора.")]
        public float meteorPhysicalDamage;

        [Tooltip("Тип физического урона метеора.")]
        public DamageType meteorPhysicalDamageType;

        [Tooltip("Огненный урон в зоне метеора.")]
        public float meteorFireDamage;

        [Tooltip("Тип огненного урона метеора.")]
        public DamageType meteorFireDamageType;

        [Tooltip("Оглушение всех в зоне метеора, секунды.")]
        public float meteorStunSeconds;

        [Tooltip("Целиться по скоплениям врагов. ВЫКЛ — цель выбирается равномерно случайно.")]
        public bool meteorUseDensityTargeting = true;

        [Tooltip("По кому выбирается цель метеора.")]
        public UnitSelector meteorTargetSelector;

        [Tooltip("Кого задевает зона метеора (обычно всех, включая своих).")]
        public UnitSelector meteorSplashSelector;

        [Tooltip("VFX падения метеора. Пусто — без визуала.")]
        public VFXReferencer meteorVFX;

        [Tooltip("Звук падения метеора.")]
        public AudioClip meteorImpactSound;

        [Tooltip("Громкость звука метеора, 0..1.")]
        [Range(0f, 1f)]
        public float meteorImpactVolume = 1f;

        [Header("Подъём павших из могил")]
        [Tooltip("Радиус поиска могил вокруг точки каста.")]
        public float resurrectRadius = 8f;

        [Tooltip("Сколько павших поднять максимум.")]
        public int resurrectCount = 3;

        [Tooltip("Поднимать сначала самых высокотировых. ВЫКЛ — в порядке реестра могил.")]
        public bool resurrectHighestTierFirst = true;

        [Tooltip("Поднятые живут ограниченное время. ВЫКЛ — остаются навсегда.")]
        public bool resurrectTemporary = true;

        [Tooltip("Сколько секунд живут поднятые, если они временные.")]
        public float resurrectLifetime = 30f;

        [Tooltip("Слушают ли поднятые общие приказы Атака/Защита.")]
        public bool resurrectObeyCommands = true;
    }
}
