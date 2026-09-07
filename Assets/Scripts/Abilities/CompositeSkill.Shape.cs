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
    }

    public partial class CompositeSkill
    {
        // --------------------------------------------------------------- 12. ОБЛИК --
        /// <summary>
        /// Подмена облика через штатный <see cref="Unit.Polymorph"/>: ядро само ведёт таймер и вызовет
        /// <see cref="Deactivate"/> по истечении. Двойной полиморф ядро тоже обрабатывает — снимает предыдущий.
        /// </summary>
        void ApplyMorph(Unit castingUnit, int castingPlayer, int level, Unit target)
        {
            if (morph == null || !morph.enabled || morph.shapeUnit == null) return;
            if (target == null || target.dead) return;

            float time = LevelValue(morph.duration, level);
            if (time <= 0f) return;

            target.Idle();

            // [Interflow fix 2026-09-03 control-as-effectors] Контроль облика — служебное СОСТОЯНИЕ на срок
            // облика, а не прямая запись флага (решение Artsiom 03.09.2026). Прямая запись перестала работать:
            // флаги контроля выводятся ТОЛЬКО из списка состояний, и любое наложение на этого юнита —
            // горение, аура замедления — стёрло бы её при пересчёте.
            // Две принятые смены поведения: (1) иммунитет к контролю теперь отбивает немоту и безоружие облика
            // (наложение идёт через приёмник); (2) при досрочном снятии облика (двойной полиморф) контроль
            // доживает свой срок — это и есть модель «до истечения последнего наложения».
            if (morph.disarmWhileMorphed) target.Disarm(time, castingUnit, castingPlayer);
            if (morph.muteWhileMorphed) target.Mute(time, castingUnit, castingPlayer);

            // Статы меняем ДО смены облика: дальность влияет на настройку снаряда (порядок из Transformation).
            AbilityPassiveEffects effects = PassiveEffectsAt(level);
            if (effects != null) effects.AddEffect(target);

            target.Polymorph(this, level, time, morph.shapeUnit);
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

            castingUnit.RestoreRenderers();
        }

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
        /// <param name="baseDamageToTarget">Урон, ЗАПИСАННЫЙ в блоке урона для основной цели (до брони) —
        /// из него берётся доля на лечение. Ноль, когда блок урона выключен или урон уносит снаряд.</param>
        void ApplySecondary(int castingPlayer, int level, Unit target, float baseDamageToTarget)
        {
            if (secondary == null || !secondary.enabled) return;
            if (target == null || secondary.radius <= 0f) return;

            Vector3 p = target.transform.position;
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

                // Лимит считаем по ФАКТИЧЕСКИ задетым, а не по перебранным: иначе отсеянные
                // по роли и по «кто бьёт цель» съедали бы лимит, ничего не получив.
                taken++;
                if (secondary.maxSecondaryTargets > 0 && taken >= secondary.maxSecondaryTargets) return;
            }
        }
    }
}
