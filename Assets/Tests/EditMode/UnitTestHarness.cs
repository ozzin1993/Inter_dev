using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StrategyCore.Tests
{
    /// <summary>
    /// Оснастка тестов Unit.Change* (шаг 1 промта «Статы»).
    ///
    /// Зачем: все Change*-методы — методы экземпляра MonoBehaviour, а Unit.Initialize() в EditMode
    /// неподъёмен (тянет сеть, туман войны и менеджеры сцены). Поэтому юнит создаётся ГОЛЫМ:
    /// new GameObject + AddComponent&lt;Unit&gt;(). У Unit нет ни Awake, ни OnEnable, ни
    /// [ExecuteInEditMode] — AddComponent ничего не запускает, а поле passiveEffects ставится
    /// инициализатором поля (Unit.cs:243), то есть приходит новый объект, где все множители равны 1.
    ///
    /// Значения полей голого юнита — ровно те, что стоят инициализаторами в Unit.cs:
    ///   unitRadius = 0.5, visionRange = 5, moveSpeed = 5, attackDamage = 10, attackSpeed = 1,
    ///   attackRange = 1, melee = true; maxHealth, health, maxMana, mana, armor, xpReward,
    ///   healthRegen, manaRegen — нули (инициализаторов нет).
    /// Всё это публичные поля, поэтому нужные значения тест задаёт сам, а не через оснастку.
    ///
    /// Почему базовый класс, а не статический хелпер: [TearDown] базового класса NUnit выполняет
    /// после каждого теста любого наследника — одна точка уборки на три файла тестов вместо трёх
    /// одинаковых копий (правило 5, меньше точек входа).
    /// </summary>
    public abstract class UnitTestHarness
    {
        /// <summary>
        /// Допуск сравнения float. Решение Artsiom 2026-08-18 (§10 п.1 промта).
        /// Замер: дрейф модели не превышает 1 ULP (~3e-5 на значениях до 500) и не накапливается.
        /// </summary>
        protected const float Tolerance = 1e-4f;

        readonly List<GameObject> spawned = new List<GameObject>();
        readonly List<Object> assets = new List<Object>();

        [TearDown]
        public void DestroySpawned()
        {
            for (int i = 0; i < spawned.Count; i++)
                if (spawned[i] != null) Object.DestroyImmediate(spawned[i]);

            for (int i = 0; i < assets.Count; i++)
                if (assets[i] != null) Object.DestroyImmediate(assets[i]);

            spawned.Clear();
            assets.Clear();
        }

        /// <summary>
        /// Голый юнит: без Initialize(), без NavMeshAgent, аниматора и сетевого контекста.
        /// </summary>
        protected Unit MakeBareUnit(string name = "TestUnit")
        {
            GameObject go = new GameObject(name);
            go.hideFlags = HideFlags.HideAndDontSave;   // тесты не должны пачкать открытую сцену

            Unit unit = go.AddComponent<Unit>();

            spawned.Add(go);
            return unit;
        }

        /// <summary>
        /// Вешает на юнита компонент AttributeUnit и связывает публичное поле unit.attributeUnit.
        /// Нужен ТОЛЬКО дефекту Д6: ветка процентных атрибутов в AbilityPassiveEffects закрыта гардом
        /// «if (attributeON &amp;&amp; unit.attributeUnit)» (PassiveAppliedEffects.cs:158, :234) — без
        /// компонента блок пропускается целиком и дефект недостижим. Разрешено Artsiom 2026-08-18.
        /// Initialize() у компонента сознательно НЕ зовётся: ему нужны Attribute-ассеты и живой юнит.
        /// </summary>
        protected AttributeUnit AttachAttributes(Unit unit)
        {
            AttributeUnit attributes = unit.gameObject.AddComponent<AttributeUnit>();
            unit.attributeUnit = attributes;
            return attributes;
        }

        /// <summary>
        /// Одноразовый ScriptableObject-ассет в памяти (DamageType, ArmorType и т.п.).
        /// Уничтожается тем же [TearDown], что и юниты, — уборка остаётся в одной точке.
        /// </summary>
        protected T MakeAsset<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            asset.hideFlags = HideFlags.HideAndDontSave;

            assets.Add(asset);
            return asset;
        }
    }
}
