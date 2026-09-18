using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StrategyCore.Tests
{
    /// <summary>
    /// Шаг 4 слияния, семья «презентация события набором полей» (проект «Презентация_Поля_Проект» §4.6).
    ///
    /// Что здесь можно проверить в режиме редактора, а что нет:
    ///   • публикацию факта — можно: хаб SkillPresentationEvents чисто событийный, и клиентский гейт
    ///     стоит на статическом флаге NetworkConnectionHandler.isClient;
    ///   • ОТПРАВКУ СООБЩЕНИЯ — нельзя: NetworkDataSync.Instance в режиме редактора всегда null, сети нет.
    ///     Поэтому «сообщения нет» проверяется структурно — тем, что локальный путь вообще не ходит
    ///     через EmitSkillFired (единственное место, откуда уходит SkillFiredSend);
    ///   • живой CompositeSkill.ApplyShield — нельзя (нужны цели, приёмник и менеджеры сцены), поэтому
    ///     правило «при визуале всегда берём перегрузку с onEnded» проверяется на ЧИСТОЙ функции
    ///     CompositeSkill.ShieldNeedsOnEnded — той самой, по которой ApplyShield и принимает решение.
    ///
    /// Теста «зеркало умения совпадает с плоскими полями» из §4.6 здесь НЕТ: зеркала не существует —
    /// решением Artsiom 26 плоские поля перенесены в набор, форма стала одна (промт §3 шаг 10).
    /// </summary>
    public class EventPresentationTests : UnitTestHarness
    {
        readonly List<Action<Unit, int, int, Unit, Vector3, int>> subscriptions =
            new List<Action<Unit, int, int, Unit, Vector3, int>>();

        bool isClientBefore;

        [SetUp]
        public void RememberPeerFlag()
        {
            // Флаг статический и переживает тест: сохраняем и возвращаем, иначе соседние тесты
            // побежали бы «на клиенте» (тот же приём, что у культуры в InvariantCultureSetUp).
            isClientBefore = NetworkConnectionHandler.isClient;
        }

        [TearDown]
        public void RestorePeerFlagAndUnsubscribe()
        {
            NetworkConnectionHandler.isClient = isClientBefore;

            // События хаба статические: не отписаться значит оставить обработчик мёртвого теста.
            for (int i = 0; i < subscriptions.Count; i++) SkillPresentationEvents.SkillFired -= subscriptions[i];
            subscriptions.Clear();
        }

        /// <summary>Записывает все факты «умение сработало», поднятые в этом тесте.</summary>
        sealed class FactLog
        {
            public readonly List<(int abilityID, int level, int eventCode, Unit caster, Unit aim, Vector3 point)> facts =
                new List<(int, int, int, Unit, Unit, Vector3)>();

            public int Count => facts.Count;
        }

        FactLog Listen()
        {
            FactLog log = new FactLog();
            Action<Unit, int, int, Unit, Vector3, int> handler =
                (caster, abilityID, level, aim, point, code) => log.facts.Add((abilityID, level, code, caster, aim, point));

            SkillPresentationEvents.SkillFired += handler;
            subscriptions.Add(handler);
            return log;
        }

        static EventPresentation Filled()
        {
            // Заполняем стейтом анимации, а не ссылкой на VFX: префаб визуала — объект сцены,
            // а «заданность» набора решается одинаково по любому из полей (EventPresentation.Any).
            return new EventPresentation { animationState = "proc" };
        }

        CompositeSkill MakeSkill(int id = 101)
        {
            CompositeSkill skill = MakeAsset<CompositeSkill>();
            skill.name = "TestSkill";
            skill.id = id;
            return skill;
        }

        CompositePassive MakePassive(int id = 202)
        {
            CompositePassive passive = MakeAsset<CompositePassive>();
            passive.name = "TestPassive";
            passive.id = id;
            return passive;
        }

        // ================================================ ПУБЛИКАЦИЯ ФАКТА ==

        [Test]
        public void Набор_Пустой_ФактНеПубликуется()
        {
            NetworkConnectionHandler.isClient = false;

            CompositeSkill skill = MakeSkill();
            FactLog log = Listen();

            InterflowAbility.EmitEventPresentation(skill, (int)AbilityEventCode.SkillHeal,
                                                   new EventPresentation(), null, 0, null, Vector3.zero);

            Assert.AreEqual(0, log.Count,
                "пустой набор сообщения не порождает: иначе каждый прок без визуала стоил бы сообщения всем клиентам");

            // null вместо набора — тот же результат: блок без набора не существует для презентации.
            InterflowAbility.EmitEventPresentation(skill, (int)AbilityEventCode.SkillHeal,
                                                   null, null, 0, null, Vector3.zero);
            Assert.AreEqual(0, log.Count, "набора нет — факта нет");
        }

        [Test]
        public void Набор_Заполнен_ФактСКодом()
        {
            NetworkConnectionHandler.isClient = false;

            CompositeSkill skill = MakeSkill(id: 555);
            Unit carrier = MakeBareUnit("Carrier");
            Unit aim = MakeBareUnit("Aim");
            Vector3 point = new Vector3(3f, 0f, 7f);
            FactLog log = Listen();

            InterflowAbility.EmitEventPresentation(skill, (int)AbilityEventCode.SkillHeal, Filled(),
                                                   carrier, 2, aim, point);

            Assert.AreEqual(1, log.Count, "заполненный набор публикует ровно один факт");

            var f = log.facts[0];
            Assert.AreEqual(555, f.abilityID, "адрес набора — id ассета-хозяина");
            Assert.AreEqual((int)AbilityEventCode.SkillHeal, f.eventCode, "второе число адреса — код набора");
            Assert.AreEqual(2, f.level);
            Assert.AreSame(carrier, f.caster);
            Assert.AreSame(aim, f.aim);
            Assert.AreEqual(point, f.point);
        }

        [Test]
        public void Код_Ноль_ЭтоСамКаст()
        {
            NetworkConnectionHandler.isClient = false;

            CompositeSkill skill = MakeSkill(id: 777);
            FactLog log = Listen();

            // Старая подпись EmitSkillFired (без кода) — единственный сегодняшний вызывающий, каст умения.
            InterflowAbility.EmitSkillFired(null, skill, 0, null, Vector3.zero);

            Assert.AreEqual(1, log.Count);
            Assert.AreEqual(0, log.facts[0].eventCode, "каст умения едет кодом 0 — поведение до наборов не изменилось");
            Assert.AreEqual(0, (int)AbilityEventCode.SkillCast, "код каста обязан быть нулём: на нём держится умолчание подписи");
        }

        [Test]
        public void КодыСобытий_НумерацияСплошнаяОтНуля()
        {
            // Коды едут по сети ЧИСЛОМ: дыра или перестановка развели бы сервер и клиента на разные наборы.
            Array values = Enum.GetValues(typeof(AbilityEventCode));

            Assert.Greater(values.Length, 0);
            for (int i = 0; i < values.Length; i++)
                Assert.AreEqual(i, (int)values.GetValue(i),
                    "нумерация AbilityEventCode обязана быть сплошной от нуля; новые коды — только в конец");
        }

        // ============================================== РЕЗОЛВ НАБОРА ==

        [Test]
        public void Резолв_Пассивки_ОтдаётНаборПоКоду()
        {
            CompositePassive passive = MakePassive();

            // Резолв презентера идёт до InterflowAbility, а не до CompositeSkill: приведение пассивки
            // к CompositeSkill дало бы null, и факт от пассивки молча потерялся бы.
            InterflowAbility ability = passive;

            Assert.AreSame(passive.onHit.presentation, ability.PresentationFor((int)AbilityEventCode.PassiveOnHit));
            Assert.AreSame(passive.onDeath.presentation, ability.PresentationFor((int)AbilityEventCode.PassiveDeath));
            Assert.AreSame(passive.onDeath.healPresentation, ability.PresentationFor((int)AbilityEventCode.PassiveDeathHeal),
                "у реакции «погиб» ДВА набора — из-за этого по сети едет код набора, а не номер блока");
            Assert.AreSame(passive.onAttackStart.presentation, ability.PresentationFor((int)AbilityEventCode.PassiveAttackStart));
            Assert.AreSame(passive.onAllyDeath.presentation, ability.PresentationFor((int)AbilityEventCode.PassiveAllyDeath),
                "реакция 7 «погиб союзник» — свой набор, хозяин показа НОСИТЕЛЬ, а не погибший");

            Assert.IsNull(ability.PresentationFor((int)AbilityEventCode.SkillHeal),
                "чужой код — набора нет, показывать нечего");

            CompositeSkill skill = MakeSkill();
            Assert.AreSame(skill.presentation, skill.PresentationFor((int)AbilityEventCode.SkillCast));
            Assert.AreSame(skill.heal.presentation, skill.PresentationFor((int)AbilityEventCode.SkillHeal));
            Assert.AreSame(skill.projectileImpact.presentation,
                           skill.PresentationFor((int)AbilityEventCode.SkillProjectileImpact));
            Assert.IsNull(skill.PresentationFor((int)AbilityEventCode.PassiveOnHit),
                "чужой код (реакция пассивки) — у умения такого набора нет");
        }

        [Test]
        public void Резолв_ВторичныеЦели_ОтдаётНаборБлока()
        {
            CompositeSkill skill = MakeSkill();

            // У блока 14 ДВА набора (решение Artsiom 33 от 17.09.2026): «цель» играет на каждой задетой
            // вторичной цели, «каст» — один раз на кастере. Коды у них разные, и каждый обязан
            // резолвиться в СВОЙ набор: перепутанные коды молча подменили бы визуал.
            Assert.AreSame(skill.secondary.presentation,
                           skill.PresentationFor((int)AbilityEventCode.SkillSecondaryHit),
                           "код «цель» резолвится в набор попадания по вторичной цели");
            Assert.AreSame(skill.secondary.castPresentation,
                           skill.PresentationFor((int)AbilityEventCode.SkillSecondaryCast),
                           "код «каст» резолвится во второй набор того же блока");

            Assert.AreNotSame(skill.PresentationFor((int)AbilityEventCode.SkillSecondaryHit),
                              skill.PresentationFor((int)AbilityEventCode.SkillSecondaryCast),
                              "наборы блока 14 — РАЗНЫЕ объекты: иначе заполнение одного молча заполнило бы второй");

            Assert.AreNotSame(skill.presentation,
                              skill.PresentationFor((int)AbilityEventCode.SkillSecondaryHit),
                              "набор блока 14 — свой, а не набор каста умения");
            Assert.AreNotSame(skill.presentation,
                              skill.PresentationFor((int)AbilityEventCode.SkillSecondaryCast),
                              "набор «каст» блока 14 — не набор каста САМОГО УМЕНИЯ, это разные события");
        }

        [Test]
        public void Резолв_Умения_КаждыйКод_ОтдаётСвойНабор()
        {
            CompositeSkill skill = MakeSkill();

            // Общая проверка таблицы резолва: сколько бы кодов ни завели, два разных кода никогда
            // не должны приводить к ОДНОМУ набору — по сети едет только код, и подмена была бы молчаливой.
            var seen = new List<EventPresentation>();
            var codes = new List<AbilityEventCode>();

            foreach (AbilityEventCode code in Enum.GetValues(typeof(AbilityEventCode)))
            {
                EventPresentation set = skill.PresentationFor((int)code);
                if (set == null) continue;   // чужой код (реакции пассивки) — у умения набора нет

                int already = seen.FindIndex(x => ReferenceEquals(x, set));
                Assert.AreEqual(-1, already,
                    $"коды {code} и {(already >= 0 ? codes[already].ToString() : "?")} резолвятся в ОДИН набор");

                seen.Add(set);
                codes.Add(code);
            }

            Assert.AreEqual(5, seen.Count,
                "у умения пять наборов: каст, лечение (блок 7), прилёт снаряда (блок 22) и ДВА у вторичных целей (блок 14)");
        }

        // ================================== СЕРВЕРНЫЙ И ЛОКАЛЬНЫЙ ПОКАЗ ==

        [Test]
        public void Локальный_Показ_НаКлиенте_ФактЕсть_СообщенияНет()
        {
            NetworkConnectionHandler.isClient = true;

            CompositePassive passive = MakePassive(id: 42);
            Unit carrier = MakeBareUnit("Carrier");
            FactLog log = Listen();

            InterflowAbility.RaiseEventPresentationLocal(passive, (int)AbilityEventCode.PassiveAttackStart,
                                                         Filled(), carrier, 0, null, Vector3.zero);

            Assert.AreEqual(1, log.Count,
                "локальный показ работает и на клиенте: событие «начал атаку» поднимается общим кодом на каждом пире");
            Assert.AreEqual((int)AbilityEventCode.PassiveAttackStart, log.facts[0].eventCode);
            Assert.AreEqual(42, log.facts[0].abilityID);

            // Сообщения нет структурно: локальный путь не ходит через EmitSkillFired — единственное
            // место, откуда уходит SkillFiredSend. Сети в режиме редактора нет, проверить иначе нечем.
            InterflowAbility.RaiseEventPresentationLocal(passive, (int)AbilityEventCode.PassiveAttackStart,
                                                         new EventPresentation(), carrier, 0, null, Vector3.zero);
            Assert.AreEqual(1, log.Count, "пустой набор молчит и на локальном пути");
        }

        [Test]
        public void Серверный_Показ_НаКлиенте_МолчитЦеликом()
        {
            NetworkConnectionHandler.isClient = true;

            CompositeSkill skill = MakeSkill();
            FactLog log = Listen();

            InterflowAbility.EmitEventPresentation(skill, (int)AbilityEventCode.SkillHeal, Filled(),
                                                   null, 0, null, Vector3.zero);
            InterflowAbility.EmitSkillFired(null, skill, 0, null, Vector3.zero);

            Assert.AreEqual(0, log.Count,
                "серверный набор на клиенте не публикуется вовсе: факты боя считает только сервер (правило 6)");
        }

        // ========================================================= ЩИТ ==

        [Test]
        public void Щит_ПриВизуале_ВсегдаБерётПерегрузкуСOnEnded()
        {
            SkillShieldBlock shield = new SkillShieldBlock { enabled = true };

            Assert.IsFalse(CompositeSkill.ShieldNeedsOnEnded(shield, 5f),
                "голый щит снимать нечего — короткий путь без onEnded");

            shield.visualEffector = MakeAsset<Effector>();
            Assert.IsTrue(CompositeSkill.ShieldNeedsOnEnded(shield, 5f),
                "задан визуал — перегрузка с onEnded обязательна, иначе состояние-визуал переживёт щит");

            // И у БЕССРОЧНОГО щита тоже: правило снижения урона при нулевой длительности не ставится,
            // а визуал ставится всегда — значит и снимать его надо всегда.
            Assert.IsTrue(CompositeSkill.ShieldNeedsOnEnded(shield, 0f),
                "бессрочный щит с визуалом всё равно требует onEnded");

            shield.visualEffector = null;
            Assert.IsFalse(CompositeSkill.ShieldNeedsOnEnded(shield, 0f));
        }

        [Test]
        public void Щит_ВизуалСнимаетсяВOnEnded()
        {
            NetworkConnectionHandler.isClient = false;

            Unit target = MakeBareUnit("ShieldCarrier");
            target.maxHealth = 100f;
            target.health = 100f;

            // Держатель визуала живёт в замыкании рядом с подпиской ответа: ApplyShield ставит наложение
            // ПОСЛЕ AbsorbShield.Apply и снимает его в onEnded. Здесь проверяется ровно тот механизм,
            // на котором это держится: перекаст поверх живого щита дёргает ПРЕЖНИЙ onEnded (и только его),
            // то есть каждое наложение снимает своё, а поставленное после Apply переживает подмену.
            object holderOfFirst = new object();
            object releasedBy = null;
            int endedCalls = 0;

            Action<Unit> firstEnded = carrier => { endedCalls++; releasedBy = holderOfFirst; };
            Action<Unit> secondEnded = carrier => { endedCalls++; };

            AbsorbShield.Apply(target, 50f, 5f, null, firstEnded);
            Assert.AreEqual(0, endedCalls, "щит только что выдан — снимать нечего");

            AbsorbShield.Apply(target, 60f, 5f, null, secondEnded);

            Assert.AreEqual(1, endedCalls, "перекаст дёргает onEnded ПРЕЖНЕГО щита ровно один раз");
            Assert.AreSame(holderOfFirst, releasedBy,
                "снимается держатель ПЕРВОГО наложения — значит визуал второго, поставленный после Apply, остаётся жив");
        }
    }
}
