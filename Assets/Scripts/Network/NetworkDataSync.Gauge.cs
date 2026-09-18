using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System;

namespace StrategyCore
{
    /// <summary>
    /// Партиал NetworkDataSync: канал ШКАЛЫ юнита (gauge).
    ///
    /// Ровно тем же устройством, что здоровье, мана, опыт и характеристики (правило 1, правило 5):
    /// накопительный список номеров, флаг «я уже в очереди» на юните, событие сброса после отправки,
    /// отправка раз в период (и внеочередная по ForceSync). Два поля — gauge и maxGauge.
    ///
    /// Правило 6 соблюдено: клиент ничего не вычисляет — сервер шлёт готовое, клиент кладёт в поле.
    /// Новый partial-файл (правило 22): NetworkDataSync.cs не раздувается.
    /// </summary>
    public partial class NetworkDataSync
    {
        public List<UInt16> gaugeChangedUnits = new List<UInt16>();

        private void GaugeChangeSend()
        {
            if (gaugeChangedUnits.Count == 0) return;

            for (int i = gaugeChangedUnits.Count - 1; i >= 0; i--)
                if (!SlotManager.Instance.unitNetID.ContainsKey(gaugeChangedUnits[i])) gaugeChangedUnits.RemoveAt(i);

            if (gaugeChangedUnits.Count > 0)
            {
                float[] gaugeValues = new float[gaugeChangedUnits.Count];
                float[] maxGaugeValues = new float[gaugeChangedUnits.Count];

                for (int i = 0; i < gaugeChangedUnits.Count; i++)
                {
                    if (SlotManager.Instance.unitNetID.TryGetValue(gaugeChangedUnits[i], out Unit unit))
                    {
                        gaugeValues[i] = unit.gauge;
                        maxGaugeValues[i] = unit.maxGauge;
                    }
                }

                GaugeChangeClientRpc(gaugeChangedUnits.ToArray(), gaugeValues, maxGaugeValues);
            }

            gaugeChangedUnits.Clear();
            onGaugeCleared?.Invoke();
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void GaugeChangeClientRpc(UInt16[] unitID, float[] unitGauge, float[] unitMaxGauge)
        {
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            for (int i = 0; i < unitID.Length; i++)
            {
                if (SlotManager.Instance.unitNetID.TryGetValue(unitID[i], out Unit unit))
                {
                    unit.maxGauge = unitMaxGauge[i];
                    unit.gauge = unitGauge[i];
                    unit.OnGaugeChange?.Invoke();
                }
                else
                {
                    Debug.LogError("Desync! Unit netID:" + unitID[i] + " should exist on client, but does not! (GaugeChangeSend NetworkDataSync)");
                }
            }
        }
    }
}
