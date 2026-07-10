namespace StrategyCore
{
    public enum UnitStates
    {
        Idle, // Unit is standing, will respond to outside influence (Being attacked / Looking for targets)
        Hold, // Unit holds position, will not move, only attack if is in range
        Move, // Move to the point
        Follow, // Unit was commanded to follow, will follow the target until it is dead or not visible
        Attack, // Unit was commanded to attack, will follow and attack the target until it is dead or not visible
        AttackMove, // Unit will move to the point attacking enemies along the way
        AbilityCasting // If this unit is currently using an ability
    }

    public enum AnimationState
    {
        Reset,
        Idle,
        IdleReady,
        Walk,
        Building,
        Casting,
        ContinuousAttack
    }

}
