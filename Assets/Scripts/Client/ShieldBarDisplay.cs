using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Серый сегмент поглощающего щита на полоске здоровья юнита. Чистая клиентская презентация:
    /// величину щита назначает только сервер (AbsorbShield), сюда она попадает напрямую (хост)
    /// или единым каналом статусов (NetworkDataSync.UnitStatus, чистый клиент) — правило 6.
    ///
    /// Рисование: дочерний квад на объекте полоски здоровья с шейдером StrategyCore/HealthBarShield —
    /// той же билборд-математикой, что у самой полоски, иначе сегмент не лёг бы на неё при поворотах камеры.
    /// Сегмент начинается там, где кончается заполненная часть здоровья («щит поверх здоровья»);
    /// не влезает справа — прижимается к правому краю (решение Artsiom 2026-08-23).
    /// Ширина = щит / максимум здоровья, тает вместе со щитом, исчезает при снятии.
    ///
    /// Компонент вешается лениво при первом появлении щита; полей в Inspector нет намеренно —
    /// материал сегмента задаётся в настройках презентации (Resources/Catalogs/SkillPresentationSettings).
    /// </summary>
    public class ShieldBarDisplay : MonoBehaviour
    {
        static readonly int segStartProp = Shader.PropertyToID("_SegStart");
        static readonly int segWidthProp = Shader.PropertyToID("_SegWidth");

        Unit unit;
        float amount;                    // текущий объём щита (для пересчёта при изменении здоровья)
        GameObject segment;              // квад сегмента (child полоски)
        MeshRenderer segmentRenderer;
        MaterialPropertyBlock matBlock;
        bool subscribed;

        /// <summary>Показ на этом пире. Зовёт презентер по факту ShieldChanged (хост — локальный Raise, клиент — из RPC).</summary>
        public static void LocalShow(Unit target, float shieldAmount)
        {
            if (Utils.Headless || target == null) return;   // выделенному серверу рисовать некому

            ShieldBarDisplay display = target.GetComponent<ShieldBarDisplay>();
            if (display == null)
            {
                if (shieldAmount <= 0f) return;             // снимать нечего
                display = target.gameObject.AddComponent<ShieldBarDisplay>();
                display.unit = target;
            }

            display.SetAmount(shieldAmount);
        }

        void SetAmount(float value)
        {
            amount = value;

            if (amount <= 0f) { Remove(); return; }

            if (!subscribed && unit != null)
            {
                unit.OnHPChange += Refresh;                 // начало сегмента зависит от текущего здоровья
                unit.OnDie += OnUnitDies;
                subscribed = true;
            }

            Refresh();
        }

        /// <summary>Пересчёт сегмента. Зовётся и на изменении здоровья: заодно переживает пересоздание полоски (смена команды).</summary>
        void Refresh()
        {
            if (unit == null || unit.dead || unit.maxHealth <= 0f) { Remove(); return; }

            if (segment == null && !TryCreateSegment()) return;

            float width = Mathf.Clamp01(amount / unit.maxHealth);
            float hpFraction = Mathf.Clamp01(unit.health / unit.maxHealth);
            float start = Mathf.Min(hpFraction, 1f - width); // не влезает справа — прижать к краю

            if (matBlock == null) matBlock = new MaterialPropertyBlock();
            segmentRenderer.GetPropertyBlock(matBlock);
            matBlock.SetFloat(segStartProp, start);
            matBlock.SetFloat(segWidthProp, width);
            segmentRenderer.SetPropertyBlock(matBlock);
        }

        /// <summary>
        /// Создать квад сегмента ребёнком полоски здоровья. Полоски может не быть в этот кадр
        /// (пересоздание при смене команды) — попробуем снова при следующем изменении здоровья.
        /// </summary>
        bool TryCreateSegment()
        {
            // [Interflow 2026-09-09 unit-overlay] Полоска — по ссылке юнита: она переехала
            // в контейнер надюнитовых элементов, поиском по прямым детям её больше не найти.
            Transform bar = unit != null ? unit.HealthBarRoot : null;
            if (bar == null) return false;

            Material material = ResolveMaterial();
            if (material == null) return false;

            segment = GameObject.CreatePrimitive(PrimitiveType.Quad);
            segment.name = "ShieldBarSegment";
            Destroy(segment.GetComponent<Collider>());      // квад нужен только как меш, столкновения ему ни к чему

            segment.layer = bar.gameObject.layer;
            segment.transform.SetParent(bar, false);        // наследует позицию и масштаб — их читает билборд-шейдер

            segmentRenderer = segment.GetComponent<MeshRenderer>();
            segmentRenderer.sharedMaterial = material;
            segmentRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            segmentRenderer.receiveShadows = false;
            segmentRenderer.sortingOrder = 1;               // поверх полоски: очередь та же, у полоски порядок 0

            return true;
        }

        static Material cachedMaterial;

        /// <summary>Материал из настроек презентации; без ассета — серый по умолчанию (подстраховка для редактора).</summary>
        static Material ResolveMaterial()
        {
            if (cachedMaterial != null) return cachedMaterial;

            SkillPresentationSettings settings = Resources.Load<SkillPresentationSettings>(SkillPresentationSettings.ResourcePath);
            if (settings != null && settings.shieldBarMaterial != null) return cachedMaterial = settings.shieldBarMaterial;

            Shader shader = Shader.Find("StrategyCore/HealthBarShield");
            if (shader == null)
            {
                InterflowDebug.Warn("Сегмент щита: нет ни материала в настройках презентации, ни шейдера StrategyCore/HealthBarShield — щит на полоске не показывается.");
                return null;
            }

            cachedMaterial = new Material(shader);
            return cachedMaterial;
        }

        void OnUnitDies(Unit dies, int killerPlayer, Unit killerUnit, bool rewards) => Remove();

        /// <summary>Снять сегмент и сам компонент: щита больше нет.</summary>
        void Remove()
        {
            if (segment != null) { Destroy(segment); segment = null; segmentRenderer = null; }
            Unsubscribe();
            Destroy(this);
        }

        void Unsubscribe()
        {
            if (!subscribed) return;
            if (unit != null)
            {
                unit.OnHPChange -= Refresh;
                unit.OnDie -= OnUnitDies;
            }
            subscribed = false;
        }

        void OnDestroy() => Unsubscribe();
    }
}
