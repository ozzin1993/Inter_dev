using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Camera_TopDownNS;
using System.Collections;

namespace StrategyCore
{
    public partial class PlayerControl : MonoBehaviour // [Interflow fix 2026-08-01 partial-split] класс разрезан на partial-файлы (задача №11)
    {
        public static PlayerControl instance;
        public static StrategyCoreInput coreInput;

        // Initial technical
        UIManager UImanager;
        public static bool isCursorOverUI;

        [HideInInspector] public PCMode mode = PCMode.Default;
        private ParticleSystem moveVFX;
        private ParticleSystem moveAttackVFX;

        // Selection
        [HideInInspector] public Unit activeUnit;
        [HideInInspector] public List<Unit> selectedUnits = new List<Unit>();

        // Selected units - hotkey (CTRL + number)
        List<Unit>[] quickSelection = new List<Unit>[10];

        bool dragSelect = false;
        Vector2 mousePosition1; // Drag selection initial mouse position
        Rect selectionRect; // Rectangle of drag selection

        float currentTime = 0; // For performance gain when dragSelecting, do selection only specified times per second

        bool dblClickWasPerformedThisFrame = false;
        [HideInInspector] public Unit dblClickUnit = null;

        // Mode
        SpriteRenderer unitProjectorSpawned; // For displaying unit at cursor
        int unitProjectorSize; // To properly display the selection texture
        bool unitProjectorColor; // To properly color the projector

        Transform areaProjectorSpawned; // For displaying area ability
        Transform rangeProjectorSpawned; // For displaying the range of the unit

        Ability activeAbility;
        int activeAbilityIndex; // Ability that is in active phase.
        bool activeIsItem = false; // if active ability is item

        // ShadowBuilding
        [HideInInspector] public Transform shadowBuilding; // When placing a building visual version of it will be stored here
        List<Renderer> shadowBuildingRenderers; // We apply material through here
        float shadowBuildingRadius;
        int shadowBuildingMask;
        bool shadowBuildingIsAir;

        // Constant
        public const float rayDistance = 300; // Distance for raycast for unit selection
        const float groupSelectionMagnitutde = 40; // if pointer moves for more than this value then we are making a group selection

    }
}
