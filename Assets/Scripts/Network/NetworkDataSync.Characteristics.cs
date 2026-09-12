using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System;

namespace StrategyCore
{
    /// <summary>
    /// Партиал NetworkDataSync: канал ХАРАКТЕРИСТИК юнита (дефект F07/F08/F11, блок 2026-09-09).
    ///
    /// Зачем. Состояния (эффекторы) меняют статы юнита ТОЛЬКО на сервере: наложение считает
    /// <c>UnitReceiver.Statuses</c>, снятие — <c>Effector.EffectorUpdate</c> и <c>EffectorRemove</c>.
    /// Клиенту об этом не сообщал никто: канала характеристик в проекте не было вовсе. При этом
    /// клиент по своим полям СЧИТАЕТ боевую картину — откат атаки берёт из <c>attackSpeed</c>
    /// (Unit.State.cs), заполнение полоски здоровья — из <c>maxHealth</c> (HealthBar.cs). Замедленный
    /// сервером юнит бил у клиента с прежней скоростью, а юнит под аурой на максимум здоровья
    /// показывал неверно заполненную полоску.
    ///
    /// Как. Ровно тем же устройством, что здоровье, мана и опыт (правило 1, правило 5): накопительный
    /// список номеров, флаг «я уже в очереди» на юните, событие сброса после отправки, отправка раз
    /// в период (и внеочередная по <c>ForceSync</c> после применения умения). Именно очередь и гасит
    /// частоту источника: аура перевешивает состояние каждые 0,1 с, но в канал юнит попадает ОДИН раз
    /// за период, каким бы способом стат ни менялся.
    ///
    /// Правило 6 соблюдено буквально: клиент ничего не вычисляет — сервер шлёт готовое значение,
    /// клиент кладёт его в поле. Гейт шкалы статусов («есть значок или зрелище») этот канал не трогает:
    /// он про то, что показать в шкале, а не про то, чем юнит бьёт.
    ///
    /// Объём — два поля, от которых клиент СТРОИТ картину сам. Урон, броня и дальность в этот канал
    /// не входят: их считает сервер, а у клиента они только печатаются в панели юнита.
    ///
    /// Отсев и приёмник написаны как у семьи ХП/МП/опыта (проверка по реестру + Debug.LogError
    /// «Desync!»), а не через помощники канала статусов ServerCanSend/StillRegistered/TryResolveUnit
    /// (NetworkDataSync.UnitStatus.cs): у тех другое поведение по мёртвым — они молчат, а накопительные
    /// каналы обязаны кричать. Выбор осознанный, отмечен здесь, чтобы не читался как недосмотр.
    ///
    /// Новый partial-файл (правило 22): NetworkDataSync.cs не раздувается.
    /// </summary>
    public partial class NetworkDataSync
    {
        public List<UInt16> charChangedUnits = new List<UInt16>();

        // Every N ticks server gathers units that have changed characteristics and send info to clients
        private void CharacteristicsChangeSend()
        {
            if (charChangedUnits.Count == 0) return;

            // Отсев сошедших с учёта — как у здоровья и маны: иначе номер уйдёт клиенту со значением 0
            // и тот напечатает «Desync!». Очистка и сброс флагов ниже идут в любом случае.
            for (int i = charChangedUnits.Count - 1; i >= 0; i--)
                if (!SlotManager.Instance.unitNetID.ContainsKey(charChangedUnits[i])) charChangedUnits.RemoveAt(i);

            if (charChangedUnits.Count > 0)
            {
                float[] attackSpeed = new float[charChangedUnits.Count];
                float[] maxHealth = new float[charChangedUnits.Count];

                for (int i = 0; i < charChangedUnits.Count; i++)
                {
                    if (SlotManager.Instance.unitNetID.TryGetValue(charChangedUnits[i], out Unit unit))
                    {
                        attackSpeed[i] = unit.attackSpeed;
                        maxHealth[i] = unit.maxHealth;
                    }
                }

                CharacteristicsChangeClientRpc(charChangedUnits.ToArray(), attackSpeed, maxHealth);
            }

            charChangedUnits.Clear();
            onCharCleared?.Invoke();
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void CharacteristicsChangeClientRpc(UInt16[] unitID, float[] unitAttackSpeed, float[] unitMaxHealth)
        {
            // Joining mid-game, we do not accept any data from the server. Only scene data.
            if (NetworkConnectionHandler.Instance.connectionStage == 2) return;

            for (int i = 0; i < unitID.Length; i++)
            {
                if (SlotManager.Instance.unitNetID.TryGetValue(unitID[i], out Unit unit))
                {
                    unit.SetAttackSpeed(unitAttackSpeed[i]);
                    unit.SetMaxHP(unitMaxHealth[i]);
                }
                else
                {
                    Debug.LogError("Desync! Unit netID:" + unitID[i] + " should exist on client, but does not! (CharacteristicsChangeSend NetworkDataSync)");
                }
            }
        }
    }
}
