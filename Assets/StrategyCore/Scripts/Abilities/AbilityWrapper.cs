namespace StrategyCore
{
    // Attribute wrapper is a simple wrapper for holding Attribute:Value pair

    [System.Serializable]
    public class AbilityWrapper
    {
        public Ability ability;
        public int level;
        public bool locked;
    }
}
