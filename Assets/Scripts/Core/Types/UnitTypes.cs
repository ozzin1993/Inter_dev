namespace StrategyCore
{
    public enum UnitType
    {
        Unit, // Units that are not buildings or Static Destructibles (Trees, Barrels)
        Building, // Buildings
        StaticDestructible, // Static Destructibles (Barrels)
        Tree, // Static Destructibles (Trees)
        Item // Items dropped to the ground (Trees, Barrels)
    }
}
