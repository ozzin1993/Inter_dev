using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static UnityEngine.GraphicsBuffer;
using static UnityEngine.UI.Image;

namespace StrategyCore
{
    public class Projectile : MonoBehaviour
    {
        [ProjectileID]
        public int id;

        [Header("Renderer")]
        [Tooltip("Assign render object that will be disabled in FoW.")]
        public GameObject renderObject;
        private bool renderersEnabled;

        [Header("Visuals")]
        [Tooltip("Determines what audio should be played when the projectile hits the target.")]
        public WeaponSound impactSound;
        [Tooltip("VFX that is played when the projectile hits the target.")]
        public ParticleSystem impactVFX;

        [Header("Parameters")]
        [Tooltip("Speed of projectile.")]
        public float speed = 8.5f;
        [Tooltip("Defines the projectile�s collision detection radius.")]
        public float radius = 0.01f;

        [Space(10)]
        [Tooltip("Disable for better performance. When enabled, adds an arc trajectory and speed variation over the projectile�s lifetime.")]
        public bool ArcSpeedCurve = false;
        [Tooltip("Ignored if ArcSpeedCurve is off. Controls how the projectile�s speed changes over time. \nNOTE: Units default this curve to 0; set it to 1 for proper functionality.")]
        public AnimationCurve speedCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 1));
        [Tooltip("Ignored if ArcSpeedCurve is off. Defines the curvature of the projectile�s trajectory.")]
        [Range(0, 1)]
        public float arcFactor = 0.25f;

        // PARAMETERS CHANGED MANUALLY or SET WHEN SPAWNED
        [Header("Splash")]
        [Tooltip("Defines if this projectile deals damage in an area. splashUnitSelector must be set if Splash is on.")]
        [HideInInspector] public bool isSplash = false;
        [Tooltip("Splash radius")]
        [HideInInspector] public float splashRadius;
        [Tooltip("Defines if units that are further should be damaged less. 1 Means no reduction. 0.9 means the farthest unit gets 90% of the damage.")]
        [HideInInspector] public float splashReduction = 1;

        [Header("Bounce")]
        [Tooltip("Defines how many times it should bounce to nearby units. SearchUnitSelector must be set if Bounce is on.")]
        [HideInInspector] public int bounceCount = 0;
        [Tooltip("Defines how far bounce attack reaches")]
        [HideInInspector] public float bounceRange;
        [Tooltip("Defines if damage should be less for bounced attacks. 1 Means no reduction. 0.9 means the next unit will get only 90% of the damage")]
        [HideInInspector] public float bounceReduction = 1;

        // Technical
        [Tooltip("Should this projectile stun the target")]
        [HideInInspector] public float stunTime = 0;

        [Header("Technical")]
        // Damage. Set by the unit that launces the projectile
        [HideInInspector] public float damage; // Damage of the projectile
        [HideInInspector] public DamageType damageType; // Damage type of the projectile
        [HideInInspector] public Effector[] attackEffectors; // Effector that will be applied to target unit
        [HideInInspector] public Ability sourceAbility; // умение-источник для диагностики очереди пакетов (решение Artsiom 05.09.2026); null — снаряд автоатаки
        // [Interflow fix 2026-08-01 projectile-callbacks] Инициализация списка. Поле заполняется ТОЛЬКО в ветке
        // !manualParameterSet, а все перегрузки Spawn (путь способностей) передают manualParameterSet = true —
        // значит у снаряда способности список оставался null. В Damage() ветка «кастер погиб» делает по нему
        // foreach: если кастер умирал, пока снаряд летел, ловили NullReferenceException, который обрывал Update
        // ДО Destroy(gameObject) — снаряд зависал и каждый кадр заново станил и бил цель.
        [HideInInspector] public List<AfterDamageDealCallback> OnAfterDamageDealCallbacks = new();

        [Tooltip("Should projectile follow target")]
        [HideInInspector] public bool followTarget = true;

        // Unit Selector. Set by the unit that launces the projectile
        [Tooltip("Defines what kind of units are affected by Bounce modificator. Must be set if Bounce is on.")]
        [HideInInspector] public UnitSelector searchUnitSelector; // Defines what kind of units get affected by this projectile
        [Tooltip("Defines what kind of units are affected by Splash modificator. Must be set if Splash is on.")]
        [HideInInspector] public UnitSelector splashUnitSelector;

        // Ownership. Set by the unit that launches the projectile
        Unit ownerUnit; // Which unit sent projectile
        int owner; // Who sent projectile

        bool directAttack; // If this projectile launched by unit attack or by ability/effector
        Unit target; // Target unit. If null projectile goes to targetPosition
        Vector3 targetPosition; // Target position is used when target is null
        float radiusSq; // Radius squared; optimisation.
        Vector3 origin; // To store where the projectile first spawned.
        float currentTime;

        // Position factor
        [HideInInspector] public float posFactor = 0;

        void Awake()
        {
            // Pre-compute the value. 
            radiusSq = radius * radius;
            origin = transform.position;
        }

        void Update()
        {
            if (!SlotManager.Instance.gameOn) return;

            // If there is no target, destroy itself and end execution.
            if (!target && followTarget)
            {
                if (impactVFX && renderersEnabled) Instantiate(impactVFX, this.transform.position, Quaternion.identity);
                // Play ground hit Sound - Attack sound, target and audio clips must be defined
                if (impactSound != null && impactSound.groundHitClips != null && impactSound.groundHitClips.Length > 0) Presentation.Audio?.PlaySoundClip(impactSound.groundHitClips, this.transform, 1);

                GameManager.Instance.Tick -= VisibilityCheck;
                Destroy(gameObject);

                return; // Stops executing this function.
            }

            if (followTarget)
            {
                targetPosition = target.transform.position + new Vector3(0, target.unitHeight * 0.5f, 0);
            }

            // Current implementation is factor based
            float totalDistance = Vector3.Distance(origin, targetPosition);
            Vector3 previousPosition = transform.position;
            Vector3 direction = targetPosition - origin;

            if (!ArcSpeedCurve)
            {
                posFactor += Time.deltaTime * (speed / totalDistance);

                // Position
                transform.position = origin + direction.normalized * (totalDistance * posFactor);

                if (Time.deltaTime != 0)
                {
                    // Rotation
                    transform.rotation = Quaternion.LookRotation((transform.position - previousPosition).normalized);
                }
            }
            else
            {
                currentTime += Time.deltaTime;
                posFactor += Time.deltaTime * (speed * speedCurve.Evaluate(currentTime / (totalDistance / speed)) / totalDistance); // Closer the target faster the projectile speed. 0.5f is distance`s influence strength

                // Position
                transform.position = origin + direction.normalized * (totalDistance * posFactor);

                float heightOffset = arcFactor * totalDistance * Mathf.Sin((totalDistance * posFactor) * Mathf.PI / totalDistance);
                transform.position += new Vector3(0, heightOffset, 0);

                if (Time.deltaTime != 0)
                {
                    // Rotation
                    transform.rotation = Quaternion.LookRotation((transform.position - previousPosition).normalized);
                }
            }

            // Projectile reached the target
            if (posFactor >= 1 || (targetPosition - transform.position).sqrMagnitude < radiusSq)
            {
                if (impactVFX && renderersEnabled) Instantiate(impactVFX, this.transform.position, Quaternion.identity);

                // Damage target if not splash
                if (followTarget)
                {
                    Damage(target, damage, damageType);

                    // Play Sound - Attack sound, target and audio clips must be defined
                    if (impactSound != null && impactSound.weaponSound[target.armorType.index].audioClips != null) Presentation.Audio?.PlaySoundClip(impactSound.weaponSound[target.armorType.index].audioClips, this.transform, 1);
                }
                else
                {
                    // Play ground hit Sound - Attack sound, target and audio clips must be defined
                    if (impactSound != null && impactSound.groundHitClips != null && impactSound.groundHitClips.Length > 0) Presentation.Audio?.PlaySoundClip(impactSound.groundHitClips, this.transform, 1);
                }

                // Splash logic - get all units in radius, damage them accordingly
                if (isSplash)
                {
                    Unit[] units;
                    if (followTarget) units = Utils.GetUnitsInRadius(new Vector2(targetPosition.x, targetPosition.z), splashRadius, owner, splashUnitSelector, -1, target);
                    else units = Utils.GetUnitsInRadius(new Vector2(targetPosition.x, targetPosition.z), splashRadius, owner, splashUnitSelector);

                    for (int i = 0; i < units.Length; i++)
                    {
                        // Damage unit based on distance if there is a splashReduction
                        float damageAmount = damage;
                        if (splashReduction != 1)
                        {
                            float dist = Vector2.Distance(new Vector2(units[i].transform.position.x, units[i].transform.position.z), new Vector2(targetPosition.x, targetPosition.z));
                            damageAmount = (damage * (1 - (dist / splashRadius) * (1 - splashReduction)));
                        }

                        Damage(units[i], damageAmount, damageType);
                    }
                }

                // Bouncy projectile
                if (bounceCount != 0)
                {
                    //  Search for closest unit that meets requirements
                    Unit unit = Utils.GetClosestUnit(new Vector2(targetPosition.x, targetPosition.z), bounceRange, owner, searchUnitSelector, target);

                    if (unit == null)
                    {
                        // No such unit in radius, destroy projectile
                        GameManager.Instance.Tick -= VisibilityCheck;
                        Destroy(gameObject);
                    }
                    else
                    {
                        // Unit is found, go to that unit next
                        if (followTarget)
                        {
                            origin = target.transform.position + new Vector3(0, target.unitHeight * 0.5f, 0);
                            SetCollisionRadius(unit.unitRadius);
                        }
                        else
                        {
                            origin = targetPosition;
                            targetPosition = unit.transform.position + new Vector3(0, unit.unitHeight * 0.5f, 0);
                            SetCollisionRadius(0.01f);
                        }
                        posFactor = 0;
                        target = unit;

                        damage = damage * bounceReduction;
                        bounceCount--;
                    }
                }
                else
                {
                    // We have reached the target and it is not bouncy projectile so there is no need to look for the next target. Destroy it.
                    GameManager.Instance.Tick -= VisibilityCheck;
                    Destroy(gameObject);
                }
            }
        }

        /// <summary>
        /// Set the collision radius for the projectile.
        /// </summary>
        /// <param name="targetRadius">Radius of the target.</param>
        public void SetCollisionRadius(float targetRadius)
        {
            radius = targetRadius;
            radiusSq = targetRadius * targetRadius;
        }

        /// <summary>
        /// Deals damage to specified target.
        /// </summary>
        /// <param name="targetUnit">Target unit to get damage.</param>
        /// <param name="amount">Damage amount.</param>
        /// <param name="damageType">Damage type.</param>
        public void Damage(Unit targetUnit, float amount, DamageType damageType)
        {
            // Stun
            // [Interflow fix 2026-09-03 control-as-effectors] Источник оглушения — стрелок. Он мог
            // погибнуть в полёте (ownerUnit == null) — слот игрока-владельца снаряда остаётся верным.
            if (stunTime != 0) targetUnit.Stun(stunTime, ownerUnit, owner);

            // Unit who sent projectile has died, deal damage with projectile and define what happens when projectile kills
            if (ownerUnit == null)
            {
                // [Interflow fix 2026-09-04 damage-full-packet] Пакет одной записи; состояния атаки едут в пакете
                // и вешаются приёмником только при прямой атаке, живому, после урона (решение Artsiom Р7).
                // Бьющего нет — пробитие и промах в пакете нулевые, состояния вешаются от слота игрока.
                DamagePacket packet = DamagePacket.Create(amount, damageType, owner, null, directAttack, sourceAbility, attackEffectors);
                targetUnit.GetDamage(in packet, out float _);

                // After damage callbacks
                foreach (var c in OnAfterDamageDealCallbacks)
                {
                    c.Callback(target, targetPosition, attackEffectors, damage, directAttack, damageType, ownerUnit, this, owner, c.Level);
                }
            }
            else
            {
                if (followTarget) ownerUnit.DealDamage(targetUnit, amount, damageType, directAttack, target.transform.position, sourceAbility);
                else ownerUnit.DealDamage(targetUnit, amount, damageType, directAttack, targetPosition, sourceAbility);
            }
        }

        // ============================= SPAWN PROJECTILE ===========================================================

        /// <summary>
        /// Spawns a projectile that copies whoSent unit`s parameters to target Unit.
        /// </summary>
        /// <param name="whoSent">Unit that sent the projectile. This unit is used to determine damage, damage type, bounce, splash. follow target.</param>
        /// <param name="prefab">Projectile object.</param>
        /// <param name="position">Initial position of the projectile.</param>
        /// <param name="rotation">Initial rotation of the projectile.</param>
        /// <param name="target">Target unit.</param>
        /// <param name="searchUnitSelector">Defines what kind of units are affected by Bounce modificator.</param>
        /// <param name="splashUnitSelector">Defines what kind of units are affected by Splash modificator.</param>
        /// <param name="directAttack">Is this a direct attack? Unit with Evasion has a chance to deflect the damage if direct attack.</param>
        /// <param name="FoWVisibilityCheck">Should the projectile not be visible in FoW.</param>
        /// <param name="stunTime">Should the projectilr stun the target.</param>
        /// <returns>Spawned projectile.</returns>
        public static Projectile SpawnAttack(Unit whoSent, Projectile prefab, Vector3 position, Quaternion rotation, Unit target, UnitSelector searchUnitSelector, UnitSelector splashUnitSelector, float dmgAmount, bool directAttack, bool FoWVisibilityCheck = true, float stunTime = 0)
        {
            return InternalSpawn(whoSent, whoSent.owner, prefab, position, rotation, target, Vector3.zero, searchUnitSelector, splashUnitSelector, dmgAmount,directAttack, false, FoWVisibilityCheck);
        }

        /// <summary>
        /// Spawns a projectile that copies whoSent unit`s parameters to target Position.
        /// </summary>
        /// <param name="whoSent">Unit that sent the projectile. This unit is used to determine damage, damage type, bounce, splash. follow target.</param>
        /// <param name="prefab">Projectile object.</param>
        /// <param name="position">Initial position of the projectile.</param>
        /// <param name="rotation">Initial rotation of the projectile.</param>
        /// <param name="targetPosition">Target position.</param>
        /// <param name="searchUnitSelector">Defines what kind of units are affected by Bounce modificator.</param>
        /// <param name="splashUnitSelector">Defines what kind of units are affected by Splash modificator.</param>
        /// <param name="directAttack">Is this a direct attack? Unit with Evasion has a chance to deflect the damage if direct attack.</param>
        /// <param name="FoWVisibilityCheck">Should the projectile not be visible in FoW.</param>
        /// <param name="stunTime">Should the projectilr stun the target.</param>
        /// <returns>Spawned projectile.</returns>
        public static Projectile SpawnAttack(Unit whoSent, Projectile prefab, Vector3 position, Quaternion rotation, Vector3 targetPosition, UnitSelector searchUnitSelector, UnitSelector splashUnitSelector, float dmgAmount, bool directAttack, bool FoWVisibilityCheck = true, float stunTime = 0)
        {
            return InternalSpawn(whoSent, whoSent.owner, prefab, position, rotation, null, targetPosition, searchUnitSelector, splashUnitSelector, dmgAmount, directAttack, false, FoWVisibilityCheck);
        }

        /// <summary>
        /// Spawn a projectile to target Unit. Manually should set Attack Effectors / Splash + splashUnitSelector / Bounce + searchUnitSelector / Stun parameters!
        /// </summary>
        /// <param name="owner">Owner of the projectile.</param>
        /// <param name="whoSent">Can be null. Unit that sent the projectile, will be rewarded.</param>
        /// <param name="prefab">Projectile object.</param>
        /// <param name="position">Initial position of the projectile.</param>
        /// <param name="rotation">Initial rotation of the projectile.</param>
        /// <param name="target">Target unit.</param>
        /// <param name="directAttack">Is this a direct attack? Unit with Evasion has a change to deflect the damage if direct attack.</param>
        /// <param name="damage">Damage amount.</param>
        /// <param name="dmgType">Damage type.</param>
        /// <param name="followTarget">Should the projectile follow the target. When off it must have Splash on!</param>
        /// <param name="FoWVisibilityCheck">Should the projectile not be visible in FoW.</param>
        /// <returns>Spawned projectile.</returns>
        public static Projectile Spawn(int owner, Unit whoSent, Projectile prefab, Vector3 position, Quaternion rotation, Unit target, bool directAttack, float damage, DamageType dmgType, bool followTarget = true, bool FoWVisibilityCheck = true, Ability sourceAbility = null)
        {
            Projectile p = InternalSpawn(whoSent, owner, prefab, position, rotation, target, Vector3.zero, new UnitSelector(), new UnitSelector(), damage, directAttack, true, FoWVisibilityCheck);

            // [Interflow fix 2026-08-01 projectile-followtarget] Согласуем флаг и состояние.
            // InternalSpawn решает «лететь за целью или в точку» по параметрам АВТОАТАКИ кастера
            // (isSplash + projectileFollowTarget), а не по параметрам способности. Прежняя строка
            // «p.followTarget = followTarget» перебивала это решение, не трогая p.target и p.targetPosition:
            //  - кастер площадной и без самонаведения → p.target не присвоен, а флаг вернулся в true →
            //    Update первым же условием (!target && followTarget) уничтожал снаряд на точке вылета;
            //  - followTarget = false при обычном кастере → p.targetPosition остался нулём → полёт в мировой ноль.
            if (followTarget && target != null)
            {
                p.followTarget = true;
                p.target = target;
                p.SetCollisionRadius(target.unitRadius);
            }
            else
            {
                p.followTarget = false;
                p.target = null;
                if (target != null) p.targetPosition = target.transform.position;
                p.SetCollisionRadius(0.01f);
            }

            p.damageType = dmgType;
            p.sourceAbility = sourceAbility;

            return p;
        }

        /// <summary>
        /// Spawn a projectile to target Position. Manually should set Attack Effectors / Splash + splashUnitSelector / Bounce + searchUnitSelector / Stun parameters!
        /// </summary>
        /// <param name="owner">Owner of the projectile.</param>
        /// <param name="whoSent">Can be null. Unit that sent the projectile, will be rewarded.</param>
        /// <param name="prefab">Projectile object.</param>
        /// <param name="position">Initial position of the projectile.</param>
        /// <param name="rotation">Initial rotation of the projectile.</param>
        /// <param name="targetPosition">Target position for the projectile.</param>
        /// <param name="directAttack">Is this a direct attack? Unit with Evasion has a change to deflect the damage if direct attack.</param>
        /// <param name="damage">Damage amount.</param>
        /// <param name="dmgType">Damage type.</param>
        /// <param name="followTarget">Should the projectile follow the target. When off it must have Splash on!</param>
        /// <param name="FoWVisibilityCheck">Should the projectile not be visible in FoW.</param>
        /// <returns>Spawned projectile.</returns>
        public static Projectile Spawn(int owner, Unit whoSent, Projectile prefab, Vector3 position, Quaternion rotation, Vector3 targetPosition, bool directAttack, float damage, DamageType dmgType, bool FoWVisibilityCheck = true, Ability sourceAbility = null)
        {
            Projectile p = InternalSpawn(whoSent, owner, prefab, position, rotation, null, targetPosition, new UnitSelector(), new UnitSelector(), damage, directAttack, true, FoWVisibilityCheck);
            p.damageType = dmgType;
            p.sourceAbility = sourceAbility;

            return p;
        }

        /// <summary>
        /// Used internally to spawn the projectile
        /// </summary>
        private static Projectile InternalSpawn(Unit whoSent, int owner, Projectile prefab, Vector3 position, Quaternion rotation, Unit target, Vector3 targetPosition, UnitSelector searchUnitSelector, UnitSelector splashUnitSelector, float dmgAmount, bool directAttack, bool manualParameterSet, bool FoWVisibilityCheck = true, float stunTime = 0)
        {
            Projectile p = Instantiate(prefab, position, rotation);

            // Ownership
            p.ownerUnit = whoSent;
            p.owner = owner;
            p.directAttack = directAttack;

            p.damage = dmgAmount;

            // When manual parameter set is on we should set damage/splash/bounce/stun parameters directly. Used by abilities
            if (!manualParameterSet)
            {
                if (whoSent != null)
                {
                    // Damage
                    p.damageType = whoSent.damageType;
                    p.attackEffectors = whoSent.attackEffectors;
                    p.OnAfterDamageDealCallbacks = whoSent.OnAfterDamageDealCallbacks;

                    // Splash
                    p.isSplash = whoSent.isSplash;
                    p.splashRadius = whoSent.splashRadius;
                    p.splashReduction = whoSent.splashReduction;

                    // Bounce
                    p.bounceCount = whoSent.bounceCount;
                    p.bounceRange = whoSent.bounceRange;
                    p.bounceReduction = whoSent.bounceReduction;
                }

                // Stun
                p.stunTime = stunTime;
            }

            // Set the projectile's target, so that it can work. If we want to damage unit at position projectile must have splash
            if (targetPosition != Vector3.zero || (whoSent.isSplash && !whoSent.projectileFollowTarget))
            {
                // Do not follow target
                p.followTarget = false;
                p.targetPosition = (target != null) ? target.transform.position : targetPosition;
                p.SetCollisionRadius(0.01f);
            }
            else if (target != null)
            {
                // Follow target
                p.followTarget = true;
                p.target = target;
                p.SetCollisionRadius(target.unitRadius);
            }

            // FoW Visibility checks
            if (!FogOfWar.Instance.TurnOff && FoWVisibilityCheck)
            {
                if (whoSent != null)
                {
                    if (!FogOfWar.Instance.IsVisible(whoSent.FoWCell, SlotManager.Instance.currentTeam)) p.DisableRenderers(true);
                    else p.renderersEnabled = true;
                }
                else
                {
                    if (!FogOfWar.Instance.IsVisible(position, SlotManager.Instance.currentTeam)) p.DisableRenderers(true);
                    else p.renderersEnabled = true;
                }

                GameManager.Instance.Tick += p.VisibilityCheck;
            }

            p.searchUnitSelector = searchUnitSelector;
            p.splashUnitSelector = splashUnitSelector;
            return p;
        }

        // ============================= VISIBILITY CHECK ===========================================================

        /// <summary>
        /// Every gameManager tickrate tests if this particle should be visible
        /// </summary>
        public void VisibilityCheck()
        {
            if (FogOfWar.Instance.IsVisible(FogOfWar.GetCellByPosition(transform.position), SlotManager.Instance.currentTeam))
            {
                EnableRenderers();      
            }
            else
            {
                DisableRenderers();
            }
        }

        /// <summary>
        /// Enables the renderers of the projectile. Renderers must be set manually in the inspector.
        /// </summary>
        public void EnableRenderers()
        {
            if (renderersEnabled == false)
            {
                renderersEnabled = true;
                renderObject.SetActive(true);
            }
        }

        /// <summary>
        /// Disables the renderers of the projectile. Renderers must be set manually in the inspector.
        /// </summary>
        public void DisableRenderers()
        {
            if (renderersEnabled == true)
            {
                renderersEnabled = false;
                renderObject.SetActive(false);
            }
        }

        /// <summary>
        /// Disables the renderers of the projectile without checking if they were enabled. Used in initialization. Renderers must be set manually in the inspector.
        /// </summary>
        /// <param name="nocheck">Indicates that it should force disable renderers.</param>
        public void DisableRenderers(bool nocheck)
        {
            renderersEnabled = false;
            renderObject.SetActive(false);
        }
    }
}