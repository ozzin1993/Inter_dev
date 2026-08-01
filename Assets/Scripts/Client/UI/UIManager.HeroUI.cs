using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Партиал UIManager: UI героя — реакция на появление/смерть героя (перерисовка ряда его умений).
    /// Кнопка ручного призыва СНЕСЕНА 2026-07-24 (решение промта Волны 2.0): герой призывается только
    /// автоспавном с волной (MatchManager.TryAutoSpawnHero), бесплатно; после смерти — пауза heroWavesToSkip волн.
    /// Ассет StrategyCore не трогаем (правило 1).
    /// </summary>
    public partial class UIManager : MonoBehaviour
    {
        // Реакция на изменение героя (сервер): призван/погиб → перерисовать ряд умений героя.
        // Подписка/отписка — в UIManager.CornerTables (SubscribeCornerEvents / отписка при OnDestroy).
        void OnHeroChangedHandler(int team, Unit hero)
        {
            if (team != CommandTeamForLocalPlayer()) return;
            RefreshAbilitySlots();   // умения героя (нижний ряд) появляются/исчезают с призывом/смертью
        }
    }
}
