using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Полоска шкалы (gauge) под полоской маны. Устройство идентично <see cref="ManaBar"/>:
    /// меш-билборд, шейдер StrategyCore/HealthBar, заполнение через _Fill, смещение через _YOffset.
    /// Три отличия: событие OnGaugeChange, поля gauge/maxGauge, гейт maxGauge, сдвиг 2.5 полоски.
    /// </summary>
    public class GaugeBar : MonoBehaviour
    {
        [Tooltip("Смещение вниз от полоски здоровья В ВЫСОТАХ ПОЛОСКИ: " +
                 "2.5 — ниже маны (мана на 1.5), зазор в половину высоты.")]
        [SerializeField] private float offsetInBarHeights = 2.5f;

        private Unit unit;

        MaterialPropertyBlock matBlock;
        MeshRenderer meshRenderer;

        static readonly int fillProp = Shader.PropertyToID("_Fill");
        static readonly int yOffsetProp = Shader.PropertyToID("_YOffset");

        void Start()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null) { enabled = false; return; }
            matBlock = new MaterialPropertyBlock();

            unit = GetComponentInParent<Unit>();
            if (unit == null) { enabled = false; return; }

            transform.localScale = new Vector3(unit.unitRadius / transform.lossyScale.x, HealthBar.BarHeight / transform.lossyScale.y, 1);
            transform.localPosition += new Vector3(0, unit.unitHeight * HealthBar.LiftInUnitHeights, 0);

            unit.OnGaugeChange += UpdateGaugeBar;
            unit.OnDie += Unsub;
            UpdateGaugeBar();
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
                unit.OnGaugeChange -= UpdateGaugeBar;
            }
        }

        public void UpdateGaugeBar()
        {
            if (unit.maxGauge <= 0f)
            {
                if (meshRenderer.enabled) meshRenderer.enabled = false;
                return;
            }
            if (!meshRenderer.enabled) meshRenderer.enabled = true;

            meshRenderer.GetPropertyBlock(matBlock);
            matBlock.SetFloat(fillProp, unit.gauge / unit.maxGauge);
            matBlock.SetFloat(yOffsetProp, -offsetInBarHeights * 2f);
            meshRenderer.SetPropertyBlock(matBlock);
        }
    }
}
