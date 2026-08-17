using Unity.Netcode;

namespace StrategyCore
{
    /// <summary>
    /// Наше partial-расширение NetworkDataSync (ядро ассета не правится, кроме слова 'partial').
    /// Синхронизация таймера до следующей волны: сервер (хост) считает остаток и шлёт клиентам
    /// секунды, клиенты показывают строку сверху по центру. Образец — NetworkDataSync.MatchCountdown.cs.
    /// Хосту RPC не приходит (он сервер) — у хоста строку обновляет MatchManager.WaveTimer.cs локально.
    /// </summary>
    public partial class NetworkDataSync
    {
        // Сервер → клиенты: обновить отсчёт до волны (seconds >= 0) или спрятать (seconds < 0).
        public void WaveTimerSend(int seconds)
        {
            if (!IsSpawned) return;   // нет активной сети (локальный тест) — клиентов нет
            WaveTimerClientRpc(seconds);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void WaveTimerClientRpc(int seconds)
        {
            Presentation.UI?.ShowWaveTimer(seconds);   // [Interflow 2026-08-01 ADR-005] через хаб
        }
    }
}
