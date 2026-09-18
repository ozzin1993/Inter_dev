using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ============ КОНСТРУКТОР СКИЛЛА: ОБЛИК, ВЛАДЕЛЕЦ, ВТОРИЧНЫЕ ЦЕЛИ (правило 22 — партиал по фиче) ==
    // Блоки, закрывающие классы Hex/Transformation (облик), Wololo (владелец) и HolyFire (вторичные цели).
    // Всё через штатные механизмы ядра (правило 2): Unit.Polymorph, Unit.SetOwnership, Utils.GetUnitsInRadius.

    /// <summary>12. Подмена облика: цель принимает вид другого юнита на время.</summary>
    [System.Serializable]
    public class SkillMorphBlock
    {
        [Tooltip("Включить блок: цель принимает чужой облик на время.")]
        public bool enabled;

        [Tooltip("Юнит, чей ВНЕШНИЙ ВИД принимает цель. Статы цели не меняются — меняются только рендереры.")]
        public Unit shapeUnit;

        [Tooltip("Сколько секунд держится облик, по уровням.")]
        public float[] duration;

        [Tooltip("Запретить цели атаковать на время облика (как «безоружие»).")]
        public bool disarmWhileMorphed = true;

        [Tooltip("Запретить цели кастовать на время облика (как «немота»).")]
        public bool muteWhileMorphed = true;

        [Tooltip("Пассивные изменения статов на время облика (урон, ХП, скорость). Пусто — статы не трогать. " +
                 "Берётся запись по уровню умения.")]
        public AbilityPassiveEffects[] passiveEffects;

        // [Interflow 2026-09-18, решения Artsiom 53 и 55] Облик НА СЕБЯ — флаг этого блока, отдельного
        // блока «облик на себя» не заводится. При флаге блок перестаёт быть «по каждой цели»: он
        // применяется один раз за каст, рядом с блоком 18, а цикл по целям его не вызывает вовсе.
        [Tooltip("ОБЛИК НА СЕБЯ: блок применяется ОДИН раз за каст к САМОМУ КАСТЕРУ, а цели не трогаются вовсе. " +
                 "Нужно превращению героя (прыжок и смена облика одним умением). " +
                 "ВЫКЛ — блок работает как раньше, по каждой цели.")]
        public bool applyToCaster;

        [Tooltip("СПОСОБ АТАКИ ОТ ЗАГОТОВКИ: на время облика носитель получает ближний бой, тип атаки, " +
                 "дальность атаки и снаряд той заготовки, чей вид принял. Возврат — парный, при снятии облика. " +
                 "Внешность и анимационные числа копируются ядром и без этой галки; " +
                 "ближний бой, тип атаки и дальность — нет.\n\n" +
                 "ВНИМАНИЕ: дальность атаки получает ДВА источника — эту копию и множитель дальности " +
                 "в пассивных изменениях статов выше. Вместе их задавать нельзя: копия перезапишет множитель.")]
        public bool copyAttackMode;

        [Tooltip("Визуал события «облик надет»: играется на том, кто сменил облик. Пусто — без визуала.")]
        public EventPresentation onPresentation = new EventPresentation();

        [Tooltip("Визуал события «облик снят»: играется на том, у кого облик кончился. Пусто — без визуала.")]
        public EventPresentation offPresentation = new EventPresentation();
    }

    /// <summary>13. Смена владельца: цель переходит под контроль владельца кастера.</summary>
    [System.Serializable]
    public class SkillOwnershipBlock
    {
        [Tooltip("Включить блок: цель переходит под контроль владельца кастера навсегда.")]
        public bool enabled;

        [Tooltip("Звук перехода. Пусто — без звука.")]
        public AudioClip sound;

        [Tooltip("Громкость звука перехода, 0..1.")]
        [Range(0f, 1f)]
        public float soundVolume = 1f;
    }

    /// <summary>
    /// 14. Вторичные цели вокруг основной: своя выборка от каждой основной цели.
    /// Закрывает связку «ударить врага и вылечить тех, кто его бьёт» (класс HolyFire).
    /// </summary>
    [System.Serializable]
    public class SkillSecondaryBlock
    {
        [Tooltip("Включить блок: вокруг каждой основной цели ищется свой набор юнитов.")]
        public bool enabled;

        [Tooltip("Радиус поиска ВОКРУГ ОСНОВНОЙ ЦЕЛИ, метры.")]
        public float radius = 6f;

        [Tooltip("Кого искать: принадлежность и тип. Отдельный селектор — основная цель обычно враг, а вторичные свои.")]
        public UnitSelector selector;

        [Tooltip("Боевые роли вторичных целей. Пусто — роль не ограничивает.")]
        public Unit.UnitCategory[] categories;

        [Tooltip("Брать только тех, кто СЕЙЧАС атакует основную цель (Unit.target). " +
                 "ВЫКЛ — всех подходящих в радиусе.")]
        public bool onlyAttackersOfTarget = true;

        [Tooltip("Лечение вторичных целей числом, по уровням.")]
        public float[] healFlat;

        [Tooltip("Добавка к лечению как ДОЛЯ от урона, ЗАПИСАННОГО в блоке урона для основной цели — " +
                 "до брони, сопротивлений и щитов. Сколько цель потеряла на самом деле, здесь не учитывается. " +
                 "0.5 — каждый вторичный получает половину этого числа сверх лечения выше. 0 — лечить только числом. " +
                 "Если урон уносит снаряд, в момент каста его ещё нет — доля будет нулевой.")]
        public float healPercentOfBaseDamage;

        [Tooltip("Максимум вторичных целей за каст. 0 — без ограничения. " +
                 "Когда подходящих больше лимита, берутся первые найденные.")]
        [Min(0)]
        public int maxSecondaryTargets;

        [Tooltip("Состояния на вторичные цели. Пусто — только лечение.")]
        public Effector[] effectors;

        [Tooltip("Визуал события «блок вторичных целей сработал»: играется ОДИН раз за каст, ДО перебора целей. " +
                 "Носитель — КАСТЕР умения, точка — позиция основной цели. Не зависит от того, задет ли хоть кто-то. " +
                 "Сюда кладётся всё, что должно сыграть на кастере один раз: замах, звук, стейт анимации. " +
                 "Пусто — без визуала.")]
        public EventPresentation castPresentation = new EventPresentation();

        [Tooltip("Визуал события «попадание по вторичной цели»: играется НА КАЖДОЙ задетой вторичной цели. " +
                 "Носитель И точка — САМА задетая цель, а не кастер: поля «у носителя» играют на ней. " +
                 "Пусто — без визуала.")]
        public EventPresentation presentation = new EventPresentation();
    }

    public partial class CompositeSkill
    {
        // --------------------------------------------------------------- 12. ОБЛИК --
        /// <summary>
        /// Облик ПО ЦЕЛИ — вызов из цикла по целям. При включённом «облике на себя» (решение Artsiom 53)
        /// цели не трогаются вовсе: блок применяется один раз за каст в <see cref="ApplyMorphToCaster"/>.
        /// </summary>
        void ApplyMorph(Unit castingUnit, int castingPlayer, int level, Unit target)
        {
            if (morph == null || !morph.enabled) return;
            if (morph.applyToCaster) return;   // блок перестал быть «по каждой цели» (решение 53)

            ApplyMorphTo(target, castingUnit, castingPlayer, level);
        }

        /// <summary>
        /// Облик НА КАСТЕРА — вызов ОДИН раз за каст, рядом с блоком 18 (решение Artsiom 53).
        /// Зовётся из <c>ApplyEffects</c> до цикла по целям: превращение героя и его прыжок — одно умение,
        /// и облик обязан встать до того, как заработают блоки по целям (у них другие статы и дальность).
        /// </summary>
        void ApplyMorphToCaster(Unit castingUnit, int castingPlayer, int level)
        {
            if (morph == null || !morph.enabled || !morph.applyToCaster) return;

            ApplyMorphTo(castingUnit, castingUnit, castingPlayer, level);
        }

        /// <summary>
        /// Подмена облика через штатный <see cref="Unit.Polymorph"/>: ядро само ведёт таймер и вызовет
        /// <see cref="Deactivate"/> по истечении. Двойной полиморф ядро тоже обрабатывает — снимает предыдущий.
        /// </summary>
        /// <param name="carrier">Кто принимает облик: цель или сам кастер.</param>
        /// <param name="castingUnit">Кастер — источник наложений контроля. Может совпадать с носителем.</param>
        void ApplyMorphTo(Unit carrier, Unit castingUnit, int castingPlayer, int level)
        {
            if (morph.shapeUnit == null) return;
            if (carrier == null || carrier.dead) return;

            float time = LevelValue(morph.duration, level);
            if (time <= 0f) return;

            carrier.Idle();

            // [Interflow fix 2026-09-03 control-as-effectors] Контроль облика — служебное СОСТОЯНИЕ на срок
            // облика, а не прямая запись флага (решение Artsiom 03.09.2026). Прямая запись перестала работать:
            // флаги контроля выводятся ТОЛЬКО из списка состояний, и любое наложение на этого юнита —
            // горение, аура замедления — стёрло бы её при пересчёте.
            // Две принятые смены поведения: (1) иммунитет к контролю теперь отбивает немоту и безоружие облика
            // (наложение идёт через приёмник); (2) при досрочном снятии облика (двойной полиморф) контроль
            // доживает свой срок — это и есть модель «до истечения последнего наложения».
            if (morph.disarmWhileMorphed) carrier.Disarm(time, castingUnit, castingPlayer);
            if (morph.muteWhileMorphed) carrier.Mute(time, castingUnit, castingPlayer);

            // Статы меняем ДО смены облика: дальность влияет на настройку снаряда (порядок из Transformation).
            AbilityPassiveEffects effects = PassiveEffectsAt(level);
            if (effects != null) effects.AddEffect(carrier);

            // [Interflow 2026-09-18, решение Artsiom 55] Способ атаки — ТОЖЕ до смены облика, тем же
            // порядком и по той же причине: дальность читает настройка снаряда внутри ReplaceRenderers.
            if (morph.copyAttackMode) CopyAttackMode(carrier, morph.shapeUnit);

            carrier.Polymorph(this, level, time, morph.shapeUnit);

            // [Interflow 2026-09-18, решение Artsiom 54] Факт «облик надет»: по нему чистый клиент делает
            // ТОЛЬКО подмену внешности. Показ ПОСЛЕ смены облика на сервере — набор описывает уже надетый вид.
            EmitEventPresentation(this, (int)AbilityEventCode.SkillMorphOn, morph.onPresentation,
                                  carrier, level, carrier, carrier.transform.position);
        }

        /// <summary>
        /// Конец облика — ядро зовёт это само по таймеру полиморфа (Unit.PolymorphUpdate).
        /// Возвращаем флаги и статы, облик снимает штатный RestoreRenderers.
        /// </summary>
        public override void Deactivate(Unit castingUnit, int castingPlayer, int level)
        {
            if (castingUnit == null) return;
            if (morph == null || !morph.enabled) return;

            // [Interflow fix 2026-09-03 control-as-effectors] Снимать контроль руками больше не нужно:
            // служебное состояние истекает по своему сроку, равному сроку облика.

            AbilityPassiveEffects effects = PassiveEffectsAt(level);
            if (effects != null) effects.RemoveEffect(castingUnit);

            // [Interflow 2026-09-18, решение Artsiom 55] Возврат способа атаки — ПАРНЫЙ к подмене
            // и ДО RestoreRenderers, зеркально порядку наложения: RestoreRenderers сам читает тип атаки,
            // решая, возвращать ли снаряд заготовки (Units/Unit.Visuals.cs).
            RestoreAttackMode(castingUnit);

            castingUnit.RestoreRenderers();

            // [Interflow 2026-09-18, решение Artsiom 54] Факт «облик снят» — парный к «надет».
            // Потерянный факт оставит модель превращённой до конца матча (цена принята решением 54).
            EmitEventPresentation(this, (int)AbilityEventCode.SkillMorphOff, morph.offPresentation,
                                  castingUnit, level, castingUnit, castingUnit.transform.position);
        }

        // ------------------------------------------------- СПОСОБ АТАКИ НА ВРЕМЯ ОБЛИКА --
        // [Interflow 2026-09-18, решение Artsiom 55] Четыре поля, которых не копирует ядро:
        // ReplaceRenderers берёт внешность, звуки, анимационные числа и снаряд (и снаряд — только при
        // совпадении типа атаки), но НЕ melee, НЕ attackType, НЕ attackRange.
        // Правка самого ReplaceRenderers отвергнута: это ядро, им пользуются Hex, Transformation
        // и сохранение игры (currentMainShape). Подмена делается СНАРУЖИ и парой «надел — снял».

        /// <summary>Что было у носителя до подмены — чтобы вернуть ровно это, а не значения заготовки.</summary>
        struct AttackModeSnapshot
        {
            public bool melee;
            public AttackType attackType;
            public float attackRange;
            public GameObject projectileGO;
        }

        // Пер-юнит хранилище снимков. Носителей одновременно может быть несколько (облик на цели —
        // блок «по каждой цели»), поэтому словарь, а не одно поле. Ассет живёт между матчами, поэтому
        // хранилище ОБЯЗАНО чиститься на старте матча — это делает ResetRuntimeState (принцип Artsiom
        // 17.09.2026: каждый матч изолирован).
        Dictionary<Unit, AttackModeSnapshot> attackModeSnapshots;

        void CopyAttackMode(Unit carrier, Unit shape)
        {
            if (carrier == null || shape == null) return;

            if (attackModeSnapshots == null) attackModeSnapshots = new Dictionary<Unit, AttackModeSnapshot>();

            // Повторное превращение того же носителя (двойной полиморф): снимок НЕ перезаписываем —
            // иначе «исходным» стал бы способ атаки предыдущей заготовки, и возврат вернул бы чужое.
            if (!attackModeSnapshots.ContainsKey(carrier))
                attackModeSnapshots[carrier] = new AttackModeSnapshot
                {
                    melee = carrier.melee,
                    attackType = carrier.attackType,
                    attackRange = carrier.attackRange,
                    projectileGO = carrier.projectileGO
                };

            carrier.melee = shape.melee;
            carrier.attackType = shape.attackType;
            carrier.attackRange = shape.attackRange;
            carrier.projectileGO = shape.projectileGO;
        }

        void RestoreAttackMode(Unit carrier)
        {
            if (carrier == null || attackModeSnapshots == null) return;
            if (!attackModeSnapshots.TryGetValue(carrier, out AttackModeSnapshot was)) return;

            carrier.melee = was.melee;
            carrier.attackType = was.attackType;
            carrier.attackRange = was.attackRange;
            carrier.projectileGO = was.projectileGO;

            attackModeSnapshots.Remove(carrier);
        }

        /// <summary>Сброс хранилища снимков — зовётся сбросом рантайма ассета на старте матча.</summary>
        void ClearAttackModeSnapshots() => attackModeSnapshots = null;

        /// <summary>Запись пассивных эффектов облика по уровню — общая выборка (Б8). Нет записи — статы не трогаем.</summary>
        AbilityPassiveEffects PassiveEffectsAt(int level)
            => morph == null ? null : LevelItem(morph.passiveEffects, level);

        // ------------------------------------------------------------ 13. ВЛАДЕЛЕЦ --
        /// <summary>
        /// Переподчинение цели штатным <see cref="Unit.SetOwnership"/>. Порядок как в ядре (Wololo):
        /// сменить владельца, сбросить текущее действие и обнулить ссылку на цель.
        /// </summary>
        void ApplyOwnership(Unit castingUnit, Unit target)
        {
            if (ownership == null || !ownership.enabled) return;
            if (castingUnit == null || target == null || target.dead) return;
            if (target.owner == castingUnit.owner) return;                 // уже наш — ничего не делаем

            target.SetOwnership(castingUnit.owner);
            target.Idle();
            target.OnReferenceChange?.Invoke(null);

            if (ownership.sound != null)
                PlaySound(ownership.sound, target.transform, ownership.soundVolume);
        }

        // ----------------------------------------------------- 14. ВТОРИЧНЫЕ ЦЕЛИ --
        /// <summary>
        /// Выборка вокруг основной цели со своими эффектами. Связка «кто бьёт эту цель» берётся
        /// из штатного <c>Unit.target</c> — отдельного реестра агрессии в ядре нет (как в HolyFire).
        /// </summary>
        /// <param name="castingUnit">Кастер умения: НОСИТЕЛЬ набора «каст» (одного показа за заход блока).
        /// Носителем набора «цель» он НЕ является — там носитель сама задетая цель (решение Artsiom 33
        /// от 17.09.2026). Может быть null (каст без юнита) — тогда визуал у носителя просто не сыграет.</param>
        /// <param name="baseDamageToTarget">Урон, ЗАПИСАННЫЙ в блоке урона для основной цели (до брони) —
        /// из него берётся доля на лечение. Ноль, когда блок урона выключен или урон уносит снаряд.</param>
        void ApplySecondary(Unit castingUnit, int castingPlayer, int level, Unit target, float baseDamageToTarget)
        {
            if (secondary == null || !secondary.enabled) return;
            if (target == null || secondary.radius <= 0f) return;

            Vector3 p = target.transform.position;

            // [Interflow 2026-09-17] Набор «КАСТ» (решение Artsiom 33 от 17.09.2026): ОДИН показ за заход
            // блока, ДО перебора целей. Носитель — кастер, точка — позиция основной цели: та же точка,
            // вокруг которой идёт выборка ниже, другой точки у блока нет. От результата выборки показ
            // не зависит — это визуал самого блока, а не попадания. Пустой набор сообщения не порождает.
            EmitEventPresentation(this, (int)AbilityEventCode.SkillSecondaryCast, secondary.castPresentation,
                                  castingUnit, level, target, p);

            Unit[] found = Utils.GetUnitsInRadius(new Vector2(p.x, p.z), secondary.radius,
                                                  castingPlayer, secondary.selector, -1, target);
            if (found == null) return;

            float heal = LevelValue(secondary.healFlat, level)
                       + baseDamageToTarget * secondary.healPercentOfBaseDamage;

            int taken = 0;
            for (int i = 0; i < found.Length; i++)
            {
                Unit u = found[i];
                if (u == null || u.dead) continue;
                if (!CategoryAllowed(u, secondary.categories)) continue;
                if (secondary.onlyAttackersOfTarget && u.target != target) continue;

                if (heal > 0f) u.ChangeHP(heal);

                if (secondary.effectors != null && secondary.effectors.Length > 0)
                    Effector.EffectorAdd(castingPlayer, u, secondary.effectors);

                // [Interflow 2026-09-17] Набор «ЦЕЛЬ» (решения Artsiom 31 и 33 от 17.09.2026): показ НА КАЖДОЙ
                // задетой цели, НОСИТЕЛЬ — САМА ЭТА ЦЕЛЬ. Кастер носителем быть перестал: его поля
                // («у носителя», стейт анимации) повторялись бы на нём по числу задетых целей.
                // Пустой набор сообщения не порождает.
                EmitEventPresentation(this, (int)AbilityEventCode.SkillSecondaryHit, secondary.presentation,
                                      u, level, u, u.transform.position);

                // Лимит считаем по ФАКТИЧЕСКИ задетым, а не по перебранным: иначе отсеянные
                // по роли и по «кто бьёт цель» съедали бы лимит, ничего не получив.
                taken++;
                if (secondary.maxSecondaryTargets > 0 && taken >= secondary.maxSecondaryTargets) return;
            }
        }
    }
}
