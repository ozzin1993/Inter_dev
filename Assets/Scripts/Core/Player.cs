using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StrategyCore
{
    public enum SlotType
    {
        Empty,
        Player,
        Bot,
        BotHidden
    }

    // Number of players, total. Can be changed. The last two belong to neutral computer. If total number is changed, do not forget to change Color array size.
    // NeutralPassive - can be used for controlling npcs that are friendly to players.
    // NeutralActive - can be used for controlling npcs that are hostile to players.

    public enum Players
    {
        Player1,
        Player2,
        Player3,
        Player4,
        Player5,
        Player6,
        Player7,
        Player8,
        Player9,
        Player10,
        Player11,
        Player12,
        NeutralPassive,
        NeutralActive
    }

    // Number of teams, total. Can be changed. The last two belong to neutral computer. If total number is changed, do not forget to change Color array size.
    public enum Teams
    {
        Team1,
        Team2,
        Team3,
        Team4,
        Team5,
        Team6,
        Team7,
        Team8,
        Team9,
        Team10,
        Team11,
        Team12,
        NeutralPassive,
        NeutralActive
    }

    [Serializable]
    public class TeamsAndPlayers
    {
        public string teamName;
        public SlotData[] players;

        // OVERRIDES NOT USED, BUT MIGHT COME USEFUL
        // Override Equals
        public override bool Equals(object obj)
        {
            if (obj is TeamsAndPlayers other)
            {
                // Compare teamName and players array
                return teamName == other.teamName &&
                       players.SequenceEqual(other.players);
            }

            return false;
        }

        // Override GetHashCode
        public override int GetHashCode()
        {
            // Combine hash codes of teamName and players array
            int hash = teamName?.GetHashCode() ?? 0;
            hash = hash * 31 + (players?.Aggregate(0, (acc, p) => acc * 31 + (p?.GetHashCode() ?? 0)) ?? 0);
            return hash;
        }

        // Overload == operator
        public static bool operator ==(TeamsAndPlayers left, TeamsAndPlayers right)
        {
            if (ReferenceEquals(left, null)) return ReferenceEquals(right, null);
            return left.Equals(right);
        }

        // Overload != operator
        public static bool operator !=(TeamsAndPlayers left, TeamsAndPlayers right)
        {
            return !(left == right);
        }
    }

    [Serializable]
    public class SlotData
    {
        [Tooltip("If this slot occupied by bot")]
        public bool isBot;
        [Tooltip("Define bot`s name")]
        public string botName;
        [Tooltip("Show this slot?")]
        public bool dontShow;

        // OVERRIDES NOT USED, BUT MIGHT COME USEFUL
        // Override Equals
        public override bool Equals(object obj)
        {
            if (obj is SlotData other)
            {
                return isBot == other.isBot &&
                       dontShow == other.dontShow &&
                       string.Equals(botName, other.botName, StringComparison.Ordinal);
            }

            return false;
        }

        // Override GetHashCode
        public override int GetHashCode()
        {
            return HashCode.Combine(isBot, dontShow, botName);
        }

        // Overload == operator
        public static bool operator ==(SlotData left, SlotData right)
        {
            if (ReferenceEquals(left, null)) return ReferenceEquals(right, null);
            return left.Equals(right);
        }

        // Overload != operator
        public static bool operator !=(SlotData left, SlotData right)
        {
            return !(left == right);
        }
    }

    [Serializable]
    public class FactionData
    {
        public string factionName;
        public UnitsForSpawn[] UnitsForSpawn;

        // [Interflow fix 2026-06-27] Объединение фракций: FactionData (имя + старт-юниты) + FactionConfig (контент).
        [Tooltip("Конфиг контента расы (волны, умения, техи, башни). MatchManager на старте берёт его по этой ссылке.")]
        public FactionConfig config;

        // Returns an array of factionNames
        public static string[] GetNames(GameManager gm)
        {
            string[] arr = new string[gm.factionData.Length];
            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = gm.factionData[i].factionName;
            }
            return arr;
        }
    }

    [Serializable]
    public class UnitsForSpawn
    {
        public int subSpawnIndex = 0;
        public int count = 1;
        public Unit unitToSpawn;
    }

    [Serializable]
    public class SpeficicUnitsDead
    {
        public UInt16[] unitIds;

        [Tooltip("Should the team that kills the specified units win")]
        public bool killerTeam;
        [Tooltip("If above is false, which team should win when the specified units are dead")]
        public int winningTeam;
    }
}
