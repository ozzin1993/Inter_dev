using System.Collections;
using UnityEngine;

namespace StrategyCore
{
    // ============================= ОПЫТ И УРОВЕНЬ ГЛАВНОГО ЗДАНИЯ (партиал MatchManager) ==
    // Опыт команды и подъём уровня главного здания (ГЗ) по порогу. Целевая модель 2026-08-21: уровень ГЗ
    // набирается опытом за активные действия и открывает очередной тир дерева технологий; покупка узла тира
    // уровень больше НЕ поднимает (снято в MatchManager.TechTiers.cs).
    //
    // Единый вход начисления — AddExperience(team, amount, source): в него сходятся все четыре источника
    // (убийство, башня, владение центром, режим Атака). Образец единого входа с перечислением источников —
    // AddSouls (MatchManager.Souls.cs). Серверо-авторитетно (правило 6): весь счёт на сервере, клиенту
    // уходит готовое значение.
    //
    // Счётчик уровня НЕ дублируется (правило 5): партиал поднимает существующий mainBuildingLevel
    // из MatchManager.TechUpgrade.cs и повторяет порядок вызовов, которым уровень поднимала покупка узла
    // тира: инкремент → рассылка клиентам → событие подписчикам.
    public partial class MatchManager
    {
        /// <summary>Источник начисления опыта ГЗ (передаётся в единый вход; для диагностики).</summary>
        public enum ExperienceSource { Kill, Tower, CentrePoint, AttackStance }

        [Header("Опыт главного здания")]
        [SerializeField, Tooltip("Сколько опыта нужно на один уровень главного здания. Порог одинаков для всех " +
            "уровней; остаток сверх порога переносится в следующий уровень. Ориентир плана — 1000. " +
            "0 или меньше — уровень по опыту не растёт. [БАЛАНС — Влад]")]
        int experiencePerLevel = 1000;

        [SerializeField, Tooltip("Прибавка опыта команде за владение центральной точкой — за один период. " +
            "Ориентир плана — 50. Ноль или меньше — источник выключен. [БАЛАНС — Влад]")]
        int centreHoldExperience = 50;

        [SerializeField, Tooltip("Период начисления опыта за владение центральной точкой, секунды. " +
            "Ориентир плана — 10. Тикает бесконечно, пока точка принадлежит команде. [БАЛАНС — Влад]")]
        float centreHoldPeriod = 10f;

        [SerializeField, Tooltip("Прибавка опыта команде за каждую секунду в режиме «Атака». Засчитывается, " +
            "если в режиме Атака стоит ХОТЯ БЫ ОДИН ряд команд. Ориентир плана — 50. " +
            "Ноль или меньше — источник выключен. [БАЛАНС — Влад]")]
        int attackStanceExperiencePerSecond = 50;

        // Накопленный опыт команды В ТЕКУЩЕМ уровне (0=A, 1=B). Сервер — источник истины; на клиент значение
        // приходит готовым (ApplyTeamExperience). Вошедшему в середине матча опыт досылается вместе с уровнем ГЗ
        // (Б11: MatchManager.ResendMatchStateTo).
        readonly int[] teamExperience = new int[2];

        /// <summary>Накопленный опыт команды в текущем уровне ГЗ (0=A, 1=B).</summary>
        public int TeamExperience(int team) => (team == 0 || team == 1) ? teamExperience[team] : 0;

        /// <summary>Порог опыта на один уровень ГЗ (для показа игроку «опыт / порог»).</summary>
        public int ExperiencePerLevel => experiencePerLevel;

        // Последнее РАЗОСЛАННОЕ клиентам значение опыта команды: неизменившееся не шлём. −1 = ещё не слали.
        readonly int[] lastSentExperience = new int[2] { -1, -1 };

        /// <summary>Клиент: применить присланный сервером опыт команды (сервер — источник истины, правило 6).</summary>
        public void ApplyTeamExperience(int team, int experience)
        {
            if (team != 0 && team != 1) return;
            teamExperience[team] = experience;
            PushMainBuildingProgress(team);
        }

        // Раздать прогресс ГЗ в презентацию: уровень + опыт + порог. Образец — PushWaveTimer
        // (MatchManager.WaveTimer.cs). Чужую команду отсеивает сам интерфейс.
        void PushMainBuildingProgress(int team)
        {
            Presentation.UI?.ShowMainBuildingProgress(team, MainBuildingLevel(team),
                                                      TeamExperience(team), experiencePerLevel); // [Interflow 2026-08-01 ADR-005]
        }

        /// <summary>
        /// Серверные «часы» опыта: раз в секунду досылают клиентам изменившееся значение. Гранулярность —
        /// секунда, как у таймера волны (MatchManager.WaveTimer.cs): опыт капает от боя и таймеров, кадровая
        /// точность игроку не нужна, а рассылка каждого начисления заливала бы сеть (режим Атака тикает ежесекундно).
        /// Значение, не изменившееся с прошлой рассылки, не шлётся. Запускается из Start() (только сервер).
        /// Именно поэтому вошедший в середине матча сам по себе опыт не увидит: своей рассылки он не дождётся,
        /// пока значение не изменится — состояние ему досылается отдельно (Б11: MatchManager.ResendMatchStateTo).
        /// </summary>
        IEnumerator ExperienceSyncLoop()
        {
            yield return new WaitUntil(() => SlotManager.Instance != null && SlotManager.Instance.gameOn);

            WaitForSeconds wait = new WaitForSeconds(1f);
            while (true)
            {
                yield return wait;

                for (int team = 0; team < 2; team++)
                {
                    if (teamExperience[team] == lastSentExperience[team]) continue;
                    lastSentExperience[team] = teamExperience[team];
                    PushMainBuildingProgress(team);                  // хост — локально
                    if (NetworkDataSync.Instance != null)
                        NetworkDataSync.Instance.TeamExperienceSend(team, teamExperience[team]);   // клиенты — по сети
                }
            }
        }

        /// <summary>
        /// Единая точка начисления опыта команде (только сервер, правило 6). Сюда сходятся все источники.
        /// За одно начисление может закрыться несколько уровней подряд — поэтому цикл, а не одно сравнение;
        /// остаток сверх порога переносится в следующий уровень.
        /// Предел уровня — число тиров дерева команды (TechTierCount): по достижении предела уровень больше
        /// не растёт, а опыт продолжает копиться (решение Artsiom 2026-08-28). У фракции без дерева тиров ноль,
        /// поэтому уровень не поднимется никогда — это ожидаемо, а не сбой.
        /// </summary>
        public void AddExperience(int team, int amount, ExperienceSource source)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (team < 0 || team > 1) return;
            if (amount <= 0) return;

            teamExperience[team] += amount;

            if (experiencePerLevel <= 0) return;   // порог не задан — уровень по опыту не растёт

            int maxLevel = TechTierCount(team);
            while (teamExperience[team] >= experiencePerLevel && mainBuildingLevel[team] < maxLevel)
            {
                teamExperience[team] -= experiencePerLevel;   // остаток переносится в следующий уровень
                RaiseMainBuildingLevel(team);
            }
        }

        // ======================== ПЕРИОДИЧЕСКИЕ ИСТОЧНИКИ ========================

        /// <summary>
        /// Серверная корутина: опыт за владение центральной точкой. Ждёт старта матча (как WaveLoop),
        /// затем раз в centreHoldPeriod секунд начисляет владельцу центра. Тикает бесконечно, пока точка
        /// принадлежит команде. Запускается из Start() (только сервер).
        /// </summary>
        IEnumerator CentreHoldExperienceLoop()
        {
            yield return new WaitUntil(() => SlotManager.Instance != null && SlotManager.Instance.gameOn);

            while (true)
            {
                // Период читаем каждый раз: ноль и меньше означал бы ожидание в один кадр — держим секунду.
                yield return new WaitForSeconds(centreHoldPeriod > 0f ? centreHoldPeriod : 1f);
                if (centreHoldExperience <= 0) continue;             // источник выключен

                PointOfInterest centre = CentrePoint();
                if (centre == null) continue;                        // центральная точка не настроена

                // CurrentTeam — команда в терминах SlotManager.playerTeam; AddExperience ждёт индекс конфига.
                int team = TeamIndexOfOwner(FindPlayerByTeam(centre.CurrentTeam));
                if (team < 0) continue;                              // центр нейтрален — опыт никому
                AddExperience(team, centreHoldExperience, ExperienceSource.CentrePoint);
            }
        }

        // Центральная точка линии. Берётся из настроенных точек с башнями (rebuildablePoints) — это единственный
        // список точек, доступный менеджеру: Lane свои точки наружу не отдаёт. null — центр не настроен.
        PointOfInterest CentrePoint()
        {
            if (rebuildablePoints == null) return null;
            for (int i = 0; i < rebuildablePoints.Length; i++)
            {
                PointTowerConfig cfg = rebuildablePoints[i];
                if (cfg != null && cfg.point != null && cfg.point.type == PointOfInterest.PointType.Centre)
                    return cfg.point;
            }
            return null;
        }

        /// <summary>
        /// Серверная корутина: опыт за режим «Атака». Раз в секунду начисляет каждой команде, у которой хотя бы
        /// ОДИН ряд стоит в режиме Атака. Запускается из Start() (только сервер).
        /// </summary>
        IEnumerator AttackStanceExperienceLoop()
        {
            yield return new WaitUntil(() => SlotManager.Instance != null && SlotManager.Instance.gameOn);

            WaitForSeconds wait = new WaitForSeconds(1f);
            while (true)
            {
                yield return wait;
                if (attackStanceExperiencePerSecond <= 0) continue;  // источник выключен

                for (int team = 0; team < 2; team++)
                    if (HasAttackStance(team))
                        AddExperience(team, attackStanceExperiencePerSecond, ExperienceSource.AttackStance);
            }
        }

        // Стоит ли у команды хотя бы один ряд в режиме «Атака». Память режима — currentCommand[team][ряд]
        // (MatchManager.Commands.cs); готового хелпера «режим ряда» нет — есть только чтение по юниту
        // (CurrentCommandForUnit), поэтому обходим ряды здесь.
        bool HasAttackStance(int team)
        {
            if (currentCommand == null || team < 0 || team >= currentCommand.Length) return false;
            BottomTableAction[] rows = currentCommand[team];
            if (rows == null) return false;
            for (int g = 0; g < rows.Length; g++)
                if (rows[g] == BottomTableAction.Attack) return true;
            return false;
        }

        // Поднять уровень ГЗ команды на единицу тем же путём, каким его поднимала покупка узла уровня тира
        // (порядок оттуда же, MatchManager.TechTiers.cs): инкремент счётчика → рассылка клиентам → событие.
        // Порядок важен (правило 8): подписчики события (статы и облик замка, генерация Душ Нежити) читают
        // уровень через MainBuildingLevel и должны увидеть уже новое значение.
        void RaiseMainBuildingLevel(int team)
        {
            mainBuildingLevel[team]++;
            BroadcastMainBuildingLevel(team);
            OnMainBuildingLevelChanged?.Invoke(team);
        }
    }
}
