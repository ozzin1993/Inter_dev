using NUnit.Framework;
using UnityEngine;

namespace StrategyCore.Tests
{
    /// <summary>
    /// Возврат ДЕРЖАТЕЛЯ наложения приёмником состояний и воронкой (решение Artsiom 13.09.2026).
    ///
    /// Зачем тесты: на возврате держателя сидит досрочное снятие значка подготовки
    /// (<c>WeaponDeployment</c>). Держатель обязан быть ТЕМ САМЫМ, что лежит в <c>unit.effectors</c>,
    /// иначе снятие либо промахнётся, либо снимет чужое наложение.
    ///
    /// Оснастка — <see cref="UnitTestHarness"/>: юнит голый (без <c>Initialize()</c>), ассет состояния
    /// одноразовый в памяти. Ассет берётся самый безобидный: без значка и VFX (канал статусов молчит),
    /// без пассивных эффектов, категория «Нет» (сопротивления не участвуют), контроля не несёт —
    /// проверяется именно проводка возврата, а не арифметика приёмника.
    ///
    /// Оба отказа приёмника (иммунитет к контролю, сопротивление от 100 %) проверены: на них висит
    /// контракт «null — держателя нет, снимать нечего», на который опирается <c>WeaponDeployment</c>.
    ///
    /// Чего здесь НЕТ и почему: (1) ветка «мёртвый» воронки — флаг <c>Unit.dead</c> с приватным
    /// сеттером, а <c>Die()</c> в EditMode не проходит (нужен <c>GameManager.Instance</c>); ветка
    /// устроена ровно как проверенная ниже ветка «здание» — тот же ранний <c>return null</c>.
    /// (2) Сам <c>WeaponDeployment</c>: его настройки приватны и сериализованы, выставить их без
    /// хака приватного состояния нельзя (правило 9) — компонент проверяется на полигоне.
    /// </summary>
    public class ReceiverStatusHolderTests : UnitTestHarness
    {
        GameObject slotManagerHost;

        [TearDown]
        public void DestroySlotManagerHost()
        {
            if (slotManagerHost != null) Object.DestroyImmediate(slotManagerHost);
            slotManagerHost = null;
        }

        /// <summary>Ассет состояния-значка: ничего, кроме длительности, не делает.</summary>
        Effector MakeStatus(int id = 777, float duration = 1.75f)
        {
            Effector status = MakeAsset<Effector>();
            status.id = id;
            status.duration = duration;
            return status;
        }

        /// <summary>
        /// Приёмнику нужен <c>SlotManager.Instance</c> — им он сверяет команды при слипании наложений
        /// (<c>UnitReceiver.Statuses.cs</c>) и при снятии (<c>Effector.EffectorRemove</c>). Поднимаем
        /// штатным публичным <c>InstanceSet()</c>; массив <c>playerTeam</c> приходит инициализатором
        /// поля, все слоты в команде 0 — юнит и источник оказываются своими друг другу.
        /// </summary>
        void EnsureSlotManager()
        {
            if (SlotManager.Instance != null) return;

            slotManagerHost = new GameObject("TestSlotManager");
            slotManagerHost.hideFlags = HideFlags.HideAndDontSave;
            slotManagerHost.AddComponent<SlotManager>().InstanceSet();
        }

        EffectorHolder Receive(Unit unit, Effector status, float durationOverride = -1f, float power = 1f)
        {
            return unit.ReceiverEnsure().Receive(new EffectorPacket(status, unit, unit.owner, 0f, power, durationOverride));
        }

        // ======================================================= ПРИЁМНИК =========

        [Test]
        public void Приёмник_НовоеНаложение_ОтдаётТотЖеДержатель_ЧтоЛёгВСписок()
        {
            Unit unit = MakeBareUnit();

            EffectorHolder holder = Receive(unit, MakeStatus());

            Assert.IsNotNull(holder, "приёмник обязан отдать держатель нового наложения");
            Assert.AreEqual(1, unit.effectors.Count);
            Assert.AreSame(unit.effectors[0], holder, "отдан должен быть ТОТ САМЫЙ держатель из списка юнита");
        }

        [Test]
        public void Приёмник_ПовторноеНаложение_ТойЖеСилыИДлительности_ОтдаётТотЖеДержатель()
        {
            EnsureSlotManager();

            Unit unit = MakeBareUnit();
            Effector status = MakeStatus();

            EffectorHolder first = Receive(unit, status);
            EffectorHolder second = Receive(unit, status);

            Assert.AreEqual(1, unit.effectors.Count, "одинаковые наложения слипаются, второго держателя нет");
            Assert.AreSame(first, second, "ветка «продлено» обязана отдать уже висящий держатель");
        }

        [Test]
        public void Приёмник_ПовторноеНаложение_ДругойДлительности_ОтдаётВторойДержатель()
        {
            EnsureSlotManager();

            Unit unit = MakeBareUnit();
            Effector status = MakeStatus();

            EffectorHolder first = Receive(unit, status, durationOverride: 1.75f);
            EffectorHolder second = Receive(unit, status, durationOverride: 3f);

            Assert.AreEqual(2, unit.effectors.Count, "разная длительность — разные наложения, они сосуществуют");
            Assert.AreNotSame(first, second);
            Assert.AreSame(unit.effectors[1], second);
        }

        [Test]
        public void Приёмник_ОтбитоИммунитетомККонтролю_ОтдаётNull()
        {
            Unit unit = MakeBareUnit();
            unit.gameObject.AddComponent<ControlImmunity>().Add();   // штатный публичный рефкаунт источников

            Effector stun = MakeStatus(id: 778);
            stun.stuns = true;

            EffectorHolder holder = Receive(unit, stun);

            Assert.IsNull(holder, "отбитое иммунитетом наложение держателя не даёт");
            Assert.AreEqual(0, unit.effectors.Count);
        }

        [Test]
        public void Приёмник_ОтбитоСопротивлениемОт100Процентов_ОтдаётNull()
        {
            Unit unit = MakeBareUnit();
            unit.gameObject.AddComponent<UnitResistances>().Add(this, EffectorCategory.MoveSlow, 1f);

            Effector slow = MakeStatus(id: 779);
            slow.category = EffectorCategory.MoveSlow;

            EffectorHolder holder = Receive(unit, slow);

            Assert.IsNull(holder, "отбитое сопротивлением наложение держателя не даёт");
            Assert.AreEqual(0, unit.effectors.Count);
        }

        // ======================================================== ВОРОНКА =========

        [Test]
        public void Воронка_ЖивойЮнит_ПробрасываетДержательПриёмника()
        {
            Unit unit = MakeBareUnit();
            Effector status = MakeStatus();

            EffectorHolder holder = Effector.EffectorAdd(unit, status, unit, unit.owner, durationOverride: 1.75f);

            Assert.IsNotNull(holder, "воронка обязана вернуть то, что вернул приёмник");
            Assert.AreEqual(1, unit.effectors.Count);
            Assert.AreSame(unit.effectors[0], holder);
        }

        [Test]
        public void Воронка_Здание_ОтдаётNull_ИНичегоНеНакладывает()
        {
            Unit building = MakeBareUnit("TestBuilding");
            building.unitType = UnitType.Building;

            EffectorHolder holder = Effector.EffectorAdd(building, MakeStatus(), building, building.owner);

            Assert.IsNull(holder, "на здания состояния не вешаются — держателя нет");
            Assert.AreEqual(0, building.effectors.Count);
        }

        // ========================================================== СНЯТИЕ ========

        [Test]
        public void Снятие_ПоДержателю_УбираетТолькоСвоёНаложение()
        {
            EnsureSlotManager();

            Unit unit = MakeBareUnit();
            Effector status = MakeStatus();

            // Два наложения ОДНОГО ассета на одном юните: разная длительность не слипается.
            // Ровно этот случай и делал поиск по ссылке на ассет негодным — снялось бы первое попавшееся.
            EffectorHolder mine = Effector.EffectorAdd(unit, status, unit, unit.owner, durationOverride: 1.75f);
            EffectorHolder other = Effector.EffectorAdd(unit, status, unit, unit.owner, durationOverride: 3f);

            Effector.EffectorRemove(unit, mine);

            Assert.AreEqual(1, unit.effectors.Count);
            Assert.AreSame(other, unit.effectors[0], "снято должно быть наложение своего держателя, чужое остаётся");
        }
    }
}
