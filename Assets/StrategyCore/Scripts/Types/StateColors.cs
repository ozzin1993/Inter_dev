using UnityEngine;

namespace StrategyCore
{
    // All colors must be inverted, that is because by default in editor material colors are set to black and we can not see the unit colors
    // In shader we invert the material block color
    public struct StateColors
    {
        [Tooltip("Default state color")]
        public static Color Default;
        [Tooltip("When placing a building if invalid")]
        public static Color Invalid;
        [Tooltip("If units are affected by area ability")]
        public static Color Affected;
        [Tooltip("Overlay color when unit is invisible")]
        public static Color Invisibility;

        /// <summary>
        /// Inverts and sets the state color.
        /// </summary>
        /// <param name="stateColor">Reference to state color.</param>
        /// <param name="desiredColor">Desired color.</param>
        public static void InvertSet(ref Color stateColor, Color desiredColor)
        {
            stateColor = new Color(1.0f - desiredColor.r, 1.0f - desiredColor.g, 1.0f - desiredColor.b, desiredColor.a);
        }

        // // Default
        // public static Color Default()
        // {
        //     Color col = new Color();
        //     col.
        //     return new Color(0, 0, 0, 0); // White. It is inverted
        // }
        // 
        // // When placing a building if invalid
        // public static Color Invalid()
        // {
        //     return new Color(0.161f, 0.663f, 0.663f, 1); // Red. It is inverted
        // }
        // 
        // // If units are affected by area ability
        // public static Color AreaAffected()
        // {
        //     return new Color(0.149f, 0.31f, 0.847f, 1); // Blue. It is inverted
        // }
        // 
        // // Invisibility ability uses it
        // public static Color Invisible()
        // {
        //     return new Color(0.9f, 0.9f, 0.9f, 1); //new Color(0.5f, 0.5f, 0.5f, 0.65f); // Grey. It is inverted
        // }
    }
}
