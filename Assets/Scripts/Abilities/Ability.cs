using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Ability class is a base class for all items, skills, spells, upgrades, constructions, unit training abilities of a unit class
    public enum AbilityType
    {
        Container, // 0 Container - this ability holds other abilities inside of it. Can be used for categorizing
        Process,   // 1 Process - this ability used for any ability that requires some time to process. For example: Unit training. Upgrades(TechTree) have their own category.
        Area,      // 2 Area - this skill is used to affect a chosen area
        Location,  // 3 Location - this skill is used at a chosen location
        Unit,      // 4 Unit - this skill can target only unit class (buildings, trees, units, objects)
        Active,    // 5 Active - this skill is activated immediately when used
        Toggle,    // 6 Toggle - this skill can be used for turning the ability on and off
        Aura,      // 7 Aura - this skill can be used for aura effects. Its Use function called every frame
        Passive,    // 8 Passive - this skill can be used for addition of constant parameters to the unit. Attack, armor, strength etc.
        Construction, // 9 Construction - Building construction, BuildingConstruction.cs must be part of the unit for additional parameters setup.
        Null // just a null type placeholder
    }

    public abstract class Ability : ScriptableObject
    {
        [AbilityID]
        public int id;

        [Tooltip("What kind of ability is this. Read more on Ability.cs or documentation")]
        public virtual AbilityType type { get { return AbilityType.Null; } } // Must be specified by subclass

        [Header("Text")]
        [Tooltip("Name of the ability. If set only for 1 level, it will be used for all levels.")]
        public string[] abilityName;
        [Tooltip("Description of the ability. If set only for 1 level, it will be used for all levels.")]
        public string[] description;
        [Tooltip("Icon of the ability. If set only for 1 level, it will be used for all levels.")]
        public Texture2D[] icon;

        [Header("Technical")]
        [Tooltip("Which slot is occupied by this ability in AbilityView. -1 for sequential display")]
        public int slotNumber = -1;

        [Header("Item")]
        [Tooltip("Is this an item or ability. Items do not have Lock state, Leveling and if placed in Abilities of the unit it will be an item for sale")]
        public bool isItem;
        [Tooltip("Should this item immediately be used when picked up by the unit")]
        public bool useUponPickUp;
        [Tooltip("Does this item drop when carrier dies")]
        public bool dropOnDeath;
        [Tooltip("If item should have charges you can set it here. Any value >0 will indicate that the item has charges. After using specified amount of times, the item will disappear")]
        public int charges;

        [Header("Levels")]
        [Tooltip("Maximum level of the ability. Can be set to 0, no level information will be displayed. Value of 1 indicates that it has only 1 level, value of 3 - 3 levels of the ability.")]
        public int maxLevels;
        [Tooltip("If this ability is leveled through unit leveling. When unit levels up he gets a point that can be spent on a learning new ability or upgrading the existing one. If this parameter is true it will be the only way for this ability to level up.")]
        public bool heroLevelable = false;

        [Header("Parameters")]
        [Tooltip("After casting ability needs cooldown to be usable again. Leave 0 if not needed.")]
        public float[] cooldown;
        [Tooltip("For target abilities, the caster if cast range is set will need to be approach the target to the specified distance.")]
        public float[] castRange;
        [Tooltip("Amount of time needed to cast this skill. If the unit is interrupted while casting, the cast will begin again, no mana will be reducted until successfull cast. For Processes it is amount of time needed to process.")]
        public float[] castTime;
        [Tooltip("Some abilities such as of Area type require this parameter, it will determine the radius of the Area of Effect")]
        public float[] radius;
        [Tooltip("Показывать круг радиуса постоянно, пока умение есть у юнита (для аур). " +
                 "Работает только при ненулевом радиусе. Круг видят все игроки, в тумане войны он гаснет вместе с юнитом.")]
        public bool showRadiusCircle;
        [Tooltip("What kind of units are affected by this ability. For target abilities. Unit target ability will not allow to target non-eligible unit, Area abilities will check the eligibility in their Use() function, but while choosing the area it will display the eligibility of units.")]
        public UnitSelector unitSelector;
        [Tooltip("If location or unit ability should the casting unit first turn in the direction of target to cast the ability")]
        public bool dontTurn;

        [Space(10)]
        [Tooltip("When this ability is continuous it will stay active until the duration ends or the caster no longer has enough mana to use. For Area, Location and Unit, Active type abilities.")]
        public bool continuous;
        [Tooltip("Does this continuous ability need the caster to stay in place to actively cast it. When false the caster can be interrupted and the ability will still be active.")]
        public bool interruptible;
        [Tooltip("When this ability is not interruptible we must define if it needs casting unit to actively cast it.")]
        public bool requiresCastingUnit = true;
        [Tooltip("If this ability is continuous it might have a duration. You can specify it here.")]
        public float[] duration;

        [Header("Requirements")]
        [Tooltip("Required tech for each level of the ability. Can be left blank.")]
        public MultiLevel<Technology>[] requiredTech;
        [Tooltip("Required level of the unit for each level of the ability. Can be left blank.")]
        public int[] requiredLevel;

        [Space(10)]
        [Tooltip("How much mana this ability needs to be cast. Can leave at 0 if no mana required.")]
        public float[] manaCost;
        [Tooltip("If this ability is toggle or continuous, you can charge the mana during the active stage of the ability.")]
        public float[] manaCostPerSecond;
        [Tooltip("How many resources are required to cast this ability; Purchasing cost for the items. Can be left blank if no cost is required. For UnitTraining and Construction the cost will be determined by resourceCost of the unit, if it exists. If it does not exist this cost will be used.")]
        public MultiLevel<ResourceWrapper>[] cost;

        [Header("Фракции")]
        [Tooltip("Ручная метка принадлежности умения к фракциям — ТОЛЬКО для поиска и фильтра в редакторе " +
                 "умений, на игру не влияет. Пусто — фракции считаются автоматически по ссылкам (стартовые " +
                 "умения главного здания, узлы дерева технологий, умения юнитов фракции). Список заполнен — " +
                 "он перекрывает автоподсчёт.")]
        public List<FactionConfig> editorFactions = new List<FactionConfig>();

        // Technical
        public void OnEnable()
        {
            if (abilityName == null || abilityName.Length == 0) abilityName = new string[0];
            if (description == null || description.Length == 0) description = new string[0];
            if (icon == null || icon.Length == 0) icon = new Texture2D[0];

            // Do some checks
            // [Interflow fix 2026-08-04 onenable-guard] Было `abilityName == null || X.Length == 0`.
            // Тремя строками выше abilityName гарантированно НЕ null, поэтому первая часть всегда false,
            // а вторая падала NullReferenceException на несозданном массиве: ЛЮБОЙ
            // `ScriptableObject.CreateInstance` наследника Ability бросал NRE — включая штатную кнопку
            // «Создать умение». Исключение не пробрасывается вызывающему (Unity ловит его на своём диспетчере),
            // поэтому баг годами жил молча.
            // Правка была зарегистрирована 2026-07-09, но на диске отсутствовала — восстановлена
            // по прямому решению Artsiom 2026-08-04.
            // Поведение загруженных с диска ассетов НЕ меняется: у них эти массивы не null.
            if (cooldown == null || cooldown.Length == 0) cooldown = new float[1] { 0 };
            if (castRange == null || castRange.Length == 0) castRange = new float[1] { 0 };
            if (castTime == null || castTime.Length == 0) castTime = new float[1] { 0 };
            if (radius == null || radius.Length == 0) radius = new float[1] { 0 };

            if (requiredTech == null) requiredTech = new MultiLevel<Technology>[0];
            if (requiredLevel == null) requiredLevel = new int[0];
        }

        public virtual void Init() // Called in the awake method of GameManager. Awake() of the scriptableObject is broken. Used for initializing all the necessary parameters for each ability. Override it with a base call - base.Init()
        {
            if (abilityName == null || abilityName.Length == 0) abilityName = new string[1] { "" };
            if (description == null || description.Length == 0) description = new string[1] { "" };
            if (icon == null || icon.Length == 0) icon = new Texture2D[1] { ReferenceManager.Instance.missingTexture };

            // Do some checks
            if (cooldown.Length == 0) cooldown = new float[1] { 0 };
            if (castRange.Length == 0) castRange = new float[1] { 0 };
            if (castTime.Length == 0) castTime = new float[1] { 0 };
            if (radius.Length == 0) radius = new float[1] { 0 };

            if (requiredTech == null) requiredTech = new MultiLevel<Technology>[0];
            if (requiredLevel == null) requiredLevel = new int[0];
        }

        // Before actually using the ability, we check if the ability`s custom requirements are met. For standard abilities check is done before Use(), for toggle and continuous before Activate().
        public virtual bool Check(Unit castingUnit, int castingPlayer, int level) { return true; }
        public virtual bool Check(Unit castingUnit, int castingPlayer, int level, Unit unit) { return true; }
        public virtual bool Check(Unit castingUnit, int castingPlayer, int level, Vector3 location) { return true; }

        // Abilities should have following methods
        // Use() - This method is called when the unit casts an ability. For Process type abilities, this is triggered when the process completes (e.g., Training spawns units, Research unlocks technology)
        public virtual void Use(Unit castingUnit, int castingPlayer, int level) { }
        public virtual void Use(Unit castingUnit, int castingPlayer, int level, Unit unit) { }
        public virtual void Use(Unit castingUnit, int castingPlayer, int level, Vector3 location) { }

        // vfxStorage - is a vfx that is spawned by the continuous ability
        public virtual void Use(Unit castingUnit, int castingPlayer, int level, ref VFXReferencer vfxStorage) { }
        public virtual void Use(Unit castingUnit, int castingPlayer, int level, Unit unit, ref VFXReferencer vfxStorage) { }
        public virtual void Use(Unit castingUnit, int castingPlayer, int level, Vector3 location, ref VFXReferencer vfxStorage) { }

        // Lock() - This method is called when the ability gets locked, if it has any passive effects it should remove them in this method. Items call the Lock() method when they are dropped.
        // Unlock() - This method is called when the ability gets unlocked, if it has any passive effects it should add them in this method. Items use this method when they are picked up.

        public virtual void Lock(Unit castingUnit, int castingPlayer, int level) { }
        public virtual void Unlock(Unit castingUnit, int castingPlayer, int level) { }

        // Activate/Deactivate is called if the skill is continuous or toggle, used to initialize or end the ability (Spawn and Removal of VFX)
        // vfxStorage - is a vfx that is spawned by continuous ability

        // The Activate method is called when the Continuous ability starts to be cast or when Toggle type ability gets turned on.
        public virtual void Activate(Unit castingUnit, int castingPlayer, int level) { }
        public virtual void Activate(Unit castingUnit, int castingPlayer, int level, ref VFXReferencer vfxStorage) { }
        public virtual void Activate(Unit castingUnit, int castingPlayer, int level, Unit unit, ref VFXReferencer vfxStorage) { }
        public virtual void Activate(Unit castingUnit, int castingPlayer, int level, Vector3 location, ref VFXReferencer vfxStorage) { }

        // The Deactivate is called when the Continuous ability ends to be cast or when Toggle type ability gets turned off.
        public virtual void Deactivate(Unit castingUnit, int castingPlayer, int level) { }
        public virtual void Deactivate(Unit castingUnit, int castingPlayer, int level, ref VFXReferencer vfxStorage) { }
        public virtual void Deactivate(Unit castingUnit, int castingPlayer, int level, Unit unit, ref VFXReferencer vfxStorage) { }
        public virtual void Deactivate(Unit castingUnit, int castingPlayer, int level, Vector3 location, ref VFXReferencer vfxStorage) { }
    }

    // Class for setting multi level requirements and costs
    [System.Serializable]
    public class MultiLevel<T>
    {
        public T[] data;
    }
}
