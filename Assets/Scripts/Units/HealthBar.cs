using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    public class HealthBar : MonoBehaviour
    {
        public Unit unit;

        MaterialPropertyBlock matBlock;
        MeshRenderer meshRenderer;

        // Start is called before the first frame update
        void Start()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            // [Interflow fix 2026-08-01 grid-headless] На сервере Roles-стрип вырезает MeshRenderer — бар
            // некому рисовать: выходим без подписок. Иначе NRE в UpdateHealthBar при каждом изменении HP
            // обрывал цепочку остальных подписчиков OnHPChange юнита (мультикаст-делегат).
            if (meshRenderer == null) { enabled = false; return; }
            matBlock = new MaterialPropertyBlock();

            unit = transform.parent.GetComponent<Unit>();
            transform.localScale = new Vector3(unit.unitRadius / transform.lossyScale.x, 0.1f / transform.lossyScale.y, 1);
            transform.localPosition += new Vector3(0, unit.unitHeight * 1.3f, 0);

            unit.OnHPChange += UpdateHealthBar;
            unit.OnDie += Unsub;
            UpdateHealthBar();
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
                unit.OnHPChange -= UpdateHealthBar;
            }
        }

        // Идентификатор свойства шейдера вместо строки в горячем пути (ревью 1.10)
        static readonly int fillProp = Shader.PropertyToID("_Fill");

        public void UpdateHealthBar()
        {
            meshRenderer.GetPropertyBlock(matBlock);
            matBlock.SetFloat(fillProp, unit.health / unit.maxHealth);
            meshRenderer.SetPropertyBlock(matBlock);
        }
    }
}
