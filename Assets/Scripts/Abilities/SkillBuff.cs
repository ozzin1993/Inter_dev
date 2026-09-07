using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// ЕДИНЫЙ длящийся баф на юните — замена четырёх почти одинаковых скелетов
    /// (огненный плащ, благословение небес, железный приговор, жертвенный костёр).
    /// Все параметры читает из блока «длящийся баф» того скилла, который его повесил.
    ///
    /// Что умеет: аура урона вокруг носителя, лечение во времени, самосожжение,
    /// множитель входящего урона, иммунитет к контролю, сопротивления и слабости к категориям
    /// состояний (с 2026-09-03) и детонацию при смерти носителя.
    ///
    /// ТОЛЬКО ГЕЙМПЛЕЙ, БЕЗ ВИЗУАЛА. Компонент существует исключительно на сервере (решение Artsiom
    /// 2026-08-01 о строгом разделении). VFX бафа показывает <see cref="SkillVisualStatus"/>: сервер шлёт
    /// клиентам отдельное сообщение «покажи вот это», а сам ничего не рисует сверх обычного локального зрителя.
    ///
    /// ОДИН КОМПОНЕНТ = ОДИН СКИЛЛ-ИСТОЧНИК. Разные скиллы вешают разные компоненты и живут независимо —
    /// ровно так же, как раньше сосуществовали четыре отдельных типа бафов. Повторное наложение ТОГО ЖЕ
    /// скилла не стакается, а продлевает остаток времени (берётся больший).
    /// </summary>
    public class SkillBuff : MonoBehaviour
    {
        Unit unit;                 // носитель бафа
        CompositeSkill source;     // скилл-источник: единственный источник параметров (правило 5)
        Unit caster;               // кто наложил — он же наносит урон детонации
        int level;                 // уровень скилла на момент последнего наложения
        int registeredLevel = -1;  // под каким уровнем зарегистрирован колбэк входящего урона

        float remaining;
        bool subscribed;
        bool dieHooked;
        bool exploded;             // детонация одноразова
        bool damageCallbackAdded;
        bool immunityAdded;
        bool resistancesAdded;     // вклады сопротивлений выданы носителю (ключ — этот компонент)
        bool cleanedUp;            // компонент уже снят; Destroy отложен до конца кадра

        /// <summary>
        /// Повесить баф на юнит или продлить существующий ОТ ТОГО ЖЕ скилла.
        /// Только сервер: баф меняет состояние мира (правило 6). Ноль/отрицательная длительность — не вешается.
        /// </summary>
        public static void Apply(Unit target, CompositeSkill source, int level, Unit caster)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (target == null || target.dead || source == null || source.buff == null || !source.buff.enabled) return;

            float duration = InterflowAbility.LevelValue(source.buff.duration, level);
            if (duration <= 0f) return;

            // Ищем ЖИВОЙ компонент именно от этого скилла: помеченный на снятие ещё висит на объекте
            // до конца кадра, и переиспользовать его нельзя — его OnDestroy тут же всё снимет.
            SkillBuff buff = null;
            SkillBuff[] existing = target.GetComponents<SkillBuff>();
            for (int i = 0; i < existing.Length; i++)
                if (existing[i] != null && !existing[i].cleanedUp && existing[i].source == source) { buff = existing[i]; break; }

            if (buff == null) buff = target.gameObject.AddComponent<SkillBuff>();
            buff.Init(target, source, level, caster, duration);
        }

        void Init(Unit target, CompositeSkill skill, int skillLevel, Unit by, float duration)
        {
            unit = target;
            source = skill;
            level = skillLevel;
            caster = by;
            remaining = Mathf.Max(remaining, duration); // продление, а не суммирование

            SkillBuffBlock cfg = source.buff;

            // Множитель входящего урона. Запись помечается парой (скилл, уровень) — по ней же и снимается,
            // чтобы не задеть чужие колбэки на этом же юните.
            float multiplier = InterflowAbility.LevelValue(cfg.incomingDamageMultiplier, level, 1f);
            bool wantCallback = multiplier != 1f && multiplier >= 0f;

            // Уровень мог измениться при повторном наложении — тогда перерегистрируем под новым ключом,
            // иначе старая запись осталась бы в списке жертвы навсегда.
            if (damageCallbackAdded && registeredLevel != level)
            {
                InterflowAbility.CallbackRemove(unit.OnBeforeGetDamageCallbacks, source, registeredLevel);
                damageCallbackAdded = false;
            }

            if (!damageCallbackAdded && wantCallback)
            {
                InterflowAbility.CallbackAdd(unit.OnBeforeGetDamageCallbacks, source, level, ModifyIncomingDamage);
                damageCallbackAdded = true;
                registeredLevel = level;
            }

            // Иммунитет к контролю — рефкаунт-маркер, чтобы несколько источников не гасили друг друга.
            if (!immunityAdded && cfg.controlImmunity)
            {
                ControlImmunity ci = unit.GetComponent<ControlImmunity>();
                if (ci == null) ci = unit.gameObject.AddComponent<ControlImmunity>();
                ci.Add();
                immunityAdded = true;
            }

            // [Interflow fix 2026-09-03 status-resistances] Сопротивления и слабости на время бафа — вклады
            // в носитель UnitResistances под ключом ЭТОГО компонента (один компонент = один скилл-источник
            // на юните, поэтому ключ однозначен и не задевает вклады пассивок и других бафов). Строки бафа
            // не зависят от уровня, поэтому при продлении перевыдавать нечего — флаг, как у иммунитета.
            if (!resistancesAdded && cfg.resistances != null && cfg.resistances.Length > 0)
            {
                UnitResistances holder = unit.GetComponent<UnitResistances>();
                if (holder == null) holder = unit.gameObject.AddComponent<UnitResistances>();

                for (int i = 0; i < cfg.resistances.Length; i++)
                {
                    ResistanceEntry entry = cfg.resistances[i];
                    if (entry == null) continue;

                    holder.Add(this, entry.category, entry.value);   // категорию «Нет» носитель отбрасывает сам
                }

                resistancesAdded = true;
            }

            // Детонация при гибели носителя — штатное событие Unit.OnDie.
            if (!dieHooked && cfg.detonateOnDeath)
            {
                unit.OnDie += HandleDie;
                dieHooked = true;
            }

            if (!subscribed && GameManager.Instance != null)
            {
                GameManager.Instance.Tick += OnTick;
                subscribed = true;
            }
        }

        // Множитель входящего урона. Действует на любой урон (прямая атака или нет).
        float ModifyIncomingDamage(Unit u, int lvl, float dmg, bool directAttack)
        {
            if (source == null || source.buff == null) return dmg;

            return dmg * InterflowAbility.LevelValue(source.buff.incomingDamageMultiplier, level, 1f);
        }

        void OnTick()
        {
            if (unit == null || unit.dead) { Cleanup(); return; }
            if (GameManager.Instance == null) return;

            // Источник пропал (ассет выгружен) — доживать смысла нет, снимаемся сразу,
            // иначе баф завис бы навсегда: таймер стоит ниже по коду.
            if (source == null || source.buff == null) { Cleanup(); return; }

            float dt = GameManager.Instance.currentDeltaTime;
            SkillBuffBlock cfg = source.buff;

            // Аура урона вокруг носителя (наносит сам носитель — как у огненного плаща).
            float dps = InterflowAbility.LevelValue(cfg.auraDamagePerSecond, level);
            if (dps > 0f && cfg.auraDamageType != null)
            {
                float aura = InterflowAbility.LevelValue(cfg.auraRadius, level);
                Vector3 p = unit.transform.position;
                Unit[] targets = Utils.GetUnitsInRadius(new Vector2(p.x, p.z), unit.unitRadius + aura,
                                                        unit.owner, cfg.auraSelector, -1, unit);
                if (targets != null)
                    for (int i = 0; i < targets.Length; i++)
                        if (targets[i] != null && !targets[i].dead)
                            unit.DealDamage(targets[i], dps * dt, cfg.auraDamageType, false, Vector3.zero, source);
            }

            // Лечение во времени: числом и/или процентами от максимума.
            float healFlat = InterflowAbility.LevelValue(cfg.healPerSecond, level);
            float healPercent = InterflowAbility.LevelValue(cfg.healPercentOfMaxPerSecond, level);
            float heal = healFlat + healPercent / 100f * unit.maxHealth; // проценты целым числом: 2 = 2%/с
            if (heal > 0f) unit.ChangeHP(heal * dt);

            // Самосожжение носителя. Через PayHealth: штатный ChangeHP только зажимает здоровье в ноль
            // и не убивает — иначе сгоревший носитель оставался ходячим с нулём ХП и регенился обратно.
            // Кастер передаётся как убийца: баф можно повесить и на врага — тогда это урон-во-времени,
            // и убийство должно засчитаться ему (решение Artsiom 2026-08-02). Самосожжение на себе
            // наград не даёт: PayHealth сам отсекает случай «убийца равен жертве». Та же атрибуция,
            // что у детонации этого же бафа — урон взрыва тоже идёт от `caster`.
            float burn = InterflowAbility.LevelValue(cfg.selfBurnPerSecond, level);
            if (burn > 0f) InterflowAbility.PayHealth(unit, burn * dt, caster);

            remaining -= dt;
            if (remaining <= 0f) Cleanup();
        }

        // Гибель носителя под бафом → детонация. Одноразово.
        void HandleDie(Unit u, int playerThatKills, Unit unitThatKills, bool rewards)
        {
            if (!exploded && unit != null && source != null && source.buff != null)
            {
                SkillBuffBlock cfg = source.buff;
                float damage = InterflowAbility.LevelValue(cfg.detonationDamage, level);
                float radius = InterflowAbility.LevelValue(cfg.detonationRadius, level);

                if (cfg.detonateOnDeath && damage > 0f && radius > 0f && cfg.detonationDamageType != null)
                {
                    exploded = true;
                    Vector3 p = unit.transform.position;
                    Unit dealer = caster != null ? caster : unit;

                    // allyDamage = 0 → группа своих в хелпере пропускается, взрыв бьёт только по detonationSelector.
                    SkvernaExplosion.Detonate(dealer, new Vector2(p.x, p.z), radius,
                                              damage, cfg.detonationDamageType, cfg.detonationSelector,
                                              0f, null, default(UnitSelector), unit, source);
                }
            }

            Cleanup();
        }

        void Cleanup()
        {
            if (cleanedUp) return;
            cleanedUp = true;

            RemoveStateEffects();

            if (subscribed && GameManager.Instance != null) GameManager.Instance.Tick -= OnTick;
            subscribed = false;

            Destroy(this);
        }

        // Снятие всего, что было добавлено носителю. Идемпотентно (флаги).
        void RemoveStateEffects()
        {
            if (unit != null)
            {
                if (damageCallbackAdded) InterflowAbility.CallbackRemove(unit.OnBeforeGetDamageCallbacks, source, registeredLevel);

                if (immunityAdded)
                {
                    ControlImmunity ci = unit.GetComponent<ControlImmunity>();
                    if (ci != null) ci.Remove();
                }

                if (resistancesAdded)
                {
                    UnitResistances holder = unit.GetComponent<UnitResistances>();
                    if (holder != null) holder.Remove(this);   // ровно выданное этим бафом, разом
                }

                if (dieHooked) unit.OnDie -= HandleDie;
            }

            damageCallbackAdded = false;
            immunityAdded = false;
            resistancesAdded = false;
            dieHooked = false;
        }

        void OnDestroy()
        {
            // Страховка, если компонент уничтожен мимо Cleanup (снос объекта, смена сцены).
            cleanedUp = true;
            RemoveStateEffects();

            if (subscribed && GameManager.Instance != null) GameManager.Instance.Tick -= OnTick;
            subscribed = false;
        }
    }
}
