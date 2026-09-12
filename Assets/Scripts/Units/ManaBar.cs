using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Полоска маны под полоской здоровья юнита. Копия механики полоски здоровья
    /// (<see cref="HealthBar"/>): тот же меш-билборд, тот же шейдер StrategyCore/HealthBar,
    /// заполнение через свойство _Fill; отличается материалом (своя текстура) и источником данных — мана.
    ///
    /// Объект живёт в контейнере надюнитовых элементов (<see cref="Unit.OverlayRoot"/>) рядом с полоской
    /// здоровья и в ТОЙ ЖЕ точке — вниз его сдвигает только свойство шейдера _YOffset. Уничтожается,
    /// гаснет в тумане и невидимости вместе с контейнером — отдельной чистки не нужно.
    /// Ставится только своим юнитам (решение Artsiom 2026-09-09); видна, только когда у юнита есть мана.
    ///
    /// Смещение вниз задаётся свойством шейдера _YOffset, а НЕ позицией объекта: полоска — билборд,
    /// её высота на экране постоянна, а сдвиг объекта в мире ужимался бы косинусом наклона камеры
    /// (65 градусов по умолчанию) — зазор между полосками пропал бы, полоски налезли бы друг на друга.
    ///
    /// В префабе меш-рендерер ВЫКЛЮЧЕН намеренно: полоска появляется только из UpdateManaBar и только
    /// у юнита с маной — иначе между созданием объекта и первым Start успевал мелькнуть полный синий кадр.
    ///
    /// Чистая клиентская презентация: ману считает и рассылает сервер, здесь только отрисовка (правило 6).
    /// </summary>
    public class ManaBar : MonoBehaviour
    {
        [Tooltip("Смещение вниз от полоски здоровья В ВЫСОТАХ ПОЛОСКИ (полоска маны той же высоты): " +
                 "1 — вплотную, 1.5 — зазор в половину высоты, 2 — зазор в целую высоту.")]
        [SerializeField] private float offsetInBarHeights = 1.5f;

        private Unit unit;

        MaterialPropertyBlock matBlock;
        MeshRenderer meshRenderer;

        // Идентификаторы свойств шейдера вместо строк в горячем пути — как у полоски здоровья.
        static readonly int fillProp = Shader.PropertyToID("_Fill");
        static readonly int yOffsetProp = Shader.PropertyToID("_YOffset");

        void Start()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            // На сервере Roles-стрип вырезает MeshRenderer — рисовать некому, выходим без подписок
            // (иначе NRE в UpdateManaBar оборвал бы цепочку остальных подписчиков OnMPChange юнита).
            if (meshRenderer == null) { enabled = false; return; }
            matBlock = new MaterialPropertyBlock();

            // Юнит — через контейнер надюнитовых элементов: он между полоской и юнитом.
            unit = GetComponentInParent<Unit>();
            if (unit == null) { enabled = false; return; }

            // Размер и подъём — теми же формулами, что у полоски здоровья (HealthBar): полоска маны
            // стоит в той же точке, а расходятся они свойством шейдера _YOffset.
            // Высота и подъём — константами полоски здоровья: полоски обязаны совпадать, и число должно
            // жить в одном месте. В Inspector остаётся только расхождение полосок (offsetInBarHeights).
            transform.localScale = new Vector3(unit.unitRadius / transform.lossyScale.x, HealthBar.BarHeight / transform.lossyScale.y, 1);
            transform.localPosition += new Vector3(0, unit.unitHeight * HealthBar.LiftInUnitHeights, 0);

            unit.OnMPChange += UpdateManaBar;
            unit.OnDie += Unsub;
            UpdateManaBar();
        }

        private void OnDestroy()
        {
            Unsub(null, 0, null, false);
        }

        void Unsub(Unit unitThatDies, int playerKiller, Unit unitKiller, bool rewards)
        {
            if (unit != null)
            {
                unit.OnDie -= Unsub;
                unit.OnMPChange -= UpdateManaBar;
            }
        }

        public void UpdateManaBar()
        {
            // Маны у юнита может не быть вовсе, а максимум — меняться в игре (пассивки и эффекторы,
            // Unit.ChangeMaxMP) и приходить из сейва. При нуле делить не на что: полоску прячем,
            // а не считаем NaN. Появится максимум — полоска вернётся.
            if (unit.maxMana <= 0f)
            {
                if (meshRenderer.enabled) meshRenderer.enabled = false;
                return;
            }
            if (!meshRenderer.enabled) meshRenderer.enabled = true;

            meshRenderer.GetPropertyBlock(matBlock);
            matBlock.SetFloat(fillProp, unit.mana / unit.maxMana);
            // Высота меша полоски — 2 единицы, поэтому высота полоски в единицах меша тоже 2:
            // переводим «высоты полоски» в единицы меша множителем 2, вниз — со знаком минус.
            matBlock.SetFloat(yOffsetProp, -offsetInBarHeights * 2f);
            meshRenderer.SetPropertyBlock(matBlock);
        }
    }
}
