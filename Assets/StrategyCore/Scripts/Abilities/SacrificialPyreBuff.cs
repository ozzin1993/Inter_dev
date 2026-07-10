using UnityEngine;

namespace StrategyCore
{
    // Временное «горение» на союзном орке от «Обряда Жертвенного Костра»: каждый штатный тик (GameManager.Tick)
    // аура наносит урон врагам вокруг носителя + сам носитель теряет ХП; при гибели носителя под бафом —
    // детонация SkvernaExplosion (урон по врагам). Навешивается из SacrificialPyreActive.
    // Урон/самосожжение/детонация — только на сервере (правило 6); VFX — локально на каждом пире (все пиры).
    // Не стакается: повторное наложение продлевает остаток времени. Ядро StrategyCore не трогаем (правило 1).
    public class SacrificialPyreBuff : MonoBehaviour
    {
        private Unit unit;
        private SacrificialPyreActive ability;   // источник параметров (.asset) — единый источник (правило 5)
        private int level;
        private Unit hero;                        // кастер-герой: источник урона детонации (dealer для DealDamage)
        private float auraRadius;                 // добавка к радиусу ауры (radius[level])
        private VFXReferencer vfxInstance;        // экземпляр VFX; снимаем прямым Destroy, не через RemoveVFX
        private float remaining;
        private bool subscribed;
        private bool dieHooked;
        private bool exploded;                    // детонация одноразова (guard)

        /// <summary>Поджечь орк или продлить горение (берётся больший остаток времени).</summary>
        public static void Apply(Unit target, SacrificialPyreActive ability, int level, Unit hero, float auraRadius, float duration)
        {
            if (target == null || target.dead || duration <= 0f) return;
            SacrificialPyreBuff buff = target.GetComponent<SacrificialPyreBuff>();
            if (buff == null) buff = target.gameObject.AddComponent<SacrificialPyreBuff>();
            buff.Init(target, ability, level, hero, auraRadius, duration);
        }

        private void Init(Unit target, SacrificialPyreActive ability, int level, Unit hero, float auraRadius, float duration)
        {
            unit = target;
            this.ability = ability;
            this.level = level;
            this.hero = hero;
            this.auraRadius = auraRadius;
            remaining = Mathf.Max(remaining, duration);

            // VFX один раз (не дублируем при продлении). Локально на каждом пире.
            if (vfxInstance == null && ability != null && ability.VFX != null)
            {
                vfxInstance = unit.AddVFX(ability.VFX, false, true);
                if (vfxInstance != null)
                    vfxInstance.transform.SetGlobalScale(new Vector3(
                        unit.unitRadius * 2f + auraRadius, unit.unitHeight, unit.unitRadius * 2f + auraRadius));
            }

            if (!subscribed && GameManager.instance != null)
            {
                GameManager.instance.Tick += OnTick;
                subscribed = true;
            }
            // Детонация при гибели носителя под бафом — штатное событие Unit.OnDie (сервер отработает внутри).
            if (!dieHooked)
            {
                unit.OnDie += HandleDie;
                dieHooked = true;
            }
        }

        private void OnTick()
        {
            if (unit == null || unit.dead) { Cleanup(); return; }
            if (GameManager.instance == null || ability == null) return;

            // Урон/самосожжение — только на сервере (правило 6). ChangeHP/DealDamage синкаются штатно.
            if (!NetworkConnectionHandler.isClient)
            {
                float dt = GameManager.instance.currentDeltaTime;

                // Аура: урон врагам вокруг носителя (dealer = сам горящий орк, как FlameCloak).
                if (ability.auraDamagePerSecond > 0f && ability.auraDamageType != null)
                {
                    Vector2 pos = new Vector2(unit.transform.position.x, unit.transform.position.z);
                    Unit[] targets = Utils.GetUnitsInRadius(pos, unit.unitRadius + auraRadius, unit.owner, ability.auraSelector, -1, unit);
                    for (int i = 0; i < targets.Length; i++)
                        if (targets[i] != null && !targets[i].dead)
                            unit.DealDamage(targets[i], ability.auraDamagePerSecond * dt, ability.auraDamageType, false, Vector3.zero);
                }

                // Самосожжение носителя.
                if (ability.selfBurnPerSecond > 0f)
                    unit.ChangeHP(-ability.selfBurnPerSecond * dt);
            }

            remaining -= GameManager.instance.currentDeltaTime;
            if (remaining <= 0f) Cleanup();
        }

        // Гибель горящего орка → детонация по врагам (dealer = герой-кастер). Одноразово (exploded).
        private void HandleDie(Unit u, int playerThatKills, Unit unitThatKills, bool rewards)
        {
            if (!exploded && !NetworkConnectionHandler.isClient && ability != null && unit != null)
            {
                exploded = true;
                Vector2 pos = new Vector2(unit.transform.position.x, unit.transform.position.z);
                SkvernaExplosion.Detonate(hero, pos, ability.deathExplosionRadius,
                    ability.deathExplosionDamage, ability.deathExplosionDamageType, ability.auraSelector,
                    0f, null, default(UnitSelector), unit);   // только враги: allyDamage=0 → группа своих пропускается
            }
            Cleanup();
        }

        private void Cleanup()
        {
            if (subscribed && GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            subscribed = false;
            if (dieHooked && unit != null) { unit.OnDie -= HandleDie; dieHooked = false; }
            if (vfxInstance != null) Destroy(vfxInstance.gameObject);
            vfxInstance = null;
            Destroy(this);
        }

        private void OnDestroy()
        {
            if (subscribed && GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            subscribed = false;
            if (dieHooked && unit != null) { unit.OnDie -= HandleDie; dieHooked = false; }
            if (vfxInstance != null) Destroy(vfxInstance.gameObject);
            vfxInstance = null;
        }
    }
}
