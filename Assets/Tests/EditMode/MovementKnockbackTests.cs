using NUnit.Framework;
using UnityEngine;

namespace StrategyCore.Tests
{
    /// <summary>
    /// Шаг 4 слияния, семья «движение кастера, отброс целей, облик»
    /// (проект «Движение_Отброс_Облик_Проект» §4.5, решения Artsiom 47–58).
    ///
    /// Что здесь ПРОВЕРЯЕМО в режиме редактора:
    ///   • чистые функции — точка пролёта за целью, число и точки зон шлейфа;
    ///   • инварианты нумерации — коды событий и категории состояний только ДОПИСЫВАЮТСЯ в конец
    ///     (значения едут по сети числом и лежат в ассетах числом, перестановка развела бы пиры);
    ///   • разбор кодов у ассета умения (PresentationFor) — чистая выборка полей;
    ///   • ЕДИНСТВЕННОЕ ИСКЛЮЧЕНИЕ семьи: состояние «в полёте» проходит мимо иммунитета к контролю
    ///     и мимо сопротивления от 100 %, а флаг «летит» выводится из списка состояний.
    ///
    /// Чего здесь НЕТ и почему:
    ///   • синхронизированный вход перемещения (Knockback.MoveToSynced) — ему нужны навигационная
    ///     сетка, NavMeshAgent, Grid и FogOfWar; в режиме редактора ни одного из них нет;
    ///   • сам отброс и перемещение кастера (ApplyKnockback, ApplyCasterMove) — те же зависимости
    ///     плюс серверный гейт; проверяются на полигоне;
    ///   • показ полёта и облик на клиенте (SkillPresenter.Flight) — нужны кадры, сетевой пир
    ///     и живые рендереры; это Play Mode;
    ///   • порядок «статы до смены облика» при «облике на себя» — ApplyMorphTo зовёт Unit.Polymorph,
    ///     а тот ReplaceRenderers с Instantiate префаба; в EditMode неподъёмно. Порядок вызовов
    ///     сверен чтением (CompositeSkill.Shape.ApplyMorphTo) и назван в отчёте как непроверенный;
    ///   • выкладка шлейфа во времени — корутина MonoBehaviour; проверяемы только её чистые функции.
    ///
    /// Оснастка — <see cref="UnitTestHarness"/> (голый юнит, одноразовые ассеты).
    /// </summary>
    public class MovementKnockbackTests : UnitTestHarness
    {
        GameObject slotManagerHost;

        [TearDown]
        public void DestroySlotManagerHost()
        {
            if (slotManagerHost != null) Object.DestroyImmediate(slotManagerHost);
            slotManagerHost = null;
        }

        /// <summary>
        /// Приёмнику нужен <c>SlotManager.Instance</c>: им он сверяет команды при слипании наложений.
        /// Поднимается штатным публичным <c>InstanceSet()</c> — как в <c>ReceiverStatusHolderTests</c>.
        /// </summary>
        void EnsureSlotManager()
        {
            if (SlotManager.Instance != null) return;

            slotManagerHost = new GameObject("TestSlotManager");
            slotManagerHost.hideFlags = HideFlags.HideAndDontSave;
            slotManagerHost.AddComponent<SlotManager>().InstanceSet();
        }

        // ============================== ТОЧКА ПРОЛЁТА ЗА ЦЕЛЬЮ ==============================

        [Test]
        public void Пролёт_НулеваяДистанция_ДаётСамуТочкуПриложения()
        {
            Vector3 point = Knockback.PointBeyond(Vector3.zero, new Vector3(5f, 0f, 0f), Vector3.forward, 0f);

            Assert.AreEqual(5f, point.x, Tolerance);
            Assert.AreEqual(0f, point.z, Tolerance);
        }

        [Test]
        public void Пролёт_ЗаТочкуПриложения_ПоНаправлениюДвижения()
        {
            Vector3 point = Knockback.PointBeyond(Vector3.zero, new Vector3(5f, 0f, 0f), Vector3.forward, 2f);

            Assert.AreEqual(7f, point.x, Tolerance, "пролёт продолжает прямую «откуда → куда», а не взгляд");
            Assert.AreEqual(0f, point.z, Tolerance);
        }

        [Test]
        public void Пролёт_ВертикальНаправлениеНеЗадаёт()
        {
            // Кастер стоит выше точки приложения: перепад высот не должен разворачивать направление.
            Vector3 point = Knockback.PointBeyond(new Vector3(0f, 10f, 0f), new Vector3(0f, 0f, 4f),
                                                  Vector3.right, 1f);

            Assert.AreEqual(0f, point.x, Tolerance);
            Assert.AreEqual(5f, point.z, Tolerance, "направление считается по горизонтали");
        }

        [Test]
        public void Пролёт_КастерУжеВТочке_БерётВзгляд()
        {
            Vector3 same = new Vector3(3f, 0f, 3f);

            Vector3 point = Knockback.PointBeyond(same, same, Vector3.forward, 2f);

            Assert.AreEqual(3f, point.x, Tolerance);
            Assert.AreEqual(5f, point.z, Tolerance, "направления нет — пролёт идёт по взгляду кастера");
        }

        [Test]
        public void Пролёт_НетНиНаправленияНиВзгляда_ДаётТочкуПриложения()
        {
            Vector3 same = new Vector3(3f, 0f, 3f);

            Vector3 point = Knockback.PointBeyond(same, same, Vector3.zero, 2f);

            Assert.AreEqual(same, point, "без направления пролёта не бывает — встаём в точку приложения");
        }

        // ============================== ЗОНЫ ШЛЕЙФА ==============================

        [Test]
        public void Шлейф_ЧислоЗон_ПоШагуПлюсЗонаВТочкеСтарта()
        {
            Vector3 start = Vector3.zero;
            Vector3 landing = new Vector3(2f, 0f, 0f);

            Assert.AreEqual(3, MatchManager.TrailZoneCount(start, landing, 1f),
                            "путь 2 м с шагом 1 м — зоны в 0, 1 и 2 метрах");
        }

        [Test]
        public void Шлейф_ЧислоЗон_ОстатокПутиНовойЗоныНеДаёт()
        {
            Assert.AreEqual(3, MatchManager.TrailZoneCount(Vector3.zero, new Vector3(2.9f, 0f, 0f), 1f),
                            "2,9 м с шагом 1 м — три зоны; на остаток 0,9 м четвёртая не ставится");
        }

        [Test]
        public void Шлейф_ЧислоЗон_КастерПриземлилсяТамЖе_ОднаЗона()
        {
            Assert.AreEqual(1, MatchManager.TrailZoneCount(Vector3.zero, Vector3.zero, 1f),
                            "нулевой путь всё равно даёт зону в точке старта");
        }

        [Test]
        public void Шлейф_ЧислоЗон_ШагНоль_ЗонНет()
        {
            Assert.AreEqual(0, MatchManager.TrailZoneCount(Vector3.zero, new Vector3(5f, 0f, 0f), 0f),
                            "шаг ≤ 0 — считать нечем, шлейфа нет");
        }

        [Test]
        public void Шлейф_ПерепадВысотПутьНеУдлиняет()
        {
            // Шаг задан в метрах ПО ЗЕМЛЕ: подъём на 10 м не должен добавлять зон.
            Assert.AreEqual(3, MatchManager.TrailZoneCount(Vector3.zero, new Vector3(2f, 10f, 0f), 1f));
        }

        [Test]
        public void Шлейф_ПервaяЗона_ВТочкеСтарта_ПоследняяНеДальшеПриземления()
        {
            Vector3 start = new Vector3(1f, 0f, 1f);
            Vector3 landing = new Vector3(1f, 0f, 3.5f);
            const float spacing = 1f;

            int count = MatchManager.TrailZoneCount(start, landing, spacing);
            Assert.AreEqual(3, count);

            Assert.AreEqual(start, MatchManager.TrailZonePosition(start, landing, spacing, 0));

            Vector3 last = MatchManager.TrailZonePosition(start, landing, spacing, count - 1);
            Assert.AreEqual(3f, last.z, Tolerance, "третья зона — в двух шагах от старта, внутри пути");

            // Индекс за пределами пути зажимается точкой приземления, а не уезжает за неё.
            Vector3 beyond = MatchManager.TrailZonePosition(start, landing, spacing, 99);
            Assert.AreEqual(landing, beyond);
        }

        [Test]
        public void Шлейф_ВысотаЗоны_ИнтерполируетсяМеждуСтартомИПриземлением()
        {
            Vector3 start = new Vector3(0f, 0f, 0f);
            Vector3 landing = new Vector3(4f, 2f, 0f);

            Vector3 middle = MatchManager.TrailZonePosition(start, landing, 1f, 2);

            Assert.AreEqual(2f, middle.x, Tolerance);
            Assert.AreEqual(1f, middle.y, Tolerance, "высота берётся линейно между концами пути");
        }

        // ============================== ИНВАРИАНТЫ НУМЕРАЦИИ ==============================

        [Test]
        public void КодыСобытий_ПятьНовыхДописаныВКонец_ПрежниеНеСдвинулись()
        {
            // Прежний последний код остался на своём числе — иначе клиент и сервер разошлись бы наборами.
            Assert.AreEqual(14, (int)AbilityEventCode.PassiveAllyDeath, "прежний последний код не сдвинулся");

            Assert.AreEqual(15, (int)AbilityEventCode.SkillCasterMoveStart);
            Assert.AreEqual(16, (int)AbilityEventCode.SkillCasterMoveArrive);
            Assert.AreEqual(17, (int)AbilityEventCode.SkillKnockbackTarget);
            Assert.AreEqual(18, (int)AbilityEventCode.SkillMorphOn);
            Assert.AreEqual(19, (int)AbilityEventCode.SkillMorphOff);
        }

        [Test]
        public void КатегорияВПолёте_ДописанаВКонец_ПрежниеНеСдвинулись()
        {
            // Категория сериализована в ассетах состояний ЧИСЛОМ: перестановка молча переназначила бы
            // категорию всем существующим ассетам.
            Assert.AreEqual(0, (int)EffectorCategory.None);
            Assert.AreEqual(1, (int)EffectorCategory.Stun);
            Assert.AreEqual(8, (int)EffectorCategory.DamageReduction, "прежняя последняя не сдвинулась");
            Assert.AreEqual(9, (int)EffectorCategory.InFlight);
        }

        [Test]
        public void КатегорияВПолёте_СопротивлениемВремяНеРежет()
        {
            // Через сопротивления она вообще не проходит (исключение в приёмнике), но вид категории
            // всё равно обязан быть определён — иначе переключение ветки «режет время / режет силу»
            // зависело бы от умолчания.
            Assert.IsFalse(UnitResistances.CutsTime(EffectorCategory.InFlight));
        }

        // ============================== РАЗБОР КОДОВ У АССЕТА ==============================

        [Test]
        public void РазборКодов_ПятьНовыхНаборовОтдаютсяСвоимиПолямиБлоков()
        {
            CompositeSkill skill = MakeAsset<CompositeSkill>();

            Assert.AreSame(skill.casterMove.startPresentation,
                           skill.PresentationFor((int)AbilityEventCode.SkillCasterMoveStart));
            Assert.AreSame(skill.casterMove.arrivePresentation,
                           skill.PresentationFor((int)AbilityEventCode.SkillCasterMoveArrive));
            Assert.AreSame(skill.knockback.presentation,
                           skill.PresentationFor((int)AbilityEventCode.SkillKnockbackTarget));
            Assert.AreSame(skill.morph.onPresentation,
                           skill.PresentationFor((int)AbilityEventCode.SkillMorphOn));
            Assert.AreSame(skill.morph.offPresentation,
                           skill.PresentationFor((int)AbilityEventCode.SkillMorphOff));
        }

        [Test]
        public void РазборКодов_НаборыСтартаИПрибытияНеПутаются()
        {
            CompositeSkill skill = MakeAsset<CompositeSkill>();

            Assert.AreNotSame(skill.PresentationFor((int)AbilityEventCode.SkillCasterMoveStart),
                              skill.PresentationFor((int)AbilityEventCode.SkillCasterMoveArrive));
            Assert.AreNotSame(skill.PresentationFor((int)AbilityEventCode.SkillMorphOn),
                              skill.PresentationFor((int)AbilityEventCode.SkillMorphOff));
        }

        // ============================== ИСКЛЮЧЕНИЕ «В ПОЛЁТЕ» ==============================

        /// <summary>
        /// Ассет «в полёте» для теста. Безоружие — ОДИН запрет из трёх, которые спрашивает иммунитет:
        /// его вход <c>DisarmEnter</c> на голом юните безопасен (firstAttack = true, AttackStop не зовётся),
        /// а немота позвала бы <c>EndActiveAbility</c> с обращением к <c>NetworkManager.Singleton</c>,
        /// которого в режиме редактора нет. В боевом ассете включены оба запрета.
        /// </summary>
        Effector MakeInFlight(int id = 801)
        {
            Effector e = MakeAsset<Effector>();
            e.id = id;
            e.duration = 0.4f;
            e.disarms = true;
            e.category = EffectorCategory.InFlight;
            return e;
        }

        EffectorHolder Receive(Unit unit, Effector status)
            => unit.ReceiverEnsure().Receive(new EffectorPacket(status, unit, unit.owner, 0f, 1f, -1f));

        [Test]
        public void ВПолёте_ИммунитетККонтролюНеОтбивает()
        {
            EnsureSlotManager();

            Unit unit = MakeBareUnit();
            unit.gameObject.AddComponent<ControlImmunity>().Add();

            EffectorHolder holder = Receive(unit, MakeInFlight());

            Assert.IsNotNull(holder, "«в полёте» — единственное состояние, которое иммунитет не отбивает");
            Assert.AreEqual(1, unit.effectors.Count);
            Assert.IsTrue(unit.inFlight, "флаг полёта выводится из списка состояний");
        }

        [Test]
        public void ТоЖеБезоружие_НоОбычнойКатегории_ИммунитетомОтбивается()
        {
            // Контраст к предыдущему: исключение узкое и держится ТОЛЬКО на категории.
            EnsureSlotManager();

            Unit unit = MakeBareUnit();
            unit.gameObject.AddComponent<ControlImmunity>().Add();

            Effector ordinary = MakeInFlight(id: 802);
            ordinary.category = EffectorCategory.Disarm;

            Assert.IsNull(Receive(unit, ordinary), "обычное безоружие иммунитет отбивает, как и раньше");
            Assert.AreEqual(0, unit.effectors.Count);
            Assert.IsFalse(unit.inFlight);
        }

        [Test]
        public void ВПолёте_СопротивлениеОт100ПроцентовНеОтбивает()
        {
            EnsureSlotManager();

            Unit unit = MakeBareUnit();
            unit.gameObject.AddComponent<UnitResistances>().Add(this, EffectorCategory.InFlight, 1f);

            EffectorHolder holder = Receive(unit, MakeInFlight(id: 803));

            Assert.IsNotNull(holder, "сопротивление категории «в полёте» наложение не отбивает");
            Assert.IsTrue(unit.inFlight);
        }

        [Test]
        public void ВПолёте_ДлительностьСопротивлениемНеРежется()
        {
            EnsureSlotManager();

            Unit unit = MakeBareUnit();
            unit.gameObject.AddComponent<UnitResistances>().Add(this, EffectorCategory.InFlight, 0.5f);

            Effector inFlight = MakeInFlight(id: 804);
            EffectorHolder holder = Receive(unit, inFlight);

            Assert.IsNotNull(holder);
            Assert.AreEqual(inFlight.duration, holder.duration, Tolerance,
                            "время полёта задано умением и совпадает со временем показа — резать его нечем");
        }

        [Test]
        public void ВПолёте_СнятиеСостояния_СбрасываетФлаг()
        {
            EnsureSlotManager();

            Unit unit = MakeBareUnit();
            EffectorHolder holder = Receive(unit, MakeInFlight(id: 805));

            Assert.IsTrue(unit.inFlight);

            Effector.EffectorRemove(unit, holder);

            Assert.IsFalse(unit.inFlight, "состояние ушло из списка — флаг обязан пересобраться в false");
            Assert.AreEqual(0, unit.effectors.Count);
        }
    }
}
