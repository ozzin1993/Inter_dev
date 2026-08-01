using System.Collections;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Партиал MatchManager: серверные «часы» таймера до следующей волны. Время считает ТОЛЬКО хост
    /// (WaveLoop идёт только на сервере, правило 6). WaveLoop фиксирует момент следующей волны
    /// (ScheduleNextWave); эти часы раз в секунду вычисляют остаток и раздают: хосту — локально
    /// (UIManager.ShowWaveTimer), клиентам — по сети (NetworkDataSync.WaveTimerSend → ClientRpc).
    /// Ядро ассета не трогаем (правило 1).
    /// </summary>
    public partial class MatchManager
    {
        float waveTimerNextTime;    // Time.time момента следующей волны (серверное время)
        bool waveTimerActive;       // момент назначен — можно отсчитывать
        bool waveTimerLoopStarted;  // защита от повторного запуска часов

        /// <summary>Зафиксировать момент следующей волны (зовётся из WaveLoop при установке ожидания).</summary>
        void ScheduleNextWave(float secondsUntil)
        {
            waveTimerNextTime = Time.time + Mathf.Max(0f, secondsUntil);
            waveTimerActive = true;
        }

        /// <summary>Запустить «часы» один раз (зовётся из WaveLoop на сервере).</summary>
        void StartWaveTimer()
        {
            if (waveTimerLoopStarted) return;
            waveTimerLoopStarted = true;
            StartCoroutine(WaveTimerLoop());
        }

        // Раз в секунду: остаток до волны → хосту локально + клиентам по сети. Гранулярность — секунда.
        IEnumerator WaveTimerLoop()
        {
            WaitForSeconds wait = new WaitForSeconds(1f);
            while (true)
            {
                if (waveTimerActive)
                {
                    int left = Mathf.Max(0, Mathf.CeilToInt(waveTimerNextTime - Time.time));
                    PushWaveTimer(left);
                }
                yield return wait;
            }
        }

        // Раздать значение: хост (с UI) — локально; клиенты — по сети (SendTo.NotServer у RPC).
        void PushWaveTimer(int seconds)
        {
            Presentation.UI?.ShowWaveTimer(seconds); // [Interflow 2026-08-01 ADR-005]
            if (NetworkDataSync.instance != null) NetworkDataSync.instance.WaveTimerSend(seconds);
        }
    }
}
