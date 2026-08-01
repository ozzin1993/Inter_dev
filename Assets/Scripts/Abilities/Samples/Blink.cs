using StrategyCore;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    [CreateAssetMenu(fileName = "Blink", menuName = "StrategyCore/Abilities/Blink")]
    public class Blink : Ability
    {
        // Teleports the casting unit to specified location

        public override AbilityType type { get { return AbilityType.Location; } } // Specify type

        [Header("Ability specific")]
        public float[] range;

        public override void Use(Unit castingUnit, int castingPlayer, int level, Vector3 position)
        {
            // We do not use the ability on clients
            if (NetworkConnectionHandler.isClient) return;

            // Find valid position - that is on navmesh and less than range
            bool navmeshCast = false;
            int navmeshMask = 0;
            int terrainMask = 0;

            if (!castingUnit.isAir)
            {
                if (castingUnit.isGround && castingUnit.isWater)
                {
                    navmeshCast = true;
                    navmeshMask = (1 << 0) | (1 << 3);
                    terrainMask = Utils.terrainMask;
                }
                else if (castingUnit.isGround)
                {
                    navmeshCast = true;
                    navmeshMask = 1 << 0;
                    terrainMask = Utils.groundMask;
                }
                else if (!castingUnit.isWater)
                {
                    navmeshCast = true;
                    navmeshMask = 1 << 3;
                    terrainMask = Utils.waterMask;
                }
            }

            if (navmeshCast)
            {
                float currentRange = range[level];
                NavMeshHit hit;
                Vector3 distancedPosition = Utils.TerrainRaycastByPosition(Utils.PointTowards(new Vector2(castingUnit.transform.position.x, castingUnit.transform.position.z), new Vector2(position.x, position.z), currentRange), terrainMask);

                float currentDistance = Mathf.Infinity;

                while (currentDistance > currentRange)
                {
                    if (currentRange >= 0 && NavMesh.SamplePosition(distancedPosition, out hit, 50f, navmeshMask))
                    {
                        currentDistance = Vector2.Distance(new Vector2(castingUnit.transform.position.x, castingUnit.transform.position.z), new Vector2(hit.position.x, hit.position.z));
                        if (currentDistance <= range[level])
                        {
                            // Position found
                            position = hit.position;
                            break;
                        }
                        else
                        {
                            // Try to find closer position
                            currentRange -= 0.25f;
                            distancedPosition = Utils.TerrainRaycastByPosition(Utils.PointTowards(new Vector2(castingUnit.transform.position.x, castingUnit.transform.position.z), new Vector2(position.x, position.z), currentRange), terrainMask);
                        }
                    }
                    else
                    {
                        Presentation.NotifyMsg("Can`t blink there!", castingUnit.owner, true);
                        return;
                    }
                }
            }

            // Rotate
            castingUnit.LookAtInstant(position);

            // Position
            if (navmeshCast)
            {
                castingUnit.transform.position = position;
            }
            else
            {
                castingUnit.transform.position = Vector3.MoveTowards(
                    castingUnit.transform.position,
                    position,
                    range[level]
                );
            }

            // Update the grid data
            Grid.AssignToChunk(castingUnit);
            FogOfWar.instance.CellAssignment(castingUnit);

            // Position network sync
            if (NetworkManager.Singleton.IsServer) NetworkDataSync.instance.SetPositionDirect(castingUnit);
        }
    }
}
