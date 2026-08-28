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
                GameManager.Instance.Tick += VisibilityUpdate;
            }
        }

        private void OnDestroy()
        {
            GameManager.Instance.Tick -= VisibilityUpdate;
        }

        void VisibilityUpdate()
        {
            Vector3 screenPoint = Utils.MainCamera.WorldToViewportPoint(transform.position);
            bool isInView = screenPoint.z > 0 && screenPoint.x > 0 && screenPoint.x < 1 && screenPoint.y > 0 && screenPoint.y < 1;

            if (isInView)
            {
                bool FoWVisible = FogOfWar.Instance.IsVisible(transform.position, SlotManager.Instance.currentPlayer);

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
