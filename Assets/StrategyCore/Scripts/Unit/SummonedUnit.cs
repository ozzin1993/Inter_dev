using UnityEngine;

namespace StrategyCore
{
    // Маркер: юнит призван способностью «Призыв к Оружию» (временное ополчение).
    // Хранит рантайм-настройки призыва (задаёт MatchManager при спавне, не в Inspector — юнит создаётся в игре):
    //   • obeyCommands — слушается ли юнит общих приказов команды (Атака/Защита всей команде);
    //   • command      — если НЕ слушается, какую команду держать (Defence/Attack), фиксированно.
    // Если obeyCommands=false — юнит исключается из командной выборки MatchManager.GetGroupUnits.
    // Собственного поведения не несёт: время жизни — штатный LifetimeUnit, приказы — MatchManager.
    // Unit.cs не трогается (правило 1).
    public class SummonedUnit : MonoBehaviour
    {
        [HideInInspector] public bool obeyCommands;            // слушать ли общие приказы команды
        [HideInInspector] public BottomTableAction command;    // фиксированная команда, если общие не слушает
    }
}
