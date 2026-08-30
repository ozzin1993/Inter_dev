using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Приёмник юнита — единственная дверь внутрь юнита (схема «пакет и приёмник», §1, §4).
    /// Компонент на юните: принимает пакет и исполняет цепочку обработки.
    ///
    /// НУЛЕВОЙ ШАГ СХЕМЫ (§16.2): сюда переехало тело <c>Unit.GetDamage</c> ПОСЛЕ блока провокации —
    /// один в один, без единого изменения чисел и порядка вызовов. Всё, что ниже выглядит спорным
    /// (щит вычитается до брони, из подписок берётся не строго наименьшее, объём щита тратится даже
    /// когда его результат не выбран, <c>isInvulnerable</c> не проверяется), — сегодняшнее поведение.
    /// Оно сохранено намеренно: переезд идёт отдельно от смены баланса (§12 схемы).
    ///
    /// Смысл переезда: порядок обработки урона теперь задан в ОДНОМ месте и действует для всех
    /// источников сразу, включая те, что пакет собирать не научатся (эффекторы, ауры, зоны, реакции).
    ///
    /// Клиентского запрета здесь НЕТ и на этом шаге быть не должно: сегодня <c>GetDamage</c> считается
    /// на любом пире, и добавление запрета сменило бы поведение (у клиента пропала бы анимация
    /// получения удара). Вопрос серверного запрета — отдельное решение после разведки §15.1 схемы.
    ///
    /// ШАГ 1 СХЕМЫ (контроль): сюда же переехало РЕШЕНИЕ по оглушению, немоте и обезоруживанию —
    /// проверки состояния и иммунитет. Исполнение осталось в <c>Unit</c> (<c>StunApply</c>,
    /// <c>MuteApply</c>, <c>DisarmApply</c>): тела контроля дёргают приватную начинку юнита
    /// (<c>MakeAgent</c>, <c>EndActiveAbility</c>, <c>AttackStop</c>, <c>Idle</c>, таймеры на <c>Tick</c>),
    /// и тащить её в приёмник значило бы открывать приватное (правило 9).
    ///
    /// Настроек у приёмника на этом шаге нет, поэтому нет и сериализуемых полей.
    /// </summary>
    public partial class UnitReceiver : MonoBehaviour
    {
        /// <summary>
        /// Свой юнит. Кешируется один раз при навешивании (<see cref="Init"/>), а не ищется на каждый
        /// удар: руководство Unity прямо требует не звать поиск компонента в горячем пути.
        /// </summary>
        Unit unit;

        /// <summary>
        /// Привязать приёмник к своему юниту. Зовётся из <c>Unit.GetDamage</c> в момент навешивания.
        /// Повторный вызов безвреден — кладётся та же ссылка (образец: <c>AbsorbShield.Init</c>).
        /// </summary>
        public void Init(Unit owner)
        {
            unit = owner;
        }

        /// <summary>
        /// Принять пакет урона. Возвращает «юнит погиб от этого урона»;
        /// <paramref name="damageDealt"/> — сколько здоровья реально снято.
        /// </summary>
        public bool Receive(in DamagePacket p, out float damageDealt)
        {
            float amount = p.amount;

            // For evasion or damage reduction on chance skills
            // Lowest acquired damage is selected

            // [Interflow fix 2026-07-24 combat-hub] Модификаторы входящего урона, которые знают АТАКУЮЩЕГО и ТИП урона
            // (промах ослеплённого, уязвимость к типу урона, снижение урона). Штатный колбэк ниже не передаёт
            // ни атакующего, ни тип. Считаем ДО штатных колбэков, чтобы промах не «съедал» поглощающий щит,
            // а щит поглощал уже итоговую величину. Логика целиком в нашем InterflowCombat.cs.
            amount = InterflowCombat.ModifyIncomingDamage(unit, p.attackingUnit, p.damageType, amount, p.directAttack);

            // Перебор подписок скопирован дословно, включая вторую ветку: она допускает РОСТ урона,
            // когда снижения не было. Это не «взять наименьшее» — упрощать нельзя, поведение изменится.
            // Каждый подписчик получает одно и то же входное число, а не результат предыдущего.
            float finalDamage = amount;
            foreach (var c in unit.OnBeforeGetDamageCallbacks)
            {
                float damageChanged = c.Callback(unit, c.Level, amount, p.directAttack);
                if (damageChanged < finalDamage) finalDamage = damageChanged;
                else if (finalDamage >= amount && damageChanged > finalDamage) finalDamage = damageChanged;
            }
            amount = finalDamage;

            // [Interflow fix 2026-07-24 combat-hub] Пробитие брони: атакующий игнорирует долю защиты цели.
            // Штатной точки для этого нет, а хук жертвы не знает, кто бьёт.
            float effectiveArmor = InterflowCombat.EffectiveArmor(unit, p.attackingUnit, unit.armor);

            // Final damage amount based on damage and armor type
            damageDealt = amount * GameManager.Instance.damageToArmor[unit.armorType.index * GameManager.Instance.DTAWidth + p.damageType.index] * (1 - ((0.06f * effectiveArmor) / (1 + 0.06f * effectiveArmor)));

            // Checks and Get damage
            if (damageDealt < 0) damageDealt = 0;

            if (damageDealt > unit.health) damageDealt = unit.health;

            bool died = unit.ChangeHP(-damageDealt);
            if (died) unit.Die(p.attackingPlayer, p.attackingUnit);
            else if (unit.hasHitAnim && p.directAttack && unit.FoWVisible) unit.animator.CrossFade("hit", unit.crossFadeTime, 0, 0f); //animator.Play("hit", 0, 0.01f);

            // [Interflow fix 2026-07-24 combat-hub] Реакции на получение урона (контрудар, ответная заморозка).
            // Только если юнит выжил: посмертные эффекты живут отдельно, в DeathEffects (OnDie).
            if (!died) InterflowCombat.NotifyDamaged(unit, p.attackingUnit, p.damageType, damageDealt, p.directAttack);

            return died;
        }

        // ================= КОНТРОЛЬ ==============================================================
        // Шаг 1 схемы. Приёмник РЕШАЕТ, применяется ли контроль; исполняет юнит.
        // Порядок проверок сохранён дословно, с одной принятой сменой поведения (решение Artsiom
        // 28.08.2026): иммунитет к контролю теперь спрашивается для ВСЕХ ТРЁХ видов, а не только
        // у оглушения. Немота и обезоруживание начнут отскакивать от иммунных.
        //
        // Отсев «есть ли кому адресовать пакет» (staticObject / dead / !canAttack) остался в воронках
        // Unit.State.cs (решение Artsiom 28.08.2026): дойти до приёмника — значит тронуть игровой
        // объект (TryGetComponent, при первом обращении AddComponent), а на трупе этого делать нельзя.
        // Так же устроен шаг 0: dead проверяется в обёртке GetDamage (Unit.Combat.cs), а не здесь.

        /// <summary>
        /// Компонент иммунитета к контролю, если он на юните есть. Кешируется ЛЕНИВО и только когда
        /// НАЙДЕН: компонент навешивается динамически по ходу матча (SkillBuff.cs:94,
        /// CompositePassive.Properties.cs:173, ControlImmunityPassive.cs:40), поэтому запоминать
        /// «его нет» нельзя — он может появиться позже. Найденный не уничтожается никогда
        /// (Remove() лишь уменьшает счётчик источников), поэтому повторный поиск после находки не нужен.
        /// </summary>
        ControlImmunity controlImmunity;

        /// <summary>
        /// Иммунен ли юнит к контролю прямо сейчас. Спрашивают и приёмник, и <see cref="Knockback"/>
        /// (отброс и рывок) — вместо собственного поиска компонента на каждый вызов, как требует §3.1 схемы.
        /// </summary>
        public bool ControlImmune
        {
            get
            {
                if (controlImmunity == null) TryGetComponent(out controlImmunity);

                return controlImmunity != null && controlImmunity.Active;
            }
        }

        /// <summary>
        /// Принять пакет контроля. Возвращает «контроль применён»: false — вызов отсеян
        /// (уже в этом статусе или иммунитет).
        /// </summary>
        public bool Receive(in ControlPacket p)
        {
            switch (p.type)
            {
                case ControlType.Stun:
                    // Порядок сегодняшнего Unit.Stun после отсева в воронке: иммунитет → тело.
                    if (ControlImmune) return false;

                    unit.StunApply(p.time);
                    return true;

                case ControlType.Mute:
                    // «Уже в немоте» стоит ВЫШЕ иммунитета — как сегодня в Unit.Mute.
                    // Комментарий оттуда: «If already muted, means currently permanently muted» —
                    // постоянная немота закодирована как muted=true при currentMuteTime==0
                    // (так её и восстанавливает SaveManager.UnitData.cs:1084).
                    if (unit.muted) return false;
                    if (ControlImmune) return false;

                    unit.MuteApply(p.time);
                    return true;

                case ControlType.Disarm:
                    // «Уже обезоружен» стоит ВЫШЕ иммунитета — как сегодня в Unit.Disarm.
                    // Комментарий оттуда: «If already disarmed, means currently permanently disarmed» —
                    // постоянное обезоруживание закодировано как disarmed=true при currentDisarmTime==0
                    // (так его и восстанавливает SaveManager.UnitData.cs:1093).
                    if (unit.disarmed) return false;
                    if (ControlImmune) return false;

                    unit.DisarmApply(p.time);
                    return true;
            }

            // Недостижимо: ControlType перебран целиком. Появится новый вид контроля —
            // добавить сюда его ветку, иначе он будет молча отсеян.
            return false;
        }
    }
}
