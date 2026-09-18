using NUnit.Framework;
using UnityEngine;

namespace StrategyCore.Tests
{
    /// <summary>
    /// Шаг 4 слияния, семья «смерть и убийство» (проект `Документы/Механики/Смерть_и_Убийство_Проект.md` §4.5,
    /// решения Artsiom 36–40 от 17.09.2026).
    ///
    /// Что здесь проверяется и что проверить НЕЛЬЗЯ:
    ///   • отбор гибели союзника и расчёт прибавки за убийство — можно: оба вынесены чистыми функциями
    ///     (CompositePassive.AllyDeathAccepted, CompositePassive.KillBonusFraction) ровно ради этого,
    ///     тем же приёмом, что GaugeHitDecision у шкалы;
    ///   • МЁРТВЫЙ юнит — нельзя: Unit.dead закрыт на запись, а Unit.Die в режиме редактора неподъёмен
    ///     (лезет в GameManager.Instance и раздачу наград). Поэтому «носитель мёртв» проверяется
    ///     параметром carrierDead — он для того и заведён, — а «жертва мертва» проверяется настолько,
    ///     насколько достижимо: отбор не смотрит на жизненные поля жертвы вовсе;
    ///   • сам обработчик HandleAllyDeathForReactions и его клиентский гейт — нельзя: метод приватный,
    ///     а событие приходит от MatchManager, которого в режиме редактора нет. Серверность проверяется
    ///     на пути НАБОРА визуала, который из обработчика и вызывается.
    ///
    /// Селекторы во всех тестах задаются по принадлежности «свой» и совпадающим владельцем: штатная
    /// UnitSelector.IsUnitCompatible при этом не доходит до SlotManager.Instance, которого в режиме
    /// редактора нет.
    /// </summary>
    public class DeathAndKillTests : UnitTestHarness
    {
        /// <summary>Свой + юнит + земля. Ровно те флаги, при которых проверка не трогает SlotManager.</summary>
        static UnitSelector OwnGroundUnit()
        {
            return new UnitSelector(Own: true, Ally: false, Enemy: false, Unit: true, Building: false,
                                    StaticDestructible: false, isTree: false, Ground: true, Water: false,
                                    Air: false, includeInvisible: false, includeInvulnerable: false);
        }

        Unit MakeAlly(string name, Vector3 at, int owner = 0)
        {
            Unit u = MakeBareUnit(name);
            u.owner = owner;
            u.unitTypeID = 1;
            u.transform.position = at;
            return u;
        }

        /// <summary>
        /// Блок реакции 7 в рабочем виде. Радиус здесь НЕ задаётся: отбор принимает уже развёрнутое
        /// по уровню число отдельным параметром — ровно для того, чтобы тест не зависел от LevelValue.
        /// </summary>
        static PassiveOnAllyDeathBlock Block(Unit[] victimPrefabs = null)
        {
            return new PassiveOnAllyDeathBlock
            {
                enabled = true,
                victimSelector = OwnGroundUnit(),
                victimPrefabs = victimPrefabs,
                healFlat = new[] { 25f }
            };
        }

        // ======================================== ГИБЕЛЬ СОЮЗНИКА ==

        [Test]
        public void ГибельСоюзника_ЖертваМертва_РеакцияВсёРавноСработала()
        {
            // Инвариант 1 проекта §4.2: на хабе смертей жертва УЖЕ мертва (Unit.Die ставит dead = true
            // до вызова OnDie), поэтому проверка «жива ли жертва» выключила бы реакцию целиком.
            // Поставить dead в режиме редактора нечем, поэтому проверяем достижимое: отбор не смотрит
            // на жизненные поля жертвы вовсе — обнулённая жертва проходит так же, как полная.
            Unit carrier = MakeAlly("Carrier", Vector3.zero);
            Unit victim = MakeAlly("Victim", new Vector3(1f, 0f, 0f));

            victim.maxHealth = 100f;
            victim.health = 100f;
            Assert.IsTrue(CompositePassive.AllyDeathAccepted(Block(), victim, carrier, false, 0f));

            victim.health = 0f;
            Assert.IsTrue(CompositePassive.AllyDeathAccepted(Block(), victim, carrier, false, 0f),
                "гибель союзника считается по факту события хаба, а не по здоровью жертвы");
        }

        [Test]
        public void ГибельСоюзника_ЖертваВнеСписка_Молчит()
        {
            Unit carrier = MakeAlly("Carrier", Vector3.zero);
            Unit victim = MakeAlly("Victim", new Vector3(1f, 0f, 0f));
            victim.unitTypeID = 7;

            Unit allowed = MakeAlly("AllowedPrefab", Vector3.zero);
            allowed.unitTypeID = 42;

            Assert.IsFalse(CompositePassive.AllyDeathAccepted(Block(victimPrefabs: new[] { allowed }),
                                                              victim, carrier, false, 0f),
                "жертва не того типа — список заготовок её не пропускает");

            victim.unitTypeID = 42;
            Assert.IsTrue(CompositePassive.AllyDeathAccepted(Block(victimPrefabs: new[] { allowed }),
                                                             victim, carrier, false, 0f),
                "сверка идёт по типу юнита, а не по ссылке на префаб: в бою живёт экземпляр");

            Assert.IsTrue(CompositePassive.AllyDeathAccepted(Block(victimPrefabs: new Unit[0]),
                                                             victim, carrier, false, 0f),
                "пустой список заготовок фильтра не ставит — проходят все");
        }

        [Test]
        public void ГибельСоюзника_ПогибСамНоситель_НеСчитается()
        {
            // Инвариант 3 проекта §4.2: иначе гибель самого носителя сработала бы как гибель союзника,
            // и мёртвый герой лечил бы сам себя.
            Unit carrier = MakeAlly("Carrier", Vector3.zero);

            Assert.IsFalse(CompositePassive.AllyDeathAccepted(Block(), carrier, carrier, false, 0f));
        }

        [Test]
        public void ГибельСоюзника_НосительМёртв_НеЛечится()
        {
            // Инвариант 2 проекта §4.2. Носителя на dead проверять ОБЯЗАТЕЛЬНО: общая гибель отряда
            // иначе лечила бы уже погибшего героя.
            Unit carrier = MakeAlly("Carrier", Vector3.zero);
            Unit victim = MakeAlly("Victim", new Vector3(1f, 0f, 0f));

            Assert.IsTrue(CompositePassive.AllyDeathAccepted(Block(), victim, carrier, false, 0f));
            Assert.IsFalse(CompositePassive.AllyDeathAccepted(Block(), victim, carrier, true, 0f));
        }

        [Test]
        public void ГибельСоюзника_Радиус_НольЗначитВсяКарта()
        {
            Unit carrier = MakeAlly("Carrier", Vector3.zero);
            Unit victim = MakeAlly("Victim", new Vector3(50f, 0f, 0f));

            Assert.IsTrue(CompositePassive.AllyDeathAccepted(Block(), victim, carrier, false, 0f),
                "нулевой радиус — ограничения расстоянием нет");

            Assert.IsFalse(CompositePassive.AllyDeathAccepted(Block(), victim, carrier, false, 10f),
                "за пределами радиуса гибель не засчитывается");

            Assert.IsTrue(CompositePassive.AllyDeathAccepted(Block(), victim, carrier, false, 50f),
                "ровно на границе радиуса гибель засчитывается");
        }

        [Test]
        public void ГибельСоюзника_ПустойСелектор_НикогоНеПропускает()
        {
            // Умолчание моих блоков: селектор приходит без инициализатора, все флаги false.
            // Это же ловит правило проверки Р32 у реакции 2; здесь фиксируется само поведение.
            Unit carrier = MakeAlly("Carrier", Vector3.zero);
            Unit victim = MakeAlly("Victim", new Vector3(1f, 0f, 0f));

            PassiveOnAllyDeathBlock b = Block();
            b.victimSelector = new UnitSelector();

            Assert.IsFalse(CompositePassive.AllyDeathAccepted(b, victim, carrier, false, 0f));
        }

        [Test]
        public void ГибельСоюзника_БлокВыключен_Молчит()
        {
            Unit carrier = MakeAlly("Carrier", Vector3.zero);
            Unit victim = MakeAlly("Victim", new Vector3(1f, 0f, 0f));

            PassiveOnAllyDeathBlock b = Block();
            b.enabled = false;

            Assert.IsFalse(CompositePassive.AllyDeathAccepted(b, victim, carrier, false, 0f));
            Assert.IsFalse(CompositePassive.AllyDeathAccepted(null, victim, carrier, false, 0f));
        }

        // ================================== ПРИБАВКА К УРОНУ ЗА УБИЙСТВО ==

        [Test]
        public void ПрибавкаЗаУбийство_Накопление_СовпадаетСПравилом()
        {
            // Решение Artsiom 37: прибавки СКЛАДЫВАЮТСЯ. Три убийства по 0,1 дают +30 %,
            // а не ×1,331 — именно этим путь через хук отличается от Unit.ChangeDamage.
            Assert.AreEqual(0.1f, CompositePassive.KillBonusFraction(0.1f, 1, 0), Tolerance);
            Assert.AreEqual(0.3f, CompositePassive.KillBonusFraction(0.1f, 3, 0), Tolerance);
            Assert.AreEqual(1.0f, CompositePassive.KillBonusFraction(0.1f, 10, 0), Tolerance);

            Assert.AreNotEqual(Mathf.Pow(1.1f, 10f) - 1f, CompositePassive.KillBonusFraction(0.1f, 10, 0),
                "накопление обязано быть сложением, а не умножением");
        }

        [Test]
        public void ПрибавкаЗаУбийство_Предел_НеПревышен()
        {
            // Решение Artsiom 38: предел задаётся ЧИСЛОМ УБИЙСТВ в счёте, а не потолком доли.
            Assert.AreEqual(0.2f, CompositePassive.KillBonusFraction(0.1f, 2, 3), Tolerance);
            Assert.AreEqual(0.3f, CompositePassive.KillBonusFraction(0.1f, 3, 3), Tolerance);
            Assert.AreEqual(0.3f, CompositePassive.KillBonusFraction(0.1f, 9, 3), Tolerance,
                "выше предела прибавка не растёт");

            Assert.AreEqual(0.9f, CompositePassive.KillBonusFraction(0.1f, 9, 0), Tolerance,
                "0 — предела нет");
        }

        [Test]
        public void ПрибавкаЗаУбийство_НовыйНоситель_НачинаетСНуля()
        {
            // Счёт живёт в ReactionState носителя: новый экземпляр героя получает новое состояние,
            // где killCount = 0. Достижимая часть — что при нулевом счёте прибавки нет вовсе;
            // сам ReactionState приватен и из теста не создаётся.
            Assert.AreEqual(0f, CompositePassive.KillBonusFraction(0.1f, 0, 0), Tolerance);
            Assert.AreEqual(0f, CompositePassive.KillBonusFraction(0.1f, 0, 3), Tolerance);

            // И обратная сторона: без заданной прибавки счёт убийств ничего не даёт.
            Assert.AreEqual(0f, CompositePassive.KillBonusFraction(0f, 5, 0), Tolerance);
        }

        // ============================================ РАДИУС ЛЕЧЕНИЯ ==

        [Test]
        public void РадиусЛечения_Пусто_РавенРадиусуУрона()
        {
            // Решение Artsiom 36: пустое или нулевое поле обязано повторять поведение до его появления,
            // иначе уже собранные ассеты молча изменили бы дальность лечения.
            Assert.AreEqual(3f, CompositePassive.HealRadiusOrDamage(0f, 3f), Tolerance);
            Assert.AreEqual(3f, CompositePassive.HealRadiusOrDamage(-1f, 3f), Tolerance);
            Assert.AreEqual(8f, CompositePassive.HealRadiusOrDamage(8f, 3f), Tolerance,
                "заданный радиус лечения перебивает радиус урона");
        }

        // ================================================ СЕРВЕРНОСТЬ ==

        [Test]
        public void ГибельСоюзника_НаКлиенте_НаборМолчит()
        {
            // Клиентский гейт самого обработчика из теста недостижим (метод приватный, события хаба нет).
            // Проверяется то, что из обработчика вызывается: показ набора гибели союзника — серверный,
            // и на клиенте он не публикует ничего.
            bool isClientBefore = NetworkConnectionHandler.isClient;
            int facts = 0;
            System.Action<Unit, int, int, Unit, Vector3, int> handler = (c, id, lvl, aim, pt, code) => facts++;

            SkillPresentationEvents.SkillFired += handler;
            try
            {
                CompositePassive passive = MakeAsset<CompositePassive>();
                passive.name = "TestPassive";
                passive.id = 909;
                passive.onAllyDeath.presentation.animationState = "proc";

                NetworkConnectionHandler.isClient = true;
                InterflowAbility.EmitEventPresentation(passive, (int)AbilityEventCode.PassiveAllyDeath,
                                                       passive.onAllyDeath.presentation, null, 0, null, Vector3.zero);
                Assert.AreEqual(0, facts, "на клиенте набор гибели союзника не публикуется вовсе (правило 6)");

                NetworkConnectionHandler.isClient = false;
                InterflowAbility.EmitEventPresentation(passive, (int)AbilityEventCode.PassiveAllyDeath,
                                                       passive.onAllyDeath.presentation, null, 0, null, Vector3.zero);
                Assert.AreEqual(1, facts, "на сервере тот же набор публикует ровно один факт");
            }
            finally
            {
                SkillPresentationEvents.SkillFired -= handler;
                NetworkConnectionHandler.isClient = isClientBefore;
            }
        }
    }
}
