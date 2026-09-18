using NUnit.Framework;
using UnityEngine;

namespace StrategyCore.Tests
{
    /// <summary>
    /// Семья «рассечение» (проект «Рассечение_Проект» §4.5, решения Artsiom 59–63 от 18.09.2026).
    ///
    /// Что здесь можно проверить в режиме редактора, а что нет:
    ///   • решения блока — можно: они вынесены статическими чистыми функциями
    ///     (<c>CleaveWanted</c>, <c>CleaveExcluded</c>, <c>CleaveHits</c>, <c>CleaveCenter</c>,
    ///     <c>CleaveRadius</c>, <c>CleaveFraction</c>, <c>CleaveNumbersUsable</c>, <c>CleavePacket</c>);
    ///   • сам обработчик удара <c>OnCleaveApply</c> — НЕЛЬЗЯ: он приватный и требует живого хука
    ///     юнита, сетки поиска <c>Grid.chunkUnits</c> и приёмника урона. Поэтому всё, что он решает,
    ///     проверяется по тем же функциям, по которым решает и он сам;
    ///   • «технология открыта» через <c>TechnologyManager</c> — НЕЛЬЗЯ: его <c>Instance</c>
    ///     с приватным сеттером ставится в <c>Awake</c>, а лезть в приватное состояние правило 9
    ///     запрещает. Ровно поэтому выбор чисел разведён на две функции: <c>CleaveUpgraded</c>
    ///     (спрашивает дерево технологий, серверная) и <c>CleaveRadius</c>/<c>CleaveFraction</c>
    ///     (берут готовый ответ) — вторые и проверяются здесь напрямую.
    ///
    /// Оснастка — <see cref="UnitTestHarness"/>, образец — <c>HealthGateTests</c>.
    /// </summary>
    public class CleaveTests : UnitTestHarness
    {
        GameObject slotManagerHost;

        [TearDown]
        public void DestroySlotManagerHost()
        {
            if (slotManagerHost != null) Object.DestroyImmediate(slotManagerHost);
            slotManagerHost = null;
        }

        /// <summary>Приёмнику состояний нужен <c>SlotManager.Instance</c> — сверка команд.</summary>
        void EnsureSlotManager()
        {
            if (SlotManager.Instance != null) return;

            slotManagerHost = new GameObject("TestSlotManager");
            slotManagerHost.hideFlags = HideFlags.HideAndDontSave;
            slotManagerHost.AddComponent<SlotManager>().InstanceSet();
        }

        /// <summary>
        /// Бессрочное состояние без длительности — ровно та оснастка, на которой приёмник состояний
        /// уже проверен в <c>HealthGateTests</c>: в режиме редактора это единственная форма наложения,
        /// которая доживает до конца теста.
        /// </summary>
        Effector MakeEffector(int id)
        {
            Effector e = MakeAsset<Effector>();
            e.id = id;
            e.permanent = true;
            e.duration = 0f;
            return e;
        }

        /// <summary>Блок с числами Халдора: центр на носителе, радиус 2.5, доля 1.</summary>
        static PassiveCleaveBlock Haldor()
        {
            return new PassiveCleaveBlock
            {
                enabled = true,
                centerOnCaster = true,
                radius = 2.5f,
                damageFraction = 1f
            };
        }

        // ================================================== ЦЕНТР И ИСКЛЮЧЕНИЯ ==

        [Test]
        public void Рассечение_ЦентрНаЦели_ОсновнаяЦельНеЗадета()
        {
            Unit carrier = MakeBareUnit("Носитель");
            Unit target = MakeBareUnit("Цель");

            // Круг стоит НА ЦЕЛИ: свой урон она уже получила основным ударом, второй раз не бьём.
            Assert.AreSame(target, CompositePassive.CleaveExcluded(false, target),
                           "при центре на цели она уходит в excludeUnit самой выборки");

            Assert.IsFalse(CompositePassive.CleaveHits(target, carrier, target, false),
                           "и отсеивается повторно в цикле — на случай, если выборка её всё-таки вернула");
        }

        [Test]
        public void Рассечение_ЦентрНаНосителе_ЦельЗадетаКакСосед()
        {
            Unit carrier = MakeBareUnit("Носитель");
            Unit target = MakeBareUnit("Цель");

            // Круг стоит НА НОСИТЕЛЕ: цель в нём — обычный сосед, исключать её незачем.
            Assert.IsNull(CompositePassive.CleaveExcluded(true, target),
                          "при центре на носителе выборка никого не исключает заранее");

            Assert.IsTrue(CompositePassive.CleaveHits(target, carrier, target, true),
                          "цель удара получает и основной урон, и долю рассечения");
        }

        [Test]
        public void Рассечение_Центр_НосительИлиТочкаУдара()
        {
            Vector3 carrierPosition = new Vector3(1f, 7f, 2f);
            Vector3 targetPosition = new Vector3(9f, 0f, 4f);

            Assert.AreEqual(new Vector2(1f, 2f), CompositePassive.CleaveCenter(true, carrierPosition, targetPosition),
                            "центр на носителе: берутся x и z носителя, высота в выборке не участвует");

            Assert.AreEqual(new Vector2(9f, 4f), CompositePassive.CleaveCenter(false, carrierPosition, targetPosition),
                            "центр на цели: берётся точка удара");
        }

        [Test]
        public void Рассечение_НосительСебяНеБьёт()
        {
            Unit carrier = MakeBareUnit("Носитель");
            Unit target = MakeBareUnit("Цель");

            Assert.IsFalse(CompositePassive.CleaveHits(carrier, carrier, target, true),
                           "центр на носителе — носитель всё равно отсеян");

            Assert.IsFalse(CompositePassive.CleaveHits(carrier, carrier, target, false),
                           "центр на цели — носитель мог попасть в круг вокруг собственной цели, и тоже отсеян");
        }

        [Test]
        public void Рассечение_ЗадетыйСосед_Проходит()
        {
            Unit carrier = MakeBareUnit("Носитель");
            Unit target = MakeBareUnit("Цель");
            Unit neighbour = MakeBareUnit("Сосед");

            Assert.IsTrue(CompositePassive.CleaveHits(neighbour, carrier, target, true));
            Assert.IsTrue(CompositePassive.CleaveHits(neighbour, carrier, target, false));

            Assert.IsFalse(CompositePassive.CleaveHits(null, carrier, target, true),
                           "пустая запись выборки задета быть не может");
        }

        // ================================================================ ЧИСЛА ==

        [Test]
        public void Рассечение_РадиусНоль_НичегоНеДелает()
        {
            Assert.IsFalse(CompositePassive.CleaveNumbersUsable(0f, 1f), "нулевой радиус — задевать некого");
            Assert.IsFalse(CompositePassive.CleaveNumbersUsable(-1f, 1f), "отрицательный радиус — то же самое");
            Assert.IsFalse(CompositePassive.CleaveNumbersUsable(2.5f, 0f), "нулевая доля — бить нечем");

            Assert.IsTrue(CompositePassive.CleaveNumbersUsable(2.5f, 1f));
        }

        [Test]
        public void Рассечение_Доля_УронСчитаетсяОтУдара()
        {
            Unit carrier = MakeBareUnit("Носитель");
            DamageType type = MakeAsset<DamageType>();

            // Доля плоская: спада по дистанции у рассечения нет, в отличие от разлёта.
            DamagePacket packet = CompositePassive.CleavePacket(40f * 0.3f, type, carrier.owner, carrier, null);

            Assert.AreEqual(12f, packet.amount, Tolerance,
                            "сосед получает долю от урона ОСНОВНОГО удара, а не от базового урона носителя");
        }

        [Test]
        public void Рассечение_ПакетНеПрямаяАтака_РекурсииНет()
        {
            Unit carrier = MakeBareUnit("Носитель");
            DamageType type = MakeAsset<DamageType>();
            CompositePassive passive = MakeAsset<CompositePassive>();

            DamagePacket packet = CompositePassive.CleavePacket(5f, type, 1, carrier, passive);

            // Признак прямой атаки — инвариант блока: с true пакет заново поднял бы
            // Unit.OnAfterDamageDealCallbacks, на котором висит сам блок.
            Assert.IsFalse(packet.directAttack,
                           "урон соседу обязан идти НЕ прямой атакой, иначе рассечение зарекурсит на своих же соседях");

            Assert.IsNull(packet.attackEffectors,
                          "состояния атаки едут только с прямой атакой — сосед их не получает");

            Assert.AreSame(passive, packet.sourceAbility, "источник пакета — сама пассивка");
            Assert.AreSame(carrier, packet.attackingUnit);
            Assert.AreEqual(1, packet.attackingPlayer);
        }

        // ======================================================= УЛУЧШЕНИЕ ТЕХНОЛОГИЕЙ ==

        [Test]
        public void Технология_Открыта_БерутсяУлучшенныеЧисла()
        {
            PassiveCleaveBlock b = new PassiveCleaveBlock
            {
                enabled = true,
                radius = 1.5f,
                damageFraction = 0.4f,
                upgradedRadius = 2f,
                upgradedDamageFraction = 0.6f
            };

            Assert.AreEqual(1.5f, CompositePassive.CleaveRadius(b, false), Tolerance);
            Assert.AreEqual(0.4f, CompositePassive.CleaveFraction(b, false), Tolerance);

            // Улучшенные числа — ПОЛНЫЕ, а не прибавка (решение Artsiom 61, как у вампиризма).
            Assert.AreEqual(2f, CompositePassive.CleaveRadius(b, true), Tolerance);
            Assert.AreEqual(0.6f, CompositePassive.CleaveFraction(b, true), Tolerance);
        }

        [Test]
        public void Технология_НеЗадана_УлучшенияНетВовсе()
        {
            PassiveCleaveBlock b = new PassiveCleaveBlock { enabled = true, radius = 1.5f, damageFraction = 0.4f };

            // Пустая ссылка не идёт в безопасную обёртку намеренно: обёртка отвечает «условия нет»
            // (true) на null, и улучшенные числа применялись бы всегда.
            Assert.IsFalse(CompositePassive.CleaveUpgraded(b, 0), "пустая ссылка на технологию — улучшения нет");
            Assert.IsFalse(CompositePassive.CleaveUpgraded(null, 0));
        }

        // ================================================== УСЛОВИЕ ПО ЗДОРОВЬЮ ==

        [Test]
        public void УсловиеПоЗдоровью_НеВыполнено_БлокМолчит()
        {
            PassiveCleaveBlock b = Haldor();
            b.carrierCondition = PassiveBlockCondition.WhileHpBelow;
            b.carrierHpBelow = 0.5f;

            Assert.IsFalse(CompositePassive.CleaveWanted(b, 80f, 100f), "здоровья больше половины — блок не подключён");
            Assert.IsFalse(CompositePassive.CleaveWanted(b, 50f, 100f), "ровно на пороге — тоже нет, сравнение строгое");
            Assert.IsTrue(CompositePassive.CleaveWanted(b, 49f, 100f), "ниже половины — блок работает");
        }

        [Test]
        public void УсловиеПоЗдоровью_Всегда_ЗдоровьеНеСмотрится()
        {
            PassiveCleaveBlock b = Haldor();

            Assert.IsTrue(CompositePassive.CleaveWanted(b, 100f, 100f), "условие «Всегда» здоровья не читает");

            b.enabled = false;
            Assert.IsFalse(CompositePassive.CleaveWanted(b, 10f, 100f), "выключенный блок не работает ни при каком здоровье");
        }

        [Test]
        public void УсловиеПоЗдоровью_Переход_ПодписываетИСнимаетТолькоНаГранице()
        {
            // Тиковый переход и есть «выдать / снять блок 11» (решение Artsiom 60): Grant подписывает
            // на хук удара, Revoke отписывает. Повторной работы на неизменном состоянии быть не должно.
            Assert.AreEqual(HealthGateStep.Grant, CompositePassive.GateStep(false, true));
            Assert.AreEqual(HealthGateStep.Revoke, CompositePassive.GateStep(true, false));
            Assert.AreEqual(HealthGateStep.None, CompositePassive.GateStep(true, true));
            Assert.AreEqual(HealthGateStep.None, CompositePassive.GateStep(false, false));
        }

        // ================================================= УСЛОВИЕ ПО СОСТОЯНИЮ ==

        [Test]
        public void УсловиеСостояния_НеВыполнено_БлокМолчит()
        {
            EnsureSlotManager();

            Unit carrier = MakeBareUnit("Носитель");
            Effector courage = MakeEffector(777);

            Assert.IsTrue(CompositePassive.CleaveCarrierStateMet(carrier, null), "пустая ссылка — условия нет");

            Assert.IsFalse(CompositePassive.CleaveCarrierStateMet(carrier, courage),
                           "состояния на носителе нет — рассечение молчит");

            Effector.EffectorAdd(carrier, courage, null, carrier.owner);

            Assert.IsTrue(CompositePassive.CleaveCarrierStateMet(carrier, courage),
                          "состояние наложено — условие выполнено");
        }

        [Test]
        public void УсловиеСостояния_СравнениеПоНомеруАссета()
        {
            EnsureSlotManager();

            Unit carrier = MakeBareUnit("Носитель");

            Effector onUnit = MakeEffector(777);
            Effector requiredCopy = MakeEffector(777);   // тот же номер, другой объект — так бывает у копий ассета на пирах
            Effector foreign = MakeEffector(778);

            Effector.EffectorAdd(carrier, onUnit, null, carrier.owner);

            Assert.IsTrue(CompositePassive.CleaveCarrierStateMet(carrier, requiredCopy),
                          "сравнение идёт по номеру ассета, как у выбора цели (SkillTargeting)");

            Assert.IsFalse(CompositePassive.CleaveCarrierStateMet(carrier, foreign),
                           "чужой номер — условие не выполнено");
        }

        // ============================================================== ВИЗУАЛ ==

        [Test]
        public void Резолв_Рассечения_ОтдаётСвойНабор()
        {
            CompositePassive passive = MakeAsset<CompositePassive>();
            passive.name = "TestCleavePassive";
            passive.id = 303;

            InterflowAbility ability = passive;

            Assert.AreSame(passive.cleave.presentation, ability.PresentationFor((int)AbilityEventCode.PassiveCleave),
                           "код рассечения резолвится в набор блока 11");

            Assert.AreNotSame(passive.onHit.presentation, ability.PresentationFor((int)AbilityEventCode.PassiveCleave),
                              "набор блока 11 — свой, а не набор реакции 5");
        }
    }
}
