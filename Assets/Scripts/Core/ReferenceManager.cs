using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    // Helpful reference manager to materials, objects ect.

    public class ReferenceManager : MonoBehaviour, IStartupService
    {
        public static ReferenceManager Instance { get; private set; }

        private bool startupDone; // защита от повторного подъёма (стартовик сцены + собственный Awake)

        [Header("Textures")]
        public Texture2D missingTexture;

        [Header("Colors")]
        [Tooltip("Overlay colors for unts to display affected units when casting an area ability")]
        public Color affected = Color.yellow;
        [Tooltip("Overlay color for a construction building if position is invalid")]
        public Color invalid = Color.red;
        [Tooltip("Overlay color for invisible units")]
        public Color invisibility = new Color(0.1f, 0.1f, 0.1f, 1);

        [Space]
        [Tooltip("Color of selection circle of the unit currently under the cursor")]
        public Color selectionAtCursorColor = Color.yellow;
        [Tooltip("Color of selection circle for own and ally units")]
        public Color selectionAllyColor = Color.green;
        [Tooltip("Color of selection circle for enemy units")]
        public Color selectionEnemyColor = Color.red;

        [Header("Cursor")]
        [Tooltip("Cursor that is used when selection location/unit")]
        public Texture2D modeCursor;

        [Header("VFX")]
        [Tooltip("TextMeshPro object used for floating texts")]
        public TextMeshPro floatingText;
        [Tooltip("Move command VFX")]
        public ParticleSystem moveVFX;
        [Tooltip("MoveAttack command VFX")]
        public ParticleSystem moveAttackVFX;
        [Tooltip("VFX for a stunned unit")]
        public Transform stunnnedVFX;
        [Tooltip("VFX for a muted unit")]
        public Transform mutedVFX;
        [Tooltip("VFX for a disarmed unit")]
        public Transform disarmedVFX;
        [Tooltip("Indicator for waypoints")]
        public Transform waypointVFX;

        [Header("Materials")]
        [Tooltip("Material for a building that is about to be constructed")]
        public Material shadowMaterial;
        [Tooltip("Material for a building that is about to be constructed at invalid position")]
        public Material shadowMaterialInvalid;
        [Tooltip("Invisibility material. Used by invisible units and for building to be constructed")]
        public Material invisibilityMat;

        [Header("Selectors")]
        public SpriteRenderer selectionRenderer;
        [Tooltip("For selection with radius from 0 to 0.45")]
        public Sprite selectionSmall;
        [Tooltip("For selection with radius from 0.45 to 1.25")]
        public Sprite selectionMedium;
        [Tooltip("For selection with radius from 1 and bigger")]
        public Sprite selectionLarge;

        public Transform rangeProjector;
        public Transform areaProjector;

        [Header("Projectiles")]
        [Tooltip("Default projectile that is used if projectile not defined")]
        public Projectile defaultProjectile;
        [Tooltip("Default VFX that is used if continuous VFX not defined")]
        public VFXLine defaultContinuousVFX;

        [Header("Prefabs")]
        [Tooltip("Prefab of an item that is dropped to the ground")]
        public Unit itemPrefab;
        [Tooltip("Healthbar prefab")]
        public Transform healthBar;
        [Tooltip("Healthbar prefab for enemies")]
        public Transform healthBarEnemy;
        [Tooltip("Префаб полоски маны. Ставится ребёнком полоски здоровья своим юнитам, у которых есть мана")]
        public Transform manaBar;
        [Tooltip("MiniMap icon prefab")]
        public Transform miniMapIcon;

        [Header("Abilities")]
        [Tooltip("Transport unit ability. Can be found in Resources/Ability/Transport")]
        public Ability takeInTransport;
        [Tooltip("Transport unit ability. Can be found in Resources/Ability/Transport")]
        public Ability takeOutTransport;

        [Header("Types")]
        public ArmorType standardArmorType;
        public DamageType standardDamageType;

        [Header("Sound")]
        public AudioClip buildingComplete;
        public AudioClip upgradeComplete;
        public AudioClip researchComplete;

        private Transform waypointSpawned;

        private void Awake() => Startup();

        /// <summary>
        /// Подъём службы (IStartupService). Идемпотентен: повторный вызов выходит сразу.
        /// </summary>
        public void Startup()
        {
            if (startupDone) return;
            startupDone = true;

            if (Instance == null)
            {
                Instance = this;
            }

            // Transport
            if (!takeInTransport || !takeOutTransport) Debug.LogWarning("Take In/Take Out transport abilities are not specified!");

            // Waypoint
            if (waypointVFX != null)
            {
                waypointSpawned = Instantiate(waypointVFX);
                waypointSpawned.gameObject.SetActive(false);
            }

            // State colors
            StateColors.InvertSet(ref StateColors.Affected, affected);
            StateColors.InvertSet(ref StateColors.Invalid, invalid);
            StateColors.InvertSet(ref StateColors.Invisibility, invisibility);
        }

        // Start is called before the first frame update
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {

        }

        // WAYPOINT --------------------------------------------------------------------------------------------------------------------------------------------------------------------
        public void ShowWaypoint(Vector2 position, Unit unit)
        {
            if (waypointSpawned == null && waypointVFX != null)
            {
                waypointSpawned = Instantiate(waypointVFX);
            }

            if (position != Vector2.zero)
            {
                waypointSpawned.position = new Vector3(position.x, Utils.GetTerrainHeight(position), position.y);
                waypointSpawned.gameObject.SetActive(true);
                return;
            }
            else if (unit != null)
            {
                waypointSpawned.parent = unit.transform;
                waypointSpawned.localPosition = new Vector3(0, unit.unitHeight + 0.1f, 0);
                waypointSpawned.gameObject.SetActive(true);
                return;
            }

            waypointSpawned.gameObject.SetActive(false);
        }

        public void HideWaypoint()
        {
            if (waypointSpawned) waypointSpawned.gameObject.SetActive(false);
        }
    }
}
