using UnityEngine.UIElements;

namespace StrategyCore
{
    public class MiniMapPinger
    {
        public VisualElement pingElement;
        public float currentTime;
        public int owner;
        public float opacityMultiplier = 1;

        public MiniMapPinger(VisualElement pingElement, int owner)
        {
            this.pingElement = pingElement;
            this.owner = owner;
        }
    }
}
