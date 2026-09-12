using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// КОНСТРУКТОР ПАССИВКИ: пассивное умение собирается в редакторе из готовых блоков,
    /// без написания нового скрипта. Дизайн — `Документы/Дизайн/Дизайн_Конструктор_Пассивок.md`
    /// (решения Artsiom 2026-08-09: конструктор с настройкой параметров, живёт РЯДОМ со старыми
    /// 25 классами; оси — «свойства» и «реакции»).
    ///
    /// Здесь реализована ОСЬ 1 — СВОЙСТВА: то, что работает всё время, пока умение открыто у юнита.
    /// Ось 2 (реакции на события боя) идёт отдельным блоком работ и в этот файл не входит.
    ///
    /// Как это работает: пассивка не кастуется. Ядро само считает замки по `requiredTech`
    /// и `requiredLevel`, зовёт <see cref="Unlock"/> при открытии и <see cref="Lock"/> при закрытии.
    /// Каждый блок — галка плюс поля; под каждым штатный механизм движка, ничего нового не изобретается
    /// (правило 2). Наследование от <see cref="CompositeSkill"/> НЕ используется намеренно: у пассивки
    /// нет каста, времени каста, маны и выбора цели игроком.
    ///
    /// ВАЖНО про ScriptableObject: ассет переживает выход из Play, поэтому всё пер-юнит состояние
    /// чистится в <see cref="Init"/> (её зовёт GameManager при старте матча).
    ///
    /// Атрибут меню создания намеренно НЕ ставится: по решению Artsiom 2026-08-09 параллельное
    /// меню Unity у умений снято, создание идёт кнопкой во вкладке «Пассивные умения».
    /// </summary>
    public partial class CompositePassive : InterflowAbility
    {
        public override AbilityType type { get { return AbilityType.Passive; } }

        // ==================================================================== БЛОКИ ==

        [Header("Блок 1 — изменение характеристик")]
        public PassiveStatsBlock stats = new PassiveStatsBlock();

        [Header("Блок 2 — иммунитет к контролю")]
        public PassiveControlImmunityBlock controlImmunity = new PassiveControlImmunityBlock();

        [Header("Блок 3 — иммунитет к замедлениям")]
        public PassiveSlowImmunityBlock slowImmunity = new PassiveSlowImmunityBlock();

        [Header("Блок 4 — постоянная невидимость")]
        public PassiveInvisibilityBlock invisibility = new PassiveInvisibilityBlock();

        [Header("Блок 5 — пробитие брони")]
        public PassiveArmorPierceBlock armorPierce = new PassiveArmorPierceBlock();

        [Header("Блок 6 — сплеш атаки")]
        public PassiveSplashBlock splash = new PassiveSplashBlock();

        [Header("Блок 7 — эффекторы к своим атакам")]
        public PassiveAttackEffectorsBlock attackEffectors = new PassiveAttackEffectorsBlock();

        [Header("Блок 8 — аура")]
        public PassiveAuraBlock aura = new PassiveAuraBlock();

        [Header("Блок 9 — защита и парирование")]
        public PassiveDefenseBlock defense = new PassiveDefenseBlock();

        [Header("Блок 10 — посмертный эффект")]
        public PassiveDeathBlock deathEffect = new PassiveDeathBlock();

        [Header("Блок 11 — награда за убийство")]
        public PassiveKillBlock killEffect = new PassiveKillBlock();
        public PassiveDamageReactionBlock damageReaction = new PassiveDamageReactionBlock();
        public PassiveCleaveBlock cleave = new PassiveCleaveBlock();
        public PassiveManaBurnBlock manaBurn = new PassiveManaBurnBlock();
        public PassiveSpellCastBlock spellCast = new PassiveSpellCastBlock();
        public PassiveCombatResourceBlock combatResource = new PassiveCombatResourceBlock();
        public PassiveAllyDeathBlock allyDeath = new PassiveAllyDeathBlock();

        [Tooltip("Визуальный скилл при попадании обычной атаки. Его механические блоки не исполняются.")]
        public CompositeSkill attackPresentation;

        // ======================================================= СОСТОЯНИЕ НОСИТЕЛЕЙ ==

        /// <summary>
        /// Что именно выдано конкретному юниту. Нужен, чтобы снять РОВНО то, что выдали:
        /// блок могли выключить в ассете между Unlock и Lock, и «снимать по текущим галкам» —
        /// значит рассинхронизировать статы юнита навсегда.
        /// </summary>
        class Carrier
        {
            public int level;

            public bool stats;
            public bool controlImmunity;
            public System.Action hpControlHandler;
            public bool slowImmunity;
            public bool invisibility;

            public float armorPierceFraction;          // 0 — не выдавали
            public SplashState splash;                 // null — сплеш не трогали
            public Effector[] originalAttackEffectors; // null — набор атаки не трогали

            public bool aura;
            public InterflowCombat.IncomingRule defenseRule;
            public System.Action<Unit, int, Unit, bool> deathHandler;
            public AfterDamageDealCallback attackVisualCallback;
            public AfterDamageDealCallback cleaveCallback;
            public AfterDamageDealCallback manaBurnCallback;
            public float killDamageFraction;
            public float reactionReadyAt;
            public InterflowCombat.DamagedHandler damagedHandler;
            public Vector3 lastPosition;               // для условия «в движении»
        }

        readonly Dictionary<Unit, Carrier> carriers = new Dictionary<Unit, Carrier>();
        readonly List<Unit> tickBuffer = new List<Unit>();
        bool tickWired;

        // ==================================================================== ЖИЗНЬ ==

        public override void Init()
        {
            base.Init();

            UnwireKillEffect();
            carriers.Clear();
            auraDamageTypeWarned = false;
            UnwireTick();
        }

        /// <summary>
        /// Умение открылось у юнита — выдаём все включённые свойства.
        /// Идемпотентно: ядро зовёт Unlock повторно на КАЖДОЕ событие открытия технологии
        /// (способность без Required Tech разблокируется заново), а второй проход удвоил бы статы.
        /// </summary>
        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            base.Unlock(unit, castingPlayer, level);

            PruneDead();

            if (unit == null || carriers.ContainsKey(unit)) return;

            Carrier c = new Carrier { level = level, lastPosition = unit.transform.position };
            carriers[unit] = c;

            ApplyStats(unit, c);
            ApplyControlImmunity(unit, c);
            ApplySlowImmunity(unit, c);
            ApplyInvisibility(unit, c);
            ApplyArmorPierce(unit, c, level);
            ApplySplash(unit, c);
            ApplyAttackEffectors(unit, c);
            ApplyAura(unit, c);
            ApplyDefense(unit, c);
            ApplyDeathEffect(unit, c);
            ApplyAttackPresentation(unit,c);
            WireKillEffect();
            ApplyDamageReaction(unit,c);
            ApplyCleave(unit,c);
            ApplyManaBurn(unit,c);
            if(!NetworkConnectionHandler.isClient&&combatResource!=null&&combatResource.enabled){var resource=unit.GetComponent<CombatResource>();if(!resource)resource=unit.gameObject.AddComponent<CombatResource>();resource.Configure(this,combatResource);}
            if(spellCast!=null&&spellCast.enabled){var m=unit.GetComponent<SpellCastModifiers>()??unit.gameObject.AddComponent<SpellCastModifiers>();m.Set(this,spellCast);}

            if (c.slowImmunity || c.aura) WireTick();
        }

        /// <summary>Умение закрылось (потеря технологии, смена облика) — снимаем ровно выданное.</summary>
        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            base.Lock(unit, castingPlayer, level);

            if (unit == null || !carriers.TryGetValue(unit, out Carrier c)) return;

            RemoveStats(unit, c);
            RemoveControlImmunity(unit, c);
            RemoveInvisibility(unit, c);
            RemoveArmorPierce(unit, c);
            RemoveSplash(unit, c);
            RemoveAttackEffectors(unit, c);
            RemoveDefense(unit, c);
            RemoveDeathEffect(unit, c);
            RemoveAttackPresentation(unit,c);
            RemoveKillEffect(unit,c);
            RemoveDamageReaction(unit,c);
            RemoveCleave(unit,c);
            RemoveManaBurn(unit,c);
            var resource=unit.GetComponent<CombatResource>();if(resource)resource.Remove(this);
            var spellModifiers=unit.GetComponent<SpellCastModifiers>();if(spellModifiers)spellModifiers.Remove(this);
            // Иммунитет к замедлениям и аура своего состояния на юните не оставляют:
            // они живут тиком, и достаточно убрать носителя из списка.

            carriers.Remove(unit);

            if (carriers.Count == 0) UnwireKillEffect();

            if (!AnyoneNeedsTick()) UnwireTick();
        }

        /// <summary>
        /// Unit.Die не зовёт Lock — мёртвые ключи убираем лениво, при очередной разблокировке.
        /// Тот же приём, что в ExtraAttackEffectors и SplashModifier.
        /// </summary>
        void PruneDead()
        {
            if (carriers.Count == 0) return;

            List<Unit> dead = null;
            foreach (Unit u in carriers.Keys)
                if (u == null || u.dead) (dead ?? (dead = new List<Unit>())).Add(u);

            if (dead == null) return;
            for (int i = 0; i < dead.Count; i++) carriers.Remove(dead[i]);
        }

        // ===================================================================== ТИК ==

        bool AnyoneNeedsTick()
        {
            foreach (Carrier c in carriers.Values)
                if (c.slowImmunity || c.aura) return true;

            return false;
        }

        void WireTick()
        {
            if (tickWired || GameManager.instance == null) return;

            GameManager.instance.Tick += OnTick;
            tickWired = true;
        }

        void UnwireTick()
        {
            if (!tickWired) return;

            if (GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            tickWired = false;
        }

        /// <summary>
        /// Штатный тик 0.1 с. Только сервер: снятие эффекторов и урон ауры меняют состояние мира
        /// (правило 6). Клиент увидит результат штатной синхронизацией.
        /// </summary>
        void OnTick()
        {
            if (NetworkConnectionHandler.isClient || GameManager.instance == null) return;
            if (carriers.Count == 0) return;

            tickBuffer.Clear();
            tickBuffer.AddRange(carriers.Keys);

            for (int i = 0; i < tickBuffer.Count; i++)
            {
                Unit u = tickBuffer[i];
                if (u == null || u.dead) { carriers.Remove(u); continue; }
                if (!carriers.TryGetValue(u, out Carrier c)) continue;

                if (c.slowImmunity) TickSlowImmunity(u);
                if (c.aura) TickAura(u, c);
            }

            if (!AnyoneNeedsTick()) UnwireTick();
        }
    }
}
