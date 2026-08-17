using StrategyCore;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Plays/Pauses sound depending on camera visibility

    public class SoundPauser : MonoBehaviour
    {
        [HideInInspector] public AudioSource audioSource;
        [HideInInspector] public bool checkPosition = true;

        // Start is called before the first frame update
        void Start()
        {
            audioSource = GetComponent<AudioSource>();

            if (checkPosition)
            {
                GameManager.instance.Tick += VisibilityUpdate;
            }
        }

        private void OnDestroy()
        {
            GameManager.instance.Tick -= VisibilityUpdate;
        }

        void VisibilityUpdate()
        {
            Vector3 screenPoint = Utils.MainCamera.WorldToViewportPoint(transform.position);
            bool isInView = screenPoint.z > 0 && screenPoint.x > 0 && screenPoint.x < 1 && screenPoint.y > 0 && screenPoint.y < 1;

            if (isInView)
            {
                bool FoWVisible = FogOfWar.instance.IsVisible(transform.position, SlotManager.instance.currentPlayer);

                if  (FoWVisible)
                {
                    if (!audioSource.isPlaying) audioSource.Play();  // Play if in view and FoW visible
                }
                else
                {
                    if (audioSource.isPlaying) audioSource.Stop();  // Stop if not FoW visible
                }
            }
            else
            {
                if (audioSource.isPlaying) audioSource.Stop();  // Stop if not FoW visible
            }
        }
    }
}
