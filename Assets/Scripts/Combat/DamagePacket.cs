namespace StrategyCore
{
    /// <summary>
    /// Пакет урона — описание одного применяемого урона, адресованное юниту.
    /// Разбирает его приёмник <see cref="UnitReceiver"/> (схема «пакет и приёмник», §1, §3).
    ///
    /// Нулевой шаг схемы: пакет несёт РОВНО те же поля, что сегодня принимает <c>Unit.GetDamage</c>,
    /// одной записью урона. Ни одного нового поля здесь нет и быть не должно — переезд идёт
    /// отдельно от смены поведения (§12 схемы).
    ///
    /// Почему struct: пакет рождается не только на каст, но и на каждом шаге начисления состояний
    /// (<c>Effector.cs:69</c>, тик 0,1 с), в аурах и зонах — то есть в горячем пути. Значимый тип
    /// не даёт выделения в куче на каждый удар (руководство Unity, programming best practices).
    /// Это данные времени исполнения, в Inspector они не живут — <c>[SerializeField]</c> не нужен.
    /// </summary>
    public struct DamagePacket
    {
        /// <summary>Величина урона до всех расчётов (то же, что параметр <c>amount</c> у GetDamage).</summary>
        public float amount;

        /// <summary>Тип урона — по нему берётся множитель из таблицы «тип брони × тип урона».</summary>
        public DamageType damageType;

        /// <summary>Игрок, наносящий урон. Может быть −1 (нейтрал, безымянный источник).</summary>
        public int attackingPlayer;

        /// <summary>Юнит, наносящий урон. Может быть null (эффектор, зона на земле, отложенный урон).</summary>
        public Unit attackingUnit;

        /// <summary>
        /// Прямая ли это атака: удар, выполненный самим бьющим юнитом, а не умением или эффектором.
        /// Влияет на правила входящего урона и на анимацию получения удара.
        /// </summary>
        public bool directAttack;

        /// <summary>Собрать пакет из одной записи урона.</summary>
        public DamagePacket(float amount, DamageType damageType, int attackingPlayer, Unit attackingUnit, bool directAttack)
        {
            this.amount = amount;
            this.damageType = damageType;
            this.attackingPlayer = attackingPlayer;
            this.attackingUnit = attackingUnit;
            this.directAttack = directAttack;
        }
    }
}
