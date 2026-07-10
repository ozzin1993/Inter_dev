using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Компонент на префабе-могилке: хранит данные об умершем юните (GraveData).
    /// Пассивный носитель — время жизни и синхронизацию ведёт менеджер (серверо-авторитетно),
    /// а не сам компонент. Вешается на префаб gravePrefab (FactionConfig).
    /// </summary>
    public class GraveMarker : MonoBehaviour
    {
        [Tooltip("Данные умершего юнита и места смерти. Заполняется при спавне могилки.")]
        public GraveData data = new GraveData();

        /// <summary>Записать данные в могилку (вызывается при спавне).</summary>
        public void SetData(GraveData graveData)
        {
            data = graveData;
        }
    }
}
