using NUnit.Framework;
using UnityEngine;

namespace StrategyCore.Tests
{
    /// <summary>
    /// Исправления базы 2026-09-12/13 (шаги 6 и 6б слияния с проектом Саши).
    ///
    /// В EditMode проверяемы два из пяти: арифметика последнего тика длящегося бафа (2.4) и снятие
    /// VFX по префабу (2.2, довнесено шагом 6б — было отложено шагом 6, Unit.Visuals.cs держал лок
    /// соседней сессии). Остальные три сидят на сети и тумане войны, которых в EditMode нет:
    ///   2.1 закрытие технологии по сети — нужен RPC и живой TechnologyManager;
    ///   2.3 перерезолв автокаста — нужен пул умений юнита и событие технологии;
    ///   2.5 impactVFX снаряда — нужен FogOfWar.Instance.
    /// Их проверка — на полигоне, список в логе сессии.
    ///
    /// Тик бафа (SkillBuff.OnTick) приватен и без GameManager.Instance не считает вовсе, поэтому
    /// проверяется вынесенная из него функция шага — SkillBuff.TickDelta. Это единственная выноска,
    /// разрешённая промтом; лечение считается той же формулой, что в OnTick (heal * dt).
    /// </summary>
    public class BaseFixes2026_09_12Tests : UnitTestHarness
    {
        // ============================================== 2.4 остаток последнего тика ==

        [Test]
        public void ПоследнийТик_ОстатокМеньшеШага_ЛечитРовноНаОстаток()
        {
            const float healPerSecond = 10f;
            const float delta = 0.1f;      // штатный тик GameManager
            const float remaining = 0.03f; // бафу осталось меньше тика

            float dt = SkillBuff.TickDelta(delta, remaining);

            Assert.AreEqual(remaining, dt, 1e-6f, "Шаг последнего тика должен быть равен остатку бафа.");

            // Лечение считается той же формулой, что в OnTick: heal * dt. Сверяемся с ДОоплаченной величиной
            // (0,3 единицы за 0,03 с при 10 в секунду), а не с dt — иначе проверка была бы тавтологией.
            Assert.AreEqual(0.3f, healPerSecond * dt, 1e-6f,
                            "Лечение последнего тика — ровно за остаток бафа.");
            Assert.AreEqual(1.0f, healPerSecond * delta, 1e-6f,
                            "Полный шаг вылечил бы втрое больше — это и был дефект.");

            // Таймер уменьшается на ТОТ ЖЕ dt: баф обязан сняться на этом же тике, как и до правки.
            Assert.LessOrEqual(remaining - dt, 0f, "После последнего тика остаток обязан уйти в ноль.");
        }

        [Test]
        public void ОбычныйТик_ОстаткаХватает_ШагНеУрезается()
        {
            const float delta = 0.1f;
            const float remaining = 2.5f;

            Assert.AreEqual(delta, SkillBuff.TickDelta(delta, remaining), 1e-6f,
                            "Пока остатка хватает, тик идёт полным шагом — прежнее поведение.");
        }

        // ============================================== 2.2 снятие VFX по префабу ==

        // Unit.RemoveVFX зовёт Object.Destroy — в режиме редактора Destroy ничего не уничтожает
        // («Destroy may not be called from edit mode!»), поэтому проверка «экземпляр уничтожен»
        // в EditMode ложна независимо от того, правильно ли выбран элемент. Проверяем поэтому
        // только выбор — чистую функцию FindVfxInstance, она не зависит от режима выполнения.

        GameObject vfxPrefabA;
        GameObject vfxPrefabB;
        GameObject vfxPrefabC;
        GameObject vfxInstanceA;
        GameObject vfxInstanceB;

        [TearDown]
        public void DestroyVfxObjects()
        {
            if (vfxPrefabA != null) Object.DestroyImmediate(vfxPrefabA);
            if (vfxPrefabB != null) Object.DestroyImmediate(vfxPrefabB);
            if (vfxPrefabC != null) Object.DestroyImmediate(vfxPrefabC);
            if (vfxInstanceA != null) Object.DestroyImmediate(vfxInstanceA);
            if (vfxInstanceB != null) Object.DestroyImmediate(vfxInstanceB);
            vfxPrefabA = vfxPrefabB = vfxPrefabC = vfxInstanceA = vfxInstanceB = null;
        }

        [Test]
        public void FindVfxInstance_ДваЭлементаСОдинаковымId_НаходитПоПрефабуИсточнику()
        {
            vfxPrefabA = new GameObject("VFXPrefabA") { hideFlags = HideFlags.HideAndDontSave };
            VFXReferencer prefabA = vfxPrefabA.AddComponent<VFXReferencer>(); // id по умолчанию — 0

            vfxPrefabB = new GameObject("VFXPrefabB") { hideFlags = HideFlags.HideAndDontSave };
            VFXReferencer prefabB = vfxPrefabB.AddComponent<VFXReferencer>(); // id тоже 0 — ровно дефект промта

            vfxPrefabC = new GameObject("VFXPrefabC") { hideFlags = HideFlags.HideAndDontSave };
            VFXReferencer prefabC = vfxPrefabC.AddComponent<VFXReferencer>(); // на держателе не встречается

            vfxInstanceA = new GameObject("VFXInstanceA") { hideFlags = HideFlags.HideAndDontSave };
            VFXReferencer instanceA = vfxInstanceA.AddComponent<VFXReferencer>();
            instanceA.source = prefabA; // как AddVFX проставляет source после Instantiate

            vfxInstanceB = new GameObject("VFXInstanceB") { hideFlags = HideFlags.HideAndDontSave };
            VFXReferencer instanceB = vfxInstanceB.AddComponent<VFXReferencer>();
            instanceB.source = prefabB;

            Assert.AreEqual(0, instanceA.id);
            Assert.AreEqual(0, instanceB.id);

            VFXReferencer[] elements = { instanceA, instanceB };

            // Старое сравнение по id (elements[i].id == vfx.id) не отличило бы префабы — оба id 0 —
            // и всегда находило бы elements[0] независимо от аргумента.
            Assert.AreSame(instanceA, Unit.FindVfxInstance(elements, prefabA),
                            "По префабу A должен найтись именно его экземпляр, не второй.");
            Assert.AreSame(instanceB, Unit.FindVfxInstance(elements, prefabB),
                            "По префабу B должен найтись именно его экземпляр, не первый.");
            Assert.IsNull(Unit.FindVfxInstance(elements, prefabC),
                          "Префаб, которого нет среди элементов держателя — null.");
        }
    }
}
