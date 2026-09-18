using System;
using UnityEngine;
using Unity.Netcode;

namespace StrategyCore
{
    // === ШКАЛА (GAUGE) — партиал по фиче (правило 22) ==
    // Общий ресурс юнита (как мана), копится от попаданий и убийств, тратится умением.
    // Конфигурация — в пассивке (блок 9), проверка — в умении (блок 20).
    // Здесь только поле, мутаторы с зажимом и очередь синка — как у ХП/маны/опыта.
    public partial class Unit
    {
        [Header("Gauge")]
        [Tooltip("Максимальная шкала юнита (0 — шкала не используется)")]
        public float maxGauge;
        [Tooltip("Текущее значение шкалы")]
        public float gauge;

        public Action OnGaugeChange;

        [HideInInspector] public bool gaugeSync = false;

        public void SetGauge(float value)
        {
            gauge = Mathf.Clamp(value, 0f, maxGauge);
            OnGaugeChange?.Invoke();

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && !gaugeSync)
            {
                NetworkDataSync.Instance.gaugeChangedUnits.Add(netID);
                gaugeSync = true;
                NetworkDataSync.Instance.onGaugeCleared += GaugeSyncFalse;
            }
        }

        public void SetMaxGauge(float value)
        {
            maxGauge = Mathf.Max(0f, value);
            if (gauge > maxGauge) gauge = maxGauge;
            OnGaugeChange?.Invoke();

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && !gaugeSync)
            {
                NetworkDataSync.Instance.gaugeChangedUnits.Add(netID);
                gaugeSync = true;
                NetworkDataSync.Instance.onGaugeCleared += GaugeSyncFalse;
            }
        }

        public void ChangeGauge(float delta)
        {
            SetGauge(gauge + delta);
        }

        public void GaugeSyncFalse()
        {
            gaugeSync = false;
            if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.onGaugeCleared -= GaugeSyncFalse;
        }
    }
}
