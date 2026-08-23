using UnityEngine;

namespace StrategyCore
{
    // Атрибут поля «уникальный id». Дровер — Assets/Editor/Interflow/InterflowIdDrawers.cs
    // (ревью, «долг редактора»: 7 копий дроверов сведены в один с кешем; using UnityEditor из рантайм-файла убран).
    public class EffectorIDAttribute : PropertyAttribute { }
}
