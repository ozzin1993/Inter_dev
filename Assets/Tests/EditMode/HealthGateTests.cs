using NUnit.Framework;
using UnityEngine;

namespace StrategyCore.Tests
{
    /// <summary>
    /// Семья «здоровье носителя и иммунитеты» (проект §4.6, решения Artsiom 41–46 от 17.09.2026).
    ///
    /// Что проверяется: чистые функции семьи (порог, ступень, шаг перехода, доля вампиризма)
    /// и пара «наложить длящийся визуал — снять по держателю» на живом приёмнике.
    ///
    /// Чего здесь НЕТ и почему:
    ///   • сам обработчик тика (<c>CompositePassive.TickHealth</c>) из EditMode недостижим —
    ///     он приватный и требует живого <c>GameManager.Instance</c>. Поэтому всё, что тик решает,
    ///     вынесено в статические чистые функции и проверяется здесь напрямую;
    ///   • ветка «наложение на труп» — <c>Unit.dead</c> с приватным сеттером, а <c>Die()</c>
    ///     в EditMode не проходит; лезть в приватное состояние правило 9 запрещает. Тест
    ///     «нулевой держатель — снятия нет» идёт через ветку «здание»: в воронке
    ///     <c>Effector.EffectorAdd</c> это ТОТ ЖЕ ранний <c>return null</c>, что и у трупа.
    ///
    /// Оснастка — <see cref="UnitTestHarness"/>, образец — <c>ReceiverStatusHolderTests</c>.
    /// </summary>
    public class HealthGateTests : UnitTestHarness
    {
        GameObject slotManagerHost;
        bool isClientBefore;

        [SetUp]
        public void RememberNetworkFlag()
        {
            isClientBefore = NetworkConnectionHandler.isClient;
        }

        [TearDown]
        public void RestoreNetworkFlagAndHost()
        {
            NetworkConnectionHandler.isClient = isClientBefore;

            if (slotManagerHost != null) Object.DestroyImmediate(slotManagerHost);
            slotManagerHost = null;
        }

        /// <summary>Приёмнику и снятию нужен <c>SlotManager.Instance</c> — сверка команд.</summary>
        void EnsureSlotManager()
        {
            if (SlotManager.Instance != null) return;

            slotManagerHost = new GameObject("TestSlotManager");
            slotManagerHost.hideFlags = HideFlags.HideAndDontSave;
            slotManagerHost.AddComponent<SlotManager>().InstanceSet();
        }

        /// <summary>Бессрочный визуал без значка, VFX и геймплея — ровно то, чего требует правило Р39.</summary>
        Effector MakeLastingVisual(int id = 901)
        {
            Effector visual = MakeAsset<Effector>();
            visual.id = id;
            visual.permanent = true;
            visual.duration = 0f;
            return visual;
        }

        // ================================================================ ПОРОГ ==

        [Test]
        public void Порог_СтрогоМеньшеДоли()
        {
            // 29 из 100 — ниже трети, условие выполнено; ровно 30 — уже нет (сравнение строгое).
            Assert.IsTrue(CompositePassive.BlockActive(PassiveBlockCondition.WhileHpBelow, 0.3f, 29f, 100f),
                          "здоровье строго ниже доли — условие выполнено");

            Assert.IsFalse(CompositePassive.BlockActive(PassiveBlockCondition.WhileHpBelow, 0.3f, 30f, 100f),
                           "ровно на пороге условие НЕ выполнено — сравнение строгое");

            Assert.IsFalse(CompositePassive.BlockActive(PassiveBlockCondition.WhileHpBelow, 0.3f, 100f, 100f),
                           "полное здоровье — условия нет");
        }

        [Test]
        public void Порог_МаксимумНоль_УсловиеНеВыполнено()
        {
            // Доли от нуля не существует, а пропускать «условие, которое нечем посчитать» нельзя.
            Assert.IsFalse(CompositePassive.BlockActive(PassiveBlockCondition.WhileHpBelow, 0.3f, 0f, 0f));
        }

        [Test]
        public void Порог_Ноль_УсловияНет()
        {
            // Нулевой порог читается как «условия нет» — блок работает при любом здоровье.
            // Ровно это и ловит правило проверки Р37: настройка выглядит условием, а работает как «всегда».
            Assert.IsTrue(CompositePassive.BlockActive(PassiveBlockCondition.WhileHpBelow, 0f, 100f, 100f));
            Assert.IsTrue(CompositePassive.BlockActive(PassiveBlockCondition.WhileHpBelow, 0f, 1f, 100f));

            // Условие «Всегда» здоровье не читает вовсе — даже нулевой максимум его не отменяет.
            Assert.IsTrue(CompositePassive.BlockActive(PassiveBlockCondition.Always, 0.3f, 0f, 0f));
        }

        // ========================================================== ПЕРЕСЕЧЕНИЕ ==

        [Test]
        public void Пересечение_ВнизИВверх_ВыдачаИСнятиеПарные()
        {
            const float below = 0.3f;
            const float max = 100f;

            bool granted = false;
            int grants = 0;
            int revokes = 0;

            // Здоровье идёт вниз через порог и обратно вверх. Каждый шаг — один тик.
            float[] track = { 100f, 50f, 31f, 30f, 29f, 10f, 29f, 30f, 60f, 100f };

            foreach (float hp in track)
            {
                bool met = CompositePassive.BlockActive(PassiveBlockCondition.WhileHpBelow, below, hp, max);

                switch (CompositePassive.GateStep(granted, met))
                {
                    case HealthGateStep.Grant:  grants++;  granted = true;  break;
                    case HealthGateStep.Revoke: revokes++; granted = false; break;
                }
            }

            Assert.AreEqual(1, grants, "вниз через порог — ровно одна выдача");
            Assert.AreEqual(1, revokes, "вверх через порог — ровно одно снятие");
            Assert.IsFalse(granted, "на полном здоровье блок снят");
        }

        [Test]
        public void Пересечение_БезИзменения_ПовторнойВыдачиНет()
        {
            // Условие выполнено и остаётся выполненным — тик не должен делать НИЧЕГО.
            // Иначе счётчик источников иммунитета к контролю рос бы десять раз в секунду.
            Assert.AreEqual(HealthGateStep.None, CompositePassive.GateStep(true, true));
            Assert.AreEqual(HealthGateStep.None, CompositePassive.GateStep(false, false));

            Assert.AreEqual(HealthGateStep.Grant, CompositePassive.GateStep(false, true));
            Assert.AreEqual(HealthGateStep.Revoke, CompositePassive.GateStep(true, false));
        }

        // =============================================================== КЛИЕНТ ==

        [Test]
        public void Клиент_ОбработчикМолчит()
        {
            // Гейт «только сервер» у семьи ОДИН — CompositePassive.OnTick, и он читает этот флаг
            // через InterflowAbility.IsClientPeer. Самого обработчика из EditMode не достать
            // (приватный, нужен живой GameManager), поэтому проверяется контракт вокруг него:
            // флаг доходит до гейта, а решения семьи от сети не зависят вовсе — значит никакой
            // второй клиентской ветки в семье нет и завестись не может незаметно.
            NetworkConnectionHandler.isClient = true;
            Assert.IsTrue(InterflowAbility.IsClientPeer, "гейт семьи читает штатный флаг пира");

            bool onClient = CompositePassive.BlockActive(PassiveBlockCondition.WhileHpBelow, 0.3f, 29f, 100f);
            float missingOnClient = CompositePassive.MissingHpFraction(29f, 100f);
            float stepOnClient = CompositePassive.QuantizeMissingHp(0.71f, 0.1f);

            NetworkConnectionHandler.isClient = false;
            Assert.IsFalse(InterflowAbility.IsClientPeer);

            Assert.AreEqual(CompositePassive.BlockActive(PassiveBlockCondition.WhileHpBelow, 0.3f, 29f, 100f), onClient,
                            "условие по здоровью — чистая функция, сеть в неё не входит");
            Assert.AreEqual(CompositePassive.MissingHpFraction(29f, 100f), missingOnClient, Tolerance);
            Assert.AreEqual(CompositePassive.QuantizeMissingHp(0.71f, 0.1f), stepOnClient, Tolerance);
        }

        // =============================================================== СТУПЕНЬ ==

        [Test]
        public void Ступень_ОкругляетВниз_ПоПолнымШагам()
        {
            Assert.AreEqual(0.3f, CompositePassive.QuantizeMissingHp(0.37f, 0.1f), Tolerance, "0.37 → три полных шага");
            Assert.AreEqual(0.2f, CompositePassive.QuantizeMissingHp(0.2f, 0.1f), Tolerance,
                            "ровно на границе ступень уже взята (двоичная погрешность не должна съедать шаг)");
            Assert.AreEqual(0f, CompositePassive.QuantizeMissingHp(0.09f, 0.1f), Tolerance, "меньше шага — ступени нет");
            Assert.AreEqual(1f, CompositePassive.QuantizeMissingHp(1f, 0.25f), Tolerance, "полная нехватка — последняя ступень");

            // Нехватка считается от здоровья, а не задаётся вручную: 65 из 100 — потеряно 0.35 → ступень 0.3.
            float missing = CompositePassive.MissingHpFraction(65f, 100f);
            Assert.AreEqual(0.3f, CompositePassive.QuantizeMissingHp(missing, 0.1f), Tolerance);
        }

        [Test]
        public void Ступень_Ноль_КриваяЧитаетсяПлавно()
        {
            Assert.AreEqual(0.37f, CompositePassive.QuantizeMissingHp(0.37f, 0f), Tolerance, "шага нет — значение не трогается");
            Assert.AreEqual(0.37f, CompositePassive.QuantizeMissingHp(0.37f, -1f), Tolerance, "отрицательный шаг читается как «шага нет»");

            // Нулевой максимум здоровья: нехватки нет, считать не от чего.
            Assert.AreEqual(0f, CompositePassive.MissingHpFraction(0f, 0f), Tolerance);
        }

        // ================================================= ВАМПИРИЗМ (решение 45) ==

        [Test]
        public void Вампиризм_БезТехнологии_БазоваяДоля()
        {
            var b = new PassiveOnHitBlock
            {
                enabled = true,
                healFromDamagePercent = 0.3f,
                healFromDamagePercentUpgraded = 0.5f   // задано, но технологии нет — читаться не должно
            };

            Assert.AreEqual(0.3f, CompositePassive.OnHitHealPercent(b, 0, 100f, 100f), Tolerance,
                            "технология не задана — улучшенное число не берётся никогда");
        }

        [Test]
        public void Вампиризм_УсловиеПоЗдоровью_НеВыполнено_ВозвратаНет()
        {
            var b = new PassiveOnHitBlock
            {
                enabled = true,
                healFromDamagePercent = 0.3f,
                healCondition = PassiveBlockCondition.WhileHpBelow,
                healCarrierHpBelow = 0.3f
            };

            Assert.AreEqual(0f, CompositePassive.OnHitHealPercent(b, 0, 100f, 100f), Tolerance,
                            "носитель цел — вампиризм не работает");
            Assert.AreEqual(0.3f, CompositePassive.OnHitHealPercent(b, 0, 29f, 100f), Tolerance,
                            "носитель ранен ниже порога — работает базовая доля");
        }

        // ============================================== ДЛЯЩИЙСЯ ВИЗУАЛ (решение 46) ==

        [Test]
        public void Визуал_ПриВыключенииУсловия_СнимаетсяПоДержателю()
        {
            EnsureSlotManager();

            Unit unit = MakeBareUnit();
            Effector visual = MakeLastingVisual();

            // Чужое наложение ТОГО ЖЕ ассета, но другой силы: оно не слипается с нашим и обязано
            // пережить снятие — ровно ради этого держатель и запоминается.
            // Разводим именно СИЛОЙ, а не длительностью: у бессрочного состояния переопределение
            // длительности игнорируется (UnitReceiver.Statuses.cs), и оба наложения слиплись бы.
            EffectorHolder foreign = Effector.EffectorAdd(unit, visual, unit, unit.owner, powerMultiplier: 2f);

            EffectorHolder mine = Effector.EffectorAdd(unit, visual, null, unit.owner);
            Assert.IsNotNull(mine, "наложение длящегося визуала обязано отдать держатель");
            Assert.AreEqual(2, unit.effectors.Count);

            Effector.EffectorRemove(unit, mine);

            Assert.AreEqual(1, unit.effectors.Count, "снято ровно своё наложение");
            Assert.AreSame(foreign, unit.effectors[0], "чужое наложение того же ассета остаётся на юните");
        }

        [Test]
        public void Визуал_ДержательПустой_СнятияНет()
        {
            // Адресата нет (здание; на трупе воронка возвращает null тем же ранним выходом) —
            // держателя не будет, и снимать при выключении условия нечего. Блок обязан это пережить.
            Unit building = MakeBareUnit("TestBuilding");
            building.unitType = UnitType.Building;

            EffectorHolder holder = Effector.EffectorAdd(building, MakeLastingVisual(902), null, building.owner);

            Assert.IsNull(holder, "наложения не было — держателя нет");
            Assert.AreEqual(0, building.effectors.Count);
        }

        // ================================================ СНЯТИЕ ПО КАТЕГОРИИ (42) ==

        [Test]
        public void СнятиеПоКатегории_УбираетТолькоСвоюКатегорию()
        {
            EnsureSlotManager();

            Unit unit = MakeBareUnit();

            Effector slow = MakeAsset<Effector>();
            slow.id = 910;
            slow.duration = 5f;
            slow.category = EffectorCategory.MoveSlow;

            Effector burn = MakeAsset<Effector>();
            burn.id = 911;
            burn.duration = 5f;
            burn.category = EffectorCategory.DamageOverTime;

            Effector plain = MakeAsset<Effector>();
            plain.id = 912;
            plain.duration = 5f;   // категория «Нет» — снятию по категории не подлежит никогда

            Effector.EffectorAdd(unit, slow, unit, unit.owner);
            Effector.EffectorAdd(unit, burn, unit, unit.owner);
            Effector.EffectorAdd(unit, plain, unit, unit.owner);
            Assert.AreEqual(3, unit.effectors.Count);

            int removed = Effector.EffectorRemoveByCategory(unit, EffectorCategory.MoveSlow);

            Assert.AreEqual(1, removed, "снято одно наложение своей категории");
            Assert.AreEqual(2, unit.effectors.Count);
            foreach (EffectorHolder eh in unit.effectors)
                Assert.AreNotEqual(EffectorCategory.MoveSlow, eh.effector.category);
        }

        [Test]
        public void СнятиеПоКатегории_КатегорияНет_НичегоНеСнимает()
        {
            EnsureSlotManager();

            Unit unit = MakeBareUnit();

            Effector plain = MakeAsset<Effector>();
            plain.id = 913;
            plain.duration = 5f;

            Effector.EffectorAdd(unit, plain, unit, unit.owner);

            Assert.AreEqual(0, Effector.EffectorRemoveByCategory(unit, EffectorCategory.None),
                            "«Нет» — не адрес: под него попали бы все нетегированные состояния разом");
            Assert.AreEqual(1, unit.effectors.Count);
        }

        [Test]
        public void СнятиеПоКатегории_СнятьНечего_НольБезОшибки()
        {
            Unit unit = MakeBareUnit();

            Assert.AreEqual(0, Effector.EffectorRemoveByCategory(unit, EffectorCategory.MoveSlow));
            Assert.AreEqual(0, Effector.EffectorRemoveByCategory(null, EffectorCategory.MoveSlow));
        }
    }
}
