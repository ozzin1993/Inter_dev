namespace StrategyCore
{
    // [Interflow 2026-08-01 ADR-005] Перенесено из UIManager.BottomTables.cs: режим команды группы юнитов
    // используется симуляцией (MatchManager, DeathEffects, SummonedUnit), поэтому тип живёт в Game-сборке.
    public enum BottomTableAction
    {
        None,     // только подсветка
        Attack,   // команда «Атака» группе юнитов
        Defence,  // команда «Защита» группе юнитов
    }
}
