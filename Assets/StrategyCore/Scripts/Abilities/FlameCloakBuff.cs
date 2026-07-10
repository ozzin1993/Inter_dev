using UnityEngine;

namespace StrategyCore
{
    // Временный «огненный плащ» на юните: каждый штатный тик (GameManager.Tick) наносит урон
    // врагам в радиусе вокруг носителя в течение duration, затем самоудаляется.
    // Навешивается из FlameCloakActive (сервер). Урон — только на сервере (правило 6).
    // Не стакается: повторное наложение продлевает остаток времени, а не складывает урон.
    public class FlameCloakBuff : MonoBehaviour
    {
        private Unit unit;
        private float dps;
        private DamageType damageType;
        private float auraRadius;
        private UnitSelector damageSelector;
        private VFXReferencer vfxInstance; // экземпляр VFX от AddVFX; снимаем прямым Destroy, не через RemoveVFX
        private float remaining;
        private bool subscribed;

        /// <summary>Навесить плащ на юнит или продлить существующий (берётся больший остаток времени).</summary>
        public static void Apply(Unit target, float dps, DamageType damageType, float auraRadius,
                                 float duration, UnitSelector damageSelector, VFXReferencer vfx)
        {
            if (target == null || target.dead || duration <= 0f) return;
            FlameCloakBuff buff = target.GetComponent<FlameCloakBuff>();
            if (buff == null) buff = target.gameObject.AddComponent<FlameCloakBuff>();
            buff.Init(target, dps, damageType, auraRadius, duration, damageSelector, vfx);
        }

        private void Init(Unit target, float dps, DamageType damageType, float auraRadius,
                          float duration, UnitSelector damageSelector, VFXReferencer vfx)
        {
            unit = target;
            this.dps = dps;
            this.damageType = damageType;
            this.auraRadius = auraRadius;
            this.damageSelector = damageSelector;
            remaining = Mathf.Max(remaining, duration);

            // VFX добавляем один раз (не дублируем при продлении). Храним сам экземпляр и снимаем
            // его прямым Destroy — Unit.RemoveVFX падает, если у юнита не инициализирован vfxHolder.
            if (vfxInstance == null && vfx != null)
            {
                vfxInstance = unit.AddVFX(vfx, false, true);
                if (vfxInstance != null)
                    vfxInstance.transform.SetGlobalScale(new Vector3(
                        unit.unitRadius * 2f + auraRadius, unit.unitHeight, unit.unitRadius * 2f + auraRadius));
            }

            if (!subscribed && GameManager.instance != null)
            {
                GameManager.instance.Tick += OnTick;
                subscribed = true;
            }
        }

        private void OnTick()
        {
            if (unit == null || unit.dead) { Cleanup(); return; }
            if (GameManager.instance == null) return;

            float dt = GameManager.instance.currentDeltaTime;

            // Урон врагам вокруг себя — как FlameCloak.Use(). Только на сервере (правило 6).
            if (!NetworkConnectionHandler.isClient && dps != 0f)
            {
                Vector2 pos = new Vector2(unit.transform.position.x, unit.transform.position.z);
                Unit[] targets = Utils.GetUnitsInRadius(pos, unit.unitRadius + auraRadius, unit.owner, damageSelector, -1, unit);
                for (int i = 0; i < targets.Length; i++)
                    if (targets[i] != null && !targets[i].dead)
                        unit.DealDamage(targets[i], dps * dt, damageType, false, Vector3.zero);
            }

            remaining -= dt;
            if (remaining <= 0f) Cleanup();
        }

        private void Cleanup()
        {
            if (subscribed && GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            subscribed = false;
            if (vfxInstance != null) Destroy(vfxInstance.gameObject);
            vfxInstance = null;
            Destroy(this);
        }

        private void OnDestroy()
        {
            if (subscribed && GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            subscribed = false;
            if (vfxInstance != null) Destroy(vfxInstance.gameObject);
            vfxInstance = null;
        }
    }
}
