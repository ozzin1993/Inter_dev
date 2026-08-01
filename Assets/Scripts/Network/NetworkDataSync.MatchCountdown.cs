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
            // [Interflow 2026-08-01 ADR-005] Вёрстка отсчёта перенесена в клиентский мост (Presentation.MenuUI).
            Presentation.MenuUI?.ShowMatchCountdown(seconds);
        }
    }
}
