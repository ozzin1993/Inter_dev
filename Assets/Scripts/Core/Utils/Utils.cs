using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    public static partial class Utils // [Interflow fix 2026-08-01 partial-split] класс разрезан на partial-файлы (задача №11)
    {
        // [Interflow 2026-08-01 ADR-005] Дальность курсорного луча — перенесено из PlayerControl.rayDistance (клиентский класс недоступен отсюда).
        public const float cursorRayDistance = 300f;

        // [Interflow 2026-08-01 server-opt] Признак выделенного сервера без рендера (batchmode-запуск).
        // Гейт чисто визуальной работы в сим-коде: бары, иконки, VFX, FoW-текстуры, скелетная анимация.
        public static bool Headless => Application.isBatchMode;

        // Might want to adjust based on your levels
        public static int maxVisionRange = 15; // Maximum FoW vision range unit can have
        public static float airUnitElevation = 5f; // How high from the grouond air units must be

        // Defaults
        public static float agentTypeRadius; // Set by GameManager.cs based on navigation agent type radius
        public static float stopDistanceOffset = 0.1f; // Stop distance should be slightly bigger than the radius of two units.
        public static float raycastPointY = 10f; // We do raycasts from this point down, no playable area should be above this point. We do ceratin raycasts from below the ground and go up. This is point below the ground. No playabale area should be below this point.
        public static float maxSlope = 0.5f; // If difference between points more than this value it will be considered non-walkable
        public static int maxCircleChecks = 6; // When dropping an item or training a unit we check for empty place around the cast unit, maximum check distance is defined here.

        public static float minimapPingDuration = 3f; // Duration of ping on the minimap

        public static float coneAngle = 45f; // The half-angle of the cone in degrees for cone-shaped unit return

        public static float levelHeightOffset = 0.1f; // Difference between different elevation levels. Example, if 1 then ground at the height of 1 up to 2 will be second level
                                                      // For positive level height we subtract it, for negative we add it. For example point at 1m, levelHeight height at 1m, we consider the point to be second level since 1m - levelHeightOffset is lower than the point at the map.

        public static float searchRadius = 15; // Radius that will be used to search for storages, collectibles automatically. For collectible multiplier is 0.65.

        // When attacking or casting an ability,
        // if a unit already starts the action we allow it to finish unless the distance is more than = N * this Multiplier
        public static float activeDistanceMultiplier = 1.5f; 

        // Projeectors
        public static float largeSelectorSize = 1.25f; // Any selector bigger than this size will be LargeSelector
        public static float mediumSelectorSize = 0.45f; // Any selector bigger than this size will be MediumSelector

        // Set by GameManager
        public static float airOffsetX; // Set by gameManager in the beginning of the game, offset of air navmesh surface in X axis
        public static float invisibilityOffsetY; // Set by gameManager in the beginning of the game, offset of invisibility navmesh surface in Y axis

        public static int terrainMaskVisuals; // Terrain mask including visual terrain data. Set in Awake of FoW
        public static int terrainMask; // Terrain mask for raycasts. Set in Awake of FoW
        public static int groundMask; // Ground mask for raycasts. Set in Awake of FoW
        public static int waterMask; // WAter mask for raycasts. Set in Awake of FoW

        // For grid pathfinding - Not used in this asset
        public static float pathPointDistance = 0.05f * 0.05f; // We decide if we have reached the pathPoint by checking Squared distance and direction.sqrMagnitude

        // Cache the camera for performance reasons
        public static Camera cachedMainCamera;
        // Ленивый доступ к главной камере: Camera.main — дорогой поиск по тегу, кеш заполняется при первом обращении
        public static Camera MainCamera
        {
            get
            {
                if (cachedMainCamera == null) cachedMainCamera = Camera.main;
                return cachedMainCamera;
            }
        }
        // Кеш маски слоя Default — вместо LayerMask.GetMask в горячих путях/циклах
        public static readonly int defaultMask = LayerMask.GetMask("Default");

    }
}

