namespace StrategyCore
{
    // ============================= ДОСЫЛКА СОСТОЯНИЯ МАТЧА (партиал MatchManager) ==
    // Б11 «возврат в матч после обрыва связи». Клиенту, вошедшему в середине матча (в том числе вернувшемуся
    // после обрыва), сервер шлёт слепок сцены — юниты, ресурсы, технологии (SaveManager, 4 блока). Состояние
    // САМОГО МАТЧА в слепок не входит: его каналы работают по правилу «изменилось — разослали», а вернувшийся
    // пропустил ровно эти изменения. Здесь собирается то, чего в слепке нет, и уходит адресно одному клиенту.
    //
    // Образец — ResendGroundZonesTo (MatchManager.GroundZones.cs): единственный уже существующий досыл
    // по тому же поводу; блоком Б11 обе досылки перенесены в момент, когда клиент ДОГРУЗИЛ слепок.
    //
    // Сюда СОЗНАТЕЛЬНО не входит:
    //   • таймер до следующей волны — WaveTimerLoop шлёт остаток всем раз в секунду, вернувшийся догоняет сам;
    //   • герой (уровень, опыт, очки, уровни умений) — лежит на самом юните и едет в слепке сцены;
    //   • заморозка состава (compositionLocked, waveSkipped, heroWavesToSkip) — поля [NonSerialized],
    //     читаются только на сервере, клиенту не нужны.
    public partial class MatchManager
    {
        /// <summary>
        /// Сервер: догнать одного клиента состоянием матча. Единственный вызыватель —
        /// NetworkDataSync.ResendMidGameState, из ClientFinishedLoadingSaveServerRpc: слепок сцены к тому моменту
        /// у клиента уже применён (раньше досылка приходила в пустую сцену и терялась).
        /// </summary>
        public void ResendMatchStateTo(ulong clientID)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (NetworkDataSync.Instance == null) return;

            for (int team = 0; team < 2; team++)
            {
                // Уровень и опыт главного здания: MainBuildingLevelSend шлётся при подъёме уровня,
                // а ExperienceSyncLoop — только при ИЗМЕНИВШЕМСЯ значении. Оба события уже прошли.
                NetworkDataSync.Instance.MainBuildingStateResendSend(clientID, team,
                                                                    MainBuildingLevel(team), TeamExperience(team));

                ResendWaveMarksTo(clientID, team);
                ResendTeamCommandsTo(clientID, team);
            }

            ResendPointTeamsTo(clientID);
        }

        // Пометки состава волны команды: рассылаются только по факту правки (BroadcastMarks).
        // Резерв золота разовых призывов клиенту не нужен — уходят одни идентификаторы типов, как в BroadcastMarks.
        void ResendWaveMarksTo(ulong clientID, int team)
        {
            TeamWaveConfig cfg = Team(team);
            if (cfg == null || cfg.autoSummon == null || cfg.oneShot == null) return;

            int[] autoIds = new int[cfg.autoSummon.Count];
            cfg.autoSummon.CopyTo(autoIds);
            int[] oneIds = new int[cfg.oneShot.Count];
            cfg.oneShot.Keys.CopyTo(oneIds, 0);
            NetworkDataSync.Instance.WaveMarksResendSend(clientID, team, autoIds, oneIds);
        }

        // Режимы рядов команды: стартовые рассылаются разово корутиной BroadcastStartCommandsLoop,
        // дальнейшие — по факту команды. Вернувшийся не получит ни тех, ни других.
        void ResendTeamCommandsTo(ulong clientID, int team)
        {
            if (currentCommand == null || team < 0 || team > 1) return;
            if (currentCommand[team] == null) return;

            for (int g = 0; g < CommandGroupCount; g++)
            {
                if (g >= currentCommand[team].Length) break;
                NetworkDataSync.Instance.TeamCommandResendSend(clientID, team, g, (int)currentCommand[team][g]);
            }
        }

        // Владельцы точек линии: PointTeamSend шлётся только на живой захват. Одним сообщением на всю линию —
        // индекс в массиве совпадает с индексом точки в Lane (тем же, что у PointTeamSend).
        void ResendPointTeamsTo(ulong clientID)
        {
            if (lane == null) return;
            int count = lane.PointCount;
            if (count <= 0) return;

            int[] teamByPoint = new int[count];
            for (int i = 0; i < count; i++) teamByPoint[i] = lane.PointTeam(i);
            NetworkDataSync.Instance.PointTeamsResendSend(clientID, teamByPoint);
        }
    }
}
