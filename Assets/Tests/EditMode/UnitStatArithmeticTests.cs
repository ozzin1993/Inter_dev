using NUnit.Framework;
using UnityEngine;

namespace StrategyCore.Tests
{
    /// <summary>
    /// Арифметика Unit.Change* — фиксируется ТЕКУЩЕЕ поведение, снятое с кода, а не желаемое.
    /// Шаг 1 промта «Статы»: следующий шаг меняет числа, и без эталона починку не отличить от регресса.
    ///
    /// Модель, которую держат эти тесты (Unit.Parameters.cs):
    ///   абсолютное изменение   value = ((value / multiplier) + amount) * multiplier
    ///   процентное, p &lt; 0     multiplier /= (1 - p);  value /= (1 - p)
    ///   процентное, p &gt;= 0    multiplier *= (1 + p);  value *= (1 + p)
    /// Из деления на множитель следует главное свойство модели: флэт хранится «до множителя»,
    /// поэтому порядок наложения флэта и процента результат не меняет.
    ///
    /// attackSpeed — единственное исключение: поле хранит ИНТЕРВАЛ между ударами, поэтому знаки
    /// у процентной перегрузки зеркальны (Unit.Parameters.cs:359-376).
    ///
    /// Дефекты сюда не входят — они в UnitStatDefectTests.cs. Здесь только те случаи,
    /// где модель работает так, как задумана.
    /// </summary>
    public class UnitStatArithmeticTests : UnitTestHarness
    {
        // ==================================================== модель множителя ==

        [Test]
        public void Урон_ПорядокНаложенияНеВлияет_ОбаПутиДают165()
        {
            Unit a = MakeBareUnit("порядок-флэт-первым");
            a.attackDamage = 100f;
            a.ChangeDamage(10f);
            a.ChangeDamage(0.5f, false);

            Unit b = MakeBareUnit("порядок-процент-первым");
            b.attackDamage = 100f;
            b.ChangeDamage(0.5f, false);
            b.ChangeDamage(10f);

            Assert.That(a.attackDamage, Is.EqualTo(165f).Within(Tolerance), "сначала +10, потом +50 %");
            Assert.That(b.attackDamage, Is.EqualTo(165f).Within(Tolerance), "сначала +50 %, потом +10");
            Assert.That(a.passiveEffects.damageChange, Is.EqualTo(b.passiveEffects.damageChange).Within(Tolerance),
                        "множители обязаны совпасть — иначе следующее снятие разъедется");
        }

        [Test]
        public void Урон_ФлэтТудаОбратно_ВозвращаетИсходноеИМножитель()
        {
            Unit unit = MakeBareUnit();
            unit.attackDamage = 100f;

            unit.ChangeDamage(10f);
            Assert.That(unit.attackDamage, Is.EqualTo(110f).Within(Tolerance));

            unit.ChangeDamage(-10f);
            Assert.That(unit.attackDamage, Is.EqualTo(100f).Within(Tolerance));
            Assert.That(unit.passiveEffects.damageChange, Is.EqualTo(1f).Within(Tolerance),
                        "флэт не имеет права трогать множитель");
        }

        [Test]
        public void Урон_ПроцентТудаОбратно_ВозвращаетИсходноеИМножитель()
        {
            Unit unit = MakeBareUnit();
            unit.attackDamage = 100f;

            unit.ChangeDamage(0.5f, false);
            Assert.That(unit.attackDamage, Is.EqualTo(150f).Within(Tolerance));
            Assert.That(unit.passiveEffects.damageChange, Is.EqualTo(1.5f).Within(Tolerance));

            unit.ChangeDamage(-0.5f, true);
            Assert.That(unit.attackDamage, Is.EqualTo(100f).Within(Tolerance));
            Assert.That(unit.passiveEffects.damageChange, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void Урон_ФлэтНаложенПриОдномМножителеСнятПриДругом_ВозвращаетсяТОЧНО()
        {
            // §5.2 п.4 промта требовал утверждать обратное — что значение НЕ возвращается.
            // По коду возвращается, и точно: флэт хранится «до множителя», деление в первой строке
            // Change* снимает текущий множитель перед вычитанием (Unit.Parameters.cs:264).
            // Решение Artsiom 2026-08-18: зафиксировать факт, премиссу промта считать опровергнутой.
            Unit unit = MakeBareUnit("флэт-через-множитель");
            unit.attackDamage = 100f;
            unit.ChangeDamage(10f);          // флэт наложен при множителе 1
            unit.ChangeDamage(0.5f, false);  // множитель стал 1.5
            unit.ChangeDamage(-10f);         // флэт снят уже при множителе 1.5

            Unit reference = MakeBareUnit("эталон-только-процент");
            reference.attackDamage = 100f;
            reference.ChangeDamage(0.5f, false);

            Assert.That(unit.attackDamage, Is.EqualTo(reference.attackDamage).Within(Tolerance),
                        "после снятия флэта должно остаться ровно то же, что даёт один процентный баф");
            Assert.That(unit.attackDamage, Is.EqualTo(150f).Within(Tolerance));
        }

        [Test]
        public void Урон_ПроцентНаложенПриФлэтеСнятПослеНего_ВозвращаетИсходное()
        {
            // Обратный порядок снятия к предыдущему тесту: процент снимается раньше флэта.
            Unit unit = MakeBareUnit();
            unit.attackDamage = 100f;

            unit.ChangeDamage(0.5f, false);
            unit.ChangeDamage(10f);
            unit.ChangeDamage(-0.5f, true);
            unit.ChangeDamage(-10f);

            Assert.That(unit.attackDamage, Is.EqualTo(100f).Within(Tolerance));
            Assert.That(unit.passiveEffects.damageChange, Is.EqualTo(1f).Within(Tolerance));
        }

        // ============================================================== дрейф ==

        [Test]
        public void Дрейф_100ЦикловПроцентТудаОбратно_НеНакапливается()
        {
            // Опорное число для шага «реестр модификаторов»: после перехода дрейф обязан стать
            // нулевым при ЛЮБЫХ значениях, а не только при удобных.
            AssertDriftIsStable(100f, 0.2f);       // удобная пара: дрейфа нет вовсе
            AssertDriftIsStable(418.05f, 0.395f);  // неудобная пара: дрейф в 1 ULP и дальше не растёт
        }

        /// <summary>
        /// Гоняет 100 циклов «наложить процент — снять процент» и проверяет, что после первого
        /// цикла значение больше не меняется: модель садится в неподвижную точку, ошибка
        /// не накапливается. Дельту печатает в вывод теста — это опорное число для шага 3.
        /// </summary>
        void AssertDriftIsStable(float startDamage, float percentage)
        {
            Unit unit = MakeBareUnit("дрейф-" + startDamage);
            unit.attackDamage = startDamage;

            unit.ChangeDamage(percentage, false);
            unit.ChangeDamage(-percentage, true);
            float afterFirst = unit.attackDamage;

            for (int i = 1; i < 100; i++)
            {
                unit.ChangeDamage(percentage, false);
                unit.ChangeDamage(-percentage, true);
            }

            float after100 = unit.attackDamage;

            TestContext.WriteLine("урон " + startDamage.ToString("R") + ", цикл ±" + (percentage * 100f) + " %: " +
                                  "после 1 цикла " + afterFirst.ToString("R") +
                                  ", после 100 " + after100.ToString("R") +
                                  ", дельта от исходного " + (after100 - startDamage).ToString("R"));

            Assert.That(after100, Is.EqualTo(afterFirst),
                        "дрейф накапливается: значение после 100 циклов разошлось со значением после первого");
            Assert.That(after100, Is.EqualTo(startDamage).Within(Tolerance),
                        "дрейф вышел за допуск " + Tolerance);
            Assert.That(unit.passiveEffects.damageChange, Is.EqualTo(1f).Within(Tolerance),
                        "множитель не вернулся к единице");
        }

        // ================================================ скорость атаки (интервал) ==

        [Test]
        public void СкоростьАтаки_ПлюсПроцент_УМЕНЬШАЕТИнтервал()
        {
            // attackSpeed — интервал между ударами, поэтому «+50 % скорости» делит поле, а не умножает.
            Unit unit = MakeBareUnit();
            unit.attackSpeed = 1f;

            unit.ChangeAttackSpeed(0.5f, false);

            Assert.That(unit.attackSpeed, Is.LessThan(1f), "интервал обязан сократиться");
            Assert.That(unit.attackSpeed, Is.EqualTo(1f / 1.5f).Within(Tolerance));
            Assert.That(unit.passiveEffects.attackSpeedChange, Is.EqualTo(1f / 1.5f).Within(Tolerance));
        }

        [Test]
        public void СкоростьАтаки_МинусПроцент_УВЕЛИЧИВАЕТИнтервал()
        {
            Unit unit = MakeBareUnit();
            unit.attackSpeed = 1f;

            unit.ChangeAttackSpeed(-0.5f, false);

            Assert.That(unit.attackSpeed, Is.EqualTo(1.5f).Within(Tolerance));
            Assert.That(unit.passiveEffects.attackSpeedChange, Is.EqualTo(1.5f).Within(Tolerance));
        }

        [Test]
        public void СкоростьАтаки_ФлэтИПроцентИмеютПротивоположныйСмыслЗнака()
        {
            // Плюс во ФЛЭТЕ прибавляется к интервалу, то есть замедляет юнита;
            // плюс в ПРОЦЕНТЕ интервал делит, то есть ускоряет. Это текущее поведение, не опечатка теста.
            Unit flat = MakeBareUnit("флэт");
            flat.attackSpeed = 1f;
            flat.ChangeAttackSpeed(0.5f);

            Unit percent = MakeBareUnit("процент");
            percent.attackSpeed = 1f;
            percent.ChangeAttackSpeed(0.5f, false);

            Assert.That(flat.attackSpeed, Is.EqualTo(1.5f).Within(Tolerance), "флэт +0.5 удлиняет интервал");
            Assert.That(percent.attackSpeed, Is.LessThan(1f), "процент +50 % укорачивает интервал");
        }

        [Test]
        public void СкоростьАтаки_ПроцентТудаОбратно_ВозвращаетЗначениеИМножитель()
        {
            Unit unit = MakeBareUnit();
            unit.attackSpeed = 1f;

            unit.ChangeAttackSpeed(0.5f, false);
            unit.ChangeAttackSpeed(-0.5f, true);

            Assert.That(unit.attackSpeed, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(unit.passiveEffects.attackSpeedChange, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void СкоростьАтаки_ФлэтТудаОбратно_ВозвращаетИсходное()
        {
            Unit unit = MakeBareUnit();
            unit.attackSpeed = 1f;

            unit.ChangeAttackSpeed(0.25f);
            Assert.That(unit.attackSpeed, Is.EqualTo(1.25f).Within(Tolerance));

            unit.ChangeAttackSpeed(-0.25f);
            Assert.That(unit.attackSpeed, Is.EqualTo(1f).Within(Tolerance));
        }

        // ============================================================== броня ==

        [Test]
        public void Броня_ФлэтТудаОбратно_ВозвращаетИсходное()
        {
            Unit unit = MakeBareUnit();
            unit.armor = 20f;

            unit.ChangeArmor(5f);
            Assert.That(unit.armor, Is.EqualTo(25f).Within(Tolerance));

            unit.ChangeArmor(-5f);
            Assert.That(unit.armor, Is.EqualTo(20f).Within(Tolerance));
        }

        [Test]
        public void Броня_ПроцентТудаОбратно_ВозвращаетИсходноеИМножитель()
        {
            Unit unit = MakeBareUnit();
            unit.armor = 20f;

            unit.ChangeArmor(0.5f, false);
            Assert.That(unit.armor, Is.EqualTo(30f).Within(Tolerance));
            Assert.That(unit.passiveEffects.armorChange, Is.EqualTo(1.5f).Within(Tolerance));

            unit.ChangeArmor(-0.5f, true);
            Assert.That(unit.armor, Is.EqualTo(20f).Within(Tolerance));
            Assert.That(unit.passiveEffects.armorChange, Is.EqualTo(1f).Within(Tolerance));
        }

        // ================================================== максимумы ХП и МП ==

        [Test]
        public void МаксХП_Флэт_ПриМножителеОдин_СохраняетДолюТекущего()
        {
            Unit unit = MakeBareUnit();
            unit.maxHealth = 100f;
            unit.health = 60f;

            unit.ChangeMaxHP(50f);

            Assert.That(unit.maxHealth, Is.EqualTo(150f).Within(Tolerance));
            Assert.That(unit.health, Is.EqualTo(90f).Within(Tolerance), "60 % от максимума обязаны сохраниться");
        }

        [Test]
        public void МаксХП_ФлэтТудаОбратно_ПриМножителеОдин_Возвращает()
        {
            Unit unit = MakeBareUnit();
            unit.maxHealth = 100f;
            unit.health = 60f;

            unit.ChangeMaxHP(50f);
            unit.ChangeMaxHP(-50f);

            Assert.That(unit.maxHealth, Is.EqualTo(100f).Within(Tolerance));
            Assert.That(unit.health, Is.EqualTo(60f).Within(Tolerance));
        }

        [Test]
        public void МаксХП_ПроцентТудаОбратно_ВозвращаетМаксимумТекущееИМножитель()
        {
            Unit unit = MakeBareUnit();
            unit.maxHealth = 100f;
            unit.health = 60f;

            unit.ChangeMaxHP(0.5f, false);
            Assert.That(unit.maxHealth, Is.EqualTo(150f).Within(Tolerance));
            Assert.That(unit.health, Is.EqualTo(90f).Within(Tolerance));
            Assert.That(unit.passiveEffects.healthChange, Is.EqualTo(1.5f).Within(Tolerance));

            unit.ChangeMaxHP(-0.5f, true);
            Assert.That(unit.maxHealth, Is.EqualTo(100f).Within(Tolerance));
            Assert.That(unit.health, Is.EqualTo(60f).Within(Tolerance));
            Assert.That(unit.passiveEffects.healthChange, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void МаксМП_ФлэтТудаОбратно_ПриМножителеОдин_Возвращает()
        {
            Unit unit = MakeBareUnit();
            unit.maxMana = 200f;
            unit.mana = 50f;

            unit.ChangeMaxMP(100f);
            Assert.That(unit.maxMana, Is.EqualTo(300f).Within(Tolerance));
            Assert.That(unit.mana, Is.EqualTo(75f).Within(Tolerance));

            unit.ChangeMaxMP(-100f);
            Assert.That(unit.maxMana, Is.EqualTo(200f).Within(Tolerance));
            Assert.That(unit.mana, Is.EqualTo(50f).Within(Tolerance));
        }

        [Test]
        public void МаксМП_ПроцентТудаОбратно_ВозвращаетМаксимумТекущееИМножитель()
        {
            Unit unit = MakeBareUnit();
            unit.maxMana = 200f;
            unit.mana = 50f;

            unit.ChangeMaxMP(0.5f, false);
            Assert.That(unit.maxMana, Is.EqualTo(300f).Within(Tolerance));
            Assert.That(unit.mana, Is.EqualTo(75f).Within(Tolerance));
            Assert.That(unit.passiveEffects.manaChange, Is.EqualTo(1.5f).Within(Tolerance));

            unit.ChangeMaxMP(-0.5f, true);
            Assert.That(unit.maxMana, Is.EqualTo(200f).Within(Tolerance));
            Assert.That(unit.mana, Is.EqualTo(50f).Within(Tolerance));
            Assert.That(unit.passiveEffects.manaChange, Is.EqualTo(1f).Within(Tolerance));
        }

        // ============================================== текущие ХП и МП (noSync) ==

        [Test]
        public void ТекущееХП_ВышеМаксимума_ЗажимаетсяКМаксимумуИНеУбивает()
        {
            Unit unit = MakeBareUnit();
            unit.maxHealth = 100f;
            unit.health = 50f;

            bool died = unit.ChangeHP(80f, true);

            Assert.That(unit.health, Is.EqualTo(100f).Within(Tolerance));
            Assert.IsFalse(died);
        }

        [Test]
        public void ТекущееХП_НижеПорога005_ОбнуляетсяИВозвращаетTrue()
        {
            Unit unit = MakeBareUnit();
            unit.maxHealth = 100f;
            unit.health = 50f;

            bool died = unit.ChangeHP(-50f, true);

            Assert.That(unit.health, Is.EqualTo(0f).Within(Tolerance));
            Assert.IsTrue(died, "падение до нуля обязано вернуть true");
        }

        [Test]
        public void ТекущееХП_ОбычноеИзменение_ВозвращаетFalse()
        {
            Unit unit = MakeBareUnit();
            unit.maxHealth = 100f;
            unit.health = 50f;

            bool died = unit.ChangeHP(-10f, true);

            Assert.That(unit.health, Is.EqualTo(40f).Within(Tolerance));
            Assert.IsFalse(died);
        }

        [Test]
        public void ТекущееМП_ЗажимаетсяВДиапазонеОтНуляДоМаксимума()
        {
            Unit unit = MakeBareUnit();
            unit.maxMana = 100f;
            unit.mana = 50f;

            unit.ChangeMP(80f, true);
            Assert.That(unit.mana, Is.EqualTo(100f).Within(Tolerance));

            unit.ChangeMP(-500f, true);
            Assert.That(unit.mana, Is.EqualTo(0f).Within(Tolerance));
        }

        // ======================================================== регенерация ==

        [Test]
        public void РегенХП_ФлэтИПроцентТудаОбратно_Возвращают()
        {
            Unit unit = MakeBareUnit();
            unit.healthRegen = 4f;

            unit.ChangeHealthRegen(2f);
            Assert.That(unit.healthRegen, Is.EqualTo(6f).Within(Tolerance));
            unit.ChangeHealthRegen(-2f);
            Assert.That(unit.healthRegen, Is.EqualTo(4f).Within(Tolerance));

            unit.ChangeHealthRegen(0.5f, false);
            Assert.That(unit.healthRegen, Is.EqualTo(6f).Within(Tolerance));
            Assert.That(unit.passiveEffects.healthRegenChange, Is.EqualTo(1.5f).Within(Tolerance));
            unit.ChangeHealthRegen(-0.5f, true);
            Assert.That(unit.healthRegen, Is.EqualTo(4f).Within(Tolerance));
            Assert.That(unit.passiveEffects.healthRegenChange, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void РегенМП_ФлэтИПроцентТудаОбратно_Возвращают()
        {
            Unit unit = MakeBareUnit();
            unit.manaRegen = 4f;

            unit.ChangeManaRegen(2f);
            Assert.That(unit.manaRegen, Is.EqualTo(6f).Within(Tolerance));
            unit.ChangeManaRegen(-2f);
            Assert.That(unit.manaRegen, Is.EqualTo(4f).Within(Tolerance));

            unit.ChangeManaRegen(0.5f, false);
            Assert.That(unit.manaRegen, Is.EqualTo(6f).Within(Tolerance));
            Assert.That(unit.passiveEffects.manaRegenChange, Is.EqualTo(1.5f).Within(Tolerance));
            unit.ChangeManaRegen(-0.5f, true);
            Assert.That(unit.manaRegen, Is.EqualTo(4f).Within(Tolerance));
            Assert.That(unit.passiveEffects.manaRegenChange, Is.EqualTo(1f).Within(Tolerance));
        }

        // ====================================================== дальность атаки ==

        [Test]
        public void Дальность_ДалекоОтПорога_ФлэтТудаОбратно_ВозвращаетИСнимаетБлижнийБой()
        {
            // unitRadius = 0.5 по умолчанию, порог флэтовой перегрузки = unitRadius + 0.1 = 0.6.
            // Значения заведомо выше порога, поэтому кламп (дефект Д2) в этот тест не лезет.
            Unit unit = MakeBareUnit();
            unit.attackRange = 5f;

            unit.ChangeAttackRange(2f);
            Assert.That(unit.attackRange, Is.EqualTo(7f).Within(Tolerance));
            Assert.IsFalse(unit.melee, "дальность выше порога обязана снять признак ближнего боя");

            unit.ChangeAttackRange(-2f);
            Assert.That(unit.attackRange, Is.EqualTo(5f).Within(Tolerance));
            Assert.IsFalse(unit.melee);
        }

        [Test]
        public void Дальность_ДалекоОтПорога_ПроцентТудаОбратно_ВозвращаетИМножитель()
        {
            Unit unit = MakeBareUnit();
            unit.attackRange = 5f;

            unit.ChangeAttackRange(0.5f, false);
            Assert.That(unit.attackRange, Is.EqualTo(7.5f).Within(Tolerance));
            Assert.That(unit.passiveEffects.attackRangeChange, Is.EqualTo(1.5f).Within(Tolerance));

            unit.ChangeAttackRange(-0.5f, true);
            Assert.That(unit.attackRange, Is.EqualTo(5f).Within(Tolerance));
            Assert.That(unit.passiveEffects.attackRangeChange, Is.EqualTo(1f).Within(Tolerance));
        }

        // ================================================================ опыт ==

        [Test]
        public void Опыт_ЦелыеЗначения_ФлэтТудаОбратно_Возвращает()
        {
            // На целых числах каст (int) ничего не теряет. Потеря — в UnitStatDefectTests (Д4).
            Unit unit = MakeBareUnit();
            unit.xpReward = 10;

            unit.ChangeXpReward(5f);
            Assert.AreEqual(15, unit.xpReward);

            unit.ChangeXpReward(-5f);
            Assert.AreEqual(10, unit.xpReward);
        }

        [Test]
        public void Опыт_ЦелыеЗначения_ПроцентТудаОбратно_ВозвращаетИМножитель()
        {
            Unit unit = MakeBareUnit();
            unit.xpReward = 10;

            unit.ChangeXpReward(0.5f, false);
            Assert.AreEqual(15, unit.xpReward);
            Assert.That(unit.passiveEffects.xpRewardChange, Is.EqualTo(1.5f).Within(Tolerance));

            unit.ChangeXpReward(-0.5f, true);
            Assert.AreEqual(10, unit.xpReward);
            Assert.That(unit.passiveEffects.xpRewardChange, Is.EqualTo(1f).Within(Tolerance));
        }

        // ============================================== простые присваивания ==

        [Test]
        public void ТипУрона_Присваивается()
        {
            Unit unit = MakeBareUnit();
            DamageType type = MakeAsset<DamageType>();

            unit.ChangeDamageType(type);

            Assert.AreSame(type, unit.damageType);
        }

        [Test]
        public void ТипБрони_Присваивается()
        {
            Unit unit = MakeBareUnit();
            ArmorType type = MakeAsset<ArmorType>();

            unit.ChangeArmorType(type);

            Assert.AreSame(type, unit.armorType);
        }

        [Test]
        public void Селектор_Непустой_ВключаетАтакуИПринудительноГаситНевидимость()
        {
            Unit unit = MakeBareUnit();

            // isEnemy + isUnit + isGround + includeInvisible
            unit.ChangeAttackSelector(new UnitSelector(false, false, true, true, false, false, false,
                                                       true, false, false, true, false));

            Assert.IsTrue(unit.canAttack);
            Assert.IsFalse(unit.attackUnitSelector.includeInvisible,
                           "Unit.Init.cs:454 гасит невидимость принудительно — прямая атака по невидимым запрещена");
            Assert.IsTrue(unit.searchUnitSelector.isEnemy, "поисковый селектор всегда враждебный");
            Assert.IsTrue(unit.splashUnitSelector.includeInvisible, "сплэшу невидимость наоборот разрешена");
        }

        [Test]
        public void Селектор_Пустой_АтакуНеВключает()
        {
            Unit unit = MakeBareUnit();

            unit.ChangeAttackSelector(new UnitSelector(false, false, false, false, false, false, false,
                                                       false, false, false, false, false));

            Assert.IsFalse(unit.canAttack);
        }

        [Test]
        public void Сплэш_ПоляПрисваиваютсяКакЕсть()
        {
            Unit unit = MakeBareUnit();

            unit.ChangeSplash(true, 3.5f, 0.4f, false);

            Assert.IsTrue(unit.isSplash);
            Assert.That(unit.splashRadius, Is.EqualTo(3.5f).Within(Tolerance));
            Assert.That(unit.splashReduction, Is.EqualTo(0.4f).Within(Tolerance));
            Assert.IsFalse(unit.projectileFollowTarget);
        }

        [Test]
        public void Мультитаргет_ПриБлижнемБою_ЗаполняетМассивЦелейИНеТрогаетСнаряд()
        {
            // melee = true по умолчанию, поэтому «!melee && ...» короткозамкнут и projectileGO
            // (null у голого юнита) не разыменовывается — Unit.Parameters.cs:411.
            Unit unit = MakeBareUnit();

            unit.ChangeMultitarget(true, 3);

            Assert.IsTrue(unit.multiTarget);
            Assert.AreEqual(3, unit.multiTargetCount);
            Assert.IsNotNull(unit.additionalTargets);
            Assert.AreEqual(4, unit.additionalTargets.Length, "массив на одну ячейку длиннее счётчика");
        }

        [Test]
        public void Отскок_ПриБлижнемБою_ПоляПрисваиваютсяКакЕсть()
        {
            Unit unit = MakeBareUnit();

            unit.ChangeBounce(2, 6f, 0.5f);

            Assert.AreEqual(2, unit.bounceCount);
            Assert.That(unit.bounceRange, Is.EqualTo(6f).Within(Tolerance));
            Assert.That(unit.bounceReduction, Is.EqualTo(0.5f).Within(Tolerance));
        }

        // ============================ непокрываемое: тесты-документация ограничений ==

        [Test]
        public void СкоростьХода_Флэт_БезNavMeshAgent_БросаетNRE_НоПолеУжеИзменено()
        {
            // Unit.Parameters.cs:508-510 разыменовывает agent без проверки. Поле moveSpeed при этом
            // успевает измениться на :506 — юнит остаётся в полусостоянии.
            // Тест обязан упасть после починки: тогда его меняют на проверку корректного поведения.
            Unit unit = MakeBareUnit();
            unit.moveSpeed = 5f;

            Assert.Throws<System.NullReferenceException>(() => unit.ChangeMoveSpeed(1f));
            Assert.That(unit.moveSpeed, Is.EqualTo(6f).Within(Tolerance),
                        "значение изменено до броска — правка неатомарна");
        }

        [Test]
        public void СкоростьХода_Процент_БезNavMeshAgent_БросаетNRE_НоПолеУжеИзменено()
        {
            Unit unit = MakeBareUnit();
            unit.moveSpeed = 5f;

            Assert.Throws<System.NullReferenceException>(() => unit.ChangeMoveSpeed(0.5f, false));
            Assert.That(unit.moveSpeed, Is.EqualTo(7.5f).Within(Tolerance));
            Assert.That(unit.passiveEffects.moveSpeedChange, Is.EqualTo(1.5f).Within(Tolerance),
                        "множитель тоже уже записан — откатывать после броска некому");
        }

        [Test]
        public void SetHP_БезNetworkManager_БросаетNRE_НоПолеУжеИзменено()
        {
            // Сетевой блок в SetHP безусловный (Unit.Parameters.cs:101), параметра noSync у метода нет.
            // Первым падает NetworkManager.Singleton (в EditMode он null), а не NetworkDataSync.instance.
            Unit unit = MakeBareUnit();
            unit.maxHealth = 100f;
            unit.health = 10f;

            Assert.Throws<System.NullReferenceException>(() => unit.SetHP(42f));
            Assert.That(unit.health, Is.EqualTo(42f).Within(Tolerance));
        }

        [Test]
        public void SetMP_БезNetworkManager_БросаетNRE_НоПолеУжеИзменено()
        {
            Unit unit = MakeBareUnit();
            unit.maxMana = 100f;
            unit.mana = 10f;

            Assert.Throws<System.NullReferenceException>(() => unit.SetMP(42f));
            Assert.That(unit.mana, Is.EqualTo(42f).Within(Tolerance));
        }
    }
}
