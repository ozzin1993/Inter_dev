using System;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Данные об умершем юните и месте смерти. Носитель для реестра могилок (сервер)
    /// и компонента GraveMarker (на заспавненной могилке).
    /// Расширяемо: новые поля добавлять только по прямому указанию (правило 7).
    /// </summary>
    [Serializable]
    public class GraveData
    {
        [Tooltip("Тип умершего юнита (Unit.unitTypeID). Ключ к префабу через GameManager.gameUnits (будущее воскрешение).")]
        public int unitTypeID;
        [Tooltip("Владелец умершего юнита — индекс игрока (Unit.owner).")]
        public int owner;
        [Tooltip("Команда умершего юнита (Unit.team).")]
        public int team;
        [Tooltip("Категория/класс умершего юнита (Unit.unitCategory).")]
        public Unit.UnitCategory unitCategory;
        [Tooltip("Тир умершего юнита (Unit.tier).")]
        public int tier;
        [Tooltip("Место смерти — мировые координаты.")]
        public Vector3 position;
    }
}
