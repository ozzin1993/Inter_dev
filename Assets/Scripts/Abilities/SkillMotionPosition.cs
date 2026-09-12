using UnityEngine;

namespace StrategyCore
{
    // Stationary Strategy Core units use an obstacle and a disabled agent.
    // Move their transform during the action lock and publish through the native position sync.
    internal sealed class SkillMotionPosition
    {
        readonly Unit unit;
        readonly bool obstacleEnabled;
        public SkillMotionPosition(Unit target)
        {
            unit=target;
            obstacleEnabled=unit.obstacle&&unit.obstacle.enabled;
            if(unit.obstacle)unit.obstacle.enabled=false;
            var sync=NetworkDataSync.instance;
            if(sync){if(!sync.positionSyncList.Contains(unit.netID))sync.positionSyncList.Add(unit.netID);sync.removeSyncList.Remove(unit.netID);unit.removeFromPosSync=false;unit.positionsSent=false;}
        }
        public bool Move(Vector3 point)
        {
            if(!unit||unit.dead)return false;
            if(unit.agent&&unit.agent.enabled&&unit.agent.isOnNavMesh)return unit.agent.Warp(point);
            unit.transform.position=point;return true;
        }
        public void Finish()
        {
            if(!unit||unit.dead)return;
            if(unit.obstacle)unit.obstacle.enabled=obstacleEnabled;
            Grid.AssignToChunk(unit);
            if(FogOfWar.instance)FogOfWar.instance.CellAssignment(unit);
            var sync=NetworkDataSync.instance;
            if(sync){unit.positionsSent=false;unit.removeFromPosSync=true;if(!sync.removeSyncList.Contains(unit.netID))sync.removeSyncList.Add(unit.netID);}
        }
    }
}
