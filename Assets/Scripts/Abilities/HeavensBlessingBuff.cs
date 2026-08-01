using UnityEngine;

namespace StrategyCore
{
    // Временное «лечение-во-времени» на юните: каждый штатный тик (GameManager.Tick) восстанавливает
    // долю макс. ХП в течение duration, затем самоудаляется. Навешивается из HeavensBlessingActive (сервер).
    // Лечение — только на сервере (правило 6); HP синкается клиентам штатным Unit.ChangeHP.
    // Не стакается: повторное наложение продлевает остаток времени, а не складывает лечение.
    public class HeavensBlessingBuff : MonoBehaviour
    {
        private Unit unit;
        private float healPerSecondPercent; // проценты maxHealth в секунду (2 = 2%/сек)
        private VFXReferencer vfxInstance;  // экземпляр VFX от AddVFX; снимаем прямым Destroy, не через RemoveVFX
        private float remaining;
        private bool subscribed;

        /// <summary>Навесить лечение на юнит или продлить существующее (берётся больший остаток времени).</summary>
        public static void Apply(Unit target, float healPerSecondPercent, float duration, VFXReferencer vfx)
        {
            if (target == null || target.dead || duration <= 0f) return;
            HeavensBlessingBuff buff = target.GetComponent<HeavensBlessingBuff>();
            if (buff == null) buff = target.gameObject.AddComponent<HeavensBlessingBuff>();
            buff.Init(target, healPerSecondPercent, duration, vfx);
        }

        private void Init(Unit target, float healPerSecondPercent, float duration, VFXReferencer vfx)
        {
            unit = target;
            this.healPerSecondPercent = healPerSecondPercent;
            remaining = Mathf.Max(remaining, duration);

            // VFX добавляем один раз (не дублируем при продлении). Храним сам экземпляр и снимаем
            // его прямым Destroy — Unit.RemoveVFX падает, если у юнита не инициализирован vfxHolder.
            if (vfxInstance == null && vfx != null)
                vfxInstance = unit.AddVFX(vfx, false, true);

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

            // Лечение — только на сервере (правило 6). ChangeHP сам клампит до maxHealth и синкает HP клиентам.
            // healPerSecondPercent — проценты (2 = 2%/сек), переводим в долю делением на 100.
            if (!NetworkConnectionHandler.isClient && healPerSecondPercent != 0f)
                unit.ChangeHP(healPerSecondPercent / 100f * unit.maxHealth * dt);

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
