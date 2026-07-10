using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    /// <summary>
    /// Наше partial-расширение NetworkDataSync (ядро ассета не правится, кроме слова 'partial').
    /// Видимый обратный отсчёт в лобби перед стартом матча: сервер шлёт оставшиеся секунды,
    /// клиенты показывают строку «Старт через N…». seconds &lt;= 0 — спрятать.
    /// Образец рассылки — NetworkDataSync.WaveComposition.cs.
    /// </summary>
    public partial class NetworkDataSync
    {
        // Сервер → пиры: показать/обновить отсчёт (seconds &gt; 0) или спрятать (seconds &lt;= 0).
        public void MatchCountdownSend(int seconds)
        {
            if (!IsSpawned) return; // нет активной сети (локальный тест) — клиентов нет
            MatchCountdownClientRpc(seconds);
        }

        // Показ — на реальных клиентах (отсчёт идёт только при авто-старте выделенного сервера; UI у сервера нет).
        [Rpc(SendTo.NotServer)]
        private void MatchCountdownClientRpc(int seconds)
        {
            // На выделенном сервере меню-UI нет (UIDocument строится в Start, на сервере пропущен) — выходим.
            UIManagerMenu menu = UIManagerMenu.instance;
            if (menu == null || menu.UIDocument == null || menu.UIDocument.rootVisualElement == null) return;

            VisualElement lobby = menu.UIDocument.rootVisualElement.Q("Lobby");
            if (lobby == null) return;

            // Строка отсчёта создаётся один раз и переиспользуется.
            Label label = lobby.Q<Label>("MatchCountdown");
            if (label == null)
            {
                label = new Label { name = "MatchCountdown" };
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.fontSize = 20;
                label.style.color = Color.white;
                lobby.Add(label);
            }

            if (seconds > 0)
            {
                label.text = $"Старт через {seconds}…";
                label.style.display = DisplayStyle.Flex;
            }
            else
            {
                label.style.display = DisplayStyle.None;
            }
        }
    }
}
