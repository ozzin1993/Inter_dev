using NUnit.Framework;
using UnityEngine;

namespace StrategyCore.Tests
{
    /// <summary>
    /// Выборка нескольких ближайших целей — <c>Utils.GetClosestUnitsInRadius</c> (дефект T04, 09.09.2026).
    ///
    /// Зачем тест: признак «несколько целей» (<c>Unit.multiTarget</c>) не включён ни в одном префабе и ни
    /// в одной сцене, а единственный включатель во время исполнения <c>Unit.ChangeMultitarget</c> зовётся
    /// только из тестов — то есть матч этот код не исполняет вовсе и починку не покажет. Проверить её
    /// можно ровно здесь.
    ///
    /// Что проверяется: (а) при одной ячейке возвращается БЛИЖАЙШИЙ, а не последний встреченный;
    /// (б) при трёх ячейках заполняются все три; (в) порядок — по возрастанию расстояния;
    /// (г) более близкий кандидат вытесняет самого дальнего, а более дальний — никого.
    ///
    /// Оснастка: сетка чанков поднимается своим объектом (в EditMode Awake не зовётся, поэтому
    /// <c>Grid.Startup()</c> дёргается вручную — метод публичный, служба идемпотентна). Юниты — голые
    /// (<see cref="UnitTestHarness.MakeBareUnit"/>) и раскладываются по чанкам штатным
    /// <c>Grid.AssignToChunkInitial</c>.
    /// </summary>
    public class ClosestUnitsTests : UnitTestHarness
    {
        const int MapSize = 100;          // карта 100×100 — влезает в предел канала позиций (655 по оси)
        const float ChunkSize = 10f;      // 10×10 чанков: юниты ниже разложены по разным чанкам намеренно
        const float SearchRadius = 60f;   // накрывает всех подопытных: самый дальний стоит в 50

        static readonly Vector2 LeftPoint = new Vector2(5f, 5f);    // ближние юниты встречаются ПЕРВЫМИ
        static readonly Vector2 RightPoint = new Vector2(55f, 5f);  // ближние юниты встречаются ПОСЛЕДНИМИ

        GameObject gridObject;

        /// <summary>Уборка сетки. Юниты убирает [TearDown] оснастки — он идёт после этого.</summary>
        [TearDown]
        public void DestroyGrid()
        {
            Grid.chunkUnits.Clear();
            if (gridObject != null) Object.DestroyImmediate(gridObject);
        }

        void MakeGrid()
        {
            gridObject = new GameObject("TestGrid");
            gridObject.hideFlags = HideFlags.HideAndDontSave;   // тесты не должны пачкать открытую сцену

            Grid grid = gridObject.AddComponent<Grid>();
            grid.width = MapSize;
            grid.height = MapSize;
            grid.cellSize = 1f;
            grid.chunkSize = ChunkSize;

            // В EditMode Awake у компонента не вызывается — поднимаем службу сами.
            grid.Startup();
        }

        /// <summary>Юнит на оси X, разложенный по чанку штатным способом. Z общий, чтобы расстояние
        /// считалось только по X и читалось глазами.</summary>
        Unit PlaceUnit(string name, float x)
        {
            Unit unit = MakeBareUnit(name);
            unit.transform.position = new Vector3(x, 0f, LeftPoint.y);
            Grid.AssignToChunkInitial(unit);

            return unit;
        }

        /// <summary>
        /// «Свои наземные юниты». Ветки «союзник» и «враг» выключены намеренно: они спрашивают
        /// <c>SlotManager.Instance.playerTeam</c> (<c>Core/Utils/UnitSlector.cs:46</c>), а в EditMode
        /// менеджера слотов нет. Невидимых и неуязвимых тоже не включаем — ветка невидимости (:49)
        /// обращается к тому же менеджеру и коротится только на юните, который не невидим.
        /// </summary>
        static UnitSelector OwnGroundUnits()
        {
            return new UnitSelector(true, false, false, true, false, false, false, true, false, false, false, false);
        }

        [Test]
        public void Выборка_ОднаЯчейка_ВозвращаетБлижайшегоАНеПоследнегоВстреченного()
        {
            MakeGrid();
            Unit near = PlaceUnit("ближний_10", 15f);
            PlaceUnit("средний_20", 25f);
            PlaceUnit("дальний_30", 35f);

            Unit[] result = Utils.GetClosestUnitsInRadius(LeftPoint, SearchRadius, 0, OwnGroundUnits(), 1);

            Assert.AreEqual(1, result.Length, "длина массива равна запрошенному числу целей");
            Assert.AreSame(near, result[0], "в единственную ячейку обязан попасть ближайший, а не последний встреченный");
        }

        [Test]
        public void Выборка_ТриЯчейки_ЗаполняютсяВсеИПоВозрастаниюРасстояния()
        {
            MakeGrid();
            Unit near = PlaceUnit("ближний_10", 15f);
            Unit middle = PlaceUnit("средний_20", 25f);
            Unit far = PlaceUnit("дальний_30", 35f);

            Unit[] result = Utils.GetClosestUnitsInRadius(LeftPoint, SearchRadius, 0, OwnGroundUnits(), 3);

            Assert.AreEqual(3, result.Length);
            Assert.AreSame(near, result[0], "первая ячейка — ближайший");
            Assert.AreSame(middle, result[1], "вторая ячейка — следующий по расстоянию");
            Assert.AreSame(far, result[2], "третья ячейка — самый дальний из отобранных");
        }

        [Test]
        public void Выборка_БолееБлизкийЧетвёртый_ВытесняетСамогоДальнего()
        {
            MakeGrid();
            // Точка поиска справа: обход чанков идёт по возрастанию X, поэтому ближние встречаются позже.
            Unit farthest = PlaceUnit("дальний_50", 5f);
            Unit middle = PlaceUnit("средний_30", 25f);
            Unit nearest = PlaceUnit("ближний_10", 45f);
            Unit late = PlaceUnit("поздний_40", 95f);

            Unit[] result = Utils.GetClosestUnitsInRadius(RightPoint, SearchRadius, 0, OwnGroundUnits(), 3);

            Assert.AreSame(nearest, result[0]);
            Assert.AreSame(middle, result[1]);
            Assert.AreSame(late, result[2], "встреченный последним, но более близкий — обязан вытеснить самого дальнего");
            CollectionAssert.DoesNotContain(result, farthest, "самый дальний вытеснен из выборки");
        }

        [Test]
        public void Выборка_БолееДальнийЧетвёртый_НеВытесняетНикого()
        {
            MakeGrid();
            Unit near = PlaceUnit("ближний_10", 15f);
            Unit middle = PlaceUnit("средний_20", 25f);
            Unit far = PlaceUnit("дальний_30", 35f);
            Unit tooFar = PlaceUnit("самый_дальний_40", 45f);

            Unit[] result = Utils.GetClosestUnitsInRadius(LeftPoint, SearchRadius, 0, OwnGroundUnits(), 3);

            Assert.AreSame(near, result[0]);
            Assert.AreSame(middle, result[1]);
            Assert.AreSame(far, result[2]);
            CollectionAssert.DoesNotContain(result, tooFar, "кандидат дальше всех отобранных в выборку не попадает");
        }
    }
}
