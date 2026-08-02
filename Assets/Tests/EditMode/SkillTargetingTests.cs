using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StrategyCore.Tests
{
    /// <summary>
    /// Единый исполнитель стратегий выбора цели (SkillTargeting) — 10 значений SkillTargetStrategy.
    ///
    /// Зачем эти тесты: в проекте компонент AutoAbilityUser стоит на ОДНОМ префабе (humanHERO,
    /// стратегия NearestEnemy), поэтому прогон матча даёт «зелёный» при любом дефекте в остальных
    /// алгоритмах. Это единственная проверка, которая их вообще задевает.
    ///
    /// Всё считается на голых GameObject с HideAndDontSave: алгоритмы — чистый выбор без побочных
    /// эффектов, сцена и сеть им не нужны, а флаг не даёт тестам пачкать открытую сцену.
    ///
    /// НЕ покрыто сознательно:
    ///   — ветка «пропустить мёртвого» (Unit.dead — свойство с приватным сеттером, а Unit.Die
    ///     в Edit Mode падает: ему нужен живой сетевой контекст). Ветка «пропустить уничтоженного»
    ///     покрыта — она ловит тот же практический случай через Unity-null;
    ///   — DensestCluster с радиусом больше нуля: он зовёт Utils.GetUnitsInRadius, которому нужен
    ///     реестр юнитов сцены. Ветка «радиус ноль» покрыта.
    /// </summary>
    public class SkillTargetingTests
    {
        readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < spawned.Count; i++)
                if (spawned[i] != null) Object.DestroyImmediate(spawned[i]);

            spawned.Clear();
        }

        /// <summary>Юнит-пустышка: только те поля, которые читают стратегии.</summary>
        Unit MakeUnit(string name, Vector3 position, float health = 100f, float maxHealth = 100f,
                      Unit.UnitCategory category = Unit.UnitCategory.Fighter, int owner = 0)
        {
            GameObject go = new GameObject(name);
            go.hideFlags = HideFlags.HideAndDontSave;   // тесты не должны пачкать открытую сцену
            go.transform.position = position;

            Unit u = go.AddComponent<Unit>();
            u.health = health;
            u.maxHealth = maxHealth;
            u.unitCategory = category;
            u.owner = owner;

            spawned.Add(go);
            return u;
        }

        Unit MakeSelf(Vector3 position) => MakeUnit("__self", position);

        // ================================================================== Nearest ==

        [Test]
        public void Nearest_БерётБлижайшегоКТочкеОтсчёта()
        {
            Unit far = MakeUnit("far", new Vector3(10f, 0f, 0f));
            Unit near = MakeUnit("near", new Vector3(2f, 0f, 0f));
            Unit middle = MakeUnit("middle", new Vector3(5f, 0f, 0f));

            Assert.AreSame(near, SkillTargeting.Nearest(new[] { far, near, middle }, null, Vector3.zero));
        }

        [Test]
        public void Nearest_ТочкаОтсчётаМеняетОтвет()
        {
            Unit a = MakeUnit("a", new Vector3(2f, 0f, 0f));
            Unit b = MakeUnit("b", new Vector3(10f, 0f, 0f));
            Unit[] set = { a, b };

            Assert.AreSame(a, SkillTargeting.Nearest(set, null, Vector3.zero));
            Assert.AreSame(b, SkillTargeting.Nearest(set, null, new Vector3(12f, 0f, 0f)));
        }

        [Test]
        public void Nearest_ПропускаетСамогоКастера()
        {
            Unit self = MakeSelf(new Vector3(1f, 0f, 0f));
            Unit other = MakeUnit("other", new Vector3(9f, 0f, 0f));

            Assert.AreSame(other, SkillTargeting.Nearest(new[] { self, other }, self, Vector3.zero));
        }

        [Test]
        public void Nearest_ПропускаетУничтоженных()
        {
            Unit destroyed = MakeUnit("destroyed", new Vector3(1f, 0f, 0f));
            Unit alive = MakeUnit("alive", new Vector3(9f, 0f, 0f));
            Object.DestroyImmediate(destroyed.gameObject);

            Assert.AreSame(alive, SkillTargeting.Nearest(new[] { destroyed, alive }, null, Vector3.zero));
        }

        [Test]
        public void Nearest_НетКандидатов_ВозвращаетNull()
        {
            Assert.IsNull(SkillTargeting.Nearest(null, null, Vector3.zero));
            Assert.IsNull(SkillTargeting.Nearest(new Unit[0], null, Vector3.zero));
        }

        // ========================================================= NearestOfCategory ==

        [Test]
        public void NearestOfCategory_БерётТолькоНужнуюРоль()
        {
            Unit closeFighter = MakeUnit("fighter", new Vector3(1f, 0f, 0f), category: Unit.UnitCategory.Fighter);
            Unit farMage = MakeUnit("mage", new Vector3(9f, 0f, 0f), category: Unit.UnitCategory.Mage);

            Assert.AreSame(farMage, SkillTargeting.NearestOfCategory(
                new[] { closeFighter, farMage }, null, Vector3.zero, Unit.UnitCategory.Mage));
        }

        [Test]
        public void NearestOfCategory_РолиНетСредиКандидатов_Null()
        {
            Unit fighter = MakeUnit("fighter", new Vector3(1f, 0f, 0f), category: Unit.UnitCategory.Fighter);

            Assert.IsNull(SkillTargeting.NearestOfCategory(
                new[] { fighter }, null, Vector3.zero, Unit.UnitCategory.Hero));
        }

        // ============================================================== MostWounded ==

        [Test]
        public void MostWounded_СчитаетДолюЗдоровья_АНеАбсолютнуюПотерю()
        {
            // 50 из 200 — доля 0.25; 30 из 100 — доля 0.3. Абсолютных ХП больше у первого,
            // но раненее он: выбор обязан идти по доле.
            Unit ratio025 = MakeUnit("ratio025", Vector3.zero, health: 50f, maxHealth: 200f);
            Unit ratio030 = MakeUnit("ratio030", Vector3.zero, health: 30f, maxHealth: 100f);

            Assert.AreSame(ratio025, SkillTargeting.MostWounded(new[] { ratio030, ratio025 }, null));
        }

        [Test]
        public void MostWounded_ПропускаетСНулевымМаксимумом()
        {
            Unit broken = MakeUnit("broken", Vector3.zero, health: 0f, maxHealth: 0f);
            Unit normal = MakeUnit("normal", Vector3.zero, health: 90f, maxHealth: 100f);

            Assert.AreSame(normal, SkillTargeting.MostWounded(new[] { broken, normal }, null));
        }

        [Test]
        public void MostWounded_НетКандидатов_Null()
        {
            Assert.IsNull(SkillTargeting.MostWounded(null, null));
            Assert.IsNull(SkillTargeting.MostWounded(new Unit[0], null));
        }

        // ================================================ MostWoundedBelowThreshold ==

        [Test]
        public void MostWoundedBelowThreshold_ВсеВышеПорога_Null()
        {
            Unit healthy = MakeUnit("healthy", Vector3.zero, health: 80f, maxHealth: 100f);

            Assert.IsNull(SkillTargeting.MostWoundedBelowThreshold(new[] { healthy }, null, 0.5f));
        }

        [Test]
        public void MostWoundedBelowThreshold_БерётСамогоРаненогоИзТехКтоНижеПорога()
        {
            Unit healthy = MakeUnit("healthy", Vector3.zero, health: 80f, maxHealth: 100f);
            Unit wounded = MakeUnit("wounded", Vector3.zero, health: 40f, maxHealth: 100f);
            Unit critical = MakeUnit("critical", Vector3.zero, health: 10f, maxHealth: 100f);

            Assert.AreSame(critical, SkillTargeting.MostWoundedBelowThreshold(
                new[] { healthy, wounded, critical }, null, 0.5f));
        }

        [Test]
        public void MostWoundedBelowThreshold_ПорогСтрогий_РовноНаПорогеНеБерётся()
        {
            Unit exactly = MakeUnit("exactly", Vector3.zero, health: 50f, maxHealth: 100f);

            Assert.IsNull(SkillTargeting.MostWoundedBelowThreshold(new[] { exactly }, null, 0.5f));
        }

        // ================================================================ Strongest ==

        [Test]
        public void Strongest_ПоМаксимальномуЗдоровью()
        {
            Unit bigPool = MakeUnit("bigPool", Vector3.zero, health: 10f, maxHealth: 500f);
            Unit fullSmall = MakeUnit("fullSmall", Vector3.zero, health: 100f, maxHealth: 100f);

            Assert.AreSame(bigPool, SkillTargeting.Strongest(new[] { bigPool, fullSmall }, null, false));
        }

        [Test]
        public void Strongest_ПоТекущемуЗдоровью()
        {
            Unit bigPool = MakeUnit("bigPool", Vector3.zero, health: 10f, maxHealth: 500f);
            Unit fullSmall = MakeUnit("fullSmall", Vector3.zero, health: 100f, maxHealth: 100f);

            Assert.AreSame(fullSmall, SkillTargeting.Strongest(new[] { bigPool, fullSmall }, null, true));
        }

        // ====================================================== CurrentAttackTarget ==

        [Test]
        public void CurrentAttackTarget_БерётЦельАтакиКастера_АНеКандидатов()
        {
            Unit self = MakeSelf(Vector3.zero);
            Unit victim = MakeUnit("victim", new Vector3(50f, 0f, 0f));
            self.target = victim;

            Assert.AreSame(victim, SkillTargeting.CurrentAttackTarget(self));
        }

        [Test]
        public void CurrentAttackTarget_ЦелиНет_Null()
        {
            Unit self = MakeSelf(Vector3.zero);

            Assert.IsNull(SkillTargeting.CurrentAttackTarget(self));
            Assert.IsNull(SkillTargeting.CurrentAttackTarget(null));
        }

        [Test]
        public void CurrentAttackTarget_ЦельУничтожена_Null()
        {
            Unit self = MakeSelf(Vector3.zero);
            Unit victim = MakeUnit("victim", Vector3.zero);
            self.target = victim;
            Object.DestroyImmediate(victim.gameObject);

            Assert.IsNull(SkillTargeting.CurrentAttackTarget(self));
        }

        // ============================================= RandomWithCategoryPriority ==

        [Test]
        public void RandomWithCategoryPriority_ПриоритетнаяРольЕсть_БерётТолькоИзНеё()
        {
            Unit fighter = MakeUnit("fighter", Vector3.zero, category: Unit.UnitCategory.Fighter);
            Unit tank = MakeUnit("tank", Vector3.zero, category: Unit.UnitCategory.Tank);
            Unit mage = MakeUnit("mage", Vector3.zero, category: Unit.UnitCategory.Mage);
            Unit[] set = { fighter, tank, mage };

            // Выбор случайный — проверяем инвариант на серии, а не единичный ответ.
            for (int i = 0; i < 40; i++)
                Assert.AreSame(mage, SkillTargeting.RandomWithCategoryPriority(set, null, Unit.UnitCategory.Mage));
        }

        [Test]
        public void RandomWithCategoryPriority_ПриоритетнойРолиНет_БерётИзВсех()
        {
            Unit fighter = MakeUnit("fighter", Vector3.zero, category: Unit.UnitCategory.Fighter);
            Unit tank = MakeUnit("tank", Vector3.zero, category: Unit.UnitCategory.Tank);
            Unit[] set = { fighter, tank };

            for (int i = 0; i < 40; i++)
            {
                Unit picked = SkillTargeting.RandomWithCategoryPriority(set, null, Unit.UnitCategory.Hero);
                Assert.IsTrue(picked == fighter || picked == tank, "выбран кто-то вне набора кандидатов");
            }
        }

        [Test]
        public void RandomWithCategoryPriority_НетЖивыхКандидатов_Null()
        {
            Unit self = MakeSelf(Vector3.zero);

            Assert.IsNull(SkillTargeting.RandomWithCategoryPriority(new[] { self }, self, Unit.UnitCategory.Tank));
        }

        // =========================================================== DensestCluster ==

        [Test]
        public void DensestCluster_НулевойРадиус_БерётКогоТоИзНабора()
        {
            Unit a = MakeUnit("a", Vector3.zero);
            Unit b = MakeUnit("b", new Vector3(3f, 0f, 0f));
            List<Unit> set = new List<Unit> { a, b };

            for (int i = 0; i < 20; i++)
            {
                Unit picked = SkillTargeting.DensestCluster(set, 0, 0f, default(UnitSelector));
                Assert.IsTrue(picked == a || picked == b, "выбран кто-то вне набора кандидатов");
            }
        }

        [Test]
        public void DensestCluster_ПустойНабор_Null()
        {
            Assert.IsNull(SkillTargeting.DensestCluster(null, 0, 0f, default(UnitSelector)));
            Assert.IsNull(SkillTargeting.DensestCluster(new List<Unit>(), 0, 0f, default(UnitSelector)));
        }

        // ============================================================= ДИСПЕТЧЕР Pick ==
        //
        // Набор подобран так, чтобы каждая стратегия давала СВОЙ ответ: перепутанная ветка
        // switch падает сразу, а не маскируется совпадением.
        //   near   — ближе всех к точке отсчёта, роль Tank, 100/100
        //   weak   — самый раненый (0.1), роль Mage, 10/100
        //   giant  — самый большой запас ХП (300/300), роль Fighter, дальше всех
        // Цель атаки кастера — giant.

        struct Fixture
        {
            public Unit self, near, weak, giant;
            public Unit[] candidates;
        }

        Fixture MakeFixture()
        {
            Fixture f = default;
            f.self = MakeSelf(Vector3.zero);
            f.near = MakeUnit("near", new Vector3(1f, 0f, 0f), 100f, 100f, Unit.UnitCategory.Tank);
            f.weak = MakeUnit("weak", new Vector3(5f, 0f, 0f), 10f, 100f, Unit.UnitCategory.Mage);
            f.giant = MakeUnit("giant", new Vector3(10f, 0f, 0f), 300f, 300f, Unit.UnitCategory.Fighter);
            f.self.target = f.giant;
            f.candidates = new[] { f.near, f.weak, f.giant };
            return f;
        }

        static SkillTargeting.Options OptionsFor(Unit.UnitCategory category = Unit.UnitCategory.Mage,
                                                 bool useCurrentHealth = false,
                                                 float hpThreshold = 0.5f,
                                                 float clusterRadius = 0f)
        {
            return new SkillTargeting.Options
            {
                category = category,
                useCurrentHealth = useCurrentHealth,
                hpThreshold = hpThreshold,
                clusterRadius = clusterRadius,
                clusterSelector = default(UnitSelector),
                origin = Vector3.zero
            };
        }

        [Test]
        public void Pick_NearestEnemy_ВедётВNearest()
        {
            Fixture f = MakeFixture();
            Assert.AreSame(f.near, SkillTargeting.Pick(
                SkillTargetStrategy.NearestEnemy, f.candidates, f.self, OptionsFor()));
        }

        [Test]
        public void Pick_NearestEnemyOfCategory_ВедётВNearestOfCategory()
        {
            Fixture f = MakeFixture();
            Assert.AreSame(f.weak, SkillTargeting.Pick(
                SkillTargetStrategy.NearestEnemyOfCategory, f.candidates, f.self, OptionsFor()));
        }

        [Test]
        public void Pick_AllyOfCategory_ВедётВТотЖеNearestOfCategory()
        {
            Fixture f = MakeFixture();
            Assert.AreSame(f.weak, SkillTargeting.Pick(
                SkillTargetStrategy.AllyOfCategory, f.candidates, f.self, OptionsFor()));
        }

        [Test]
        public void Pick_MostWoundedEnemy_ВедётВMostWounded()
        {
            Fixture f = MakeFixture();
            Assert.AreSame(f.weak, SkillTargeting.Pick(
                SkillTargetStrategy.MostWoundedEnemy, f.candidates, f.self, OptionsFor()));
        }

        [Test]
        public void Pick_MostWoundedAlly_ВедётВТотЖеMostWounded()
        {
            Fixture f = MakeFixture();
            Assert.AreSame(f.weak, SkillTargeting.Pick(
                SkillTargetStrategy.MostWoundedAlly, f.candidates, f.self, OptionsFor()));
        }

        [Test]
        public void Pick_StrongestEnemy_ВедётВStrongest_ПоМаксимуму()
        {
            Fixture f = MakeFixture();
            Assert.AreSame(f.giant, SkillTargeting.Pick(
                SkillTargetStrategy.StrongestEnemy, f.candidates, f.self, OptionsFor()));
        }

        [Test]
        public void Pick_StrongestEnemy_УважаетФлагТекущегоЗдоровья()
        {
            Fixture f = MakeFixture();
            // giant по-прежнему сильнейший и по текущему — проверяем, что флаг доезжает,
            // на отдельном наборе: у near текущее 100, у giant искусственно 5.
            f.giant.health = 5f;
            Assert.AreSame(f.near, SkillTargeting.Pick(
                SkillTargetStrategy.StrongestEnemy, f.candidates, f.self, OptionsFor(useCurrentHealth: true)));
        }

        [Test]
        public void Pick_CurrentAttackTarget_ВедётВЦельАтакиКастера()
        {
            Fixture f = MakeFixture();
            Assert.AreSame(f.giant, SkillTargeting.Pick(
                SkillTargetStrategy.CurrentAttackTarget, f.candidates, f.self, OptionsFor()));
        }

        [Test]
        public void Pick_RandomEnemyWithCategoryPriority_ВедётВПулПриоритетнойРоли()
        {
            Fixture f = MakeFixture();
            for (int i = 0; i < 20; i++)
                Assert.AreSame(f.weak, SkillTargeting.Pick(
                    SkillTargetStrategy.RandomEnemyWithCategoryPriority, f.candidates, f.self, OptionsFor()));
        }

        [Test]
        public void Pick_WoundedAllyBelowThreshold_ВедётВВыборПоПорогу()
        {
            Fixture f = MakeFixture();
            // Ниже порога 0.5 только weak (0.1) — near и giant целыми быть не должны.
            Assert.AreSame(f.weak, SkillTargeting.Pick(
                SkillTargetStrategy.WoundedAllyBelowThreshold, f.candidates, f.self, OptionsFor()));
        }

        [Test]
        public void Pick_WoundedAllyBelowThreshold_НиктоНеПросел_Null()
        {
            Fixture f = MakeFixture();
            f.weak.health = 100f;
            Assert.IsNull(SkillTargeting.Pick(
                SkillTargetStrategy.WoundedAllyBelowThreshold, f.candidates, f.self, OptionsFor()));
        }

        [Test]
        public void Pick_EnemyCluster_ВедётВВыборПоПлотности()
        {
            Fixture f = MakeFixture();
            Unit picked = SkillTargeting.Pick(
                SkillTargetStrategy.EnemyCluster, f.candidates, f.self, OptionsFor(clusterRadius: 0f));

            Assert.IsTrue(picked == f.near || picked == f.weak || picked == f.giant,
                          "выбран кто-то вне набора кандидатов");
        }

        // ============================================================== TakeTargets ==

        [Test]
        public void TakeTargets_ЛимитаНет_ПереноситВесьНабор()
        {
            Unit a = MakeUnit("a", Vector3.zero);
            Unit b = MakeUnit("b", new Vector3(1f, 0f, 0f));
            List<Unit> source = new List<Unit> { a, b };
            List<Unit> into = new List<Unit>();

            SkillTargeting.TakeTargets(source, Vector3.zero, 0, SkillTargeting.MultiPick.Nearest, into);

            CollectionAssert.AreEqual(source, into);
        }

        [Test]
        public void TakeTargets_ЦелейМеньшеЛимита_ПереноситВесьНабор()
        {
            Unit a = MakeUnit("a", Vector3.zero);
            List<Unit> source = new List<Unit> { a };
            List<Unit> into = new List<Unit>();

            SkillTargeting.TakeTargets(source, Vector3.zero, 5, SkillTargeting.MultiPick.Nearest, into);

            CollectionAssert.AreEqual(source, into);
        }

        [Test]
        public void TakeTargets_Ближайшие_БерётNБлижайшихИНеПортитИсточник()
        {
            Unit far = MakeUnit("far", new Vector3(10f, 0f, 0f));
            Unit near = MakeUnit("near", new Vector3(1f, 0f, 0f));
            Unit middle = MakeUnit("middle", new Vector3(5f, 0f, 0f));
            List<Unit> source = new List<Unit> { far, near, middle };
            List<Unit> into = new List<Unit>();

            SkillTargeting.TakeTargets(source, Vector3.zero, 2, SkillTargeting.MultiPick.Nearest, into);

            Assert.AreEqual(2, into.Count);
            Assert.AreSame(near, into[0]);
            Assert.AreSame(middle, into[1]);
            CollectionAssert.AreEqual(new List<Unit> { far, near, middle }, source, "исходный набор изменён");
        }

        [Test]
        public void TakeTargets_Случайные_БерётРовноNБезПовторов()
        {
            Unit a = MakeUnit("a", Vector3.zero);
            Unit b = MakeUnit("b", new Vector3(1f, 0f, 0f));
            Unit c = MakeUnit("c", new Vector3(2f, 0f, 0f));
            List<Unit> source = new List<Unit> { a, b, c };
            List<Unit> into = new List<Unit>();

            for (int i = 0; i < 20; i++)
            {
                SkillTargeting.TakeTargets(source, Vector3.zero, 2, SkillTargeting.MultiPick.Random, into);

                Assert.AreEqual(2, into.Count);
                Assert.AreNotSame(into[0], into[1], "один и тот же юнит попал в набор дважды");
                CollectionAssert.IsSubsetOf(into, source);
            }
        }

        [Test]
        public void TakeTargets_ИсточникПустИлиNull_РезультатОчищен()
        {
            List<Unit> into = new List<Unit> { MakeUnit("остаток", Vector3.zero) };

            SkillTargeting.TakeTargets(null, Vector3.zero, 3, SkillTargeting.MultiPick.Nearest, into);
            Assert.AreEqual(0, into.Count);

            into.Add(MakeUnit("остаток2", Vector3.zero));
            SkillTargeting.TakeTargets(new List<Unit>(), Vector3.zero, 3, SkillTargeting.MultiPick.Nearest, into);
            Assert.AreEqual(0, into.Count);
        }

        // ============================================================= WeightedPick ==

        [Test]
        public void WeightedPick_ПустойИлиNull_ВозвращаетНоль()
        {
            Assert.AreEqual(0, SkillTargeting.WeightedPick(null));
            Assert.AreEqual(0, SkillTargeting.WeightedPick(new float[0]));
        }

        [Test]
        public void WeightedPick_ВесьВесВПервомЭлементе_ВозвращаетПервый()
        {
            // r принадлежит [0; 10), первый же шаг даёт r - 10 < 0 — ответ детерминированный.
            for (int i = 0; i < 40; i++)
                Assert.AreEqual(0, SkillTargeting.WeightedPick(new[] { 10f, 0f, 0f }));
        }

        [Test]
        public void WeightedPick_ВсеВесаНулевые_ИндексВДиапазоне()
        {
            for (int i = 0; i < 40; i++)
            {
                int index = SkillTargeting.WeightedPick(new[] { 0f, 0f, 0f });
                Assert.IsTrue(index >= 0 && index < 3, "индекс вне диапазона: " + index);
            }
        }
    }
}
