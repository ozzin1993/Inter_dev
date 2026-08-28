using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    public class FloatingText : MonoBehaviour
    {
        public float floatSpeed = 1;
        public static float destroyTime = 1;

        void Update()
        {
            Vector3 directionToCamera = Utils.cachedMainCamera.transform.position - transform.position;
            directionToCamera.x = 0;
            transform.rotation = Quaternion.LookRotation(-directionToCamera, Vector3.up);

            //transform.rotation = Quaternion.LookRotation(transform.position - Utils.cachedMainCamera.transform.position);
            transform.position += Vector3.up * floatSpeed * Time.deltaTime; // Float upward
        }

        /// <summary>
        /// Spawns floating text at given position, checks the visibility.
        /// </summary>
        /// <param name="player">Which player to show the floating text. -1 means to everyone.</param>
        /// <param name="position">Position of the text.</param>
        /// <param name="text">Text.</param>
        /// <param name="color">Color of the text.</param>
        /// <param name="sync">Should server sync with clients.</param>
        public static void Spawn(int player, Vector3 position, string text, Color color, bool sync = true)
        {
            // Certain events in game happen only on the server, but we still want them to be shown in local players
            if (sync && NetworkManager.Singleton.IsServer)
            {
                NetworkDataSync.Instance.FloatingTextSend(player, position, text, color);
            }

            // [Interflow fix 2026-06-20] На headless-сервере камеры/FoW нет: после релая клиентам выходим (локальный текст не создаём).
            if (ServerBootstrap.IsHeadlessServer) return;

            // Do noy play audio if not in the camera view
            if (!Utils.IsInView(position)) return;
            // Do not play if FoW not visible
            if (!FogOfWar.Instance.IsVisible(position, SlotManager.Instance.currentPlayer)) return;

            TextMeshPro textMesh = Instantiate(ReferenceManager.Instance.floatingText, position, Quaternion.identity);
            textMesh.text = text;
            textMesh.color = color;
            Destroy(textMesh.gameObject, destroyTime);
        }
    }
}
