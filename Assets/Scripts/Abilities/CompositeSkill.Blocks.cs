using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
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

        [Tooltip("Не накладывать щит на цель, у которой он уже висит. ВЫКЛ — новый щит заменит прежний.")]
        public bool skipIfAlreadyShielded = true;
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

    /// <summary>10. Зона-ловушка на земле (колья, огненный ковёр, шипы).</summary>
    [Serializable]
    public class SkillGroundZoneBlock
    {
        [Tooltip("Включить блок: умение выставляет зону на землю.")]
        public bool enabled;

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

    // =====================================================================================
    // Исполнитель блоков. Единственная точка, где блоки применяются, — порядок фиксирован здесь.
    // Вызывается ТОЛЬКО на сервере (гейт стоит в CompositeSkill.Execute).
    // =====================================================================================
    public partial class CompositeSkill
    {
        /// <summary>
        /// Применить включённые блоки в фиксированном порядке:
        /// стоимость → урон → контроль → эффекторы → лечение → баф → щит → ослепление →
        /// призыв → зона → серверный сервис.
        /// Цель, погибшую от урона этого же каста, дальше не обрабатываем (паттерн EffectorArea).
        /// </summary>
        /// Вызывается ТОЛЬКО на сервере: клиент до этого места не доходит. Значки состояний и VFX
        /// уезжают клиенту отдельным сообщением (CompositeSkill.SendPresentation) — геймплейного
        /// состояния у клиента не появляется вовсе.
        /// <param name="skipProjectileCarried">
        /// true, когда доставка идёт снарядом: урон, контроль-оглушение и эффекторы уже переданы снаряду
        /// и мгновенно применяться не должны.
        /// </param>
        void ApplyEffects(Unit castingUnit, int castingPlayer, int level, List<Unit> targets,
                          Vector3 origin, bool skipProjectileCarried)
        {
            // ---------- 1. Стоимость в здоровье кастера ----------
            if (selfCost != null && selfCost.enabled && castingUnit != null)
            {
                float pct = LevelValue(selfCost.percentOfCurrentHp, level);
                float flat = LevelValue(selfCost.flatHp, level);

                // Списание через PayHealth: штатный ChangeHP только зажимает здоровье в ноль и не убивает,
                // поэтому кастер с выключенным «Запретить каст, если стоимость добьёт» оставался жив с нулём ХП.
                if (pct > 0f) PercentHpCost.PayFromCaster(castingUnit, pct);
                if (flat > 0f) PayHealth(castingUnit, flat);
            }

            // ---------- 2..15. Блоки по каждой цели (порядок фиксирован) ----------
            if (targets != null)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    Unit t = targets[i];
                    if (t == null || t.dead) continue;

                    // Рывок идёт ПЕРВЫМ: не притянулась — цель выпадает из каста целиком
                    // (решение Artsiom 2026-08-06), КД и мана при этом списаны штатно.
                    if (!ApplyPull(castingUnit, t)) continue;

                    // Урон запоминаем: из него блок вторичных целей берёт долю на лечение.
                    float baseDamageToTarget = skipProjectileCarried
                        ? 0f
                        : ApplyDamage(castingUnit, castingPlayer, level, t, origin);
                    if (t.dead) continue; // погиб от этого же урона — дальше по нему не работаем

                    ApplyDrain(castingUnit, level, t);
                    if (t.dead) continue; // высасывание добило — дальше по нему не работаем

                    ApplyStatus(level, t, skipProjectileCarried);
                    ApplyEffectors(castingPlayer, level, t); // эффекторы снарядом не переносятся — вешаем сами
                    ApplyHeal(level, t);
                    ApplyMana(level, t);
                    ApplyBuff(castingUnit, level, t);
                    ApplyShield(castingPlayer, level, t);
                    ApplyBlind(level, t);
                    ApplyMorph(level, t);
                    ApplyOwnership(castingUnit, t);          // после всех эффектов: меняет сторону цели
                    ApplySecondary(castingPlayer, level, t, baseDamageToTarget); // своя выборка вокруг этой цели
                    ApplyKnockback(castingUnit, level, t);   // последним: сдвигает цель, всё позиционное уже сработало
                }
            }

            // ---------- 16. Призыв ----------
            if (summon != null && summon.enabled) ApplySummon(castingUnit, castingPlayer, level);

            // ---------- 17. Зона на земле ----------
            if (groundZone != null && groundZone.enabled) ApplyGroundZone(castingUnit, castingPlayer, level, origin);

            // ---------- 18. Перемещение кастера ----------
            ApplyCasterMove(castingUnit, origin);

            // ---------- 19. Серверный сервис ----------
            if (delegateService != null && delegateService.enabled) ApplyDelegate(castingUnit, castingPlayer, origin);
        }

        // ------------------------------------------------------------------ 2. УРОН --
        /// <returns>
        /// Сумма урона, ЗАПИСАННОГО в блоке для этой цели (по всем сработавшим записям), — до брони,
        /// сопротивлений и щитов. Нужна блоку вторичных целей: он лечит долей от неё.
        /// Фактически прошедший урон здесь не считается — так же вёл себя класс HolyFire, чьё поведение
        /// блок повторяет; смена на фактический молча изменила бы силу лечения на бронированных целях.
        /// </returns>
        float ApplyDamage(Unit castingUnit, int castingPlayer, int level, Unit target, Vector3 origin)
        {
            if (damage == null || !damage.enabled || damage.entries == null) return 0f;

            float dealt = 0f;

            for (int e = 0; e < damage.entries.Length; e++)
            {
                SkillDamageEntry entry = damage.entries[e];
                if (entry == null || entry.damageType == null) continue;
                if (target.dead) return dealt;

                float amount = LevelValue(entry.amount, level);
                if (amount <= 0f) continue;

                // Кого именно задевает ЭТА запись — решает штатный предикат селектора,
                // у которого подменены только флаги свой/союзник/враг.
                if (!UnitSelector.IsUnitCompatible(castingPlayer, target, RelationSelector(entry.targets))) continue;

                if (castingUnit != null)
                    castingUnit.DealDamage(target, amount, entry.damageType, false, origin); // false: способность, не прямая атака
                else
                    target.GetDamage(amount, entry.damageType, castingPlayer, null, false, out float _);

                dealt += amount;
            }

            return dealt;
        }

        /// <summary>
        /// Селектор ТОЛЬКО для проверки отношения «свой/союзник/враг». Типовые флаги
        /// (юнит/здание/земля/воздух и пр.) включены все осознанно: набор целей УЖЕ прошёл через
        /// unitSelector скилла при сборе, а в режимах «на себя»/«вся команда» селектор вообще не участвует
        /// и ГД его не заполняет — с копией флагов такой скилл молча не наносил бы урона.
        /// </summary>
        static UnitSelector RelationSelector(SkillDamageTargets relation)
        {
            bool own  = relation != SkillDamageTargets.Enemies;
            bool ally = relation != SkillDamageTargets.Enemies;
            bool foe  = relation != SkillDamageTargets.Allies;

            return new UnitSelector(own, ally, foe, true, true, true, true, true, true, true, true, true);
        }

        // -------------------------------------------------------------- 3. КОНТРОЛЬ --
        void ApplyStatus(int level, Unit target, bool stunCarriedByProjectile)
        {
            if (status == null || !status.enabled) return;

            // Оглушение переносится снарядом (штатное поле stunTime) — мгновенно его не вешаем.
            if (!stunCarriedByProjectile)
            {
                float stun = LevelValue(status.stunSeconds, level);
                if (stun > 0f) target.Stun(stun);
            }

            float disarm = LevelValue(status.disarmSeconds, level);
            if (disarm > 0f) target.Disarm(disarm);

            float mute = LevelValue(status.muteSeconds, level);
            if (mute > 0f) target.Mute(mute);
        }

        // ------------------------------------------------------------- 4. ЭФФЕКТОРЫ --
        // Ассет эффектора говорит ЧТО происходит, умение — СКОЛЬКО и КАК ДОЛГО (ADR-006 §5.2).
        // Здесь же вешается эффектор-значок состояния: он тоже эффектор, только своих чисел не имеет.
        void ApplyEffectors(int castingPlayer, int level, Unit target)
        {
            if (effectors != null && effectors.enabled && effectors.records != null)
            {
                for (int i = 0; i < effectors.records.Length; i++)
                {
                    SkillEffectorRecord r = effectors.records[i];
                    if (r == null || r.effector == null) continue;

                    // unitOwner = null: тот же владелец, что был у прежнего массивного вызова
                    // Effector.EffectorAdd(castingPlayer, target, set) — поведение не меняется.
                    Effector.EffectorAdd(target, r.effector, null, castingPlayer, 0f,
                                         RecordPower(r, level), RecordDuration(r, level));
                }
            }

            if (statusEffector != null)
                Effector.EffectorAdd(target, statusEffector, null, castingPlayer);
        }

        /// <summary>Множитель силы записи на уровне. Пусто или 0 — как в ассете (множитель 1).</summary>
        /// Выборка с КЛАМПОМ к последнему элементу (LevelValue) — семантика по умолчанию
        /// для нового кода, см. InterflowAbility.
        public static float RecordPower(SkillEffectorRecord r, int level)
        {
            if (r == null) return 1f;
            float v = LevelValue(r.power, level);
            return v > 0f ? v : 1f;
        }

        /// <summary>Длительность записи на уровне, секунды. Пусто или 0 — «не переопределять» (−1).</summary>
        public static float RecordDuration(SkillEffectorRecord r, int level)
        {
            if (r == null) return -1f;
            float v = LevelValue(r.duration, level);
            return v > 0f ? v : -1f;
        }

        // ---------------------------------------------------------------- 5. ЛЕЧЕНИЕ --
        void ApplyHeal(int level, Unit target)
        {
            if (heal == null || !heal.enabled) return;

            float amount = LevelValue(heal.flat, level);
            float percent = LevelValue(heal.percentOfMaxHp, level);
            if (percent > 0f) amount += percent / 100f * target.maxHealth; // проценты целым числом: 25 = 25%

            if (amount > 0f) target.ChangeHP(amount); // сам клампит до максимума и синкает клиентам
        }

        // -------------------------------------------------------------------- 6. БАФ --
        void ApplyBuff(Unit castingUnit, int level, Unit target)
        {
            if (buff == null || !buff.enabled) return;
            if (LevelValue(buff.duration, level) <= 0f) return;

            SkillBuff.Apply(target, this, level, castingUnit);
        }

        // -------------------------------------------------------------------- 7. ЩИТ --
        void ApplyShield(int castingPlayer, int level, Unit target)
        {
            if (shield == null || !shield.enabled) return;

            // Поверх живого щита не накладываем (поведение класса ShieldAlly). Откат и мана при этом
            // уже списаны — как и раньше: проверка стоит в применении, а не в выборе цели, потому что
            // блок работает и по области, где целей несколько.
            if (shield.skipIfAlreadyShielded && AbsorbShield.IsActiveOn(target)) return;

            float amount = LevelValue(shield.flat, level)
                         + LevelValue(shield.percentOfMaxHp, level) * target.maxHealth;
            if (amount <= 0f) return;

            float duration = LevelValue(shield.duration, level);

            Effector[] onDepletedEffectors = shield.onDepletedEffectors;
            float blindChance = shield.onDepletedBlindChance;
            float blindDuration = shield.onDepletedBlindDuration;
            float burstRadius = shield.onDepletedRadius;
            bool hasReaction = (onDepletedEffectors != null && onDepletedEffectors.Length > 0)
                               || (blindChance > 0f && blindDuration > 0f);
            bool hasRetaliation = shield.retaliationRadius > 0f
                                  && (shield.retaliationStunSeconds > 0f
                                      || (shield.retaliationEffectors != null && shield.retaliationEffectors.Length > 0));

            // Снижение входящего урона привязано к ЖИЗНИ ЩИТА, а не к своему таймеру: ставим вместе
            // со щитом, снимаем в onEnded (пробит, истёк или носитель погиб). Тем и отличается
            // от такого же поля в блоке бафа.
            // Длительность обязательна: штатный IncomingDamageModifier.Apply при нулевой молча ничего
            // не ставит (IncomingDamageModifier.cs:43). У бессрочного щита снижения не будет — ровно так же
            // вёл себя класс ShieldAlly, поведение не меняем.
            float incomingMultiplier = shield.incomingDamageMultiplier;
            bool hasIncomingRule = !Mathf.Approximately(incomingMultiplier, 1f)
                                   && incomingMultiplier > 0f
                                   && duration > 0f;

            if (!hasReaction && !hasRetaliation && !hasIncomingRule)
            {
                AbsorbShield.Apply(target, amount, duration);
                return;
            }

            // Реакция на ПРОБИТИЕ (объём исчерпан), а не на любое снятие — паттерн ShieldAlly.
            // При заданном радиусе она достаётся не носителю, а всем вокруг него по селектору ответа.
            Action<Unit> onDepleted = carrier =>
            {
                if (carrier == null || carrier.dead) return;

                if (burstRadius <= 0f)
                {
                    if (onDepletedEffectors != null && onDepletedEffectors.Length > 0)
                        Effector.EffectorAdd(castingPlayer, carrier, onDepletedEffectors);
                    if (blindChance > 0f && blindDuration > 0f)
                        BlindDebuff.Apply(carrier, blindChance, blindDuration);
                    return;
                }

                Unit[] around = UnitsAroundCarrier(carrier, burstRadius);
                if (around == null) return;

                for (int i = 0; i < around.Length; i++)
                {
                    Unit u = around[i];
                    if (u == null || u.dead) continue;

                    if (onDepletedEffectors != null && onDepletedEffectors.Length > 0)
                        Effector.EffectorAdd(castingPlayer, u, onDepletedEffectors);
                    if (blindChance > 0f && blindDuration > 0f)
                        BlindDebuff.Apply(u, blindChance, blindDuration);
                }
            };

            if (!hasRetaliation && !hasIncomingRule)
            {
                AbsorbShield.Apply(target, amount, duration, onDepleted);
                return;
            }

            // Ответ на удар и снижение урона живут ровно столько, сколько щит: снимаем их в onEnded
            // (любая причина снятия). ПОРЯДОК КАК В ShieldAlly: сначала щит, потом подписка — перекаст
            // поверх живого щита дёргает прежний onEnded, и тот снёс бы только что поставленную подписку.
            InterflowCombat.DamagedHandler retaliation = null;
            Action<Unit> onEnded = carrier =>
            {
                if (carrier == null) return;

                if (retaliation != null) InterflowCombat.DamagedListenerRemove(carrier, retaliation);
                if (hasIncomingRule) IncomingDamageModifier.RemoveRule(carrier, incomingMultiplier);
            };

            AbsorbShield.Apply(target, amount, duration, onDepleted, onEnded);

            if (hasRetaliation)
            {
                retaliation = (victim, attacker, damageType, damageDealt, directAttack) =>
                    RetaliateAround(victim, castingPlayer);
                InterflowCombat.DamagedListenerAdd(target, retaliation);
            }

            // Ставим ПОСЛЕ щита по той же причине, что и подписку: onEnded прежнего щита снял бы новое правило.
            // Таймер здесь страховочный — обычно правило снимает onEnded, когда щит сходит.
            if (hasIncomingRule) IncomingDamageModifier.Apply(target, incomingMultiplier, duration);
        }

        /// <summary>Кого задевают вспышка и ответ щита: враги носителя по селектору ответа.</summary>
        Unit[] UnitsAroundCarrier(Unit carrier, float radius)
        {
            if (carrier == null || radius <= 0f) return null;

            Vector2 center = new Vector2(carrier.transform.position.x, carrier.transform.position.z);
            return Utils.GetUnitsInRadius(center, radius, carrier.owner, shield.reactionSelector, -1, carrier);
        }

        /// <summary>Ответ щита на удар по носителю: оглушение и эффекторы тем, кто рядом.</summary>
        void RetaliateAround(Unit carrier, int castingPlayer)
        {
            if (IsClientPeer) return;
            if (carrier == null || carrier.dead) return;

            Unit[] around = UnitsAroundCarrier(carrier, shield.retaliationRadius);
            if (around == null) return;

            for (int i = 0; i < around.Length; i++)
            {
                Unit u = around[i];
                if (u == null || u.dead) continue;
                if (shield.retaliationOnlyMelee && !u.melee) continue;

                if (shield.retaliationStunSeconds > 0f) u.Stun(shield.retaliationStunSeconds);
                if (shield.retaliationEffectors != null && shield.retaliationEffectors.Length > 0)
                    Effector.EffectorAdd(castingPlayer, u, shield.retaliationEffectors);
            }
        }

        // ------------------------------------------------------------- 8. ОСЛЕПЛЕНИЕ --
        void ApplyBlind(int level, Unit target)
        {
            if (blind == null || !blind.enabled) return;

            float chance = LevelValue(blind.chance, level);
            float duration = LevelValue(blind.duration, level);
            if (chance > 0f && duration > 0f) BlindDebuff.Apply(target, chance, duration);
        }

        // ----------------------------------------------------------------- 9. ПРИЗЫВ --
        void ApplySummon(Unit castingUnit, int castingPlayer, int level)
        {
            MatchManager mm = MatchManager.Instance;
            if (mm == null) { Debug.LogWarning($"[{name}] Призыв: MatchManager.Instance == null — пропуск."); return; }

            int count = Mathf.RoundToInt(LevelValue(summon.count, level));
            float lifetime = LevelValue(summon.lifetime, level);

            switch (summon.mode)
            {
                case SkillSummonMode.FromCaster:
                    if (castingUnit == null) { Debug.LogWarning($"[{name}] Призыв от кастера: нет кастера — пропуск."); return; }
                    mm.SummonFromUnit(castingUnit, summon.prefab, count, lifetime,
                                      summon.obeyCommands, summon.command,
                                      summon.maxAlivePerCaster, summon.killSummonsOnOwnerDeath, summon.spawnSpread);
                    break;

                case SkillSummonMode.FixedSquadAtEnemyPoint:
                    mm.SummonFixedSquadAtEnemyPoint(castingPlayer, summon.prefab, count, summon.maxAlivePerPlayer,
                                                    summon.spawnSpread, summon.summonVFX,
                                                    summon.summonSound, summon.summonSoundVolume);
                    break;

                case SkillSummonMode.LastWave:
                    mm.SummonLastWave(castingPlayer, lifetime, summon.obeyCommands, summon.command,
                                      summon.summonVFX, summon.summonSound, summon.summonSoundVolume);
                    break;
            }
        }

        // -------------------------------------------------------------------- 10. ЗОНА --
        void ApplyGroundZone(Unit castingUnit, int castingPlayer, int level, Vector3 origin)
        {
            if (groundZone.zonePrefab == null)
            {
                Debug.LogWarning($"[{name}] Зона на земле: не задан префаб — каст пропущен.");
                return;
            }

            Vector3 center = origin;
            if (castingUnit != null && groundZone.forwardOffset != 0f)
                center += castingUnit.transform.forward * groundZone.forwardOffset;

            for (int i = 0; i < groundZone.zoneCount; i++)
            {
                Vector3 pos = center;
                if (groundZone.zoneCount > 1 && groundZone.spread > 0f)
                {
                    Vector2 offset = UnityEngine.Random.insideUnitCircle * groundZone.spread;
                    pos += new Vector3(offset.x, 0f, offset.y);
                }

                GameObject go = Instantiate(groundZone.zonePrefab, pos, Quaternion.identity);

                GroundDamageZone zone = go.GetComponent<GroundDamageZone>();
                if (zone != null) zone.SetOwner(castingPlayer);
                else Debug.LogWarning($"[{name}] На префабе зоны нет компонента GroundDamageZone — зона не будет действовать.");

                // Клиенты узнают о зоне фактом из серверного реестра: позиция уже с учётом разброса,
                // префаб клиент берёт из этого же ассета по id умения. Реестр сам разошлёт сообщение.
                if (MatchManager.Instance != null) MatchManager.Instance.RegisterGroundZone(go, id, level, pos);
            }
        }

        // -------------------------------------------------------- 11. СЕРВЕРНЫЙ СЕРВИС --
        void ApplyDelegate(Unit castingUnit, int castingPlayer, Vector3 origin)
        {
            MatchManager mm = MatchManager.Instance;
            if (mm == null) { Debug.LogWarning($"[{name}] Серверный сервис: MatchManager.Instance == null — пропуск."); return; }

            switch (delegateService.service)
            {
                case SkillServerService.MeteorStorm:
                    mm.StartMeteorStorm(castingUnit, castingPlayer,
                        delegateService.meteorDurationSeconds, delegateService.meteorsPerSecond,
                        delegateService.meteorImpactRadius,
                        delegateService.meteorPhysicalDamage, delegateService.meteorPhysicalDamageType,
                        delegateService.meteorFireDamage, delegateService.meteorFireDamageType,
                        delegateService.meteorStunSeconds, delegateService.meteorUseDensityTargeting,
                        delegateService.meteorTargetSelector, delegateService.meteorSplashSelector,
                        delegateService.meteorVFX, delegateService.meteorImpactSound, delegateService.meteorImpactVolume);
                    break;

                case SkillServerService.ResurrectFromGraves:
                {
                    int team = TeamIndexOfPlayer(castingPlayer);
                    if (team < 0)
                    {
                        Debug.LogWarning($"[{name}] Подъём павших: игрок {castingPlayer} не владеет командой — пропуск.");
                        return;
                    }
                    mm.ResurrectFromGraves(team, origin, delegateService.resurrectRadius, delegateService.resurrectCount,
                                           delegateService.resurrectHighestTierFirst, delegateService.resurrectTemporary,
                                           delegateService.resurrectLifetime, delegateService.resurrectObeyCommands);
                    break;
                }
            }
        }

        /// <summary>
        /// Индекс команды (0 = A, 1 = B) по игроку-владельцу; -1 — игрок не владеет ни одной командой.
        /// Считается через публичный MatchManager.Team(index).ownerPlayer — приватных полей менеджера не трогаем (правило 9).
        /// </summary>
        internal static int TeamIndexOfPlayer(int player)
        {
            MatchManager mm = MatchManager.Instance;
            if (mm == null) return -1;

            if (mm.Team(0) != null && mm.Team(0).ownerPlayer == player) return 0;
            if (mm.Team(1) != null && mm.Team(1).ownerPlayer == player) return 1;
            return -1;
        }
    }
}
