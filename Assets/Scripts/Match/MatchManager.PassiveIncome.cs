using System;
using System.Collections;
using UnityEngine;

namespace StrategyCore
{
    // Партиал MatchManager: пассивный доход золота (Фаза 2 плана «Улучшения ГЗ и техи»).
    //   • Раз в incomeInterval секунд каждой команде начисляется baseIncomePerTick + сумма бонусов
    //     разблокированных грейдов «Пассивный доход» (грейд = технология дерева ГЗ, читается из штатного TechTree).
    //   • Серверо-авторитетно (правило 6): корутина стартует только на сервере (из Start(), после isClient-гейта);
    //     начисление — штатным GameResources.ChangeAmount (decrease=false — добавить; calledByServer=true — синк клиентам).
    //   • Всё в Inspector (правило 3). Новый partial-файл — ассет StrategyCore не трогается (правило 1).

    // Грейд пассивного дохода: технология дерева ГЗ + прибавка золота за начисление при её разблокировке.
    [Serializable]
    public class IncomeGrade
    {
        [Tooltip("Технология грейда «Пассивный доход» (ScriptableObject из Resources/Technology). " +
                 "Разблокируется штатно в дереве ГЗ; когда разблокирована у команды — прибавка активна.")]
        public Technology tech;

        [Tooltip("Прибавка золота за одно начисление при разблокированном грейде. По дизайну: 1/3=+15, 2/3=+20, 3/3=+25.")]
        public int bonus;
    }

    public partial class MatchManager
    {
        [Header("Пассивный доход золота")]
        [SerializeField, Tooltip("Интервал начисления пассивного дохода, секунды. По дизайну — 15.")]
        float incomeInterval = 15f;

        [SerializeField, Tooltip("Базовый доход золота за одно начисление (ГЗ уровень 1, без грейдов). По дизайну — 150.")]
        int baseIncomePerTick = 150;

        [SerializeField, Tooltip("Грейды «Пассивный доход» (техи дерева ГЗ обеих фракций). Прибавки суммируются с базой " +
                                 "при разблокировке у команды. По дизайну три уровня: +15/+20/+25. Заполняется, когда созданы техи-ассеты (Фаза 1).")]
        IncomeGrade[] incomeGrades;

        /// <summary>
        /// Серверная корутина пассивного дохода. Ждёт старта матча (gameOn, как WaveLoop), затем каждые
        /// incomeInterval секунд начисляет доход обеим командам. Запускается из Start() (только сервер).
        /// </summary>
        IEnumerator PassiveIncomeLoop()
        {
            yield return new WaitUntil(() => SlotManager.Instance != null && SlotManager.Instance.gameOn);

            while (true)
            {
                yield return new WaitForSeconds(incomeInterval);
                GrantPassiveIncome(teamA);
                GrantPassiveIncome(teamB);
            }
        }

        /// <summary>
        /// Начислить пассивный доход одной команде: база + сумма бонусов разблокированных грейдов.
        /// Штатный GameResources.ChangeAmount: золото — Standard-ресурс, decrease=false добавляет;
        /// calledByServer=true — серверо-авторитетно + авто-синк клиентам.
        /// </summary>
        void GrantPassiveIncome(TeamWaveConfig cfg)
        {
            if (cfg == null || goldResource == null || GameResources.Instance == null) return;

            int amount = ComputeIncomeAmount(cfg.ownerPlayer);
            if (amount <= 0) return;

            GameResources.Instance.ChangeAmount(
                cfg.ownerPlayer, new ResourceWrapper(goldResource, amount), 1, false, true);
        }

        /// <summary>
        /// Итоговый доход за начисление для игрока: base + сумма бонусов грейдов, разблокированных в штатном TechTree.
        /// Грейды образуют цепочку (уровень L требует предыдущего — гейт corner-tables), поэтому простое суммирование даёт верный итог.
        /// Массив общий на обе фракции: игрок разблокирует только техи своей расы, чужие остаются false и дают 0.
        /// </summary>
        int ComputeIncomeAmount(int playerIndex)
        {
            int amount = baseIncomePerTick;
            if (incomeGrades == null || incomeGrades.Length == 0) return amount;

            TechnologyManager tm = TechnologyManager.Instance;
            if (tm == null || tm.TechTree == null || playerIndex < 0 || playerIndex >= tm.TechTree.Length)
                return amount;

            var tree = tm.TechTree[playerIndex];
            for (int i = 0; i < incomeGrades.Length; i++)
            {
                IncomeGrade g = incomeGrades[i];
                if (g == null || g.tech == null) continue;
                if (tree.ContainsKey(g.tech) && tree[g.tech]) amount += g.bonus;
            }
            return amount;
        }
    }
}
